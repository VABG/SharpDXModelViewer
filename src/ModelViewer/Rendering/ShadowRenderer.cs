using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Device = SharpDX.Direct3D11.Device;

namespace ModelViewer.Rendering;

/// <summary>
/// Encapsulates all shadow map rendering logic:
///   • Shadow map texture and views ownership
///   • Depth-pass shaders and constant buffer
///   • Rendering the scene from the light's perspective
///   • Binding shadow resources for the main scene pass
///
/// This keeps Renderer.cs focused on orchestration while ShadowRenderer
/// owns the entire shadow pipeline.
/// </summary>
public class ShadowRenderer : IDisposable
{
    private readonly DirectionalLightSettings _lightSettings;

    // ── Shadow map resources ──
    private readonly ShadowMap _shadowMap;

    // ── Depth-pass shaders ──
    private readonly VertexShader _shadowVertexShader;
    private readonly PixelShader _shadowPixelShader;

    // ── Shadow constant buffer (holds LightViewProjection + LightDirection + settings) ──
    private readonly Buffer _shadowConstantBuffer;

    // ── World matrix constant buffer (cbuffer b2, one 4×4 matrix = 64 bytes) ──
    // Owned by ShadowRenderer so the depth pass is fully self-contained.
    private readonly Buffer _worldMatrixBuffer;

    public ShadowMap ShadowMap => _shadowMap;

    /// <summary>
    /// Initializes the shadow rendering pipeline.
    /// </summary>
    /// <param name="device">The D3D11 device.</param>
    /// <param name="lightSettings">Centralized light/shadow settings (shared with Renderer).</param>
    /// <param name="shadowSize">Shadow map texture resolution (default 2048).</param>
    public ShadowRenderer(Device device, DirectionalLightSettings lightSettings, int shadowSize = 2048)
    {
        _lightSettings = lightSettings;

        // ── Create the shadow map ──
        _shadowMap = new ShadowMap(device, shadowSize);

        // ── Compile depth-pass shaders ──
        var shadowVsPath = ShaderCompiler.ResolveShaderPath("VertexShaderShadow.hlsl");
        var shadowPsPath = ShaderCompiler.ResolveShaderPath("PixelShaderShadow.hlsl");

        var (shadowVs, _) = ShaderCompiler.CompileVertexShader(device, shadowVsPath);
        _shadowVertexShader = shadowVs;

        _shadowPixelShader = ShaderCompiler.CompilePixelShader(device, shadowPsPath);

        // ── Create shadow constant buffer ──
        var shadowCbDesc = new BufferDescription
        {
            SizeInBytes = Marshal.SizeOf<ShadowConstantBuffer>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        _shadowConstantBuffer = new Buffer(device, shadowCbDesc);

        // ── Create world matrix constant buffer (cbuffer b2, one 4×4 matrix = 64 bytes) ──
        var worldCbDesc = new BufferDescription
        {
            SizeInBytes = Marshal.SizeOf<Matrix>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        _worldMatrixBuffer = new Buffer(device, worldCbDesc);
    }

    /// <summary>
    /// Updates the light direction for shadow mapping and diffuse lighting.
    /// Call from the UI thread; the change takes effect on the next frame.
    /// </summary>
    public void SetLightDirection(Vector3 direction, float sceneRadius = 150f, float sceneHeight = 100f)
    {
        // ShadowMap.UpdateLightCamera expects the direction the light shines FROM.
        // It internally negates it to store the direction pointing TO the light.
        _shadowMap.UpdateLightCamera(direction, sceneRadius, sceneHeight);
    }

    /// <summary>
    /// Renders the scene from the light's perspective into the shadow map.
    /// </summary>
    public void RenderDepthPass(DeviceContext context, IReadOnlyList<IDrawableObject> scene)
    {
        if (_shadowMap.ShadowDepthView == null)
            return;

        // Clear shadow map depth buffer to 1.0 (farthest possible depth)
        context.ClearDepthStencilView(_shadowMap.ShadowDepthView,
            DepthStencilClearFlags.Depth, 1.0f, 0);

        // Bind shadow map as the depth target (no RTV — we only need depth)
        context.OutputMerger.SetTargets(_shadowMap.ShadowDepthView);

        // Set viewport to shadow map size
        context.Rasterizer.SetViewport(0, 0, _shadowMap.Size, _shadowMap.Size);

        // Upload shadow constant buffer with LightViewProjection matrix + LightDirection
        UploadShadowConstantBuffer(context);

        // Set depth-pass pipeline state
        context.VertexShader.Set(_shadowVertexShader);
        context.PixelShader.Set(_shadowPixelShader);
        context.VertexShader.SetConstantBuffer(0, _shadowConstantBuffer);

        // ── Draw all scene objects into shadow map ──
        foreach (var obj in scene)
        {
            DrawShadowMapObject(context, obj);
        }
    }

    /// <summary>
    /// Binds shadow resources for the main scene pass:
    ///   • Shadow constant buffer at cbuffer slot b1 (vertex + pixel shader)
    ///   • Shadow map texture at t0 (pixel shader)
    /// </summary>
    public void BindForMainPass(DeviceContext context)
    {
        // ── Bind shadow matrices at cbuffer slot b1 (vertex + pixel shader) ──
        context.VertexShader.SetConstantBuffer(1, _shadowConstantBuffer);
        context.PixelShader.SetConstantBuffer(1, _shadowConstantBuffer);

        // ── Bind shadow map texture at t0 (pixel shader) ──
        if (_shadowMap.ShadowSrv != null)
        {
            context.PixelShader.SetShaderResource(0, _shadowMap.ShadowSrv);
        }
    }

    /// <summary>
    /// Unbinds the shadow map texture from the pixel shader.
    /// Call before Present() as a best practice.
    /// </summary>
    public void UnbindFromMainPass(DeviceContext context)
    {
        context.PixelShader.SetShaderResource(0, null);
    }

    /// <summary>
    /// Uploads the shadow constant buffer with the current light view-projection matrix
    /// and lighting/shadow settings snapshot.
    /// </summary>
    private void UploadShadowConstantBuffer(DeviceContext context)
    {
        var cb = new ShadowConstantBuffer(_shadowMap.LightViewProjectionMatrix, _shadowMap.LightDirection);
        cb.LightViewProjection.Transpose();

        // Apply current UI-controlled lighting parameters from a thread-safe snapshot
        ShadowSettingsSnapshot settings = _lightSettings.CaptureSnapshot();
        cb.PcfRadius = settings.PcfRadius;
        cb.ShadowBias = settings.ShadowBias;
        cb.ShadowNormalBias = settings.ShadowNormalBias;
        cb.LightColor = settings.LightColor;
        cb.AmbientColor = settings.AmbientColor;

        context.MapSubresource(_shadowConstantBuffer, MapMode.WriteDiscard,
            MapFlags.None, out var shadowData);
        Marshal.StructureToPtr(cb, shadowData.DataPointer, false);
        context.UnmapSubresource(_shadowConstantBuffer, 0);
    }

    /// <summary>
    /// Uploads world matrix and issues a draw call for shadow map depth pass.
    /// Uses the same vertex format as the main pass (VertexPositionNormalTexture).
    /// </summary>
    private void DrawShadowMapObject(DeviceContext context, IDrawableObject obj)
    {
        if (obj.VertexBuffer == null || obj.IndexBuffer == null)
            return;

        UploadWorldMatrix(context, obj.Transform);

        var stride = VertexPositionNormalTexture.SizeInBytes;
        context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(
            obj.VertexBuffer, stride, 0));
        context.InputAssembler.SetIndexBuffer(obj.IndexBuffer,
            Format.R32_UInt, 0);
        context.InputAssembler.PrimitiveTopology =
            SharpDX.Direct3D.PrimitiveTopology.TriangleList;
        context.DrawIndexed(obj.IndexCount, 0, 0);
    }

    /// <summary>
    /// Uploads a model-space world matrix to the GPU constant buffer at slot b2.
    /// </summary>
    private void UploadWorldMatrix(DeviceContext context, ModelTransform transform)
    {
        var world = transform.ToMatrix();
        world.Transpose();

        context.MapSubresource(_worldMatrixBuffer, MapMode.WriteDiscard,
            MapFlags.None, out var map);
        Marshal.StructureToPtr(world, map.DataPointer, false);
        context.UnmapSubresource(_worldMatrixBuffer, 0);

        context.VertexShader.SetConstantBuffer(2, _worldMatrixBuffer);
    }

    public void Dispose()
    {
        _shadowMap?.Dispose();
        _shadowVertexShader?.Dispose();
        _shadowPixelShader?.Dispose();
        _shadowConstantBuffer?.Dispose();
        _worldMatrixBuffer?.Dispose();
    }
}
