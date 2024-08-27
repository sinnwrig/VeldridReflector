using Veldrid;

using System.Text.RegularExpressions;

#pragma warning disable

namespace Application
{
    public partial class BindableShader
    {
        public readonly BindableShaderDescription description;
        public readonly ShaderSetDescription shaderSet;
        public readonly ResourceLayout resourceLayout;

        private Dictionary<string, uint> semanticLookup;
        private Dictionary<string, ulong> uniformLookup;

        private int bufferCount;

        public Uniform[] Uniforms => description.Uniforms;


        [GeneratedRegex(@"\d+$")]
        private static partial Regex TrailingInteger();


        public BindableShader(BindableShaderDescription description, ShaderDescription[] shaderDescriptions, GraphicsDevice device)
        {
            this.description = description;
            
            // Create shader set description
            Shader[] shaders = new Shader[shaderDescriptions.Length];

            this.semanticLookup = new();

            for (int shaderIndex = 0; shaderIndex < shaders.Length; shaderIndex++)
                shaders[shaderIndex] = device.ResourceFactory.CreateShader(shaderDescriptions[shaderIndex]);

            VertexLayoutDescription[] vertexLayouts = new VertexLayoutDescription[description.VertexInputs.Length];

            for (int inputIndex = 0; inputIndex < vertexLayouts.Length; inputIndex++)
            {
                StageInput input = description.VertexInputs[inputIndex];

                // Add in_var_ to match reflected name in SPIRV-Cross generated GLSL.
                vertexLayouts[inputIndex] = new VertexLayoutDescription(
                    new VertexElementDescription("in_var_" + input.semantic, input.format, VertexElementSemantic.TextureCoordinate));

                semanticLookup[input.semantic] = (uint)inputIndex;

                // If the last char of the semantic is a single '0', add a non-indexed version of the semantic to the lookup.
                if (input.semantic.Length >= 2 && 
                    input.semantic[input.semantic.Length - 1] == '0' && 
                    !char.IsNumber(input.semantic[input.semantic.Length - 2]))
                {
                    semanticLookup[input.semantic.Substring(0, input.semantic.Length - 1)] = (uint)inputIndex;
                }
            }

            foreach (var k in semanticLookup.Keys)
                Console.WriteLine(k);

            this.shaderSet = new ShaderSetDescription(vertexLayouts, shaders);

            // Create resource layout and uniform lookups
            this.uniformLookup = new();

            ResourceLayoutDescription layoutDescription = new ResourceLayoutDescription(
                new ResourceLayoutElementDescription[Uniforms.Length]);

            for (ushort uniformIndex = 0; uniformIndex < Uniforms.Length; uniformIndex++)
            {
                Uniform uniform = Uniforms[uniformIndex];
                ShaderStages stages = description.UniformStages[uniformIndex];

                layoutDescription.Elements[uniformIndex] = 
                    new ResourceLayoutElementDescription(GetGLSLName(uniform.name), uniform.kind, stages);

                uniformLookup[uniform.name] = Pack(uniformIndex, -1, -1);

                if (uniform.kind != ResourceKind.UniformBuffer)
                    continue;
                
                uniformLookup[uniform.name] = Pack(uniformIndex, (short)bufferCount, -1);

                for (short member = 0; member < uniform.members.Length; member++)
                    uniformLookup[uniform.members[member].name] = Pack(uniformIndex, (short)bufferCount, member);

                bufferCount++;
            }

            this.resourceLayout = device.ResourceFactory.CreateResourceLayout(layoutDescription);
        }


        private BindableResource GetBindableResource(GraphicsDevice device, Uniform uniform, out DeviceBuffer? buffer)
        {
            buffer = null;

            if (uniform.kind == ResourceKind.TextureReadOnly)
                return TextureUtils.GetEmptyTexture(device);

            if (uniform.kind == ResourceKind.TextureReadWrite)
                return TextureUtils.GetEmptyRWTexture(device);
            
            if (uniform.kind == ResourceKind.Sampler)
                return device.PointSampler;

            if (uniform.kind == ResourceKind.StructuredBufferReadOnly)
                return BufferUtils.GetEmptyBuffer(device);
            
            if (uniform.kind == ResourceKind.StructuredBufferReadWrite)
                return BufferUtils.GetEmptyRWBuffer(device);

            uint bufferSize = (uint)Math.Ceiling((uniform.size / (double)16)) * 16;
            buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(bufferSize, BufferUsage.UniformBuffer | BufferUsage.DynamicWrite));

            return buffer;
        }


        public BindableResourceSet CreateResources(GraphicsDevice device)
        {
            DeviceBuffer[] boundBuffers = new DeviceBuffer[bufferCount];
            BindableResource[] boundResources = new BindableResource[Uniforms.Length];
            byte[][] intermediateBuffers = new byte[bufferCount][];

            for (int i = 0, b = 0; i < Uniforms.Length; i++)
            {
                boundResources[i] = GetBindableResource(device, Uniforms[i], out DeviceBuffer? buffer);

                if (buffer != null)
                {
                    boundBuffers[b] = buffer;
                    intermediateBuffers[b] = new byte[buffer.SizeInBytes];

                    b++; 
                }
            }

            ResourceSetDescription setDescription = new ResourceSetDescription(resourceLayout, boundResources);
            BindableResourceSet resources = new BindableResourceSet(this, setDescription, boundBuffers, intermediateBuffers);

            return resources;
        }


        public bool GetUniform(string name, out int uniform, out int buffer, out int member)
        {
            uniform = -1;
            buffer = -1;
            member = -1;

            if (uniformLookup.TryGetValue(name, out ulong packed))
            {
                Unpack(packed, out ushort u, out short b, out member);
                uniform = (int)u;
                buffer = (int)b;
                return true;
            }

            return false;
        }


        public void BindVertexBuffer(CommandList list, string semantic, DeviceBuffer buffer, uint offset = 0)
        {
            if (semanticLookup.TryGetValue(semantic, out uint location))
                list.SetVertexBuffer(location, buffer, offset);
        }


        // This is so fucking stupid. 
        // On kOt's machine, the following GLSL:
        // "uniform type_SomeBuf { mat4 someProp; } _SomeBuf;"
        // must be bound using 'type_SomeBuf' instead of the technically correct '_SomeBuf' because stupid vendor-specific compilers.
        private static string GetGLSLName(string name)
        {
            if (name[0] == '_')
                return "type" + name.Replace("$", "");
            
            return "type_" + name.Replace("$", "");
        }

        public static ulong Pack(ushort a, short b, int c)
            => ((ulong)(ushort)a << 48) | ((ulong)(ushort)b << 32) | (uint)c;

        public static void Unpack(ulong packed, out ushort a, out short b, out int c)    
            => (a, b, c) = ((ushort)(packed >> 48), (short)(packed >> 32), (int)(packed & uint.MaxValue));
    }
}