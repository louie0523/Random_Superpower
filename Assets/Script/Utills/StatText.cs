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
        s == StatType.CooldownReduction || s == StatType.LifeSteal;

    /// <summary>+25 / +25% / -10% 형태</summary>
    public static string Value(StatType stat, ModType type, float v)
    {
        string sign = v >= 0f ? "+" : "-";
        float a = Mathf.Abs(v);

        if (type == ModType.Percent) return $"{sign}{a:0.#}%";              // 25 → +25%
        if (IsMultiplier(stat) || IsRatio(stat)) return $"{sign}{a * 100f:0.#}%";  // 0.25 → +25%
        return $"{sign}{a:0.##}";                                          // 25 → +25
    }
}