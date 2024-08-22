using System.Numerics;
using Veldrid;
using DirectXShaderCompiler.NET;
using SPIRVCross.NET;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Text;

#pragma warning disable

namespace Application
{
    public readonly record struct PropertyID
    {
        private readonly int _internalValue;

        public static explicit operator uint(PropertyID id) => Unsafe.As<PropertyID, uint>(ref id);

        public static explicit operator PropertyID(uint id) => Unsafe.As<uint, PropertyID>(ref id);

        public static implicit operator PropertyID(string propertyName) => new PropertyID(propertyName); 


        public PropertyID(string propertyName)
        {
            _internalValue = HashProperty(propertyName);
        }

        public PropertyID(int propertyID)
        {
            _internalValue = propertyID;
        }

        private static int HashProperty(string propertyName)
        {
            return propertyName.GetHashCode();
        }
    }


    public enum UniformType
    {
        Texture,
        Sampler,
        StorageBuffer,
        ConstantBuffer,
    }

    public enum ValueType
    {
        None = 0,
        Float,
        Int,
        UInt
    }

    public record struct ConstantBufferMember 
    { 
        public PropertyID identifier;

        public uint bufferOffsetInBytes;

        public uint width;
        public uint height;
        public uint size;

        public uint arrayStride;
        public uint matrixStride;

        public ValueType type;
    }

    public class Uniform(string name, uint binding, ResourceKind kind)
    { 
        public readonly ResourceKind kind = kind;
        public readonly string name = name;
        public readonly PropertyID identifier = name;
        public readonly uint binding = binding;

        public uint size;
        public ConstantBufferMember[] members;

        public override string ToString()
        {
            StringBuilder sb = new();

            sb.AppendLine($"{identifier}");
            sb.AppendLine($"  Type: {kind}");
            sb.AppendLine($"  Binding: {binding}");

            if (kind != ResourceKind.UniformBuffer)
                return sb.ToString();

            sb.AppendLine($"  Byte size: {size}");
            sb.AppendLine("  Members:");

            foreach (var member in members)
            {
                sb.AppendLine($"    ID: {member.identifier}");
                sb.AppendLine($"    Type: {member.type}");
                sb.AppendLine($"    Width: {member.width}");
                sb.AppendLine($"    Height: {member.height}");
                sb.AppendLine($"    Buffer offset: {member.bufferOffsetInBytes}");
                sb.AppendLine($"    Size: {member.size}");
                sb.AppendLine($"    Array stride: {member.arrayStride}");
                sb.AppendLine($"    Matrix stride: {member.matrixStride}");
            }

            return sb.ToString();
        }


        public bool IsEqual(Uniform other)
        {
            if (kind != other.kind)
                return false;

            if (kind == ResourceKind.UniformBuffer && size != other.size && !members.SequenceEqual(other.members))
                return false;
            
            return identifier == other.identifier && binding == other.binding;
        }
    }

    public static partial class UniformReflector
    {
        public static Uniform[] GetUniforms(Reflector reflector, Resources resources)
        {
            List<Uniform> uniforms = new();       

            foreach (var res in resources.StorageImages)
                uniforms.Add(new Uniform(res.name, GetBinding(reflector, res.id), ResourceKind.TextureReadWrite));

            foreach (var res in resources.SeparateImages)
                uniforms.Add(new Uniform(res.name, GetBinding(reflector, res.id), ResourceKind.TextureReadOnly));

            foreach (var res in resources.SeparateSamplers)
                uniforms.Add(new Uniform(res.name, GetBinding(reflector, res.id), ResourceKind.Sampler));

            foreach (var res in resources.StorageBuffers)
                uniforms.Add(CreateStorageBuffer(reflector, res));

            // Combined image samplers don't output any names, meaning we don't need to add uniforms for them
            // since the few platforms that care about it (old OpenGL) bind by name, meaning it's useless.
            // foreach (var combinedImage in resources.SampledImages);

            foreach (var res in resources.UniformBuffers)
                uniforms.Add(CreateConstantBuffer(reflector, res));

            uniforms.Sort((x, y) => x.binding.CompareTo(y.binding));

            return uniforms.ToArray();
        }

        static Uniform CreateStorageBuffer(Reflector reflector, ReflectedResource bufferResource)
        {
            uint binding = GetBinding(reflector, bufferResource.id);

            var decoratedType = reflector.GetTypeHandle(bufferResource.type_id);

            if (reflector.HasDecoration(bufferResource.id, Decoration.NonWritable))
                return new Uniform(bufferResource.name, binding, ResourceKind.StructuredBufferReadOnly);
            
            return new Uniform(bufferResource.name, binding, ResourceKind.StructuredBufferReadWrite);
        }

        static Uniform CreateConstantBuffer(Reflector reflector, ReflectedResource bufferResource)
        {
            uint binding = GetBinding(reflector, bufferResource.id);

            List<ConstantBufferMember> members = new();

            var decoratedType = reflector.GetTypeHandle(bufferResource.type_id);
            var baseType = reflector.GetTypeHandle(decoratedType.BaseTypeID);

            if (baseType.BaseType != BaseType.Struct)
                throw new Exception("Constant buffer uniform is not a structure.");

            uint size = (uint)reflector.GetDeclaredStructSize(baseType);

            for (uint i = 0; i < baseType.MemberCount; i++)
            {
                TypeID memberID = baseType.GetMemberType(i);
                var type = reflector.GetTypeHandle(memberID);

                if (!IsPrimitiveType(type.BaseType))
                    continue;

                ConstantBufferMember member;

                member.identifier = reflector.GetMemberName(baseType.BaseTypeID, i);
                member.bufferOffsetInBytes = reflector.StructMemberOffset(baseType, i);
                member.size = (uint)reflector.GetDeclaredStructMemberSize(baseType, i);

                member.arrayStride = 0;
                if (type.ArrayDimensions != 0)
                    member.arrayStride = reflector.StructMemberArrayStride(baseType, i);
                
                member.matrixStride = 0;
                if (type.Columns > 1)
                    member.matrixStride = reflector.StructMemberMatrixStride(baseType, i);

                member.width = type.VectorSize;
                member.height = type.Columns;

                member.type = type.BaseType switch
                {
                    BaseType.Boolean or
                    BaseType.Int8 or 
                    BaseType.Int16 or 
                    BaseType.Int32 or 
                    BaseType.Int64
                        => ValueType.Int,

                    BaseType.Float16 or
                    BaseType.Float32 or
                    BaseType.Float64 
                        => ValueType.Float,

                    BaseType.UInt8 or 
                    BaseType.UInt16 or 
                    BaseType.UInt32 or 
                    BaseType.UInt64
                        => ValueType.UInt,
                };

                members.Add(member);
            }

            return new Uniform(bufferResource.name, binding, ResourceKind.UniformBuffer) { size = size, members = members.ToArray() };
        }

        static uint GetBinding(Reflector reflector, ID id)
        {
            if (!reflector.HasDecoration(id, Decoration.Binding))
                throw new Exception("Uniform does not have binding decoration");

            return reflector.GetDecoration(id, Decoration.Binding);
        }

        static bool IsPrimitiveType(BaseType type)
        {
            return 
                type == BaseType.Boolean || 
                type == BaseType.Float16 || 
                type == BaseType.Float32 || 
                type == BaseType.Float64 || 
                type == BaseType.Int16 || 
                type == BaseType.Int32 || 
                type == BaseType.Int64 || 
                type == BaseType.Int8 ||
                type == BaseType.UInt16 ||
                type == BaseType.UInt32 ||
                type == BaseType.UInt64 ||
                type == BaseType.UInt8;
        }
    }
}