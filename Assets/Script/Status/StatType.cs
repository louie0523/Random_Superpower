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

    // ── 헤드샷 (비율, Flat만 · 0.2 = 20%) — 뒤에만 추가 (저장값) ──
    HeadshotDamage,     // 헤드샷 배율 추가 (0.25 → 총 2배가 2.25배)
    HeadshotReduction,  // 받는 헤드샷 피해 감소 (투구) — 몸샷 피해 밑으로는 안 내려감

    // ── 쉴드 (일반 수치 · Flat 50 = 50) ──
    Shield,             // 재생 쉴드 최대치 — 안 맞으면 다시 참
    SoulShield,         // 소울 쉴드 최대치 — 소울로 충전

    // ── 무기 능력치 종합 증가 (인챈트 · Percent 10 = 무기 기초 능력치마다 +10%) — 뒤에만 추가 (저장값) ──
    // 플레이어 능력치에 직접 더해지지 않고, 그 무기의 기초 능력치(BaseStats)에 비율만큼 추가로 붙음 (PlayerEnchant)
    WeaponStatBoost,

    // ── 받는 피해 감소 (갑옷 주요 능력치 · 비율, Flat 0.15 = 15%) — 헤드샷 피해에는 안 걸림 (투구가 맡음) — 뒤에만 추가 (저장값) ──
    BodyDamageReduction,
}

public enum ModType
{
    /// <summary>실수치 그대로 더함. +25 → 25</summary>
    Flat,
    /// <summary>퍼센트. 25 → +25%</summary>
    Percent,
}