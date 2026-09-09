public enum StatType
{
    MaxHP,
    AttackPower,
    Defense,
    MoveSpeed,
    AttackSpeed,
    CritChance,
    CritDamage,
    CooldownReduction,
    LifeSteal,

    // ── 배율 계열 (기본 1.0 = 100%) ──
    DamageDealt,        // 가하는 피해
    DamageTaken,        // 받는 피해
    MeleeDamage,        // 근접 무기
    RangedDamage,       // 원거리 무기
    SkillDamage,        // 모든 스킬 (궁극기 포함)
    UltimateDamage,     // 궁극기 전용
}

public enum ModType
{
    /// <summary>실수치 그대로 더함. +25 → 25</summary>
    Flat,
    /// <summary>퍼센트. 25 → +25%</summary>
    Percent,
}