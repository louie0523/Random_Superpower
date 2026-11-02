// 피격 번쩍임 덮개 — HitFlash.cs가 렌더러 재질 끝에 잠깐 붙임.
// 가장자리(림)는 진하고 가운데는 옅게 → 모델 텍스처가 비쳐 보이는 카툰식 번쩍임. URP 전용.
Shader "Hidden/Game/HitFlash"
{
    Properties
    {
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        _Rim ("Rim Power", Range(0.5, 6)) = 1.6
        _Core ("Center Strength", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HitFlash"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FlashColor;
                half _Rim;
                half _Core;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                half rim = pow(1.0h - saturate(dot(n, v)), _Rim);
                half a = _FlashColor.a * lerp(_Core, 1.0h, rim);
                return half4(_FlashColor.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
