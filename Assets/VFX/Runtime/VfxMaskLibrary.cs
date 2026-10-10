using System.Collections.Generic;
using UnityEngine;

namespace ToonVfx
{
    /// <summary>
    /// 코드로 만드는 기본 마스크. 에셋이 없어도 동작하도록 런타임에 생성·캐시.
    /// 값 규칙: 모양 바깥 = 0, 테두리로 갈수록 0에 가깝고 안쪽(코어)으로 갈수록 1 — 이로전이 테두리부터 갉아먹음.
    /// </summary>
    public static class VfxMaskLibrary
    {
        public const int DefaultSize = 256;
        static readonly Dictionary<VfxBuiltinMask, Texture2D> Cache = new();

        public static Texture2D Get(VfxBuiltinMask kind)
        {
            if (Cache.TryGetValue(kind, out var tex) && tex != null) return tex;
            tex = Generate(kind, DefaultSize);
            Cache[kind] = tex;
            return tex;
        }

        public static Texture2D Generate(VfxBuiltinMask kind, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.R8, true, true)
            {
                name = $"VfxMask_{kind}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            var px = new byte[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    px[y * size + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(Value(kind, u, v)) * 255f);
                }

            tex.SetPixelData(px, 0);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>u, v ∈ [0,1] 에서의 마스크 값</summary>
        // 돌 조각 꼭짓점 (중심 기준, 반시계)
        static readonly Vector2[] RockPts =
        {
            new(0.40f, 0.05f), new(0.22f, 0.36f), new(-0.18f, 0.40f),
            new(-0.42f, 0.06f), new(-0.28f, -0.34f), new(0.20f, -0.40f),
        };

        // 연기 원 (x, y, 반지름)
        static readonly Vector3[] SmokePuffs =
        {
            new(0.38f, 0.42f, 0.24f), new(0.62f, 0.44f, 0.22f),
            new(0.50f, 0.62f, 0.25f), new(0.50f, 0.36f, 0.20f),
        };

        // ── 모양 거리 함수 (중심 기준 좌표, 안쪽이 음수) ──

        static Vector2 P(float u, float v) => new(u - 0.5f, v - 0.5f);

        /// <summary>안쪽 거리 → 마스크 값. 테두리 0, 안쪽으로 falloff만큼 들어가면 1</summary>
        static float Inside(float d, float falloff) => Mathf.Clamp01(-d / falloff);

        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        static float Box(Vector2 p, Vector2 c, Vector2 half)
        {
            var d = new Vector2(Mathf.Abs(p.x - c.x) - half.x, Mathf.Abs(p.y - c.y) - half.y);
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        static float Tri(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 q0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 q1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 q2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            float dx = Mathf.Min(Mathf.Min(Vector2.Dot(q0, q0), Vector2.Dot(q1, q1)), Vector2.Dot(q2, q2));
            float dy = Mathf.Min(Mathf.Min(s * (v0.x * e0.y - v0.y * e0.x), s * (v1.x * e1.y - v1.y * e1.x)),
                                 s * (v2.x * e2.y - v2.y * e2.x));
            return -Mathf.Sqrt(dx) * Mathf.Sign(dy);
        }

        static float Star5(Vector2 p, float r, float rf)
        {
            var k1 = new Vector2(0.809016994f, -0.587785252f);
            var k2 = new Vector2(-k1.x, k1.y);
            p.x = Mathf.Abs(p.x);
            p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
            p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
            p.x = Mathf.Abs(p.x);
            p.y -= r;
            var ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }

        /// <summary>부드러운 합치기 — 두 모양 사이를 둥글게 이음</summary>
        static float SMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        public static float Value(VfxBuiltinMask kind, float u, float v)
        {
            switch (kind)
            {
                case VfxBuiltinMask.SlashStrip:
                {
                    // 폭 방향 가운데가 밝고, 길이 방향으로는 끝(머리)이 밝음 → 꼬리부터 갉아먹힘
                    float across = 1f - Mathf.Abs(v * 2f - 1f);
                    float along = Mathf.Lerp(0.3f, 1f, Mathf.Pow(u, 0.8f));
                    return Mathf.Pow(across, 0.6f) * along;
                }

                case VfxBuiltinMask.Radial:
                {
                    float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                    return Mathf.Pow(Mathf.Clamp01(1f - d), 0.8f);
                }

                case VfxBuiltinMask.Flame:
                {
                    // 아래는 둥글고 위로 뾰족한 물방울
                    const float r = 0.27f, cy = 0.33f, tip = 0.95f;
                    float x = u - 0.5f, y = v - cy;

                    if (y <= 0f)
                        return Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) / r), 0.7f);

                    float h = y / (tip - cy);
                    if (h >= 1f) return 0f;
                    float halfW = r * Mathf.Pow(1f - h, 0.9f);
                    float k = Mathf.Abs(x) / Mathf.Max(halfW, 1e-4f);
                    return Mathf.Pow(Mathf.Clamp01(1f - k), 0.7f) * Mathf.Sqrt(1f - h);
                }

                case VfxBuiltinMask.Star:
                {
                    // 4방향 반짝임 (아스트로이드)
                    float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
                    return Mathf.Clamp01(1f - (Mathf.Sqrt(x) + Mathf.Sqrt(y)));
                }

                case VfxBuiltinMask.Spike:
                {
                    // 위아래로 긴 가시 (양끝 뾰족) — 피격·폭발의 방사형 조각용
                    float along = 1f - Mathf.Abs(v * 2f - 1f);
                    float halfW = 0.3f * Mathf.Pow(along, 1.4f);
                    float k = Mathf.Abs(u - 0.5f) * 2f / Mathf.Max(halfW * 2f, 1e-4f);
                    return Mathf.Clamp01(1f - k) * Mathf.Sqrt(along);
                }

                case VfxBuiltinMask.Band:
                {
                    // 띠 — 폭 가운데가 밝고 길이 방향은 일정 (링·줄무늬용)
                    return Mathf.Pow(1f - Mathf.Abs(v * 2f - 1f), 0.6f);
                }

                case VfxBuiltinMask.Rock:
                {
                    // 각진 돌 조각 — 6각형 다각형, 테두리에서 안쪽으로 밝아짐
                    var p = new Vector2(u - 0.5f, v - 0.5f);
                    float inside = float.MaxValue;
                    for (int i = 0; i < RockPts.Length; i++)
                    {
                        Vector2 a = RockPts[i], b = RockPts[(i + 1) % RockPts.Length];
                        Vector2 e = (b - a).normalized;
                        float d = (p.x - a.x) * e.y - (p.y - a.y) * e.x;   // 변 안쪽이 +
                        inside = Mathf.Min(inside, -d);
                    }
                    return Mathf.Clamp01(inside / 0.14f);
                }

                case VfxBuiltinMask.Smoke:
                {
                    // 뭉게구름 — 원 네 개를 합친 덩어리
                    float best = 0f;
                    foreach (var c in SmokePuffs)
                    {
                        float d = Vector2.Distance(new Vector2(u, v), new Vector2(c.x, c.y));
                        best = Mathf.Max(best, Mathf.Pow(Mathf.Clamp01(1f - d / c.z), 0.7f));
                    }
                    return best;
                }

                case VfxBuiltinMask.PillarFade:
                {
                    // 기둥 — 아래가 밝고 위로 갈수록 사라짐 (이로전이 위부터 먹어 들어감), 바닥 끝은 살짝 흐림
                    return Mathf.Pow(1f - v, 0.7f) * Mathf.SmoothStep(0f, 1f, v / 0.08f);
                }

                case VfxBuiltinMask.FlameTongue:
                {
                    // 둥근 밑동 + 가운데 큰 혀 + 양옆 작은 혀, 위로 갈수록 살짝 휘어짐
                    var p = P(u, v);
                    p.x -= Mathf.Sin((p.y + 0.5f) * Mathf.PI) * 0.05f;
                    float d = Circle(p, new Vector2(0f, -0.19f), 0.26f);
                    d = SMin(d, Tri(p, new Vector2(-0.24f, -0.1f), new Vector2(0.22f, -0.1f), new Vector2(0.05f, 0.47f)), 0.08f);
                    d = SMin(d, Tri(p, new Vector2(-0.32f, -0.2f), new Vector2(-0.04f, -0.02f), new Vector2(-0.27f, 0.23f)), 0.06f);
                    d = SMin(d, Tri(p, new Vector2(0.04f, -0.02f), new Vector2(0.32f, -0.2f), new Vector2(0.26f, 0.15f)), 0.06f);
                    return Inside(d, 0.11f);
                }

                case VfxBuiltinMask.Snowflake:
                {
                    // 6갈래 + 갈래마다 잔가지 한 쌍 + 가운데 육각 심
                    var p = P(u, v);
                    float d = Circle(p, Vector2.zero, 0.07f);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3f + Mathf.PI * 0.5f;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        var side = new Vector2(-dir.y, dir.x);
                        d = Mathf.Min(d, Segment(p, Vector2.zero, dir * 0.43f, 0.035f));
                        var b0 = dir * 0.25f;
                        d = Mathf.Min(d, Segment(p, b0, b0 + (dir + side) * 0.085f, 0.025f));
                        d = Mathf.Min(d, Segment(p, b0, b0 + (dir - side) * 0.085f, 0.025f));
                    }
                    return Inside(d, 0.045f);
                }

                case VfxBuiltinMask.Skull:
                {
                    // 둥근 머리 + 턱, 눈·코 구멍과 이빨 홈을 뚫음
                    var p = P(u, v);
                    float d = Circle(p, new Vector2(0f, 0.07f), 0.3f);
                    d = SMin(d, Box(p, new Vector2(0f, -0.21f), new Vector2(0.15f, 0.12f)) - 0.03f, 0.05f);
                    d = Mathf.Max(d, -Circle(p, new Vector2(-0.115f, 0.04f), 0.08f));
                    d = Mathf.Max(d, -Circle(p, new Vector2(0.115f, 0.04f), 0.08f));
                    d = Mathf.Max(d, -Tri(p, new Vector2(-0.04f, -0.12f), new Vector2(0.04f, -0.12f), new Vector2(0f, -0.04f)));
                    for (int k = -1; k <= 1; k++)
                        d = Mathf.Max(d, -Box(p, new Vector2(k * 0.065f, -0.29f), new Vector2(0.012f, 0.06f)));
                    return Inside(d, 0.07f);
                }

                case VfxBuiltinMask.ArrowDown:
                {
                    // 굵은 몸통 + 아래를 향한 화살촉
                    var p = P(u, v);
                    float d = Box(p, new Vector2(0f, 0.17f), new Vector2(0.085f, 0.26f)) - 0.02f;
                    d = SMin(d, Tri(p, new Vector2(-0.29f, -0.05f), new Vector2(0.29f, -0.05f), new Vector2(0f, -0.44f)), 0.02f);
                    return Inside(d, 0.1f);
                }

                case VfxBuiltinMask.Star5:
                {
                    var p = P(u, v);
                    return Inside(Star5(p, 0.45f, 0.42f), 0.12f);
                }

                case VfxBuiltinMask.Droplet:
                {
                    // 아래는 둥글고 위로 뾰족 — 떨어지는 핏방울·물방울
                    var p = P(u, v);
                    float d = Circle(p, new Vector2(0f, -0.12f), 0.25f);
                    d = SMin(d, Tri(p, new Vector2(-0.215f, -0.03f), new Vector2(0.215f, -0.03f), new Vector2(0f, 0.42f)), 0.03f);
                    return Inside(d, 0.13f);
                }

                case VfxBuiltinMask.Ring:
                {
                    float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                    return Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(d - 0.75f) / 0.2f), 0.8f);
                }
            }
            return 0f;
        }
    }
}
