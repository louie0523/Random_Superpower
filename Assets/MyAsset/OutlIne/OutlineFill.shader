// 아웃라인 2단계 — 몸을 법선 방향으로 부풀려 그리되, 1단계에서 표시한 몸 안쪽은 빼서 '테두리'만 남김.
//   _ZTest 4 (LessEqual) = 보이는 부분만 · 8 (Always) = 벽 너머로도
// 매끈한 법선(TEXCOORD3)이 있으면 그걸로 부풀림 — 각진 로우폴리에서도 테두리가 끊기지 않음 (PlayerOutline이 구워 넣음)
Shader "Hidden/Game/OutlineFill"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (1, 0.25, 0.2, 1)
        _OutlineWidth ("Width", Range(0, 10)) = 2.5
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+110" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "DisableBatching" = "True" }

        Pass
        {
            Name "OutlineFill"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZTest [_ZTest]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            Stencil
            {
                Ref 1
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float3 smoothOS   : TEXCOORD3;
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 n = any(i.smoothOS) ? i.smoothOS : i.normalOS;

                float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 posVS = TransformWorldToView(posWS);
                float3 nVS = normalize(mul((float3x3)GetWorldToViewMatrix(), TransformObjectToWorldNormal(n)));

                // 멀어져도 화면에서 비슷한 두께
                posVS += nVS * (-posVS.z) * _OutlineWidth * 0.001;
                o.positionCS = TransformWViewToHClip(posVS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
