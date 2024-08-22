using System.Numerics;
using Veldrid;
using DirectXShaderCompiler.NET;
using SPIRVCross.NET;
using System.Text.RegularExpressions;

#pragma warning disable

namespace Application
{
    public class BindableResourceDescription
    {
        public ShaderSetDescription shaderSet;
        public ResourceLayoutDescription layoutDescription;
        public ResourceLayout layout;


        private Dictionary<StageInput, uint> inputs;
        public Uniform[] uniforms;
        private Dictionary<PropertyID, ulong> uniformLookup;


        public BindableResourceDescription(
            GraphicsDevice device, 
            Dictionary<StageInput, uint> inputs, 
            (Uniform, ShaderStages)[] uniformStages, 
            (ShaderStages, byte[])[] compiledShaders)
        {
            this.shaderSet = new ShaderSetDescription(
                inputs.Select(x => CreateVertexLayout(x.Key)).ToArray(),
                compiledShaders.Select(x => CreateShader(x, device)).ToArray()
            );

            this.layoutDescription = new ResourceLayoutDescription(
                uniformStages.Select(CreateElementDescription).ToArray());

            this.layout = device.ResourceFactory.CreateResourceLayout(this.layoutDescription);

            this.inputs = inputs;
            this.uniforms = uniformStages.Select(x => x.Item1).ToArray();
            this.uniformLookup = new(uniformStages.Length);

            for (uint i = 0; i < uniformStages.Length; i++)
            {
                Uniform uniform = uniformStages[i].Item1;

                if (uniform.kind != ResourceKind.UniformBuffer)
                {
                    uniformLookup.Add(uniform.identifier, Pack(i, 0));
                    continue;
                }

                for (ushort j = 0; j < uniform.members.Length; j++)
                {
                    ConstantBufferMember member = uniform.members[j];
                    uniformLookup.Add(member.identifier, Pack(i, j));
                }
            }
        }


        public int GetInputLocation(StageInput input)
            => inputs.TryGetValue(input, out uint val) ? (int)val : -1;

        public bool GetUniform(PropertyID ID, out Uniform? uniform, out ConstantBufferMember member)
        {
            uniform = null;
            member = default;
            if (!uniformLookup.TryGetValue(ID, out ulong value))
                return false;

            (uint uniformIndex, uint memberIndex) = Unpack(value);

            uniform = uniforms[uniformIndex];

            if (uniform.kind == ResourceKind.UniformBuffer)
                member = uniform.members[memberIndex];

            return true;
        }

        private static ulong Pack(uint a, uint b)
            => ((ulong)a << 32) | b;

        private static (uint, uint) Unpack(ulong packed)    
            => ((uint)(packed >> 32), (uint)(packed & uint.MaxValue));

        private static VertexLayoutDescription CreateVertexLayout(StageInput input)
        {
            return new VertexLayoutDescription(
                new VertexElementDescription(input.fullName, input.format, VertexElementSemantic.TextureCoordinate));
        }

        private static Shader CreateShader((ShaderStages, byte[]) compiledShader, GraphicsDevice device)
        {
            ShaderDescription description = new(compiledShader.Item1, compiledShader.Item2, "main");
            return device.ResourceFactory.CreateShader(description);
        }

        private static ResourceLayoutElementDescription CreateElementDescription((Uniform, ShaderStages) uniformStage)
        {
            ResourceLayoutElementDescription element = new();

            element.Kind = uniformStage.Item1.kind;
            element.Name = uniformStage.Item1.name;
            element.Stages = uniformStage.Item2;

            return element;
        }
    }


    public class BindableResources
    {
        private static Texture _emptyTex;
        private static Texture GetEmptyTexture(GraphicsDevice device)
        {
            if (_emptyTex != null)
                return _emptyTex;

            TextureDescription desc = new();
            desc.Width = 1;
            desc.Height = 1;
            desc.Depth = 1;
            desc.ArrayLayers = 1;
            desc.Format = PixelFormat.R8_G8_B8_A8_UNorm;
            desc.MipLevels = 1;
            desc.SampleCount = TextureSampleCount.Count1;
            desc.Type = TextureType.Texture2D;
            desc.Usage = TextureUsage.Sampled;
            _emptyTex = device.ResourceFactory.CreateTexture(desc);

            return _emptyTex;
        }

        private static Texture _emptyRWTex;
        private static Texture GetEmptyRWTexture(GraphicsDevice device)
        {
            if (_emptyRWTex != null)
                return _emptyRWTex;

            TextureDescription desc = new();
            desc.Width = 1;
            desc.Height = 1;
            desc.Depth = 1;
            desc.ArrayLayers = 1;
            desc.Format = PixelFormat.R8_G8_B8_A8_UNorm;
            desc.MipLevels = 1;
            desc.SampleCount = TextureSampleCount.Count1;
            desc.Type = TextureType.Texture2D;
            desc.Usage = TextureUsage.Storage;
            _emptyRWTex = device.ResourceFactory.CreateTexture(desc);

            return _emptyRWTex;
        }


        private static DeviceBuffer _emptyBuffer;
        private static DeviceBuffer GetEmptyBuffer(GraphicsDevice device)
        {
            if (_emptyBuffer != null)
                return _emptyBuffer;

            _emptyBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(1, BufferUsage.StructuredBufferReadOnly));
            
            return _emptyBuffer;
        }

        private static DeviceBuffer _emptyRWBuffer;
        private static DeviceBuffer GetEmptyRWBuffer(GraphicsDevice device)
        {
            if (_emptyRWBuffer != null)
                return _emptyRWBuffer;

            _emptyRWBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(1, BufferUsage.StructuredBufferReadWrite));
            
            return _emptyRWBuffer;
        }

        public readonly BindableResourceDescription description;
        public readonly ResourceSetDescription setDescription;

        public readonly Dictionary<PropertyID, DeviceBuffer> buffers;


        public BindableResources(BindableResourceDescription description, GraphicsDevice device)
        {
            this.buffers = new();
            this.description = description;

            this.setDescription = new ResourceSetDescription(description.layout,
                description.uniforms.Select(x => GetBindableResource(x, device)).ToArray()
            );
        }

        private BindableResource GetBindableResource(Uniform uniform, GraphicsDevice device)
        {
            if (uniform.kind.HasFlag(ResourceKind.TextureReadOnly))
                return GetEmptyTexture(device);

            if (uniform.kind.HasFlag(ResourceKind.TextureReadWrite))
                return GetEmptyRWTexture(device);
            
            if (uniform.kind.HasFlag(ResourceKind.Sampler))
                return device.PointSampler;

            if (uniform.kind.HasFlag(ResourceKind.StructuredBufferReadOnly))
                return GetEmptyBuffer(device);
            
            if (uniform.kind.HasFlag(ResourceKind.StructuredBufferReadWrite))
                return GetEmptyRWBuffer(device);

            var buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(uniform.size, BufferUsage.UniformBuffer | BufferUsage.DynamicWrite));
                
            buffers.Add(uniform.identifier, buffer);

            return buffer;
        }

        public ResourceSet CreateResourceSet(GraphicsDevice device)
        {
            return device.ResourceFactory.CreateResourceSet(setDescription);
        }

        public void SetTexture(CommandList list, PropertyID ID, Texture value)
        {
            if (value == null || 
                (!value.Usage.HasFlag(TextureUsage.Sampled) &&
                !value.Usage.HasFlag(TextureUsage.Storage)))
                return;

            if (description.GetUniform(ID, out Uniform? uni, out _))
            {
                if (uni.kind != ResourceKind.TextureReadOnly && uni.kind != ResourceKind.TextureReadWrite)
                    return;

                if (!value.Usage.HasFlag(TextureUsage.Storage) && uni.kind == ResourceKind.TextureReadWrite)
                    return;
                
                setDescription.BoundResources[uni.binding] = value;
            }
        }

        public void SetTexture(CommandList list, PropertyID ID, TextureView value)
        {
            if (value == null || 
                (!value.Target.Usage.HasFlag(TextureUsage.Sampled) &&
                !value.Target.Usage.HasFlag(TextureUsage.Storage)))
                return;

            if (description.GetUniform(ID, out Uniform? uni, out _))
            {
                if (uni.kind != ResourceKind.TextureReadOnly && uni.kind != ResourceKind.TextureReadWrite)
                    return;
                
                if (!value.Target.Usage.HasFlag(TextureUsage.Storage) && uni.kind == ResourceKind.TextureReadWrite)
                    return;
                
                setDescription.BoundResources[uni.binding] = value;
            }
        }

        public void SetSampler(CommandList list, PropertyID ID, Sampler value)
        {
            if (value == null)
                return;

            if (description.GetUniform(ID, out Uniform? uni, out _))
            {
                if (uni.kind != ResourceKind.Sampler)
                    return;
                
                setDescription.BoundResources[uni.binding] = value;
            }
        }

        public void SetFloat(CommandList list, PropertyID ID, float value)
        {
            UploadData(list, ID, ref value, ValueType.Float, sizeof(float));
        }

        public void SetInt(CommandList list, PropertyID ID, int value)
        {
            UploadData(list, ID, ref value, ValueType.Int, sizeof(int));
        }

        public void SetInt(CommandList list, PropertyID ID, uint value)
        {
            UploadData(list, ID, ref value, ValueType.UInt, sizeof(uint));
        }

        public void SetVector(CommandList list, PropertyID ID, Vector4 value)
        {
            UploadData(list, ID, ref value, ValueType.Float, sizeof(float) * 4);
        }
        
        public unsafe void SetMatrix(CommandList list, PropertyID ID, Matrix4x4 value)
        {
            UploadData(list, ID, ref value, ValueType.Float, sizeof(float) * 4 * 4);
        }

        public unsafe void SetFloatArray(CommandList list, PropertyID ID, float[] values)
        {
            if (values == null)
                return;

            fixed (float* valuesPtr = values)
                UploadData(list, ID, (nint)valuesPtr, ValueType.Float, sizeof(float) * values.Length);
        }

        public unsafe void SetIntArray(CommandList list, PropertyID ID, int[] values)
        {
            if (values == null)
                return;
            
            fixed (int* valuesPtr = values)
                UploadData(list, ID, (nint)valuesPtr, ValueType.Int, sizeof(int) * values.Length);
        }

        public unsafe void SetVectorArray(CommandList list, PropertyID ID, Vector4[] values)
        {
            if (values == null)
                return;

            fixed (Vector4* valuesPtr = values)
                UploadData(list, ID, (nint)valuesPtr, ValueType.Float, sizeof(float) * 4 * values.Length);
        }

        public unsafe void SetMatrixArray(CommandList list, PropertyID ID, Matrix4x4[] values)
        {
            if (values == null)
                return;

            fixed (Matrix4x4* valuesPtr = values)
                UploadData(list, ID, (nint)valuesPtr, ValueType.Float, sizeof(float) * 4 * 4 * values.Length);
        }

        private unsafe void UploadData(CommandList list, PropertyID ID, nint dataPtr, ValueType type, int maxSize)
        {
            if (description.GetUniform(ID, out Uniform? uni, out ConstantBufferMember member))
            {
                if (uni.kind != ResourceKind.UniformBuffer || member.type != type)
                    return;

                list.UpdateBuffer(buffers[uni.identifier], member.bufferOffsetInBytes, dataPtr, 
                (uint)Math.Min(member.size, maxSize));  
            }
        }

        private unsafe void UploadData<T>(CommandList list, PropertyID ID, ref T data, ValueType type, int maxSize) where T : unmanaged
        {
            if (description.GetUniform(ID, out Uniform? uni, out ConstantBufferMember member))
            {
                if (uni.kind != ResourceKind.UniformBuffer || member.type != type)
                    return;

                list.UpdateBuffer(buffers[uni.identifier], member.bufferOffsetInBytes, ref data, 
                (uint)Math.Min(member.size, maxSize));  
            }
        }

        public void SetBuffer(CommandList list, PropertyID ID, DeviceBuffer value)
        {
            if (value == null ||
                (!value.Usage.HasFlag(BufferUsage.StructuredBufferReadOnly) && 
                !value.Usage.HasFlag(BufferUsage.StructuredBufferReadWrite)))
                return;

            if (description.GetUniform(ID, out Uniform? uni, out _))
            {
                if (uni.kind != ResourceKind.StructuredBufferReadOnly && uni.kind != ResourceKind.StructuredBufferReadWrite)
                    return;
                
                if (!value.Usage.HasFlag(BufferUsage.StructuredBufferReadWrite) && uni.kind == ResourceKind.StructuredBufferReadWrite)
                    return;
                
                setDescription.BoundResources[uni.binding] = value;
            }
        }
    }
}