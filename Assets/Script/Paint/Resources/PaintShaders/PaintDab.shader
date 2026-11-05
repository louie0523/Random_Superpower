// 붓 자국 한 점 (동그라미) — 덩어리 마스크(R)에 Max로 겹침. GL로 그림 (PaintCanvas)
//   TEXCOORD1.x = 진하기, .y = 단단함 (0 = 부드러움 · 1 = 딱딱)
Shader "Hidden/Paint/Dab"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" }
        Cull Off ZWrite Off ZTest Always
        BlendOp Max
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 p : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 p : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.p = v.p;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                float a = (1.0 - smoothstep(i.p.y, 1.0, r)) * i.p.x;
                return float4(a, a, a, a);
            }
            ENDCG
        }
    }
}
