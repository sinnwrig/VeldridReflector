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

#define SIZE 1000

float4x4 MVP;
Texture2D<float4> SurfaceTexture;
SamplerState SurfaceSampler;
float4 BaseColor;

cbuffer _MyBuf1
{
    float4 VData;
}

cbuffer _MyBuf2
{
    float4 PData;
}

v2f vert(appdata input)
{
    v2f output = (v2f)0;

    output.pos = mul(MVP, float4(input.pos * VData.xyz, 1.0));
    output.uv = input.uv;

    return output;
}

float4 frag(v2f input) : SV_TARGET
{
    return SurfaceTexture.Sample(SurfaceSampler, input.uv) * BaseColor * PData;
}