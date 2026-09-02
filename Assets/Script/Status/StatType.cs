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
}

public enum ModType
{
    /// <summary>실수치 그대로 더함. +25 → 25</summary>
    Flat,
    /// <summary>퍼센트. 25 → +25%</summary>
    Percent,
}