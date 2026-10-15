// Scratch tool: compiles the SMAA HLSL wrappers through SharpDX.D3DCompiler,
// i.e. exactly the code path ShaderCompiler.cs uses, and prints any diagnostics.
// Not part of the application - delete this folder once the shaders are verified.

using System;
using System.IO;
using SharpDX;
using SharpDX.D3DCompiler;

class Probe
{
    static void Main()
    {
        File.WriteAllText(@"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\probe2.txt",
            "None=" + (int)SharpDX.D3DCompiler.ShaderFlags.None +
            " SkipValidation=" + (int)SharpDX.D3DCompiler.ShaderFlags.SkipValidation + "\n");

        var shaderDir = @"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders";

        var shaders = new[]
        {
            new[] { "VertexShaderSMAAEdge.hlsl", "VSMain", "vs_4_0" },
            new[] { "PixelShaderSMAAEdge.hlsl", "PSMain", "ps_4_0" },
            new[] { "VertexShaderSMAAWeights.hlsl", "VSMain", "vs_4_0" },
            new[] { "PixelShaderSMAAWeights.hlsl", "PSMain", "ps_4_0" },
            new[] { "VertexShaderSMAAResolve.hlsl", "VSMain", "vs_4_0" },
            new[] { "PixelShaderSMAAResolve.hlsl", "PSMain", "ps_4_0" },
        };

        var failures = 0;

        var header = ReadText(Path.Join(shaderDir, "SMAA.hlsl"));

        foreach (var shader in shaders)
        {
            var path = Path.Combine(shaderDir, shader[0]);
            var source = ReadText(path).Replace(@"#include ""SMAA.hlsl""", header);
            try
            {
                using var blob = ShaderBytecode.Compile(source, shader[1], shader[2]);
                Console.WriteLine("{0,-32} {1,-6} OK", shader[0], shader[2]);
            }
            catch (SharpDXException ex)
            {
                failures++;
                Console.WriteLine("{0,-32} {1,-6} FAILED: {2}", shader[0], shader[2], ex.Message);
            }
        }

        Console.WriteLine("{0} of {1} compiled", shaders.Length - failures, shaders.Length);
    }

    static string ReadText(string path)
    {
        return File.ReadAllText(path);
    }
}
