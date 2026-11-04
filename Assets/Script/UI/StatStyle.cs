using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 스탯마다 색 · 아이콘 — 툴팁 · 상점 · 인챈트 · 스킬 설명의 (공격력 120% + 30) 등에서 "아이콘 + 색 글자"로 보임.
/// Resources 폴더에 이름 그대로 'StatStyle'로 두면 그걸 씀 (없으면 기본 색, 아이콘 없음).
/// 만들기 · 열기: Tools → UI → 스탯 색 · 아이콘 열기
///
/// 아이콘 (둘 중 하나)
///   A. TMP 스프라이트를 직접: Sprite Asset(비우면 TMP 기본 스프라이트 에셋) + 칸마다 Sprite Name 또는 Sprite Index
///      → 글에 &lt;sprite name="AttackPower"&gt; / &lt;sprite=0&gt; 가 붙음
///   B. 칸마다 Icon(스프라이트 그림)만 넣고 인스펙터 아래 '아이콘으로 TMP 스프라이트 에셋 만들기' → A가 저절로 채워짐
/// </summary>
[CreateAssetMenu(menuName = "Game/UI/Stat Style", fileName = "StatStyle")]
public class StatStyle : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public StatType Stat;
        public Color Color = Color.white;
        [Tooltip("UI용 그림 (B 방식이면 이걸로 TMP 스프라이트를 만듦)")]
        public Sprite Icon;
        [Tooltip("TMP 스프라이트 이름 (비우면 Index)")]
        public string SpriteName;
        [Tooltip("TMP 스프라이트 번호 (-1 = 아이콘 없음)")]
        public int SpriteIndex = -1;
    }

    [Header("보이기")]
    [Tooltip("글 앞에 아이콘")]
    public bool ShowIcon = true;
    [Tooltip("이름 · 수치를 스탯 색으로")]
    public bool ColorText = true;
    [Tooltip("아이콘을 글자 색으로 물들임 (흰 아이콘을 쓸 때)")]
    public bool TintIcon;
    [Tooltip("아이콘 크기 (%) — 100 = 글자 높이")]
    [Range(50, 160)] public int IconSize = 100;

    [Header("TMP 스프라이트")]
    [Tooltip("비우면 TMP 기본 스프라이트 에셋 (Project Settings → TextMesh Pro)")]
    public TMP_SpriteAsset SpriteAsset;

    [Header("스탯마다")]
    public Entry[] Entries = Defaults();

    // ══════════════════ 찾기 ══════════════════

    static StatStyle _cur;
    Dictionary<StatType, Entry> _map;

    public static StatStyle Current
    {
        get
        {
            if (_cur != null) return _cur;
            _cur = Resources.Load<StatStyle>("StatStyle");
            if (_cur == null)
            {
                _cur = CreateInstance<StatStyle>();
                _cur.hideFlags = HideFlags.DontSave;
            }
            _cur.Register();
            return _cur;
        }
    }

    public static void Reload() => _cur = null;

    void OnEnable() => Register();

#if UNITY_EDITOR
    void OnValidate()
    {
        _map = null;
        Register();
    }
#endif

    /// <summary>&lt;sprite="이름"&gt;으로 찾을 수 있게 TMP에 등록</summary>
    void Register()
    {
        if (SpriteAsset != null) MaterialReferenceManager.AddSpriteAsset(SpriteAsset);
    }

    public Entry Get(StatType s)
    {
        if (_map == null)
        {
            _map = new Dictionary<StatType, Entry>();
            if (Entries != null)
                foreach (var e in Entries)
                    if (e != null && !_map.ContainsKey(e.Stat)) _map[e.Stat] = e;
        }
        return _map.TryGetValue(s, out var r) ? r : null;
    }

    public Color ColorOf(StatType s)
    {
        var e = Get(s);
        return e != null ? e.Color : Color.white;
    }

    public Sprite IconOf(StatType s) => Get(s)?.Icon;

    /// <summary>TMP 스프라이트 태그 — 없으면 ""</summary>
    public string Tag(StatType s)
    {
        if (!ShowIcon) return "";
        var e = Get(s);
        if (e == null) return "";

        string asset = SpriteAsset != null ? $"=\"{SpriteAsset.name}\"" : "";
        string tint = TintIcon ? " tint=1" : "";
        string tag;
        if (!string.IsNullOrEmpty(e.SpriteName)) tag = $"<sprite{asset} name=\"{e.SpriteName}\"{tint}>";
        else if (e.SpriteIndex >= 0) tag = SpriteAsset != null ? $"<sprite{asset} index={e.SpriteIndex}{tint}>" : $"<sprite={e.SpriteIndex}{tint}>";
        else return "";

        return IconSize != 100 ? $"<size={IconSize}%>{tag}</size>" : tag;
    }

    /// <summary>아이콘 + 색 글 — "&lt;sprite&gt; &lt;color&gt;공격력 +25&lt;/color&gt;"</summary>
    public string Wrap(StatType s, string body)
    {
        string icon = Tag(s);
        string text = ColorText ? $"<color=#{ColorUtility.ToHtmlStringRGB(ColorOf(s))}>{body}</color>" : body;
        return icon.Length > 0 ? icon + " " + text : text;
    }

    // ══════════════════ 기본값 ══════════════════

    static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

    public static Color DefaultColor(StatType s) => s switch
    {
        StatType.MaxHP => H("#FF6B6B"),
        StatType.AttackPower => H("#FF9F43"),
        StatType.Defense => H("#8FB8DE"),
        StatType.MoveSpeed => H("#7BED9F"),
        StatType.AttackSpeed => H("#FFD447"),
        StatType.CritChance => H("#FF7AB6"),
        StatType.CritDamage => H("#E056FD"),
        StatType.CooldownReduction => H("#5BC8FF"),
        StatType.LifeSteal => H("#FF4D5E"),
        StatType.DamageDealt => H("#FFA26B"),
        StatType.DamageTaken => H("#B0B7C3"),
        StatType.MeleeDamage => H("#FFB86B"),
        StatType.RangedDamage => H("#9FD3FF"),
        StatType.SkillDamage => H("#B98CFF"),
        StatType.UltimateDamage => H("#D6A2FF"),
        StatType.HeadshotDamage => H("#FF5E57"),
        StatType.HeadshotReduction => H("#A4B0BE"),
        StatType.Shield => H("#E8F1FF"),
        StatType.SoulShield => H("#8CD1FF"),
        StatType.WeaponStatBoost => H("#FFE08A"),
        _ => Color.white,
    };

    static Entry[] Defaults()
    {
        var all = (StatType[])System.Enum.GetValues(typeof(StatType));
        var list = new Entry[all.Length];
        for (int i = 0; i < all.Length; i++)
            list[i] = new Entry { Stat = all[i], Color = DefaultColor(all[i]) };
        return list;
    }

    [ContextMenu("빠진 스탯 칸 채우기")]
    public void AddMissing()
    {
        var have = new HashSet<StatType>();
        var list = new List<Entry>(Entries ?? new Entry[0]);
        foreach (var e in list) if (e != null) have.Add(e.Stat);
        foreach (StatType s in System.Enum.GetValues(typeof(StatType)))
            if (!have.Contains(s)) list.Add(new Entry { Stat = s, Color = DefaultColor(s) });
        Entries = list.ToArray();
        _map = null;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
