using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace ModelViewer.Rendering;

/// <summary>
/// Manages the Direct3D11 device, swap chain, render target view, and depth/stencil state.
/// </summary>
public class DeviceManager : IDisposable
{
    private readonly D3DRenderSurface _surface;
    private SharpDX.Direct3D11.RenderTargetView? _renderTargetView;
    private SharpDX.Direct3D11.DepthStencilState? _depthStencilState;
    private SharpDX.Direct3D11.DepthStencilView? _depthStencilView;
    private SharpDX.Direct3D11.Texture2D? _depthStencilTexture;
    private SharpDX.Direct3D11.RasterizerState? _rasterizerState;
    private bool _disposed;

    public SharpDX.Direct3D11.Device Device => _surface.Device!;
    public SwapChain SwapChain => _surface.SwapChain!;
    public SharpDX.Direct3D11.RenderTargetView? RenderTargetView => _renderTargetView;
    public SharpDX.Direct3D11.DepthStencilState? DepthStencilState => _depthStencilState;
    public SharpDX.Direct3D11.DepthStencilView? DepthStencilView => _depthStencilView;
    public SharpDX.Direct3D11.RasterizerState? RasterizerState => _rasterizerState;

    public DeviceManager(D3DRenderSurface surface)
    {
        _surface = surface;
        Initialize();
    }

    private void Initialize()
    {
        using var backBuffer = SwapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0);
        _renderTargetView = new SharpDX.Direct3D11.RenderTargetView(Device, backBuffer);

        var depthDesc = new SharpDX.Direct3D11.Texture2DDescription
        {
            Width = SwapChain.Description.ModeDescription.Width,
            Height = SwapChain.Description.ModeDescription.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D24_UNorm_S8_UInt,
            SampleDescription = new SampleDescription(1, 0),
            Usage = SharpDX.Direct3D11.ResourceUsage.Default,
            BindFlags = SharpDX.Direct3D11.BindFlags.DepthStencil,
            CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
            OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None,
        };
        _depthStencilTexture = new SharpDX.Direct3D11.Texture2D(Device, depthDesc);
        _depthStencilView = new SharpDX.Direct3D11.DepthStencilView(Device, _depthStencilTexture);

        var depthStateDesc = new SharpDX.Direct3D11.DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthComparison = SharpDX.Direct3D11.Comparison.Less,
            DepthWriteMask = DepthWriteMask.All,
            IsStencilEnabled = false,
        };
        _depthStencilState = new SharpDX.Direct3D11.DepthStencilState(Device, depthStateDesc);

        var rasterDesc = new SharpDX.Direct3D11.RasterizerStateDescription
        {
            FillMode = SharpDX.Direct3D11.FillMode.Solid,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
            IsFrontCounterClockwise = true,
            IsAntialiasedLineEnabled = false,
            IsDepthClipEnabled = true,
        };
        _rasterizerState = new SharpDX.Direct3D11.RasterizerState(Device, rasterDesc);
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
        using var backBuffer = SwapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0);
        _renderTargetView = new SharpDX.Direct3D11.RenderTargetView(Device, backBuffer);

        var depthDesc = new SharpDX.Direct3D11.Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0),
            Usage = SharpDX.Direct3D11.ResourceUsage.Default,
            BindFlags = SharpDX.Direct3D11.BindFlags.DepthStencil,
            CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
            OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None,
        };
        _depthStencilTexture = new SharpDX.Direct3D11.Texture2D(Device, depthDesc);
        _depthStencilView = new SharpDX.Direct3D11.DepthStencilView(Device, _depthStencilTexture);
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
