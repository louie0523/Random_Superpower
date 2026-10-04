public enum AbilityKind { Main, Sub, Random }
public enum AbilityTag { Melee, Ranged, Growth, Utility, Trick, Soul }
public enum SubTier { Silver, Gold, Platinum }

/// <summary>언제 발동하는가</summary>
public enum TriggerType
{
    Passive,        // 항상 (스탯 수정자)
    OnSkillUse,
    OnHit,          // 내가 때렸을 때
    OnHitTaken,     // 내가 맞았을 때
    OnKill,
    OnDeath,
    OnSoulGain,
    Periodic,
    OnRespawn,
    OnMove,
    OnStateActive,
}

/// <summary>무엇을 하는가</summary>
public enum EffectType
{
    StatMod,        // 스탯 변경
    Damage,
    Heal,
    Seal,           // 능력 봉인
    Slow,
    Blind,          // 시야 흐림
    Dash,
    Projectile,
    MarkTarget,     // 복수 대상 지정 등
    GrantSoul,
    AddCharge,
    EnterState,
    ApplyStatus,
}

/// <summary>누구에게</summary>
public enum TargetType
{
    Self,
    Attacker,       // 나를 때린 사람
    Victim,         // 내가 때린 사람
    Killer,
    MarkedTarget,
    AimTarget,      // 조준 방향
    AreaAroundSelf,
}

[System.Flags]
public enum WeaponRequirement
{
    None = 0,
    Unarmed = 1 << 0,
    Melee = 1 << 1,      // 맨손 제외 근접
    Bow = 1 << 2,
    Staff = 1 << 3,
    Gun = 1 << 4,
    Thrown = 1 << 5,

    AnyRanged = Bow | Staff | Gun | Thrown,
    Any = Unarmed | Melee | AnyRanged,
}