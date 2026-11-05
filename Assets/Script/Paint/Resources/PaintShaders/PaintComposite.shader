// 그리는 중인 한 획(마스크)을 칠 그림 위에 얹음 — 스프레이 알갱이 · 카툰 외곽선 · 지우개
Shader "Hidden/Paint/Composite"
{
    Properties { _MainTex ("Paint", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Mask;
            sampler2D _Noise;
            float4 _Mask_TexelSize;
            float4 _Color;
            float4 _Ink;
            float4 _NoiseScale;
            float _Spray;
            float _Erase;
            float _OutlinePx;

            float Fill(float2 uv) { return smoothstep(0.3, 0.5, tex2D(_Mask, uv).r); }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 dst = tex2D(_MainTex, i.uv);
                float m = tex2D(_Mask, i.uv).r;
                float n = tex2D(_Noise, i.uv * _NoiseScale.xy).r;
                float fill = smoothstep(0.3, 0.5, m + (n - 0.5) * 0.55 * _Spray);

                if (_Erase > 0.5)
                {
                    dst.a *= 1.0 - fill;
                    return dst;
                }

                float ring = 0.0;
                if (_OutlinePx > 0.5)
                {
                    float2 px = _Mask_TexelSize.xy * _OutlinePx;
                    [unroll] for (int k = 0; k < 12; k++)
                    {
                        float ang = k * 0.5235988;
                        float2 o = float2(cos(ang), sin(ang)) * px;
                        ring = max(ring, Fill(i.uv + o));
                        ring = max(ring, Fill(i.uv + o * 0.5));
                    }
                    ring = saturate(ring - fill);
                }

                fill *= _Color.a;
                float sa = fill + ring * (1.0 - fill);
                if (sa <= 0.0001) return dst;
                float3 sc = (_Color.rgb * fill + _Ink.rgb * ring * (1.0 - fill)) / sa;
                float oa = sa + dst.a * (1.0 - sa);
                float3 oc = (sc * sa + dst.rgb * dst.a * (1.0 - sa)) / max(oa, 0.0001);
                return float4(oc, oa);
            }
            ENDCG
        }
    }
}
