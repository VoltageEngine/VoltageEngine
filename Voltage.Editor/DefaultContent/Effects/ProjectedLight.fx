sampler s0;
float4x4 _matrixTransform;
float3 _surfaceToLight;
texture _normalMap;
sampler2D normalSampler = sampler_state
{
    Texture = <_normalMap>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct LightVertex
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 Texture : TEXCOORD0;
    float4 Screen : TEXCOORD1;
};

LightVertex LightVert(float4 position : POSITION0, float4 color : COLOR0, float2 uv : TEXCOORD0)
{
    LightVertex output;
    output.Position = mul(position, _matrixTransform);
    output.Screen = output.Position;
    output.Color = color;
    output.Texture = uv;
    return output;
}

float4 LightPixel(LightVertex input) : COLOR0
{
    float2 uv = input.Screen.xy / input.Screen.w * float2(0.5, -0.5) + 0.5;
    float3 normal = tex2D(normalSampler, uv).rgb * 2.0 - 1.0;
    normal.y *= -1;
    float diffuse = max(0, dot(normal, _surfaceToLight));
    return float4(tex2D(s0, input.Texture).rgb * input.Color.rgb * diffuse, 1);
}

technique ProjectedLight
{
    pass Pass1
    {
        VertexShader = compile vs_3_0 LightVert();
        PixelShader = compile ps_3_0 LightPixel();
    }
}
