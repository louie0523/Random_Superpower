using UnityEngine;

/// <summary>스탯 수치를 플레이어용 문자열로. 단위 규칙은 PlayerStats.Recalculate 기준</summary>
public static class StatText
{
    public static bool IsMultiplier(StatType s) =>
        s == StatType.DamageDealt || s == StatType.DamageTaken ||
        s == StatType.MeleeDamage || s == StatType.RangedDamage ||
        s == StatType.SkillDamage || s == StatType.UltimateDamage;

    public static bool IsRatio(StatType s) =>
        s == StatType.CritChance || s == StatType.CritDamage ||
        s == StatType.CooldownReduction || s == StatType.LifeSteal ||
        s == StatType.HeadshotDamage || s == StatType.HeadshotReduction;

    /// <summary>+25 / +25% / -10% 형태</summary>
    public static string Value(StatType stat, ModType type, float v)
    {
        string sign = v >= 0f ? "+" : "-";
        float a = Mathf.Abs(v);

        if (type == ModType.Percent) return $"{sign}{a:0.#}%";              // 25 → +25%
        if (IsMultiplier(stat) || IsRatio(stat)) return $"{sign}{a * 100f:0.#}%";  // 0.25 → +25%
        return $"{sign}{a:0.##}";                                          // 25 → +25
    }

    // ══════════════════ 보이는 글 (색 · 아이콘 — StatStyle) ══════════════════

    /// <summary>스탯 이름 (번역)</summary>
    public static string Name(StatType s) => LocUtil.Get($"stat.{s}");

    /// <summary>아이콘 + 스탯 색으로 감싼 글 — 예) Styled(AttackPower, "공격력 120% + 30")</summary>
    public static string Styled(StatType s, string body) => StatStyle.Current.Wrap(s, body);

    /// <summary>아이콘 + 색 이름만</summary>
    public static string Label(StatType s) => Styled(s, Name(s));

    /// <summary>아이콘 + 색 "공격력 +25" — 툴팁 · 상점 · 인챈트 한 줄</summary>
    public static string Line(StatType s, ModType type, float v, string gap = " ") => Styled(s, Name(s) + gap + Value(s, type, v));
}
