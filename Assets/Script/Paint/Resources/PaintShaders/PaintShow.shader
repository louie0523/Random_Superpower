// 보이는 그림 = 바탕(기본 그림 × 바탕색) 위에 칠 그림
Shader "Hidden/Paint/Show"
{
    Properties { _MainTex ("Paint", 2D) = "black" {} _BaseTex ("Base", 2D) = "white" {} }
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
            sampler2D _BaseTex;
            float4 _BaseColor;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 p = tex2D(_MainTex, i.uv);
                float4 b = tex2D(_BaseTex, i.uv) * _BaseColor;
                float oa = p.a + b.a * (1.0 - p.a);
                float3 oc = (p.rgb * p.a + b.rgb * b.a * (1.0 - p.a)) / max(oa, 0.0001);
                return float4(oc, oa);
            }
            ENDCG
        }
    }
}
