// 이펙트 기본 셰이더 (URP Unlit)
//   마스크(흑백) → 일렁임 → 불꽃 가장자리 → 이로전으로 깎음 → 스타일별 색
//   스타일 0 카툰: 3단 단색 + 진한 외곽선, 딱 떨어지는 가장자리 (Alpha Clip 머티리얼)
//   스타일 1 빛  : 부드러운 그러데이션 + 하얗게 타는 심지 + 부드러운 가장자리 (Additive 머티리얼)
//   값은 VfxLayer가 MaterialPropertyBlock으로 넣음 (머티리얼은 공유)
Shader "Toon/VfxErosion"
{
    Properties
    {
        _MaskTex ("Mask (R)", 2D) = "white" {}

        [Header(Style)]
        [Enum(Toon, 0, Glow, 1)] _Style ("Style", Float) = 0
        _Softness ("Glow Edge Softness", Range(0.01, 0.6)) = 0.2
        _CoreGlow ("Glow Core Burn", Range(0, 2)) = 0.8

        [Header(Palette)]
        _ColorCore ("Core", Color) = (1, 0.97, 0.78, 1)
        _ColorMid ("Mid", Color) = (1, 0.78, 0.15, 1)
        _ColorEdge ("Edge", Color) = (1, 0.36, 0.05, 1)
        _ColorOutline ("Outline", Color) = (0.55, 0.05, 0.02, 1)
        _CoreThreshold ("Core Threshold", Range(0, 1)) = 0.65
        _MidThreshold ("Mid Threshold", Range(0, 1)) = 0.35
        _OutlineWidth ("Outline Width", Range(0, 0.3)) = 0.06

        [Header(Animation)]
        _Erosion ("Erosion", Range(0, 1)) = 0
        _ErodeInvert ("Erode From Center", Range(0, 1)) = 0
        _ColorShift ("Color Shift", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
        _Brightness ("Brightness", Float) = 1.5
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Thin ("Thickness", Range(0, 1)) = 1
        _Sweep ("Sweep Head", Range(0, 1)) = 1
        _SweepTail ("Sweep Tail", Range(0, 1)) = 0

        [Header(Shape)]
        [Enum(Card, 0, Arc, 1, Pillar, 2, Cone, 3)] _ShapeMode ("Shape Mode", Float) = 0
        _HeadBias ("Arc Head Bias", Range(0.05, 0.95)) = 0.7
        _Sharpness ("Arc Sharpness", Range(0.2, 3)) = 1
        _TipLength ("Arc Moving Tip Length", Range(0.01, 0.5)) = 0.15
        [Toggle] _FaceCamera ("Arc Face Camera", Float) = 1
        [Toggle] _PerParticle ("Per-Particle Anim (Vertex Color)", Float) = 0

        [Header(Cone Streaks)]
        _StreakCount ("Streak Count", Float) = 14
        _StreakAmount ("Streak Amount", Range(0, 1)) = 0
        _StreakSpeed ("Streak Speed", Float) = 3
        _Twist ("Twist", Float) = 0

        [Header(Flame Edge)]
        _FlameEdge ("Flame Edge", Range(0, 1)) = 0
        _FlameScale ("Flame Scale", Float) = 6
        _FlameSpeed ("Flame Speed", Float) = 3

        [Header(Wobble)]
        _Wobble ("Wobble", Range(0, 1)) = 0.15
        _WobbleScale ("Wobble Scale", Float) = 3
        _WobbleSpeed ("Wobble Speed", Float) = 1.5
        _UVScroll ("UV Scroll (xy)", Vector) = (0, 0, 0, 0)
        _VfxTime ("Time", Float) = 0

        [Header(Blend)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VfxForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                float _Style;
                float _Softness;
                float _CoreGlow;
                float4 _ColorCore;
                float4 _ColorMid;
                float4 _ColorEdge;
                float4 _ColorOutline;
                float _CoreThreshold;
                float _MidThreshold;
                float _OutlineWidth;
                float _Erosion;
                float _ErodeInvert;
                float _ColorShift;
                float _Flash;
                float _Brightness;
                float _Alpha;
                float _Thin;
                float _Sweep;
                float _SweepTail;
                float _ShapeMode;
                float _HeadBias;
                float _Sharpness;
                float _TipLength;
                float _FaceCamera;
                float _PerParticle;
                float _StreakCount;
                float _StreakAmount;
                float _StreakSpeed;
                float _Twist;
                float _FlameEdge;
                float _FlameScale;
                float _FlameSpeed;
                float _Wobble;
                float _WobbleScale;
                float _WobbleSpeed;
                float4 _UVScroll;
                float _VfxTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;      // 호: 안쪽으로 펼칠 기본 방향
                float4 tangentOS : TANGENT;    // 호: 궤적 진행 방향
                float2 uv : TEXCOORD0;
                float4 uv1 : TEXCOORD1;        // 호: x 펼칠 거리, y 띠 전체 폭, z 가운데 정렬, w 카메라 향함 허용
                float4 color : COLOR;          // 뿌리기: 조각별 r 이로전, g 색 식음, b 번쩍, a 투명도
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float sym : TEXCOORD1;         // 1 = 세로 띠 (초승달을 위아래 대칭으로)
                float4 anim : TEXCOORD2;       // 조각별 애니메이션 값
                float aspect : TEXCOORD3;      // 조각 세로/가로 비율 (뿌리기) — 노이즈가 같이 늘어나지 않게
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;
                float total = v.uv1.y;

                if (total > 0.0)
                {
                    float3 dir = v.normalOS;
                    float centered = v.uv1.z;

                    // 카메라 향함 (가로 띠만): 궤적을 가운데 두고 폭을 '궤적 방향 × 시선 방향'으로 펼침
                    if (_FaceCamera > 0.5 && v.uv1.w > 0.5)
                    {
                        float3 camOS = TransformWorldToObject(GetCameraPositionWS());
                        float3 view = normalize(camOS - p);
                        float3 side = cross(v.tangentOS.xyz, view);
                        float len = length(side);
                        if (len > 0.0001)
                        {
                            side /= len;
                            // 방향 고르기 — 원래 안쪽과 같은 편. 애매하면(1인칭 정면) 위쪽 → 안쪽 가장자리가 항상 위
                            float d = dot(side, v.normalOS);
                            float pick = abs(d) > 0.3 ? d : dot(side, float3(0, 1, 0));
                            dir = pick < 0.0 ? -side : side;
                            centered = 1.0;
                        }
                    }
                    p += dir * (v.uv1.x - total * 0.5 * centered);
                }

                o.positionCS = TransformObjectToHClip(p);
                o.uv = v.uv;
                o.sym = (v.uv1.z > 0.5 && v.uv1.w < 0.5) ? 1.0 : 0.0;
                o.anim = v.color;
                o.aspect = _PerParticle > 0.5 ? max(v.uv1.x, 0.05) : 1.0;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            /// 가로로 wrapX 칸마다 반복되는 노이즈 — 기둥 둘레의 이음새가 안 보이게
            float WrapNoise(float2 p, float wrapX)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float x0 = fmod(i.x, wrapX);
                float x1 = fmod(i.x + 1.0, wrapX);
                float a = Hash21(float2(x0, i.y));
                float b = Hash21(float2(x1, i.y));
                float c = Hash21(float2(x0, i.y + 1.0));
                float d = Hash21(float2(x1, i.y + 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // 뿌리기는 조각마다 다른 값 (정점 색), 나머지는 레이어 공통 값
                float erosion = lerp(_Erosion, i.anim.r, _PerParticle);
                float colorShift = lerp(_ColorShift, i.anim.g, _PerParticle);
                float flash = lerp(_Flash, i.anim.b, _PerParticle);
                float alpha = lerp(_Alpha, i.anim.a, _PerParticle);

                // ── 보이는 구간 [꼬리, 머리] ──
                float head = _Sweep;
                float tail = min(_SweepTail, head);
                clip(head - uv.x + 0.0001);
                clip(uv.x - tail);

                bool pillar = _ShapeMode > 1.5;

                float v;
                if (abs(_ShapeMode - 1.0) < 0.5)
                {
                    // 호: 초승달 모양은 제자리에 고정 — 붓끝만 지나가며 드러남
                    float bias = clamp(_HeadBias, 0.02, 0.98);
                    float x = uv.x < bias ? uv.x / bias : (1.0 - uv.x) / (1.0 - bias);
                    float w = pow(sin(saturate(x) * 1.5707963), _Sharpness);

                    // 움직이는 머리 끝 · 꼬리 끝만 짧게 뾰족하게
                    float tip = max(_TipLength, 0.001);
                    float headTip = head < 0.999 ? saturate((head - uv.x) / tip) : 1.0;
                    float tailTip = tail > 0.001 ? saturate((uv.x - tail) / tip) : 1.0;
                    w *= sqrt(headTip * tailTip) * _Thin;

                    if (i.sym > 0.5)
                    {
                        // 세로 띠: 가운데 기준 위아래로 대칭
                        v = (uv.y - 0.5) / max(w, 0.0001) + 0.5;
                        clip(v);
                        clip(1.0 - v);
                    }
                    else
                    {
                        // 가로 띠: 바깥 테두리에 붙은 초승달
                        v = (uv.y - (1.0 - w)) / max(w, 0.0001);
                        clip(v);
                    }
                    uv.y = v;
                }
                else
                {
                    // 카드: 폭(V)을 가운데 기준으로 좁힘
                    float halfBand = max(_Thin, 0.001) * 0.5;
                    v = (uv.y - 0.5) / halfBand * 0.5 + 0.5;
                    clip(v);
                    clip(1.0 - v);
                    uv.y = v;
                }

                // ── 일렁임 ──
                // 늘인 조각은 노이즈도 같이 늘어나 머리카락처럼 갈라지므로, 실제 비율 공간에서 계산
                float2 aspectScale = float2(1.0, i.aspect);
                float2 np = uv * aspectScale * _WobbleScale + float2(0.0, -_VfxTime * _WobbleSpeed);
                float2 offs;
                float rag;
                if (pillar)
                {
                    // 기둥: 둘레를 정수 칸으로 나눠 반복 — 한 바퀴 돌아도 이어짐
                    float wrap = max(1.0, round(_WobbleScale * 2.0));
                    float2 wp = float2(uv.x * wrap, np.y);
                    offs = float2(WrapNoise(wp, wrap), WrapNoise(wp + float2(0.0, 17.3), wrap)) - 0.5;
                    rag = WrapNoise(float2(wp.x * 2.0, wp.y * 1.7 + 5.1), wrap * 2.0);
                }
                else
                {
                    offs = (float2(ValueNoise(np), ValueNoise(np + 17.3)) - 0.5) / aspectScale;
                    rag = ValueNoise(np * 1.7 + 5.1);
                }
                float2 muv = uv + offs * (_Wobble * 0.12) + _UVScroll.xy * _VfxTime;
                if (pillar) muv.x = frac(muv.x);
                muv = muv * _MaskTex_ST.xy + _MaskTex_ST.zw;

                float m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, muv).r;
                m = saturate(m - (rag - 0.5) * _Wobble * 0.25);

                // ── 원뿔 빛줄기: 둘레를 가는 줄로 나누고 앞 → 뒤로 흘려보냄 (소용돌이만큼 비틀림) ──
                if (_ShapeMode > 2.5 && _StreakAmount > 0.001)
                {
                    float sn = max(1.0, round(_StreakCount));
                    float cu = frac(uv.x + uv.y * _Twist);
                    float2 sp = float2(cu * sn, uv.y * 1.2 - _VfxTime * _StreakSpeed);
                    float nz = WrapNoise(sp, sn) * 0.65 + WrapNoise(float2(sp.x * 2.0, sp.y * 1.4 + 3.7), sn * 2.0) * 0.35;
                    float ridge = 1.0 - abs(nz * 2.0 - 1.0);         // 노이즈 등고선 → 가는 줄
                    float st = smoothstep(0.7, 0.95, ridge);
                    m *= lerp(1.0, st, _StreakAmount);
                }

                // ── 불꽃: 꼬리 방향으로 흐르는 노이즈로 찢음 → 혀처럼 날름거림 ──
                //    양쪽 가장자리는 강하게, 몸통 안쪽은 약하게 (전체가 타오르되 형태는 유지)
                if (_FlameEdge > 0.001)
                {
                    float f, edgeW;
                    if (pillar)
                    {
                        // 기둥: 불꽃이 위로 흐르고, 위쪽일수록 많이 찢김
                        float wrap = max(1.0, round(_FlameScale));
                        float2 fp = float2(uv.x * wrap, uv.y * _FlameScale * 0.5 - _VfxTime * _FlameSpeed);
                        f = WrapNoise(fp, wrap) * 0.65 + WrapNoise(float2(fp.x * 2.0, fp.y * 2.3 + 7.1), wrap * 2.0) * 0.35;
                        edgeW = lerp(0.3, 1.0, uv.y);
                    }
                    else
                    {
                        float2 fp = float2(uv.x * _FlameScale + _VfxTime * _FlameSpeed, uv.y * i.aspect * _FlameScale * 0.35);
                        f = ValueNoise(fp) * 0.65 + ValueNoise(fp * 2.3 + 7.1) * 0.35;
                        float center = 1.0 - abs(uv.y * 2.0 - 1.0);           // 가장자리 0 → 가운데 1
                        edgeW = lerp(1.0, 0.35, center);
                    }
                    m = saturate(m - f * _FlameEdge * edgeW * 0.9);
                }

                // ── 이로전: 보통은 낮은 값(테두리·꼬리)부터, 반전이면 높은 값(가운데)부터 ──
                float lower = lerp(erosion, 0.0, _ErodeInvert);
                float upper = lerp(2.0, 1.0 - erosion, _ErodeInvert);
                float dist = min(m - lower, upper - m);
                float cm = m - colorShift * 0.7;

                // ════ 카툰 ════
                if (_Style < 0.5)
                {
                    clip(dist - 0.001);

                    float3 col = lerp(_ColorEdge.rgb, _ColorMid.rgb, step(_MidThreshold, cm));
                    col = lerp(col, _ColorCore.rgb, step(_CoreThreshold, cm));
                    col *= _Brightness;
                    col = lerp(col, _ColorOutline.rgb, step(dist, _OutlineWidth));
                    col = lerp(col, float3(1, 1, 1) * max(_Brightness, 1.0) * 1.2, flash);
                    return half4(col, alpha);
                }

                // ════ 빛 ════
                float a = smoothstep(0.0, _Softness, dist);
                clip(a - 0.004);

                float3 g = lerp(_ColorEdge.rgb, _ColorMid.rgb, smoothstep(_MidThreshold - 0.2, _MidThreshold + 0.2, cm));
                g = lerp(g, _ColorCore.rgb, smoothstep(_CoreThreshold - 0.15, _CoreThreshold + 0.15, cm));
                g *= _Brightness;
                g += float3(1, 1, 1) * pow(saturate(cm), 4.0) * _CoreGlow * _Brightness;   // 하얗게 타는 심지
                g = lerp(g, float3(1, 1, 1) * max(_Brightness, 1.0) * 1.5, flash);
                return half4(g, a * alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
