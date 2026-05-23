using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
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
    private VertexShader? _vertexShader;
    private PixelShader? _pixelShader;
    private InputLayout? _inputLayout;
    private ShaderSignature? _inputSignature;
    private readonly SharpDX.Direct3D11.Buffer? _viewProjectionBuffer;

        // ── Shadow map resources ──
    private ShadowMap? _shadowMap;
    private VertexShader? _shadowVertexShader;
    private PixelShader? _shadowPixelShader;
    private SharpDX.Direct3D11.Buffer? _shadowConstantBuffer;

    // ── World matrix constant buffer (cbuffer b2) ──
    private SharpDX.Direct3D11.Buffer? _worldMatrixBuffer;

    // Scene state
    private Model? _currentModel;
    private readonly Grid? _grid;

                // Render loop state
    private readonly CancellationTokenSource _cts = new();
    private Task? _renderLoopTask;
    private Task? _updateLoopTask;
    private int _frameCount;

    /// <summary>
    /// Updates the light direction for shadow mapping and diffuse lighting.
    /// Call from the UI thread; the change takes effect on the next frame.
    /// </summary>
    public void SetLightDirection(Vector3 direction)
    {
        if (_shadowMap == null) return;

        // ShadowMap.UpdateLightCamera expects the direction the light shines FROM.
        // It internally negates it to store the direction pointing TO the light.
        _shadowMap.UpdateLightCamera(direction, sceneRadius: 100f, sceneHeight: 50f);
    }
    private readonly Stopwatch _fpsWatch = new();
    private int _lastWidth;
    private int _lastHeight;

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

        CompileAndLoadShaders();
        InitializeShadowMap();

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

                // ── Create shadow constant buffer (holds LightViewProjection matrix + LightDirection) ──
        var shadowCbDesc = new BufferDescription
        {
            SizeInBytes = Marshal.SizeOf<ShadowConstantBuffer>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None,
        };
                _shadowConstantBuffer = new SharpDX.Direct3D11.Buffer(_deviceManager.Device, shadowCbDesc);

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

        private void CompileAndLoadShaders()
        {
            var device = _deviceManager.Device;

            // Load and compile shaders from .hlsl files
            var vsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "VertexShader.hlsl");
            var psFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "PixelShader.hlsl");

            if (!File.Exists(vsFilePath))
                throw new FileNotFoundException($"Vertex shader file not found: {vsFilePath}");
            if (!File.Exists(psFilePath))
                throw new FileNotFoundException($"Pixel shader file not found: {psFilePath}");

            // ── Compile vertex shader ──
            ShaderBytecode vsBlob;
            try
            {
                vsBlob = ShaderBytecode.CompileFromFile(vsFilePath, "VSMain", "vs_4_0");
            }
            catch (SharpDXException ex)
            {
                throw new InvalidOperationException(
                    $"Vertex shader compilation failed ({vsFilePath}):\n\n{ex.Message}", ex);
            }

            _inputSignature = ShaderSignature.GetInputSignature(vsBlob);
            _vertexShader = new VertexShader(device, vsBlob);

            // ── Compile pixel shader ──
            ShaderBytecode psBlob;
            try
            {
                psBlob = ShaderBytecode.CompileFromFile(psFilePath, "PSMain", "ps_4_0");
            }
            catch (SharpDXException ex)
            {
                throw new InvalidOperationException(
                    $"Pixel shader compilation failed ({psFilePath}):\n\n{ex.Message}", ex);
            }

            _pixelShader = new PixelShader(device, psBlob);

            var layoutElements = new[]
            {
                new InputElement("Position", 0, Format.R32G32B32_Float, 0),
                new InputElement("Normal",   0, Format.R32G32B32_Float, 0),
                new InputElement("TexCoord", 0, Format.R32G32_Float,   0),
            };
            _inputLayout = new InputLayout(device, _inputSignature, layoutElements);

            vsBlob.Dispose();
            psBlob.Dispose();
        }

        /// <summary>
        /// Creates the shadow map texture and compiles the depth-pass shaders.
        /// </summary>
        private void InitializeShadowMap()
        {
            var device = _deviceManager.Device;

            // ── Create the shadow map (2048x2048, orthographic skylight) ──
            _shadowMap = new ShadowMap(device, shadowSize: 2048);

            // ── Compile depth-pass vertex shader ──
            var shadowVsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Shaders", "VertexShaderShadow.hlsl");

            try
            {
                var shadowVsBlob = ShaderBytecode.CompileFromFile(shadowVsPath, "VSMain", "vs_4_0");
                _shadowVertexShader = new VertexShader(device, shadowVsBlob);
                shadowVsBlob.Dispose();
            }
            catch (SharpDXException ex)
            {
                throw new InvalidOperationException(
                    $"Shadow vertex shader compilation failed ({shadowVsPath}):\n\n{ex.Message}", ex);
            }

            // ── Compile depth-pass pixel shader ──
            var shadowPsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Shaders", "PixelShaderShadow.hlsl");

            try
            {
                var shadowPsBlob = ShaderBytecode.CompileFromFile(shadowPsPath, "PSMain", "ps_4_0");
                _shadowPixelShader = new PixelShader(device, shadowPsBlob);
                shadowPsBlob.Dispose();
            }
            catch (SharpDXException ex)
            {
                throw new InvalidOperationException(
                    $"Shadow pixel shader compilation failed ({shadowPsPath}):\n\n{ex.Message}", ex);
            }
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
        long lastTimestamp = Stopwatch.GetTimestamp();
        double ticksPerMs = (double)Stopwatch.Frequency / 1000.0;

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
                System.Diagnostics.Debug.WriteLine($"Update error: {ex.Message}");
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
                if (_surface.HasPendingResize)
                {
                    var (newWidth, newHeight) = _surface.ConsumePendingResize();
                    // DeviceManager.Resize does the full atomic sequence:
                    //   1. Flush GPU (drain commands referencing old back buffer)
                    //   2. Dispose old RTV + depth stencil (release COM refs)
                    //   3. ResizeBuffers (now safe — no live references)
                    //   4. Create new RTV + depth stencil from new back buffer
                    _deviceManager.Resize(newWidth, newHeight);
                    _lastWidth = newWidth;
                    _lastHeight = newHeight;
                }

                // ── Query back buffer dimensions ───────────────────────────────
                // SwapChain.Description.ModeDescription is frozen at creation time
                // (1×1).  Read the live back buffer instead, which reflects
                // ResizeBuffers calls from ArrangeOverride.
                var swapChain = _deviceManager.SwapChain;
                using var backBuffer = swapChain.GetBackBuffer<Texture2D>(0);
                int width = backBuffer.Description.Width;
                int height = backBuffer.Description.Height;

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

                                // ═══════════════════════════════════════════════════════════════
                //  SHADOW MAP DEPTH PASS
                //  Render the scene from the light's perspective into the shadow map.
                // ═══════════════════════════════════════════════════════════════
                if (_shadowMap != null && _shadowMap.ShadowDepthView != null)
                {
                    // Clear shadow map depth buffer to 1.0 (farthest possible depth)
                    context.ClearDepthStencilView(_shadowMap.ShadowDepthView,
                        DepthStencilClearFlags.Depth, 1.0f, 0);

                    // Bind shadow map as the depth target (no RTV — we only need depth)
                    context.OutputMerger.SetTargets(_shadowMap.ShadowDepthView);

                    // Set viewport to shadow map size
                    context.Rasterizer.SetViewport(0, 0, _shadowMap.Size, _shadowMap.Size);

                    // Update shadow constant buffer with LightViewProjection matrix + LightDirection
                    var cb = new ShadowConstantBuffer(_shadowMap.LightViewProjectionMatrix, _shadowMap.LightDirection);
                    cb.LightViewProjection.Transpose();
                    context.MapSubresource(_shadowConstantBuffer, MapMode.WriteDiscard,
                        MapFlags.None, out var shadowData);
                    Marshal.StructureToPtr(cb, shadowData.DataPointer, false);
                    context.UnmapSubresource(_shadowConstantBuffer, 0);

                    // Set depth-pass pipeline state
                    context.InputAssembler.InputLayout = _inputLayout;
                    context.VertexShader.Set(_shadowVertexShader);
                    context.PixelShader.Set(_shadowPixelShader);
                    context.VertexShader.SetConstantBuffer(0, _shadowConstantBuffer);

                                        // ── Draw grid into shadow map ──
                    DrawObject(context, _grid!);

                    // ── Draw model into shadow map ──
                    if (_currentModel != null)
                        DrawObject(context, _currentModel);
                }

                // ═══════════════════════════════════════════════════════════════
                //  MAIN SCENE PASS
                //  Restore main targets and render with shadows applied.
                // ═══════════════════════════════════════════════════════════════

                // Restore main viewport and render targets
                context.Rasterizer.SetViewport(0, 0, width, height);
                context.OutputMerger.SetTargets(depthStencilView, renderTargetView);

                // ── Update constant buffer (View + Projection matrices) ────────
                var viewMatrix = _camera.ViewMatrix;
                var projMatrix = _camera.ProjectionMatrix;
                viewMatrix.Transpose();
                projMatrix.Transpose();

                // Map the dynamic buffer, write both matrices, then unmap
                context.MapSubresource(_viewProjectionBuffer, MapMode.WriteDiscard,
                    MapFlags.None, out var data);

                // Write View matrix (first 64 bytes)
                Marshal.StructureToPtr(viewMatrix, data.DataPointer, false);

                // Write Projection matrix (next 64 bytes, offset by sizeof(Matrix))
                IntPtr projPtr = IntPtr.Add(data.DataPointer, Marshal.SizeOf<Matrix>());
                Marshal.StructureToPtr(projMatrix, projPtr, false);

                context.UnmapSubresource(_viewProjectionBuffer, 0);

                // ── Set pipeline state ─────────────────────────────────────────
                context.InputAssembler.InputLayout = _inputLayout;
                context.VertexShader.Set(_vertexShader);
                context.PixelShader.Set(_pixelShader);
                context.VertexShader.SetConstantBuffer(0, _viewProjectionBuffer);

                                // ── Bind shadow matrices at cbuffer slot b1 (vertex + pixel shader) ──
                if (_shadowMap != null && _shadowConstantBuffer != null)
                {
                    context.VertexShader.SetConstantBuffer(1, _shadowConstantBuffer);
                    context.PixelShader.SetConstantBuffer(1, _shadowConstantBuffer);
                }

                // ── Bind shadow map texture at t0 (pixel shader) ──
                if (_shadowMap != null && _shadowMap.ShadowSrv != null)
                {
                    context.PixelShader.SetShaderResource(0, _shadowMap.ShadowSrv);
                }

                context.OutputMerger.SetDepthStencilState(_deviceManager.DepthStencilState);
                context.Rasterizer.State = _deviceManager.RasterizerState;

                                                                                // ── Draw grid (always visible) ─────────────────────────────────
                DrawObject(context, _grid!);

                // ── Draw model ─────────────────────────────────────────────────
                if (_currentModel != null)
                    DrawObject(context, _currentModel);

                                // ── Unbind shadow map resources (good practice before present) ──
                                context.PixelShader.SetShaderResource(0, null);

                // ── Present ────────────────────────────────────────────────────
                _surface.Present();

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

                Thread.Sleep(1);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SharpDXException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Render error: {ex.Message}");
                Thread.Sleep(16);
            }
        }
    }

    public void LoadModel(string filePath)
    {
        _currentModel?.Dispose();
        _currentModel = Model.Load(_deviceManager.Device, filePath);
    }

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
        _currentModel?.Dispose();
        _grid?.Dispose();
        _shadowMap?.Dispose();
        _shadowVertexShader?.Dispose();
        _shadowPixelShader?.Dispose();
        _shadowConstantBuffer?.Dispose();
        _worldMatrixBuffer?.Dispose();
        _inputLayout?.Dispose();
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputSignature?.Dispose();
        _viewProjectionBuffer?.Dispose();
        _deviceManager.Dispose();
        _surface?.Dispose();
    }
}
