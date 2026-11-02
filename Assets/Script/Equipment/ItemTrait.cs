using System.Text;
using UnityEngine;

/// <summary>
/// 아이템 고유 특성 · 방어구 특수 인챈트의 '언제' — 순서가 저장값, 뒤에만 추가
/// </summary>
public enum TraitTrigger
{
    Always,             // 끼고 있는 동안 늘 (StatBuff만 의미 있음)
    OnWeaponHit,        // 무기로 적중
    OnHeadshot,         // 헤드샷 적중
    OnKill,             // 처치
    OnAssist,           // 어시스트
    OnTakedown,         // 처치 관여 (처치 또는 어시스트)
    OnHitTaken,         // 피해를 받음
    OnSkillUse,         // 일반 스킬 사용 (궁극기 · 랜덤 제외)
    OnUltimateUse,      // 궁극기 사용
    OnRandomUse,        // 랜덤 능력 사용
    OnShieldBreak,      // 내 쉴드가 다 깨짐
    OnLowHealth,        // 체력이 Threshold% 아래로 떨어지는 순간 (다시 위로 올라가야 또 발동)
    OnRespawn,          // 부활
    WhileOutOfCombat,   // 비전투 중인 동안
    WhileInCombat,      // 전투 중인 동안
    WhileLowHealth,     // 체력 Threshold% 이하인 동안
    Periodic,           // Threshold초마다
    WhileShielded,      // 쉴드가 남아 있는 동안
}

/// <summary>무엇을 — 순서가 저장값, 뒤에만 추가</summary>
public enum TraitEffect
{
    StatBuff,           // 스탯 (Stat · Mod · Value). Duration초 (0 = 조건 동안 · Always면 계속). MaxStacks까지 쌓임
    CooldownPercent,    // 남은 쿨타임 Value% 줄임 (Slots)
    CooldownSeconds,    // 남은 쿨타임 Value초 줄임 (Slots)
    TempShield,         // 소모품 쉴드 Value (Duration초, 0 = 깨질 때까지)
    TempShieldMaxHp,    // 소모품 쉴드 최대 체력의 Value%
    RefillShield,       // 재생 쉴드 바로 가득
    Heal,               // 체력 Value 회복
    HealMaxHp,          // 최대 체력 Value% 회복
    GainSoul,           // 소울 Value
    GainStones,         // 인챈트 스톤 Value
    StatusToTarget,     // 대상에게 상태이상 (Status · Value · Duration) — 적중 · 헤드샷 대상, 맞았으면 때린 사람
    StatusToSelf,       // 나에게 상태이상 (가속 · 회피 등)
    BonusDamage,        // 대상에게 공격력의 Value% 추가 피해 (방어력 무시 · 연쇄 발동 안 함)
    RefundAmmo,         // 든 총에 탄약 Value발
}

/// <summary>쿨타임 효과가 닿는 칸</summary>
public enum TraitSlots
{
    Skills,         // 일반 스킬 (Shift · E · 우클릭)
    All,            // 궁극기 · 랜덤 포함 전부
    Ultimate,
    Random,
    SkillsAndUlt,
}

/// <summary>
/// 특성 하나 = 언제(Trigger) + 무엇을(Effect). 예)
///   처치 관여 시 방어력 +10 (6초, 3중첩)  → OnTakedown · StatBuff · Defense · Flat 10 · 6초 · 3
///   무기 적중 시 일반 스킬 쿨타임 15% 감소 → OnWeaponHit · CooldownPercent · 15 · 재사용 1초
///   체력 30% 이하로 떨어지면 쉴드 80      → OnLowHealth(30) · TempShield 80 · 5초 · 재사용 20초
/// </summary>
[System.Serializable]
public class ItemTrait
{
    [Tooltip("언제")]
    public TraitTrigger Trigger = TraitTrigger.OnWeaponHit;
    [Tooltip("OnLowHealth · WhileLowHealth: 체력 % (30 = 30%)\nPeriodic: 간격 (초)")]
    public float Threshold = 30f;
    [Tooltip("발동 확률 (1 = 항상)")]
    [Range(0f, 1f)] public float Chance = 1f;
    [Tooltip("한 번 발동하면 이 시간(초) 동안 다시 안 됨")]
    [Min(0f)] public float Cooldown = 0f;
    [Tooltip("이 무기를 들고 있을 때만 (None = 상관없음)")]
    public WeaponRequirement RequireWeapon = WeaponRequirement.None;

    [Tooltip("무엇을")]
    public TraitEffect Effect = TraitEffect.StatBuff;
    public StatType Stat = StatType.Defense;
    public ModType Mod = ModType.Flat;
    [Tooltip("수치 — 효과마다 뜻이 다름 (설명 참고)")]
    public float Value = 10f;
    [Tooltip("지속 (초). StatBuff 0 = 조건 동안 / Always면 계속")]
    [Min(0f)] public float Duration = 5f;
    [Tooltip("StatBuff 최대 중첩")]
    [Min(1)] public int MaxStacks = 1;
    [Tooltip("쿨타임 효과가 닿는 칸")]
    public TraitSlots Slots = TraitSlots.Skills;
    [Tooltip("StatusToTarget · StatusToSelf")]
    public StatusType Status = StatusType.Slow;

    [Tooltip("설명을 직접 쓸 때 (비우면 자동). {v} 수치 · {d} 지속 · {c} 확률% · {t} 기준 · {s} 중첩 · {cd} 재사용")]
    [TextArea(1, 3)] public string Text;
    [Tooltip("번역 키 (시트). 비우면 Text · 자동 설명")]
    public string TextKey;

    public bool IsWhile => Trigger is TraitTrigger.Always or TraitTrigger.WhileOutOfCombat
                                   or TraitTrigger.WhileInCombat or TraitTrigger.WhileLowHealth
                                   or TraitTrigger.WhileShielded;

    // ══════════════════ 설명 ══════════════════

    /// <summary>툴팁 한 줄 — "[처치 관여 시] 방어력 +10 (6초 · 최대 3중첩)"</summary>
    public string Describe()
    {
        string custom = !string.IsNullOrEmpty(TextKey) ? LocUtil.Pick(TextKey, Text) : Text;
        if (!string.IsNullOrWhiteSpace(custom)) return Fill(custom);

        var sb = new StringBuilder();
        sb.Append("<color=#ffd27a>[").Append(TriggerText()).Append("]</color> ").Append(EffectText());

        var tail = new StringBuilder();
        if (Chance < 0.999f) Add(tail, SetLoc.F("trait.tail.chance", "{0}% 확률", Mathf.RoundToInt(Chance * 100f)));
        if (Cooldown > 0f) Add(tail, SetLoc.F("trait.tail.cooldown", "재사용 {0}초", Num(Cooldown)));
        if (RequireWeapon != WeaponRequirement.None) Add(tail, SetLoc.F("trait.tail.weapon", "{0} 들었을 때", WeaponText()));
        if (tail.Length > 0) sb.Append(" <color=#9a9aa5>(").Append(tail).Append(")</color>");
        return sb.ToString();
    }

    static void Add(StringBuilder sb, string s) { if (sb.Length > 0) sb.Append(" · "); sb.Append(s); }
    static string Num(float v) => v.ToString("0.##");

    string Fill(string s) => s.Replace("{v}", Num(Value)).Replace("{d}", Num(Duration))
                              .Replace("{c}", Mathf.RoundToInt(Chance * 100f).ToString())
                              .Replace("{t}", Num(Threshold)).Replace("{s}", MaxStacks.ToString())
                              .Replace("{cd}", Num(Cooldown));

    string TriggerText() => Trigger switch
    {
        TraitTrigger.Always => SetLoc.T("trait.trig.Always", "장착 중"),
        TraitTrigger.OnWeaponHit => SetLoc.T("trait.trig.OnWeaponHit", "무기 적중 시"),
        TraitTrigger.OnHeadshot => SetLoc.T("trait.trig.OnHeadshot", "헤드샷 시"),
        TraitTrigger.OnKill => SetLoc.T("trait.trig.OnKill", "처치 시"),
        TraitTrigger.OnAssist => SetLoc.T("trait.trig.OnAssist", "어시스트 시"),
        TraitTrigger.OnTakedown => SetLoc.T("trait.trig.OnTakedown", "처치 관여 시"),
        TraitTrigger.OnHitTaken => SetLoc.T("trait.trig.OnHitTaken", "피해를 받으면"),
        TraitTrigger.OnSkillUse => SetLoc.T("trait.trig.OnSkillUse", "스킬 사용 시"),
        TraitTrigger.OnUltimateUse => SetLoc.T("trait.trig.OnUltimateUse", "궁극기 사용 시"),
        TraitTrigger.OnRandomUse => SetLoc.T("trait.trig.OnRandomUse", "랜덤 능력 사용 시"),
        TraitTrigger.OnShieldBreak => SetLoc.T("trait.trig.OnShieldBreak", "쉴드가 깨지면"),
        TraitTrigger.OnLowHealth => SetLoc.F("trait.trig.OnLowHealth", "체력이 {0}% 아래로 떨어지면", Num(Threshold)),
        TraitTrigger.OnRespawn => SetLoc.T("trait.trig.OnRespawn", "부활 시"),
        TraitTrigger.WhileOutOfCombat => SetLoc.T("trait.trig.WhileOutOfCombat", "비전투 중"),
        TraitTrigger.WhileInCombat => SetLoc.T("trait.trig.WhileInCombat", "전투 중"),
        TraitTrigger.WhileLowHealth => SetLoc.F("trait.trig.WhileLowHealth", "체력 {0}% 이하일 때", Num(Threshold)),
        TraitTrigger.Periodic => SetLoc.F("trait.trig.Periodic", "{0}초마다", Num(Threshold)),
        TraitTrigger.WhileShielded => SetLoc.T("trait.trig.WhileShielded", "쉴드가 있는 동안"),
        _ => Trigger.ToString(),
    };

    string SlotText() => Slots switch
    {
        TraitSlots.All => SetLoc.T("trait.slots.All", "모든 스킬"),
        TraitSlots.Ultimate => SetLoc.T("trait.slots.Ultimate", "궁극기"),
        TraitSlots.Random => SetLoc.T("trait.slots.Random", "랜덤 능력"),
        TraitSlots.SkillsAndUlt => SetLoc.T("trait.slots.SkillsAndUlt", "스킬 · 궁극기"),
        _ => SetLoc.T("trait.slots.Skills", "일반 스킬"),
    };

    string WeaponText()
    {
        var sb = new StringBuilder();
        foreach (WeaponRequirement f in System.Enum.GetValues(typeof(WeaponRequirement)))
            if (f != WeaponRequirement.None && (RequireWeapon & f) != 0)
                Add(sb, LocUtil.GetOr($"ui.weapon_req.{f}", f switch
                {
                    WeaponRequirement.Unarmed => "맨손",
                    WeaponRequirement.Melee => "근접 무기",
                    WeaponRequirement.Bow => "활",
                    WeaponRequirement.Staff => "지팡이",
                    WeaponRequirement.Gun => "총",
                    WeaponRequirement.Thrown => "투척 무기",
                    _ => f.ToString(),
                }));
        return sb.ToString();
    }

    string EffectText()
    {
        string dur = Duration > 0f ? " " + SetLoc.F("trait.dur", "({0}초)", Num(Duration)) : "";
        switch (Effect)
        {
            case TraitEffect.StatBuff:
                {
                    string s = $"{LocUtil.Get($"stat.{Stat}")} {StatText.Value(Stat, Mod, Value)}";
                    if (Duration > 0f && !IsWhile) s += dur;
                    if (MaxStacks > 1) s += " · " + SetLoc.F("trait.stacks", "최대 {0}중첩", MaxStacks);
                    return s;
                }
            case TraitEffect.CooldownPercent: return SetLoc.F("trait.eff.CooldownPercent", "{0} 남은 쿨타임 {1}% 감소", SlotText(), Num(Value));
            case TraitEffect.CooldownSeconds: return SetLoc.F("trait.eff.CooldownSeconds", "{0} 남은 쿨타임 {1}초 감소", SlotText(), Num(Value));
            case TraitEffect.TempShield: return SetLoc.F("trait.eff.TempShield", "쉴드 {0}", Num(Value)) + dur;
            case TraitEffect.TempShieldMaxHp: return SetLoc.F("trait.eff.TempShieldMaxHp", "최대 체력 {0}%만큼 쉴드", Num(Value)) + dur;
            case TraitEffect.RefillShield: return SetLoc.T("trait.eff.RefillShield", "재생 쉴드 즉시 충전");
            case TraitEffect.Heal: return SetLoc.F("trait.eff.Heal", "체력 {0} 회복", Num(Value));
            case TraitEffect.HealMaxHp: return SetLoc.F("trait.eff.HealMaxHp", "최대 체력 {0}% 회복", Num(Value));
            case TraitEffect.GainSoul: return SetLoc.F("trait.eff.GainSoul", "소울 {0}", Num(Value));
            case TraitEffect.GainStones: return SetLoc.F("trait.eff.GainStones", "인챈트 스톤 {0}", Num(Value));
            case TraitEffect.StatusToTarget: return SetLoc.F("trait.eff.StatusToTarget", "대상에게 {0} {1}", LocUtil.GetOr($"status.{Status}", Status.ToString()), Num(Value)) + dur;
            case TraitEffect.StatusToSelf: return SetLoc.F("trait.eff.StatusToSelf", "자신에게 {0} {1}", LocUtil.GetOr($"status.{Status}", Status.ToString()), Num(Value)) + dur;
            case TraitEffect.BonusDamage: return SetLoc.F("trait.eff.BonusDamage", "공격력 {0}% 추가 피해", Num(Value));
            case TraitEffect.RefundAmmo: return SetLoc.F("trait.eff.RefundAmmo", "탄약 {0}발 회복", Num(Value));
        }
        return Effect.ToString();
    }
}
