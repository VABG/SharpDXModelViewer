using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.D3DCompiler;
using Buffer = SharpDX.Direct3D11.Buffer;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Device = SharpDX.Direct3D11.Device;

namespace ModelViewer.Rendering;

/// <summary>
/// Subpixel Morphological Antialiasing (SMAA 1x) post-processing pipeline.
///
/// The scene is drawn into an offscreen target and then resolved through three
/// fullscreen passes (see the reference 'SMAA.hlsl' / 'SMAA.cpp' pair):
///
///   1. Edge detection        →  edges RT  (.rg = horizontal / vertical edges)
///   2. Blend weight calc.    →  blend  RT  (.rg = horizontal weights,
///                                          .ba = vertical weights)
///   3. Neighborhood blending →  the swap chain back buffer
///
/// Renderer.cs drives the pipeline with PrepareSceneTargets() before the main
/// scene pass and Apply() after it.  When SMAA is disabled PrepareSceneTargets
/// returns false and the scene keeps rendering straight into the back buffer,
/// exactly as it did before this pass was added.
///
/// The three passes are thin HLSL wrappers around 'SMAA.hlsl'
/// (VertexShaderSMAA*.hlsl / PixelShaderSMAA*.hlsl).  The reference header
/// cannot infer SMAA_RT_METRICS on its own, so it is bound to the 'SMAAConstants'
/// cbuffer (slot b0) that this class uploads every frame.
/// </summary>
public class SMAARenderer : IDisposable
{
    private readonly Device _device;

    // ── Offscreen scene target (the image that gets resolved) ──
    private Texture2D? _sceneTexture;
    private RenderTargetView? _sceneRenderTargetView;
    private ShaderResourceView? _sceneSrv;

    // ── Edge detection target ──
    private Texture2D? _edgesTexture;
    private RenderTargetView? _edgesRenderTargetView;
    private ShaderResourceView? _edgesSrv;

    // ── Blend weight target ──
    private Texture2D? _blendTexture;
    private RenderTargetView? _blendRenderTargetView;
    private ShaderResourceView? _blendSrv;

    // ── SMAA lookup tables (DDS assets shipped in the Shaders folder) ──
    private Texture2D? _areaTexture;
    private ShaderResourceView? _areaSrv;
    private Texture2D? _searchTexture;
    private ShaderResourceView? _searchSrv;

    // ── Pass 1: edge detection ──
    private readonly VertexShader _edgeVertexShader;
    private readonly PixelShader _edgePixelShader;
    private readonly InputLayout _edgeInputLayout;

    // ── Pass 2: blending weight calculation ──
    private readonly VertexShader _weightsVertexShader;
    private readonly PixelShader _weightsPixelShader;
    private readonly InputLayout _weightsInputLayout;

    // ── Pass 3: neighborhood blending (resolve) ──
    private readonly VertexShader _resolveVertexShader;
    private readonly PixelShader _resolvePixelShader;
    private readonly InputLayout _resolveInputLayout;

    // ── Fullscreen quad (same geometry as the stencil overlay pass) ──
    private readonly Buffer _quadVertexBuffer;
    private readonly Buffer _quadIndexBuffer;
    private const int QuadIndexCount = 6; // 2 triangles, triangle list

    // ── Screen-space passes must not touch depth/stencil ──
    private readonly DepthStencilState _passthroughDepthState;

    // ── Screen-space passes must not be culled either ──
    // Renderer.cs leaves DeviceManager's rasterizer state bound for the whole
    // frame, and that state asks for back-face culling with a counter-clockwise
    // front face. The fullscreen quad below is counter-clockwise in NDC but
    // clockwise once the viewport flips Y for screen space, so under that state
    // both of its triangles are back-facing and get culled — which silently
    // kills all three SMAA draws. Culling is meaningless for a screen-covering
    // quad, so give the passes their own cull-free state.
    private readonly RasterizerState _quadRasterizerState;

    // ── SMAA_RT_METRICS (cbuffer b0, shared by all three passes) ──
    private readonly Buffer _constantsBuffer;

    private int _width;
    private int _height;
    private bool _enabled = true;
    private bool _disposed;

    /// <summary>
    /// Offscreen render target the main scene pass must draw into.
    /// Null when SMAA is disabled or the targets have not been allocated yet —
    /// in that case Renderer falls back to the back buffer.
    /// </summary>
    public RenderTargetView? SceneRenderTargetView => _sceneRenderTargetView;

    /// <summary>
    /// Whether the SMAA pipeline is active.
    /// </summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// Turns SMAA on or off. Call from the UI thread; takes effect next frame.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    /// <summary>
    /// Allocates the SMAA pipeline: shaders, fullscreen quad, lookup tables and
    /// the RT-metrics constant buffer.  Screen-sized targets are created lazily
    /// by PrepareSceneTargets() because the window starts out at 1x1.
    /// </summary>
    public SMAARenderer(Device device)
    {
        _device = device;

        // ── Compile the three passes (entry points follow project convention) ──
        var (edgeVs, edgeSignature) = ShaderCompiler.CompileVertexShader(
            device, ShaderCompiler.ResolveShaderPath("VertexShaderSMAAEdge.hlsl"));
        _edgeVertexShader = edgeVs;
        _edgePixelShader = ShaderCompiler.CompilePixelShader(
            device, ShaderCompiler.ResolveShaderPath("PixelShaderSMAAEdge.hlsl"));
        _edgeInputLayout = CreateQuadInputLayout(device, edgeSignature);
        edgeSignature.Dispose();

        var (weightsVs, weightsSignature) = ShaderCompiler.CompileVertexShader(
            device, ShaderCompiler.ResolveShaderPath("VertexShaderSMAAWeights.hlsl"));
        _weightsVertexShader = weightsVs;
        _weightsPixelShader = ShaderCompiler.CompilePixelShader(
            device, ShaderCompiler.ResolveShaderPath("PixelShaderSMAAWeights.hlsl"));
        _weightsInputLayout = CreateQuadInputLayout(device, weightsSignature);
        weightsSignature.Dispose();

        var (resolveVs, resolveSignature) = ShaderCompiler.CompileVertexShader(
            device, ShaderCompiler.ResolveShaderPath("VertexShaderSMAAResolve.hlsl"));
        _resolveVertexShader = resolveVs;
        _resolvePixelShader = ShaderCompiler.CompilePixelShader(
            device, ShaderCompiler.ResolveShaderPath("PixelShaderSMAAResolve.hlsl"));
        _resolveInputLayout = CreateQuadInputLayout(device, resolveSignature);
        resolveSignature.Dispose();
        // ── Fullscreen quad in NDC space (two triangles, same winding as the
        //    stencil overlay pass so the project's rasterizer state keeps them) ──
        var quadVertices = new[]
        {
            new Vector4(-1.0f, -1.0f, 0.0f, 1.0f), // 0: bottom-left
            new Vector4(-1.0f,  1.0f, 0.0f, 1.0f), // 1: top-left
            new Vector4( 1.0f,  1.0f, 0.0f, 1.0f), // 2: top-right
            new Vector4(-1.0f, -1.0f, 0.0f, 1.0f), // 3: bottom-left (duplicate)
            new Vector4( 1.0f,  1.0f, 0.0f, 1.0f), // 4: top-right (duplicate)
            new Vector4( 1.0f, -1.0f, 0.0f, 1.0f), // 5: bottom-right
        };

        var vbDesc = new BufferDescription
        {
            SizeInBytes = quadVertices.Length * Marshal.SizeOf<Vector4>(),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.VertexBuffer,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };

        var vbPtr = Marshal.AllocCoTaskMem(vbDesc.SizeInBytes);
        for (int i = 0; i < quadVertices.Length; i++)
            Marshal.StructureToPtr(quadVertices[i],
                IntPtr.Add(vbPtr, i * Marshal.SizeOf<Vector4>()), false);

        using var vbStream = new DataStream(vbPtr, vbDesc.SizeInBytes, false, false);
        _quadVertexBuffer = new Buffer(device, vbStream, vbDesc);
        Marshal.FreeCoTaskMem(vbPtr);

        var indices = new[] { 0, 2, 1, 3, 5, 4 };
        var ibDesc = new BufferDescription
        {
            SizeInBytes = indices.Length * sizeof(int),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.IndexBuffer,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        var ibPtr = Marshal.AllocCoTaskMem(ibDesc.SizeInBytes);
        for (int i = 0; i < indices.Length; i++)
            Marshal.WriteInt32(ibPtr, i * sizeof(int), indices[i]);

        using var ibStream = new DataStream(ibPtr, ibDesc.SizeInBytes, false, false);
        _quadIndexBuffer = new Buffer(device, ibStream, ibDesc);
        Marshal.FreeCoTaskMem(ibPtr);

        // ── SMAA lookup tables ──
        // AreaTexDX9.dds is 160x560 R8G8_UNORM and SearchTex.dds is 64x16 R8_UNORM.
        // Both DDS files already store their pixels in the DXGI-native layout, so
        // LoadDdsTexture only has to validate the header and skip 128 bytes.
        (_areaTexture, _areaSrv) = LoadDdsTexture(device,
            ShaderCompiler.ResolveShaderPath("AreaTexDX9.dds"), 160, 560, 2,
            Format.R8G8_UNorm);
        (_searchTexture, _searchSrv) = LoadDdsTexture(device,
            ShaderCompiler.ResolveShaderPath("SearchTex.dds"), 64, 16, 1,
            Format.R8_UNorm);

        // ── SMAA_RT_METRICS constant buffer (one float4 = 16 bytes, slot b0) ──
        var cbDesc = new BufferDescription
        {
            SizeInBytes = Marshal.SizeOf<Vector4>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        _constantsBuffer = new Buffer(device, cbDesc);

        // ── Depth/stencil state that leaves both untouched ──
        // The fullscreen quad sits at NDC z = 0 (window depth 0.5), which would
        // lose a Less depth test against the scene depth buffer — so depth has
        // to be explicitly disabled for all three SMAA passes.
        var passthroughDesc = new DepthStencilStateDescription
        {
            IsDepthEnabled = false,
            DepthWriteMask = DepthWriteMask.Zero,
            IsStencilEnabled = false,
            StencilWriteMask = 0,
        };
        _passthroughDepthState = new DepthStencilState(device, passthroughDesc);

        // ── Cull-free rasterizer state for the three fullscreen draws ──
        // See the _quadRasterizerState field comment: the inherited state culls
        // the quad away. Everything else stays at the project's defaults.
        var quadRasterDesc = new RasterizerStateDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            IsFrontCounterClockwise = true,
            IsAntialiasedLineEnabled = false,
            IsDepthClipEnabled = true,
        };
        _quadRasterizerState = new RasterizerState(device, quadRasterDesc);
    }

    /// <summary>
    /// Allocates (or re-allocates) the screen-sized targets for the given
    /// back-buffer size and reports whether SMAA should be used this frame.
    /// Call before the main scene pass, then read SceneRenderTargetView.
    /// </summary>
    public bool PrepareSceneTargets(int width, int height)
    {
        if (!_enabled || width <= 1 || height <= 1)
            return false;

        if (_sceneRenderTargetView != null && _width == width && _height == height)
            return true;

        ReleaseScreenTargets();
        CreateScreenTargets(width, height);
        return true;
    }

    /// <summary>
    /// Runs the three SMAA passes and writes the resolved image into the back
    /// buffer. Call after the main scene pass, before Present().
    /// </summary>
    public void Apply(DeviceContext context, RenderTargetView backBufferRenderTargetView)
    {
        if (_sceneRenderTargetView == null || backBufferRenderTargetView == null)
            return;

        UploadRtMetrics(context);

        // ── Shared state for all three fullscreen draws ──
        // No samplers are bound here: SMAA.hlsl declares its own inline sampler
        // states ("SamplerState LinearSampler { ... }" / "PointSampler", lines
        // 541-542 of the SMAA_HLSL_4 block), which the compiler bakes into the
        // shader bytecode.  App-bound sampler states would be redundant.
        context.Rasterizer.SetViewport(0, 0, _width, _height);
        context.Rasterizer.State = _quadRasterizerState;
        context.OutputMerger.SetBlendState(null, null, 0xFFFFFFFF);
        context.OutputMerger.SetDepthStencilState(_passthroughDepthState, 0);

        context.InputAssembler.PrimitiveTopology =
            SharpDX.Direct3D.PrimitiveTopology.TriangleList;
        var stride = Marshal.SizeOf<Vector4>();
        context.InputAssembler.SetVertexBuffers(0,
            new VertexBufferBinding(_quadVertexBuffer, stride, 0));
        context.InputAssembler.SetIndexBuffer(_quadIndexBuffer, Format.R32_UInt, 0);

        // ── Clear the intermediate targets ───────────────────────────────────
        // Both the edge-detection and the weights shaders discard pixels that
        // have no edge / no blend weight, so both targets must be reset to zero
        // at the start of every frame; otherwise last frame's values survive.
        context.ClearRenderTargetView(_edgesRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        context.ClearRenderTargetView(_blendRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));

        // ═══════════════════════════════════════════════════════════════════
        //  PASS 1 — edge detection (scene → edges RT, .rg = H/V edges)
        // ═══════════════════════════════════════════════════════════════════
        context.InputAssembler.InputLayout = _edgeInputLayout;
        context.VertexShader.Set(_edgeVertexShader);
        context.PixelShader.Set(_edgePixelShader);
        context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
        context.PixelShader.SetConstantBuffer(0, _constantsBuffer);

        // The scene render target view is still bound to the Output Merger when
        // the scene SRV is bound, and D3D rejects binding a resource as a
        // shader resource while it is also bound as a render target — the
        // binding is dropped and t0 stays null. Set the target first.
        context.OutputMerger.SetTargets(_edgesRenderTargetView);
        context.PixelShader.SetShaderResource(0, _sceneSrv);
        context.DrawIndexed(QuadIndexCount, 0, 0);

        // ═══════════════════════════════════════════════════════════════════
        //  PASS 2 — blending weights (edges + area + search → blend RT)
        //  .rg = horizontal weights, .ba = vertical weights
        // ═══════════════════════════════════════════════════════════════════
        context.InputAssembler.InputLayout = _weightsInputLayout;
        context.VertexShader.Set(_weightsVertexShader);
        context.PixelShader.Set(_weightsPixelShader);
        context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
        context.PixelShader.SetConstantBuffer(0, _constantsBuffer);

        // Set the target before binding _edgesSrv: it views the texture that is
        // still bound as the previous pass' render target.
        context.OutputMerger.SetTargets(_blendRenderTargetView);
        context.PixelShader.SetShaderResource(0, _edgesSrv);
        context.PixelShader.SetShaderResource(1, _areaSrv);
        context.PixelShader.SetShaderResource(2, _searchSrv);
        context.DrawIndexed(QuadIndexCount, 0, 0);

        // ═══════════════════════════════════════════════════════════════════
        //  PASS 3 — neighborhood blending (scene + blend → back buffer)
        // ═══════════════════════════════════════════════════════════════════
        context.InputAssembler.InputLayout = _resolveInputLayout;
        context.VertexShader.Set(_resolveVertexShader);
        context.PixelShader.Set(_resolvePixelShader);
        context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
        context.PixelShader.SetConstantBuffer(0, _constantsBuffer);

        // Set the target before binding _blendSrv: it views the texture that is
        // still bound as the previous pass' render target.
        context.OutputMerger.SetTargets(backBufferRenderTargetView);
        context.PixelShader.SetShaderResource(0, _sceneSrv);
        context.PixelShader.SetShaderResource(1, _blendSrv);
        // t2 is unused by the resolve shader — clear it so the previous pass'
        // search-table binding cannot be sampled by accident.
        context.PixelShader.SetShaderResource(2, null);
        context.DrawIndexed(QuadIndexCount, 0, 0);

        // ── TEMPORARY: verify the resolve orientation on a synthetic scene ──
        SmaaSyntheticCheck(context);
        SmaaAngleCheck(context);
    }

    /// <summary>
    /// TEMPORARY DIAGNOSTIC helper: appends a step marker so a failing D3D call
    /// can be identified from the last marker written.
    /// </summary>
    private static void DiagMark(string tag)
    {
        try
        {
            System.IO.File.AppendAllText(
                @"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\weightcheck.log",
                "STEP " + tag + "\n");
        }
        catch { }
    }

    // ── TEMPORARY DIAGNOSTIC: GPU-side SMAA check on a synthetic image ──
    // Runs once on the first Apply() call. Renders a hard-edged rectangle
    // through the three SMAA passes at the real screen size and reads every
    // intermediate back through one shared readback path that the self-tests
    // below prove.
    private bool _smaaDiagDone;

    /// <summary>
    /// Appends one diagnostic line immediately so a crash later in the check
    /// still leaves the earlier evidence on disk.
    /// </summary>
    private void Diag(string line)
    {
        try
        {
            System.IO.File.AppendAllText(
                @"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\weightcheck.log",
                line + "\r\n");
        }
        catch
        {
        }
    }

    private void DiagRaw(string block)
    {
        try
        {
            System.IO.File.AppendAllText(
                @"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\weightcheck.log",
                block);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Reads a whole R8G8B8A8 Texture2D back into packed little-endian uints.
    /// Every diagnostic number goes through this one path so a zero result
    /// cannot be a readback artefact.
    /// </summary>
    private int[] ReadBackRgba(DeviceContext context, Texture2D src, string label)
    {
        var d = src.Description;
        d.Usage = ResourceUsage.Staging;
        d.BindFlags = BindFlags.None;
        d.CpuAccessFlags = CpuAccessFlags.Read;
        d.SampleDescription = new SampleDescription(1, 0);
        var staging = new Texture2D(_device, d);
        try
        {
            Diag("stg " + label + " fmt=" + staging.Description.Format +
                " w=" + staging.Description.Width + " h=" + staging.Description.Height +
                " srcfmt=" + src.Description.Format);
            context.Flush();
            _device.ImmediateContext.CopyResource(src, staging);
            _device.ImmediateContext.Flush();
            _device.ImmediateContext.MapSubresource(
                staging, 0, MapMode.Read, MapFlags.None, out var mapped);
            var bpp = BytesOf(src.Description.Format);
            var words = (int)src.Description.Width * (int)src.Description.Height * bpp / 4;
            var pixels = new int[words];
            for (var i = 0; i < words; ++i)
                pixels[i] = Marshal.ReadInt32(mapped.DataPointer, i * 4);
            _device.ImmediateContext.UnmapSubresource(staging, 0);
            return pixels;
        }
        finally
        {
            staging.Dispose();
        }
    }

    private static int BytesOf(Format f)
    {
        if (f == Format.R8G8B8A8_UNorm) return 4;
        if (f == Format.R8G8_UNorm) return 2;
        if (f == Format.R8_UNorm) return 1;
        return 4;
    }

    private void SmaaSyntheticCheck(DeviceContext context)
    {
        if (_smaaDiagDone)
            return;
        _smaaDiagDone = true;
        var w = _width;
        var h = _height;
        Diag("BEGIN w=" + w + " h=" + h);
        try
        {
        // ── synthetic scene: a 45-degree edge, not a pixel-aligned box ──
        // A box whose borders sit exactly on pixel boundaries is SMAA's
        // degenerate case: the edge is already aligned, so the area table
        // legitimately returns ~zero weights along the straight runs (and
        // with SMAA_MAX_SEARCH_STEPS = 16 the searches never reach corners
        // 128 px away).  A diagonal edge forces non-zero weights at every
        // pixel along it, which is what actually exercises the lookup.
        var x0 = w / 4;
        var x1 = w - w / 4;
        var y0 = h / 4;
        var y1 = h - h / 4;
        var pattern = new int[w * h];
        for (var y = 0; y < h; ++y)
            for (var x = 0; x < w; ++x)
            {
                var diagonal = x + y < w;
                var box = x >= x0 && x < x1 && y >= y0 && y < y1;
                pattern[y * w + x] = (diagonal ^ box) ? 0x000000FF : 0x0000FF00;
            }

        var stageDesc = new Texture2DDescription
        {
            Width = w,
            Height = h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Write | CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None,
        };
        using var stScene = new Texture2D(_device, stageDesc);
        context.MapSubresource(stScene, 0, MapMode.Write, MapFlags.None, out var mappedScene);
        Marshal.Copy(pattern, 0, mappedScene.DataPointer, pattern.Length);
        context.UnmapSubresource(stScene, 0);

        // ── self-test A0: pure CPU staging round-trip (no GPU copy) ──
        var cpuOk = 0;
        context.MapSubresource(stScene, 0, MapMode.Read, MapFlags.None, out var check);
        for (var i = 0; i < pattern.Length; ++i)
            if (Marshal.ReadInt32(check.DataPointer, i * 4) == pattern[i])
                ++cpuOk;
        context.UnmapSubresource(stScene, 0);
        Diag("A0 cpu staging match=" + cpuOk + "/" + pattern.Length);

        // ── self-test A2: staging -> staging via CopyResource (isolates the copy) ──
        try
        {
            var stMatch = 0;
            using var stA = new Texture2D(_device, stageDesc);
            using var stB = new Texture2D(_device, stageDesc);
            _device.ImmediateContext.MapSubresource(
                stA, 0, MapMode.Write, MapFlags.None, out var wA);
            Marshal.Copy(pattern, 0, wA.DataPointer, pattern.Length);
            _device.ImmediateContext.UnmapSubresource(stA, 0);
            _device.ImmediateContext.CopyResource(stB, stA);
            _device.ImmediateContext.Flush();
            _device.ImmediateContext.MapSubresource(
                stB, 0, MapMode.Read, MapFlags.None, out var rB);
            for (var i = 0; i < pattern.Length; ++i)
                if (Marshal.ReadInt32(rB.DataPointer, i * 4) == pattern[i])
                    ++stMatch;
            _device.ImmediateContext.UnmapSubresource(stB, 0);
            Diag("A2 staging->staging match=" + stMatch + "/" + pattern.Length);
        }
        catch (Exception e)
        {
            Diag("A2 EXC " + e);
        }

        // ── self-test A3: reversed CopyResource arg order (proves the signature) ──
        try
        {
            var stMatch3 = 0;
            using var stC = new Texture2D(_device, stageDesc);
            using var stD = new Texture2D(_device, stageDesc);
            _device.ImmediateContext.MapSubresource(
                stC, 0, MapMode.Write, MapFlags.None, out var wC);
            Marshal.Copy(pattern, 0, wC.DataPointer, pattern.Length);
            _device.ImmediateContext.UnmapSubresource(stC, 0);
            _device.ImmediateContext.CopyResource(stC, stD);
            _device.ImmediateContext.Flush();
            _device.ImmediateContext.MapSubresource(
                stD, 0, MapMode.Read, MapFlags.None, out var rD);
            for (var i = 0; i < pattern.Length; ++i)
                if (Marshal.ReadInt32(rD.DataPointer, i * 4) == pattern[i])
                    ++stMatch3;
            _device.ImmediateContext.UnmapSubresource(stD, 0);
            Diag("A3 reversed-order match=" + stMatch3 + "/" + pattern.Length);
        }
        catch (Exception e)
        {
            Diag("A3 EXC " + e);
        }

        int[] scene = null;
        try
        {
            context.CopyResource(stScene, _sceneTexture);
            scene = ReadBackRgba(context, _sceneTexture, "scene");
        }
        catch (Exception e)
        {
            Diag("A EXC " + e);
        }
        var sceneOk = scene != null && scene.Length == pattern.Length;
        if (sceneOk)
            for (var i = 0; i < pattern.Length; ++i)
                if (scene[i] != pattern[i])
                {
                    sceneOk = false;
                    break;
                }
        Diag("A scene roundtrip=" + (sceneOk ? "ok" : "MISMATCH"));

        // ── self-test B: clearing an RT and reading it back yields red ──
        int[] probe = null;
        try
        {
            context.ClearRenderTargetView(
                _edgesRenderTargetView, new Color4(1.0f, 0.0f, 0.0f, 1.0f));
            probe = ReadBackRgba(context, _edgesTexture, "clear");
        }
        catch (Exception e)
        {
            Diag("B EXC " + e);
        }
        var probeRed = 0;
        if (probe != null)
            for (var i = 0; i < probe.Length; ++i)
                if ((probe[i] & 0xFF) != 0)
                    ++probeRed;
        Diag("B clear+read red=" + probeRed + "/" + (probe == null ? 0 : probe.Length));

        // ── self-test C: the DDS lookup tables reached the GPU ──
        int[] area = null;
        int[] search = null;
        try
        {
            area = ReadBackRgba(context, _areaTexture, "area");
        }
        catch (Exception e)
        {
            Diag("C1 EXC " + e);
        }
        try
        {
            search = ReadBackRgba(context, _searchTexture, "search");
        }
        catch (Exception e)
        {
            Diag("C2 EXC " + e);
        }
        var areaNz = 0;
        if (area != null)
            for (var i = 0; i < area.Length; ++i)
                if (area[i] != 0)
                    ++areaNz;
        var searchNz = 0;
        if (search != null)
            for (var i = 0; i < search.Length; ++i)
                if (search[i] != 0)
                    ++searchNz;
        Diag("C area nz=" + areaNz + "/" + (area == null ? 0 : area.Length) +
             " search nz=" + searchNz + "/" + (search == null ? 0 : search.Length));


        // ── run the three real SMAA passes on the synthetic scene ──
        // The resolve writes into a throw-away target so the real frame's
        // back buffer is untouched when the diagnostic returns.
        var diagDesc = new Texture2DDescription
        {
            Width = w,
            Height = h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        using var diagTex = new Texture2D(_device, diagDesc);
        using var diagRtv = new RenderTargetView(_device, diagTex);

        context.Rasterizer.SetViewport(0, 0, w, h);
        context.Rasterizer.State = _quadRasterizerState;
        context.OutputMerger.SetBlendState(null, null, 0xFFFFFFFF);
        context.OutputMerger.SetDepthStencilState(_passthroughDepthState, 0);

        context.InputAssembler.PrimitiveTopology =
            SharpDX.Direct3D.PrimitiveTopology.TriangleList;
        var stride = Marshal.SizeOf<Vector4>();
        context.InputAssembler.SetVertexBuffers(0,
            new VertexBufferBinding(_quadVertexBuffer, stride, 0));
        context.InputAssembler.SetIndexBuffer(_quadIndexBuffer, Format.R32_UInt, 0);

        context.ClearRenderTargetView(_edgesRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        context.ClearRenderTargetView(_blendRenderTargetView, new Color4(0.0f, 0.0f, 0.0f, 0.0f));

        context.InputAssembler.InputLayout = _edgeInputLayout;
        context.VertexShader.Set(_edgeVertexShader);
        context.PixelShader.Set(_edgePixelShader);
        context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
        context.PixelShader.SetConstantBuffer(0, _constantsBuffer);
        context.OutputMerger.SetTargets(_edgesRenderTargetView);
        context.PixelShader.SetShaderResource(0, _sceneSrv);
        UploadRtMetrics(context);
        context.DrawIndexed(QuadIndexCount, 0, 0);

        context.InputAssembler.InputLayout = _weightsInputLayout;
        context.VertexShader.Set(_weightsVertexShader);
        context.PixelShader.Set(_weightsPixelShader);
        context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
        context.PixelShader.SetConstantBuffer(0, _constantsBuffer);
        context.OutputMerger.SetTargets(_blendRenderTargetView);
        context.PixelShader.SetShaderResource(0, _edgesSrv);
        context.PixelShader.SetShaderResource(1, _areaSrv);
        context.PixelShader.SetShaderResource(2, _searchSrv);
        UploadRtMetrics(context);
        context.DrawIndexed(QuadIndexCount, 0, 0);

        context.InputAssembler.InputLayout = _resolveInputLayout;
        context.VertexShader.Set(_resolveVertexShader);
        context.PixelShader.Set(_resolvePixelShader);
        context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
        context.PixelShader.SetConstantBuffer(0, _constantsBuffer);
        context.OutputMerger.SetTargets(diagRtv);
        context.PixelShader.SetShaderResource(0, _sceneSrv);
        context.PixelShader.SetShaderResource(1, _blendSrv);
        UploadRtMetrics(context);
        context.DrawIndexed(QuadIndexCount, 0, 0);

        // ── read every intermediate back through the proven path ──
        int[] resolved = null;
        int[] blend = null;
        int[] edges = null;
        try
        {
            resolved = ReadBackRgba(context, diagTex, "resolved");
        }
        catch (Exception e)
        {
            Diag("D0 EXC " + e);
        }
        try
        {
            blend = ReadBackRgba(context, _blendTexture, "blend");
        }
        catch (Exception e)
        {
            Diag("D1 EXC " + e);
        }
        try
        {
            edges = ReadBackRgba(context, _edgesTexture, "edges");
        }
        catch (Exception e)
        {
            Diag("D2 EXC " + e);
        }

        var edgesNz = 0;
        if (edges != null)
            for (var i = 0; i < edges.Length; ++i)
                if (edges[i] != 0)
                    ++edgesNz;
        var blendNz = 0;
        if (blend != null)
            for (var i = 0; i < blend.Length; ++i)
                if (blend[i] != 0)
                    ++blendNz;
        var changed = 0;
        if (resolved != null && scene != null && resolved.Length == scene.Length)
            for (var i = 0; i < resolved.Length; ++i)
                if (resolved[i] != scene[i])
                    ++changed;
        Diag("D edges nz=" + edgesNz + "/" + (edges == null ? 0 : edges.Length) +
             " blend nz=" + blendNz + "/" + (blend == null ? 0 : blend.Length) +
             " changed=" + changed + "/" + (resolved == null ? 0 : resolved.Length));

        // ── channel-wise counts and ASCII maps so the failure pattern is
        // visible: SMAA stores horizontal edges in .x and vertical edges in
        // .y, and the weights pass writes four blend channels. ──
        var hEdges = 0;
        var vEdges = 0;
        var bL = 0;
        var bR = 0;
        var bT = 0;
        var bB = 0;
        for (var i = 0; i < w * h; ++i)
        {
            var e = edges == null ? 0 : edges[i];
            if ((e & 0xFF) != 0) ++hEdges;
            if (((e >> 8) & 0xFF) != 0) ++vEdges;
            var b = blend == null ? 0 : blend[i];
            if ((b & 0xFF) != 0) ++bL;
            if (((b >> 8) & 0xFF) != 0) ++bR;
            if (((b >> 16) & 0xFF) != 0) ++bT;
            if (((b >> 24) & 0xFF) != 0) ++bB;
        }
        Diag("chan edgesH=" + hEdges + " edgesV=" + vEdges +
             " blendL=" + bL + " blendR=" + bR + " blendT=" + bT +
             " blendB=" + bB);

        var row = new System.Text.StringBuilder();
        DiagRaw("MAP edges begin (H=edges.x V=edges.y .=zero)\n");
        for (var y = 0; y < h; ++y)
        {
            row.Clear();
            for (var x = 0; x < w; ++x)
            {
                var ee = edges == null ? 0 : edges[y * w + x];
                row.Append((ee & 0xFF) != 0 ? 'H' :
                    (((ee >> 8) & 0xFF) != 0 ? 'V' : '.'));
            }
            DiagRaw(row.ToString() + "\n");
        }
        DiagRaw("MAP edges end\n");

        DiagRaw("MAP blend begin (L=l . R=r T=t B=b .=zero)\n");
        for (var y = 0; y < h; ++y)
        {
            row.Clear();
            for (var x = 0; x < w; ++x)
            {
                var bb = blend == null ? 0 : blend[y * w + x];
                row.Append((bb & 0xFF) != 0 ? 'L' :
                    (((bb >> 8) & 0xFF) != 0 ? 'R' :
                    (((bb >> 16) & 0xFF) != 0 ? 'T' :
                    (((bb >> 24) & 0xFF) != 0 ? 'B' : '.'))));
            }
            DiagRaw(row.ToString() + "\n");
        }
        DiagRaw("MAP blend end\n");

        // ── numeric dump along the top border row and the left border column ──
        DiagRaw("NUM row y=64 x=56..200\n");
        for (var x = 56; x <= 200; ++x)
        {
            var bb = blend == null ? 0 : blend[64 * w + x];
            var ee2 = edges == null ? 0 : edges[64 * w + x];
            Diag("x=" + x + " b=" + (bb & 0xFF) + "," + ((bb >> 8) & 0xFF) +
                 "," + ((bb >> 16) & 0xFF) + "," + ((bb >> 24) & 0xFF) +
                 " e=" + (ee2 & 0xFF) + "," + ((ee2 >> 8) & 0xFF));
        }
        DiagRaw("NUM col x=64 y=56..200\n");
        for (var y = 56; y <= 200; ++y)
        {
            var bb = blend == null ? 0 : blend[y * w + 64];
            var ee2 = edges == null ? 0 : edges[y * w + 64];
            Diag("y=" + y + " b=" + (bb & 0xFF) + "," + ((bb >> 8) & 0xFF) +
                 "," + ((bb >> 16) & 0xFF) + "," + ((bb >> 24) & 0xFF) +
                 " e=" + (ee2 & 0xFF) + "," + ((ee2 >> 8) & 0xFF));
        }
        Diag("END");
        }
        catch (Exception e)
        {
            Diag("EXC " + e);
        }
    }

    // ── TEMPORARY DIAGNOSTIC: SMAA at several edge angles ────────────────────
    private bool _smaaAngleDiagDone;

    /// <summary>
    /// TEMPORARY DIAGNOSTIC: renders five rotated squares (edges at 15/30/45/60/
    /// 75 degrees) through the three real SMAA passes and reports, per angle, how
    /// many edge pixels received a blend weight.
    ///
    /// SMAA splits its work by edge orientation: a 45-degree diagonal is handled
    /// by SMAACalculateDiagWeights (which only ever touches texcoord and SMAA_RT_
    /// METRICS), while every other angle goes through the orthogonal searches
    /// (SMAASearchX*/Y*) plus the area-table lookup, which are the only consumers
    /// of offset[1]/offset[2]/pixcoord.  So "45 looks fine, other angles do not"
    /// isolates that second path, and the per-angle ratio below pins it down: a
    /// correct SMAA produces roughly one blend weight per edge pixel at every
    /// angle, so a collapsed ratio at some angle names the broken stage.
    /// </summary>
    private void SmaaAngleCheck(DeviceContext context)
    {
        if (_smaaAngleDiagDone)
            return;
        _smaaAngleDiagDone = true;
        var w = _width;
        var h = _height;
        Diag("ANGLE BEGIN w=" + w + " h=" + h);
        try
        {
            var angles = new double[] { 15.0, 30.0, 45.0, 60.0, 75.0 };
            var colW = w / 5;
            var cy = h / 2;
            var half = 16;

            // Five rotated squares, one per fifth of the width.  A square
            // rotated by theta has sides at theta and theta+90, so each
            // sample covers a shallow and a steep orientation at once.
            var pattern = new int[w * h];
            for (var i = 0; i < pattern.Length; ++i)
                pattern[i] = 0x00FF00;
            for (var s0 = 0; s0 < 5; ++s0)
            {
                var cx = s0 * colW + colW / 2;
                var th = angles[s0] * Math.PI / 180.0;
                var cs = Math.Cos(th);
                var sn = Math.Sin(th);
                for (var y = 0; y < h; ++y)
                    for (var x = 0; x < w; ++x)
                    {
                        var dx = (double)(x - cx);
                        var dy = (double)(y - cy);
                        var rx = dx * cs + dy * sn;
                        var ry = -dx * sn + dy * cs;
                        if (Math.Abs(rx) <= (double)half &&
                            Math.Abs(ry) <= (double)half)
                            pattern[y * w + x] = 0x0000FF;
                    }
            }

            var stageDesc = new Texture2DDescription
            {
                Width = w,
                Height = h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Write | CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None,
            };
            using var stScene = new Texture2D(_device, stageDesc);
            context.MapSubresource(
                stScene, 0, MapMode.Write, MapFlags.None, out var mappedScene);
            Marshal.Copy(pattern, 0, mappedScene.DataPointer, pattern.Length);
            context.UnmapSubresource(stScene, 0);
            context.CopyResource(stScene, _sceneTexture);
            context.Flush();
            // ── the three real SMAA passes ──
            context.Rasterizer.SetViewport(0, 0, w, h);
            context.Rasterizer.State = _quadRasterizerState;
            context.OutputMerger.SetBlendState(null, null, 0xFFFFFFFF);
            context.OutputMerger.SetDepthStencilState(_passthroughDepthState, 0);
            context.InputAssembler.PrimitiveTopology =
                SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            var stride = Marshal.SizeOf<Vector4>();
            context.InputAssembler.SetVertexBuffers(0,
                new VertexBufferBinding(_quadVertexBuffer, stride, 0));
            context.InputAssembler.SetIndexBuffer(
                _quadIndexBuffer, Format.R32_UInt, 0);

            context.ClearRenderTargetView(_edgesRenderTargetView,
                new Color4(0.0f, 0.0f, 0.0f, 0.0f));
            context.ClearRenderTargetView(_blendRenderTargetView,
                new Color4(0.0f, 0.0f, 0.0f, 0.0f));

            context.InputAssembler.InputLayout = _edgeInputLayout;
            context.VertexShader.Set(_edgeVertexShader);
            context.PixelShader.Set(_edgePixelShader);
            context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
            context.PixelShader.SetConstantBuffer(0, _constantsBuffer);
            context.OutputMerger.SetTargets(_edgesRenderTargetView);
            context.PixelShader.SetShaderResource(0, _sceneSrv);
            UploadRtMetrics(context);
            context.DrawIndexed(QuadIndexCount, 0, 0);

            context.InputAssembler.InputLayout = _weightsInputLayout;
            context.VertexShader.Set(_weightsVertexShader);
            context.PixelShader.Set(_weightsPixelShader);
            context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
            context.PixelShader.SetConstantBuffer(0, _constantsBuffer);
            context.OutputMerger.SetTargets(_blendRenderTargetView);
            context.PixelShader.SetShaderResource(0, _edgesSrv);
            context.PixelShader.SetShaderResource(1, _areaSrv);
            context.PixelShader.SetShaderResource(2, _searchSrv);
            UploadRtMetrics(context);
            context.DrawIndexed(QuadIndexCount, 0, 0);

            var diagDesc = new Texture2DDescription
            {
                Width = w,
                Height = h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
            };
            using var diagTex = new Texture2D(_device, diagDesc);
            using var diagRtv = new RenderTargetView(_device, diagTex);

            context.InputAssembler.InputLayout = _resolveInputLayout;
            context.VertexShader.Set(_resolveVertexShader);
            context.PixelShader.Set(_resolvePixelShader);
            context.VertexShader.SetConstantBuffer(0, _constantsBuffer);
            context.PixelShader.SetConstantBuffer(0, _constantsBuffer);
            context.OutputMerger.SetTargets(diagRtv);
            context.PixelShader.SetShaderResource(0, _sceneSrv);
            context.PixelShader.SetShaderResource(1, _blendSrv);
            UploadRtMetrics(context);
            context.DrawIndexed(QuadIndexCount, 0, 0);
            int[] angleEdges = null;
            int[] angleBlend = null;
            int[] angleResolved = null;
            try
            {
                angleEdges = ReadBackRgba(context, _edgesTexture, "angle-edges");
            }
            catch (Exception e)
            {
                Diag("ANGLE-E EXC " + e);
            }
            try
            {
                angleBlend = ReadBackRgba(context, _blendTexture, "angle-blend");
            }
            catch (Exception e)
            {
                Diag("ANGLE-B EXC " + e);
            }
            try
            {
                angleResolved = ReadBackRgba(context, diagTex, "angle-resolved");
            }
            catch (Exception e)
            {
                Diag("ANGLE-R EXC " + e);
            }

            // ── per-angle statistics ──
            // "pattern" is the CPU-side scene, so a resolve that leaves a pixel
            // untouched must reproduce it exactly.
            Diag("ANGLE counts (blend/edges should stay near 1.0):");
            for (var s1 = 0; s1 < 5; ++s1)
            {
                var x0 = s1 * colW;
                var x1 = s1 == 4 ? w : x0 + colW;
                var eNz = 0;
                var bNz = 0;
                var lN = 0;
                var rN = 0;
                var tN = 0;
                var bNm = 0;
                var chg = 0;
                for (var y = 0; y < h; ++y)
                    for (var x = x0; x < x1; ++x)
                    {
                        var idx = y * w + x;
                        var ew = angleEdges != null ? angleEdges[idx] : 0;
                        var bl = angleBlend != null ? angleBlend[idx] : 0;
                        if (ew != 0)
                            ++eNz;
                        if (bl != 0)
                            ++bNz;
                        if ((bl & 0xFF) != 0)
                            ++lN;
                        if (((bl >> 8) & 0xFF) != 0)
                            ++rN;
                        if (((bl >> 16) & 0xFF) != 0)
                            ++tN;
                        if (((bl >> 24) & 0xFF) != 0)
                            ++bNm;
                        if (angleResolved != null &&
                            angleResolved[idx] != pattern[idx])
                            ++chg;
                    }
                Diag("ang=" + angles[s1] + " edges=" + eNz +
                     " blend=" + bNz + " L=" + lN + " R=" + rN +
                     " T=" + tN + " B=" + bNm + " changed=" + chg);
            }

            // ── numeric walk across each square at its vertical middle ──
            Diag("ANGLE row y=" + cy + ":");
            for (var s2 = 0; s2 < 5; ++s2)
            {
                var mid = s2 * colW + colW / 2;
                for (var x = mid - half - 3; x <= mid + half + 3; ++x)
                {
                    var idx = cy * w + x;
                    var ew = angleEdges != null ? angleEdges[idx] : 0;
                    var bl = angleBlend != null ? angleBlend[idx] : 0;
                    Diag("s" + s2 + " x=" + x +
                         " e=" + (ew & 0xFF) + "," + ((ew >> 8) & 0xFF) +
                         " w=" + (bl & 0xFF) + "," + ((bl >> 8) & 0xFF) +
                         "," + ((bl >> 16) & 0xFF) + "," + ((bl >> 24) & 0xFF));
                }
            }

            // ── full boundary trace ──
            // Every pixel of each square's bounding box that the GPU marked as
            // an edge or gave a weight to.  Feeding this plus the CPU pattern
            // into a reference SMAA run pins down which stage disagrees.
            var reach = half + 12;
            Diag("ANGLE BOUNDARY s,x,y e=west,north w=l,r,t,b p=pattern:");
            for (var s3 = 0; s3 < 5; ++s3)
            {
                var cx3 = s3 * colW + colW / 2;
                for (var y2 = cy - reach; y2 <= cy + reach; ++y2)
                {
                    if (y2 < 0 || y2 >= h)
                        continue;
                    for (var x2 = cx3 - reach; x2 <= cx3 + reach; ++x2)
                    {
                        if (x2 < 0 || x2 >= w)
                            continue;
                        var idx2 = y2 * w + x2;
                        var ew2 = angleEdges != null ? angleEdges[idx2] : 0;
                        var bl2 = angleBlend != null ? angleBlend[idx2] : 0;
                        if (ew2 == 0 && bl2 == 0)
                            continue;
                        Diag(s3 + "," + x2 + "," + y2 +
                             " e=" + (ew2 & 0xFF) + "," + ((ew2 >> 8) & 0xFF) +
                             " w=" + (bl2 & 0xFF) + "," + ((bl2 >> 8) & 0xFF) +
                             "," + ((bl2 >> 16) & 0xFF) + "," + ((bl2 >> 24) & 0xFF) +
                             " p=" + (pattern[idx2] == 0x0000FF ? 1 : 0));
                    }
                }
            }
            Diag("ANGLE END");
        }
        catch (Exception e)
        {
            Diag("ANGLE EXC " + e);
        }
    }



    /// <summary>
    /// Creates the three screen-sized targets (scene, edges, blend weights).
    /// All are RGBA8: the reference implementation (SMAA.cpp) uses
    /// R8G8B8A8 for both intermediate targets and the scene copy, and the
    /// resolve/edge shaders only ever write .rg / .rgba of it.
    /// </summary>
    private void CreateScreenTargets(int width, int height)
    {
        _width = width;
        _height = height;

        _sceneTexture = CreateScreenTexture(Format.R8G8B8A8_UNorm);
        _sceneRenderTargetView = new RenderTargetView(_device, _sceneTexture);
        _sceneSrv = new ShaderResourceView(_device, _sceneTexture);

        _edgesTexture = CreateScreenTexture(Format.R8G8B8A8_UNorm);
        _edgesRenderTargetView = new RenderTargetView(_device, _edgesTexture);
        _edgesSrv = new ShaderResourceView(_device, _edgesTexture);

        _blendTexture = CreateScreenTexture(Format.R8G8B8A8_UNorm);
        _blendRenderTargetView = new RenderTargetView(_device, _blendTexture);
        _blendSrv = new ShaderResourceView(_device, _blendTexture);
    }

    /// <summary>
    /// One screen-sized RGBA8 texture usable as both RTV and SRV.
    /// </summary>
    private Texture2D CreateScreenTexture(Format dxgiFormat)
    {
        var desc = new Texture2DDescription
        {
            Width = _width,
            Height = _height,
            MipLevels = 1,
            ArraySize = 1,
            Format = dxgiFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        return new Texture2D(_device, desc);
    }

    /// <summary>
    /// Releases the screen-sized targets (views first — they reference the
    /// underlying textures). Used on resize and on Dispose().
    /// </summary>
    private void ReleaseScreenTargets()
    {
        _blendSrv?.Dispose();
        _blendRenderTargetView?.Dispose();
        _blendTexture?.Dispose();
        _blendSrv = null;
        _blendRenderTargetView = null;
        _blendTexture = null;

        _edgesSrv?.Dispose();
        _edgesRenderTargetView?.Dispose();
        _edgesTexture?.Dispose();
        _edgesSrv = null;
        _edgesRenderTargetView = null;
        _edgesTexture = null;

        _sceneSrv?.Dispose();
        _sceneRenderTargetView?.Dispose();
        _sceneTexture?.Dispose();
        _sceneSrv = null;
        _sceneRenderTargetView = null;
        _sceneTexture = null;
    }

    /// <summary>
    /// Uploads SMAA_RT_METRICS = (1/width, 1/height, width, height) to cbuffer
    /// b0. The reference header has no default for this macro, so it is fed in
    /// as a constant buffer bound to 'SMAARtMetrics' by every SMAA wrapper
    /// shader. Only 16 bytes — uploading per frame keeps the value correct
    /// across swap-chain resizes without extra bookkeeping.
    /// </summary>
    private void UploadRtMetrics(DeviceContext context)
    {
        var metrics = new Vector4(
            1.0f / _width,
            1.0f / _height,
            (float)_width,
            (float)_height);

        context.MapSubresource(_constantsBuffer, MapMode.WriteDiscard,
            MapFlags.None, out var data);
        Marshal.StructureToPtr(metrics, data.DataPointer, false);
        context.UnmapSubresource(_constantsBuffer, 0);
    }

    /// <summary>
    /// Input layout for the fullscreen quad: a single float4 POSITION, matching
    /// the SMAA wrapper vertex shaders.
    /// </summary>
    private static InputLayout CreateQuadInputLayout(
        Device device, ShaderSignature signature)
    {
        var elements = new[]
        {
            new InputElement("POSITION", 0,
                SharpDX.DXGI.Format.R32G32B32A32_Float, 0),
        };
        return new InputLayout(device, signature, elements);
    }

    /// <summary>
    /// Loads one of SMAA's two lookup tables from a .dds asset in the Shaders
    /// folder and uploads it as a Dynamic shader-resource texture.
    ///
    /// The assets are authored in DXGI-native form — AreaTexDX9.dds is R8G8_UNORM
    /// and SearchTex.dds is R8_UNORM — so no pixel decoding is required: the
    /// loader only validates the magic, works out the pixel block size and copies
    /// the payload verbatim.  Marshal.Copy has no byte[] overload, so the payload
    /// is repacked into little-endian 32-bit words (both payloads are a whole
    /// number of words).
    ///
    /// Expected dimensions are supplied by the caller and cross-checked against
    /// the header: these SMAA assets store height at @12 and width at @16.
    /// (Their DX9 format field at @72 and the DXGI format field at @124 are both
    /// zero, so the payload size is the only usable validation — it pins the
    /// bytes-per-texel, which is what distinguishes R8G8_UNORM from R8_UNORM.)
    /// </summary>
    private static (Texture2D, ShaderResourceView) LoadDdsTexture(
        Device device, string path, int expectedWidth, int expectedHeight,
        int bytesPerPixel, Format dxgiFormat)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"SMAA lookup texture not found: {path}");

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128)
            throw new InvalidOperationException(
                $"SMAA lookup texture '{path}' is truncated (header needs 128 bytes)");

        if (bytes[0] != 'D' || bytes[1] != 'D' || bytes[2] != 'S' || bytes[3] != ' ')
            throw new InvalidOperationException(
                $"SMAA lookup texture '{path}' is not a DDS file");

        // 124-byte DX9 header + 4-byte four-padded field = 128 bytes of header.
        var headerSize = 128;
        var payloadSize = bytes.Length - headerSize;

        if (payloadSize % 4 != 0)
            throw new InvalidOperationException(
                $"SMAA lookup texture '{path}' has a payload that is not a whole number of 32-bit words");

        var width = expectedWidth;
        var height = expectedHeight;

        var a = ReadUInt32(bytes, 12);
        var b = ReadUInt32(bytes, 16);
        if ((long)a * b * bytesPerPixel == payloadSize)
        {
            // Header agrees with the payload: @12 = height, @16 = width.
            height = a;
            width = b;
        }
        else if ((long)expectedWidth * expectedHeight * bytesPerPixel != payloadSize)
        {
            throw new InvalidOperationException(
                $"SMAA lookup texture '{path}': {payloadSize} payload bytes do not match " +
                $"{expectedWidth}x{expectedHeight} at {bytesPerPixel} bytes per texel");
        }

        var desc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = dxgiFormat,
            SampleDescription = new SampleDescription(1, 0),
            // Dynamic (not Default) so the payload can be mapped in once; the
            // tables never change after load.
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        var texture = new Texture2D(device, desc);

        // The payload starts after the 128-byte header: starting the copy at
        // byte 0 would upload the header itself as texels and shift the whole
        // image by 32 words (64 texels for R8G8, 128 for R8), which moves every
        // SMAAArea/SMAASearchLength lookup into a neighbouring sub-table.
        var words = new int[payloadSize / 4];
        for (int i = 0, j = headerSize; i < words.Length; i++, j += 4)
        {
            words[i] = bytes[j]
                | (bytes[j + 1] << 8)
                | (bytes[j + 2] << 16)
                | (bytes[j + 3] << 24);
        }

        var context = device.ImmediateContext;
        context.MapSubresource(texture, 0, MapMode.WriteDiscard, MapFlags.None,
            out var mapped);
        Marshal.Copy(words, 0, mapped.DataPointer, words.Length);
        context.UnmapSubresource(texture, 0);

        return (texture, new ShaderResourceView(device, texture));
    }

    /// <summary>
    /// Reads a little-endian unsigned 32-bit field out of a DDS header.
    /// </summary>
    private static int ReadUInt32(byte[] bytes, int offset)
    {
        return bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16)
            | (bytes[offset + 3] << 24);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseScreenTargets();

        _searchSrv?.Dispose();
        _searchTexture?.Dispose();
        _areaSrv?.Dispose();
        _areaTexture?.Dispose();

        _resolveInputLayout.Dispose();
        _resolvePixelShader.Dispose();
        _resolveVertexShader.Dispose();
        _weightsInputLayout.Dispose();
        _weightsPixelShader.Dispose();
        _weightsVertexShader.Dispose();
        _edgeInputLayout.Dispose();
        _edgePixelShader.Dispose();
        _edgeVertexShader.Dispose();

        _passthroughDepthState.Dispose();
        _quadRasterizerState.Dispose();
        _constantsBuffer.Dispose();
        _quadIndexBuffer.Dispose();
        _quadVertexBuffer.Dispose();
    }
}

