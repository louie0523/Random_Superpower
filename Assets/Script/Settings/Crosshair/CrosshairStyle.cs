using System;
using System.Text;
using UnityEngine;

public enum CrosshairKind
{
    Gun = 0,      // 총
    Bow = 1,      // 활 · 지팡이 · 투척
    Melee = 2,    // 근접 (맨손 포함)
}

/// <summary>
/// 조준점 한 벌 — 발로란트 / 오버워치처럼: 가운데 점 · 안쪽 선 · 바깥 선 · 원 · 테두리.
/// 크기 단위는 화면 픽셀(1080p 기준 — 화면이 크면 비례해서 커짐).
/// </summary>
[Serializable]
public class CrosshairStyle
{
    public Color Color = Color.white;

    [Header("테두리")]
    public bool Outline = true;
    public float OutlineOpacity = 0.5f;
    public int OutlineThickness = 1;

    [Header("가운데 점")]
    public bool Dot = false;
    public int DotSize = 2;
    public float DotOpacity = 1f;

    [Header("안쪽 선")]
    public bool Inner = true;
    public int InnerLength = 6;
    public int InnerThickness = 2;
    public int InnerOffset = 3;
    public float InnerOpacity = 0.8f;
    public bool InnerMove = true;     // 움직이면 벌어짐
    public bool InnerFire = true;     // 쏘면 벌어짐

    [Header("바깥 선")]
    public bool Outer = false;
    public int OuterLength = 2;
    public int OuterThickness = 2;
    public int OuterOffset = 10;
    public float OuterOpacity = 0.35f;
    public bool OuterMove = true;
    public bool OuterFire = true;

    [Header("원")]
    public bool Circle = false;
    public int CircleRadius = 12;
    public int CircleThickness = 2;
    public float CircleOpacity = 0.6f;
    public bool CircleMove = true;
    public bool CircleFire = true;

    [Header("기타")]
    [Tooltip("T자 — 위쪽 선 없음")]
    public bool TStyle = false;
    [Tooltip("벌어지는 세기 (0 = 안 벌어짐)")]
    public float ErrorScale = 1f;

    const string Prefix = "XH1:";

    public CrosshairStyle Clone() => JsonUtility.FromJson<CrosshairStyle>(JsonUtility.ToJson(this));

    public string ToCode() => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(this)));

    public static bool TryParse(string text, out CrosshairStyle style)
    {
        style = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        try
        {
            string json = text.StartsWith(Prefix)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(Prefix.Length)))
                : text.StartsWith("{") ? text : null;
            if (json == null) return false;
            style = JsonUtility.FromJson<CrosshairStyle>(json);
            return style != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>무기 종류별 기본 모양</summary>
    public static CrosshairStyle Default(CrosshairKind k)
    {
        var s = new CrosshairStyle();
        switch (k)
        {
            case CrosshairKind.Bow:
                s.Dot = true;
                s.InnerLength = 4;
                s.InnerOffset = 6;
                s.InnerMove = false;
                break;
            case CrosshairKind.Melee:
                s.Dot = true;
                s.DotSize = 4;
                s.Inner = false;
                break;
        }
        return s;
    }

    // ── 색 고르기 (발로란트 색 목록 비슷하게) ──
    public static readonly string[] ColorNames = { "흰색", "초록", "연두", "노랑", "청록", "분홍", "빨강", "검정" };
    public static readonly Color[] Colors =
    {
        Color.white, new(0f, 1f, 0f), new(0.68f, 1f, 0f), new(1f, 1f, 0f),
        new(0f, 1f, 1f), new(1f, 0.3f, 1f), new(1f, 0.15f, 0.15f), Color.black,
    };

    public int ColorIndex
    {
        get
        {
            for (int i = 0; i < Colors.Length; i++)
                if (Approximately(Colors[i], Color)) return i;
            return -1;      // 코드로 넣은 색
        }
    }

    static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
}

/// <summary>무기 종류별 조준점 저장 (PlayerPrefs). '모두 같은 조준점'이면 총 것을 다 씀</summary>
public static class CrosshairSettings
{
    const string P = "xh.";

    public static event Action Changed;

    static readonly CrosshairStyle[] _cache = new CrosshairStyle[3];

    public static bool ShareAll
    {
        get => PlayerPrefs.GetInt(P + "share", 0) != 0;
        set { PlayerPrefs.SetInt(P + "share", value ? 1 : 0); Changed?.Invoke(); }
    }

    /// <summary>조준점 보이기 (끄면 이 조준점을 안 그림)</summary>
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(P + "on", 1) != 0;
        set { PlayerPrefs.SetInt(P + "on", value ? 1 : 0); Changed?.Invoke(); }
    }

    /// <summary>설정 창에서 편집 중인 종류</summary>
    public static CrosshairKind Editing { get; set; } = CrosshairKind.Gun;

    /// <summary>실제로 쓰는 모양 (공유면 총 것)</summary>
    public static CrosshairStyle For(CrosshairKind k) => Get(ShareAll ? CrosshairKind.Gun : k);

    /// <summary>편집 대상 (공유면 총 것)</summary>
    public static CrosshairStyle Edit => For(Editing);

    public static CrosshairStyle Get(CrosshairKind k)
    {
        int i = (int)k;
        if (_cache[i] != null) return _cache[i];

        string json = PlayerPrefs.GetString(P + k, "");
        CrosshairStyle s = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { s = JsonUtility.FromJson<CrosshairStyle>(json); } catch { s = null; }
        }
        _cache[i] = s ?? CrosshairStyle.Default(k);
        return _cache[i];
    }

    public static void Set(CrosshairKind k, CrosshairStyle s)
    {
        _cache[(int)k] = s;
        Save(k);
    }

    /// <summary>편집 대상 고치기 (값을 바꾼 뒤 부름)</summary>
    public static void EditChanged() => Save(ShareAll ? CrosshairKind.Gun : Editing);

    static void Save(CrosshairKind k)
    {
        PlayerPrefs.SetString(P + k, JsonUtility.ToJson(Get(k)));
        Changed?.Invoke();
    }

    public static void ResetEdit()
    {
        var k = ShareAll ? CrosshairKind.Gun : Editing;
        Set(k, CrosshairStyle.Default(k));
    }

    public static void CopyEditToAll()
    {
        var src = Edit;
        for (int i = 0; i < 3; i++)
            if ((CrosshairKind)i != Editing) _cache[i] = src.Clone();
        for (int i = 0; i < 3; i++) Save((CrosshairKind)i);
    }
}
