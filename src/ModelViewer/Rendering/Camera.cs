using SharpDX;

namespace ModelViewer.Rendering;

/// <summary>
/// Orbit camera implementation supporting rotation, panning, and zooming.
/// Uses spherical coordinates for intuitive orbit controls around a target point.
/// </summary>
public class Camera
{
    private float _distance = 20.0f;
    private float _pitch = MathF.PI / 4;
    private float _yaw = MathF.PI / 4;

    private Vector3 _target = Vector3.Zero;

    public float MinDistance { get; set; } = 0.1f;
    public float MaxDistance { get; set; } = 100.0f;

    /// <summary>
    /// Gets the current view matrix.
    /// </summary>
    public Matrix ViewMatrix { get; private set; }

    /// <summary>
    /// Gets the current projection matrix.
    /// </summary>
    public Matrix ProjectionMatrix { get; private set; }
    public float RotationSensitivity { get; set; } = 0.005f;

    /// <summary>
    /// Handles mouse wheel input for zooming.
    /// </summary>
    public void OnZoom(float delta)
    {
        if (delta == 0) return;

        // Normalize scroll delta to discrete steps (1–3 per frame)
        // Prevents runaway zoom when scrolling fast or when raw delta values are large
        float absDelta = MathF.Abs(delta);
        int steps = (int)MathF.Max(1f, MathF.Min(3f, absDelta));
        float direction = MathF.Sign(delta);

        // Multiplicative zoom factor — consistent feel at all distances
        const float zoomFactor = 0.1f;

        for (int i = 0; i < steps; i++)
        {
            _distance *= direction > 0 ? (1f - zoomFactor) : (1f + zoomFactor);
            _distance = MathF.Max(MinDistance, MathF.Min(_distance, MaxDistance));

            // Stop early if we've hit a boundary
            if (_distance <= MinDistance || _distance >= MaxDistance)
                break;
        }

        UpdateViewMatrix();
    }

    private float PanSensitivity { get; set; } = 0.005f;

    public Camera()
    {
        UpdateViewMatrix();
    }

    /// <summary>
    /// Updates the projection matrix based on viewport dimensions.
    /// </summary>
    public void UpdateProjection(int width, int height, float fov = MathF.PI / 3, 
        float nearPlane = 0.1f, float farPlane = 1000.0f)
    {
        float aspectRatio = width / (float)height;
        ProjectionMatrix = Matrix.PerspectiveFovLH(fov, aspectRatio, nearPlane, farPlane);
    }

    public void OnRotate(float deltaX, float deltaY)
    {
        _yaw += deltaX * RotationSensitivity;
        _pitch += deltaY * RotationSensitivity;

        // Clamp pitch to prevent flipping (pole singularity)
        _pitch = MathF.Max(-MathF.PI / 2 + 0.01f, MathF.Min(_pitch, MathF.PI / 2 - 0.01f));

        UpdateViewMatrix();
    }

    /// <summary>
    /// Handles mouse delta input for panning (right-click drag).
    /// </summary>
    public void OnPan(float deltaX, float deltaY)
    {
        var lookDir = CalculateLookDirection();
        var right = Vector3.Normalize(Vector3.Cross(lookDir, Vector3.UnitY));
        var up = Vector3.Cross(right, lookDir);

        _target += right * (-deltaX * PanSensitivity * _distance);
        _target += up * (deltaY * PanSensitivity * _distance);

        UpdateViewMatrix();
    }
    
    /// <summary>
    /// Resets the camera to its default position.
    /// </summary>
    public void Reset()
    {
        _distance = 20.0f;
        _pitch = MathF.PI / 4;
        _yaw = MathF.PI / 4;
        _target = Vector3.Zero;
        UpdateViewMatrix();
    }

    /// <summary>
    /// Sets the camera target point for orbiting.
    /// </summary>
    public void SetTarget(Vector3 target)
    {
        _target = target;
        UpdateViewMatrix();
    }

    private void UpdateViewMatrix()
    {
        var sinPitch = MathF.Sin(_pitch);
        var cosPitch = MathF.Cos(_pitch);
        var sinYaw = MathF.Sin(_yaw);
        var cosYaw = MathF.Cos(_yaw);

        var position = new Vector3(
            _distance * cosPitch * sinYaw,
            _distance * sinPitch,
            _distance * cosPitch * cosYaw
        ) + _target;

        ViewMatrix = Matrix.LookAtLH(position, _target, Vector3.UnitY);
    }

    private Vector3 CalculateLookDirection()
    {
        var sinPitch = MathF.Sin(_pitch);
        var cosPitch = MathF.Cos(_pitch);
        var sinYaw = MathF.Sin(_yaw);
        var cosYaw = MathF.Cos(_yaw);

        return new Vector3(
            cosPitch * sinYaw,
            sinPitch,
            cosPitch * cosYaw
        );
    }
}

