using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

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
///   궁극기    굶주린 괴수    — N초 변형. 주위 적을 괴수가 자동으로 물어뜯음 (대상마다 3초). 체력 30% 이하 적에게 +50%.
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
    [ForSkill(nameof(UltSkill), "괴수 탐식 계수 (공격력 %)")] public LevelValue MonsterCoef = 80f;
    [ForSkill(nameof(UltSkill), "괴수 탐식 고정 피해")] public LevelValue MonsterFlat = 0f;
    [ForSkill(nameof(UltSkill), "괴수 탐식 반경 (m)")] public LevelValue MonsterRadius = 6f;
    [ForSkill(nameof(UltSkill), "대상별 간격 (초)")] public LevelValue MonsterTargetCd = 3f;
    [ForSkill(nameof(UltSkill), "약한 적 기준 체력 (0.3 = 30%)")] public LevelValue MonsterLowHp = 0.3f;
    [ForSkill(nameof(UltSkill), "약한 적 피해 증가 (%)")] public LevelValue MonsterLowHpBonus = 50f;
    [Tooltip("변형 겉모습 (괴수 파츠)")]
    public AlterData MonsterAlter;

    [Header("연출 — 모든 클라이언트")]
    public GameObject BiteFx;
    public GameObject SpitFx;
    public GameObject MonsterBiteFx;
    public GameObject EatFx;
    public GameObject BlastFx;

    const int EvBite = 0, EvSpit = 1, EvMonster = 2, EvEat = 3, EvBlast = 4;
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

    struct Bitten { public PlayerHealth H; public float Time; }
    private readonly List<Bitten> _recent = new();
    private readonly Dictionary<PlayerHealth, float> _monsterCd = new();

    struct Pending { public float At; public Vector3 Pos; public float Coef; public float Radius; }
    private readonly List<Pending> _blasts = new();

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

        LoadState();
        if (!Ctx.HasStateAuthority) return;

        ApplyStats();
        if (InUlt && MonsterAlter != null) _alter?.Add(MonsterAlter);
    }

    public override void OnUnequip()
    {
        if (!Ctx.HasStateAuthority) return;
        Ctx.Stats?.RemoveBySource(Src(SrcAbsorb));
        Ctx.Stats?.RemoveBySource(Src(SrcSpecial));
        if (MonsterAlter != null) _alter?.Remove(MonsterAlter);
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
            OnUnequip();
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
            else TickMonster();
        }
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
        if (!other.TryGetComponent<PlayerStatus>(out var st)) return;

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
            _recent.Add(new Bitten { H = h, Time = Now });
            if (++n >= max) break;
        }

        if (dealt > 0f) _health?.RPC_Heal(Mathf.RoundToInt(dealt * Lv(DevourHealPct) * 0.01f));
        Broadcast(EvBite, o + fwd * 1.5f + Vector3.up * 1.2f);
    }

    /// <summary>물고 나서 잠시 안에 죽으면 탐식 처치 — 소울 · 쿨 초기화 · 뱉을 것 · 궁 연장</summary>
    void TickKillCredit()
    {
        for (int i = _recent.Count - 1; i >= 0; i--)
        {
            var b = _recent[i];
            if (b.H == null || Now - b.Time > KillCreditWindow) { _recent.RemoveAt(i); continue; }
            if (!b.H.IsDead) continue;

            _recent.RemoveAt(i);
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

    // ── 굶주린 괴수 ──

    void BeginUlt()
    {
        if (!InUlt && MonsterAlter != null) _alter?.Add(MonsterAlter);
        _s.UltEnd = Now + Lv(UltDuration);
        _monsterCd.Clear();
    }

    void EndUlt()
    {
        bool was = _s.UltEnd > 0f;
        _s.UltEnd = 0f;
        if (MonsterAlter != null) _alter?.Remove(MonsterAlter);
        _monsterCd.Clear();
        if (was) ReleaseCooldown(UltSkill);       // '효과 끝나고 쿨타임'이면 지금부터
    }

    void TickMonster()
    {
        float cd = Lv(MonsterTargetCd);
        foreach (var h in Around(Ctx.transform.position, Lv(MonsterRadius)))
        {
            if (_monsterCd.TryGetValue(h, out float until) && Now < until) continue;
            _monsterCd[h] = Now + cd;

            var info = MakeHit(Lv(MonsterCoef) * 0.01f, Lv(MonsterFlat), DamageSource.Ultimate, out _);
            info.ExecuteBelow = Lv(MonsterLowHp);                 // 맞는 쪽에서 정확한 체력으로 판정
            info.ExecuteBonus = Lv(MonsterLowHpBonus) * 0.01f;
            info.IsMelee = true;
            h.RPC_TakeDamage(info);

            _recent.Add(new Bitten { H = h, Time = Now });
            Broadcast(EvMonster, h.transform.position + Vector3.up * 1.2f);
        }
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
}