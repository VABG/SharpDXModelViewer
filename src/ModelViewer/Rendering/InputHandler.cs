using System.Windows;
using System.Windows.Input;
namespace ModelViewer.Rendering;

/// <summary>
/// Handles mouse input (rotation, panning, zoom) and translates it into camera commands.
/// Subscribes to standard WPF routed mouse events (MouseDown, MouseUp, MouseMove,
/// MouseWheel) raised by D3DRenderSurface via its UIElement base class — no Win32
/// message hooking required.
/// Uses named event handlers so they can be unsubscribed on Dispose.
/// </summary>
public class InputHandler : IDisposable
{
    private readonly Camera _camera;
    private readonly D3DRenderSurface _surface;

    private bool _isLeftMouseDown;
    private bool _isRightMouseDown;
    private Point _lastMousePosition;
    private bool _disposed;

    // Named event handlers so we can unsubscribe on Dispose
    private readonly MouseButtonEventHandler _downHandler;
    private readonly MouseButtonEventHandler _upHandler;
    private readonly MouseEventHandler _moveHandler;
    private readonly MouseWheelEventHandler _wheelHandler;

    public InputHandler(D3DRenderSurface surface, Camera camera)
    {
        _camera = camera;
        _surface = surface;

        _downHandler = OnMouseDown;
        _upHandler = OnMouseUp;
        _moveHandler = OnMouseMove;
        _wheelHandler = OnMouseWheel;

        _surface.MouseDown += _downHandler;
        _surface.MouseUp += _upHandler;
        _surface.MouseMove += _moveHandler;
        _surface.MouseWheel += _wheelHandler;
    }

    private void OnMouseDown(object? sender, MouseButtonEventArgs e)
    {
        var pos = _surface.LastMousePosition;

        if (e.ChangedButton == MouseButton.Left)
        {
            _isLeftMouseDown = true;
            _lastMousePosition = pos;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _isRightMouseDown = true;
            _lastMousePosition = pos;
        }

        // Mark handled so the event doesn't bubble up past the render surface
        e.Handled = true;
    }

    private void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            _isLeftMouseDown = false;
        else if (e.ChangedButton == MouseButton.Right)
            _isRightMouseDown = false;

        e.Handled = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        var currentPos = _surface.LastMousePosition;
        var deltaX = (float)(currentPos.X - _lastMousePosition.X);
        var deltaY = (float)(currentPos.Y - _lastMousePosition.Y);

        if (_isLeftMouseDown && (deltaX != 0 || deltaY != 0))
            _camera.OnRotate(deltaX, deltaY);
        else if (_isRightMouseDown)
            _camera.OnPan(deltaX, deltaY);

        _lastMousePosition = currentPos;
    }

    private void OnMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        _camera.OnZoom(e.Delta);
        e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _surface.MouseDown -= _downHandler;
        _surface.MouseUp -= _upHandler;
        _surface.MouseMove -= _moveHandler;
        _surface.MouseWheel -= _wheelHandler;
    }
}

