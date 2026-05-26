using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace ModelViewer.Rendering;

/// <summary>
/// Main rendering orchestrator. Manages the render loop, shader pipeline,
/// camera, model loading, and frame presentation.
/// </summary>
public class Renderer : IDisposable
{
    private readonly D3DRenderSurface _surface;
    private readonly DeviceManager _deviceManager;
    private readonly Camera _camera;
    private readonly InputHandler _inputHandler;

    // Shader pipeline resources
    private CompiledShaders? _mainShaders;
    private readonly SharpDX.Direct3D11.Buffer? _viewProjectionBuffer;

    // ── Shadow rendering pipeline (self-contained) ──
    private readonly ShadowRenderer? _shadowRenderer;

    // ── World matrix constant buffer (cbuffer b2, for main scene pass) ──
    private SharpDX.Direct3D11.Buffer? _worldMatrixBuffer;

    // Scene state
    private readonly ModelList _modelList = new();
    private readonly Grid? _grid;

    // Render loop state
    private readonly CancellationTokenSource _cts = new();
    private Task? _renderLoopTask;
    private Task? _updateLoopTask;
    private int _frameCount;

    // ── Centralized shadow + lighting settings (single source of truth) ──
    private readonly DirectionalLightSettings _directionalLightSettings = new();

    /// <summary>
    /// Updates the light direction for shadow mapping and diffuse lighting.
    /// Call from the UI thread; the change takes effect on the next frame.
    /// </summary>
    public void SetLightDirection(Vector3 direction)
    {
        _shadowRenderer?.SetLightDirection(direction);
    }

    /// <summary>
    /// Gets the centralized shadow settings instance.
    /// Exposed so WPF controls can data-bind to it directly.
    /// </summary>
    public DirectionalLightSettings DirectionalLightSettings => _directionalLightSettings;

    /// <summary>
    /// Updates shadow quality parameters (PcfRadius, ShadowBias, ShadowNormalBias).
    /// Call from the UI thread; the change takes effect on the next frame.
    /// </summary>
    public void SetShadowParams(float pcfRadius, float shadowBias, float shadowNormalBias)
    {
        _directionalLightSettings.SetShadowParams(pcfRadius, shadowBias, shadowNormalBias);
    }

    /// <summary>
    /// Updates light and ambient colors.
    /// Call from the UI thread; the change takes effect on the next frame.
    /// </summary>
    public void SetLightColors(Vector4 lightColor, Vector4 ambientColor)
    {
        _directionalLightSettings.LightColor = lightColor;
        _directionalLightSettings.AmbientColor = ambientColor;
    }

    private readonly Stopwatch _fpsWatch = new();

    /// <summary>
    /// Callback invoked once per second with the current FPS.
    /// Used by the UI to update the status bar.
    /// </summary>
    public Action<int>? OnFpsChanged { get; set; }

    public Camera Camera => _camera;

    /// <summary>
    /// Initializes a new renderer with the given D3D surface.
    /// </summary>
    public Renderer(D3DRenderSurface surface)
    {
        _surface = surface;
        _deviceManager = new DeviceManager(surface);
        _camera = new Camera();

        _mainShaders = ShaderCompiler.CompileAndLoad(
            _deviceManager.Device,
            ShaderCompiler.ResolveShaderPath("VertexShader.hlsl"),
            ShaderCompiler.ResolveShaderPath("PixelShader.hlsl"));

        _shadowRenderer = new ShadowRenderer(_deviceManager.Device, _directionalLightSettings);
        // Create view-projection constant buffer (non-generic for SharpDX 4.2.0)
        // Holds two 4x4 matrices (View + Projection) = 128 bytes
        var cbDesc = new BufferDescription
        {
            SizeInBytes = Marshal.SizeOf<Matrix>() * 2,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        _viewProjectionBuffer = new SharpDX.Direct3D11.Buffer(_deviceManager.Device, cbDesc);
        
        // ── Create world matrix constant buffer (cbuffer b2, one 4×4 matrix = 64 bytes) ──
        var worldCbDesc = new BufferDescription
        {
            SizeInBytes = Marshal.SizeOf<Matrix>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
        _worldMatrixBuffer = new SharpDX.Direct3D11.Buffer(_deviceManager.Device, worldCbDesc);

        // Create reference grid visible even when no model is loaded
        _grid = Grid.Create(_deviceManager.Device, size: 200.0f, divisionsCount: 20);

        _inputHandler = new InputHandler(surface, _camera);
        StartUpdateLoop();
        StartRenderLoop();
    }

    private void StartRenderLoop()
    {
        _fpsWatch.Restart();
        _renderLoopTask = Task.Run(() => RenderLoop(_cts.Token));
    }

    /// <summary>
    /// Starts a dedicated update loop that runs on its own thread at ~60 Hz.
    /// Transforms are updated here so the render loop only reads them (thread-safe via lock).
    /// </summary>
    private void StartUpdateLoop()
    {
        _updateLoopTask = Task.Run(() => UpdateLoop(_cts.Token));
    }

    /// <summary>
    /// Runs every frame to update model transforms, animations, physics, etc.
    /// Mutations are guarded by _transformLock so the render thread can safely read them.
    /// </summary>
    private void UpdateLoop(CancellationToken token)
    {
        const int targetIntervalMs = 16; // ~60 Hz
        var lastTimestamp = Stopwatch.GetTimestamp();
        var ticksPerMs = Stopwatch.Frequency / 1000.0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                float dt = MathF.Min((float)((now - lastTimestamp) / ticksPerMs / 1000.0), 0.1f); // clamp to 100ms
                lastTimestamp = now;

                // Sleep to maintain ~60 Hz cadence
                Thread.Sleep(targetIntervalMs);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SharpDXException ex)
            {
                Debug.WriteLine($"Update error: {ex.Message}");
                Thread.Sleep(16);
            }
        }
    }

    private void Update(float dt)
    {
        // ── Update grid transform (slow Y-axis rotation test) ────────────
        const float gridRotationSpeed = 0.15f; // radians per second
        _grid!.Transform = _grid.Transform.WithRotationAdded(
            new Vector3(0, gridRotationSpeed * dt, 0));
    }

    /// <summary>
    /// Uploads world matrix and issues a draw call for the given drawable object.
    /// The object's <see cref="IDrawableObject.Transform"/> property is thread-safe
    /// (guarded by the object's own internal lock).
    /// </summary>
    private void DrawObject(DeviceContext context, IDrawableObject obj)
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
    /// Call once per draw with the object's current transform.
    /// </summary>
    private void UploadWorldMatrix(DeviceContext context, ModelTransform transform)
    {
        if (_worldMatrixBuffer == null) return;

        var world = transform.ToMatrix();
        world.Transpose();

        context.MapSubresource(_worldMatrixBuffer, MapMode.WriteDiscard,
            MapFlags.None, out var map);
        Marshal.StructureToPtr(world, map.DataPointer, false);
        context.UnmapSubresource(_worldMatrixBuffer, 0);

        context.VertexShader.SetConstantBuffer(2, _worldMatrixBuffer);
    }

    /// <summary>
    /// Uploads the current view and projection matrices to the GPU constant buffer at slot b0.
    /// </summary>
    private void UploadViewProjection(DeviceContext context)
    {
        var viewMatrix = _camera.ViewMatrix;
        var projMatrix = _camera.ProjectionMatrix;
        viewMatrix.Transpose();
        projMatrix.Transpose();

        context.MapSubresource(_viewProjectionBuffer, MapMode.WriteDiscard,
            MapFlags.None, out var data);

        // Write View matrix (first 64 bytes)
        Marshal.StructureToPtr(viewMatrix, data.DataPointer, false);

        // Write Projection matrix (next 64 bytes, offset by sizeof(Matrix))
        IntPtr projPtr = IntPtr.Add(data.DataPointer, Marshal.SizeOf<Matrix>());
        Marshal.StructureToPtr(projMatrix, projPtr, false);

        context.UnmapSubresource(_viewProjectionBuffer, 0);
    }

    /// <summary>
    /// Configures the full D3D11 pipeline state for the main scene pass:
    /// shaders, constant buffers, shadow map bindings, depth stencil, and rasterizer state.
    /// </summary>
    private void SetupMainScenePipeline(DeviceContext context)
    {
        // ── Set pipeline state ─────────────────────────────────────────
        context.InputAssembler.InputLayout = _mainShaders!.InputLayout;
        context.VertexShader.Set(_mainShaders.VertexShader);
        context.PixelShader.Set(_mainShaders.PixelShader);
        context.VertexShader.SetConstantBuffer(0, _viewProjectionBuffer);

        _shadowRenderer?.BindForMainPass(context);
        
        context.OutputMerger.SetDepthStencilState(_deviceManager.DepthStencilState);
        context.Rasterizer.State = _deviceManager.RasterizerState;
    }

    /// <summary>
    /// Draws the grid and all scene models in the snapshot.
    /// </summary>
    private void DrawMainScene(DeviceContext context, IReadOnlyList<SceneModel> snapshot)
    {
        // ── Draw grid (always visible) ─────────────────────────────────
        DrawObject(context, _grid!);

        // ── Draw all scene models ────────────────────────────────────
        foreach (var sm in snapshot)
            DrawObject(context, sm);
    }

    /// <summary>
    /// Processes a pending swap chain resize on the render thread.
    /// </summary>
    private void HandlePendingResize()
    {
        if (_surface.HasPendingResize)
        {
            var (newWidth, newHeight) = _surface.ConsumePendingResize();
            _deviceManager.Resize(newWidth, newHeight);
        }
    }

    /// <summary>
    /// Returns the current back buffer dimensions.
    /// </summary>
    private (int Width, int Height) GetBackBufferDimensions()
    {
        using var backBuffer = _deviceManager.SwapChain.GetBackBuffer<Texture2D>(0);
        return (backBuffer.Description.Width, backBuffer.Description.Height);
    }

    /// <summary>
    /// Ticks the FPS counter. If a full second has elapsed, notifies the UI and resets.
    /// </summary>
    private void TickFps()
    {
        _frameCount++;
        if (_fpsWatch.ElapsedMilliseconds >= 1000)
        {
            int fps = _frameCount;
            _frameCount = 0;
            _fpsWatch.Restart();

            // Notify UI of FPS (callback is invoked on render thread;
            // caller is responsible for marshaling to UI thread)
            OnFpsChanged?.Invoke(fps);
        }
    }

    private void RenderLoop(CancellationToken token)
    {
        var device = _deviceManager.Device;
        var context = device.ImmediateContext;

        while (!token.IsCancellationRequested)
        {
            try
            {
                // ── Handle pending resize on the render thread ─────────────────
                // ArrangeOverride (UI thread) queues resize dimensions. We consume
                // them here so ResizeBuffers runs while no resources are bound,
                // avoiding device-resource-conflict exceptions.
                HandlePendingResize();

                // ── Query back buffer dimensions ───────────────────────────────
                // SwapChain.Description.ModeDescription is frozen at creation time
                // (1×1).  Read the live back buffer instead, which reflects
                // ResizeBuffers calls from ArrangeOverride.
                var (width, height) = GetBackBufferDimensions();

                // Wait until WPF layout has resized the swap chain to a real size.
                // The initial window is 1×1; ArrangeOverride will resize it once
                // WPF layout runs (which happens after MainWindow_Loaded returns).
                if (width <= 1 || height <= 1)
                {
                    Thread.Sleep(16);
                    continue;
                }

                var depthStencilView = _deviceManager.DepthStencilView;
                var renderTargetView = _deviceManager.RenderTargetView;

                if (renderTargetView == null || depthStencilView == null)
                {
                    Thread.Sleep(16);
                    continue;
                }

                // ── Set viewport ───────────────────────────────────────────────
                // D3D11 default viewport is (0,0,0,0) — zero size means the
                // rasterizer clips every primitive away. Must set it every frame
                // in case the swap chain was resized.
                context.Rasterizer.SetViewport(0, 0, width, height);

                // ── Clear ──────────────────────────────────────────────────────
                context.OutputMerger.SetTargets(depthStencilView, renderTargetView);
                context.ClearRenderTargetView(renderTargetView, new Color4(0.2f, 0.2f, 0.2f, 1.0f));
                context.ClearDepthStencilView(depthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

                // ── Camera ─────────────────────────────────────────────────────
                _camera.UpdateProjection(width, height);

                // ── Take a thread-safe snapshot of scene models ──
                // This snapshot is valid for the entire frame and is used in both
                // the shadow depth pass and the main scene pass.
                var snapshot = _modelList.GetSnapshot();
                
                _shadowRenderer?.RenderDepthPass(context, snapshot);

                // ═══════════════════════════════════════════════════════════════
                //  MAIN SCENE PASS
                //  Restore main targets and render with shadows applied.
                // ═══════════════════════════════════════════════════════════════

                context.Rasterizer.SetViewport(0, 0, width, height);
                context.OutputMerger.SetTargets(depthStencilView, renderTargetView);
                UploadViewProjection(context);
                SetupMainScenePipeline(context);
                DrawMainScene(context, snapshot);


                // ── Unbind shadow map resources (good practice before present) ──
                _shadowRenderer?.UnbindFromMainPass(context);

                // ── Present ────────────────────────────────────────────────────
                _surface.Present();

                TickFps();

                Thread.Sleep(1);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SharpDXException ex)
            {
                Debug.WriteLine($"Render error: {ex.Message}");
                Thread.Sleep(16);
            }
        }
    }

    /// <summary>
    /// Adds a model to the scene. Multiple models can be loaded simultaneously.
    /// </summary>
    public SceneModel AddModel(string filePath)
    {
        return _modelList.Add(_deviceManager.Device, filePath);
    }

    /// <summary>
    /// Removes a model from the scene and disposes its GPU resources.
    /// </summary>
    public bool RemoveModel(SceneModel sceneModel)
    {
        return _modelList.Remove(sceneModel);
    }

    /// <summary>
    /// Gets the model list for UI binding.
    /// </summary>
    public ModelList ModelList => _modelList;

    public void ResetCamera()
    {
        _camera.Reset();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _renderLoopTask?.Wait(2000);
        _updateLoopTask?.Wait(2000);

        _inputHandler.Dispose();
        _modelList.Dispose();
        _grid?.Dispose();
        _worldMatrixBuffer?.Dispose();
        _mainShaders?.Dispose();
        _viewProjectionBuffer?.Dispose();
        _deviceManager.Dispose();
        _surface.Dispose();
    }
}