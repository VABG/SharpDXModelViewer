using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Device = SharpDX.Direct3D11.Device;

namespace ModelViewer.Rendering;

/// <summary>
/// FXAA (Fast Approximate Anti-Aliasing) post-processing pass.
/// 
/// Renders the scene to an offscreen texture, applies the FXAA shader,
/// then blits the result to the back buffer.  Fully self-contained like
/// ShadowRenderer — owns its own shaders, render target, and constant buffer.
/// </summary>
public class FxaaRenderer : IDisposable
{
    // ── FXAA shaders ──
    private readonly VertexShader _fxaaVertexShader;
    private readonly PixelShader _fxaaPixelShader;
    private readonly InputLayout _fxaaInputLayout;

    // ── FXAA constant buffer (cbuffer FxaaSettings : register(b0)) ──
    // Layout: float FxaaSubpix; float FxaaEdgeThresholdMin; float FxaaEdgeThresholdMax;
    //         float FxaaMaxIterations; float2 FxaaPixelThreshold; float4 _padding;
    // Total = 64 bytes (one complete register slot)
    private readonly Buffer _fxaaConstantBuffer;

    // ── Fullscreen quad geometry (2 triangles covering the entire screen) ──
    private readonly Buffer _fxaaQuadVertexBuffer;
    private readonly Buffer _fxaaQuadIndexBuffer;
    private const int FxaaQuadIndexCount = 6;

    // ── Offscreen render target (scene is rendered here first) ──
    private Texture2D? _fxaaTexture;
    private RenderTargetView? _fxaaRenderTargetView;
    private ShaderResourceView? _fxaaShaderResourceView;

    // ── Track current size to avoid unnecessary recreations ──
    private int _currentWidth;
    private int _currentHeight;

    // ── Bilinear sampler for scene texture sampling in FXAA shader ──
    private readonly SamplerState _bilinearSampler;

    // ── Blend state: no blending for the final blit ──
    private readonly BlendState _opaqueBlendState;

    // ── Toggle ──
    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Initializes the FXAA post-processing pipeline.
    /// </summary>
    public FxaaRenderer(Device device, int width, int height)
    {
        // ── Compile FXAA shaders ──
        var fxaaVsPath = ShaderCompiler.ResolveShaderPath("VertexShaderFxaa.hlsl");
        var fxaaPsPath = ShaderCompiler.ResolveShaderPath("PixelShaderFxaa.hlsl");

        var (fxaaVs, fxaaSignature) = ShaderCompiler.CompileVertexShader(device, fxaaVsPath);
        _fxaaVertexShader = fxaaVs;
        _fxaaPixelShader = ShaderCompiler.CompilePixelShader(device, fxaaPsPath);

        // ── Input layout: position only (float4) ──
        var fxaaElements = new[]
        {
            new InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32A32_Float, 0),
        };
        _fxaaInputLayout = new InputLayout(device, fxaaSignature, fxaaElements);
        fxaaSignature.Dispose();

        // ── Create fullscreen quad vertices (NDC space) ──
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
            Marshal.StructureToPtr(quadVertices[i], IntPtr.Add(vbPtr, i * Marshal.SizeOf<Vector4>()), false);

        using var vbStream = new DataStream(vbPtr, vbDesc.SizeInBytes, false, false);
        _fxaaQuadVertexBuffer = new Buffer(device, vbStream, vbDesc);
        Marshal.FreeCoTaskMem(vbPtr);

        // ── Create index buffer (triangle list: 0,1,2 and 3,5,4) ──
        // Both triangles must be CCW to match the rasterizer state
        // (IsFrontCounterClockwise = true, CullMode.Back)
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
        _fxaaQuadIndexBuffer = new Buffer(device, ibStream, ibDesc);
        Marshal.FreeCoTaskMem(ibPtr);
        
        // ── Create FXAA constant buffer (64 bytes) ──
        var cbDesc = new BufferDescription
        {
            SizeInBytes = 64,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        _fxaaConstantBuffer = new Buffer(device, cbDesc);

        // ── Create offscreen render target ──
        CreateFxaaRenderTarget(device, width, height);

        // ── Create bilinear sampler state ──
        var samplerDesc = new SamplerStateDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunction = Comparison.Never,
            BorderColor = SharpDX.Color.Black,
        };
        _bilinearSampler = new SamplerState(device, samplerDesc);

        // ── Opaque blend state (no blending for final blit) ──
        var blendDesc = new BlendStateDescription
        {
            AlphaToCoverageEnable = false,
        };
        blendDesc.RenderTarget[0].IsBlendEnabled = false;
        blendDesc.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;
        _opaqueBlendState = new BlendState(device, blendDesc);
    }

    /// <summary>
    /// Creates or recreates the offscreen render target texture and its views.
    /// </summary>
    private void CreateFxaaRenderTarget(Device device, int width, int height)
    {
        _currentWidth = width;
        _currentHeight = height;

        _fxaaRenderTargetView?.Dispose();
        _fxaaShaderResourceView?.Dispose();
        _fxaaTexture?.Dispose();

        var textureDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        _fxaaTexture = new Texture2D(device, textureDesc);

        _fxaaRenderTargetView = new RenderTargetView(device, _fxaaTexture);

        var srvDesc = new ShaderResourceViewDescription
        {
            Format = Format.R8G8B8A8_UNorm,
            Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
            Texture2D = { MostDetailedMip = 0, MipLevels = 1 },
        };
        _fxaaShaderResourceView = new ShaderResourceView(device, _fxaaTexture, srvDesc);
    }

    /// <summary>
    /// Called when the window is resized. Recreates the offscreen render target only if dimensions changed.
    /// </summary>
    public void Resize(Device device, int width, int height)
    {
        if (width == _currentWidth && height == _currentHeight)
            return;

        CreateFxaaRenderTarget(device, width, height);
    }

    /// <summary>
    /// Returns the FXAA offscreen render target view. 
    /// The main scene should render to this instead of the back buffer when FXAA is enabled.
    /// </summary>
    public RenderTargetView? FxaaRenderTargetView => _fxaaRenderTargetView;

    /// <summary>
    /// Uploads FXAA settings to the GPU constant buffer.
    /// </summary>
    private void UploadFxaaSettings(DeviceContext context, int width, int height)
    {
        // Pack the constant buffer manually:
        // Offset 0:  float FxaaSubpix           (4 bytes)
        // Offset 4:  float FxaaEdgeThresholdMin (4 bytes)
        // Offset 8:  float FxaaEdgeThresholdMax (4 bytes)
        // Offset 12: float FxaaMaxIterations    (4 bytes)
        // Offset 16: float2 FxaaPixelThreshold  (8 bytes)  = 1.0 / (width, height)
        // Offset 24: float4 _padding            (16 bytes)
        // Total: 40 bytes of data, buffer is 64 bytes (aligned to register slot)

        float subpix = 0.25f;
        float edgeThresholdMin = 0.03125f;
        float edgeThresholdMax = 0.0625f;
        float maxIterations = 12.0f;
        float invWidth = 1.0f / width;
        float invHeight = 1.0f / height;

        context.MapSubresource(_fxaaConstantBuffer, MapMode.WriteDiscard,
            MapFlags.None, out var map);

        // Write floats at known offsets using Marshal
        Marshal.StructureToPtr(subpix, map.DataPointer, false);
        Marshal.StructureToPtr(edgeThresholdMin, IntPtr.Add(map.DataPointer, 4), false);
        Marshal.StructureToPtr(edgeThresholdMax, IntPtr.Add(map.DataPointer, 8), false);
        Marshal.StructureToPtr(maxIterations, IntPtr.Add(map.DataPointer, 12), false);
        Marshal.StructureToPtr(invWidth, IntPtr.Add(map.DataPointer, 16), false);
        Marshal.StructureToPtr(invHeight, IntPtr.Add(map.DataPointer, 20), false);
        // Remaining bytes are zeroed by WriteDiscard

        context.UnmapSubresource(_fxaaConstantBuffer, 0);
    }

    /// <summary>
    /// Renders the FXAA fullscreen quad.
    /// 
    /// Expects:
    ///   - Scene already rendered to _fxaaTexture
    ///   - Output merger targets set to the BACK buffer RTV + DSV
    /// 
    /// This binds the FXAA shaders, uploads settings, binds the scene texture,
    /// and draws a fullscreen quad.
    /// </summary>
    public void RenderFxaaPass(DeviceContext context, int width, int height)
    {
        if (!_enabled) return;
        if (_fxaaShaderResourceView == null) return;

        // Upload FXAA settings
        UploadFxaaSettings(context, width, height);

        // Set FXAA input layout and topology
        context.InputAssembler.InputLayout = _fxaaInputLayout;
        context.InputAssembler.PrimitiveTopology =
            SharpDX.Direct3D.PrimitiveTopology.TriangleList;

        // Bind fullscreen quad geometry
        var stride = Marshal.SizeOf<Vector4>();
        context.InputAssembler.SetVertexBuffers(0,
            new VertexBufferBinding(_fxaaQuadVertexBuffer, stride, 0));
        context.InputAssembler.SetIndexBuffer(_fxaaQuadIndexBuffer,
            Format.R32_UInt, 0);

        // Set FXAA pipeline state
        context.VertexShader.Set(_fxaaVertexShader);
        context.PixelShader.Set(_fxaaPixelShader);
        context.VertexShader.SetConstantBuffer(0, _fxaaConstantBuffer);
        context.PixelShader.SetConstantBuffer(0, _fxaaConstantBuffer);

        // Bind the scene texture at t0
        context.PixelShader.SetShaderResource(0, _fxaaShaderResourceView);

        // Bind bilinear sampler at s0
        context.PixelShader.SetSampler(0, _bilinearSampler);

        // Disable depth testing and writing for post-process
        context.OutputMerger.SetDepthStencilState(null);

        // Set opaque blending (no alpha blending for the blit)
        context.OutputMerger.SetBlendState(_opaqueBlendState, null, 0xFFFFFFFF);

        // Draw fullscreen quad
        context.DrawIndexed(FxaaQuadIndexCount, 0, 0);

        // Clean up: unbind scene texture
        context.PixelShader.SetShaderResource(0, null);
    }

    public void Dispose()
    {
        _fxaaVertexShader.Dispose();
        _fxaaPixelShader.Dispose();
        _fxaaInputLayout.Dispose();
        _fxaaConstantBuffer.Dispose();
        _fxaaQuadVertexBuffer.Dispose();
        _fxaaQuadIndexBuffer.Dispose();
        _fxaaRenderTargetView?.Dispose();
        _fxaaShaderResourceView?.Dispose();
        _fxaaTexture?.Dispose();
        _bilinearSampler.Dispose();
        _opaqueBlendState.Dispose();
    }
}
