using Veldrid;

using DirectXShaderCompiler.NET;

#pragma warning disable

namespace Application
{
    public static partial class ShaderCompiler
    {
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
        

        public static ShaderDescription[] Compile(string code, (string, ShaderStages)[] entrypoints, bool flipVertexY)
        {
            byte[][] compiledSPIRV = new byte[entrypoints.Length][];

            for (int i = 0; i < entrypoints.Length; i++)
            {
                DirectXShaderCompiler.NET.CompilerOptions options = new(StageToType(entrypoints[i].Item2).ToProfile(6, 0));

                options.generateAsSpirV = true;
                options.useOpenGLMemoryLayout = true;
                options.entryPoint = entrypoints[i].Item1;
                options.entrypointName = "main"; // Ensure 'main' entrypoint for OpenGL compatibility.

                if (entrypoints[i].Item2 == ShaderStages.Vertex && flipVertexY)
                    options.invertY = true;

                CompilationResult result = DirectXShaderCompiler.NET.ShaderCompiler.Compile(code, options, NoInclude);

                if (result.compilationErrors != null)
                    throw new Exception($"Compilation errors encountered:\n\n{result.compilationErrors}");

                compiledSPIRV[i] = result.objectBytes;
            }

            return compiledSPIRV.Zip(entrypoints, (x, y) => new ShaderDescription(y.Item2, x, "main")).ToArray();
        }


        static string NoInclude(string file)
        {
            return $"// Including {file}";
        }
    }
}