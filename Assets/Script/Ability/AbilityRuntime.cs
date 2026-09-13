using System.Collections.Generic;
using Fusion;
using UnityEngine;
using static AbilityData;

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

    [Networked] private int RandomAbilityKills { get; set; }

    // ══════════════════ 네트워크 상태 ══════════════════

    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int MainAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int SubAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int RandomAbilityId { get; set; }

    [Networked, Capacity(5)] public NetworkArray<float> CooldownEnd { get; }
    [Networked, Capacity(5)] public NetworkArray<float> Charges { get; }
    [Networked, Capacity(5)] public NetworkArray<float> MaxCharges { get; }

    [Networked] public TickTimer StateTimer { get; set; }
    [Networked] public int ActiveStateId { get; set; }
    [Networked] public TickTimer SealTimer { get; set; }

    public const int ChargeSlotCount = 4;



    // ══════════════════ 내부 상태 ══════════════════

    private AbilityData.SkillDef _casting;
    private float _castTimer = -1f;
    private bool _castHitApplied;

    private Vector3 _dashDir;
    private float _dashSpeed;
    private float _dashTimer;
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

    private readonly List<AbilityData> _active = new();
    private readonly List<AbilityBehaviour> _behaviours = new();
    private readonly AbilityData.SkillDef[] _slots = new AbilityData.SkillDef[SkillSlotExt.Count];
    private readonly Dictionary<int, float> _perTargetCd = new();

    [SerializeField] private float dashDecayTime = 0.15f;
    private float _dashDecayTimer;

    private bool _castingIsUltimate;

    private PlayerEquipment _equipment;
    public PlayerEquipment Equipment => _equipment;

    private PlayerAttack _attack;
    private static int _buffSeq;



    // ══════════════════ 능력 고유 효과 관련 ══════════════════

    #region 능력 고유 효과
    public LayerMask HitMask => hitMask;
    public enum MoveLockMode { None, StrafeOnly, ForwardOnly }
    public MoveLockMode MoveLock { get; set; } = MoveLockMode.None;

    #endregion

    // ══════════════════ 공개 접근자 ══════════════════

    public IReadOnlyList<AbilityData> Active => _active;
    public IReadOnlyList<AbilityBehaviour> Behaviours => _behaviours;

    public PlayerStats Stats => _stats;
    public PlayerHealth Health => _health;
    public Transform CameraRootTransform => cameraRoot;

    public bool IsSealed => !SealTimer.ExpiredOrNotRunning(Runner);
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

        if (weaponAnim == null) weaponAnim = GetComponentInChildren<WeaponAnimator>(true);

        _normalLayer = gameObject.layer;
        _ghostLayer = LayerMask.NameToLayer(ghostLayerName);
        if (_ghostLayer < 0)
        {
            Debug.LogWarning($"[Ability] '{ghostLayerName}' 레이어가 없습니다.");
            _ghostLayer = _normalLayer;
        }

        if (HasStateAuthority)
        {
            if (forceMainId != 0) MainAbilityId = forceMainId;
            if (forceSubId != 0) SubAbilityId = forceSubId;
            if (forceRandomId != 0) RandomAbilityId = forceRandomId;

            if (RandomAbilityId == 0) RerollRandomAbility();
        }

        RebuildAbilities();

        Debug.Log($"[Ability] input={_input != null} behaviours={_behaviours.Count} " +
          $"slots=[{_slots[0]?.Name ?? "-"}] [{_slots[1]?.Name ?? "-"}] " +
          $"[{_slots[2]?.Name ?? "-"}] [{_slots[3]?.Name ?? "-"}]");
    }

    void OnAbilityChanged() => RebuildAbilities();

    void RebuildAbilities()
    {
        if (_stats == null || database == null) return;

        foreach (var b in _behaviours) b.OnUnequip();
        _behaviours.Clear();

        _stats.RemoveBySource(SourceId.MainAbility);
        _stats.RemoveBySource(SourceId.SubAbility);
        _stats.RemoveBySource(SourceId.RandomAbility);

        _active.Clear();
        for (int i = 0; i < _slots.Length; i++) _slots[i] = null;

        AddAbility(database.Get(MainAbilityId), SourceId.MainAbility);
        AddAbility(database.Get(SubAbilityId), SourceId.SubAbility);
        AddAbility(database.Get(RandomAbilityId), SourceId.RandomAbility);
    }

    void AddAbility(AbilityData a, int sourceId)
    {
        if (a == null) return;
        _active.Add(a);

        if (a.Passives != null)
            foreach (var p in a.Passives)
            {
                if (p.Effects == null) continue;
                foreach (var e in p.Effects)
                    if (e.Trigger == TriggerType.Passive && e.Effect == EffectType.StatMod)
                        _stats.AddModifier(new StatModifier(
                            e.Stat, e.ModType, e.GetValue(), sourceId));
            }

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

    // ══════════════════ 메인 루프 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (_health.IsDead)
        {
            _castTimer = -1f;
            _casting = null;
            if (_dashTimer > 0f) { _dashTimer = 0f; RPC_SetGhost(false); }
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
                _dashDecayTimer = dashDecayTime;   // ★ 감속 시작
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

        if (_casting.EffectTiming == SkillEffectTiming.OnHitFrame && !_castHitApplied)
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
            if (_casting.EffectTiming == SkillEffectTiming.OnCastEnd && !_castHitApplied)
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
        Debug.Log($"[Skill] TryUse slot={slotIndex}");

        if (!HasStateAuthority) return false;
        if (IsSealed) return false;
        if (IsCasting) return false;

        // 기본 공격 중이면 스킬 못 씀
        if (_attack != null && _attack.IsAttacking) return false;

        var skill = GetSlot(slotIndex);
        if (skill == null) { Debug.Log("  → 슬롯 비어있음"); return false; }

        if (Runner.SimulationTime < CooldownEnd[slotIndex]) { Debug.Log("  → 쿨타임"); return false; }
        if (_health.Soul < skill.SoulCost) { Debug.Log("  → 소울 부족"); return false; }

        _castingIsUltimate = (slotIndex == (int)SkillSlot.Ultimate);

        bool handled = false;
        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i].OnSkillUsed(slotIndex, skill)) handled = true;

        Debug.Log($"  → {skill.Name} handled={handled} " +
                  $"motion={(skill.Motion != null ? skill.Motion.Duration.ToString("F2") : "null")} " +
                  $"timing={skill.EffectTiming} effects={skill.Effects?.Length ?? 0}");

        float cdr = _stats.Get(StatType.CooldownReduction);
        float cd = skill.Cooldown * (1f - Mathf.Clamp01(cdr));
        CooldownEnd.Set(slotIndex, Runner.SimulationTime + cd);

        if (skill.SoulCost > 0) _health.SpendSoul(skill.SoulCost);

        if (logSkills)
            Debug.Log($"[Skill] {skill.Name} 사용 (handled={handled}, timing={skill.EffectTiming})");

        var motion = skill.Motion;

        if (motion != null && motion.Duration > 0.01f)
        {
            _casting = skill;
            _castTimer = 0f;
            _castHitApplied = handled;
            RPC_PlaySkillMotion(slotIndex);

            if (skill.EffectTiming == SkillEffectTiming.Immediate && !handled)
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

    void FireSkillEffects(AbilityData.SkillDef skill)
    {
        if (skill.Effects == null || skill.Effects.Length == 0) return;

        GameObject target = null;
        if (skill.NeedsTarget)
        {
            target = FindTarget(skill);
            if (target == null) return;
        }

        foreach (var e in skill.Effects)
            Execute(e, target);

        _castingIsUltimate = false;
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
        var s = GetSlot(slot);
        if (s == null || s.Cooldown <= 0f) return 0f;
        return Mathf.Clamp01(GetCooldownRemain(slot) / s.Cooldown);
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

    public bool TryOverrideAttack(out AttackOverride ovr)
    {
        ovr = AttackOverride.None;
        if (!HasStateAuthority) return false;

        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i].TryOverrideAttack(out ovr))
                return true;

        return false;
    }

    // ══════════════════ RPC ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplySeal(float seconds)
    {
        SealTimer = TickTimer.CreateFromSeconds(Runner, seconds);
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

    void Execute(AbilityData.EffectDef e, GameObject other)
    {
        if (e.Chance < 1f && Random.value > e.Chance)
        { Debug.Log($"[Exec] {e.Effect} → 확률 실패 (Chance={e.Chance})"); return; }

        if (e.RequireStateId != 0 && !HasState(e.RequireStateId))
        { Debug.Log($"[Exec] {e.Effect} → 상태 조건 미충족"); return; }

        if (e.ChargeCost > 0f && !SpendCharge(e.ChargeSlot, e.ChargeCost))
        { Debug.Log($"[Exec] {e.Effect} → 게이지 부족"); return; }

        GameObject target = ResolveTarget(e.Target, other);
        if (target == null && e.Target != TargetType.AreaAroundSelf)
        { Debug.Log($"[Exec] {e.Effect} → 대상 null (Target={e.Target})"); return; }


        if (e.PerTargetCooldown > 0f && target != null)
        {
            int key = target.GetInstanceID();
            if (_perTargetCd.TryGetValue(key, out float until) &&
                Runner.SimulationTime < until) return;
            _perTargetCd[key] = Runner.SimulationTime + e.PerTargetCooldown;
        }

        switch (e.Effect)
        {
            case EffectType.StatMod:
                ApplyTempStat(target, e);
                break;

            case EffectType.Damage:
                {
                    if (!target.TryGetComponent<PlayerHealth>(out var h)) break;

                    var src = _castingIsUltimate ? DamageSource.Ultimate : DamageSource.Skill;
                    float outgoing = DamageCalc.CalcOutgoing(
                        _stats, e.GetValue(), src, true, out _);

                    h.RPC_TakeDamage(DamageInfo.FromPlayerHit(
                        Mathf.RoundToInt(outgoing), Object.InputAuthority, transform.position));
                    break;
                }

            case EffectType.Heal:
                if (target.TryGetComponent<PlayerHealth>(out var hh))
                    hh.RPC_Heal(Mathf.RoundToInt(e.GetValue()));
                break;

            case EffectType.Seal:
                if (target.TryGetComponent<AbilityRuntime>(out var ar))
                    ar.RPC_ApplySeal(e.GetValue());
                break;

            case EffectType.GrantSoul:
                if (target.TryGetComponent<PlayerHealth>(out var sh))
                    sh.RPC_GainSoul(Mathf.RoundToInt(e.GetValue()));
                break;

            case EffectType.AddCharge:
                if (target.TryGetComponent<AbilityRuntime>(out var ac))
                    ac.AddCharge(e.ChargeSlot, e.GetValue());
                break;

            case EffectType.EnterState:
                EnterState(Mathf.RoundToInt(e.GetValue()), e.Duration);
                break;

            case EffectType.Dash:
                DoDash(new DashParams
                {
                    Distance = e.GetValue(),
                    Duration = e.Duration > 0f ? e.Duration : 0.2f,
                    Direction = DashDirection.Horizontal,
                    MaxPitch = 45f,
                    PassThroughPlayers = false,
                    StopOnContact = true,
                });
                break;

            default:
                Debug.Log($"[Ability] 미구현 효과: {e.Effect}");
                break;
        }
    }

    GameObject ResolveTarget(TargetType t, GameObject other) => t switch
    {
        TargetType.Self => gameObject,
        TargetType.Attacker or TargetType.Victim or
        TargetType.Killer or TargetType.AimTarget => other,
        _ => null,
    };

    void ApplyTempStat(GameObject target, AbilityData.EffectDef e)
    {
        if (target == null) return;
        if (!target.TryGetComponent<PlayerStats>(out var st)) return;

        if (e.Duration <= 0f)
        {
            st.AddModifier(new StatModifier(e.Stat, e.ModType, e.GetValue(),
                                            SourceId.BuffBase));
            return;
        }

        int src = SourceId.BuffBase + (_buffSeq++);
        st.AddModifier(new StatModifier(e.Stat, e.ModType, e.GetValue(), src));
        StartCoroutine(RemoveAfter(st, src, e.Duration));
    }

    System.Collections.IEnumerator RemoveAfter(PlayerStats st, int src, float t)
    {
        yield return new WaitForSeconds(t);
        if (st != null) st.RemoveBySource(src);
    }

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