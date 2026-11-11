Shader "Doodlik/Cat Clothing World"
{
    Properties
    {
        [PerRendererData] _MainTex ("Cat texture", 2D) = "white" {}
        _BodyTint ("Fur", Color) = (1,1,1,1)
        _ClothingColor ("Clothing", Color) = (1,0.5,0.1,1)
        _ClothingStrength ("Dye strength", Float) = 0
        _AstronautHeadMask ("Astronaut head trim", Float) = 0
        _AstronautSuitSeamMask ("Astronaut suit seam trim", Float) = 0
        _SpriteUVRect ("Sprite UV", Vector) = (0,0,1,1)
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            struct Attributes { COMMON_2D_INPUTS half4 color : COLOR; };
            struct Varyings { COMMON_2D_OUTPUTS half4 color : COLOR; };
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BodyTint;
                half4 _ClothingColor;
                float4 _SpriteUVRect;
                half _ClothingStrength;
                half _AstronautHeadMask;
                half _AstronautSuitSeamMask;
            CBUFFER_END
            #include "CatClothingColor.hlsl"
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings output = CommonUnlitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return DoodlikCatColor(source, input.uv) * input.color;
            }
            ENDHLSL
        }
    }
}
