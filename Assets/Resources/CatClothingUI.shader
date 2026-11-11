Shader "Doodlik/Cat Clothing UI"
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
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 position : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            half4 _Color, _BodyTint, _ClothingColor;
            float4 _SpriteUVRect, _ClipRect;
            half _ClothingStrength;
            half _AstronautHeadMask;
            half _AstronautSuitSeamMask;
            #include "CatClothingColor.hlsl"
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 value = DoodlikCatColor(tex2D(_MainTex, input.uv), input.uv) * input.color;
                #ifdef UNITY_UI_CLIP_RECT
                value.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(value.a - 0.001);
                #endif
                return value;
            }
            ENDCG
        }
    }
}
