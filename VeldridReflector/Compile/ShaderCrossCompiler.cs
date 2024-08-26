using Veldrid;

using SPIRVCross.NET;
using SPIRVCross.NET.GLSL;
using SPIRVCross.NET.HLSL;
using SPIRVCross.NET.MSL;

using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable

namespace Application
{
    public static partial class ShaderCompiler
    {
        // HLSL semantics are treated as identifiers for the program to know what inputs go where.
        // semantics and their formats are enforced by the reflector to provide guarantee that at least 1 buffer will be bound to a known input.
        static readonly Dictionary<string, VertexElementFormat> semantics = new()
        {
            { "POSITION0", VertexElementFormat.Float4 },
            { "TEXCOORD0", VertexElementFormat.Float4 },
            { "NORMAL0", VertexElementFormat.Float4 },
            { "COLOR0", VertexElementFormat.Byte4_Norm },
        };

        public static BindableShaderDescription Reflect(Context context, ShaderDescription[] compiledSPIRV)
        {
            StageInput[] vertexInputs = [];

            List<Uniform> uniforms = [];
            List<ShaderStages> stages = [];

            for (int i = 0; i < compiledSPIRV.Length; i++)
            {
                ShaderDescription shader = compiledSPIRV[i];

                ParsedIR IR = context.ParseSpirv(shader.ShaderBytes);

                var compiler = context.CreateReflector(IR);

                var resources = compiler.CreateShaderResources();

                if (shader.Stage == ShaderStages.Vertex)
                    vertexInputs = VertexInputReflector.GetStageInputs(compiler, resources, semantics.TryGetValue);

                var stageUniforms = UniformReflector.GetUniforms(compiler, resources);

                MergeUniforms(uniforms, stages, stageUniforms, shader.Stage);
            }
            
            return new(vertexInputs, uniforms.ToArray(), stages.ToArray());
        }


        public static ShaderDescription[] CrossCompile(Context context, GraphicsBackend backend, ShaderDescription[] compiledSPIRV)
        {
            ShaderDescription[] result = new ShaderDescription[compiledSPIRV.Length];

            for (int i = 0; i < result.Length; i++)
            {
                ShaderDescription shader = compiledSPIRV[i];
                result[i] = CrossCompile(context, backend, shader.Stage, shader.EntryPoint, shader.ShaderBytes);
            }
            
            return result;
        }


        private static ShaderDescription CrossCompile( 
            Context context,
            GraphicsBackend backend, 
            ShaderStages stage, 
            string entrypoint, 
            byte[] sourceSPIRV)
        {
            ShaderDescription shader = new();

            shader.Stage = stage;
            shader.EntryPoint = entrypoint;

            ParsedIR IR = context.ParseSpirv(sourceSPIRV);

            shader.ShaderBytes = backend switch
            {
                GraphicsBackend.Direct3D11 => CompileHLSL(context, IR),
                GraphicsBackend.Metal => CompileMSL(context, IR),
                GraphicsBackend.OpenGL => CompileGLSL(context, IR, false),
                GraphicsBackend.OpenGLES => CompileGLSL(context, IR, true),
                _ => sourceSPIRV,
            };

            return shader;
        }
        

        private static byte[] CompileHLSL(Context context, ParsedIR IR)
        {
            HLSLCrossCompiler compiler = context.CreateHLSLCompiler(IR);

            compiler.hlslOptions.shaderModel = 50;
            compiler.hlslOptions.pointSizeCompat = true;

            return Encoding.ASCII.GetBytes(compiler.Compile());
        }


        private static byte[] CompileMSL(Context context, ParsedIR IR)
        {
            MSLCrossCompiler compiler = context.CreateMSLCompiler(IR);

            return Encoding.UTF8.GetBytes(compiler.Compile());
        }


        private static byte[] CompileGLSL(Context context, ParsedIR IR, bool es, bool supportsCompute = true)
        {
            GLSLCrossCompiler compiler = context.CreateGLSLCompiler(IR);

            compiler.glslOptions.ES = es;

            if (supportsCompute)
                compiler.glslOptions.version = !es ? 430u : 310u;
            else
                compiler.glslOptions.version = !es ? 330u : 300u;

            compiler.BuildDummySamplerForCombinedImages(out _);
            compiler.BuildCombinedImageSamplers();

            foreach (var res in compiler.GetCombinedImageSamplers())
                compiler.SetName(res.combined_id, compiler.GetName(res.image_id));

            return Encoding.ASCII.GetBytes(compiler.Compile());
        }


        private static void MergeUniforms(List<Uniform> uniforms, List<ShaderStages> stages, Uniform[] other, ShaderStages stage)
        {
            foreach (var ub in other)
            {
                int match = uniforms.FindIndex(x => x.IsEqual(ub));

                if (match == -1)  
                {            
                    // No match, add the uniform
                    uniforms.Add(ub);
                    stages.Add(stage);
                }
                else
                {
                    // Uniform already exists, OR the shader stage.
                    stages[match] |= stage;
                }
            }
        }
    }
}