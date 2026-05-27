using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace ModelViewer.Rendering;

/// <summary>
/// Renders the selected model's silhouette into the stencil buffer using the
/// existing vertex and pixel shaders — no extra geometry pass is required.
///
/// Each frame:
///   1. Clear stencil to 0
///   2. Bind stencil-write depth-stencil state with a reference value (StencilId)
///   3. Draw the selected model once (normal shading + stencil stamp in one pass)
///   4. Restore normal depth-stencil state for remaining models
///   5. Draw a fullscreen overlay quad that reads the stencil buffer and
///      highlights pixels matching the StencilId.
///
/// This keeps Renderer.cs focused on orchestration while this class owns
/// the entire stencil-selection pipeline.
/// </summary>
public class StencilSelectionRenderer : IDisposable
{
    private readonly Device _device;
    private readonly DeviceManager _deviceManager;

    // ── Stencil ID written to the stencil buffer for the selected model ──
    private const byte StencilId = 0x42;

    // ── World matrix constant buffer (cbuffer b2, one 4×4 matrix = 64 bytes) ──
    private readonly Buffer _worldMatrixBuffer;

    // ── Overlay shaders (fullscreen quad that visualizes the stencil buffer) ──
    private readonly VertexShader _overlayVertexShader;
    private readonly PixelShader _overlayPixelShader;
    private readonly InputLayout _overlayInputLayout;

    // ── Fullscreen quad geometry (2 triangles covering the entire screen) ──
    private readonly Buffer _overlayVertexBuffer;
    private readonly Buffer _overlayIndexBuffer;
    private const int OverlayIndexCount = 6; // triangle strip → 2 triangles

    /// <summary>
    /// Initializes the stencil selection pipeline.
    /// </summary>
    /// <param name="device">The D3D11 device.</param>
    /// <param name="deviceManager">Provides stencil-write state, stencil SRV, point sampler.</param>
    public StencilSelectionRenderer(Device device, DeviceManager deviceManager)
    {
        _device = device;
        _deviceManager = deviceManager;

        // ── Create world matrix constant buffer (cbuffer b2) ──
        var worldCbDesc = new BufferDescription
        {
            SizeInBytes = Marshal.SizeOf<Matrix>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        _worldMatrixBuffer = new Buffer(device, worldCbDesc);

        // ── Compile overlay shaders ──
        var overlayVsPath = ShaderCompiler.ResolveShaderPath("VertexShaderStencilOverlay.hlsl");
        var overlayPsPath = ShaderCompiler.ResolveShaderPath("PixelShaderStencilOverlay.hlsl");

        var (overlayVs, overlaySignature) = ShaderCompiler.CompileVertexShader(device, overlayVsPath);
        _overlayVertexShader = overlayVs;

        _overlayPixelShader = ShaderCompiler.CompilePixelShader(device, overlayPsPath);

        // Simple input layout: position only (float4)
        var overlayElements = new[]
        {
            new InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32A32_Float, 0),
        };
        _overlayInputLayout = new InputLayout(device, overlaySignature, overlayElements);
        overlaySignature.Dispose();

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
        _overlayVertexBuffer = new Buffer(device, vbStream, vbDesc);
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
        _overlayIndexBuffer = new Buffer(device, ibStream, ibDesc);
        Marshal.FreeCoTaskMem(ibPtr);
    }

    /// <summary>
    /// Called before drawing the selected model. Clears the stencil buffer
    /// and configures the pipeline to stamp StencilId into the stencil
    /// for every fragment that passes the depth test.
    /// </summary>
    public void BeginSelectedModelPass(DeviceContext context)
    {
        // Clear stencil to 0 (leave depth untouched)
        if (_deviceManager.DepthStencilView != null)
        {
            context.ClearDepthStencilView(
                _deviceManager.DepthStencilView,
                DepthStencilClearFlags.Stencil, 0, 1);
        }

        // Switch to stencil-write depth-stencil state with reference value
        // (Still does depth testing + writing, but also writes StencilId
        //  to the stencil buffer on every pixel that passes.)
        if (_deviceManager.StencilWriteState != null)
        {
            context.OutputMerger.SetDepthStencilState(_deviceManager.StencilWriteState, StencilId);
        }
    }

    /// <summary>
    /// Called after drawing the selected model. Restores the normal
    /// depth-stencil state (depth-only, no stencil writes) so remaining
    /// models render normally.
    /// </summary>
    public void EndSelectedModelPass(DeviceContext context)
    {
        // Restore normal depth-only state
        if (_deviceManager.DepthStencilState != null)
        {
            context.OutputMerger.SetDepthStencilState(_deviceManager.DepthStencilState);
        }
    }

        /// <summary>
    /// Draws a fullscreen overlay quad that highlights pixels where the stencil
    /// buffer matches the selected model's stencil ID.
    ///
    /// Uses the hardware stencil test (Comparison.Equal) so only fragments
    /// matching StencilId pass through — the pixel shader just outputs a solid color.
    ///
    /// Call this just before Present(), after all scene geometry is drawn.
    /// </summary>
    public void DrawOverlay(DeviceContext context)
    {
        // Keep both RTV and DSV bound so the stencil test can read the stencil buffer
        if (_deviceManager.RenderTargetView != null && _deviceManager.DepthStencilView != null)
        {
            context.OutputMerger.SetTargets(_deviceManager.DepthStencilView, _deviceManager.RenderTargetView);
        }

        // Switch to stencil-test state: only pass fragments where stencil == StencilId
        if (_deviceManager.StencilTestState != null)
        {
            context.OutputMerger.SetDepthStencilState(_deviceManager.StencilTestState, StencilId);
        }

        // Enable alpha blending so the overlay composites over the scene
        if (_deviceManager.AlphaBlendState != null)
        {
            context.OutputMerger.SetBlendState(_deviceManager.AlphaBlendState, null, 0xFFFFFFFF);
        }

        // ── Set overlay shaders ──
        context.InputAssembler.InputLayout = _overlayInputLayout;
        context.VertexShader.Set(_overlayVertexShader);
        context.PixelShader.Set(_overlayPixelShader);

        // ── Bind fullscreen quad geometry ──
        var stride = Marshal.SizeOf<Vector4>();
        context.InputAssembler.SetVertexBuffers(0,
            new VertexBufferBinding(_overlayVertexBuffer, stride, 0));
        context.InputAssembler.SetIndexBuffer(_overlayIndexBuffer,
            Format.R32_UInt, 0);
        context.InputAssembler.PrimitiveTopology =
            SharpDX.Direct3D.PrimitiveTopology.TriangleList;

        // ── Draw overlay ──
        context.DrawIndexed(OverlayIndexCount, 0, 0);

        // Restore default blend state
        context.OutputMerger.SetBlendState(null, null, 0xFFFFFFFF);
    }

    /// <summary>
    /// Returns the stencil ID used for the selected model.
    /// </summary>
    public static byte StencilValue => StencilId;

    public void Dispose()
    {
        _worldMatrixBuffer.Dispose();
        _overlayVertexShader.Dispose();
        _overlayPixelShader.Dispose();
        _overlayInputLayout.Dispose();
        _overlayVertexBuffer.Dispose();
        _overlayIndexBuffer.Dispose();
    }
}