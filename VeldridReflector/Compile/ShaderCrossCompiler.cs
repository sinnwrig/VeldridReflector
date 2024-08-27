using Veldrid;

using SPIRVCross.NET;
using SPIRVCross.NET.GLSL;
using SPIRVCross.NET.HLSL;
using SPIRVCross.NET.MSL;

using System.Text;

#pragma warning disable

namespace Application
{
    public struct ReflectedResourceInfo
    {
        public StageInput[] vertexInputs;
        public Uniform[] uniforms;
        public ShaderStages[] stages;
    }

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

        public static ReflectedResourceInfo Reflect(Context context, ShaderDescription[] compiledSPIRV)
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
            
            return new ReflectedResourceInfo() { vertexInputs = vertexInputs, uniforms = uniforms.ToArray(), stages = stages.ToArray() };
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

            MakeIncrementalBindings(compiler, false);

            string c = compiler.Compile();

            Console.WriteLine(c);

            return Encoding.ASCII.GetBytes(c);
        }


        private static byte[] CompileMSL(Context context, ParsedIR IR)
        {
            MSLCrossCompiler compiler = context.CreateMSLCompiler(IR);

            MakeIncrementalBindings(compiler, true);

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

            var resources = compiler.CreateShaderResources();

            // Removes annoying 'type_' prefix
            foreach (var res in resources.UniformBuffers)
                compiler.SetName(res.base_type_id, compiler.GetName(res.id));

            string c = compiler.Compile();

            Console.WriteLine(c);

            return Encoding.ASCII.GetBytes(c);
        }
        

        private static uint GetResourceIndex(
            bool targetMSL,
            ResourceKind resourceKind,
            ref uint bufferIndex,
            ref uint textureIndex,
            ref uint uavIndex,
            ref uint samplerIndex)
        {
            switch (resourceKind)
            {
                case ResourceKind.UniformBuffer:
                    return bufferIndex++;
                
                case ResourceKind.StructuredBufferReadWrite:
                    if (targetMSL)
                        return bufferIndex++;
                    else
                        return uavIndex++;

                case ResourceKind.TextureReadWrite:
                    if (targetMSL)
                        return textureIndex++;
        
                    return uavIndex++;

                case ResourceKind.TextureReadOnly:
                    return textureIndex++;

                case ResourceKind.StructuredBufferReadOnly:
                    if (targetMSL)
                        return bufferIndex++;
                    
                    return textureIndex++;
            }
    
            return samplerIndex++;
        }


        private static void MakeIncrementalBindings(Reflector reflector, bool isMSL = false)
        {
            var resources = reflector.CreateShaderResources();

            uint b = 0;
            uint t = 0;
            uint uav = 0;
            uint s = 0;

            foreach (var v in resources.UniformBuffers)
                reflector.SetDecoration(v.id, Decoration.Binding, GetResourceIndex(isMSL, ResourceKind.UniformBuffer, ref b, ref t, ref uav, ref s));
            
            foreach (var v in resources.SeparateImages)
                reflector.SetDecoration(v.id, Decoration.Binding, GetResourceIndex(isMSL, ResourceKind.TextureReadOnly, ref b, ref t, ref uav, ref s));

            foreach (var v in resources.SeparateSamplers)
                reflector.SetDecoration(v.id, Decoration.Binding, GetResourceIndex(isMSL, ResourceKind.Sampler, ref b, ref t, ref uav, ref s));

            foreach (var v in resources.StorageImages)
                reflector.SetDecoration(v.id, Decoration.Binding, GetResourceIndex(isMSL, ResourceKind.TextureReadWrite, ref b, ref t, ref uav, ref s));

            foreach (var v in resources.StorageBuffers)
            {
                if (reflector.HasDecoration(v.id, Decoration.NonWritable))
                    reflector.SetDecoration(v.id, Decoration.Binding, GetResourceIndex(isMSL, ResourceKind.StructuredBufferReadOnly, ref b, ref t, ref uav, ref s));
                else
                    reflector.SetDecoration(v.id, Decoration.Binding, GetResourceIndex(isMSL, ResourceKind.StructuredBufferReadWrite, ref b, ref t, ref uav, ref s));
            }
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