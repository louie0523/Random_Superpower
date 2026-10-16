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

    [Header("설명 — {x:name} {x:tier} {x:nametag} {x:stats} {x:chargedist} {x:키}")]
    public DKTierInfo[] Tiers = DefaultTiers();

    const string BaseChargeText =
        "<b>돌진</b> — 탑승 중 다시 누르면 이동속도에 비례해 약 {x:chargedist}m 돌진, 처음 부딪힌 적에게 {dmg:ChargeCoef} 피해를 주고 멈춤";

    static DKTierInfo[] DefaultTiers() => new DKTierInfo[]
    {
        new() { Tier = DKTier.Peasant, Label = "농민", Color = new Color(0.72f, 0.72f, 0.72f), Texts = new[]
        {
            new DescLine("slash", "<b>농작</b> — 가한 피해의 {b:FarmHealPct}%를 회복"),
            new DescLine("mount", "<b>미숙한 승마</b> — 이동속도 증가 -{b:UnskilledPenalty}\n" + BaseChargeText),
        }},
        new() { Tier = DKTier.Mercenary, Label = "용병", Color = new Color(0.5f, 0.7f, 1f), Texts = new[]
        {
            new DescLine("slash", "<b>농작</b> — 가한 피해의 {b:FarmHealPct}%를 회복\n<b>전투감각</b> — 잠시 뒤 {b:SecondMul:pct}% 위력으로 한 번 더 벰"),
            new DescLine("mount", "<b>미숙한 승마</b> — 이동속도 증가 -{b:UnskilledPenalty}\n" + BaseChargeText),
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
            new DescLine("mount", "<b>성기사의 돌진</b> — 탑승 중 다시 누르면 속도 +{b:ArthurChargeSpeedPct}%로 약 {x:chargedist}m 돌진, 적을 꿰뚫고 지나가며 {dmg:ChargeCoef} 피해 + 밀쳐내고 {b:ArthurStun}초 기절"),
            new DescLine("ult", "<b>빛의 폭발</b> — 폭발 범위에 {dmg:ArthurBlastCoef} 추가 피해"),
        },
        DKNamed.Roland => new[]
        {
            new DescLine("slash", "<b>뒤랑달</b> — 위력 +{b:RolandBonusPct}%, 대상이 잃은 체력의 {b:RolandMissingPct}% 추가 피해"),
            new DescLine("mount", "<b>도약</b> — 탑승 중 다시 누르면 뛰어올라 주위 {b:RolandPushRadius}m의 적을 밀쳐내고 잃은 체력의 {b:RolandHealMissingPct}% 회복"),
            new DescLine("ult", "<b>일격 강화</b> — 전방 피해가 공격력의 {b:RolandFinisherCoef}%로 증가, {b:RolandStun}초 기절"),
        },
        DKNamed.Bernhard => new[]
        {
            new DescLine("slash", "<b>학살자</b> — 위력 +{b:BernhardBonusPct}%, {b:BernhardWoundTime}초간 깊은 상처 {b:BernhardWoundPct}%"),
            new DescLine("mount", "<b>망령 돌진</b> — 탑승 중 다시 누르면 고스트 상태로 약 {x:chargedist}m 돌진, 적을 통과하며 {dmg:ChargeCoef} 피해 (위력 +{b:BernhardChargePct}%)"),
            new DescLine("ult", "<b>죽음의 불길</b> — 폭발에 맞은 적이 {b:DeathFlameTime}초간 매초 최대 체력의 {b:DeathFlamePct}% 화상"),
        },
        _ => null,
    };

    [ContextMenu("설명 기본값으로 덮어쓰기 (등급·네임드 전부)")]
    void ResetAllTexts()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "설명 기본값");
#endif
        Tiers = DefaultTiers();
        if (Names != null)
            foreach (var n in Names)
                if (n != null) n.Texts = NamedTexts(n.Named);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

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
    [Tooltip("탑승 중 3인칭 카메라 (본인 화면)")]
    public bool MountThirdPerson = true;
    public ThirdPersonSettings MountCamera = new()
    {
        Offset = new Vector3(0.3f, 0.6f, -4.2f),
        PivotHeight = 0.2f,
        PitchOffset = 6f,
        Fov = 6f,
    };
    [ForSkill(nameof(MountSkill), "지속 (초)")] public LevelValue MountDuration = 15f;
    [ForSkill(nameof(MountSkill), "이속 증가 (실수치)")] public LevelValue MountSpeed = 5f;
    [ForSkill(nameof(MountSkill), "미숙한 승마 — 이속 감소")] public LevelValue UnskilledPenalty = 1f;
    [ForSkill(nameof(MountSkill), "받는 피해 감소 (%)")] public LevelValue MountDamageReduce = 30f;
    [ForSkill(nameof(MountSkill), "황천의 검 사거리 배율")] public LevelValue MountRangeMul = 1.5f;

    [Header("승마 — 돌진 (탑승 중 스킬 키를 다시 누르면)")]
    [Tooltip("방향·중력 등 기본값. 거리·시간·통과 여부는 아래 수치와 이름에 따라 덮어씀")]
    public DashParams ChargeDash = new()
    {
        Distance = 8f,
        Duration = 0.35f,
        Direction = DashDirection.Horizontal,
        PassThroughPlayers = true,
        StopOnContact = false,
    };
    [Tooltip("돌진·도약 모션 (탑승 모션과 별개). 판정 프레임에 돌진 시작. 비우면 누르는 즉시")]
    public AbilityMotionData ChargeMotion;
    [Tooltip("롤란드 도약 모션. 판정 프레임에 뛰어오름. 비우면 ChargeMotion 사용")]
    public AbilityMotionData LeapMotion;
    [ForSkill(nameof(MountSkill), "돌진 횟수 (탑승당)")] public LevelValue ChargeUses = 1f;
    [ForSkill(nameof(MountSkill), "돌진 속도 (이동속도 ×)")] public LevelValue ChargeSpeedMul = 1.5f;
    [ForSkill(nameof(MountSkill), "돌진 시간 (초)")] public LevelValue ChargeDuration = 0.35f;
    [ForSkill(nameof(MountSkill), "최대 돌진 거리 (m, 0 = 제한 없음)")] public LevelValue ChargeDistance = 0f;
    [Tooltip("돌진 시작 시 시야 확 넓히기 (속도감). 본인 화면만")]
    public float ChargeFovKick = 14f;
    [Tooltip("돌진 수치를 콘솔에 출력 (속도 확인용)")]
    public bool LogCharge;
    [ForSkill(nameof(MountSkill), "돌진 피해 계수 (공격력 %)")] public LevelValue ChargeCoef = 150f;
    [ForSkill(nameof(MountSkill), "돌진 폭 (m)")] public LevelValue ChargeWidth = 1.3f;
    [ForSkill(nameof(MountSkill), "아서 — 돌진 속도 증가 (%)")] public LevelValue ArthurChargeSpeedPct = 30f;
    [ForSkill(nameof(MountSkill), "베른하르트 — 돌진 위력 증가 (%)")] public LevelValue BernhardChargePct = 50f;
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
    private ThirdPersonView _tpv;

    private DKName _name;
    private float _ghostCd;
    private float _mountTimer;
    private int _chargeLeft;
    private AlterData _mountedAlter;
    private float _secondTimer = -1f;
    private float _secondCoef, _secondFlat;
    private float _flat;
    private Vector3 _secondDir;

    private float _chargeTimer;
    private Vector3 _chargePrev, _chargeStart, _chargeFwd;
    private bool _chargePush, _chargeStopOnHit;
    private float _chargeDmgMul = 1f;
    [System.NonSerialized] private readonly HashSet<PlayerHealth> _chargeHit = new();

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[32];
    [System.NonSerialized] private readonly HashSet<PlayerHealth> _seen = new();

    public DKName CurrentName => _name;
    public bool Mounted => _mountTimer > 0f;

    bool Skilled => _name != null && _name.Tier == DKTier.Named;
    bool HasFarm => _name != null;                                             // 모든 등급
    bool HasSecond => _name != null && _name.Tier != DKTier.Peasant;          // 용병 이상
    // 원격에선 OnEquip 때 저장값이 아직 안 왔을 수 있어서 늦게라도 이름을 찾음
    DKNamed Named => CurName() != null ? _name.Named : DKNamed.None;

    /// <summary>이름 — 저장값(네트워크)이 바뀌었으면 다시 읽음 (치트로 바꾼 이름이 다른 화면에도 반영)</summary>
    DKName CurName()
    {
        int saved = Ctx != null ? Ctx.GetSaved(Data.Id) : 0;
        if (saved > 0 && Names != null && saved <= Names.Length && (_name == null || _name != Names[saved - 1]))
            _name = Names[saved - 1];
        return _name ??= ResolveName();
    }

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _health = Ctx.GetComponent<PlayerHealth>();
        _status = Ctx.GetComponent<PlayerStatus>();
        _alter = Ctx.GetComponent<PlayerAlter>();
        _move = Ctx.GetComponent<PlayerMovement>();
        _tpv = Ctx.GetComponent<ThirdPersonView>();

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

        if (dead) _chargeTimer = 0f;
        else TickCharge(dt);
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
            case "chargedist":
                float sm = Named == DKNamed.Arthur ? 1f + ArthurChargeSpeedPct.At(rank) * 0.01f : 1f;
                return ChargeDistanceNow(sm).ToString("0.#");
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
        if (coefField == nameof(ChargeCoef) && Named == DKNamed.Bernhard)
            return 1f + BernhardChargePct.At(rank) * 0.01f;
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

    public override AbilityMotionData GetChainMotion(int tag) => tag switch
    {
        ChainSecond => SecondMotion,
        ChainCharge => Named == DKNamed.Roland && LeapMotion != null ? LeapMotion : ChargeMotion,
        _ => null,
    };

    public override void OnChainHit(int tag)
    {
        if (_health != null && _health.IsDead) return;
        if (tag == ChainSecond) SecondSlash();
        else if (tag == ChainCharge) DoCharge();
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

        if (MountThirdPerson && Ctx.HasInputAuthority && _tpv != null) _tpv.Enter(this, MountCamera);

        float speed = Lv(MountSpeed) - (Skilled ? 0f : Lv(UnskilledPenalty));
        var stats = Ctx.Stats;
        stats?.AddModifier(new StatModifier(StatType.MoveSpeed, ModType.Flat, speed, Src(SrcMount)));
        stats?.AddModifier(new StatModifier(StatType.DamageTaken, ModType.Percent, -Lv(MountDamageReduce), Src(SrcMount)));

        // 돌진(롤란드는 도약)은 탑승 중 스킬 키를 다시 눌러야 발동 (OnSkillRecast)
        _chargeLeft = Mathf.Max(0, Mathf.RoundToInt(Lv(ChargeUses)));
    }

    // ── 탑승 중 다시 누르기 → 돌진 ──

    const int ChainCharge = 2;

    public override bool CanRecast(AbilityData.SkillDef skill) =>
        System.Array.IndexOf(Data.Skills, skill) == MountSkill && Mounted && _chargeLeft > 0;

    public override bool OnSkillRecast(int slot, AbilityData.SkillDef skill)
    {
        if (!CanRecast(skill)) return false;
        _chargeLeft--;
        ChainMotion(ChainCharge);      // 돌진 모션 → 판정 프레임에 OnChainHit(ChainCharge). 모션 없으면 바로
        return true;
    }

    /// <summary>
    /// 농민·용병 — 기본 돌진: 처음 부딪힌 적에게 피해 주고 멈춤
    /// 아서 — 꿰뚫는 돌진 (밀쳐내기·기절) + 속도 증가 · 베른하르트 — 고스트로 통과 + 위력 증가 · 롤란드 — 도약
    /// </summary>
    void DoCharge()
    {
        if (!Mounted) return;
        switch (Named)
        {
            case DKNamed.Arthur:
                Charge(pass: true, push: true, ghost: false, speedMul: 1f + Lv(ArthurChargeSpeedPct) * 0.01f, dmgMul: 1f);
                break;
            case DKNamed.Bernhard:
                Charge(pass: true, push: false, ghost: true, speedMul: 1f, dmgMul: 1f + Lv(BernhardChargePct) * 0.01f);
                break;
            case DKNamed.Roland:
                Leap();
                break;
            default:
                Charge(pass: false, push: false, ghost: false, speedMul: 1f, dmgMul: 1f);
                break;
        }
    }

    /// <summary>지금 이동속도 기준 돌진 거리 (설명 {x:chargedist}에도 씀)</summary>
    float ChargeDistanceNow(float speedMul)
    {
        float move = Ctx != null && Ctx.Stats != null ? Ctx.Stats.Get(StatType.MoveSpeed) : 12f;
        float dist = move * Lv(ChargeSpeedMul) * speedMul * Mathf.Max(0.05f, Lv(ChargeDuration));
        float max = Lv(ChargeDistance);
        return max > 0f ? Mathf.Min(dist, max) : dist;
    }

    void Dismount()
    {
        bool was = _mountTimer > 0f || _mountedAlter != null;
        _mountTimer = 0f;
        _chargeLeft = 0;
        Ctx.Stats?.RemoveBySource(Src(SrcMount));
        if (was && _mountedAlter != null) _alter?.Remove(_mountedAlter);
        _mountedAlter = null;
        if (_tpv != null) _tpv.Exit(this);
        if (was) ReleaseCooldown(MountSkill);     // '효과 끝나고 쿨타임'이면 지금부터 쿨
    }

    /// <summary>전방 돌진 — 속도는 이동속도 비례 (시간 고정, 빠를수록 멀리)</summary>
    void Charge(bool pass, bool push, bool ghost, float speedMul, float dmgMul)
    {
        Vector3 start = Ctx.transform.position;
        Vector3 fwd = Forward();
        var self = Ctx.Object.InputAuthority;

        var dash = ChargeDash;
        dash.Duration = Mathf.Max(0.05f, Lv(ChargeDuration));
        dash.Distance = ChargeDistanceNow(speedMul);
        dash.PassThroughPlayers = pass;
        dash.StopOnContact = !pass;            // 기본 돌진은 부딪히면 멈춤
        if (ghost) _status?.Apply(StatusType.Ghost, 1f, dash.Duration + 0.15f, self);
        Ctx.DoDash(dash);

        if (Ctx.HasInputAuthority)
        {
            var cam = Ctx.GetComponentInChildren<FPSCamera>(true);
            if (cam != null) cam.FovKick(ChargeFovKick);
        }

        if (LogCharge)
        {
            float move = Ctx.Stats != null ? Ctx.Stats.Get(StatType.MoveSpeed) : 0f;
            Debug.Log($"[DK 돌진] 이동속도 {move:0.#} → 돌진 {dash.Distance / dash.Duration:0.#} m/s " +
                      $"(×{Lv(ChargeSpeedMul) * speedMul:0.##}), 거리 {dash.Distance:0.#}m / {dash.Duration:0.##}초, " +
                      $"최대거리 {Lv(ChargeDistance):0.#}, 부딪히면 멈춤 {dash.StopOnContact}");
        }

        // 실제로 지나가는 동안 판정 — 매 틱 지난 구간만 검사, 한 대상은 한 번
        _chargeTimer = dash.Duration + 0.05f;
        _chargePrev = start;
        _chargeStart = start;
        _chargeFwd = fwd;
        _chargePush = push;
        _chargeStopOnHit = !pass;
        _chargeDmgMul = dmgMul;
        _chargeHit.Clear();
        TickCharge(0f);

        Broadcast(EvCharge, start);
    }

    void TickCharge(float dt)
    {
        if (_chargeTimer <= 0f) return;
        _chargeTimer -= dt;

        Vector3 cur = Ctx.transform.position;
        Vector3 seg = cur - _chargePrev;
        float len = seg.magnitude;
        Vector3 dir = len > 0.001f ? seg / len : _chargeFwd;

        // 몸 앞쪽까지 조금 더 봐서 정면으로 부딪힌 적도 맞게
        foreach (var h in Path(_chargePrev, dir, len + Lv(ChargeWidth) * 0.5f, Lv(ChargeWidth)))
            if (_chargeHit.Add(h))
            {
                ChargeHit(h);
                if (_chargeStopOnHit)                                   // 처음 부딪힌 한 명만 — 그 자리에서 멈춤
                {
                    _chargeTimer = 0f;
                    Ctx.StopDash();
                    break;
                }
            }

        _chargePrev = cur;
    }

    void ChargeHit(PlayerHealth h)
    {
        var self = Ctx.Object.InputAuthority;
        var info = MakeHit(Lv(ChargeCoef) * 0.01f * _chargeDmgMul, DamageSource.Skill, out _);
        info.CanCauseFall = _chargePush;
        h.RPC_TakeDamage(info);

        if (!_chargePush) return;

        if (h.TryGetComponent<PlayerMovement>(out var mv))
        {
            Vector3 side = Vector3.Cross(Vector3.up, _chargeFwd);
            float sgn = Vector3.Dot(h.transform.position - _chargeStart, side) >= 0f ? 1f : -1f;
            mv.AddKnockback((_chargeFwd + side * sgn * 0.6f).normalized * Lv(ArthurPush) + Vector3.up * 3f);
        }
        if (h.TryGetComponent<PlayerStatus>(out var st))
            st.Apply(StatusType.Stun, 1f, Lv(ArthurStun), self);
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

        // 탈것 애니메이션 — 붙어 있는 말(파츠)의 RigAnimator에 그 상태가 있으면 재생 (안 타고 있으면 말이 없으니 무시됨)
        if (_alter != null)
        {
            if (key == EvCharge) _alter.PlayPartState("Charge", Lv(ChargeDuration));
            else if (key == EvLeap) _alter.PlayPartState("Leap");
            else if (key == EvSlash || key == EvSlash2) _alter.PlayPartState("Attack");
        }
    }

    // ══════════════════ 치트 (/job) ══════════════════

    public override string DevCommand(string[] a)
    {
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "help";
        switch (sub)
        {
            case "help":
                return "죽음의 기사: /job name [이름·번호] · names · mount · dismount · charge · ghost";

            case "names":
                {
                    var sb = new System.Text.StringBuilder("이름 목록:");
                    for (int i = 0; i < Names.Length; i++)
                        if (Names[i] != null)
                            sb.Append($"\n  {i + 1}. {LocUtil.Resolve(Names[i].Name)} ({TierLabel(Names[i].Tier)})" + (Names[i] == _name ? "  ← 지금" : ""));
                    return sb.ToString();
                }

            case "name":
            case "이름":
                {
                    if (Names == null || Names.Length == 0) return "이름 목록이 비어 있음";
                    int idx = -1;
                    if (a.Length > 1)
                    {
                        if (int.TryParse(a[1], out int n)) idx = n - 1;
                        else
                            for (int i = 0; i < Names.Length; i++)
                                if (Names[i] != null && LocUtil.Resolve(Names[i].Name).Contains(a[1])) { idx = i; break; }
                        if (a.Length > 1 && idx < 0)
                            foreach (DKTier t in System.Enum.GetValues(typeof(DKTier)))      // 등급으로: peasant · mercenary · named
                                if (string.Equals(t.ToString(), a[1], System.StringComparison.OrdinalIgnoreCase) || TierLabel(t) == a[1])
                                {
                                    var pool = new List<int>();
                                    for (int i = 0; i < Names.Length; i++) if (Names[i] != null && Names[i].Tier == t) pool.Add(i);
                                    if (pool.Count > 0) idx = pool[Random.Range(0, pool.Count)];
                                }
                    }
                    else
                    {
                        int cur = System.Array.IndexOf(Names, _name);
                        do idx = Random.Range(0, Names.Length); while (Names.Length > 1 && idx == cur);
                    }
                    if (idx < 0 || idx >= Names.Length || Names[idx] == null) return $"이름 '{(a.Length > 1 ? a[1] : "")}' 없음 — /job names";

                    bool wasMounted = Mounted;
                    if (wasMounted) Dismount();          // 이름별 말이 다를 수 있음
                    Ctx.SetSaved(Data.Id, idx + 1);
                    _name = Names[idx];
                    ApplyNameStats();
                    return $"이름 → {LocUtil.Resolve(_name.Name)} ({TierLabel(_name.Tier)})" + (wasMounted ? " — 말에서 내림" : "");
                }

            case "mount":
            case "승마":
                if (Mounted) return "이미 타고 있음";
                Mount();
                return "승마";

            case "dismount":
                if (!Mounted) return "안 타고 있음";
                Dismount();
                return "내림";

            case "charge":
            case "돌진":
                _chargeLeft = Mathf.Max(1, Mathf.RoundToInt(Lv(ChargeUses)));
                return $"돌진 횟수 {_chargeLeft}";

            case "ghost":
                _ghostCd = 0f;
                return "망자의 육신 쿨 초기화";
        }
        return null;
    }

    public override IEnumerable<(string value, string label)> DevComplete(string[] a, int index)
    {
        if (index == 0)
            return new[] { ("name", "이름 바꾸기 (비우면 무작위)"), ("names", "이름 목록"), ("mount", "승마"), ("dismount", "내리기"), ("charge", "돌진 횟수 채우기"), ("ghost", "고스트 쿨 초기화") };
        if (index == 1 && a.Length > 0 && (a[0] == "name" || a[0] == "이름") && Names != null)
        {
            var list = new List<(string, string)>
            {
                ("peasant", "농민 중 무작위"), ("mercenary", "용병 중 무작위"), ("named", "네임드 중 무작위"),
            };
            for (int i = 0; i < Names.Length; i++)
                if (Names[i] != null) list.Add(((i + 1).ToString(), $"{LocUtil.Resolve(Names[i].Name)} ({TierLabel(Names[i].Tier)})"));
            return list;
        }
        return null;
    }

    string TierLabel(DKTier t)
    {
        var info = TierInfo(t);
        return info != null && !string.IsNullOrEmpty(info.Label) ? LocUtil.Resolve(info.Label) : t.ToString();
    }
}