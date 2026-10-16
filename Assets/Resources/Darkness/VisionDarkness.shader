// 암흑 시야 — 내 몸 둘레 반경 _VisionRadius(m)만 보이고 그 밖은 어둠.
// 화면 전체를 덮는 삼각형 하나를 그리며, 깊이 텍스처로 각 픽셀의 월드 위치를 복원해서 내 몸(선분)과의 거리로 어둡게 함.
// ScreenDarkness.cs가 Graphics.RenderMesh로 게임 카메라에만 그림 — 머티리얼/렌더러 기능 설정 필요 없음.
// 빌드 포함: 이 파일을 Resources 폴더에 두기 (ScreenDarkness가 Resources.Load로 찾음)
Shader "Hidden/Game/VisionDarkness"
{
    Properties { }

    SubShader
    {
        Tags { "Queue" = "Overlay+500" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "VisionDarkness"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // 전역 값 (ScreenDarkness가 매 프레임 넣음)
            float4 _VisionA;          // 몸 선분 아래 끝 (발 근처)
            float4 _VisionB;          // 몸 선분 위 끝 (머리 근처)
            float  _VisionRadius;     // 또렷이 보이는 반경 (m)
            float  _VisionFade;       // 어둠으로 넘어가는 폭 (m)
            float  _VisionStrength;   // 0~1
            float4 _VisionColor;      // 어둠 색 (a = 최대 불투명도)

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                // 메쉬 꼭짓점 xy가 곧 화면 좌표(-1~3) — 화면 전체를 덮는 삼각형
                o.positionCS = float4(v.positionOS.xy, 0.5, 1.0);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.positionCS.xy / _ScaledScreenParams.xy;

                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                // 몸 선분까지 거리 — 발밑 바닥도 반경 안으로 잡히게
                float3 a  = _VisionA.xyz;
                float3 ab = _VisionB.xyz - a;
                float  t  = saturate(dot(wp - a, ab) / max(dot(ab, ab), 1e-5));
                float  d  = distance(wp, a + ab * t);

                // 반경 밖 = 완전한 어둠. 경계 폭(_VisionFade) 안에서만 빠르게 어두워짐
                float k = saturate((d - _VisionRadius) / max(_VisionFade, 0.01));
                k = 1.0 - (1.0 - k) * (1.0 - k);            // 경계 바깥쪽으로 갈수록 확 어두워짐
                float alpha = k * _VisionColor.a * _VisionStrength;
                // 알파 1이면 원래 색을 완전히 덮음 — HDR로 밝은 하늘 · 이펙트도 새어 나오지 않게 색 자체를 0으로
                return half4(_VisionColor.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
