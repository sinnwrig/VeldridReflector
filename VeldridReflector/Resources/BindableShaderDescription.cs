using System.Numerics;
using Veldrid;

namespace Application
{
    public class BindableShaderDescription(StageInput[] vertexInputs, Uniform[] uniforms, ShaderStages[] stages)
    {
        public StageInput[] VertexInputs = vertexInputs;
        public Uniform[] Uniforms = uniforms;
        public ShaderStages[] UniformStages = stages;
    }
}