using System.IO;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;

namespace ModelViewer.Rendering;

/// <summary>
/// Result of a shader compilation and load operation.
/// Holds all Direct3D shader pipeline resources needed for rendering.
/// </summary>
public sealed class CompiledShaders : IDisposable
{
    public VertexShader VertexShader { get; }
    public PixelShader PixelShader { get; }
    public InputLayout InputLayout { get; }
    private readonly ShaderSignature _inputSignature;

    public CompiledShaders(
        VertexShader vertexShader,
        PixelShader pixelShader,
        InputLayout inputLayout,
        ShaderSignature inputSignature)
    {
        VertexShader = vertexShader;
        PixelShader = pixelShader;
        InputLayout = inputLayout;
        _inputSignature = inputSignature;
    }

    public void Dispose()
    {
        VertexShader.Dispose();
        PixelShader.Dispose();
        InputLayout.Dispose();
        _inputSignature.Dispose();
    }
}

/// <summary>
/// Static helper that compiles HLSL shader files and creates the corresponding
/// Direct3D 11 pipeline resources (vertex shader, pixel shader, input layout).
/// </summary>
public static class ShaderCompiler
{
    /// <summary>
    /// Default input layout for Position / Normal / Texture coordinate vertices.
    /// </summary>
    private static readonly InputElement[] DefaultInputElements =
    {
        new("Position", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0),
        new("Normal", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0),
        new("TexCoord", 0, SharpDX.DXGI.Format.R32G32_Float, 0),
    };

    /// <summary>
    /// Compiles a single vertex shader from an HLSL file.
    /// </summary>
    /// <param name="device">The Direct3D device.</param>
    /// <param name="filePath">Path to the .hlsl file.</param>
    /// <param name="entryPoint">Shader entry point function name (default: "VSMain").</param>
    /// <param name="profile">Shader profile string (default: "vs_4_0").</param>
    /// <returns>The compiled vertex shader and its input signature.</returns>
    public static (VertexShader Shader, ShaderSignature InputSignature) CompileVertexShader(
        Device device, string filePath, string entryPoint = "VSMain", string profile = "vs_4_0")
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Vertex shader file not found: {filePath}");

        ShaderBytecode blob;
        try
        {
            blob = ShaderBytecode.CompileFromFile(filePath, entryPoint, profile);
        }
        catch (SharpDXException ex)
        {
            throw new InvalidOperationException(
                $"Vertex shader compilation failed ({filePath}):\n\n{ex.Message}", ex);
        }

        var inputSignature = ShaderSignature.GetInputSignature(blob);
        var vertexShader = new VertexShader(device, blob);

        return (vertexShader, inputSignature);
    }

    /// <summary>
    /// Compiles a single pixel shader from an HLSL file.
    /// </summary>
    /// <param name="device">The Direct3D device.</param>
    /// <param name="filePath">Path to the .hlsl file.</param>
    /// <param name="entryPoint">Shader entry point function name (default: "PSMain").</param>
    /// <param name="profile">Shader profile string (default: "ps_4_0").</param>
    /// <returns>The compiled pixel shader.</returns>
    public static PixelShader CompilePixelShader(
        Device device, string filePath, string entryPoint = "PSMain", string profile = "ps_4_0")
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Pixel shader file not found: {filePath}");

        ShaderBytecode blob;
        try
        {
            blob = ShaderBytecode.CompileFromFile(filePath, entryPoint, profile);
        }
        catch (SharpDXException ex)
        {
            throw new InvalidOperationException(
                $"Pixel shader compilation failed ({filePath}):\n\n{ex.Message}", ex);
        }

        return new PixelShader(device, blob);
    }

    /// <summary>
    /// Compiles both vertex and pixel shaders and creates a matching input layout.
    /// </summary>
    /// <param name="device">The Direct3D device.</param>
    /// <param name="vsFilePath">Path to the vertex shader .hlsl file.</param>
    /// <param name="psFilePath">Path to the pixel shader .hlsl file.</param>
    /// <param name="inputElements">
    /// Input element descriptors for the layout. Defaults to Position/Normal/TexCoord.
    /// </param>
    /// <returns>A <see cref="CompiledShaders"/> instance with all pipeline resources.</returns>
    public static CompiledShaders CompileAndLoad(
        Device device, string vsFilePath, string psFilePath,
        InputElement[]? inputElements = null)
    {
        var elements = inputElements ?? DefaultInputElements;

        var (vertexShader, inputSignature) = CompileVertexShader(device, vsFilePath);
        var pixelShader = CompilePixelShader(device, psFilePath);
        var inputLayout = new InputLayout(device, inputSignature, elements);

        return new CompiledShaders(vertexShader, pixelShader, inputLayout, inputSignature);
    }

    /// <summary>
    /// Resolves a shader file path relative to the application base directory under the "Shaders" folder.
    /// </summary>
    public static string ResolveShaderPath(string fileName)
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", fileName);
    }
}
