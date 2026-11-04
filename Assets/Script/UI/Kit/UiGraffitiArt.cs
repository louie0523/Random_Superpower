using UnityEngine;

/// <summary>
/// v7 '카툰 그래피티' 모양 그림 — 전부 코드로 그림 (PNG 없이도 돌아감).
/// 'UI 프리팹 만들기 창'이 만들 때 PNG로 저장해서 끼워 넣음 (프리팹이 그림을 기억하게).
///   Slab   = 칼로 오린 듯 살짝 울퉁불퉁한 판 + 검정 외곽선 (오른쪽 아래 모서리 잘림) — 창 · 칸
///   Plain  = 같은 판, 모서리 안 잘림 — 입력 칸 · 작은 칸
///   Para   = 기울어진 평행사변형 + 외곽선 — 버튼 · 탭 · 줄 · 이름표
///   Dash   = 점선 테두리만 (빈 칸)
///   Spray  = 스프레이로 쓱 그은 자국 (제목 밑줄 · 고른 탭) / SprayDrip = 아래로 흘러내림
///   Splat  = 물감 튄 자국
///   Grain · Stripe · Dots = 반복 무늬 (벽 질감 · 파란 판 줄무늬 · 망점)
///   PipOn · PipOff = 칸 막대 (소리 크기 같은 숫자)
///   Doll · DollShirt = 카툰 인형 (대기실 카드 · 인벤토리에서 3D 모델을 못 찍을 때) — 셔츠만 따로 색칠
/// 흰색으로 그리고 검정 외곽선은 검정 그대로 → Image 색을 곱하면 면만 그 색, 선은 검정으로 남음.
/// </summary>
public static class UiGraffitiArt
{
    // ══════════════════ 크기 (9-slice 테두리) ══════════════════

    public const int SlabSize = 128, SlabBorder = 40, SlabOutline = 5;
    public const int ParaW = 160, ParaH = 96, ParaSlant = 16, ParaBorderX = 40, ParaBorderY = 12, ParaOutline = 5;
    public const int DashSize = 64, DashBorder = 16;
    public const int PipW = 34, PipH = 24;

    static readonly Color32 Clear = new(255, 255, 255, 0);
    const byte InkV = 17;

    // ══════════════════ 기본 ══════════════════

    static Texture2D New(int w, int h)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
    }

    static Texture2D Done(Texture2D t, Color32[] px)
    {
        t.SetPixels32(px);
        t.Apply();
        return t;
    }

    /// <summary>부드러운 잡음 -0.5 ~ 0.5</summary>
    static float Noise(float x, float y, int seed, float f) =>
        Mathf.PerlinNoise(x * f + seed * 13.37f, y * f + seed * 7.11f) - 0.5f;

    static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>볼록 다각형 안쪽 거리 (+ = 안). 점은 반시계 순서 (y 위로)</summary>
    static float Inside(Vector2 p, Vector2[] pts)
    {
        float m = float.MaxValue;
        for (int i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            var e = (b - a).normalized;
            var n = new Vector2(-e.y, e.x);
            m = Mathf.Min(m, Vector2.Dot(p - a, n));
        }
        return m;
    }

    /// <summary>안쪽 거리 → 흰 면 + 검정 외곽선</summary>
    static Color32 Outlined(float d, float outline)
    {
        float a = Mathf.Clamp01(d + 0.5f);
        if (a <= 0f) return Clear;
        float ink = Mathf.Clamp01(outline - d + 0.5f);
        byte v = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, InkV, ink));
        return new Color32(v, v, v, (byte)(a * 255f));
    }

    static void Max(ref Color32 c, float a)
    {
        byte b = (byte)(Mathf.Clamp01(a) * 255f);
        if (b > c.a) c = new Color32(255, 255, 255, b);
    }

    // ══════════════════ 판 · 평행사변형 ══════════════════

    /// <summary>오린 판 — chamfer = 오른쪽 아래 모서리 잘림</summary>
    public static Texture2D Slab(bool chamfer, int seed = 3)
    {
        int W = SlabSize, H = SlabSize;
        const float mg = 4f, ch = 20f;
        var r = new System.Random(seed);
        float J() => (float)(r.NextDouble() * 3.0 - 1.5);
        var pts = chamfer
            ? new[] { new Vector2(mg + J(), mg + J()), new Vector2(W - mg - ch + J(), mg + J()), new Vector2(W - mg + J(), mg + ch + J()),
                      new Vector2(W - mg + J(), H - mg + J()), new Vector2(mg + J(), H - mg + J()) }
            : new[] { new Vector2(mg + J(), mg + J()), new Vector2(W - mg + J(), mg + J()),
                      new Vector2(W - mg + J(), H - mg + J()), new Vector2(mg + J(), H - mg + J()) };
        var t = New(W, H);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Inside(p, pts) + Noise(p.x, p.y, seed, 0.09f) * 2.6f;
                px[y * W + x] = Outlined(d, SlabOutline);
            }
        return Done(t, px);
    }

    /// <summary>기울어진 평행사변형 (위가 오른쪽으로) — 가로 9-slice</summary>
    public static Texture2D Para(int seed = 4)
    {
        int W = ParaW, H = ParaH;
        const float mg = 3f;
        var pts = new[] { new Vector2(mg, mg), new Vector2(W - mg - ParaSlant, mg), new Vector2(W - mg, H - mg), new Vector2(mg + ParaSlant, H - mg) };
        var t = New(W, H);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Inside(p, pts) + Noise(p.x, p.y, seed, 0.11f) * 1.8f;
                px[y * W + x] = Outlined(d, ParaOutline);
            }
        return Done(t, px);
    }

    /// <summary>점선 테두리만 (흰색) — Image 'Tiled'로 쓰면 점선이 늘어나지 않고 반복</summary>
    public static Texture2D Dash()
    {
        int N = DashSize;
        const float line = 3.5f, inset = 4f, period = 32f, on = 18f;
        var t = New(N, N);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dl = fx - inset, dr = N - inset - fx, db = fy - inset, dt = N - inset - fy;
                float dEdge = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
                float a = Mathf.Clamp01(0.5f - Mathf.Abs(dEdge - line * 0.5f) + line * 0.5f);
                if (a > 0f)
                {
                    bool vertical = Mathf.Min(dl, dr) < Mathf.Min(db, dt);
                    float along = vertical ? fy : fx;
                    bool corner = (fx < DashBorder || fx > N - DashBorder) && (fy < DashBorder || fy > N - DashBorder);
                    if (!corner && Mathf.Repeat(along - DashBorder, period) > on) a = 0f;
                }
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        return Done(t, px);
    }

    // ══════════════════ 스프레이 ══════════════════

    /// <summary>스프레이 자국 — 세 번 겹쳐 뿌린 몸통 + 번진 가장자리 + 점 + (흘러내림)</summary>
    public static Texture2D Spray(bool drips, int seed = 5)
    {
        int W = 512, H = drips ? 256 : 128;
        var r = new System.Random(seed);
        float top = H * (drips ? 0.62f : 0.5f);
        float band = H * (drips ? 0.62f : 0.9f);
        float rad = band * 0.30f;
        var a = new float[W * H];

        var strokes = new[] { (0f, 1f, 0f, 0f), (rad * 0.30f, 0.78f, 0.04f, 0.07f), (-rad * 0.32f, 0.72f, 0.08f, 0.02f) };
        for (int k = 0; k < strokes.Length; k++)
        {
            var (dy, kr, s0, s1) = strokes[k];
            float R = rad * kr;
            float x0 = R + W * s0 + 2f, x1 = W - R - W * s1 - 2f;
            float ph = (float)(r.NextDouble() * 6.28);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float yc = top + dy + Mathf.Sin(fx / W * 3.1f + ph) * band * 0.06f;
                    float wob = Noise(fx, k * 10f, seed + k, 0.035f) * R * 0.28f;
                    float cx = Mathf.Clamp(fx, x0, x1);
                    float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - yc) * (fy - yc)) - (R + wob);
                    float cov = 1f - Smooth(-1.5f, 1.5f, d);
                    float halo = d > 0f ? Mathf.Exp(-d / (R * 0.22f)) * 0.2f : 0f;
                    int i = y * W + x;
                    a[i] = Mathf.Max(a[i], Mathf.Max(cov, halo));
                }
        }

        // 가장자리 점
        int dots = W / 5;
        for (int n = 0; n < dots; n++)
        {
            float x = Mathf.Lerp(rad, W - rad, (float)r.NextDouble());
            float side = r.NextDouble() < 0.5 ? -1f : 1f;
            float dd = rad * (1f + Mathf.Abs(Gauss(r) * 0.45f));
            float y = top + side * dd;
            float rr = Mathf.Max(0.7f, Mathf.Lerp(0.8f, 2.6f, (float)r.NextDouble()) * (1.4f - (dd / rad - 1f)));
            Disc(a, W, H, x, y, rr);
        }

        if (drips)
        {
            int n = Mathf.Max(2, W / 120);
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(rad * 1.5f, W - rad * 1.5f, (float)r.NextDouble());
                float dw = Mathf.Lerp(3f, 6f, (float)r.NextDouble());
                float ln = Mathf.Lerp(0.25f, 0.9f, (float)r.NextDouble()) * (top - rad * 0.5f);
                float y0 = top - rad * 0.6f, y1 = Mathf.Max(dw, y0 - ln);
                Drip(a, W, H, x, y0, y1, dw);
            }
        }
        return FromAlpha(a, W, H);
    }

    /// <summary>물감 튄 자국 (가운데 덩어리 + 둘레 점 + 흘러내림)</summary>
    public static Texture2D Splat(int seed = 3)
    {
        const int N = 256;
        var r = new System.Random(seed);
        var a = new float[N * N];
        float c = N * 0.5f, cy = N * 0.58f;

        // 덩어리 — 고르지 않은 둘레 (무작위 파형 몇 개)
        var amp = new float[4];
        var ph = new float[4];
        int[] k = { 2, 3, 5, 7 };
        for (int i = 0; i < 4; i++) { amp[i] = Mathf.Lerp(0.04f, 0.12f, (float)r.NextDouble()); ph[i] = (float)(r.NextDouble() * 6.28); }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = x + 0.5f - c, fy = y + 0.5f - cy;
                float ang = Mathf.Atan2(fy, fx);
                float R = N * 0.15f;
                float m = 1f;
                for (int i = 0; i < 4; i++) m += amp[i] * Mathf.Sin(ang * k[i] + ph[i]);
                float d = Mathf.Sqrt(fx * fx + fy * fy) - R * m;
                a[y * N + x] = 1f - Smooth(-1f, 1f, d);
            }
        // 겹친 작은 덩어리
        for (int i = 0; i < 3; i++)
        {
            float t = (float)(r.NextDouble() * 6.28);
            Disc(a, N, N, c + Mathf.Cos(t) * N * 0.12f, cy + Mathf.Sin(t) * N * 0.1f, N * Mathf.Lerp(0.05f, 0.08f, (float)r.NextDouble()));
        }
        // 둘레 점
        for (int i = 0; i < 34; i++)
        {
            float t = (float)(r.NextDouble() * 6.28);
            float d = N * (0.2f + Mathf.Abs(Gauss(r) * 0.13f));
            float x = c + Mathf.Cos(t) * d, y = cy + Mathf.Sin(t) * d * 0.9f;
            float rr = Mathf.Max(1.2f, Mathf.Lerp(2f, 9f, (float)r.NextDouble()) * (1f - d / (N * 0.55f)));
            Disc(a, N, N, x, y, rr);
        }
        // 흘러내림
        for (int i = 0; i < 3; i++)
        {
            float x = c + Mathf.Lerp(-N * 0.1f, N * 0.1f, (float)r.NextDouble());
            float dw = Mathf.Lerp(5f, 9f, (float)r.NextDouble());
            Drip(a, N, N, x, cy, cy - Mathf.Lerp(0.15f, 0.45f, (float)r.NextDouble()) * N, dw);
        }
        return FromAlpha(a, N, N);
    }

    static float Gauss(System.Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
    }

    static void Disc(float[] a, int W, int H, float cx, float cy, float rr)
    {
        int x0 = Mathf.Max(0, (int)(cx - rr - 2)), x1 = Mathf.Min(W - 1, (int)(cx + rr + 2));
        int y0 = Mathf.Max(0, (int)(cy - rr - 2)), y1 = Mathf.Min(H - 1, (int)(cy + rr + 2));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float v = 1f - Smooth(rr - 0.8f, rr + 0.8f, Mathf.Sqrt(dx * dx + dy * dy));
                int i = y * W + x;
                if (v > a[i]) a[i] = v;
            }
    }

    /// <summary>흘러내린 줄 (위 y0 → 아래 y1) + 끝 방울</summary>
    static void Drip(float[] a, int W, int H, float x, float y0, float y1, float dw)
    {
        int xa = Mathf.Max(0, (int)(x - dw * 2)), xb = Mathf.Min(W - 1, (int)(x + dw * 2));
        int ya = Mathf.Max(0, (int)(y1 - dw * 2)), yb = Mathf.Min(H - 1, (int)(y0 + 1));
        for (int y = ya; y <= yb; y++)
            for (int xx = xa; xx <= xb; xx++)
            {
                float fx = xx + 0.5f, fy = y + 0.5f;
                float dseg = fy <= y0 && fy >= y1 ? Mathf.Abs(fx - x) - dw * 0.5f : 99f;
                float bulb = Mathf.Sqrt((fx - x) * (fx - x) + (fy - y1) * (fy - y1)) - dw * 0.78f;
                float v = 1f - Smooth(-0.8f, 0.8f, Mathf.Min(dseg, bulb));
                int i = y * W + xx;
                if (v > a[i]) a[i] = v;
            }
    }

    static Texture2D FromAlpha(float[] a, int W, int H)
    {
        var t = New(W, H);
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i]) * 255f));
        return Done(t, px);
    }

    // ══════════════════ 무늬 (반복) ══════════════════

    /// <summary>시멘트 벽 알갱이 — 흰 점 (아주 옅게) · Image 'Tiled'</summary>
    public static Texture2D Grain()
    {
        const int N = 256;
        var r = new System.Random(1);
        var t = New(N, N);
        t.wrapMode = TextureWrapMode.Repeat;
        var px = new Color32[N * N];
        for (int i = 0; i < px.Length; i++)
        {
            double v = r.NextDouble();
            byte al = (byte)(v > 0.55 ? (v - 0.55) * 60.0 : 0.0);
            byte c = (byte)(v > 0.8 ? 255 : 0);       // 밝은 점 · 어두운 점 섞기
            px[i] = new Color32(c, c, c, al);
        }
        return Done(t, px);
    }

    /// <summary>파란 판 위 대각선 줄무늬 (흰 반투명) · 이어 붙여도 끊김 없음</summary>
    public static Texture2D Stripe()
    {
        const int N = 64;
        var t = New(N, N);
        t.wrapMode = TextureWrapMode.Repeat;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = Mathf.Repeat(x + y + 0.5f, 32f);
                float a = Mathf.Clamp01(Mathf.Min(u, 14f - u) + 0.5f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        return Done(t, px);
    }

    /// <summary>망점 (카드 바탕)</summary>
    public static Texture2D Dots()
    {
        const int N = 14;
        var t = New(N, N);
        t.wrapMode = TextureWrapMode.Repeat;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - N * 0.5f, dy = y + 0.5f - N * 0.5f;
                float a = Mathf.Clamp01(2.4f - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        return Done(t, px);
    }

    /// <summary>칸 막대 한 칸 (오른쪽에 틈) — on = 흰 면 + 검정 선, off = 흰 선만</summary>
    public static Texture2D Pip(bool on)
    {
        int W = PipW, H = PipH;
        const float s = 4f, gap = 6f, mg = 1.5f;
        var pts = new[] { new Vector2(mg, mg), new Vector2(W - gap - s, mg), new Vector2(W - gap, H - mg), new Vector2(mg + s, H - mg) };
        var t = New(W, H);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float d = Inside(new Vector2(x + 0.5f, y + 0.5f), pts);
                if (on) px[y * W + x] = Outlined(d, 2.5f);
                else
                {
                    float a = Mathf.Clamp01(d + 0.5f) * Mathf.Clamp01(2.5f - d + 0.5f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
        return Done(t, px);
    }

    /// <summary>오른쪽을 가리키는 세모 (흰색 — 고르기 화살표)</summary>
    public static Texture2D Tri()
    {
        const int N = 32;
        var pts = new[] { new Vector2(6f, 3f), new Vector2(29f, 16f), new Vector2(6f, 29f) };
        var t = New(N, N);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Inside(new Vector2(x + 0.5f, y + 0.5f), pts) + Noise(x, y, 6, 0.2f) * 0.8f;
                px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(d + 0.5f) * 255f));
            }
        return Done(t, px);
    }

    // ══════════════════ 인형 ══════════════════

    static readonly Color32 Skin = new(242, 201, 160, 255), Pants = new(59, 74, 107, 255),
                            Shoes = new(139, 90, 60, 255), Hair = new(62, 42, 30, 255);

    const float DollScale = 0.5f;
    public const int DollW = 132, DollH = 280;

    /// <summary>카툰 인형 (셔츠 자리는 비어 있음 — 뒤에 DollShirt를 색칠해서 깔기)</summary>
    public static Texture2D Doll() => DrawDoll(false);

    /// <summary>인형 셔츠 모양 (흰색 — 색은 Image 색으로)</summary>
    public static Texture2D DollShirt() => DrawDoll(true);

    static Texture2D DrawDoll(bool shirtOnly)
    {
        int W = DollW, H = DollH;
        const float sw = 9f;
        var col = new Color[W * H];
        var t = New(W, H);

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // 그림 좌표 (264 × 560, y 아래로)
                float X = (x + 0.5f) / DollScale, Y = (H - (y + 0.5f)) / DollScale;
                float aa = 0.5f / DollScale;
                var c = new Color(1f, 1f, 1f, 0f);

                if (shirtOnly)
                {
                    float d = RR(X, Y, 56, 204, 152, 168, 28, 0f);
                    c = new Color(1f, 1f, 1f, Mathf.Clamp01(-d * DollScale + 0.5f));
                    col[y * W + x] = c;
                    continue;
                }

                Paint(ref c, RR(X, Y, 80, 352, 46, 150, 16, 0f), Pants, sw, aa);
                Paint(ref c, RR(X, Y, 138, 352, 46, 150, 16, 0f), Pants, sw, aa);
                Paint(ref c, RR(X, Y, 66, 488, 66, 40, 14, 0f), Shoes, sw, aa);
                Paint(ref c, RR(X, Y, 132, 488, 66, 40, 14, 0f), Shoes, sw, aa);
                Paint(ref c, RR(X, Y, 18, 214, 44, 132, 20, 8f), Skin, sw, aa);
                Paint(ref c, RR(X, Y, 202, 214, 44, 132, 20, -8f), Skin, sw, aa);
                Paint(ref c, RR(X, Y, 56, 204, 152, 168, 28, 0f), new Color32(0, 0, 0, 0), sw, aa);   // 셔츠 = 구멍
                Paint(ref c, RR(X, Y, 44, 34, 176, 164, 44, 0f), Skin, sw, aa);
                Paint(ref c, Mathf.Max(RR(X, Y, 40, 26, 184, 172, 48, 0f), Y - 84f), Hair, sw, aa);  // 머리카락 = 머리 윗부분
                Solid(ref c, RR(X, Y, 88, 112, 20, 30, 8, 0f), aa);
                Solid(ref c, RR(X, Y, 156, 112, 20, 30, 8, 0f), aa);
                float smile = Y > 156f ? Mathf.Abs(Mathf.Sqrt((X - 132f) * (X - 132f) + (Y - 136f) * (Y - 136f)) - 28f) - 3.5f : 99f;
                Solid(ref c, smile, aa);
                col[y * W + x] = c;
            }
        t.SetPixels(col);
        t.Apply();
        return t;
    }

    /// <summary>둥근 사각형 거리 (- = 안). x · y = 왼쪽 위, 회전은 도</summary>
    static float RR(float px, float py, float x, float y, float w, float h, float r, float rotDeg)
    {
        float cx = x + w * 0.5f, cy = y + h * 0.5f;
        float qx = px - cx, qy = py - cy;
        if (rotDeg != 0f)
        {
            float rad = -rotDeg * Mathf.Deg2Rad, cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            float rx = qx * cs - qy * sn, ry = qx * sn + qy * cs;
            qx = rx; qy = ry;
        }
        float ax = Mathf.Abs(qx) - w * 0.5f + r, ay = Mathf.Abs(qy) - h * 0.5f + r;
        float outside = Mathf.Sqrt(Mathf.Max(ax, 0f) * Mathf.Max(ax, 0f) + Mathf.Max(ay, 0f) * Mathf.Max(ay, 0f));
        return outside + Mathf.Min(Mathf.Max(ax, ay), 0f) - r;
    }

    /// <summary>면 + 외곽선으로 덮어 그리기 (SVG처럼 나중 것이 위)</summary>
    static void Paint(ref Color c, float d, Color32 fill, float sw, float aa)
    {
        float cover = Mathf.Clamp01(sw * 0.5f - d + aa);
        if (cover <= 0f) return;
        float inner = Mathf.Clamp01(-sw * 0.5f - d + aa);
        var ink = new Color(InkV / 255f, InkV / 255f, InkV / 255f, 1f);
        var shape = Color.Lerp(ink, fill, inner);
        c = Color.Lerp(c, shape, cover);
    }

    static void Solid(ref Color c, float d, float aa)
    {
        float cover = Mathf.Clamp01(-d + aa);
        if (cover <= 0f) return;
        c = Color.Lerp(c, new Color(InkV / 255f, InkV / 255f, InkV / 255f, 1f), cover);
    }
}
