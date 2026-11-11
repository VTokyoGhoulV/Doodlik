Shader "Doodlik/SoftStarInk"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float edge : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                output.edge = input.uv.x;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // The mesh carries one at the centre and zero on its outline.
                // Derivatives soften only the last screen pixel, independently
                // of phone resolution, camera zoom or the star's world size.
                float feather = max(fwidth(input.edge) * 1.15, 0.0001);
                half4 color = input.color;
                color.a *= smoothstep(0.0, feather, input.edge);
                return color;
            }
            ENDHLSL
        }
    }
}
