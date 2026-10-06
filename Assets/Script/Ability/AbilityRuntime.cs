using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class AbilityRuntime : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private AbilityDatabase database;
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private LayerMask hitMask;
    [SerializeField] private WeaponAnimator weaponAnim;

    [Header("Debug — 테스트용 강제 지정")]
    [SerializeField] private int forceMainId = 0;
    [SerializeField] private int forceSubId = 0;
    [SerializeField] private int forceRandomId = 0;
    [SerializeField] private bool logSkills = false;

    [Header("Dash")]
    [SerializeField] private string ghostLayerName = "PlayerGhost";
    [SerializeField] private float dashDecayTime = 0.15f;

    [Header("쿨감")]
    [Tooltip("쿨타임 감소 상한")]
    [Range(0f, 1f)][SerializeField] private float maxCooldownReduction = 0.9f;


    // ══════════════════ 네트워크 상태 ══════════════════

    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int MainAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int SubAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int RandomAbilityId { get; set; }

    [Networked] private int RandomAbilityKills { get; set; }

    [Networked, Capacity(5)] public NetworkArray<float> CooldownEnd { get; }
    [Networked, Capacity(5)] public NetworkArray<float> Charges { get; }
    [Networked, Capacity(5)] public NetworkArray<float> MaxCharges { get; }

    [Networked] public TickTimer StateTimer { get; set; }
    [Networked] public int ActiveStateId { get; set; }
    [Networked] public TickTimer SealTimer { get; set; }

    // ── 항목별 레벨 (메인·서브만, 랜덤 제외) ──
    public const int EntriesPerAbility = 8;
    public const int OwnerMain = 0;
    public const int OwnerSub = 1;

    /// <summary>[owner * 8 + local] = 레벨 - 1. local은 패시브 먼저, 이어서 스킬</summary>
    [Networked, Capacity(16), OnChangedRender(nameof(OnRanksChanged))]
    public NetworkArray<int> EntryRanks { get; }

    /// <summary>남은 강화 포인트</summary>
    [Networked] public int SkillPoints { get; set; }

    public const int ChargeSlotCount = 4;

    private static int _curseSeq;

    // ══════════════════ 내부 상태 ══════════════════

    private AbilityData.SkillDef _casting;
    private float _castTimer = -1f;
    private bool _castHitApplied;

    private Vector3 _dashDir;
    private float _dashSpeed;
    private float _dashTimer;
    private float _dashDecayTimer;
    private DashParams _dashParams;
    private float _moveTickAccum;

    private int _normalLayer;
    private int _ghostLayer;

    private PlayerStats _stats;
    private PlayerHealth _health;
    private PlayerSkillInput _input;
    private PlayerMovement _movement;
    private CharacterController _cc;
    private MotionEventPlayer _events;
    private PlayerEquipment _equipment;
    private PlayerAttack _attack;
    private PlayerStatus _status;
    public PlayerStatus Status => _status;

    private bool _weaponHitSkill;   // 지금 실행 중인 스킬이 무기 판정인가
    private float _skillDealt;      // 그 스킬로 준 피해 합 (탐식 흡혈용)

    private readonly List<AbilityData> _active = new();
    private readonly List<AbilityBehaviour> _behaviours = new();
    private readonly AbilityData.SkillDef[] _slots = new AbilityData.SkillDef[SkillSlotExt.Count];
    private readonly Dictionary<(int target, AbilityData.EffectDef effect), float> _perTargetCd = new();

    /// <summary>효과 → 소속 항목의 레벨 인덱스. 여기 없으면(랜덤 능력) 레벨 0</summary>
    private readonly Dictionary<AbilityData.EffectDef, int> _effectEntry = new();

    private static int _buffSeq;
    private static readonly HashSet<AbilityData.EffectDef> _warnedUnimplemented = new();

    /// <summary>실제로 적용된 쿨타임 (레벨·쿨감 반영). UI 비율 계산용</summary>
    [Networked, Capacity(5)] public NetworkArray<float> CooldownTotal { get; }

    /// <summary>스킬 → 소속 항목의 레벨 인덱스</summary>
    private readonly Dictionary<AbilityData.SkillDef, int> _skillEntry = new();

    // ══════════════════ 능력 고유 효과 관련 ══════════════════

    public LayerMask HitMask => hitMask;
    public enum MoveLockMode { None, StrafeOnly, ForwardOnly }
    public MoveLockMode MoveLock { get; set; } = MoveLockMode.None;

    // ══════════════════ 공개 접근자 ══════════════════

    public IReadOnlyList<AbilityData> Active => _active;
    public IReadOnlyList<AbilityBehaviour> Behaviours => _behaviours;

    public PlayerStats Stats => _stats;
    public PlayerHealth Health => _health;
    public PlayerEquipment Equipment => _equipment;
    public Transform CameraRootTransform => cameraRoot;

    public bool IsSealed => (_status != null && _status.IsSealed) || !SealTimer.ExpiredOrNotRunning(Runner);
    public bool IsStunned => _status != null && _status.IsStunned;
    public bool IsCasting => _castTimer >= 0f;
    public bool IsDashing => _dashTimer > 0f;
    public bool IsMoving => _movement != null && _movement.IsMoving;
    public bool DashIgnoresGravity => _dashTimer > 0f && _dashParams.IgnoreGravity;

    public AbilityData.SkillDef GetSlot(int i) =>
        (i >= 0 && i < _slots.Length) ? _slots[i] : null;

    public bool HasState(int id) =>
        ActiveStateId == id && !StateTimer.ExpiredOrNotRunning(Runner);

    public T FindAbility<T>() where T : AbilityBehaviour
    {
        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i] is T t) return t;
        return null;
    }

    // ══════════════════ 게이지 ══════════════════

    public float GetCharge(int slot) =>
        (slot >= 0 && slot < ChargeSlotCount) ? Charges[slot] : 0f;

    public float GetMaxCharge(int slot) =>
        (slot >= 0 && slot < ChargeSlotCount) ? MaxCharges[slot] : 0f;

    public float GetChargeRatio(int slot)
    {
        float max = GetMaxCharge(slot);
        return max <= 0f ? 0f : GetCharge(slot) / max;
    }

    public void SetMaxCharge(int slot, float v)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= ChargeSlotCount) return;

        MaxCharges.Set(slot, Mathf.Max(1f, v));
        Charges.Set(slot, Mathf.Min(Charges[slot], MaxCharges[slot]));
    }

    public void AddCharge(int slot, float amount)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= ChargeSlotCount) return;

        float max = MaxCharges[slot];
        if (max <= 0f) max = 100f;

        Charges.Set(slot, Mathf.Clamp(Charges[slot] + amount, 0f, max));
    }

    public bool SpendCharge(int slot, float amount)
    {
        if (!HasStateAuthority) return false;
        if (slot < 0 || slot >= ChargeSlotCount) return false;
        if (Charges[slot] < amount) return false;

        Charges.Set(slot, Charges[slot] - amount);
        return true;
    }

    // ══════════════════ 레벨 ══════════════════

    public static int EntryIndex(int owner, int local) => owner * EntriesPerAbility + local;

    public static int LocalEntryCount(AbilityData d) =>
        d == null ? 0 : (d.Passives?.Length ?? 0) + (d.Skills?.Length ?? 0);

    public AbilityData GetOwnerData(int owner) =>
        owner == OwnerMain ? GetMainAbility() :
        owner == OwnerSub ? GetSubAbility() : null;

    static bool ValidEntry(int owner, int local) =>
        (owner == OwnerMain || owner == OwnerSub) && local >= 0 && local < EntriesPerAbility;

    /// <summary>항목의 현재 레벨 - 1 (0 = Lv1)</summary>
    public int GetEntryRank(int owner, int local) =>
        ValidEntry(owner, local) ? EntryRanks[EntryIndex(owner, local)] : 0;

    /// <summary>Behaviour용 — 이 능력 데이터의 항목 레벨. 랜덤 능력이면 0</summary>
    public int GetRank(AbilityData data, int local)
    {
        if (data == null) return 0;
        if (data.Id == MainAbilityId) return GetEntryRank(OwnerMain, local);
        if (data.Id == SubAbilityId) return GetEntryRank(OwnerSub, local);
        return 0;
    }

    public int GetEntryMaxLevel(int owner, int local)
    {
        var d = GetOwnerData(owner);
        if (d == null || local < 0) return 1;

        int p = d.Passives?.Length ?? 0;
        if (local < p) return Mathf.Max(1, d.Passives[local]?.MaxLevel ?? 1);

        int s = local - p;
        if (d.Skills != null && s < d.Skills.Length) return Mathf.Max(1, d.Skills[s]?.MaxLevel ?? 1);

        return 1;
    }

    public bool CanUpgrade(int owner, int local)
    {
        if (SkillPoints <= 0) return false;
        if (!ValidEntry(owner, local)) return false;

        var d = GetOwnerData(owner);
        if (d == null || local >= LocalEntryCount(d)) return false;

        return GetEntryRank(owner, local) + 1 < GetEntryMaxLevel(owner, local);
    }

    /// <summary>UI에서 호출</summary>
    public void RequestUpgrade(int owner, int local)
    {
        if (!CanUpgrade(owner, local)) return;
        RPC_UpgradeEntry(owner, local);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_UpgradeEntry(int owner, int local)
    {
        if (!CanUpgrade(owner, local)) return;

        int i = EntryIndex(owner, local);
        float before = _stats.Get(StatType.MaxHP);
        EntryRanks.Set(i, EntryRanks[i] + 1);
        SkillPoints--;
        RefreshPassiveStats();
        _health.ApplyMaxHpGain(before);
    }

    public void AddSkillPoints(int n)
    {
        if (!HasStateAuthority) return;
        SkillPoints = Mathf.Max(0, SkillPoints + n);
    }

    int RankOf(AbilityData.EffectDef e) =>
        e != null && _effectEntry.TryGetValue(e, out int i) ? EntryRanks[i] : 0;

    /// <summary>스킬의 현재 레벨 - 1. 랜덤 능력 스킬은 0</summary>
    public int GetSkillRank(AbilityData.SkillDef s) =>
        s != null && _skillEntry.TryGetValue(s, out int i) ? EntryRanks[i] : 0;

    void OnRanksChanged() => RefreshPassiveStats();

    // ══════════════════ 초기화 ══════════════════

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _input = GetComponent<PlayerSkillInput>();
        _movement = GetComponent<PlayerMovement>();
        _cc = GetComponent<CharacterController>();
        _events = GetComponent<MotionEventPlayer>();
        _equipment = GetComponent<PlayerEquipment>();
        _attack = GetComponent<PlayerAttack>();
        _status = GetComponent<PlayerStatus>();

        if (weaponAnim == null) weaponAnim = GetComponentInChildren<WeaponAnimator>(true);

        _normalLayer = gameObject.layer;
        _ghostLayer = LayerMask.NameToLayer(ghostLayerName);
        if (_ghostLayer < 0)
        {
            Debug.LogWarning($"[Ability] '{ghostLayerName}' 레이어가 없습니다.");
            _ghostLayer = _normalLayer;
        }

        if (HasStateAuthority) AssignAbilities();

        RebuildAbilities();

        if (logSkills)
            Debug.Log($"[Ability] input={_input != null} behaviours={_behaviours.Count} " +
                      $"slots=[{_slots[0]?.Name ?? "-"}] [{_slots[1]?.Name ?? "-"}] " +
                      $"[{_slots[2]?.Name ?? "-"}] [{_slots[3]?.Name ?? "-"}] [{_slots[4]?.Name ?? "-"}]");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // 상태 접근 없이 인스턴스만 정리
        foreach (var b in _behaviours)
            if (b != null) Destroy(b);
        _behaviours.Clear();
    }

    /// <summary>능력 배정. 디버그 강제 ID가 있으면 그것, 없으면 추첨</summary>
    void AssignAbilities()
    {
        if (!HasStateAuthority || database == null) return;

        MainAbilityId = forceMainId != 0 ? forceMainId : (database.RollMain()?.Id ?? 0);
        SubAbilityId = forceSubId != 0 ? forceSubId : (database.RollSub()?.Id ?? 0);

        if (forceRandomId != 0)
        {
            RandomAbilityId = forceRandomId;
            RandomAbilityKills = 0;
        }
        else RerollRandomAbility();
    }

    /// <summary>능력 상태를 처음으로 — 쿨타임·게이지·상태·버프·레벨 전부</summary>
    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        StopAllCoroutines();            // 진행 중인 임시 버프 해제 타이머
        _perTargetCd.Clear();

        _casting = null;
        _castTimer = -1f;
        _castHitApplied = false;
        _dashTimer = 0f;
        _dashDecayTimer = 0f;
        _moveTickAccum = 0f;
        RPC_SetGhost(false);
        _input?.ClearAll();

        for (int i = 0; i < SkillSlotExt.Count; i++)
        {
            CooldownEnd.Set(i, 0f);
            CooldownTotal.Set(i, 0f);
        }
        for (int i = 0; i < ChargeSlotCount; i++)
        {
            Charges.Set(i, 0f);
            MaxCharges.Set(i, 0f);      // OnEquip에서 다시 설정됨
        }

        ActiveStateId = 0;
        StateTimer = default;
        SealTimer = default;

        SkillPoints = 0;
        for (int i = 0; i < EntryRanks.Length; i++) EntryRanks.Set(i, 0);

        AssignAbilities();
        RebuildAbilities();     // ID가 같아도 OnEquip을 다시 부르게 강제
    }

    void OnAbilityChanged() => RebuildAbilities();

    void RebuildAbilities()
    {
        if (_stats == null || database == null) return;

        foreach (var b in _behaviours)
        {
            if (b == null) continue;
            b.OnUnequip();
            Destroy(b);
        }
        _behaviours.Clear();
        _skillEntry.Clear();

        _active.Clear();
        _effectEntry.Clear();
        for (int i = 0; i < _slots.Length; i++) _slots[i] = null;

        var main = database.Get(MainAbilityId);
        var sub = database.Get(SubAbilityId);
        var rand = database.Get(RandomAbilityId);

        // 레벨 연결 (랜덤은 등록 안 함 → 항상 레벨 0)
        RegisterEntries(main, OwnerMain);
        RegisterEntries(sub, OwnerSub);

        AddAbility(main);
        AddAbility(sub);
        AddAbility(rand);

        RefreshPassiveStats();
    }

    void RegisterEntries(AbilityData a, int owner)
    {
        if (a == null) return;

        int local = 0;
        if (a.Passives != null)
            foreach (var p in a.Passives) MapEffects(p?.Effects, owner, local++);
        if (a.Skills != null)
            foreach (var s in a.Skills)
            {
                if (s != null && local < EntriesPerAbility)
                    _skillEntry[s] = EntryIndex(owner, local);
                MapEffects(s?.Effects, owner, local++);
            }

        if (local > EntriesPerAbility)
            Debug.LogWarning($"[Ability] {a.name}: 패시브+스킬이 {local}개 — " +
                             $"레벨은 앞 {EntriesPerAbility}개까지만 저장됩니다", a);
    }

    void MapEffects(AbilityData.EffectDef[] effects, int owner, int local)
    {
        if (effects == null || local >= EntriesPerAbility) return;

        int idx = EntryIndex(owner, local);
        foreach (var e in effects)
            if (e != null) _effectEntry[e] = idx;
    }

    void AddAbility(AbilityData a)
    {
        if (a == null) return;
        _active.Add(a);

        if (a.Skills != null)
            foreach (var s in a.Skills)
            {
                int idx = s.Slot.Index();
                if (idx < 0 || idx >= _slots.Length) continue;

                if (_slots[idx] == null) _slots[idx] = s;
                else Debug.LogWarning($"[Ability] 슬롯 {s.Slot} 중복: {a.DisplayName} / {s.Name}");
            }

        if (a.Behaviour != null)
        {
            var b = a.Behaviour.CreateInstance(this, a);
            _behaviours.Add(b);
            b.OnEquip();
        }
    }

    /// <summary>패시브 StatMod를 현재 레벨 값으로 다시 건다</summary>
    void RefreshPassiveStats()
    {
        if (_stats == null || database == null) return;

        _stats.RemoveBySource(SourceId.MainAbility);
        _stats.RemoveBySource(SourceId.SubAbility);
        _stats.RemoveBySource(SourceId.RandomAbility);

        ApplyPassiveStats(database.Get(MainAbilityId), SourceId.MainAbility);
        ApplyPassiveStats(database.Get(SubAbilityId), SourceId.SubAbility);
        ApplyPassiveStats(database.Get(RandomAbilityId), SourceId.RandomAbility);
    }

    void ApplyPassiveStats(AbilityData a, int sourceId)
    {
        if (a?.Passives == null) return;

        foreach (var p in a.Passives)
        {
            if (p?.Effects == null) continue;
            foreach (var e in p.Effects)
                if (e.Trigger == TriggerType.Passive && e.Effect == EffectType.StatMod)
                    _stats.AddModifier(new StatModifier(
                        e.Stat, e.ModType, e.GetValue(RankOf(e)), sourceId));
        }
    }

    // ══════════════════ 메인 루프 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (_health.IsDead)
        {
            _castTimer = -1f;
            _casting = null;
            if (_dashTimer > 0f) { _dashTimer = 0f; RPC_SetGhost(false); }
            _dashDecayTimer = 0f;       // 부활 직후 미끄러짐 방지
            _input?.ClearAll();
            return;
        }

        float dt = Runner.DeltaTime;

        // 대시
        if (_dashTimer > 0f)
        {
            _dashTimer -= dt;
            if (_cc != null)
            {
                var flags = _cc.Move(_dashDir * _dashSpeed * dt);
                if (_dashParams.StopOnContact && (flags & CollisionFlags.Sides) != 0)
                    _dashTimer = 0f;
            }

            if (_dashTimer <= 0f)
            {
                if (_dashParams.PassThroughPlayers) RPC_SetGhost(false);
                _dashDecayTimer = dashDecayTime;
            }
        }
        else if (_dashDecayTimer > 0f)
        {
            _dashDecayTimer -= dt;
            float t = Mathf.Clamp01(_dashDecayTimer / dashDecayTime);
            if (_cc != null) _cc.Move(_dashDir * _dashSpeed * t * dt);
        }

        // 상태 만료
        if (ActiveStateId != 0 && StateTimer.Expired(Runner))
        {
            int old = ActiveStateId;
            ActiveStateId = 0;
            RPC_OnStateChanged(old, false);
        }

        // 전용 로직
        for (int i = 0; i < _behaviours.Count; i++)
            _behaviours[i].Tick(dt);

        // 이동 트리거
        if (IsMoving)
        {
            _moveTickAccum += dt;
            if (_moveTickAccum >= 1f)
            {
                _moveTickAccum -= 1f;
                FireTrigger(TriggerType.OnMove);
            }
        }
        else _moveTickAccum = 0f;

        if (IsStunned)
        {
            _castTimer = -1f;
            _casting = null;
            _input?.ClearAll();
            return;
        }

        // 시전 중
        if (_castTimer >= 0f)
        {
            TickCast(dt);
            return;
        }

        // 스킬 입력
        for (int i = 0; i < SkillSlotExt.Count; i++)
            if (_input != null && _input.Consume(i))
                TryUseSkill(i);
    }

    void TickCast(float dt)
    {
        _castTimer += dt;

        var m = _casting.Motion;
        if (m == null) { _castTimer = -1f; _casting = null; return; }

        if (_casting.EffectTiming == AbilityData.SkillEffectTiming.OnHitFrame && !_castHitApplied)
        {
            float activeStart = m.WindUp;
            float activeEnd = m.WindUp + m.ActiveWindow;

            bool inWindow = _castTimer >= activeStart && _castTimer <= activeEnd;
            bool missed = _castTimer > activeEnd && _castTimer < m.TotalTime;

            if (inWindow || missed)
            {
                _castHitApplied = true;
                FireSkillEffects(_casting);
            }
        }

        if (_castTimer >= m.TotalTime)
        {
            if (_casting.EffectTiming == AbilityData.SkillEffectTiming.OnCastEnd && !_castHitApplied)
            {
                _castHitApplied = true;
                FireSkillEffects(_casting);
            }

            _castTimer = -1f;
            _casting = null;
        }
    }

    // ══════════════════ 스킬 ══════════════════

    public bool TryUseSkill(int slotIndex)
    {
        if (!HasStateAuthority) return false;
        if (IsSealed) return false;
        if (IsStunned) return false;
        if (IsCasting) return false;

        // 기본 공격 중이면 스킬 못 씀
        if (_attack != null && _attack.IsAttacking) return false;

        var skill = _slots[slotIndex];
        if (skill == null) return false;

        if (!MeetsWeaponRequirement(skill)) return false;

        if (Runner.SimulationTime < CooldownEnd[slotIndex])
        { if (logSkills) Debug.Log($"[Skill] {skill.Name} → 쿨타임"); return false; }

        if (_health.Soul < skill.SoulCost)
        { if (logSkills) Debug.Log($"[Skill] {skill.Name} → 소울 부족"); return false; }

        bool handled = false;
        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i].OnSkillUsed(slotIndex, skill)) handled = true;

        float cdr = Mathf.Clamp(_stats.Get(StatType.CooldownReduction), 0f, maxCooldownReduction);
        float cd = skill.GetCooldown(GetSkillRank(skill)) * (1f - cdr);
        CooldownEnd.Set(slotIndex, Runner.SimulationTime + cd);
        CooldownTotal.Set(slotIndex, cd);

        if (skill.SoulCost > 0) _health.SpendSoul(skill.SoulCost);

        if (logSkills)
            Debug.Log($"[Skill] {skill.Name} 사용 (handled={handled}, timing={skill.EffectTiming}, " +
                      $"effects={skill.Effects?.Length ?? 0})");

        var motion = skill.Motion;

        if (motion != null && motion.Duration > 0.01f)
        {
            _casting = skill;
            _castTimer = 0f;
            _castHitApplied = handled;
            RPC_PlaySkillMotion(slotIndex);

            if (skill.EffectTiming == AbilityData.SkillEffectTiming.Immediate && !handled)
            {
                _castHitApplied = true;
                FireSkillEffects(skill);
            }
        }
        else if (!handled)
        {
            FireSkillEffects(skill);
        }

        // 스킬 사용 트리거 (다른 능력의 OnSkillUse 반응용)
        FireTrigger(TriggerType.OnSkillUse, null, skill);
        return true;
    }

    bool MeetsWeaponRequirement(AbilityData.SkillDef skill)
    {
        if (skill.RequiredWeapon == WeaponRequirement.None) return true;

        var w = Equipment?.GetWeapon();
        if (w == null) return false;

        return (skill.RequiredWeapon & w.ToRequirementFlag()) != 0;
    }

    public bool CanUseWeapon(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
        var s = _slots[slotIndex];
        return s == null || MeetsWeaponRequirement(s);
    }

    void FireSkillEffects(AbilityData.SkillDef skill)
    {
        if (skill.Effects == null || skill.Effects.Length == 0) return;

        GameObject target = null;
        if (skill.NeedsTarget)
        {
            target = FindTarget(skill);
            if (target == null) return;
        }

        _weaponHitSkill = skill.AppliesWeaponEnchants;
        _skillDealt = 0f;

        var src = skill.Slot == SkillSlot.Ultimate ? DamageSource.Ultimate : DamageSource.Skill;
        foreach (var e in skill.Effects)
            Execute(e, target, src);

        if (_weaponHitSkill && target != null && target.TryGetComponent<PlayerHealth>(out var th))
        {
            int heal = 0;
            GetComponent<PlayerEnchant>()?.OnWeaponHit(th, _skillDealt, ref heal);
            if (heal > 0) _health.RPC_Heal(heal);
        }

        _weaponHitSkill = false;
    }

    public GameObject FindTarget(AbilityData.SkillDef skill)
    {
        if (cameraRoot == null) return null;

        Vector3 origin = transform.position;
        Vector3 flatFwd = new Vector3(cameraRoot.forward.x, 0f, cameraRoot.forward.z).normalized;
        if (flatFwd.sqrMagnitude < 0.001f) flatFwd = transform.forward;

        Collider[] cols = Physics.OverlapSphere(origin, skill.Range, hitMask);
        if (cols.Length == 0) return null;

        System.Array.Sort(cols, (a, b) =>
            (a.transform.position - origin).sqrMagnitude
            .CompareTo((b.transform.position - origin).sqrMagnitude));

        float half = skill.HitAngle * 0.5f;

        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;
            if (!col.TryGetComponent<PlayerHealth>(out var h)) continue;
            if (h.IsDead) continue;

            Vector3 to = col.transform.position - origin;
            if (Mathf.Abs(to.y) > skill.HitHeight) continue;

            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.magnitude > skill.Range) continue;

            if (skill.HitMode == WeaponHitMode.ForwardArc && flat.magnitude > 0.01f)
                if (Vector3.Angle(flatFwd, flat.normalized) > half) continue;

            return col.gameObject;
        }

        return null;
    }

    // ══════════════════ 트리거 ══════════════════

    public void FireTrigger(TriggerType trigger, GameObject other = null,
                            AbilityData.SkillDef exclude = null)
    {
        if (!HasStateAuthority) return;
        if (IsSealed && trigger != TriggerType.OnHitTaken) return;

        // 패시브만 트리거에 반응
        foreach (var ability in _active)
        {
            if (ability.Passives == null) continue;

            foreach (var p in ability.Passives)
            {
                if (p.Effects == null) continue;
                foreach (var e in p.Effects)
                    if (e.Trigger == trigger) Execute(e, other);
            }
        }

        for (int i = 0; i < _behaviours.Count; i++)
            _behaviours[i].OnTrigger(trigger, other);
    }

    // ══════════════════ 상태 / 대시 ══════════════════

    public void EnterState(int stateId, float duration)
    {
        if (!HasStateAuthority) return;
        ActiveStateId = stateId;
        StateTimer = TickTimer.CreateFromSeconds(Runner, duration);
        RPC_OnStateChanged(stateId, true);
    }

    public void ExitState()
    {
        if (!HasStateAuthority || ActiveStateId == 0) return;
        int old = ActiveStateId;
        ActiveStateId = 0;
        StateTimer = default;
        RPC_OnStateChanged(old, false);
    }

    public void DoDash(DashParams p)
    {
        if (!HasStateAuthority) return;
        if (p.Distance <= 0f) return;

        _dashParams = p;
        _dashDir = ResolveDashDirection(p);
        _dashSpeed = p.Distance / Mathf.Max(0.05f, p.Duration);
        _dashTimer = Mathf.Max(0.05f, p.Duration);

        if (p.PassThroughPlayers) RPC_SetGhost(true);
    }

    Vector3 ResolveDashDirection(DashParams p)
    {
        switch (p.Direction)
        {
            case DashDirection.CameraForward:
                {
                    if (cameraRoot == null) return transform.forward;

                    Vector3 dir = cameraRoot.forward;

                    if (p.MaxPitch > 0f)
                    {
                        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                        if (flat.sqrMagnitude < 0.0001f) return transform.forward;
                        flat.Normalize();

                        float pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
                        pitch = Mathf.Clamp(pitch, -p.MaxPitch, p.MaxPitch);

                        float rad = pitch * Mathf.Deg2Rad;
                        dir = flat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
                    }

                    return dir.normalized;
                }

            case DashDirection.MoveInput:
                {
                    if (_movement == null) return transform.forward;
                    Vector3 local = _movement.LocalMoveDir;
                    if (local.sqrMagnitude < 0.01f) return transform.forward;
                    return transform.TransformDirection(local).normalized;
                }

            case DashDirection.Backward:
                return -transform.forward;

            default:
                {
                    Vector3 f = transform.forward;
                    f.y = 0f;
                    return f.sqrMagnitude > 0.0001f ? f.normalized : transform.forward;
                }
        }
    }

    // ══════════════════ 쿨타임 ══════════════════

    public float GetCooldownRemain(int slot)
    {
        if (slot < 0 || slot >= SkillSlotExt.Count) return 0f;
        return Mathf.Max(0f, CooldownEnd[slot] - Runner.SimulationTime);
    }

    public float GetCooldownRatio(int slot)
    {
        if (slot < 0 || slot >= SkillSlotExt.Count) return 0f;
        float total = CooldownTotal[slot];
        if (total <= 0f) return 0f;
        return Mathf.Clamp01(GetCooldownRemain(slot) / total);
    }

    // ══════════════════ 랜덤 능력 ══════════════════

    public void RerollRandomAbility()
    {
        if (!HasStateAuthority || database == null) return;

        var inUse = new List<int>();
        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null) continue;
            if (obj == Object) continue;   // 자기 자신 제외
            if (obj.TryGetComponent<AbilityRuntime>(out var ar))
                inUse.Add(ar.RandomAbilityId);
        }

        var next = database.RollRandom(RandomAbilityId, inUse);
        if (next != null) RandomAbilityId = next.Id;
        RandomAbilityKills = 0;
    }

    public AbilityData GetRandomAbility() => database?.Get(RandomAbilityId);
    public AbilityData GetMainAbility() => database?.Get(MainAbilityId);
    public AbilityData GetSubAbility() => database?.Get(SubAbilityId);

    // ══════════════════ 공격 대체 ══════════════════

    public bool TryOverrideAttack(out AttackOverride ovr, out int source)
    {
        ovr = AttackOverride.None;
        source = -1;
        if (!HasStateAuthority) return false;

        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i].TryOverrideAttack(out ovr))
            {
                source = i;
                return true;
            }

        return false;
    }

    public ComboStep GetOverrideStep(int source) =>
        source >= 0 && source < _behaviours.Count ? _behaviours[source].GetOverrideStep() : null;

    // ══════════════════ RPC ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplySeal(float seconds)
    {
        _status?.Apply(StatusType.Seal, 1f, seconds, PlayerRef.None);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_SetGhost(bool on)
    {
        gameObject.layer = on ? _ghostLayer : _normalLayer;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlaySkillMotion(int slotIndex)
    {
        var skill = GetSlot(slotIndex);
        var step = skill?.Motion;
        if (step == null) return;

        weaponAnim?.Play(step);
        _events?.Play(step, HasStateAuthority);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_OnStateChanged(int stateId, bool entered)
    {
        for (int i = 0; i < _behaviours.Count; i++)
        {
            if (entered) _behaviours[i].OnStateEnter(stateId);
            else _behaviours[i].OnStateExit(stateId);
        }
    }

    // ══════════════════ 효과 실행 ══════════════════

    void Execute(AbilityData.EffectDef e, GameObject other, DamageSource src = DamageSource.Skill)
    {
        // ① 발동 조건
        if (e.Chance < 1f && Random.value > e.Chance) return;
        if (e.RequireStateId != 0 && !HasState(e.RequireStateId)) return;

        // ② 대상
        GameObject target = ResolveTarget(e.Target, other);
        if (target == null)
        {
            if ((e.Target == TargetType.AreaAroundSelf || e.Target == TargetType.MarkedTarget)
                && _warnedUnimplemented.Add(e))
                Debug.LogWarning($"[Exec] Target={e.Target}는 아직 미구현 — {e.Effect} 효과가 무시됩니다");
            else if (logSkills)
                Debug.Log($"[Exec] {e.Effect} → 대상 없음 (Target={e.Target})");
            return;
        }

        // ③ 대상별 재발동 쿨타임 — 여기선 확인만
        var cdKey = (target.GetInstanceID(), e);
        if (e.PerTargetCooldown > 0f &&
            _perTargetCd.TryGetValue(cdKey, out float until) &&
            Runner.SimulationTime < until) return;

        // ④ 비용 — 모든 조건을 통과한 뒤 마지막에, 소모 슬롯에서
        if (e.ChargeCost > 0f && !SpendCharge(e.CostChargeSlot, e.ChargeCost))
        {
            if (logSkills) Debug.Log($"[Exec] {e.Effect} → 게이지 부족 (슬롯 {e.CostChargeSlot})");
            return;
        }

        // 발동 확정 → 재발동 쿨타임 기록
        if (e.PerTargetCooldown > 0f)
            _perTargetCd[cdKey] = Runner.SimulationTime + e.PerTargetCooldown;

        // 이 효과가 속한 패시브/스킬의 레벨로 수치 결정
        float v = e.GetValue(RankOf(e));

        switch (e.Effect)
        {
            case EffectType.StatMod:
                ApplyTempStat(target, e, v);
                break;

            case EffectType.Damage:
                {
                    if (!target.TryGetComponent<PlayerHealth>(out var h)) break;

                    float outgoing = DamageCalc.CalcOutgoing(_stats, v, src, true, out bool crit);
                    var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(outgoing), Object.InputAuthority, transform.position);
                    info.IsCrit = crit;

                    if (_weaponHitSkill)
                    {
                        GetComponent<PlayerEnchant>()?.ModifyWeaponHit(ref info);
                        _skillDealt += outgoing;
                    }

                    h.RPC_TakeDamage(info);
                    break;
                }

            case EffectType.Heal:
                if (target.TryGetComponent<PlayerHealth>(out var hh))
                    hh.RPC_Heal(Mathf.RoundToInt(v));
                break;


            case EffectType.Seal:
                if (target.TryGetComponent<AbilityRuntime>(out var ar))
                    ar.RPC_ApplySeal(v);
                break;

            case EffectType.GrantSoul:
                if (target.TryGetComponent<PlayerHealth>(out var sh))
                    sh.RPC_GainSoul(Mathf.RoundToInt(v));
                break;

            case EffectType.AddCharge:
                if (target == gameObject)
                    AddCharge(e.ChargeSlot, v);
                else if (target.TryGetComponent<AbilityRuntime>(out var ac))
                    ac.RPC_AddCharge(e.ChargeSlot, v);
                break;

            case EffectType.EnterState:
                // 값이 '상태 ID'라서 레벨과 무관하게 첫 값 사용
                EnterState(Mathf.RoundToInt(e.GetValue(0)), e.Duration);
                break;

            case EffectType.Dash:
                DoDash(new DashParams
                {
                    Distance = v,
                    Duration = e.Duration > 0f ? e.Duration : 0.2f,
                    Direction = DashDirection.Horizontal,
                    MaxPitch = 45f,
                    PassThroughPlayers = false,
                    StopOnContact = true,
                });
                break;

            case EffectType.Slow:
                if (target.TryGetComponent<PlayerStatus>(out var slowTarget))
                    slowTarget.Apply(StatusType.Slow, v, e.Duration, Object.InputAuthority);
                break;

            case EffectType.ApplyStatus:
                if (target.TryGetComponent<PlayerStatus>(out var st))
                    st.Apply(e.Status, v, e.Duration, Object.InputAuthority);
                break;

            default:
                if (_warnedUnimplemented.Add(e))
                    Debug.LogWarning($"[Ability] 미구현 효과: {e.Effect}");
                break;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddCharge(int slot, float amount) => AddCharge(slot, amount);

    GameObject ResolveTarget(TargetType t, GameObject other) => t switch
    {
        TargetType.Self => gameObject,
        TargetType.Attacker or TargetType.Victim or
        TargetType.Killer or TargetType.AimTarget => other,
        _ => null,
    };

    public void BroadcastBehaviourEvent(AbilityBehaviour b, int key, Vector3 pos)
    {
        if (!HasStateAuthority) return;

        int index = _behaviours.IndexOf(b);
        if (index < 0) return;

        RPC_BehaviourEvent(index, b.AbilityId, key, pos);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_BehaviourEvent(int index, int abilityId, int key, Vector3 pos)
    {
        if (index < 0 || index >= _behaviours.Count) return;

        var b = _behaviours[index];
        if (b.AbilityId != abilityId) return;      // 그 사이 능력이 바뀌었으면 무시

        b.OnRemoteEvent(key, pos);
    }

    void ApplyTempStat(GameObject target, AbilityData.EffectDef e, float v)
    {
        if (target == gameObject)
        {
            AddTimedStat(e.Stat, e.ModType, v, e.Duration, SourceId.BuffBase, ref _buffSeq);
            return;
        }

        // 남에게 — 상대 쪽에서 적용하고, 해제 타이머도 상대 쪽에서 돔
        if (target.TryGetComponent<AbilityRuntime>(out var other))
            other.RPC_ApplyTempStat((int)e.Stat, (int)e.ModType, v, e.Duration);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplyTempStat(int stat, int modType, float value, float duration)
    {
        AddTimedStat((StatType)stat, (ModType)modType, value, duration, SourceId.CurseBase, ref _curseSeq);
    }

    /// <summary>내 스탯에 지속시간 있는 수정자. 0이면 영구(baseId 그 자체)</summary>
    void AddTimedStat(StatType stat, ModType mod, float v, float duration, int baseId, ref int seq)
    {
        if (_stats == null) return;

        if (duration <= 0f)
        {
            _stats.AddModifier(new StatModifier(stat, mod, v, baseId));
            return;
        }

        int src = baseId + 1 + (seq++ % 99998);
        _stats.AddModifier(new StatModifier(stat, mod, v, src));
        StartCoroutine(RemoveAfter(_stats, src, duration));
    }

    System.Collections.IEnumerator RemoveAfter(PlayerStats st, int src, float t)
    {
        yield return new WaitForSeconds(t);
        if (st != null) st.RemoveBySource(src);
    }

    // ══════════════════ 진화 ══════════════════

    public void OnPlayerKill()
    {
        if (!HasStateAuthority) return;

        RandomAbilityKills++;

        var cur = database?.Get(RandomAbilityId);
        if (cur == null || cur.EvolveToId == 0) return;
        if (cur.EvolveKillCount <= 0) return;

        if (RandomAbilityKills >= cur.EvolveKillCount)
        {
            RandomAbilityId = cur.EvolveToId;
            RandomAbilityKills = 0;
            RPC_OnEvolved(cur.EvolveToId);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_OnEvolved(int newId)
    {
        Debug.Log($"[Ability] 진화! → {database?.Get(newId)?.DisplayName}");
        // 나중에 화면 연출
    }

    public void PlayEffectLocal(GameObject prefab, Vector3 pos)
    {
        if (prefab == null) return;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        Destroy(go, 3f);
    }
}