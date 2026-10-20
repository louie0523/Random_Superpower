using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>무기를 먹었을 때 얻는 특수 패시브 (무기별로 지정)</summary>
[System.Serializable]
public class DevourSpecial
{
    [Tooltip("이 무기를 먹으면 발동")]
    public EquipmentData Weapon;
    public string Name = "특수 패시브";
    [TextArea(2, 8)] public string Description;
    [Tooltip("영구 추가 능력치")]
    public AlterStat[] Stats;
    [Tooltip("겉모습 변화 (선택)")]
    public AlterData Alter;

    [Header("타격 시 상태이상 (선택)")]
    public bool OnHitStatus;
    public StatusType Status = StatusType.Burn;
    public float StatusValue = 2f;
    public float StatusTime = 3f;
    [Range(0f, 1f)] public float StatusChance = 0.25f;
}

/// <summary>뱉어내기 — 처치한 적이 이 능력을 가졌으면 추가 효과 (대인기피증 → 2차 폭발 등)</summary>
[System.Serializable]
public class SpitAbilityBonus
{
    public AbilityData Ability;
    public string Label = "2차 폭발";
    [Tooltip("추가 피해 계수 (공격력 %)")]
    public float BonusCoef = 0f;

    [Header("2차 폭발")]
    public bool SecondBlast = true;
    public float BlastDelay = 0.6f;
    [Tooltip("공격력 대비 %")]
    public float BlastCoef = 80f;
    public float BlastRadius = 4f;

    [Header("상태이상")]
    public bool ApplyStatus;
    public StatusType Status = StatusType.Slow;
    public float StatusValue = 20f;
    public float StatusTime = 2f;
}

/// <summary>
/// 탐식자
///   패시브 0  탐식자        — 무기를 먹어 능력치 일부를 영구히 흡수. 등급별 소화 시간 동안 다음 무기 못 먹음.
///                            특정 무기는 특수 패시브. 총기는 못 먹음
///   스킬 0    탐식          — 전방을 크게 물어 피해 + 피해의 N% 회복. 처치 시 소울 추가 + 쿨 초기화
///   스킬 1    뱉어내기      — 최근 탐식으로 처치 / 무기를 먹었을 때만. 전방에 토사물 → 피해.
///                            처치한 적의 공격력·능력(대인기피증 → 2차 폭발 등) 또는 먹은 무기의 공격력·특수 인챈트를 계승
///   스킬 2    무구 섭취      — 들고 있는 무기를 먹음 (탐식자 패시브의 키)
///   궁극기    굶주린 괴수    — N초간 가면을 씀(변형 — 능력치는 AlterData에서) + 괴수 여러 마리가 내 둘레를 천천히 돎.
///                            전투 중이면 각 괴수가 범위 안 적에게 날아가 약화된 탐식(위력·효과 50%).
///                            노리는 순서: 내가 최근 때린 적 → 범위 안 체력이 가장 낮은 적.
///                            괴수마다 3초 안에 문 대상은 다시 안 물음. 대상이 범위를 벗어나면 돌아옴.
///                            탐식(스킬·괴수)으로 처치할 때마다 지속 +3초
/// 수치는 레벨별(LevelValue) — 능력 데이터의 각 스킬·패시브 탭에서도 편집. 설명에서 {b:필드} {dmg:계수} {x:키}
///   {x:digest} 소화 상태 · {x:eaten} 먹은 개수 · {x:absorbed} 흡수한 능력치 · {x:specials} 얻은 특수 패시브 · {x:spit} 지금 뱉을 것
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Devourer")]
public class DevourerAbility : AbilityBehaviour
{
    [Header("항목 번호 (Passives / Skills 배열)")]
    public int EatPassive = 0;
    public int DevourSkill = 0;
    public int SpitSkill = 1;
    public int EatSkill = 2;
    public int UltSkill = 3;

    // ══════════════════ 무기 먹기 ══════════════════

    [Header("탐식자 — 무기 먹기")]
    [ForPassive(nameof(EatPassive), "흡수 비율 (0.3 = 무기 능력치의 30%)")] public LevelValue AbsorbRatio = 0.3f;
    [ForPassive(nameof(EatPassive), "인챈트 능력치도 흡수 (1 = 예)")] public LevelValue AbsorbEnchants = 1f;
    [Tooltip("등급별 흡수 배율 [C, B, A, S]")]
    public float[] GradeAbsorbMul = { 1f, 1.2f, 1.5f, 2f };
    [Tooltip("등급별 소화 시간 (초) [C, B, A, S]")]
    public float[] GradeDigestTime = { 15f, 25f, 40f, 60f };
    [ForPassive(nameof(EatPassive), "소화 시간 배율")] public LevelValue DigestMul = 1f;
    [Tooltip("소화 게이지를 보여줄 게이지 슬롯 (능력 데이터 '게이지'에 같은 번호로 추가)")]
    [Range(0, 3)] public int DigestChargeSlot = 0;
    public DevourSpecial[] Specials;

    // ══════════════════ 탐식 ══════════════════

    [Header("탐식")]
    [ForSkill(nameof(DevourSkill), "피해 계수 (공격력 %)")] public LevelValue DevourCoef = 130f;
    [ForSkill(nameof(DevourSkill), "고정 추가 피해")] public LevelValue DevourFlat = 0f;
    [ForSkill(nameof(DevourSkill), "사거리 (m)")] public LevelValue DevourRange = 3.2f;
    [ForSkill(nameof(DevourSkill), "각도 (°)")] public LevelValue DevourAngle = 120f;
    [ForSkill(nameof(DevourSkill), "최대 대상")] public LevelValue DevourMaxTargets = 3f;
    [ForSkill(nameof(DevourSkill), "피해 흡혈 (%)")] public LevelValue DevourHealPct = 25f;
    [ForSkill(nameof(DevourSkill), "처치 시 추가 소울")] public LevelValue DevourKillSoul = 10f;
    public float DevourHeight = 1.8f;
    [Tooltip("물고 나서 이 시간 안에 대상이 죽으면 탐식 처치로 인정 (초)")]
    public float KillCreditWindow = 1f;

    // ══════════════════ 뱉어내기 ══════════════════

    [Header("뱉어내기")]
    [ForSkill(nameof(SpitSkill), "피해 계수 (공격력 %)")] public LevelValue SpitCoef = 120f;
    [ForSkill(nameof(SpitSkill), "고정 추가 피해")] public LevelValue SpitFlat = 0f;
    [ForSkill(nameof(SpitSkill), "사거리 (m)")] public LevelValue SpitRange = 7f;
    [ForSkill(nameof(SpitSkill), "각도 (°)")] public LevelValue SpitAngle = 50f;
    [ForSkill(nameof(SpitSkill), "처치한 적 공격력 계승 (%)")] public LevelValue InheritKillAtkPct = 50f;
    [ForSkill(nameof(SpitSkill), "먹은 무기 공격력 계승 (%)")] public LevelValue InheritWeaponAtkPct = 100f;
    [ForSkill(nameof(SpitSkill), "뱉을 수 있는 시간 (초)")] public LevelValue SpitWindow = 20f;
    public float SpitHeight = 2f;
    [Tooltip("처치한 적이 이 능력을 가졌으면 추가 효과")]
    public SpitAbilityBonus[] AbilityBonuses;

    // ══════════════════ 굶주린 괴수 ══════════════════

    [Header("궁극기 — 굶주린 괴수")]
    [ForSkill(nameof(UltSkill), "지속 (초)")] public LevelValue UltDuration = 10f;
    [ForSkill(nameof(UltSkill), "처치 시 지속 증가 (초)")] public LevelValue UltKillExtend = 3f;
    [ForSkill(nameof(UltSkill), "괴수 수")] public LevelValue MonsterCount = 3f;
    [ForSkill(nameof(UltSkill), "괴수 탐지 범위 (m, 나 기준)")] public LevelValue MonsterRadius = 8f;
    [ForSkill(nameof(UltSkill), "약화된 탐식 — 위력·효과 배율 (0.5 = 50%)")] public LevelValue MonsterPowerMul = 0.5f;
    [ForSkill(nameof(UltSkill), "같은 대상 다시 물기까지 (초, 괴수마다)")] public LevelValue MonsterTargetCd = 3f;
    [ForSkill(nameof(UltSkill), "약한 적 기준 체력 (0.3 = 30%, 0 = 없음)")] public LevelValue MonsterLowHp = 0.3f;
    [ForSkill(nameof(UltSkill), "약한 적 피해 증가 (%)")] public LevelValue MonsterLowHpBonus = 50f;

    [Tooltip("궁극기 동안 나에게 붙는 변형 (가면 등). 능력치는 이 AlterData의 Stats에서 조절")]
    [FormerlySerializedAs("MonsterAlter")]
    public AlterData MaskAlter;

    [Tooltip("괴수 모델 (앞이 +Z). RigAnimator를 붙이면 Idle(제자리) · Run(날아갈 때) · Attack(물 때) 상태를 재생. 비우면 임시 구")]
    public GameObject MonsterPrefab;
    [Tooltip("괴수 공격 모션 상태 이름 (RigAnimData) — 한 번짜리(Loop 끔)로")]
    public string MonsterAttackState = "Attack";
    [Tooltip("공격 모션의 이 지점에서 이빨자국 이펙트 + 피해 (0.4 = 모션 40% 지점). 모션이 없으면 바로")]
    [Range(0f, 1f)] public float MonsterHitAt = 0.4f;
    [Tooltip("켜면 내가 전투 중일 때만 물러 감 (비전투 중엔 둘레를 돌기만)")]
    public bool MonsterOnlyInCombat = true;
    [Tooltip("'내가 최근 때린 적'으로 치는 시간 (초) — 이 안에 때린 적을 가장 먼저 노림")]
    public float RecentHitWindow = 5f;
    [Tooltip("괴수 날아가는 속도 (m/초)")]
    public float MonsterSpeed = 14f;
    [Tooltip("이 거리까지 다가가면 공격 모션 시작 (m). 무는 순간엔 이것의 1.5배 안에 있어야 맞음")]
    public float BiteReach = 1.3f;
    [Tooltip("공격 모션이 끝나고 다음 공격까지 (초)")]
    public float BiteRecover = 0.35f;
    [Tooltip("둘레 반지름 · 높이 (m) · 도는 빠르기 (°/초)")]
    public float OrbitRadius = 2.2f, OrbitHeight = 1.6f, OrbitSpin = 25f;
    [Tooltip("물고 나서 다시 물 수 있을 때까지 대상 둘레를 맴도는 반지름 (m) · 빠르기 (°/초)")]
    public float ProwlRadius = 2.5f, ProwlSpin = 45f;

    [Header("궁극기 — 괴수 이빨 연출 (모든 클라이언트)")]
    [Tooltip("괴수가 물 때 대상 앞에 띄우는 이빨 (RigAnimator 모델). 비우면 탐식 모션의 Effect 이벤트 중 RigAnimator가 있는 프리팹")]
    public GameObject MonsterTeethFx;
    [Tooltip("크기 배율 — 탐식 모션 이벤트의 크기 기준 (0.5 = 절반)")]
    public float MonsterTeethScale = 0.5f;
    [Tooltip("이빨 애니메이션 시작 후 턱이 닫히는 시점 (초). 이 순간에 피해가 들어가도록 미리 띄움")]
    public float MonsterTeethCloseAt = 0.2f;
    [Tooltip("남아 있는 시간 (초). 0이면 탐식 모션 이벤트의 수명, 그것도 없으면 0.6초")]
    public float MonsterTeethLife = 0f;
    [Tooltip("대상 중심에서 괴수 쪽으로 당겨 띄우는 거리 (m)")]
    public float MonsterTeethPull = 0.3f;
    [Tooltip("모델 방향 보정 (°) — 괴수 → 대상 방향이 기준(+Z)")]
    public Vector3 MonsterTeethRot;

    [Header("연출 — 모든 클라이언트")]
    public GameObject BiteFx;
    public GameObject SpitFx;
    [Tooltip("괴수가 무는 순간 이펙트 — 이빨 연출이 있으면 이건 넣었을 때만, 없으면 비울 때 BiteFx")]
    public GameObject MonsterBiteFx;
    public GameObject EatFx;
    public GameObject BlastFx;

    const int EvBite = 0, EvSpit = 1, EvMonster = 2, EvEat = 3, EvBlast = 4;
    // 괴수 — 키 + 괴수 번호
    const int EvBeastChase = 100, EvBeastBite = 200, EvBeastReturn = 300, EvSwarmOn = 400, EvSwarmOff = 401, EvBeastAttack = 500, EvBeastProwl = 600;
    const int SrcAbsorb = 0, SrcSpecial = 1;

    // ══════════════════ 라운드 동안 유지 (사망·재빌드해도) ══════════════════

    class SpitSource
    {
        public bool FromKill;
        public float Expire;
        public float Atk;                       // 처치한 적 공격력 or 먹은 무기 공격력
        public int[] AbilityIds;                // 처치한 적의 능력
        public readonly Dictionary<int, int> Enchants = new();   // 먹은 무기의 특수 인챈트 (ID → 등급)
        public string Label;
    }

    class State
    {
        public readonly Dictionary<(StatType, ModType), float> Absorbed = new();
        public readonly List<int> Specials = new();     // Specials 배열 번호
        public int Eaten;
        public float DigestEnd;
        public float DigestTotal;
        public SpitSource Spit;
        public float UltEnd;
    }

    private State _s;
    private string MemKey => $"devourer.{Data.Id}";

    private PlayerHealth _health;
    private PlayerEquipment _equip;
    private PlayerEnchant _enchant;
    private PlayerAlter _alter;
    private PlayerAttack _attack;

    struct Bitten { public PlayerHealth H; public float Time; }
    private readonly List<Bitten> _recent = new();

    struct Pending { public float At; public Vector3 Pos; public float Coef; public float Radius; }
    private readonly List<Pending> _blasts = new();

    // ── 괴수 ──
    class Beast
    {
        public PlayerHealth Target;
        public bool Prowl;              // 대상 근처를 맴도는 중 (다시 물 수 있을 때까지 기다림)
        public float NextBite;
        public float HitAt = -1f;       // 공격 모션 중 — 이 시각에 물기 (-1 = 아님)
        public float AttackEnd;         // 공격 모션 끝 — 그때까지 대상 앞에 붙어 있음
        public readonly Dictionary<PlayerHealth, float> HitUntil = new();
    }
    private List<Beast> _beasts;                // 권한자만
    private DevourerSwarm _swarm;               // 모든 클라이언트 (연출)
    private PlayerHealth _lastHit;              // 내가 최근 때린 적
    private float _lastHitTime = -999f;

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[32];
    [System.NonSerialized] private readonly HashSet<PlayerHealth> _seen = new();

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    public bool InUlt => _s != null && _s.UltEnd > Now;
    public bool Digesting => _s != null && _s.DigestEnd > Now;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _health = Ctx.GetComponent<PlayerHealth>();
        _equip = Ctx.GetComponent<PlayerEquipment>();
        _enchant = Ctx.GetComponent<PlayerEnchant>();
        _alter = Ctx.GetComponent<PlayerAlter>();
        _attack = Ctx.GetComponent<PlayerAttack>();

        LoadState();
        if (!Ctx.HasStateAuthority) return;

        ApplyStats();
        if (_attack != null) _attack.WeaponHit += OnWeaponHit;

        if (InUlt)
        {
            if (MaskAlter != null) _alter?.Add(MaskAlter);
            StartSwarm();
        }
    }

    public override void OnUnequip()
    {
        KillSwarm();
        if (!Ctx.HasStateAuthority) return;

        if (_attack != null) _attack.WeaponHit -= OnWeaponHit;
        Ctx.Stats?.RemoveBySource(Src(SrcAbsorb));
        Ctx.Stats?.RemoveBySource(Src(SrcSpecial));
        if (MaskAlter != null) _alter?.Remove(MaskAlter);
        RemoveSpecialAlters();
    }

    void LoadState()
    {
        var mem = AbilityMemory.Of(Ctx);
        if (Ctx.HasStateAuthority && Ctx.GetSaved(Data.Id) == 0)
        {
            mem.Remove(MemKey);             // 새 라운드
            Ctx.SetSaved(Data.Id, 1);
        }
        _s = mem.GetOrCreate<State>(MemKey);
    }

    public override void Tick(float dt)
    {
        if (!Ctx.HasStateAuthority) return;

        // 라운드가 바뀌었으면 처음부터
        if (Ctx.GetSaved(Data.Id) == 0)
        {
            StopSwarm();                                               // 다른 화면 괴수도 지움
            OnUnequip();
            if (_attack != null) _attack.WeaponHit += OnWeaponHit;     // OnUnequip이 뗀 걸 다시
            LoadState();
            ApplyStats();
        }

        bool dead = _health != null && _health.IsDead;

        TickDigestGauge();
        TickKillCredit();
        TickBlasts();

        if (_s.Spit != null && Now > _s.Spit.Expire) _s.Spit = null;

        if (_s.UltEnd > 0f)
        {
            if (dead || Now >= _s.UltEnd) EndUlt();
            else TickMonsters();
        }
    }

    /// <summary>내가 무기로 때린 적 — 괴수가 가장 먼저 노림</summary>
    void OnWeaponHit(PlayerHealth target, float outgoing, bool enchants) => MarkHit(target);

    void MarkHit(PlayerHealth h)
    {
        if (h == null || h.IsDead) return;
        _lastHit = h;
        _lastHitTime = Now;
    }

    // ══════════════════ 스킬 ══════════════════

    int IndexOf(AbilityData.SkillDef s) => System.Array.IndexOf(Data.Skills, s);

    /// <summary>누르기 전 확인 — 안 되면 쿨타임도 안 돎</summary>
    public override bool CanUseSkill(AbilityData.SkillDef skill)
    {
        int i = IndexOf(skill);
        if (i == SpitSkill) return _s != null && _s.Spit != null && Now <= _s.Spit.Expire;
        if (i == EatSkill) return CanEat(out _);
        return true;
    }

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        int i = IndexOf(skill);
        if (i == DevourSkill) { Devour(); return true; }
        if (i == SpitSkill) { Spit(); return true; }
        if (i == EatSkill) { Eat(); return true; }
        if (i == UltSkill) { BeginUlt(); return true; }
        return false;
    }

    // ── 무구 섭취 ──

    bool CanEat(out EquipmentData w)
    {
        w = null;
        if (_equip == null || Digesting) return false;

        int slot = _equip.ActiveWeaponSlot;
        if (_equip.GetWeaponIdAt(slot) == 0) return false;         // 맨손

        w = _equip.GetWeapon();
        if (w == null) return false;
        return !(w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun);   // 총기는 못 먹음
    }

    void Eat()
    {
        if (!CanEat(out var w)) return;

        int slot = _equip.ActiveWeaponSlot;
        if (!_equip.TakeWeaponAt(slot, out int id, out int[] enchants)) return;

        float maxBefore = Ctx.Stats.Get(StatType.MaxHP);
        int grade = Mathf.Clamp((int)w.Grade, 0, 3);
        float ratio = Lv(AbsorbRatio) * Grade(GradeAbsorbMul, grade, 1f);

        // ① 무기 기본 능력치
        float weaponAtk = 0f;
        if (w.BaseStats != null)
            foreach (var st in w.BaseStats)
            {
                Absorb(st.Stat, st.Type, st.Value * ratio);
                if (st.Stat == StatType.AttackPower && st.Type == ModType.Flat) weaponAtk += st.Value;
            }

        // ② 일반 인챈트 능력치 · 특수 인챈트 목록
        var spit = new SpitSource { FromKill = false, Atk = weaponAtk, Label = Display(w) };
        if (enchants != null)
            foreach (int code in enchants)
            {
                if (code == 0) break;
                if (EnchantCode.IsSpecial(code))
                {
                    int sid = EnchantCode.SpecialId(code), tier = EnchantCode.Tier(code);
                    if (!spit.Enchants.TryGetValue(sid, out int cur) || tier > cur) spit.Enchants[sid] = tier;
                    continue;
                }
                if (Lv(AbsorbEnchants) < 0.5f) continue;
                var ed = _enchant != null && _enchant.Database != null ? _enchant.Database.Get(code) : null;
                if (ed?.Stats == null) continue;
                foreach (var st in ed.Stats) Absorb(st.Stat, st.Type, st.Value * ratio);
            }

        // ③ 특수 패시브
        if (Specials != null)
            for (int i = 0; i < Specials.Length; i++)
            {
                var sp = Specials[i];
                if (sp?.Weapon != null && sp.Weapon.Id == id && !_s.Specials.Contains(i))
                {
                    _s.Specials.Add(i);
                    if (sp.Alter != null) _alter?.Add(sp.Alter);
                }
            }

        // ④ 소화 · 뱉을 것
        _s.Eaten++;
        _s.DigestTotal = Grade(GradeDigestTime, grade, 15f) * Mathf.Max(0f, Lv(DigestMul));
        _s.DigestEnd = Now + _s.DigestTotal;
        spit.Expire = Now + Lv(SpitWindow);
        _s.Spit = spit;

        ApplyStats();
        _health?.ApplyMaxHpGain(maxBefore);
        Broadcast(EvEat, Ctx.transform.position + Vector3.up * 1.4f);
    }

    static float Grade(float[] arr, int g, float fallback) =>
        arr != null && arr.Length > 0 ? arr[Mathf.Clamp(g, 0, arr.Length - 1)] : fallback;

    static string Display(EquipmentData w) =>
        w == null ? "" : LocUtil.GetOr($"weapon.{w.Id}.name", LocUtil.Resolve(w.DisplayName));

    void Absorb(StatType stat, ModType type, float v)
    {
        if (Mathf.Approximately(v, 0f)) return;
        var key = (stat, type);
        _s.Absorbed.TryGetValue(key, out float cur);
        _s.Absorbed[key] = cur + v;
    }

    void ApplyStats()
    {
        var stats = Ctx.Stats;
        if (stats == null || _s == null) return;

        stats.RemoveBySource(Src(SrcAbsorb));
        foreach (var kv in _s.Absorbed)
            stats.AddModifier(new StatModifier(kv.Key.Item1, kv.Key.Item2, kv.Value, Src(SrcAbsorb)));

        stats.RemoveBySource(Src(SrcSpecial));
        foreach (int i in _s.Specials)
        {
            var sp = Specials != null && i < Specials.Length ? Specials[i] : null;
            if (sp?.Stats == null) continue;
            foreach (var st in sp.Stats)
                if (st != null) stats.AddModifier(new StatModifier(st.Stat, st.Type, st.Value, Src(SrcSpecial)));
        }
    }

    void RemoveSpecialAlters()
    {
        if (_s == null || _alter == null || Specials == null) return;
        foreach (int i in _s.Specials)
            if (i < Specials.Length && Specials[i]?.Alter != null) _alter.Remove(Specials[i].Alter);
    }

    void TickDigestGauge()
    {
        float total = Mathf.Max(1f, _s.DigestTotal);
        Ctx.SetMaxCharge(DigestChargeSlot, total);
        float left = Mathf.Max(0f, _s.DigestEnd - Now);
        float cur = Ctx.GetCharge(DigestChargeSlot);
        Ctx.AddCharge(DigestChargeSlot, left - cur);       // 남은 소화 시간을 게이지로
    }

    // ── 특수 패시브: 타격 시 상태이상 ──

    public override void OnTrigger(TriggerType t, GameObject other)
    {
        if (t != TriggerType.OnHit || other == null || _s == null || Specials == null) return;
        var st = other.GetComponentInParent<PlayerStatus>();
        if (st == null) return;

        foreach (int i in _s.Specials)
        {
            var sp = i < Specials.Length ? Specials[i] : null;
            if (sp == null || !sp.OnHitStatus) continue;
            if (Random.value <= sp.StatusChance)
                st.Apply(sp.Status, sp.StatusValue, sp.StatusTime, Ctx.Object.InputAuthority);
        }
    }

    // ── 탐식 ──

    void Devour()
    {
        Vector3 o = Ctx.transform.position;
        Vector3 fwd = Forward();
        float coef = Lv(DevourCoef) * 0.01f, flat = Lv(DevourFlat);
        int max = Mathf.Max(1, Mathf.RoundToInt(Lv(DevourMaxTargets)));

        float dealt = 0f;
        int n = 0;
        foreach (var h in Cone(o, fwd, Lv(DevourRange), Lv(DevourAngle), DevourHeight))
        {
            var info = MakeHit(coef, flat, DamageSource.Skill, out float dmg);
            info.IsMelee = true;
            h.RPC_TakeDamage(info);
            dealt += dmg;
            MarkBitten(h);
            MarkHit(h);
            if (++n >= max) break;
        }

        if (dealt > 0f) _health?.RPC_Heal(Mathf.RoundToInt(dealt * Lv(DevourHealPct) * 0.01f));
        Broadcast(EvBite, o + fwd * 1.5f + Vector3.up * 1.2f);
    }

    /// <summary>물고 나서 잠시 안에 죽으면 탐식 처치 — 소울 · 쿨 초기화 · 뱉을 것 · 궁 연장</summary>
    /// <summary>탐식 처치 후보로 기록 — 같은 대상은 한 줄만 (여러 번 물면 시간만 갱신)</summary>
    void MarkBitten(PlayerHealth h)
    {
        for (int i = 0; i < _recent.Count; i++)
            if (_recent[i].H == h) { _recent[i] = new Bitten { H = h, Time = Now }; return; }
        _recent.Add(new Bitten { H = h, Time = Now });
    }

    void TickKillCredit()
    {
        for (int i = _recent.Count - 1; i >= 0; i--)
        {
            if (i >= _recent.Count) continue;           // 아래에서 여러 줄을 지웠을 때
            var b = _recent[i];
            if (b.H == null || Now - b.Time > KillCreditWindow) { _recent.RemoveAt(i); continue; }
            if (!b.H.IsDead) continue;

            _recent.RemoveAll(x => x.H == b.H);         // 한 처치는 한 번만 (괴수 여러 마리 · 탐식이 같이 물어도)
            OnDevourKill(b.H);
        }
    }

    void OnDevourKill(PlayerHealth victim)
    {
        int soul = Mathf.RoundToInt(Lv(DevourKillSoul));
        if (soul > 0) _health?.RPC_GainSoul(soul);

        Ctx.ResetCooldown(Data.Skills[DevourSkill].Slot.Index());

        // 뱉을 것 — 처치한 적의 공격력·능력
        var spit = new SpitSource { FromKill = true, Expire = Now + Lv(SpitWindow) };
        if (victim.TryGetComponent<PlayerStats>(out var vs)) spit.Atk = vs.Get(StatType.AttackPower);
        if (victim.TryGetComponent<AbilityRuntime>(out var vr))
            spit.AbilityIds = new[] { vr.MainAbilityId, vr.SubAbilityId, vr.RandomAbilityId };
        spit.Label = victim.name;
        _s.Spit = spit;

        if (InUlt) _s.UltEnd += Lv(UltKillExtend);
    }

    // ── 뱉어내기 ──

    void Spit()
    {
        var src = _s.Spit;
        if (src == null) return;
        _s.Spit = null;

        Vector3 o = Ctx.transform.position;
        Vector3 fwd = Forward();

        float coef = Lv(SpitCoef) * 0.01f;
        float flat = Lv(SpitFlat) + src.Atk * (src.FromKill ? Lv(InheritKillAtkPct) : Lv(InheritWeaponAtkPct)) * 0.01f;

        var bonuses = src.FromKill ? MatchBonuses(src.AbilityIds) : null;
        if (bonuses != null) foreach (var b in bonuses) coef += b.BonusCoef * 0.01f;

        var self = Ctx.Object.InputAuthority;
        foreach (var h in Cone(o, fwd, Lv(SpitRange), Lv(SpitAngle), SpitHeight))
        {
            var info = MakeHit(coef, flat, DamageSource.Skill, out _);
            ApplyInheritedPreHit(src, ref info);
            h.RPC_TakeDamage(info);
            ApplyInheritedPostHit(src, h);
            MarkHit(h);

            if (bonuses != null)
                foreach (var b in bonuses)
                {
                    if (b.ApplyStatus && h.TryGetComponent<PlayerStatus>(out var st))
                        st.Apply(b.Status, b.StatusValue, b.StatusTime, self);
                    if (b.SecondBlast)
                        _blasts.Add(new Pending { At = Now + b.BlastDelay, Pos = h.transform.position, Coef = b.BlastCoef * 0.01f, Radius = b.BlastRadius });
                }
        }

        Broadcast(EvSpit, o + fwd * 1.2f + Vector3.up * 1.4f);
    }

    List<SpitAbilityBonus> MatchBonuses(int[] ids)
    {
        if (ids == null || AbilityBonuses == null) return null;
        List<SpitAbilityBonus> list = null;
        foreach (var b in AbilityBonuses)
        {
            if (b?.Ability == null) continue;
            foreach (int id in ids)
                if (id != 0 && id == b.Ability.Id) { (list ??= new()).Add(b); break; }
        }
        return list;
    }

    /// <summary>먹은 무기의 관통·처형</summary>
    void ApplyInheritedPreHit(SpitSource src, ref DamageInfo info)
    {
        foreach (var e in InheritedEffects(src))
        {
            if (Random.value > e.GetChance()) continue;
            if (e.Kind == SpecialEffectKind.DefensePen)
                info.DefensePen = Mathf.Clamp01(info.DefensePen + e.Value * 0.01f);
            else if (e.Kind == SpecialEffectKind.Execute)
            {
                info.ExecuteBelow = Mathf.Max(info.ExecuteBelow, e.Value * 0.01f);
                info.ExecuteBonus = Mathf.Max(info.ExecuteBonus, e.Value2 * 0.01f);
            }
        }
    }

    /// <summary>먹은 무기의 상태이상·최대 체력 피해 (화염 인챈트였으면 화상)</summary>
    void ApplyInheritedPostHit(SpitSource src, PlayerHealth target)
    {
        var self = Ctx.Object.InputAuthority;
        foreach (var e in InheritedEffects(src))
        {
            if (Random.value > e.GetChance()) continue;
            switch (e.Kind)
            {
                case SpecialEffectKind.ApplyStatus:
                    if (target.TryGetComponent<PlayerStatus>(out var st))
                        st.Apply(e.Status, e.Value, e.Duration, self, e.GetMaxStacks());
                    break;
                case SpecialEffectKind.MaxHpDamage:
                    var info = DamageInfo.FromPlayerHit(0, self, Ctx.transform.position);
                    info.MaxHpPercent = e.Value;
                    info.IgnoreDefense = e.IgnoreDefense;
                    info.IsDot = true;
                    target.RPC_TakeDamage(info);
                    break;
            }
        }
    }

    IEnumerable<SpecialEffect> InheritedEffects(SpitSource src)
    {
        if (src.FromKill || src.Enchants.Count == 0 || _enchant == null || _enchant.Database == null) yield break;
        foreach (var kv in src.Enchants)
        {
            var tier = _enchant.Database.GetSpecial(kv.Key)?.GetTier(kv.Value);
            if (tier?.Effects == null) continue;
            foreach (var e in tier.Effects) if (e != null) yield return e;
        }
    }

    void TickBlasts()
    {
        for (int i = _blasts.Count - 1; i >= 0; i--)
        {
            var p = _blasts[i];
            if (Now < p.At) continue;
            _blasts.RemoveAt(i);

            foreach (var h in Around(p.Pos, p.Radius))
                h.RPC_TakeDamage(MakeHit(p.Coef, 0f, DamageSource.Skill, out _));
            Broadcast(EvBlast, p.Pos);
        }
    }

    // ══════════════════ 굶주린 괴수 ══════════════════

    void BeginUlt()
    {
        if (!InUlt && MaskAlter != null) _alter?.Add(MaskAlter);
        _s.UltEnd = Now + Lv(UltDuration);
        StartSwarm();
    }

    void EndUlt()
    {
        bool was = _s.UltEnd > 0f;
        _s.UltEnd = 0f;
        if (MaskAlter != null) _alter?.Remove(MaskAlter);
        StopSwarm();
        if (was) ReleaseCooldown(UltSkill);       // '효과 끝나고 쿨타임'이면 지금부터
    }

    /// <summary>권한자 — 괴수 상태를 새로 만들고 모든 화면에 괴수를 띄움</summary>
    void StartSwarm()
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(Lv(MonsterCount)), 1, 12);
        _beasts = new List<Beast>(n);
        for (int i = 0; i < n; i++) _beasts.Add(new Beast());

        MakeSwarm(n);
        Broadcast(EvSwarmOn, new Vector3(n, 0f, 0f));
    }

    void StopSwarm()
    {
        bool had = _swarm != null || _beasts != null;
        _beasts = null;
        KillSwarm();
        if (had) Broadcast(EvSwarmOff, Vector3.zero);
    }

    /// <summary>모든 클라이언트 — 괴수 연출 만들기</summary>
    void MakeSwarm(int n)
    {
        KillSwarm();
        _swarm = DevourerSwarm.Create(Ctx.transform, _health, MonsterPrefab, n);
        _swarm.Radius = OrbitRadius;
        _swarm.Height = OrbitHeight;
        _swarm.Spin = OrbitSpin;
        _swarm.Speed = MonsterSpeed;
        _swarm.AttackState = MonsterAttackState;
        _swarm.StopDistance = Mathf.Max(0.3f, BiteReach * 0.7f);
        _swarm.ProwlRadius = ProwlRadius;
        _swarm.ProwlSpin = ProwlSpin;

        ResolveTeeth(out var teeth, out float scale, out float life, out string state);
        _swarm.SetTeeth(teeth, scale, life, state, MonsterTeethRot, MonsterTeethPull);
    }

    void KillSwarm()
    {
        if (_swarm != null) Destroy(_swarm.gameObject);
        _swarm = null;
    }

    /// <summary>권한자 — 괴수마다 대상 고르기 · 물기</summary>
    void TickMonsters()
    {
        if (_swarm == null || _beasts == null) { StartSwarm(); return; }

        bool fight = !MonsterOnlyInCombat || (_health != null && _health.InCombat);
        float range = Lv(MonsterRadius);
        Vector3 me = Ctx.transform.position;

        for (int i = 0; i < _beasts.Count; i++)
        {
            var b = _beasts[i];

            // 공격 모션 중 — 정한 지점에서 물고, 모션이 끝날 때까지 대상 앞에 붙어 있음
            if (b.HitAt >= 0f && Now >= b.HitAt)
            {
                b.HitAt = -1f;
                Bite(i, b, me, range);
            }
            if (Now < b.AttackEnd) continue;

            // ① 지금 대상을 물 수 있으면 계속 ② 다른 물 수 있는 대상 ③ 방금 문 대상이 범위 안이면 그 둘레를 맴돌며 기다림 ④ 돌아옴
            //   (범위는 살짝 여유 — 경계에서 왔다 갔다 안 하게)
            float keep = range * 1.15f;
            var want = b.Target != null && CanBite(b, b.Target, me, keep, fight) ? b.Target : null;
            if (want == null && fight) want = PickTarget(b, me, range);
            if (want != null) Retarget(i, want, false);
            else if (b.Target != null && InRange(b.Target, me, keep, fight)) Retarget(i, b.Target, true);
            else Retarget(i, null, false);

            if (b.Target == null || b.Prowl || Now < b.NextBite) continue;

            Vector3 aim = b.Target.transform.position + Vector3.up * _swarm.AimHeight;
            if ((aim - _swarm.Position(i)).sqrMagnitude <= BiteReach * BiteReach) StartAttack(i, b);
        }
    }

    /// <summary>공격 모션 시작 — MonsterHitAt 지점에서 Bite</summary>
    void StartAttack(int i, Beast b)
    {
        float len = _swarm.AttackLength;                   // 모션이 없으면 0
        float hit = HitDelay(len);
        b.HitAt = Now + hit;
        b.AttackEnd = Now + Mathf.Max(len, hit);
        b.NextBite = b.AttackEnd + BiteRecover;

        _swarm.Attack(i, TeethDelay(len));
        Broadcast(EvBeastAttack + i, b.Target.transform.position);
    }

    /// <summary>공격 시작 → 물기까지 (초). 이빨 연출이 있으면 턱이 닫힐 시간은 확보</summary>
    float HitDelay(float attackLen)
    {
        float hit = attackLen * MonsterHitAt;
        if (_swarm != null && _swarm.HasTeeth) hit = Mathf.Max(hit, MonsterTeethCloseAt);
        return hit;
    }

    /// <summary>공격 시작 → 이빨 띄우기까지 (초) — 턱이 닫히는 순간 = 물기</summary>
    float TeethDelay(float attackLen) => Mathf.Max(0f, HitDelay(attackLen) - MonsterTeethCloseAt);

    /// <summary>이빨 프리팹 · 크기 · 수명 · Rig 상태 — 비어 있으면 탐식 모션의 Effect 이벤트에서 가져옴</summary>
    void ResolveTeeth(out GameObject prefab, out float scale, out float life, out string state)
    {
        prefab = MonsterTeethFx;
        scale = MonsterTeethScale;
        life = MonsterTeethLife;
        state = null;

        var skills = Data != null ? Data.Skills : null;
        var sk = skills != null && DevourSkill >= 0 && DevourSkill < skills.Length ? skills[DevourSkill] : null;
        var motion = sk != null ? sk.Motion : null;

        MotionEvent src = null;
        if (motion != null && motion.Events != null)
            foreach (var e in motion.Events)
            {
                if (e == null || e.Type != MotionEventType.Effect || e.Prefab == null) continue;
                bool match = prefab != null ? e.Prefab == prefab : e.Prefab.GetComponentInChildren<RigAnimator>(true) != null;
                if (match) { src = e; break; }
            }

        if (src != null)
        {
            if (prefab == null) prefab = src.Prefab;
            scale *= src.Scale;
            if (life <= 0f) life = src.Lifetime;
            state = src.RigState;
        }
        if (life <= 0f) life = 0.6f;
    }

    /// <summary>살아 있는 적이 범위 안에 있음 (최근에 물었는지는 안 봄)</summary>
    bool InRange(PlayerHealth h, Vector3 me, float range, bool fight)
    {
        if (!fight || h == null || h.IsDead) return false;
        if (h.transform.root == Ctx.transform.root) return false;
        if (TeamUtil.IsFriendly(Ctx.Object.InputAuthority, h)) return false;
        Vector3 d = h.transform.position - me;
        return d.sqrMagnitude <= range * range;
    }

    bool CanBite(Beast b, PlayerHealth h, Vector3 me, float range, bool fight)
    {
        if (!InRange(h, me, range, fight)) return false;
        return !(b.HitUntil.TryGetValue(h, out float until) && Now < until);      // 최근에 이 괴수가 문 대상
    }

    /// <summary>① 내가 최근 때린 적 ② 범위 안 체력이 가장 낮은 적</summary>
    PlayerHealth PickTarget(Beast b, Vector3 me, float range)
    {
        if (_lastHit != null && Now - _lastHitTime <= RecentHitWindow && CanBite(b, _lastHit, me, range, true))
            return _lastHit;

        PlayerHealth best = null;
        foreach (var h in Around(me, range))
        {
            if (!CanBite(b, h, me, range, true)) continue;
            if (best == null || h.HP < best.HP) best = h;
        }
        return best;
    }

    /// <summary>prowl = 대상에게 달려들지 않고 둘레를 맴돎</summary>
    void Retarget(int i, PlayerHealth t, bool prowl)
    {
        var b = _beasts[i];
        if (t == null) prowl = false;
        if (b.Target == t && b.Prowl == prowl) return;
        b.Target = t;
        b.Prowl = prowl;
        if (_swarm != null) _swarm.SetTarget(i, t != null ? t.transform : null, prowl);
        if (t == null) Broadcast(EvBeastReturn + i, Vector3.zero);
        else Broadcast((prowl ? EvBeastProwl : EvBeastChase) + i, t.transform.position);
    }

    /// <summary>무는 순간 — 약화된 탐식 (위력 · 흡혈 × MonsterPowerMul) + 이빨자국 이펙트. 그새 피했으면 빗나감</summary>
    void Bite(int i, Beast b, Vector3 me, float range)
    {
        var h = b.Target;
        if (h == null || !CanBite(b, h, me, range * 1.15f, true)) return;
        Vector3 aim = h.transform.position + Vector3.up * _swarm.AimHeight;
        float reach = BiteReach * 1.5f;
        if ((aim - _swarm.Position(i)).sqrMagnitude > reach * reach) return;     // 모션 도중 빠져나감

        float mul = Lv(MonsterPowerMul);

        var info = MakeHit(Lv(DevourCoef) * 0.01f * mul, Lv(DevourFlat) * mul, DamageSource.Ultimate, out float dmg);
        info.IsMelee = true;
        if (Lv(MonsterLowHp) > 0f)
        {
            info.ExecuteBelow = Lv(MonsterLowHp);                 // 맞는 쪽에서 정확한 체력으로 판정
            info.ExecuteBonus = Lv(MonsterLowHpBonus) * 0.01f;
        }
        h.RPC_TakeDamage(info);

        if (dmg > 0f) _health?.RPC_Heal(Mathf.RoundToInt(dmg * Lv(DevourHealPct) * 0.01f * mul));
        MarkBitten(h);

        b.HitUntil[h] = Now + Lv(MonsterTargetCd);          // 이 괴수는 3초 동안 이 대상 안 물음

        _swarm.Bite(i);
        Broadcast(EvBeastBite + i, h.transform.position + Vector3.up * 1.2f);
        // 모션이 끝나면 TickMonsters에서 다른 대상 · 없으면 돌아옴
    }

    /// <summary>다른 화면 — 이 위치에 가장 가까운 사람 (괴수가 쫓을 대상)</summary>
    Transform NearestPlayer(Vector3 pos)
    {
        Transform best = null;
        float bestD = 4f;
        int n = Physics.OverlapSphereNonAlloc(pos, 2f, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (h == null || h.transform.root == Ctx.transform.root) continue;      // 주인은 빼고
            float d = (h.transform.position - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h.transform; }
        }
        return best;
    }

    // ══════════════════ 설명 ({x:키}) ══════════════════

    public override string DescToken(string key, int rank)
    {
        if (_s == null) return null;
        switch (key)
        {
            case "digest":
                return Digesting ? $"소화 중 {Mathf.CeilToInt(_s.DigestEnd - Now)}초" : "소화 완료 — 먹을 수 있음";
            case "eaten":
                return _s.Eaten.ToString();
            case "absorbed":
                return AbsorbedLine();
            case "specials":
                return SpecialsLine();
            case "spit":
                if (_s.Spit == null || Now > _s.Spit.Expire) return null;
                return _s.Spit.FromKill ? "처치한 적의 힘을 뱉을 수 있음" : $"{_s.Spit.Label}을(를) 뱉을 수 있음";
        }
        return null;
    }

    string AbsorbedLine()
    {
        if (_s.Absorbed.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var kv in _s.Absorbed)
        {
            var (stat, type) = kv.Key;
            bool ratio = type == ModType.Flat && (stat is StatType.CritChance or StatType.CritDamage or StatType.CooldownReduction or StatType.LifeSteal);
            float v = ratio ? kv.Value * 100f : kv.Value;
            bool pct = ratio || type == ModType.Percent;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(LocUtil.Get($"stat.{stat}")).Append(' ').Append(v > 0f ? "+" : "")
              .Append(v.ToString("0.#")).Append(pct ? "%" : "");
        }
        return sb.ToString();
    }

    string SpecialsLine()
    {
        if (_s.Specials.Count == 0 || Specials == null) return null;
        var sb = new StringBuilder();
        foreach (int i in _s.Specials)
        {
            if (i >= Specials.Length || Specials[i] == null) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<b>").Append(LocUtil.Resolve(Specials[i].Name)).Append("</b>");
            if (!string.IsNullOrWhiteSpace(Specials[i].Description))
                sb.Append(" — ").Append(LocUtil.Resolve(Specials[i].Description));
        }
        return sb.ToString();
    }

    // ══════════════════ 공통 ══════════════════

    DamageInfo MakeHit(float coef, float flat, DamageSource src, out float dmg)
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

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key >= EvBeastChase) { BeastEvent(key, pos); return; }

        GameObject fx = key switch
        {
            EvBite => BiteFx,
            EvSpit => SpitFx,
            EvMonster => MonsterBiteFx != null ? MonsterBiteFx : BiteFx,
            EvEat => EatFx,
            EvBlast => BlastFx,
            _ => null,
        };
        Ctx.PlayEffectLocal(fx, pos);
    }

    /// <summary>괴수 연출 — 권한자는 이미 직접 했으니 다른 화면만 따라 함 (물기 이펙트는 모두)</summary>
    void BeastEvent(int key, Vector3 pos)
    {
        bool sa = Ctx.HasStateAuthority;

        if (key == EvSwarmOn) { if (!sa) MakeSwarm(Mathf.Max(1, Mathf.RoundToInt(pos.x))); return; }
        if (key == EvSwarmOff) { if (!sa) KillSwarm(); return; }

        int kind = key / 100 * 100;
        int i = key - kind;

        switch (kind)
        {
            case EvBeastChase:
                if (!sa && _swarm != null) _swarm.SetTarget(i, NearestPlayer(pos), false);
                break;
            case EvBeastProwl:
                if (!sa && _swarm != null) _swarm.SetTarget(i, NearestPlayer(pos), true);
                break;
            case EvBeastReturn:
                if (!sa && _swarm != null) _swarm.SetTarget(i, null, false);
                break;
            case EvBeastAttack:
                if (!sa && _swarm != null)
                {
                    _swarm.SetTarget(i, NearestPlayer(pos), false);
                    _swarm.Attack(i, TeethDelay(_swarm.AttackLength));
                }
                break;
            case EvBeastBite:
                // 이빨 연출이 있으면 그게 무는 이펙트 — MonsterBiteFx는 넣었을 때만 추가로
                if (_swarm != null && _swarm.HasTeeth) Ctx.PlayEffectLocal(MonsterBiteFx, pos);
                else Ctx.PlayEffectLocal(MonsterBiteFx != null ? MonsterBiteFx : BiteFx, pos);
                if (!sa && _swarm != null) _swarm.Bite(i);
                break;
        }
    }

    // ══════════════════ 치트 (/job) ══════════════════

    public override string DevCommand(string[] a)
    {
        if (_s == null) LoadState();
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "help";
        switch (sub)
        {
            case "help":
                return "탐식자: /job digest [초] · special [번호·이름] · specials · spit [공격력] · ult · reset";

            case "digest":
            case "소화":
                {
                    float sec = a.Length > 1 && float.TryParse(a[1], System.Globalization.NumberStyles.Float,
                                                               System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
                    _s.DigestTotal = Mathf.Max(1f, sec);
                    _s.DigestEnd = Now + sec;
                    TickDigestGauge();
                    return sec <= 0f ? "소화 끝 (바로 먹을 수 있음)" : $"소화 {sec:0.#}초";
                }

            case "specials":
                {
                    if (Specials == null || Specials.Length == 0) return "특수 패시브 목록이 비어 있음";
                    var sb = new StringBuilder("특수 패시브:");
                    for (int i = 0; i < Specials.Length; i++)
                        if (Specials[i] != null)
                            sb.Append($"\n  {i + 1}. {LocUtil.Resolve(Specials[i].Name)}" +
                                      (Specials[i].Weapon != null ? $" ({Specials[i].Weapon.Id})" : "") +
                                      (_s.Specials.Contains(i) ? "  ✔" : ""));
                    return sb.ToString();
                }

            case "special":
            case "특수":
                {
                    if (Specials == null || Specials.Length == 0) return "특수 패시브 목록이 비어 있음";
                    int idx = -1;
                    if (a.Length > 1)
                    {
                        if (int.TryParse(a[1], out int n)) idx = n - 1;
                        else for (int i = 0; i < Specials.Length; i++)
                                if (Specials[i] != null && LocUtil.Resolve(Specials[i].Name).Contains(a[1])) { idx = i; break; }
                    }
                    else
                    {
                        var left = new List<int>();
                        for (int i = 0; i < Specials.Length; i++) if (Specials[i] != null && !_s.Specials.Contains(i)) left.Add(i);
                        if (left.Count > 0) idx = left[Random.Range(0, left.Count)];
                    }
                    if (idx < 0 || idx >= Specials.Length || Specials[idx] == null) return "그 특수 패시브 없음 — /job specials";
                    if (_s.Specials.Contains(idx)) return "이미 있음";

                    float maxBefore = Ctx.Stats.Get(StatType.MaxHP);
                    _s.Specials.Add(idx);
                    if (Specials[idx].Alter != null) _alter?.Add(Specials[idx].Alter);
                    ApplyStats();
                    _health?.ApplyMaxHpGain(maxBefore);
                    return $"특수 패시브: {LocUtil.Resolve(Specials[idx].Name)}";
                }

            case "spit":
            case "뱉기":
                {
                    float atk = a.Length > 1 && float.TryParse(a[1], out float v) ? v : 50f;
                    _s.Spit = new SpitSource { FromKill = false, Atk = atk, Expire = Now + Lv(SpitWindow), Label = "치트" };
                    return $"뱉을 것 준비 (공격력 {atk:0})";
                }

            case "ult":
            case "궁":
                BeginUlt();
                return $"괴수화 — 괴수 {_beasts?.Count ?? 0}마리";

            case "reset":
            case "초기화":
                Ctx.SetSaved(Data.Id, 0);        // 다음 틱에 새 라운드처럼 비움
                return "먹은 것 · 흡수 능력치 · 특수 패시브 초기화";
        }
        return null;
    }

    public override IEnumerable<(string value, string label)> DevComplete(string[] a, int index)
    {
        if (index == 0)
            return new[] { ("digest", "소화 남은 시간 (비우면 끝)"), ("special", "특수 패시브 받기"), ("specials", "특수 패시브 목록"),
                           ("spit", "뱉을 것 준비"), ("ult", "괴수화"), ("reset", "먹은 것 초기화") };
        if (index == 1 && a.Length > 0 && (a[0] == "special" || a[0] == "특수") && Specials != null)
        {
            var list = new List<(string, string)>();
            for (int i = 0; i < Specials.Length; i++)
                if (Specials[i] != null) list.Add(((i + 1).ToString(), LocUtil.Resolve(Specials[i].Name)));
            return list;
        }
        return null;
    }
}