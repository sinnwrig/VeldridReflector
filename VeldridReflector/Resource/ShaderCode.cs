

public static class ShaderCode
{
    public const string sourceCode = 
"""
struct appdata
{
    float3 pos : POSITION;
    float2 uv : TEXCOORD0;
};

struct v2f 
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4x4 MVP;
Texture2D<float4> SurfaceTexture;
SamplerState SurfaceSampler;
float4 BaseColor;

v2f vert(appdata input)
{
    v2f output = (v2f)0;

    output.pos = mul(MVP, float4(input.pos.xyz, 1.0));
    output.uv = input.uv;

    return output;
}

float4 frag(v2f input) : SV_TARGET
{
    return SurfaceTexture.Sample(SurfaceSampler, input.uv) * BaseColor;
}
""";
}