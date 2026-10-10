sampler s0;

texture _normalMap;
sampler _normalMapSampler = sampler_state { Texture = <_normalMap>; };

float _alphaCutoff;
float _alphaAsSelfIllumination;
float _selfIlluminationPower;
float4 _normalUvTransform;
float4 _normalTransform;


struct PixelMultiTextureOut
{
    float4 color  : COLOR0;
    float4 normal : COLOR1;
};


PixelMultiTextureOut deferredSpritePixel( float2 texCoords:TEXCOORD0, float4 color:COLOR0 )
{
    PixelMultiTextureOut output;

    // output our diffuse into the color texture
    output.color = tex2D( s0, texCoords ) * color;

    // get out of here if we fail the alpha test
	clip( ( output.color.a < _alphaCutoff ) ? -1 : 1 );


    // output our normal map into the normal texture. the alpha channel is used for self illumination with 1 being fully self illuminated
    // and 0 having no self illumination
    output.normal = tex2D( _normalMapSampler, texCoords * _normalUvTransform.xy + _normalUvTransform.zw );
    float2 normal = output.normal.xy * 2.0 - 1.0;
    output.normal.xy = float2(dot(_normalTransform.xy, normal), dot(_normalTransform.zw, normal)) * 0.5 + 0.5;
    output.normal.a *= _alphaAsSelfIllumination * _selfIlluminationPower;

    return output;
}


technique DeferredSpriteTechnique
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 deferredSpritePixel();
    }
}
