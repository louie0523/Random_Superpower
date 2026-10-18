using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 능력 표시용 공통 — 이름 · 번호 · 요약 · 색 (AbilityData의 HUD 칸), 그리고 코드로 만드는 UI 조각들.
/// 클래스 선택 카드(ClassPickPanel)와 랜덤 능력 HUD(RandomAbilityHud)가 같이 씀.
/// </summary>
public static class AbilityUi
{
    // ══════════════════ 글 ══════════════════

    public static string Name(AbilityData a)
    {
        if (a == null) return LocUtil.GetOr("ui.ability.none", "-");
        return LocUtil.GetOr($"ability.{a.Id}.name", LocUtil.Resolve(a.DisplayName ?? a.name));
    }

    /// <summary>한 줄 요약 — 시트 ability.{Id}.summary → Summary 칸 → 플레이버</summary>
    public static string Summary(AbilityData a)
    {
        if (a == null) return "";
        string s = LocUtil.GetOr($"ability.{a.Id}.summary", LocUtil.Resolve(a.Summary ?? ""));
        if (string.IsNullOrWhiteSpace(s))
            s = LocUtil.GetOr($"ability.{a.Id}.flavor", LocUtil.Resolve(a.Flavor ?? ""));
        return s ?? "";
    }

    // ══════════════════ 번호 (NO. N / 진화 NO. N - 단계) ══════════════════

    /// <summary>번호 계산에 쓰는 데이터베이스 — HUD · 선택 화면이 알아서 넣음</summary>
    public static AbilityDatabase Database;

    static AbilityDatabase _mapDb;
    static readonly System.Collections.Generic.Dictionary<int, int> _evolvedFrom = new();   // 진화 결과 Id → 진화 전 Id

    /// <summary>
    /// 번호와 진화 단계.
    ///   번호: 처음(진화 전) 능력의 Number 칸, 비어 있으면 그 Id의 뒷자리 2개 (6012 → 12)
    ///   단계: 진화를 몇 번 거쳤는지 (EvolveToId로 이어진 횟수). 진화 전이면 0
    /// </summary>
    public static void GetNumber(AbilityData a, AbilityDatabase db, out int number, out int stage)
    {
        number = 0;
        stage = 0;
        if (a == null) return;
        if (db == null) db = Database;

        var root = a;
        if (db != null)
        {
            BuildEvolveMap(db);
            int guard = 0;
            while (guard++ < 16 && _evolvedFrom.TryGetValue(root.Id, out int prevId))
            {
                var prev = db.Get(prevId);
                if (prev == null || prev == a) break;
                root = prev;
                stage++;
            }
        }
        number = root.Number > 0 ? root.Number : Mathf.Abs(root.Id) % 100;
    }

    public static int Number(AbilityData a)
    {
        GetNumber(a, null, out int n, out _);
        return n;
    }

    /// <summary>"NO. 12" / 진화했으면 "NO. 12 - 1"</summary>
    public static string NumberText(AbilityData a, AbilityDatabase db = null)
    {
        if (a == null) return "";
        GetNumber(a, db, out int n, out int stage);
        string fmt = stage > 0
            ? LocUtil.GetOr("ui.ability.number_evolved", "NO. {0} - {1}")
            : LocUtil.GetOr("ui.ability.number", "NO. {0}");
        return fmt.Replace("{0}", n.ToString()).Replace("{1}", stage.ToString());
    }

    /// <summary>진화 연결을 다시 읽음 (에디터에서 EvolveToId를 바꾼 뒤 등)</summary>
    public static void InvalidateNumbers() => _mapDb = null;

    static void BuildEvolveMap(AbilityDatabase db)
    {
        if (_mapDb == db) return;
        _mapDb = db;
        _evolvedFrom.Clear();
        foreach (var x in AllOf(db))
            if (x != null && x.EvolveToId != 0 && x.EvolveToId != x.Id && !_evolvedFrom.ContainsKey(x.EvolveToId))
                _evolvedFrom[x.EvolveToId] = x.Id;
    }

    static System.Collections.Generic.IEnumerable<AbilityData> AllOf(AbilityDatabase db)
    {
        if (db.All != null && db.All.Length > 0) { foreach (var x in db.All) yield return x; yield break; }
        if (db.MainAbilities != null) foreach (var x in db.MainAbilities) yield return x;
        if (db.SubAbilities != null) foreach (var x in db.SubAbilities) yield return x;
        if (db.RandomAbilities != null) foreach (var x in db.RandomAbilities) yield return x;
    }

    static AbilityDatabase _dbCache;
    static AbilityRuntime _dbOf;

    /// <summary>AbilityRuntime이 가진 데이터베이스 (private 칸이라 한 번만 찾아 둠). 찾으면 Database에도 넣음</summary>
    public static AbilityDatabase DbOf(AbilityRuntime ar)
    {
        if (ar == null) return Database;
        if (_dbOf == ar && _dbCache != null) return _dbCache;
        _dbOf = ar;
        var f = typeof(AbilityRuntime).GetField("database", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        _dbCache = f != null ? f.GetValue(ar) as AbilityDatabase : null;
        if (_dbCache != null) Database = _dbCache;
        return _dbCache;
    }

    /// <summary>패시브(◆) · 스킬(•) 이름 목록</summary>
    public static string Details(AbilityData a)
    {
        if (a == null) return "";
        var sb = new System.Text.StringBuilder();
        if (a.Passives != null)
            foreach (var p in a.Passives)
            {
                if (p == null) continue;
                string n = LocUtil.Resolve(p.Name ?? "");
                if (!string.IsNullOrWhiteSpace(n)) sb.Append("◆ ").Append(n).Append('\n');
            }
        if (a.Skills != null)
            foreach (var s in a.Skills)
            {
                if (s == null) continue;
                string n = LocUtil.Resolve(s.Name ?? "");
                if (!string.IsNullOrWhiteSpace(n)) sb.Append("• ").Append(n).Append('\n');
            }
        return sb.ToString().TrimEnd();
    }

    static Color Or(Color c, Color fallback) => c.a > 0.001f ? c : fallback;

    public static Color Accent(AbilityData a) => a == null ? new Color(0.4f, 0.4f, 0.45f) : Or(a.UiAccent, new Color(0.82f, 0.16f, 0.18f));
    public static Color NameColor(AbilityData a) => a == null ? Color.white : Or(a.UiNameColor, Color.white);
    public static Color NumberColor(AbilityData a) => a == null ? Color.white : Or(a.UiNumberColor, new Color(1f, 0.84f, 0.22f));

    // ══════════════════ 코드로 만드는 UI ══════════════════

    static Sprite _rounded, _gradient, _circle;

    /// <summary>에디터 프리팹 생성기가 PNG 에셋으로 바꿔 끼움 — 비어 있으면 코드로 만든 것</summary>
    public static Sprite RoundedOverride, CircleOverride, GradientOverride;

    public const int RoundedSize = 64, RoundedRadius = 16;

    /// <summary>모서리 둥근 사각형 (9-slice)</summary>
    public static Sprite Rounded
    {
        get
        {
            if (RoundedOverride != null) return RoundedOverride;
            if (_rounded != null) return _rounded;
            var tex = RoundedTexture();
            int R = RoundedRadius;
            _rounded = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
            return _rounded;
        }
    }

    /// <summary>왼쪽 진하고 오른쪽으로 사라지는 띠</summary>
    public static Sprite Gradient
    {
        get
        {
            if (GradientOverride != null) return GradientOverride;
            if (_gradient != null) return _gradient;
            var tex = GradientTexture();
            _gradient = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _gradient;
        }
    }

    public static Sprite Circle
    {
        get
        {
            if (CircleOverride != null) return CircleOverride;
            if (_circle != null) return _circle;
            var tex = CircleTexture();
            _circle = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _circle;
        }
    }

    public static Texture2D RoundedTexture()
    {
        int N = RoundedSize, R = RoundedRadius;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, R, N - R), cy = Mathf.Clamp(y + 0.5f, R, N - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(R - d + 0.5f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    public static Texture2D GradientTexture()
    {
        const int W = 256, H = 4;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < W; x++)
        {
            float t = x / (float)(W - 1);
            float a = t < 0.55f ? 1f : Mathf.SmoothStep(1f, 0f, (t - 0.55f) / 0.45f);
            for (int y = 0; y < H; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    public static Texture2D CircleTexture()
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f));
                float a = Mathf.Clamp01(N * 0.5f - d);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>위-왼쪽 기준으로 놓기</summary>
    public static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    public static void Fill(RectTransform rt, float pad = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    public static Image Img(string name, Transform parent, Sprite sprite, Color color, bool sliced = false)
    {
        var rt = Rect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sliced) img.type = Image.Type.Sliced;
        return img;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color,
                                       TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
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
}