using System.Numerics;
using Veldrid;
using DirectXShaderCompiler.NET;
using SPIRVCross.NET;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable

namespace Application
{
    public readonly struct StageInput : IEquatable<StageInput>
    {
        public readonly string semantic;
        public readonly int index;

        public readonly string fullName;
        public readonly VertexElementFormat format;

        public StageInput(string fullName, string semantic, int index, VertexElementFormat format)
        {
            this.semantic = semantic;
            this.index = index;
            this.fullName = fullName;
            this.format = format;
        }

        public StageInput(string semantic, int index)
        {
            this.semantic = semantic;
            this.index = index;
            this.fullName = "";
            this.format = VertexElementFormat.Float1;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(semantic, index);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is not StageInput other)
                return false;

            return Equals(other);
        }

        public bool Equals(StageInput other)
        {
            return semantic == other.semantic && index == other.index;
        }
    }    

    public static partial class VertexInputReflector
    {
        public static Dictionary<StageInput, uint> GetStageInputs(
            Reflector reflector, Resources resources, 
            Dictionary<string, VertexElementFormat> semanticFormats
        ) {
            Dictionary<StageInput, uint> inputLocations = new();

            for (int i = 0; i < resources.StageInputs.Length; i++)
            {
                ReflectedResource resource = resources.StageInputs[i];

                var typeInfo = reflector.GetTypeHandle(resource.type_id);

                // Default to texcoord semantic with parsed format in cases where semantic is not detected.
                var format = ParseSemantic(resource.name, semanticFormats, out string cleansedSemantic, out int index)
                    ?? ToElementFormat(typeInfo.BaseType, typeInfo.VectorSize);

                if (!reflector.HasDecoration(resource.id, Decoration.Location))
                    throw new Exception("Stage input does not contain location decoration.");

                uint location = reflector.GetDecoration(resource.id, Decoration.Location);

                inputLocations.Add(new(resource.name, cleansedSemantic, index, format), location);
            }

            return inputLocations;
        }


        [GeneratedRegex(@"(\d+)$")]
        private static partial Regex TrailingIndexRegex();

        private static VertexElementFormat? ParseSemantic(string name, 
            Dictionary<string, VertexElementFormat> semanticFormats, 
            out string semantic, out int index)
        {
            semantic = name.Substring(name.LastIndexOf('.') + 1);

            Match match = TrailingIndexRegex().Match(semantic);

            index = 0;
            
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out index))
                    semantic = semantic.Substring(0, match.Index);
            }

            if (semanticFormats.TryGetValue(semantic, out VertexElementFormat format))
                return format;

            return null;
        }

        static VertexElementFormat ToElementFormat(BaseType type, uint vectorSize)
        {
            VertexElementFormat Offset(VertexElementFormat baseFormat)
                => (VertexElementFormat)((int)baseFormat + (vectorSize - 1));

            if (type == BaseType.Int8 && vectorSize != 1 && vectorSize != 3)
                return Offset(VertexElementFormat.SByte2);
            if (type == BaseType.UInt8 && vectorSize != 1 && vectorSize != 3)
                return Offset(VertexElementFormat.Byte2);
            
            if (type == BaseType.Int16 && vectorSize != 1 && vectorSize != 3)
                return Offset(VertexElementFormat.Short2);
            if (type == BaseType.UInt16 && vectorSize != 1 && vectorSize != 3)
                return Offset(VertexElementFormat.UShort2);

            if (type == BaseType.Int32)
                return Offset(VertexElementFormat.Int1);
            if (type == BaseType.UInt32)
                return Offset(VertexElementFormat.UInt1);

            if (type == BaseType.Float16 && vectorSize != 3)
                return Offset(VertexElementFormat.Half1);
            if (type == BaseType.Float32)
                return Offset(VertexElementFormat.Float1);

            throw new Exception($"Unsupported vertex element format of Vector{vectorSize} with type {type}");
        }
    }
}