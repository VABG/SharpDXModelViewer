using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace ModelViewer.Rendering;

/// <summary>
/// Manages the shadow map for orthographic (directional) skylight shadows.
/// 
/// Architecture:
///   1. Creates a high-res depth texture (shadow map) + depth-stencil view.
///   2. Provides an orthographic light camera that covers the scene bounding area.
///   3. Renders a depth-only pass from the light's POV.
///   4. Exposes the shadow map as a ShaderResourceView for the main scene pass.
/// </summary>
public class ShadowMap : IDisposable
{
    private readonly Device _device;
    private Texture2D? _shadowTexture;
    private DepthStencilView? _shadowDepthView;
    private ShaderResourceView? _shadowSrv;
    private bool _disposed;

    /// <summary> Shadow map resolution — 2048 is a good balance for most scenes. </summary>
    public int Size { get; }

    /// <summary> The orthographic light-space view matrix. </summary>
    public Matrix LightViewMatrix { get; private set; }

    /// <summary> The orthographic projection matrix covering the scene area. </summary>
    public Matrix LightProjectionMatrix { get; private set; }

    /// <summary> Combined light View × Projection matrix (pre-transposed for shader upload). </summary>
    public Matrix LightViewProjectionMatrix { get; private set; }

    /// <summary> Direction pointing TO the light source (for diffuse lighting). </summary>
    public Vector3 LightDirection { get; private set; }

    /// <summary> Shader resource view bound to the pixel shader for shadow sampling. </summary>
    public ShaderResourceView? ShadowSrv => _shadowSrv;

    /// <summary> Depth-stencil view used as render target during the depth pass. </summary>
    public DepthStencilView? ShadowDepthView => _shadowDepthView;

    /// <summary>
    /// Creates a shadow map of the given resolution.
    /// </summary>
    /// <param name="shadowSize">Power-of-2 texture size (default 2048).</param>
    /// <param name="lightDirection">Direction the light shines FROM (default: top-down at angle).</param>
    /// <param name="sceneRadius">Half-extent of the orthographic frustum in X/Z (default: 100).</param>
    /// <param name="sceneHeight">Half-height of the orthographic frustum in Y (default: 50).</param>
    public ShadowMap(Device device, int shadowSize = 2048,
        Vector3? lightDirection = null, float sceneRadius = 100f, float sceneHeight = 50f)
    {
        _device = device;
        Size = shadowSize;

        // Default light direction: top-down at ~45° angle, pointing toward origin
        var lightDir = lightDirection ?? new Vector3(0.577f, -0.577f, 0.577f);
        lightDir = Vector3.Normalize(lightDir);

        // Store direction pointing TO the light (opposite of ray direction)
        LightDirection = -lightDir;

        BuildOrthographicCamera(lightDir, sceneRadius, sceneHeight);
        CreateShadowTexture();
    }

    /// <summary>
    /// Builds the orthographic light camera matrices.
    /// 
    /// The light "camera" looks down the light direction vector from above the scene,
    /// using an orthographic frustum sized to cover the entire scene area.
    /// </summary>
    private void BuildOrthographicCamera(Vector3 lightDirection, float sceneRadius, float sceneHeight)
    {
        // Light position: far up along the light direction (so everything is in front of it)
        var lightPosition = -lightDirection * (sceneRadius * 2.0f);

        // Light looks toward the origin
        var lightTarget = Vector3.Zero;

        // Avoid gimbal lock: if light direction is parallel to WorldUp, use WorldX as up vector
        Vector3 upVector = Math.Abs(lightDirection.Y) > 0.999f ? Vector3.UnitX : Vector3.UnitY;

        // Build view matrix: light camera looking from lightPosition toward origin
        LightViewMatrix = Matrix.LookAtLH(lightPosition, lightTarget, upVector);

        // Orthographic projection covering the scene bounding box
        LightProjectionMatrix = Matrix.OrthoLH(
            width: sceneRadius * 2.0f,
            height: sceneHeight * 2.0f,
            znear: 0.1f,
            zfar: sceneRadius * 4.0f
        );

        LightViewProjectionMatrix = LightViewMatrix * LightProjectionMatrix;
    }

    /// <summary>
    /// Creates the shadow map texture, depth-stencil view, and shader resource view.
    /// 
    /// Uses R32_Typeless texture so it can be bound as both DepthStencil and ShaderResource.
    /// The DSV sees it as D32_Float (depth), the SRV sees it as R32_Float (readable).
    /// </summary>
    private void CreateShadowTexture()
    {
        var textureDesc = new Texture2DDescription
        {
            Width = Size,
            Height = Size,
            MipLevels = 1,          // No mipmaps needed for shadow maps
            ArraySize = 1,
            Format = Format.R32_Typeless,  // Allows both DSV and SRV bindings
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None,
        };

        _shadowTexture = new Texture2D(_device, textureDesc);

        // Create DSV with explicit depth format
        var dsvDesc = new DepthStencilViewDescription
        {
            Format = Format.D32_Float,
            Dimension = DepthStencilViewDimension.Texture2D,
            Texture2D = { MipSlice = 0 }
        };
        _shadowDepthView = new DepthStencilView(_device, _shadowTexture, dsvDesc);

        // Create SRV with explicit readable format
        var srvDesc = new ShaderResourceViewDescription
        {
            Format = Format.R32_Float,
            Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
            Texture2D = { MostDetailedMip = 0, MipLevels = 1 }
        };
        _shadowSrv = new ShaderResourceView(_device, _shadowTexture, srvDesc);
    }

    /// <summary>
    /// Rebuilds the light camera with a new direction or scene bounds.
    /// Call this if your scene changes significantly.
    /// </summary>
    public void UpdateLightCamera(Vector3 lightDirection, float sceneRadius, float sceneHeight)
    {
        lightDirection = Vector3.Normalize(lightDirection);
        LightDirection = -lightDirection; // Direction pointing TO the light
        BuildOrthographicCamera(lightDirection, sceneRadius, sceneHeight);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _shadowSrv?.Dispose();
        _shadowDepthView?.Dispose();
        _shadowTexture?.Dispose();
        _disposed = true;
    }
}
