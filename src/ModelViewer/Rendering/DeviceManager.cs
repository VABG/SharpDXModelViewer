using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace ModelViewer.Rendering;

/// <summary>
/// Manages the Direct3D11 device, swap chain, render target view, and depth/stencil state.
/// </summary>
public class DeviceManager : IDisposable
{
    private readonly D3DRenderSurface _surface;
    private RenderTargetView? _renderTargetView;
    private DepthStencilState? _depthStencilState;
    private DepthStencilState? _stencilWriteState;
    private DepthStencilState? _stencilTestState;
    private DepthStencilView? _depthStencilView;
    private Texture2D? _depthStencilTexture;
    private ShaderResourceView? _depthStencilSrv; // SRV for stencil sampling in outline shader
    private SamplerState? _pointSampler;
    private RasterizerState? _rasterizerState;
    private BlendState? _alphaBlendState; // For overlay rendering

    private bool _disposed;

    public SharpDX.Direct3D11.Device Device => _surface.Device!;
    public SwapChain SwapChain => _surface.SwapChain!;
    public RenderTargetView? RenderTargetView => _renderTargetView;
    public DepthStencilState? DepthStencilState => _depthStencilState;
    public DepthStencilState? StencilWriteState => _stencilWriteState;
    public DepthStencilState? StencilTestState => _stencilTestState;
    public DepthStencilView? DepthStencilView => _depthStencilView;
    public ShaderResourceView? DepthStencilSrv => _depthStencilSrv;
    public SamplerState? PointSampler => _pointSampler;
    public RasterizerState? RasterizerState => _rasterizerState;
    public BlendState? AlphaBlendState => _alphaBlendState;

    public DeviceManager(D3DRenderSurface surface)
    {
        _surface = surface;
        Initialize();
    }

    private void Initialize()
    {
        CreateBackBufferResources(
            SwapChain.Description.ModeDescription.Width,
            SwapChain.Description.ModeDescription.Height);

        CreateDeviceIndependentStates();
    }

    /// <summary>
    /// Creates the render target view and depth/stencil resources for the given dimensions.
    /// Used by both Initialize and Resize.
    /// </summary>
    private void CreateBackBufferResources(int width, int height)
    {
        using var backBuffer = SwapChain.GetBackBuffer<Texture2D>(0);
        _renderTargetView = new RenderTargetView(Device, backBuffer);

        var depthDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R32G8X24_Typeless, // Typeless allows both DSV and SRV bindings
            SampleDescription = new SampleDescription(1, 0), // No MSAA — required for SRV binding
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        _depthStencilTexture = new Texture2D(Device, depthDesc);

        // ── Create DSV with typed depth/stencil format ──
        var dsvDesc = new DepthStencilViewDescription
        {
            Format = Format.D32_Float_S8X24_UInt,
            Dimension = DepthStencilViewDimension.Texture2D,
            Texture2D = { MipSlice = 0 },
        };
        _depthStencilView = new DepthStencilView(Device, _depthStencilTexture, dsvDesc);

        // ── Create SRV for stencil sampling (R32G8X24_UInt: .x=depth, .y=stencil) ──
        var srvDesc = new ShaderResourceViewDescription
        {
            Format = Format.R32_Float_X8X24_Typeless, // .x = depth (R32), .y = stencil (G8)
            Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
            Texture2D = { MostDetailedMip = 0, MipLevels = 1 },
        };
        _depthStencilSrv = new ShaderResourceView(Device, _depthStencilTexture, srvDesc);
    }

    /// <summary>
    /// Creates device-independent state objects that don't need to be recreated on resize.
    /// </summary>
    private void CreateDeviceIndependentStates()
    {
        // ── Point sampler for stencil sampling in outline shader ──
        var pointSamplerDesc = new SamplerStateDescription
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            BorderColor = new SharpDX.Color4(0, 0, 0, 0),
            ComparisonFunction = Comparison.Never,
        };
        _pointSampler = new SamplerState(Device, pointSamplerDesc);

        var depthStateDesc = new DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthComparison = Comparison.Less,
            DepthWriteMask = DepthWriteMask.All,
            IsStencilEnabled = false,
        };
        _depthStencilState = new DepthStencilState(Device, depthStateDesc);

        // ── Stencil-write state (used to stamp selected model into stencil buffer) ──
        // Writes stencil ref value on every fragment, so the selected model's silhouette
        // is recorded in the stencil buffer during its normal draw call.
        var stencilWriteDesc = new DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthComparison = Comparison.Less,
            DepthWriteMask = DepthWriteMask.All,

            IsStencilEnabled = true,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
            FrontFace = new DepthStencilOperationDescription
            {
                FailOperation = StencilOperation.Keep,
                DepthFailOperation = StencilOperation.Keep,
                PassOperation = StencilOperation.Replace,
                Comparison = Comparison.Always,
            },
            BackFace = new DepthStencilOperationDescription
            {
                FailOperation = StencilOperation.Keep,
                DepthFailOperation = StencilOperation.Keep,
                PassOperation = StencilOperation.Replace,
                Comparison = Comparison.Always,
            },
        };
        _stencilWriteState = new DepthStencilState(Device, stencilWriteDesc);

        // ── Stencil-test state (used for overlay pass) ──
        // Only passes fragments where stencil value == reference value.
        // Depth testing is disabled so the overlay always draws on top.
        // No stencil writes - we only read the existing stencil values.
        var stencilTestDesc = new DepthStencilStateDescription
        {
            IsDepthEnabled = false,
            DepthComparison = Comparison.Always,
            DepthWriteMask = DepthWriteMask.Zero,

            IsStencilEnabled = true,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0x00, // Read-only during overlay pass
            FrontFace = new DepthStencilOperationDescription
            {
                FailOperation = StencilOperation.Keep,
                DepthFailOperation = StencilOperation.Keep,
                PassOperation = StencilOperation.Keep,
                Comparison = Comparison.Equal, // Only pass where stencil == ref
            },
            BackFace = new DepthStencilOperationDescription
            {
                FailOperation = StencilOperation.Keep,
                DepthFailOperation = StencilOperation.Keep,
                PassOperation = StencilOperation.Keep,
                Comparison = Comparison.Equal,
            },
        };
        _stencilTestState = new DepthStencilState(Device, stencilTestDesc);

        var rasterDesc = new RasterizerStateDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.Back,
            IsFrontCounterClockwise = true,
            IsAntialiasedLineEnabled = false,
            IsDepthClipEnabled = true,
        };
        _rasterizerState = new RasterizerState(Device, rasterDesc);

        // ── Alpha blending state (used for stencil overlay pass) ──
        BlendStateDescription blendStateDescription = new BlendStateDescription
        {
            AlphaToCoverageEnable = false,
        };

        blendStateDescription.RenderTarget[0].IsBlendEnabled = true;
        blendStateDescription.RenderTarget[0].SourceBlend = BlendOption.SourceAlpha;
        blendStateDescription.RenderTarget[0].DestinationBlend = BlendOption.InverseSourceAlpha;
        blendStateDescription.RenderTarget[0].BlendOperation = BlendOperation.Add;
        blendStateDescription.RenderTarget[0].SourceAlphaBlend = BlendOption.Zero;
        blendStateDescription.RenderTarget[0].DestinationAlphaBlend = BlendOption.Zero;
        blendStateDescription.RenderTarget[0].AlphaBlendOperation = BlendOperation.Add;
        blendStateDescription.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;
        _alphaBlendState = new BlendState(Device, blendStateDescription);
    }

    /// <summary>
    /// Resizes the swap chain and all dependent resources.
    /// Must be called on the render thread with no resources bound.
    /// 
    /// The correct sequence is:
    ///   1. Flush the immediate context (drain any GPU commands referencing the old back buffer)
    ///   2. Dispose old RTV + depth stencil (release COM references to the old back buffer)
    ///   3. ResizeBuffers (now safe — no live references to the old back buffer)
    ///   4. Create new RTV + depth stencil from the new back buffer
    /// </summary>
    public void Resize(int width, int height)
    {
        var context = Device.ImmediateContext;

        // 1. Flush — ensure the GPU has finished all work referencing the old back buffer
        context.Flush();

        // 2. Dispose old resources that hold COM references to the old back buffer
        _renderTargetView?.Dispose();
        _renderTargetView = null;

        _depthStencilView?.Dispose();
        _depthStencilSrv?.Dispose();
        _depthStencilTexture?.Dispose();
        _depthStencilView = null;
        _depthStencilSrv = null;
        _depthStencilTexture = null;

        // 3. Resize the swap chain — safe now because no live references remain
        _surface.ResizeSwapChain(width, height);

        // 4. Create new resources from the resized back buffer
        using var backBuffer = SwapChain.GetBackBuffer<Texture2D>(0);
        _renderTargetView = new RenderTargetView(Device, backBuffer);

        var depthDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R24G8_Typeless, // Typeless allows both DSV and SRV bindings
            SampleDescription = new SampleDescription(1, 0), // No MSAA — required for SRV binding
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        _depthStencilTexture = new Texture2D(Device, depthDesc);

        // ── Create DSV with typed depth/stencil format ──
        var dsvDesc = new DepthStencilViewDescription
        {
            Format = Format.D24_UNorm_S8_UInt,
            Dimension = DepthStencilViewDimension.Texture2D,
            Texture2D = { MipSlice = 0 },
        };
        _depthStencilView = new DepthStencilView(Device, _depthStencilTexture, dsvDesc);

        // ── Create SRV for stencil sampling (R8_UInt reads the stencil channel) ──
        var srvDesc = new ShaderResourceViewDescription
        {
            Format = Format.R24_UNorm_X8_Typeless,
            Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
            Texture2D = { MostDetailedMip = 0, MipLevels = 1 },
        };
        _depthStencilSrv = new ShaderResourceView(Device, _depthStencilTexture, srvDesc);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _pointSampler?.Dispose();
        _alphaBlendState?.Dispose();
        _depthStencilSrv?.Dispose();
        _stencilWriteState?.Dispose();
        _stencilTestState?.Dispose();
        _depthStencilState?.Dispose();
        _depthStencilView?.Dispose();
        _depthStencilTexture?.Dispose();
        _renderTargetView?.Dispose();
        _rasterizerState?.Dispose();
        _disposed = true;
    }
}
