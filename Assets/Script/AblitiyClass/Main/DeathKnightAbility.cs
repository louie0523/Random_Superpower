using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum DKTier { Peasant, Mercenary, Named }
public enum DKNamed { None, Arthur, Roland, Bernhard }

[System.Serializable]
public class DKName
{
    public string Name = "윌리엄";
    public DKTier Tier = DKTier.Peasant;
    [Tooltip("네임드 고유 효과")]
    public DKNamed Named = DKNamed.None;
    [Tooltip("뽑힐 가중치")]
    [Min(0)] public float Weight = 10f;
    [Tooltip("이 이름의 추가 능력치")]
    public AlterStat[] Stats;
    [Tooltip("이 이름 전용 말 (비우면 공통 말)")]
    public AlterData MountOverride;
    [Tooltip("설명의 {x:키} 자리에 들어갈 이 이름만의 글 (등급 공통 글 뒤에 붙음).\n" +
             "키 예: slash · mount · ult · ghost. 글 안에서 {b:필드} {dmg:계수} 사용 가능")]
    public DescLine[] Texts;
}

/// <summary>등급별 표시 이름·색·공통 설명</summary>
[System.Serializable]
public class DKTierInfo
{
    public DKTier Tier;
    public string Label = "농민";
    public Color Color = Color.white;
    [Tooltip("이 등급 모두에게 들어가는 {x:키} 글")]
    public DescLine[] Texts;
}

/// <summary>
/// 죽음의 기사
///   패시브 0  망령          — 능력을 받을 때 이름 하나가 정해짐 (라운드 동안 유지). 이름별 능력치
///   패시브 1  망자의 육신   — 체력 50% 이하가 되면 2초 고스트 (모든 공격 무시 + 통과). 쿨 30초
///   스킬 0    황천의 검     — 전방 베기. 농민: 피해 30% 회복 / 용병: + 잠시 뒤 50% 2타 / 네임드: 둘 다 + 고유
///   스킬 1    승마          — 언데드 말. 이속 증가, 받는 피해 30% 감소, 황천의 검 사거리 +50%. 네임드는 탈 때 고유 돌진
///   궁극기    최후의 일격   — 전방 무기 피해 200% + 흑마법 폭발(강한 중독). 네임드 고유 강화
/// 수치는 레벨별(LevelValue) — 능력 데이터의 각 스킬·패시브 탭에서도 같이 편집. 설명에서 {b:필드이름}
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Death Knight")]
public class DeathKnightAbility : AbilityBehaviour
{
    // ══════════════════ 이름 ══════════════════

    [Header("이름 — 등급별 기본값은 새 에셋을 만들 때 채워짐")]
    public DKName[] Names =
    {
        P("윌리엄"), P("존"), P("토마스"), P("헨리"), P("로버트"),
        M("울프"), M("하콘"), M("에릭"),
        N("아서", DKNamed.Arthur), N("롤란드", DKNamed.Roland), N("베른하르트", DKNamed.Bernhard),
    };

    static DKName P(string n) => new() { Name = n, Tier = DKTier.Peasant, Weight = 10f, Stats = TierStats(10, 2, 0f) };
    static DKName M(string n) => new() { Name = n, Tier = DKTier.Mercenary, Weight = 6f, Stats = TierStats(25, 5, 0.5f) };
    static DKName N(string n, DKNamed k) => new() { Name = n, Tier = DKTier.Named, Named = k, Weight = 2f, Stats = TierStats(50, 10, 1f), Texts = NamedTexts(k) };

    static AlterStat[] TierStats(float hp, float atk, float move) => new[]
    {
        new AlterStat { Stat = StatType.MaxHP, Type = ModType.Flat, Value = hp },
        new AlterStat { Stat = StatType.AttackPower, Type = ModType.Flat, Value = atk },
        new AlterStat { Stat = StatType.MoveSpeed, Type = ModType.Flat, Value = move },
    };

    [Header("설명 — {x:name} {x:tier} {x:nametag} {x:stats} {x:키}")]
    public DKTierInfo[] Tiers =
    {
        new() { Tier = DKTier.Peasant, Label = "농민", Color = new Color(0.72f, 0.72f, 0.72f), Texts = new[]
        {
            new DescLine("slash", "<b>농작</b> — 가한 피해의 {b:FarmHealPct}%를 회복"),
            new DescLine("mount", "<b>미숙한 승마</b> — 이동속도 증가 -{b:UnskilledPenalty}"),
        }},
        new() { Tier = DKTier.Mercenary, Label = "용병", Color = new Color(0.5f, 0.7f, 1f), Texts = new[]
        {
            new DescLine("slash", "<b>농작</b> — 가한 피해의 {b:FarmHealPct}%를 회복\n<b>전투감각</b> — 잠시 뒤 {b:SecondMul:pct}% 위력으로 한 번 더 벰"),
            new DescLine("mount", "<b>미숙한 승마</b> — 이동속도 증가 -{b:UnskilledPenalty}"),
        }},
        new() { Tier = DKTier.Named, Label = "네임드", Color = new Color(1f, 0.79f, 0.29f), Texts = new[]
        {
            new DescLine("slash", "<b>농작</b> — 가한 피해의 {b:FarmHealPct}%를 회복\n<b>전투감각</b> — 잠시 뒤 {b:SecondMul:pct}% 위력으로 한 번 더 벰"),
        }},
    };

    static DescLine[] NamedTexts(DKNamed k) => k switch
    {
        DKNamed.Arthur => new[]
        {
            new DescLine("slash", "<b>성검</b> — 피해의 {b:ArthurTrueRatio:pct}%가 방어력을 무시"),
            new DescLine("mount", "<b>돌진</b> — 앞으로 {b:ChargeDistance}m 돌진, 지나간 적에게 {dmg:ChargeCoef} 피해 + 밀쳐내고 {b:ArthurStun}초 기절"),
            new DescLine("ult", "<b>빛의 폭발</b> — 폭발 범위에 {dmg:ArthurBlastCoef} 추가 피해"),
        },
        DKNamed.Roland => new[]
        {
            new DescLine("slash", "<b>뒤랑달</b> — 위력 +{b:RolandBonusPct}%, 대상이 잃은 체력의 {b:RolandMissingPct}% 추가 피해"),
            new DescLine("mount", "<b>도약</b> — 뛰어올라 주위 {b:RolandPushRadius}m의 적을 밀쳐내고 잃은 체력의 {b:RolandHealMissingPct}% 회복"),
            new DescLine("ult", "<b>일격 강화</b> — 전방 피해가 공격력의 {b:RolandFinisherCoef}%로 증가, {b:RolandStun}초 기절"),
        },
        DKNamed.Bernhard => new[]
        {
            new DescLine("slash", "<b>학살자</b> — 위력 +{b:BernhardBonusPct}%, {b:BernhardWoundTime}초간 깊은 상처 {b:BernhardWoundPct}%"),
            new DescLine("mount", "<b>망령 돌진</b> — 고스트 상태로 {b:ChargeDistance}m 돌진, 지나간 적에게 {dmg:ChargeCoef} 피해"),
            new DescLine("ult", "<b>죽음의 불길</b> — 폭발에 맞은 적이 {b:DeathFlameTime}초간 매초 최대 체력의 {b:DeathFlamePct}% 화상"),
        },
        _ => null,
    };

    [ContextMenu("네임드 설명 기본값 채우기 (빈 것만)")]
    void FillNamedTexts()
    {
        if (Names == null) return;
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "네임드 설명 채우기");
#endif
        foreach (var n in Names)
            if (n != null && n.Named != DKNamed.None && (n.Texts == null || n.Texts.Length == 0))
                n.Texts = NamedTexts(n.Named);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    [Header("항목 번호 (Passives / Skills 배열)")]
    public int GhostPassive = 1;
    public int SlashSkill = 0;
    public int MountSkill = 1;
    public int UltSkill = 2;

    // 아래 [ForSkill]/[ForPassive] 수치는 레벨별 — 능력 데이터의 그 스킬·패시브 탭에서도 편집.
    // 설명에는 {b:필드이름} (현재 레벨) / {b:필드이름:all} (전 레벨)

    [Header("망자의 육신")]
    [ForPassive(nameof(GhostPassive), "발동 체력 (0.5 = 50%)")] public LevelValue GhostAtHp = 0.5f;
    [ForPassive(nameof(GhostPassive), "고스트 시간 (초)")] public LevelValue GhostTime = 2f;
    [ForPassive(nameof(GhostPassive), "재사용 (초)")] public LevelValue GhostCooldown = 30f;

    [Header("황천의 검")]
    [ForSkill(nameof(SlashSkill), "피해 계수 (공격력 %)")] public LevelValue SlashCoef = 140f;
    [ForSkill(nameof(SlashSkill), "고정 추가 피해")] public LevelValue SlashFlat = 0f;
    [ForSkill(nameof(SlashSkill), "사거리 (m)")] public LevelValue SlashRange = 3.5f;
    [ForSkill(nameof(SlashSkill), "각도 (°)")] public LevelValue SlashAngle = 110f;
    [ForSkill(nameof(SlashSkill), "최대 대상")] public LevelValue SlashMaxTargets = 4f;
    [ForSkill(nameof(SlashSkill), "농작 — 피해의 % 회복")] public LevelValue FarmHealPct = 30f;
    [ForSkill(nameof(SlashSkill), "전투감각 — 2타 배율 (0.5)")] public LevelValue SecondMul = 0.5f;
    [ForSkill(nameof(SlashSkill), "아서 — 방어 무시 비율 (0.5)")] public LevelValue ArthurTrueRatio = 0.5f;
    [ForSkill(nameof(SlashSkill), "롤란드 — 위력 증가 (%)")] public LevelValue RolandBonusPct = 30f;
    [ForSkill(nameof(SlashSkill), "롤란드 — 잃은 체력 비례 (%)")] public LevelValue RolandMissingPct = 15f;
    [ForSkill(nameof(SlashSkill), "베른하르트 — 위력 증가 (%)")] public LevelValue BernhardBonusPct = 60f;
    [ForSkill(nameof(SlashSkill), "베른하르트 — 깊은 상처 (%)")] public LevelValue BernhardWoundPct = 100f;
    [ForSkill(nameof(SlashSkill), "베른하르트 — 깊은 상처 (초)")] public LevelValue BernhardWoundTime = 3f;
    public float SlashHeight = 1.8f;
    [Tooltip("전투감각 — 2타 모션 (용병·네임드). 1타 모션이 끝나면 이어서 재생되고, 이 모션의 판정 프레임에 2타 피해.\n" +
             "Motion Setup에서 1타를 복제 → ⇋ 좌우 반전하면 반대 손으로 베는 2타가 됨")]
    public AbilityMotionData SecondMotion;
    [Tooltip("2타 모션이 비어 있을 때만 — 1타 판정 후 이 시간 뒤 2타")]
    public float SecondDelay = 0.35f;

    [Header("승마")]
    public AlterData MountAlter;
    [ForSkill(nameof(MountSkill), "지속 (초)")] public LevelValue MountDuration = 15f;
    [ForSkill(nameof(MountSkill), "이속 증가 (실수치)")] public LevelValue MountSpeed = 5f;
    [ForSkill(nameof(MountSkill), "미숙한 승마 — 이속 감소")] public LevelValue UnskilledPenalty = 1f;
    [ForSkill(nameof(MountSkill), "받는 피해 감소 (%)")] public LevelValue MountDamageReduce = 30f;
    [ForSkill(nameof(MountSkill), "황천의 검 사거리 배율")] public LevelValue MountRangeMul = 1.5f;

    [Header("승마 — 네임드 고유 (탈 때 한 번)")]
    public DashParams ChargeDash = new()
    {
        Distance = 8f,
        Duration = 0.35f,
        Direction = DashDirection.Horizontal,
        PassThroughPlayers = true,
        StopOnContact = false,
    };
    [ForSkill(nameof(MountSkill), "돌진 거리 (m)")] public LevelValue ChargeDistance = 8f;
    [ForSkill(nameof(MountSkill), "돌진 피해 계수 (공격력 %)")] public LevelValue ChargeCoef = 150f;
    [ForSkill(nameof(MountSkill), "돌진 폭 (m)")] public LevelValue ChargeWidth = 1.3f;
    [ForSkill(nameof(MountSkill), "아서 — 밀치기 세기")] public LevelValue ArthurPush = 10f;
    [ForSkill(nameof(MountSkill), "아서 — 기절 (초)")] public LevelValue ArthurStun = 1f;
    [ForSkill(nameof(MountSkill), "롤란드 — 도약 세기")] public LevelValue RolandLeap = 9f;
    [ForSkill(nameof(MountSkill), "롤란드 — 밀쳐내기 반경 (m)")] public LevelValue RolandPushRadius = 4f;
    [ForSkill(nameof(MountSkill), "롤란드 — 밀쳐내기 세기")] public LevelValue RolandPush = 9f;
    [ForSkill(nameof(MountSkill), "롤란드 — 잃은 체력 회복 (%)")] public LevelValue RolandHealMissingPct = 30f;

    [Header("최후의 일격")]
    [ForSkill(nameof(UltSkill), "무기 피해 계수 (공격력 %)")] public LevelValue FinisherCoef = 200f;
    [ForSkill(nameof(UltSkill), "고정 추가 피해")] public LevelValue FinisherFlat = 0f;
    [ForSkill(nameof(UltSkill), "전방 사거리 (m)")] public LevelValue FinisherRange = 4f;
    [ForSkill(nameof(UltSkill), "전방 각도 (°)")] public LevelValue FinisherAngle = 90f;
    [ForSkill(nameof(UltSkill), "폭발 거리 (m)")] public LevelValue BlastForward = 3f;
    [ForSkill(nameof(UltSkill), "폭발 반경 (m)")] public LevelValue BlastRadius = 3.5f;
    [ForSkill(nameof(UltSkill), "중독 — 중첩당 %/초")] public LevelValue PoisonPct = 0.8f;
    [ForSkill(nameof(UltSkill), "중독 — 지속 (초)")] public LevelValue PoisonTime = 6f;
    [ForSkill(nameof(UltSkill), "중독 — 쌓는 중첩")] public LevelValue PoisonStacks = 5f;
    [ForSkill(nameof(UltSkill), "중독 — 최대 중첩")] public LevelValue PoisonMaxStacks = 9f;
    [ForSkill(nameof(UltSkill), "아서 — 빛의 폭발 계수 (%)")] public LevelValue ArthurBlastCoef = 250f;
    [ForSkill(nameof(UltSkill), "롤란드 — 전방 계수 (%)")] public LevelValue RolandFinisherCoef = 350f;
    [ForSkill(nameof(UltSkill), "롤란드 — 기절 (초)")] public LevelValue RolandStun = 1f;
    [ForSkill(nameof(UltSkill), "베른하르트 — 화상 (%/초)")] public LevelValue DeathFlamePct = 4f;
    [ForSkill(nameof(UltSkill), "베른하르트 — 화상 (초)")] public LevelValue DeathFlameTime = 20f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject SlashFx;
    public GameObject ChargeFx;
    public GameObject LeapFx;
    public GameObject FinisherFx;
    public GameObject BlastFx;
    public GameObject HolyBlastFx;

    const int EvSlash = 0, EvSlash2 = 1, EvCharge = 2, EvLeap = 3, EvFinisher = 4, EvBlast = 5, EvHoly = 6;
    const int SrcName = 0, SrcMount = 1;

    private PlayerHealth _health;
    private PlayerStatus _status;
    private PlayerAlter _alter;
    private PlayerMovement _move;

    private DKName _name;
    private float _ghostCd;
    private float _mountTimer;
    private AlterData _mountedAlter;
    private float _secondTimer = -1f;
    private float _secondCoef, _secondFlat;
    private float _flat;
    private Vector3 _secondDir;

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[32];
    [System.NonSerialized] private readonly HashSet<PlayerHealth> _seen = new();

    public DKName CurrentName => _name;
    public bool Mounted => _mountTimer > 0f;

    bool Skilled => _name != null && _name.Tier == DKTier.Named;
    bool HasFarm => _name != null;                                             // 모든 등급
    bool HasSecond => _name != null && _name.Tier != DKTier.Peasant;          // 용병 이상
    DKNamed Named => _name != null ? _name.Named : DKNamed.None;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _health = Ctx.GetComponent<PlayerHealth>();
        _status = Ctx.GetComponent<PlayerStatus>();
        _alter = Ctx.GetComponent<PlayerAlter>();
        _move = Ctx.GetComponent<PlayerMovement>();

        _name = ResolveName();
        ApplyNameStats();

        if (_health != null) _health.Damaged += OnDamaged;
        _mountTimer = 0f;
        _secondTimer = -1f;
    }

    public override void OnUnequip()
    {
        if (_health != null) _health.Damaged -= OnDamaged;
        Ctx.Stats?.RemoveBySource(Src(SrcName));
        Dismount();
    }

    /// <summary>처음 받을 때 한 번 뽑고, 라운드 동안 같은 이름 (사망·재빌드해도 유지)</summary>
    DKName ResolveName()
    {
        if (Names == null || Names.Length == 0) return null;

        int saved = Ctx.GetSaved(Data.Id);         // 0 = 아직 없음, 이름 번호 + 1
        if (saved > 0 && saved <= Names.Length) return Names[saved - 1];
        if (!Ctx.HasStateAuthority) return null;

        float total = 0f;
        foreach (var n in Names) total += Mathf.Max(0f, n.Weight);

        int pick = Names.Length - 1;
        float r = Random.value * total;
        for (int i = 0; i < Names.Length; i++)
        {
            r -= Mathf.Max(0f, Names[i].Weight);
            if (r <= 0f) { pick = i; break; }
        }

        Ctx.SetSaved(Data.Id, pick + 1);
        return Names[pick];
    }

    void ApplyNameStats()
    {
        var stats = Ctx.Stats;
        if (stats == null || !Ctx.HasStateAuthority) return;

        float maxBefore = stats.Get(StatType.MaxHP);
        stats.RemoveBySource(Src(SrcName));

        if (_name?.Stats != null)
            foreach (var s in _name.Stats)
                if (s != null) stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, Src(SrcName)));

        _health?.ApplyMaxHpGain(maxBefore);
    }

    public override void Tick(float dt)
    {
        // 새 라운드로 저장값이 지워졌으면 이름을 다시 뽑음
        if (Ctx.HasStateAuthority && Ctx.GetSaved(Data.Id) == 0)
        {
            _name = ResolveName();
            ApplyNameStats();
        }

        if (_ghostCd > 0f) _ghostCd -= dt;

        bool dead = _health != null && _health.IsDead;

        if (_mountTimer > 0f)
        {
            _mountTimer -= dt;
            if (_mountTimer <= 0f || dead) Dismount();
        }

        if (_secondTimer >= 0f)
        {
            _secondTimer -= dt;
            if (_secondTimer < 0f && !dead) SecondSlash();
        }
    }

    // ══════════════════ 설명 ({x:키}) ══════════════════

    DKTierInfo TierInfo(DKTier t)
    {
        if (Tiers != null)
            foreach (var i in Tiers)
                if (i != null && i.Tier == t) return i;
        return null;
    }

    static string Colored(string text, DKTierInfo info) =>
        info != null ? $"<color=#{ColorUtility.ToHtmlStringRGB(info.Color)}>{text}</color>" : text;

    /// <summary>
    /// {x:name} 이름 · {x:tier} 등급 · {x:nametag} "롤란드 (네임드)" · {x:stats} 추가 능력치
    /// 그 외 키 → 등급 공통 글 + 이름 글 (둘 다 없으면 그 줄 사라짐)
    /// </summary>
    public override string DescToken(string key, int rank)
    {
        if (_name == null) return null;
        var tier = TierInfo(_name.Tier);
        string tierLabel = tier != null && !string.IsNullOrEmpty(tier.Label) ? LocUtil.Resolve(tier.Label) : _name.Tier.ToString();
        string name = LocUtil.Resolve(_name.Name);

        switch (key)
        {
            case "name": return Colored(name, tier);
            case "tier": return Colored(tierLabel, tier);
            case "nametag": return $"{Colored(name, tier)} <color=#8A8A8A>({tierLabel})</color>";
            case "stats": return StatLine(_name.Stats);
        }

        string a = DescLine.Find(tier?.Texts, key);
        string b = DescLine.Find(_name.Texts, key);
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return a + "\n" + b;
    }

    /// <summary>"최대 체력 +50, 공격력 +10, 이동속도 +1" — 이름별 추가 능력치</summary>
    static string StatLine(AlterStat[] stats)
    {
        if (stats == null) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var s in stats)
        {
            if (s == null || Mathf.Approximately(s.Value, 0f)) continue;

            // 비율 스탯(치확·치피·쿨감·흡혈)은 Flat 0~1 → %로
            bool ratio = s.Type == ModType.Flat &&
                         (s.Stat is StatType.CritChance or StatType.CritDamage or StatType.CooldownReduction or StatType.LifeSteal);
            float v = ratio ? s.Value * 100f : s.Value;
            bool pct = ratio || s.Type == ModType.Percent;

            if (sb.Length > 0) sb.Append(", ");
            sb.Append(LocUtil.Get($"stat.{s.Stat}")).Append(' ')
              .Append(v > 0f ? "+" : "").Append(v.ToString("0.##")).Append(pct ? "%" : "");
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>{dmg:SlashCoef} 등이 이름 보정까지 반영되게</summary>
    public override float DescDamageMul(string coefField, int rank)
    {
        if (coefField == nameof(SlashCoef))
        {
            if (Named == DKNamed.Roland) return 1f + RolandBonusPct.At(rank) * 0.01f;
            if (Named == DKNamed.Bernhard) return 1f + BernhardBonusPct.At(rank) * 0.01f;
        }
        if (coefField == nameof(FinisherCoef) && Named == DKNamed.Roland)
        {
            float b = FinisherCoef.At(rank);
            return b > 0f ? RolandFinisherCoef.At(rank) / b : 1f;
        }
        return 1f;
    }

    // ══════════════════ 망자의 육신 ══════════════════

    void OnDamaged(DamageInfo info, int amount)
    {
        if (_ghostCd > 0f || _health == null || _health.IsDead) return;

        float max = Mathf.Max(1f, Ctx.Stats.Get(StatType.MaxHP));
        if (_health.HP > max * Lv(GhostAtHp)) return;

        _ghostCd = Lv(GhostCooldown);
        _status?.Apply(StatusType.Ghost, 1f, Lv(GhostTime), Ctx.Object.InputAuthority);
    }

    // ══════════════════ 스킬 ══════════════════

    // 버튼을 누른 순간이 아니라 스킬 모션의 효과 타이밍(데이터의 '효과 타이밍')에 발동
    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        int i = System.Array.IndexOf(Data.Skills, skill);

        if (i == SlashSkill) { Slash(); return true; }
        if (i == MountSkill) { Mount(); return true; }
        if (i == UltSkill) { Finisher(); return true; }
        return false;
    }

    const int ChainSecond = 1;

    public override AbilityMotionData GetChainMotion(int tag) => tag == ChainSecond ? SecondMotion : null;

    public override void OnChainHit(int tag)
    {
        if (tag == ChainSecond && _health != null && !_health.IsDead) SecondSlash();
    }

    // ── 황천의 검 ──

    float SlashBaseCoef()
    {
        float coef = Lv(SlashCoef) * 0.01f;
        if (Named == DKNamed.Roland) coef *= 1f + Lv(RolandBonusPct) * 0.01f;
        if (Named == DKNamed.Bernhard) coef *= 1f + Lv(BernhardBonusPct) * 0.01f;
        return coef;
    }

    void Slash()
    {
        float coef = SlashBaseCoef();
        _flat = Lv(SlashFlat);
        Vector3 fwd = Forward();
        float dealt = SlashHit(coef, fwd);

        if (HasFarm && dealt > 0f) _health?.RPC_Heal(Mathf.RoundToInt(dealt * Lv(FarmHealPct) * 0.01f));

        if (HasSecond)
        {
            _secondCoef = coef * Lv(SecondMul);
            _secondFlat = _flat * Lv(SecondMul);
            _secondDir = fwd;

            if (SecondMotion != null) ChainMotion(ChainSecond);   // 1타 모션 끝 → 2타 모션 → 판정 프레임에 OnChainHit
            else _secondTimer = SecondDelay;
        }

        Broadcast(EvSlash, Ctx.transform.position + fwd * 1.5f);
    }

    void SecondSlash()
    {
        _secondTimer = -1f;
        _flat = _secondFlat;
        Vector3 dir = SecondMotion != null ? Forward() : _secondDir;   // 모션으로 이어질 땐 그 사이 돌아본 방향으로
        float dealt = SlashHit(_secondCoef, dir);
        if (HasFarm && dealt > 0f) _health?.RPC_Heal(Mathf.RoundToInt(dealt * Lv(FarmHealPct) * 0.01f));
        Broadcast(EvSlash2, Ctx.transform.position + dir * 1.5f);
    }

    /// <summary>전방 베기 — 이름별 고유 효과 포함. 준 피해 합 반환</summary>
    float SlashHit(float coef, Vector3 fwd)
    {
        float range = Lv(SlashRange) * (Mounted ? Lv(MountRangeMul) : 1f);
        int max = Mathf.Max(1, Mathf.RoundToInt(Lv(SlashMaxTargets)));
        float total = 0f;
        int n = 0;

        foreach (var h in Cone(Ctx.transform.position, fwd, range, Lv(SlashAngle), SlashHeight))
        {
            var info = MakeHit(coef, DamageSource.Skill, out float dmg, _flat);

            if (Named == DKNamed.Arthur)
            {
                // 절반은 방어 무시 — 두 번에 나눠서
                var trueHit = info;
                trueHit.Amount = Mathf.RoundToInt(dmg * Lv(ArthurTrueRatio));
                trueHit.IgnoreDefense = true;
                trueHit.IsDot = true;
                info.Amount = Mathf.RoundToInt(dmg * (1f - Lv(ArthurTrueRatio)));
                h.RPC_TakeDamage(info);
                h.RPC_TakeDamage(trueHit);
            }
            else
            {
                if (Named == DKNamed.Roland) info.MissingHpPercent = Lv(RolandMissingPct);
                h.RPC_TakeDamage(info);
            }

            if (Named == DKNamed.Bernhard && h.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.DeepWound, Lv(BernhardWoundPct), Lv(BernhardWoundTime), Ctx.Object.InputAuthority);

            total += dmg;
            if (++n >= max) break;
        }
        return total;
    }

    // ── 승마 ──

    void Mount()
    {
        Dismount();
        _mountTimer = Lv(MountDuration);

        _mountedAlter = _name?.MountOverride != null ? _name.MountOverride : MountAlter;
        _alter?.Add(_mountedAlter);

        float speed = Lv(MountSpeed) - (Skilled ? 0f : Lv(UnskilledPenalty));
        var stats = Ctx.Stats;
        stats?.AddModifier(new StatModifier(StatType.MoveSpeed, ModType.Flat, speed, Src(SrcMount)));
        stats?.AddModifier(new StatModifier(StatType.DamageTaken, ModType.Percent, -Lv(MountDamageReduce), Src(SrcMount)));

        switch (Named)
        {
            case DKNamed.Arthur: Charge(push: true, ghost: false); break;
            case DKNamed.Bernhard: Charge(push: false, ghost: true); break;
            case DKNamed.Roland: Leap(); break;
        }
    }

    void Dismount()
    {
        bool was = _mountTimer > 0f || _mountedAlter != null;
        _mountTimer = 0f;
        Ctx.Stats?.RemoveBySource(Src(SrcMount));
        if (was && _mountedAlter != null) _alter?.Remove(_mountedAlter);
        _mountedAlter = null;
        if (was) ReleaseCooldown(MountSkill);     // '효과 끝나고 쿨타임'이면 지금부터 쿨
    }

    /// <summary>아서·베른하르트 — 전방 돌진, 지나가는 길의 적에게 피해</summary>
    void Charge(bool push, bool ghost)
    {
        Vector3 start = Ctx.transform.position;
        Vector3 fwd = Forward();
        var self = Ctx.Object.InputAuthority;

        var dash = ChargeDash;
        dash.Distance = Lv(ChargeDistance);
        if (ghost) _status?.Apply(StatusType.Ghost, 1f, dash.Duration + 0.15f, self);
        Ctx.DoDash(dash);

        foreach (var h in Path(start, fwd, dash.Distance, Lv(ChargeWidth)))
        {
            var info = MakeHit(Lv(ChargeCoef) * 0.01f, DamageSource.Skill, out _);
            info.CanCauseFall = push;
            h.RPC_TakeDamage(info);

            if (!push) continue;

            if (h.TryGetComponent<PlayerMovement>(out var mv))
            {
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                float s = Vector3.Dot(h.transform.position - start, side) >= 0f ? 1f : -1f;
                mv.AddKnockback((fwd + side * s * 0.6f).normalized * Lv(ArthurPush) + Vector3.up * 3f);
            }
            if (h.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.Stun, 1f, Lv(ArthurStun), self);
        }

        Broadcast(EvCharge, start);
    }

    /// <summary>롤란드 — 위로 도약, 주위 밀쳐내기, 잃은 체력 회복</summary>
    void Leap()
    {
        Vector3 o = Ctx.transform.position;
        _move?.AddKnockback(Vector3.up * Lv(RolandLeap));

        foreach (var h in Around(o, Lv(RolandPushRadius)))
        {
            if (!h.TryGetComponent<PlayerMovement>(out var mv)) continue;
            Vector3 dir = h.transform.position - o;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Forward();
            mv.AddKnockback(dir.normalized * Lv(RolandPush) + Vector3.up * 3f);
        }

        if (_health != null)
        {
            float max = Ctx.Stats.Get(StatType.MaxHP);
            _health.RPC_Heal(Mathf.RoundToInt((max - _health.HP) * Lv(RolandHealMissingPct) * 0.01f));
        }

        Broadcast(EvLeap, o);
    }

    // ── 최후의 일격 ──

    void Finisher()
    {
        var self = Ctx.Object.InputAuthority;
        Vector3 o = Ctx.transform.position;
        Vector3 fwd = Forward();

        float mul = (Named == DKNamed.Roland ? Lv(RolandFinisherCoef) : Lv(FinisherCoef)) * 0.01f;
        float flatDmg = Lv(FinisherFlat);

        // 전방 — 무기 피해
        foreach (var h in Cone(o, fwd, Lv(FinisherRange), Lv(FinisherAngle), SlashHeight))
        {
            var info = MakeHit(mul, DamageSource.MeleeWeapon, out _, flatDmg);
            h.RPC_TakeDamage(info);

            if (Named == DKNamed.Roland && h.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.Stun, 1f, Lv(RolandStun), self);
        }
        Broadcast(EvFinisher, o + fwd * 1.5f);

        // 흑마법 폭발 — 강한 중독
        int stacks = Mathf.RoundToInt(Lv(PoisonStacks));
        int maxStacks = Mathf.Max(1, Mathf.RoundToInt(Lv(PoisonMaxStacks)));
        Vector3 c = o + fwd * Lv(BlastForward);
        foreach (var h in Around(c, Lv(BlastRadius)))
        {
            if (h.TryGetComponent<PlayerStatus>(out var st))
            {
                for (int k = 0; k < stacks; k++)
                    st.Apply(StatusType.Poison, Lv(PoisonPct), Lv(PoisonTime), self, maxStacks);

                if (Named == DKNamed.Bernhard)
                    st.Apply(StatusType.Burn, Lv(DeathFlamePct), Lv(DeathFlameTime), self);
            }

            if (Named == DKNamed.Arthur)
                h.RPC_TakeDamage(MakeHit(Lv(ArthurBlastCoef) * 0.01f, DamageSource.Ultimate, out _));
        }
        Broadcast(Named == DKNamed.Arthur ? EvHoly : EvBlast, c);
    }

    // ══════════════════ 공통 ══════════════════

    /// <summary>피해 = 공격력 × 계수 + 고정 피해 (치명타·피해 증가 적용)</summary>
    DamageInfo MakeHit(float coef, DamageSource src, out float dmg, float flat = 0f)
    {
        var stats = Ctx.Stats;
        dmg = DamageCalc.CalcOutgoing(stats, stats.Get(StatType.AttackPower) * coef + flat, src, true, out bool crit);

        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), Ctx.Object.InputAuthority, Ctx.transform.position);
        info.IsCrit = crit;
        return info;
    }

    Vector3 Forward()
    {
        var cam = Ctx.CameraRootTransform;
        Vector3 f = cam != null ? cam.forward : Ctx.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.001f ? f.normalized : Ctx.transform.forward;
    }

    List<PlayerHealth> Around(Vector3 center, float radius)
    {
        var list = new List<PlayerHealth>();
        _seen.Clear();

        int n = Physics.OverlapSphereNonAlloc(center, radius, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.root == Ctx.transform.root) continue;
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (h != null && !h.IsDead && _seen.Add(h)) list.Add(h);
        }

        list.Sort((a, b) => (a.transform.position - center).sqrMagnitude
            .CompareTo((b.transform.position - center).sqrMagnitude));
        return list;
    }

    List<PlayerHealth> Cone(Vector3 o, Vector3 fwd, float range, float angle, float height)
    {
        var list = Around(o, range);
        list.RemoveAll(h =>
        {
            Vector3 to = h.transform.position - o;
            if (Mathf.Abs(to.y) > height) return true;
            Vector3 flat = new(to.x, 0f, to.z);
            return flat.sqrMagnitude > 0.01f && Vector3.Angle(fwd, flat) > angle * 0.5f;
        });
        return list;
    }

    List<PlayerHealth> Path(Vector3 start, Vector3 dir, float dist, float width)
    {
        var list = new List<PlayerHealth>();
        _seen.Clear();

        Vector3 a = start + Vector3.up * 0.5f;
        Vector3 b = a + dir * dist;
        int n = Physics.OverlapCapsuleNonAlloc(a, b, width, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.root == Ctx.transform.root) continue;
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (h != null && !h.IsDead && _seen.Add(h)) list.Add(h);
        }
        return list;
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        GameObject fx = key switch
        {
            EvSlash or EvSlash2 => SlashFx,
            EvCharge => ChargeFx,
            EvLeap => LeapFx,
            EvFinisher => FinisherFx,
            EvBlast => BlastFx,
            EvHoly => HolyBlastFx != null ? HolyBlastFx : BlastFx,
            _ => null,
        };
        Ctx.PlayEffectLocal(fx, pos);
    }
}