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
    private DepthStencilView? _depthStencilView;
    private Texture2D? _depthStencilTexture;
    private RasterizerState? _rasterizerState;
    private bool _disposed;

    public SharpDX.Direct3D11.Device Device => _surface.Device!;
    public SwapChain SwapChain => _surface.SwapChain!;
    public RenderTargetView? RenderTargetView => _renderTargetView;
    public DepthStencilState? DepthStencilState => _depthStencilState;
    public DepthStencilView? DepthStencilView => _depthStencilView;
    public RasterizerState? RasterizerState => _rasterizerState;

    public DeviceManager(D3DRenderSurface surface)
    {
        _surface = surface;
        Initialize();
    }

    private void Initialize()
    {
        using var backBuffer = SwapChain.GetBackBuffer<Texture2D>(0);
        _renderTargetView = new RenderTargetView(Device, backBuffer);

        var depthDesc = new Texture2DDescription
        {
            Width = SwapChain.Description.ModeDescription.Width,
            Height = SwapChain.Description.ModeDescription.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D24_UNorm_S8_UInt,
            SampleDescription = new SampleDescription(4, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        _depthStencilTexture = new Texture2D(Device, depthDesc);
        _depthStencilView = new DepthStencilView(Device, _depthStencilTexture);

        var depthStateDesc = new DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthComparison = Comparison.Less,
            DepthWriteMask = DepthWriteMask.All,
            IsStencilEnabled = false,
        };
        _depthStencilState = new DepthStencilState(Device, depthStateDesc);

        var rasterDesc = new RasterizerStateDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.Back,
            IsFrontCounterClockwise = true,
            IsAntialiasedLineEnabled = false,
            IsDepthClipEnabled = true,
        };
        _rasterizerState = new RasterizerState(Device, rasterDesc);
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
        _depthStencilTexture?.Dispose();
        _depthStencilView = null;
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
            Format = Format.D32_Float,
            SampleDescription = new SampleDescription(4, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };
        _depthStencilTexture = new Texture2D(Device, depthDesc);
        _depthStencilView = new DepthStencilView(Device, _depthStencilTexture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _rasterizerState?.Dispose();
        _depthStencilState?.Dispose();
        _depthStencilView?.Dispose();
        _depthStencilTexture?.Dispose();
        _renderTargetView?.Dispose();
        _disposed = true;
    }
}