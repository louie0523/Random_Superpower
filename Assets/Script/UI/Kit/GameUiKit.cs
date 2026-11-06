using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI 도구 — v7 '카툰 그래피티' (2026-11-04 시안) + v2 '스티커 카툰'.
///   v7 (메뉴 창 전부 — 설정 · 로비 · 인벤토리 · F1 · Tab · 상점 …): 그림자 없음 · 검정 외곽선 · 오린 판(Slab) ·
///      기울어진 버튼(Para) · 스프레이 밑줄 · 빨간 이름표. 그림은 UiGraffitiArt가 코드로 그림.
///   게임 HUD는 v2 그대로 — HudUiBuilder가 'using (GameUiKit.Classic())' 안에서 만듦.
///   테마(UiTheme)의 'Graffiti'를 끄면 전부 v2.
/// ── 아래는 v2 설명 ── 색은 UiTheme(색 배합 에셋)에서 — 바꾸면 전부 따라감.
///   칸 · 창은 조용하게: 테두리 없음, 어두운 판 + 모서리 두 곳 잘림(Cut), 부드러운 그림자(Shadow)
///   카툰은 아이콘 · 스티커에만 (육각 Hex · UiIcons)
///   색은 쓰임새대로: 민트 = 기본 강조 · 노랑(Gold) = 랜덤 · SC · 뽑기 · 빨강 = 위험 · 나머지 회색 · 흰색
/// 예전 이름(Ink · Paper · Yellow …)은 그대로 두고 값만 v2로 — 빌더 · 화면 코드는 안 고쳐도 새 모양이 됨.
///   Ink = 판 위의 글자(밝음) · Paper = 창 · Soft = 칸 · Yellow = 주요 버튼(민트) · Color.white를 면으로 넘기면 Raised(보통 버튼)
/// 프리팹 만들기(GameUiBuilders · SettingsUiBuilders)와 뷰들이 같이 씀.
/// </summary>
public static class GameUiKit
{
    // ══════════════════ 색 — UiTheme (Resources/UiTheme)에서 ══════════════════

    static UiTheme T => UiTheme.Current;

    // ══════════════════ v7 / v2 고르기 ══════════════════

    static int _classic;

    /// <summary>v7 그래피티 모양으로 만드는 중인지 — 테마에서 켜져 있고 Classic() 범위 밖이면 true</summary>
    public static bool Graffiti => _classic == 0 && T.Graffiti;

    /// <summary>이 범위 안에서는 v2 모양 (게임 HUD) — using (GameUiKit.Classic()) { ... }</summary>
    public static ClassicScope Classic()
    {
        _classic++;
        return new ClassicScope(true);
    }

    public readonly struct ClassicScope : System.IDisposable
    {
        readonly bool _on;
        public ClassicScope(bool on) { _on = on; }
        public void Dispose() { if (_on && _classic > 0) _classic--; }
    }

    static bool G => Graffiti;

    public static Color Bg => G ? T.GWall : T.Background;                 // 화면 바탕
    public static Color Panel => G ? T.GPanel : T.Panel;                  // 창
    public static Color Panel2 => G ? T.GRow : T.Panel2;                  // 칸
    public static Color Raised => G ? T.GButton : T.Raised;               // 보통 버튼
    public static Color Field => G ? T.GDeep : T.Field;                   // 막대 바닥 (v7 입력 칸은 종이)
    public static Color LineColor => G ? new Color(0.353f, 0.322f, 0.4f, 1f) : T.Line;   // 구분선 · 대기 표시
    public static Color Light => G ? T.GPaper : T.StickerRim;             // 흰 스티커 · 어두운 면 위 흰 글자
    public static Color Dim => G ? T.GTextDim : T.TextDim;                // 흐린 글자
    public static Color Dark => G ? T.GInk : T.TextOnLight;               // 밝은 면 위 글자
    public static Color Mint => G ? T.GTeal : T.Accent;                   // 기본 강조 · 고르기
    public static Color MintDark => G ? T.GTealDark : T.AccentDark;       // 켜진 줄 바탕
    public static Color Gold => G ? T.GYellow : T.Gold;                   // 랜덤 · SC · 소울 · 실행
    public static Color Danger => G ? T.GRed : T.Danger;                  // 위험 · 체력
    public static Color Ok => G ? T.GTeal : T.Ok;                         // 준비 · 성공
    public static Color Info => G ? new Color(0.247f, 0.663f, 1f, 1f) : T.Info;   // 보조
    public static Color HudPanel => T.HudPanel;                           // HUD 글자 판

    // ── v7 전용 ──
    public static Color InkLine => T.GInk;          // 외곽선 검정
    public static Color PaperFace => T.GPaper;      // 종이
    public static Color Pink => T.GPink;
    public static Color Purple => T.GPurple;
    public static Color Orange => T.GOrange;
    public static Color BluePanel => T.GBlue;       // 줄무늬 파란 판

    // ── 예전 이름 → 테마 값 (빌더 · 화면 코드 안 고쳐도 됨) ──
    public static Color Ink => G ? T.GText : T.Text;      // 판 위의 글자
    public static Color Paper => Panel;                   // 창 바탕
    public static Color Soft => Panel2;                   // 칸 바탕
    public static Color Muted => Dim;                     // 흐린 글자
    public static Color Yellow => G ? T.GYellow : T.Accent;   // 주요 버튼 (v7 = 노랑 · v2 = 민트)
    public static Color Red => Danger;
    public static Color Blue => Info;
    public static Color Green => Ok;
    public static Color Sky => Bg;                        // 로비 바탕
    public static Color DimColor => G ? T.GBackdrop : T.Backdrop;
    public static Color CheckOn => MintDark;

    public const float Line = 0f;      // v2 — 테두리 없음 (예전 호출 호환용)
    public const float Drop = 8f;      // 그림자 거리

    /// <summary>면 색 정리 — Color.white(예전 '보통 흰 버튼')는 Raised로</summary>
    public static Color Face(Color c) => c == Color.white ? Raised : c;

    /// <summary>이 면 위에 올릴 글자 색 — 밝은 면이면 검정, 어두운 면이면 밝음</summary>
    public static Color TextOn(Color face)
    {
        float l = 0.2126f * face.r + 0.7152f * face.g + 0.0722f * face.b;
        if (G) return l > T.GLightFace ? T.GInk : T.GText;
        return l > T.LightFaceThreshold ? T.TextOnLight : T.TextOnDark;
    }

    // ══════════════════ 모양 (스프라이트) ══════════════════

    public static Sprite Round => AbilityUi.Rounded;
    public static Sprite Circle => AbilityUi.Circle;

    /// <summary>에디터 프리팹 생성기가 PNG 에셋으로 바꿔 끼움 — 비어 있으면 코드로 만든 것</summary>
    public static Sprite CutOverride, ShadowOverride, HexOverride;
    static Sprite _cut, _shadow, _hex;

    public const int CutSize = 64, CutCorner = 14, CutBorder = 18;
    public const int ShadowSize = 64, ShadowFade = 22, ShadowBorder = 26;

    /// <summary>오른쪽 위 · 왼쪽 아래 모서리가 잘린 판 (9-slice) — 창 · 버튼 · 칸</summary>
    public static Sprite Cut
    {
        get
        {
            if (G) return Slab;                 // v7 — 오린 판 + 외곽선
            if (CutOverride != null) return CutOverride;
            if (_cut != null) return _cut;
            var t = CutTexture();
            return _cut = Sprite.Create(t, new UnityEngine.Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0,
                                        SpriteMeshType.FullRect, new Vector4(CutBorder, CutBorder, CutBorder, CutBorder));
        }
    }

    /// <summary>부드러운 그림자 (9-slice) — 검정 반투명으로 칠해서 판 뒤에</summary>
    public static Sprite Shadow
    {
        get
        {
            if (ShadowOverride != null) return ShadowOverride;
            if (_shadow != null) return _shadow;
            var t = ShadowTexture();
            return _shadow = Sprite.Create(t, new UnityEngine.Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0,
                                           SpriteMeshType.FullRect, new Vector4(ShadowBorder, ShadowBorder, ShadowBorder, ShadowBorder));
        }
    }

    // ══════════════════ v7 그림 (UiGraffitiArt) ══════════════════

    /// <summary>에디터 프리팹 생성기가 PNG 에셋으로 바꿔 끼움 — 비어 있으면 코드로 그린 것</summary>
    public static Sprite SlabOverride, PlainOverride, ParaOverride, DashOverride, SprayOverride, SprayDripOverride,
                         SplatOverride, GrainOverride, StripeOverride, DotsOverride, PipOnOverride, PipOffOverride,
                         DollOverride, DollShirtOverride, TriOverride, SprayLongOverride, SprayFillOverride;
    static Sprite _sprayLong;
    static Sprite _slab, _plain, _para, _dash, _spray, _sprayDrip, _splat, _grain, _stripe, _dots, _pipOn, _pipOff, _doll, _dollShirt, _tri;

    static Sprite Art(ref Sprite cache, Sprite ov, System.Func<Texture2D> draw, Vector4 border)
    {
        if (ov != null) return ov;
        if (cache != null) return cache;
        var t = draw();
        return cache = Sprite.Create(t, new UnityEngine.Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0,
                                     SpriteMeshType.FullRect, border);
    }

    static Vector4 B(float b) => new(b, b, b, b);
    public static Vector4 ParaBorder => new(UiGraffitiArt.ParaBorderX, UiGraffitiArt.ParaBorderY, UiGraffitiArt.ParaBorderX, UiGraffitiArt.ParaBorderY);

    /// <summary>오린 판 + 외곽선 (오른쪽 아래 잘림) — 창 · 큰 판</summary>
    public static Sprite Slab => Art(ref _slab, SlabOverride, () => UiGraffitiArt.Slab(true), B(UiGraffitiArt.SlabBorder));
    /// <summary>오린 판 (안 잘림) — 입력 칸 · 체크칸 · 작은 칸</summary>
    public static Sprite Plain => Art(ref _plain, PlainOverride, () => UiGraffitiArt.Slab(false, 8), B(UiGraffitiArt.SlabBorder));
    /// <summary>기울어진 평행사변형 + 외곽선 — 버튼 · 탭 · 줄 · 이름표</summary>
    public static Sprite Para => Art(ref _para, ParaOverride, () => UiGraffitiArt.Para(), ParaBorder);
    /// <summary>점선 테두리 (빈 칸) — Tiled</summary>
    public static Sprite Dash => Art(ref _dash, DashOverride, UiGraffitiArt.Dash, B(UiGraffitiArt.DashBorder));
    public static Sprite Spray => Art(ref _spray, SprayOverride, () => UiGraffitiArt.Spray(false), Vector4.zero);
    public static Sprite SprayDrip => Art(ref _sprayDrip, SprayDripOverride, () => UiGraffitiArt.Spray(true, 9), Vector4.zero);
    /// <summary>긴 스프레이 (4:1 · 흘러내림) — 로고 밑 같은 긴 자국. 늘려 쓰지 말고 비슷한 비율로</summary>
    public static Sprite SprayLong => Art(ref _sprayLong, SprayLongOverride, () => UiGraffitiArt.Spray(true, 11, 1024), Vector4.zero);
    static Sprite _sprayFill;
    /// <summary>스프레이로 채운 직사각형 (8:5) — 그림 칸 바탕</summary>
    public static Sprite SprayFill => Art(ref _sprayFill, SprayFillOverride, () => UiGraffitiArt.SprayFill(), Vector4.zero);
    public static Sprite Splat => Art(ref _splat, SplatOverride, () => UiGraffitiArt.Splat(), Vector4.zero);
    public static Sprite Grain => Art(ref _grain, GrainOverride, UiGraffitiArt.Grain, Vector4.zero);
    public static Sprite Stripe => Art(ref _stripe, StripeOverride, UiGraffitiArt.Stripe, Vector4.zero);
    public static Sprite Dots => Art(ref _dots, DotsOverride, UiGraffitiArt.Dots, Vector4.zero);
    public static Sprite PipOn => Art(ref _pipOn, PipOnOverride, () => UiGraffitiArt.Pip(true), Vector4.zero);
    public static Sprite PipOff => Art(ref _pipOff, PipOffOverride, () => UiGraffitiArt.Pip(false), Vector4.zero);
    public static Sprite Doll => Art(ref _doll, DollOverride, UiGraffitiArt.Doll, Vector4.zero);
    public static Sprite DollShirt => Art(ref _dollShirt, DollShirtOverride, UiGraffitiArt.DollShirt, Vector4.zero);
    /// <summary>오른쪽 세모 (고르기 화살표 — 왼쪽은 돌려서)</summary>
    public static Sprite Tri => Art(ref _tri, TriOverride, UiGraffitiArt.Tri, Vector4.zero);

    // ── v7 외곽선 굵기 (Pixels Per Unit Multiplier — 클수록 선이 가늘어짐) ──
    public const float PpuPanel = 2f;       // 판 · 띠 · 버튼 (Back · Face …)
    public const float PpuCheck = 6f;       // 체크칸 · 체크
    public const float PpuDots = 0.7f;      // 망점 (1보다 작으면 점이 커짐 — 0~1)
    public const float PpuGrain = 0.5f;     // 벽 알갱이
    /// <summary>망점 진하기 — 잘 보이기보다 옅게 깔리는 느낌 (모든 망점에 곱함)</summary>
    public const float DotsFade = 0.45f;

    /// <summary>이 그림 · 이름에 맞는 Pixels Per Unit Multiplier (v7)</summary>
    public static float PpuFor(Sprite s, string name)
    {
        if (s == null) return 1f;
        if (s == Para || s == Slab || s == Plain)
            return name == "CheckBox" || name == "Check" ? PpuCheck : PpuPanel;
        if (s == Dots) return PpuDots;
        if (s == Grain) return PpuGrain;
        if (s == Stripe) return PpuDots;
        return 1f;      // 칸 막대(Pip)는 1 그대로 — 칸 폭 계산이 1 기준
    }

    /// <summary>
    /// 다 만든 화면 한 번 훑어서 v7 굵기 맞추기 — Img()를 안 거치고 sprite를 직접 넣은 칸까지 (빌더 끝에서 부름)
    /// </summary>
    public static void Finish(Transform root)
    {
        if (!G || root == null) return;
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            var sp = img.sprite;
            if (sp == null) continue;
            if (img.type != Image.Type.Sliced && img.type != Image.Type.Tiled) continue;
            float want = PpuFor(sp, img.name);
            if (!Mathf.Approximately(want, 1f) && Mathf.Approximately(img.pixelsPerUnitMultiplier, 1f))
                img.pixelsPerUnitMultiplier = want;       // 직접 바꾼 값(1이 아닌 것)은 그대로 둠
        }
    }

    /// <summary>반복 무늬 그림 (Image 'Tiled')</summary>
    public static bool IsTiled(Sprite s) =>
        s != null && (s == Dash || s == Grain || s == Stripe || s == Dots || s == PipOn || s == PipOff);

    public static Sprite SlantOverride;
    static Sprite _slant;

    /// <summary>기울어진 막대 (체력 · 쉴드 막대) — Filled로 써도 됨</summary>
    public static Sprite Slant
    {
        get
        {
            if (SlantOverride != null) return SlantOverride;
            if (_slant != null) return _slant;
            var t = SlantTexture();
            return _slant = Sprite.Create(t, new UnityEngine.Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
        }
    }

    public static Texture2D SlantTexture()
    {
        const int W = 512, H = 32; const float S = 10f;      // 위가 오른쪽으로 S만큼 밀림
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float off = S * (y + 0.5f) / H;
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f;
                float a = Mathf.Clamp01(Mathf.Min(fx - off, (W - S + off) - fx) + 0.5f);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    /// <summary>가로로 넓은 육각형 — 키 이름표 · 스티커 바탕</summary>
    public static Sprite Hex
    {
        get
        {
            if (HexOverride != null) return HexOverride;
            if (_hex != null) return _hex;
            var t = HexTexture();
            return _hex = Sprite.Create(t, new UnityEngine.Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
        }
    }

    public static Texture2D CutTexture()
    {
        int N = CutSize; float C = CutCorner;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        const float R2 = 1.41421356f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float tr = ((N - fx) + (N - fy) - C) / R2;     // 오른쪽 위
                float bl = (fx + fy - C) / R2;                 // 왼쪽 아래
                float a = Mathf.Clamp01(Mathf.Min(tr, bl) + 0.5f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    public static Texture2D ShadowTexture()
    {
        int N = ShadowSize; float F = ShadowFade;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(F - (x + 0.5f), (x + 0.5f) - (N - F)));
                float dy = Mathf.Max(0f, Mathf.Max(F - (y + 0.5f), (y + 0.5f) - (N - F)));
                float d = Mathf.Sqrt(dx * dx + dy * dy) / F;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);              // 부드럽게
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    public static Texture2D HexTexture()
    {
        const int W = 128, H = 112;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pts = new Vector2[] { new(W * 0.25f, 0), new(W * 0.75f, 0), new(W, H * 0.5f), new(W * 0.75f, H), new(W * 0.25f, H), new(0, H * 0.5f) };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float m = float.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % 6];
                    var e = (b - a).normalized;
                    var n = new Vector2(-e.y, e.x);        // 반시계 → 안쪽
                    m = Mathf.Min(m, Vector2.Dot(p - a, n));
                }
                float al = Mathf.Clamp01(m + 0.5f);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(al * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ══════════════════ 글꼴 ══════════════════

    /// <summary>제목 글꼴 — UiPrefabSet의 '제목 글꼴'(예: Black Han Sans), 없으면 본문 글꼴</summary>
    public static TMP_FontAsset Display(TMP_FontAsset fallback)
    {
        var set = UiPrefabSet.Get();
        return set != null && set.DisplayFont != null ? set.DisplayFont : fallback;
    }

    // ══════════════════ 기본 ══════════════════

    /// <summary>스티커 · 칩 기울기 — 테마의 '살짝 기울이기'가 꺼져 있으면 0</summary>
    public static Quaternion Tilt(float degrees) =>
        Quaternion.Euler(0f, 0f, T.TiltStickers ? degrees : 0f);

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;   // UI
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Fill(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    public static void Center(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>레이아웃 크기 (-1 = 안 건드림)</summary>
    public static LayoutElement Size(Component c, float w = -1f, float h = -1f, float flexW = -1f, float flexH = -1f)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
        if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
        if (flexW >= 0f) le.flexibleWidth = flexW;
        if (flexH >= 0f) le.flexibleHeight = flexH;
        return le;
    }

    public static Image Img(string name, Transform parent, Sprite sprite, Color color)
    {
        var rt = Rect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null)
        {
            if (IsTiled(sprite)) img.type = Image.Type.Tiled;
            else if (sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            if (G) img.pixelsPerUnitMultiplier = PpuFor(sprite, name);
        }
        return img;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color,
                                       TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
    {
        var rt = Rect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        if (bold) t.fontStyle = FontStyles.Bold;
        return t;
    }

    /// <summary>
    /// 판: root 안에 (그림자) · 면을 깔고 면을 돌려줌 — 내용은 면 안에.
    /// v2: 테두리 없음 (line은 무시), drop > 0이면 아래로 살짝 퍼지는 그림자. 면은 모서리 잘린 모양
    /// </summary>
    public static Image Sticker(RectTransform root, Color face, float line = Line, float drop = Drop)
    {
        if (G)
        {
            var g = Img("Face", root, Slab, Face(face));      // v7 — 그림자 없음 · 외곽선은 그림에
            Fill(g.rectTransform);
            return g;
        }
        if (drop > 0f)
        {
            var sh = Img("Shadow", root, Shadow, new Color(0f, 0f, 0f, Mathf.Clamp(0.22f + drop * 0.02f, 0.25f, 0.5f)));
            Fill(sh.rectTransform);
            float spread = 6f + drop;
            sh.rectTransform.offsetMin = new Vector2(-spread, -spread - drop * 0.7f);
            sh.rectTransform.offsetMax = new Vector2(spread, spread - drop * 0.7f);
        }
        var f = Img("Face", root, Cut, Face(face));
        Fill(f.rectTransform);
        return f;
    }

    /// <summary>육각 스티커 이름표 (키 이름 · 짧은 표시) — 흰 테두리 + 색 면 + 글자</summary>
    public static Image HexTag(string name, Transform parent, TMP_FontAsset font, string label, Color face, float w = 64f, float h = 56f)
    {
        var rt = Rect(name, parent);
        Size(rt, w, h);
        var rim = Img("Rim", rt, Hex, G ? InkLine : Light);
        Fill(rim.rectTransform);
        var f = Img("Face", rim.transform, Hex, face);
        Fill(f.rectTransform, 4f);
        var t = Text("Label", f.transform, font, h * 0.34f, TextOn(face), TextAlignmentOptions.Center, true);
        Fill(t.rectTransform, 2f);
        t.text = label;
        return f;
    }

    // ══════════════════ 화면 · 창 ══════════════════

    /// <summary>
    /// 캔버스 + 전체 화면 Root (+ 어둡게). 기본은 오버레이.
    /// matchCamera = 실행 중 씬 UI 캔버스가 스크린 카메라 모드면 같이 카메라 모드 (HUD)
    /// </summary>
    public static RectTransform MakeScreen(GameObject host, int sortingOrder, Color? backdrop, bool matchCamera = false)
    {
        if (host.GetComponent<RectTransform>() == null) host.AddComponent<RectTransform>();
        host.layer = 5;     // UI — 스크린 카메라 모드의 UI 카메라가 그리는 레이어
        var c = host.GetComponent<Canvas>();
        if (c == null) c = host.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortingOrder;
        var follow = host.GetComponent<UiCanvasFollow>();
        if (follow == null) follow = host.AddComponent<UiCanvasFollow>();
        follow.matchCamera = matchCamera;
        var sc = host.GetComponent<CanvasScaler>();
        if (sc == null) sc = host.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 1f;
        if (host.GetComponent<GraphicRaycaster>() == null) host.AddComponent<GraphicRaycaster>();

        var root = Rect("Root", host.transform);
        Fill(root);
        if (backdrop.HasValue)
        {
            bool wall = G && backdrop.Value.a >= 0.99f;      // 불투명 바탕 = 로비 같은 전체 화면 → 시멘트 벽
            var d = Img("Backdrop", root, null, wall ? T.GWall : backdrop.Value);
            d.raycastTarget = true;     // 뒤 클릭 막음
            Fill(d.rectTransform);
            if (wall) Wall(d.rectTransform);
        }
        return root;
    }

    /// <summary>v7 벽 — 알갱이 질감 + (테마 GDecor) 스프레이 · 물감 장식. 전부 바탕 자식이라 내용 뒤에 깔림</summary>
    public static void Wall(RectTransform back)
    {
        var grain = Img("Grain", back, Grain, Color.white);
        Fill(grain.rectTransform);
        if (!T.GDecor) return;
        Deco(back, SprayDrip, Pink, new Vector2(0f, 0f), new Vector2(330f, 150f), new Vector2(980f, 490f), -9f);
        Deco(back, SprayDrip, Mint, new Vector2(1f, 1f), new Vector2(-330f, -150f), new Vector2(860f, 430f), 6f, 0.85f);
        Deco(back, Splat, Gold, new Vector2(0f, 1f), new Vector2(120f, -300f), new Vector2(230f, 230f), 0f);
        Deco(back, Splat, Purple, new Vector2(1f, 0f), new Vector2(-150f, 250f), new Vector2(200f, 200f), 20f);
    }

    /// <summary>장식 그림 한 장 (앵커 · 위치 · 크기 · 기울기)</summary>
    public static Image Deco(Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size, float rot, float alpha = 1f)
    {
        var img = Img("Deco", parent, sprite, new Color(color.r, color.g, color.b, alpha));
        var r = img.rectTransform;
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        r.localRotation = Quaternion.Euler(0f, 0f, rot);
        return img;
    }

    /// <summary>스프레이 밑줄 · 강조 (레이아웃에서 빠짐) — 부모 아래쪽 절반에 깔림</summary>
    public static Image SprayUnder(Transform parent, Color color, string name = "Spray")
    {
        var img = Img(name, parent, Spray, color);
        var le = img.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var r = img.rectTransform;
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(1f, 0.62f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(-16f, -8f);
        r.offsetMax = new Vector2(12f, 2f);
        return img;
    }

    /// <summary>판 위에 흰 망점 (파란 판 — 카툰 느낌) — 외곽선 안쪽으로. 예전 이름 그대로 (빗금 대신 망점)</summary>
    public static Image StripesOn(Image face, float alpha = 0.15f)
    {
        var s = Img("Dots", face.transform, Dots, new Color(1f, 1f, 1f, (alpha + 0.05f) * DotsFade));
        Fill(s.rectTransform, 7f);
        s.transform.SetAsFirstSibling();
        var le = s.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        return s;
    }

    /// <summary>판 위에 망점 (카드)</summary>
    public static Image DotsOn(Image face, float alpha = 0.22f)
    {
        var s = Img("Dots", face.transform, Dots, new Color(0f, 0f, 0f, alpha * DotsFade));
        Fill(s.rectTransform, 7f);
        s.transform.SetAsFirstSibling();
        var le = s.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        return s;
    }

    /// <summary>
    /// 이름표 (기울어진 스티커 + 굵은 글자) — 글자 길이에 맞춰 폭이 정해짐 (부모 레이아웃이 있으면).
    /// 돌려주는 건 이름표 묶음, 글자는 out
    /// </summary>
    public static RectTransform Tag(Transform parent, TMP_FontAsset font, string label, Color face, float size, out TextMeshProUGUI text, string name = "Tag")
    {
        var rt = Rect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = G ? Para : Cut;
        img.type = Image.Type.Sliced;
        img.color = face;
        img.raycastTarget = false;
        if (G) img.pixelsPerUnitMultiplier = PpuPanel;
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(Mathf.RoundToInt(size * 0.9f + 8f), Mathf.RoundToInt(size * 0.9f + 8f), 3, 5);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        text = Text("Text", rt, Display(font), size, TextOn(face), TextAlignmentOptions.Center, false);
        text.fontStyle = G ? FontStyles.Italic : FontStyles.Normal;
        text.text = label;
        return rt;
    }

    /// <summary>제목 글자에 굵은 외곽선 (실행할 때 켜짐) — v7 제목</summary>
    public static void InkOutline(TMP_Text t, float width = 0.26f)
    {
        if (t == null || !G) return;
        var o = t.GetComponent<UiTextOutline>();
        if (o == null) o = t.gameObject.AddComponent<UiTextOutline>();
        o.Width = width;
        Color32 c = InkLine;
        o.Color = new Color32(c.r, c.g, c.b, 255);
    }

    /// <summary>창 — 돌려주는 건 루트(위치 · 크기), content = 내용 부모 (세로 쌓기)</summary>
    public static RectTransform Window(string name, Transform parent, float w, float h, out RectTransform content,
                                       int pad = 28, float spacing = 14f)
    {
        var rt = Rect(name, parent);
        Center(rt, 0f, 0f, w, h);
        var face = Sticker(rt, Paper, 0f, 14f);
        face.raycastTarget = true;
        var v = face.gameObject.AddComponent<VerticalLayoutGroup>();
        if (G) pad += 6;
        v.padding = new RectOffset(pad, pad, pad - 6, pad);
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        content = (RectTransform)face.transform;
        return rt;
    }

    /// <summary>제목 — 민트 마름모 + 큰 제목 글꼴. 마름모 이름 'Accent' (대상 선택 창 테마가 색을 바꿈)</summary>
    public static TextMeshProUGUI Header(Transform parent, TMP_FontAsset font, string title, float size = 40f)
    {
        if (G) return HeaderV7(parent, font, title, size);
        var rt = Rect("Header", parent);
        Size(rt, -1f, size * 1.35f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var acc = Img("Accent", rt, null, Mint);
        Size(acc, size * 0.42f, size * 0.42f);
        acc.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var t = Text("Title", rt, Display(font), size, Ink, TextAlignmentOptions.MidlineLeft, false);
        Size(t, -1f, size * 1.3f, 1f);
        t.text = title;
        return t;
    }

    /// <summary>
    /// v7 제목 — 기울어진 큰 글자 + 검정 외곽선 + 노랑 스프레이 밑줄('Accent').
    /// 글자 폭만큼만 차지 (TitleBox) — 오른쪽에 다른 걸 붙여도 됨
    /// </summary>
    static TextMeshProUGUI HeaderV7(Transform parent, TMP_FontAsset font, string title, float size)
    {
        var rt = Rect("Header", parent);
        Size(rt, -1f, size * 1.5f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var box = Rect("TitleBox", rt);
        var bh = box.gameObject.AddComponent<HorizontalLayoutGroup>();
        bh.padding = new RectOffset(6, 22, 0, 0);
        bh.childAlignment = TextAnchor.MiddleLeft;
        bh.childControlWidth = bh.childControlHeight = true;
        bh.childForceExpandWidth = bh.childForceExpandHeight = false;

        SprayUnder(box, Gold, "Accent");

        var t = Text("Title", box, Display(font), size * 1.1f, Ink, TextAlignmentOptions.MidlineLeft, false);
        t.fontStyle = FontStyles.Italic;
        t.overflowMode = TextOverflowModes.Overflow;
        Size(t, -1f, size * 1.4f);
        t.text = title;
        InkOutline(t);
        return t;
    }

    /// <summary>작은 제목 — v7: 빨간 이름표 / v2: 흰 마름모 + 글자</summary>
    public static TextMeshProUGUI Section(Transform parent, TMP_FontAsset font, string title)
    {
        if (G)
        {
            var row = Rect("Section", parent);
            Size(row, -1f, 50f);
            var rh = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rh.childAlignment = TextAnchor.LowerLeft;
            rh.padding = new RectOffset(2, 0, 6, 6);
            rh.childControlWidth = rh.childControlHeight = true;
            rh.childForceExpandWidth = rh.childForceExpandHeight = false;
            Tag(row, font, title, Danger, 21f, out var tt);
            return tt;
        }
        var rt = Rect("Section", parent);
        Size(rt, -1f, 44f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.LowerLeft;
        h.padding = new RectOffset(2, 0, 0, 4);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var dot = Img("Dot", rt, null, Light);
        Size(dot, 10f, 10f);
        dot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var t = Text("Text", rt, Display(font), 24f, Ink, TextAlignmentOptions.BottomLeft, false);
        Size(t, -1f, 34f, 1f);
        t.text = title;
        return t;
    }

    public static TextMeshProUGUI Note(Transform parent, TMP_FontAsset font, string text, float size = 19f)
    {
        var t = Text("Note", parent, font, size, Muted, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.text = text;
        return t;
    }

    /// <summary>세로 묶음</summary>
    public static VerticalLayoutGroup Group(string name, Transform parent, float spacing = 8f)
    {
        var rt = Rect(name, parent);
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    /// <summary>가로 줄 (높이 고정)</summary>
    public static HorizontalLayoutGroup Row(string name, Transform parent, float height, float spacing = 10f)
    {
        var rt = Rect(name, parent);
        Size(rt, -1f, height, -1f, 0f);     // 세로로 안 늘어나게 (남는 공간은 스크롤이 가짐)
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.padding = new RectOffset(0, 0, 0, 6);   // 그림자 자리
        return h;
    }

    // ══════════════════ 조작 ══════════════════

    /// <summary>버튼 — 면 색에 맞춰 글자 색이 저절로 (밝은 면 = 검정 글자). textColor는 예전 호출 호환용 (무시)</summary>
    public static Button Btn(string name, Transform parent, TMP_FontAsset font, string label, Color face, float fontSize,
                             out TextMeshProUGUI text, Color? textColor = null)
    {
        var rt = Rect(name, parent);
        var f = G ? ParaFace(rt, face) : Sticker(rt, face, 0f, 3f);
        f.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = f;
        Tint(b);
        text = Text("Label", f.transform, font, fontSize, TextOn(f.color), TextAlignmentOptions.Center, true);
        Fill(text.rectTransform, 4f);
        text.text = label;
        return b;
    }

    /// <summary>v7 기울어진 면 (버튼 · 줄 · 탭)</summary>
    public static Image ParaFace(RectTransform root, Color face)
    {
        var f = Img("Face", root, Para, Face(face));
        Fill(f.rectTransform);
        return f;
    }

    public static void Tint(Selectable s)
    {
        var cb = s.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);   // 올리면 살짝 밝게
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.06f;
        s.colors = cb;
        s.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    /// <summary>켜고 끄는 버튼. check = 왼쪽 체크칸 (켜면 민트 칸), 아니면 켜졌을 때 면이 민트</summary>
    public static UiOption Option(string name, Transform parent, TMP_FontAsset font, string label, bool check,
                                  float h = 48f, float fontSize = 22f)
    {
        var b = Btn(name, parent, font, label, Raised, fontSize, out var t);
        Size(b, -1f, h);
        var opt = b.gameObject.AddComponent<UiOption>();
        opt.Button = b;
        opt.Face = (Image)b.targetGraphic;
        opt.Label = t;

        if (check && G)
        {
            var box = Img("CheckBox", opt.Face.transform, Plain, PaperFace);
            var r = box.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(22f, 0f);
            r.sizeDelta = new Vector2(30f, 30f);
            var dot = Img("Check", box.transform, Plain, Mint);
            Fill(dot.rectTransform, 6f);
            opt.Check = dot;
            opt.OnFace = MintDark;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.rectTransform.offsetMin = new Vector2(64f, 4f);
        }
        else if (check)
        {
            var box = Img("CheckBox", opt.Face.transform, Cut, LineColor);
            var r = box.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(14f, 0f);
            r.sizeDelta = new Vector2(26f, 26f);
            var inner = Img("Inner", box.transform, Cut, Field);
            Fill(inner.rectTransform, 3f);
            var dot = Img("Check", inner.transform, Cut, Mint);
            Fill(dot.rectTransform, 3f);
            opt.Check = dot;
            opt.OnFace = MintDark;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.rectTransform.offsetMin = new Vector2(52f, 4f);
        }
        else opt.OnFace = Mint;

        opt.OffFace = Raised;
        opt.SetOn(false);
        return opt;
    }

    public static TMP_InputField Input(string name, Transform parent, TMP_FontAsset font, float fontSize, string placeholder, int limit)
    {
        var rt = Rect(name, parent);
        Image face;
        if (G)
        {
            face = Img("Face", rt, Plain, PaperFace);         // v7 — 종이 칸 + 외곽선, 검정 글자
            Fill(face.rectTransform);
        }
        else face = Sticker(rt, Field, 0f, 0f);
        face.raycastTarget = true;

        var area = Rect("Text Area", face.transform);
        Fill(area, G ? 14f : 12f);
        area.gameObject.AddComponent<RectMask2D>();

        Color txt = G ? InkLine : Ink;
        Color phc = G ? new Color(InkLine.r, InkLine.g, InkLine.b, 0.42f) : new Color(Dim.r, Dim.g, Dim.b, 0.6f);
        var ph = Text("Placeholder", area, font, fontSize, phc);
        Fill(ph.rectTransform);
        ph.fontStyle = FontStyles.Italic;
        ph.text = placeholder;

        var tx = Text("Text", area, font, fontSize, txt);
        Fill(tx.rectTransform);
        tx.overflowMode = TextOverflowModes.Overflow;

        var inp = rt.gameObject.AddComponent<TMP_InputField>();
        inp.textViewport = area;
        inp.textComponent = tx;
        inp.placeholder = ph;
        inp.targetGraphic = face;
        inp.characterLimit = limit;
        inp.pointSize = fontSize;
        if (font != null) inp.fontAsset = font;
        inp.customCaretColor = true;
        inp.caretColor = G ? InkLine : Mint;
        inp.caretWidth = 2;
        inp.selectionColor = new Color(Mint.r, Mint.g, Mint.b, 0.35f);
        Tint(inp);
        return inp;
    }

    public static Slider SliderBar(string name, Transform parent, float height = 34f)
    {
        if (G) return PipBar(name, parent, height);
        var rt = Rect(name, parent);
        Size(rt, -1f, height);

        var bg = Img("Back", rt, Cut, Field);
        Band(bg.rectTransform, 10f);
        bg.raycastTarget = true;

        var fillArea = Rect("Fill Area", rt);
        Band(fillArea, 10f);
        var fill = Img("Fill", fillArea, Cut, Mint);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = Vector2.zero;

        var handleArea = Rect("Handle Slide Area", rt);
        Fill(handleArea);
        handleArea.offsetMin = new Vector2(height * 0.35f, 0f);
        handleArea.offsetMax = new Vector2(-height * 0.35f, 0f);
        var handle = Img("Handle", handleArea, Hex, Light);
        handle.rectTransform.anchorMin = Vector2.zero;
        handle.rectTransform.anchorMax = new Vector2(0f, 1f);
        handle.rectTransform.sizeDelta = new Vector2(height * 0.8f, -height * 0.15f);
        var hf = Img("Face", handle.transform, Hex, Raised);
        Fill(hf.rectTransform, 3f);

        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = hf;
        s.direction = Slider.Direction.LeftToRight;
        Tint(s);
        return s;
    }

    /// <summary>v7 숫자 막대 — 기울어진 칸이 차오름 (Slider 그대로 · 손잡이 없음, 막대를 누르거나 끌면 됨)</summary>
    static Slider PipBar(string name, Transform parent, float height)
    {
        var rt = Rect(name, parent);
        Size(rt, -1f, Mathf.Max(height, UiGraffitiArt.PipH + 6f));

        var bg = Img("Back", rt, PipOff, new Color(Dim.r, Dim.g, Dim.b, 0.55f));
        Band(bg.rectTransform, UiGraffitiArt.PipH);
        bg.raycastTarget = true;

        var fillArea = Rect("Fill Area", rt);
        Band(fillArea, UiGraffitiArt.PipH);
        var fill = Img("Fill", fillArea, PipOn, Gold);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = Vector2.zero;

        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform;
        s.targetGraphic = bg;
        s.direction = Slider.Direction.LeftToRight;
        Tint(s);
        var snap = rt.gameObject.AddComponent<UiPipSnap>();     // 채움이 칸 중간에서 안 잘리게
        snap.Slider = s;
        snap.Fill = fill.rectTransform;
        snap.PipWidth = UiGraffitiArt.PipW;
        return s;
    }

    /// <summary>v7 숫자 막대 폭 — 칸 n개가 딱 들어가는 폭 (끝 칸이 잘리지 않게)</summary>
    public static float PipBarWidth(int pips) => pips * UiGraffitiArt.PipW;

    static void Band(RectTransform r, float h)
    {
        r.anchorMin = new Vector2(0f, 0.5f);
        r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(0f, -h * 0.5f);
        r.offsetMax = new Vector2(0f, h * 0.5f);
    }

    /// <summary>세로 스크롤 목록 — 돌려주는 건 Content (줄이 여기 쌓임)</summary>
    public static RectTransform Scroll(string name, Transform parent, out ScrollRect sr, float spacing = 10f)
    {
        var rt = Rect(name, parent);
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);          // 휠 · 끌기 받기
        sr = rt.gameObject.AddComponent<ScrollRect>();

        var vp = Rect("Viewport", rt);
        Fill(vp);
        vp.gameObject.AddComponent<RectMask2D>();

        var content = Rect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = new RectOffset(4, 12, 4, 14);       // 오른쪽 · 아래 = 그림자 자리
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vp;
        sr.content = content;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        return content;
    }

    // ══════════════════ 줄 ══════════════════

    /// <summary>목록 한 줄 — wholeButton = 줄 전체가 버튼, actionLabel = 오른쪽 버튼 (null이면 없음)</summary>
    public static UiListRow ListRow(string name, Transform parent, TMP_FontAsset font, bool wholeButton, string actionLabel,
                                    float h = 60f, bool withSub = true)
    {
        var rt = Rect(name, parent);
        Size(rt, -1f, h);
        var face = G ? ParaFace(rt, Panel2) : Sticker(rt, Raised, 0f, 3f);
        var row = rt.gameObject.AddComponent<UiListRow>();
        row.Face = face;

        if (wholeButton)
        {
            face.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = face;
            Tint(b);
            row.Button = b;
        }

        var hl = face.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = G ? new RectOffset(30, 26, 8, 8) : new RectOffset(18, 8, 6, 6);
        hl.spacing = 10f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;

        var label = Text("Label", face.transform, font, 24f, Ink, TextAlignmentOptions.MidlineLeft, true);
        Size(label, 100f, -1f, 1f);
        label.text = "이름";
        row.Label = label;
        if (withSub)
        {
            var sub = Text("Sub", face.transform, font, 20f, Muted, TextAlignmentOptions.MidlineRight);
            Size(sub, 150f);
            sub.text = "";
            row.Sub = sub;
        }

        if (actionLabel != null)
        {
            var a = Btn("Action", face.transform, font, actionLabel, Mint, 20f, out var at);
            Size(a, 150f);
            row.Action = a;
            row.ActionLabel = at;
        }
        return row;
    }

    /// <summary>값 줄: 이름 · [--] · [-] · 값 · [+] · [++]</summary>
    public static StepperRowView Stepper(string name, Transform parent, TMP_FontAsset font, string label, bool big,
                                         string minus = "-", string plus = "+")
    {
        var row = Row(name, parent, 54f, 8f);
        var view = row.gameObject.AddComponent<StepperRowView>();

        var lb = Text("Label", row.transform, font, 22f, Ink, TextAlignmentOptions.MidlineLeft, true);
        Size(lb, 220f, -1f, 1f);
        lb.text = label;
        view.Label = lb;

        if (big) view.BigMinus = Small("BigMinus", row.transform, font, "--");
        view.Minus = Small("Minus", row.transform, font, minus);

        var box = Rect("Value", row.transform);
        Size(box, 190f);
        var face = Sticker(box, Field, 0f, 0f);
        var val = Text("Text", face.transform, font, 22f, Ink, TextAlignmentOptions.Center, true);
        Fill(val.rectTransform, 4f);
        val.text = "0";
        view.Value = val;

        view.Plus = Small("Plus", row.transform, font, plus);
        if (big) view.BigPlus = Small("BigPlus", row.transform, font, "++");
        return view;
    }

    static Button Small(string name, Transform parent, TMP_FontAsset font, string label)
    {
        var b = Btn(name, parent, font, label, Raised, 22f, out _);
        Size(b, 54f);
        return b;
    }

    // ══════════════════ 실행 중 도우미 ══════════════════

    /// <summary>uGUI 클릭에 EventSystem이 필요 — 없으면 만듦</summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    /// <summary>누른 버튼이 '선택됨'으로 남으면 Enter · Space가 또 누름 → 버튼 선택 풀기 (입력 칸은 그대로)</summary>
    public static void DropButtonFocus()
    {
        var es = EventSystem.current;
        if (es == null) return;
        var sel = es.currentSelectedGameObject;
        if (sel == null || sel.GetComponent<TMP_InputField>() != null) return;
        es.SetSelectedGameObject(null);
    }

    /// <summary>글이 바뀌었을 때만 넣기</summary>
    public static void Set(TMP_Text t, string s)
    {
        if (t != null && t.text != s) t.text = s;
    }

    public static void Active(Component c, bool on)
    {
        if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
    }

    public static void Active(GameObject g, bool on)
    {
        if (g != null && g.activeSelf != on) g.SetActive(on);
    }

    public static void Interact(Selectable s, bool on)
    {
        if (s != null && s.interactable != on) s.interactable = on;
    }
}
