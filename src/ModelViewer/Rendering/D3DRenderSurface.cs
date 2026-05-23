using System.Windows.Interop;
using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.DXGI;
using Point = System.Windows.Point;

namespace ModelViewer.Rendering;

/// <summary>
/// WPF HwndHost that provides a rendering surface for SharpDX/Direct3D11.
/// This is the critical bridge between WPF and DirectX - it creates a native window
/// handle that D3D can render to, embedded within the WPF visual tree.
///
/// Mouse input: the child HWND created in BuildWindowCore intercepts all Win32
/// mouse messages, preventing them from reaching WPF's input system automatically.
/// We install a custom WNDPROC via SetWindowLongPtr to translate those messages
/// into WPF routed events so that the InputHandler (and any other WPF input listeners)
/// receive them correctly.
/// </summary>
public class D3DRenderSurface : HwndHost
{
    private readonly TaskCompletionSource<bool> _readySource = new();
    private SwapChain? _swapChain;
    private SharpDX.Direct3D11.Device? _device;
    private IntPtr _hwnd = IntPtr.Zero;
    private int _lastWidth = 0;
    private int _lastHeight = 0;

    // Pending resize dimensions — set by ArrangeOverride (UI thread), consumed by the render thread.
    private int _pendingWidth = 0;
    private int _pendingHeight = 0;
    private bool _pendingResize = false;
    
    // Store the original window procedure so we can chain to it
    private IntPtr _originalWndProc;
    private Format _rendertargetFormat = Format.R8G8B8A8_UNorm;

    // Static lookup: HWND -> D3DRenderSurface instance (for the WNDPROC callback)
    private static readonly Dictionary<IntPtr, D3DRenderSurface> _instances = new();
    private static readonly object _instancesLock = new();

    // Keep a rooted reference to the delegate to prevent GC
    private static readonly WndProcDelegate _wndProcDelegate = WndProc;

    /// <summary>
    /// Last known mouse position relative to the surface, updated by the WNDPROC on every
    /// mouse message.  Read by InputHandler instead of calling e.GetPosition() which
    /// returns stale values for manually-raised HwndHost events.
    /// </summary>
    public Point LastMousePosition { get; private set; } = new Point(0,0);

    /// <summary>
    /// Extracts the mouse position (X, Y) from the lParam of a Win32 mouse message.
    /// Low-order word = X, high-order word = Y.
    /// </summary>
    private static Point ExtractMousePosition(IntPtr lParam)
    {
        long raw = lParam.ToInt64();
        int x = (int)(raw & 0xFFFF);           // low-order word
        int y = (int)((raw >> 16) & 0xFFFF);   // high-order word
        return new Point(x, y);
    }

    /// <summary>
    /// Gets the Direct3D11 device created for this surface.
    /// </summary>
    public SharpDX.Direct3D11.Device? Device => _device;

    /// <summary>
    /// Gets the swap chain for presenting frames.
    /// </summary>
    public SwapChain? SwapChain => _swapChain;

    /// <summary>
    /// Gets a task that completes when the surface is ready for rendering.
    /// </summary>
    public Task ReadyAsync() => _readySource.Task;

    /// <summary>
    /// Returns true if a resize is pending on the render thread.
    /// </summary>
    public bool HasPendingResize => _pendingResize;

    /// <summary>
    /// Returns the pending resize dimensions and clears the flag.
    /// Call this from the render thread to consume the resize request.
    /// </summary>
    public (int width, int height) ConsumePendingResize()
    {
        var width = _pendingWidth;
        var height = _pendingHeight;
        _pendingResize = false;
        _pendingWidth = 0;
        _pendingHeight = 0;
        return (width, height);
    }

    #region Win32 P/Invoke for HWND creation and message handling

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

    // Win32 message IDs for mouse input
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSEWHEEL = 0x020A;

    // Mouse key flags in wParam
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;
    private const int MK_MBUTTON = 0x0010;

    // SetWindowLongPtr index for replacing the window procedure
    private const int GWLP_WNDPROC = -4;

    /// <summary>
    /// Win32 WNDPROC delegate signature.
    /// </summary>
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpszClass, string? lpszName,
        int dwStyle, int x, int y, int cx, int cy, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    #endregion
    
    public D3DRenderSurface()
    {
    }

    /// <summary>
    /// Builds the native window handle and initializes D3D device/swap chain.
    /// After the HWND is created, we install a custom WNDPROC to intercept mouse
    /// messages and raise them as WPF routed events.
    /// </summary>
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(0, "STATIC", null,
            (int)(WS_CHILD | WS_CLIPCHILDREN | WS_VISIBLE),
            0, 0, 256, 256,
            hwndParent.Handle, IntPtr.Zero, GetModuleHandle(null));

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create rendering window handle.");

        // Register this instance so our static WNDPROC can find it
        lock (_instancesLock)
        {
            _instances[_hwnd] = this;
        }

        // Install our custom WNDPROC to intercept mouse messages.
        // This is the critical fix: HwndHost's child HWND consumes all mouse input
        // in its own window procedure. Without hooking it, WPF routed events never fire.
        _originalWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _wndProcDelegate);

        try
        {
            var deviceFlags = SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport;

            var swapChainDesc = new SwapChainDescription
            {
                BufferCount = 1,
                Usage = Usage.RenderTargetOutput,
                IsWindowed = true,
                SwapEffect = SwapEffect.Discard,
                OutputHandle = _hwnd,
                ModeDescription = new ModeDescription(0, 0, new Rational(60, 1), _rendertargetFormat),
                SampleDescription = new SampleDescription(4, 0),
                Flags = SwapChainFlags.None,
            };

            SharpDX.Direct3D11.Device.CreateWithSwapChain(
                DriverType.Hardware, deviceFlags, swapChainDesc,
                out _device, out _swapChain);

            _readySource.SetResult(true);
        }
        catch (SharpDXException ex)
        {
            _readySource.SetException(ex);
            System.Diagnostics.Debug.WriteLine($"D3D Initialization failed: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _readySource.SetException(ex);
            throw;
        }

        return new HandleRef(this, _hwnd);
    }

    /// <summary>
    /// Override to size the child HWND and queue a swap-chain resize for the render thread.
    /// Without this, the render surface stays at 1x1 pixels.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        int width = (int)Math.Max(1, finalSize.Width);
        int height = (int)Math.Max(1, finalSize.Height);

        if (_hwnd != IntPtr.Zero)
        {
            MoveWindow(_hwnd, 0, 0, width, height, true);
        }

        if (width != _lastWidth || height != _lastHeight)
        {
            _lastWidth = width;
            _lastHeight = height;

            // Queue the resize for the render thread — never call ResizeBuffers on
            // the UI thread while the render thread may have the back buffer bound.
            _pendingWidth = width;
            _pendingHeight = height;
            _pendingResize = true;
        }

        return finalSize;
    }

    /// <summary>
    /// Destroys the native window handle when the host is being disposed.
    /// </summary>
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // Restore the original WNDPROC before destroying the window
        if (_hwnd != IntPtr.Zero && _originalWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _originalWndProc);
        }

        // Unregister from the static lookup
        lock (_instancesLock)
        {
            _instances.Remove(_hwnd);
        }

        _swapChain?.Dispose();
        _device?.Dispose();
        _swapChain = null;
        _device = null;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Our custom WNDPROC that intercepts mouse messages and raises WPF routed events.
    /// All other messages are forwarded to the original window procedure via CallWindowProc.
    /// </summary>
    private static IntPtr WndProc(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam)
    {
        // Find the D3DRenderSurface instance associated with this HWND
        D3DRenderSurface? surface = null;
        lock (_instancesLock)
        {
            _instances.TryGetValue(hWnd, out surface);
        }

        if (uMsg is 132 or 70)
            return DefWindowProc(hWnd, uMsg, wParam, lParam);
        
        if (surface != null)
        {
            switch (uMsg)
            {
                // --- Left button ---
                case WM_LBUTTONDOWN:
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    surface.RaiseWpfMouseDown(MouseButton.Left);
                    break;

                case WM_LBUTTONUP:
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    surface.RaiseWpfMouseUp(MouseButton.Left);
                    break;

                // --- Right button ---
                case WM_RBUTTONDOWN:
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    surface.RaiseWpfMouseDown(MouseButton.Right);
                    break;

                case WM_RBUTTONUP:
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    surface.RaiseWpfMouseUp(MouseButton.Right);
                    break;

                // --- Middle button ---
                case WM_MBUTTONDOWN:
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    surface.RaiseWpfMouseDown(MouseButton.Middle);
                    break;

                case WM_MBUTTONUP:
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    surface.RaiseWpfMouseUp(MouseButton.Middle);
                    break;

                // --- Mouse move ---
                case WM_MOUSEMOVE:
                {
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    surface.RaiseWpfMouseMove();
                    break; 
                }

                // --- Mouse wheel ---
                case WM_MOUSEWHEEL:
                {
                    surface.LastMousePosition = ExtractMousePosition(lParam);
                    int delta = (int)(short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    surface.RaiseWpfMouseWheel(delta);
                    break;
                }
            }
        }

        // Always forward to the original/default window procedure
        if (surface?._originalWndProc != IntPtr.Zero)
        {
            return CallWindowProc(surface._originalWndProc, hWnd, uMsg, wParam, lParam);
        }

        return DefWindowProc(hWnd, uMsg, wParam, lParam);
    }

    #region Win32 → WPF event translation helpers

    /// <summary>
    /// Raises a WPF MouseDown event on this HwndHost element.
    /// </summary>
    private void RaiseWpfMouseDown(MouseButton button)
    {
        var args = new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            button)
        {
            RoutedEvent = UIElement.MouseDownEvent,
            Source = this,
        };
        RaiseEvent(args);
    }

    /// <summary>
    /// Raises a WPF MouseUp event on this HwndHost element.
    /// </summary>
    private void RaiseWpfMouseUp(MouseButton button)
    {
        var args = new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            button)
        {
            RoutedEvent = UIElement.MouseUpEvent,
            Source = this,
        };
        RaiseEvent(args);
    }

    /// <summary>
    /// Raises a WPF MouseMove event on this HwndHost element.
    /// </summary>
    private void RaiseWpfMouseMove()
    {
        var args = new MouseEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount)
        {
            RoutedEvent = UIElement.MouseMoveEvent,
            Source = this,
        };
        RaiseEvent(args);
    }

    /// <summary>
    /// Raises a WPF MouseWheel event on this HwndHost element.
    /// </summary>
    private void RaiseWpfMouseWheel(int delta)
    {
        var args = new MouseWheelEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = this,
        };
        RaiseEvent(args);
    }

    #endregion

    /// <summary>
    /// Resize the swap chain buffers. Must be called on the render thread
    /// (not the UI thread) to avoid resource conflicts with bound render targets.
    /// </summary>
    public void ResizeSwapChain(int width, int height)
    {
        if (_swapChain == null || width <= 0 || height <= 0) return;

        try
        {
            _swapChain.ResizeBuffers(1, width, height, _rendertargetFormat, SwapChainFlags.None);
        }
        catch (SharpDXException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Resize failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resize the swap chain when the window size changes.
    /// </summary>
    public void Resize(int width, int height)
    {
        ResizeSwapChain(width, height);
    }

    /// <summary>
    /// Present the back buffer to the screen.
    /// </summary>
    public void Present()
    {
        try
        {
            _swapChain?.Present(1, PresentFlags.None);
        }
        catch (SharpDXException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Present failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleanup all D3D resources.
    /// </summary>
    public new void Dispose()
    {
        _swapChain?.Dispose();
        _device?.Dispose();
        _swapChain = null;
        _device = null;
    }
}
