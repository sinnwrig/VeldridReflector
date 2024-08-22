using System.Numerics;
using Veldrid;
using DirectXShaderCompiler.NET;
using SPIRVCross.NET;
using System.Text.RegularExpressions;

#pragma warning disable

namespace Application
{
    public static partial class ShaderReflector
    {
        // HLSL semantics are treated as identifiers for the program to know what inputs go where.
        // A shader author can make any semantic they want and bind any element to it, 
        // but we cann enfore default formats for different semantics.
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

        public static BindableResourceDescription Reflect(GraphicsDevice device, (ShaderStages, byte[])[] compiledSPIRV)
        {
            using Context context = new Context();

            Dictionary<StageInput, uint> vertexInputs = new();
            List<(Uniform, ShaderStages)> uniforms = [];

            for (int i = 0; i < compiledSPIRV.Length; i++)
            {
                (ShaderStages stage, byte[] spirv) = compiledSPIRV[i];

                ParsedIR IR = context.ParseSpirv(spirv);
                var compiler = context.CreateReflector(IR);

                var resources = compiler.CreateShaderResources();

                if (stage == ShaderStages.Vertex)
                    vertexInputs = VertexInputReflector.GetStageInputs(compiler, resources, DefaultSemantics);

                var stageUniforms = UniformReflector.GetUniforms(compiler, resources);

                MergeUniforms(uniforms, stageUniforms, stage);
            }

            return new BindableResourceDescription(device, vertexInputs, uniforms.ToArray(), compiledSPIRV);
        }

        public static void MergeUniforms(List<(Uniform, ShaderStages)> uniforms, Uniform[] other, ShaderStages stage)
        {
            List<(Uniform, ShaderStages)> newUniforms = new();

            foreach (var ub in other)
            {
                int uniformMatch = -1;

                for (int i = 0; i < uniforms.Count; i++)
                {
                    if (uniforms[i].Item1.IsEqual(ub))
                    {
                        uniformMatch = i;
                        break;
                    }
                }

                if (uniformMatch == -1)  
                {            
                    // No match, add the uniform
                    newUniforms.Add((ub, stage));
                }
                else
                {
                    var match = uniforms[uniformMatch];

                    // Uniform already exists, add a shader stage.
                    match.Item2 |= stage;
                    
                    uniforms[uniformMatch] = match;
                }
            }

            uniforms.AddRange(newUniforms);
        }



        static string NoInclude(string file)
        {
            return $"// Including {file}";
        }

        static int spaceDepth = 0;
        static void Log(string msg)
        {
            string spaces = "";

            for (int i = 0; i < spaceDepth; i++)
                spaces += "  ";

            msg = spaces + msg.Replace("\n", "\n" + spaces);
            
            Console.WriteLine(msg);
        }
    }
}