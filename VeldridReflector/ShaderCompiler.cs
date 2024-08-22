using System.Numerics;
using Veldrid;
using DirectXShaderCompiler.NET;
using SPIRVCross.NET;
using System.Text.RegularExpressions;

#pragma warning disable

namespace Application
{
    public static partial class ShaderCompiler
    {
        // HLSL semantics are treated as identifiers for the program to know what inputs go where.
        // A shader author can make any semantic they want and bind any element to it, 
        // but we can pass in some defaults that have an enforced format that meshes will upload.
        static readonly Dictionary<string, VertexElementFormat> DefaultSemantics = new()
        {
            { "COLOR", VertexElementFormat.Float4 },
            { "NORMAL", VertexElementFormat.Float4 },
            { "POSITION", VertexElementFormat.Float4 },
            { "TANGENT", VertexElementFormat.Float4 },
            { "TEXCOORD", VertexElementFormat.Float4 },
        };

        private static ShaderType StageToType(ShaderStages stages)
        {
            return stages switch 
            {
                ShaderStages.Vertex => ShaderType.Vertex,
                ShaderStages.Geometry => ShaderType.Geometry,
                ShaderStages.TessellationControl => ShaderType.Hull,
                ShaderStages.TessellationEvaluation => ShaderType.Domain,
                ShaderStages.Fragment => ShaderType.Fragment
            };
        }

        public static (ShaderStages, byte[])[] Compile(string code, (string, ShaderStages)[] entrypoints, GraphicsBackend backend)
        {
            byte[][] compiledSPIRV = new byte[entrypoints.Length][];

            for (int i = 0; i < entrypoints.Length; i++)
            {
                DirectXShaderCompiler.NET.CompilerOptions options = new(StageToType(entrypoints[i].Item2).ToProfile(6, 0));

                options.generateAsSpirV = true;
                options.useOpenGLMemoryLayout = true;
                options.entryPoint = entrypoints[i].Item1;
                options.entrypointName = "main"; // Ensure 'main' entrypoint for OpenGL compatibility.

                if (entrypoints[i].Item2 == ShaderStages.Vertex)
                    options.invertY = backend == GraphicsBackend.Vulkan;

                CompilationResult result = DirectXShaderCompiler.NET.ShaderCompiler.Compile(code, options, NoInclude);

                if (result.compilationErrors != null)
                    throw new Exception($"Compilation errors encountered:\n\n{result.compilationErrors}");

                compiledSPIRV[i] = result.objectBytes;
            }

            return compiledSPIRV.Zip(entrypoints, (x, y) => (y.Item2, x)).ToArray();
        }

        static string NoInclude(string file)
        {
            return $"// Including {file}";
        }
    }
}