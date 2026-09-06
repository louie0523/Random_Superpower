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
    [Tooltip("0이 아니면 이 능력으로 시작 (테스트용)")]
    [SerializeField] private int forceMainId = 0;
    [SerializeField] private int forceSubId = 0;
    [SerializeField] private int forceRandomId = 0;

    [Header("Charge")]
    [SerializeField] private float defaultMaxCharge = 100f;

    // ══════════════════ 네트워크 상태 ══════════════════

    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int MainAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int SubAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int RandomAbilityId { get; set; }

    [Networked, Capacity(4)]
    public NetworkArray<float> CooldownEnd { get; }

    [Networked] public float Charge { get; set; }
    [Networked] public float MaxCharge { get; set; }

    [Networked] public TickTimer StateTimer { get; set; }
    [Networked] public int ActiveStateId { get; set; }

    [Networked] public TickTimer SealTimer { get; set; }

    // ══════════════════ 내부 상태 ══════════════════

    private AbilityData.SkillDef _casting;
    private float _castTimer = -1f;
    private bool _castHitApplied;

    private Vector3 _dashDir;
    private float _dashSpeed;
    private float _dashTimer;
    private float _moveTickAccum;

    private PlayerStats _stats;
    private PlayerHealth _health;
    private PlayerSkillInput _input;
    private PlayerMovement _movement;
    private CharacterController _cc;

    private readonly List<AbilityData> _active = new();
    private readonly List<AbilityBehaviour> _behaviours = new();
    private readonly AbilityData.SkillDef[] _slots = new AbilityData.SkillDef[SkillSlotExt.Count];
    private readonly Dictionary<int, float> _perTargetCd = new();


    [Header("Dash")]
    [SerializeField] private string ghostLayerName = "PlayerGhost";

    private int _normalLayer;
    private int _ghostLayer;
    private DashParams _dashParams;

    // ══════════════════ 공개 접근자 ══════════════════

    public IReadOnlyList<AbilityData> Active => _active;
    public IReadOnlyList<AbilityBehaviour> Behaviours => _behaviours;

    public PlayerStats Stats => _stats;
    public PlayerHealth Health => _health;
    public Transform CameraRoot => cameraRoot;

    public bool IsSealed => !SealTimer.ExpiredOrNotRunning(Runner);
    public bool IsCasting => _castTimer >= 0f;
    public bool IsDashing => _dashTimer > 0f;
    public bool IsMoving => _movement != null && _movement.IsMoving;

    public float ChargeRatio => MaxCharge <= 0f ? 0f : Charge / MaxCharge;

    public bool DashIgnoresGravity => _dashTimer > 0f && _dashParams.IgnoreGravity;

    public AbilityData.SkillDef GetSlot(int i) =>
        (i >= 0 && i < _slots.Length) ? _slots[i] : null;

    public bool HasState(int id) =>
        ActiveStateId == id && !StateTimer.ExpiredOrNotRunning(Runner);

    public T GetAbilityBehaviour<T>() where T : AbilityBehaviour
    {
        foreach (var b in _behaviours)
            if (b is T t) return t;
        return null;
    }

    /// <summary>장착된 전용 로직 중 T 타입 찾기</summary>
    public T FindAbility<T>() where T : AbilityBehaviour
    {
        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i] is T t) return t;
        return null;
    }

    // ══════════════════ 초기화 ══════════════════

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _input = GetComponent<PlayerSkillInput>();
        _movement = GetComponent<PlayerMovement>();
        _cc = GetComponent<CharacterController>();

        if (weaponAnim == null) weaponAnim = GetComponentInChildren<WeaponAnimator>(true);

        if (HasStateAuthority)
        {
            if (MaxCharge <= 0f) MaxCharge = defaultMaxCharge;

            if (forceMainId != 0) MainAbilityId = forceMainId;
            if (forceSubId != 0) SubAbilityId = forceSubId;
            if (forceRandomId != 0) RandomAbilityId = forceRandomId;


            if (RandomAbilityId == 0) RerollRandomAbility();


        }

        RebuildAbilities();

        _normalLayer = gameObject.layer;
        _ghostLayer = LayerMask.NameToLayer(ghostLayerName);
        if (_ghostLayer < 0)
        {
            Debug.LogWarning($"[Ability] '{ghostLayerName}' 레이어가 없습니다.");
            _ghostLayer = _normalLayer;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_SetGhost(bool on)
    {
        gameObject.layer = on ? _ghostLayer : _normalLayer;
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

        // 패시브 스탯
        if (a.Passives != null)
            foreach (var p in a.Passives)
            {
                if (p.Effects == null) continue;
                foreach (var e in p.Effects)
                    if (e.Trigger == TriggerType.Passive && e.Effect == EffectType.StatMod)
                        _stats.AddModifier(new StatModifier(
                            e.Stat, e.ModType, e.GetValue(), sourceId));
            }

        // 스킬 슬롯 배치
        if (a.Skills != null)
            foreach (var s in a.Skills)
            {
                int idx = s.Slot.Index();
                if (idx < 0 || idx >= _slots.Length) continue;

                if (_slots[idx] == null) _slots[idx] = s;
                else Debug.LogWarning($"[Ability] 슬롯 {s.Slot} 중복: {a.DisplayName} / {s.Name}");
            }

        // 전용 로직
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

        if (_dashTimer > 0f)
        {
            _dashTimer -= dt;

            if (_cc != null)
            {
                var flags = _cc.Move(_dashDir * _dashSpeed * dt);

                if (_dashParams.StopOnContact && (flags & CollisionFlags.Sides) != 0)
                    _dashTimer = 0f;
            }

            if (_dashTimer <= 0f && _dashParams.PassThroughPlayers)
                RPC_SetGhost(false);
        }

        // 상태 만료
        if (ActiveStateId != 0 && StateTimer.Expired(Runner))
        {
            int old = ActiveStateId;
            ActiveStateId = 0;
            RPC_OnStateChanged(old, false);
        }

        // 전용 로직 틱
        for (int i = 0; i < _behaviours.Count; i++)
            _behaviours[i].Tick(dt);

        // 이동 중 트리거 (초당 1회)
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

        if (_moveTickAccum >= 1f)
        {
            _moveTickAccum -= 1f;
            FireTrigger(TriggerType.OnMove);
            Debug.Log($"[Charge] {Charge:F0}/{MaxCharge:F0}");
        }
    }

    void TickCast(float dt)
    {
        _castTimer += dt;

        var m = _casting.Motion;
        float activeStart = m.WindUp;
        float activeEnd = m.WindUp + m.ActiveWindow;

        bool inWindow = _castTimer >= activeStart && _castTimer <= activeEnd;
        bool missed = !_castHitApplied && _castTimer > activeEnd && _castTimer < m.TotalTime;

        if ((inWindow || missed) && !_castHitApplied)
        {
            _castHitApplied = true;
            FireSkillEffects(_casting);
        }

        if (_castTimer >= m.TotalTime)
        {
            _castTimer = -1f;
            _casting = null;
        }
    }

    // ══════════════════ 스킬 사용 ══════════════════

    public bool TryUseSkill(int slotIndex)
    {
        if (!HasStateAuthority) return false;
        if (IsSealed) { Debug.Log("[Skill] 봉인됨"); return false; }

        var skill = GetSlot(slotIndex);
        if (skill == null) { Debug.Log($"[Skill] 슬롯 {slotIndex} 비어있음"); return false; }

        if (Runner.SimulationTime < CooldownEnd[slotIndex]) { Debug.Log("[Skill] 쿨타임"); return false; }
        if (_health.Soul < skill.SoulCost) { Debug.Log("[Skill] 소울 부족"); return false; }

        // 전용 로직이 처리하는지 먼저 확인
        bool handled = false;
        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i].OnSkillUsed(slotIndex, skill)) handled = true;

        // 쿨타임 / 코스트
        float cdr = _stats.Get(StatType.CooldownReduction);
        float cd = skill.Cooldown * (1f - Mathf.Clamp01(cdr));
        CooldownEnd.Set(slotIndex, Runner.SimulationTime + cd);

        if (skill.SoulCost > 0) _health.SpendSoul(skill.SoulCost);

        // 모션
        if (skill.Motion != null && skill.Motion.Duration > 0.01f)
        {
            _casting = skill;
            _castTimer = 0f;
            _castHitApplied = handled;   // 이미 처리됐으면 효과 재발동 안 함
            RPC_PlaySkillMotion(slotIndex);
        }
        else if (!handled)
        {
            FireSkillEffects(skill);
        }

        FireTrigger(TriggerType.OnSkillUse);
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
    }

    /// <summary>스킬 조준 대상 찾기 (공격 판정과 동일 방식)</summary>
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

    public void FireTrigger(TriggerType trigger, GameObject other = null)
    {
        if (!HasStateAuthority) return;
        if (IsSealed && trigger != TriggerType.OnHitTaken) return;

        // 데이터 기반 효과
        foreach (var ability in _active)
        {
            if (ability.Passives != null)
                foreach (var p in ability.Passives)
                {
                    if (p.Effects == null) continue;
                    foreach (var e in p.Effects)
                        if (e.Trigger == trigger) Execute(e, other);
                }

            if (ability.Skills != null)
                foreach (var s in ability.Skills)
                {
                    if (s.Effects == null) continue;
                    foreach (var e in s.Effects)
                        if (e.Trigger == trigger) Execute(e, other);
                }
        }

        // 전용 로직
        for (int i = 0; i < _behaviours.Count; i++)
            _behaviours[i].OnTrigger(trigger, other);
    }


    // ══════════════════ 자원 ══════════════════

    public void AddCharge(float amount)
    {
        if (!HasStateAuthority) return;
        Charge = Mathf.Clamp(Charge + amount, 0f, MaxCharge);
    }

    public bool SpendCharge(float amount)
    {
        if (!HasStateAuthority) return false;
        if (Charge < amount) return false;
        Charge -= amount;
        return true;
    }

    public void SetMaxCharge(float v)
    {
        if (!HasStateAuthority) return;
        MaxCharge = Mathf.Max(1f, v);
        Charge = Mathf.Min(Charge, MaxCharge);
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

                    // 피치 제한
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

            default:   // Horizontal
                {
                    Vector3 f = transform.forward;
                    f.y = 0f;
                    return f.sqrMagnitude > 0.0001f ? f.normalized : transform.forward;
                }
        }
    }

    // ══════════════════ 쿨타임 조회 ══════════════════

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
        var next = database.RollRandom(RandomAbilityId);
        if (next != null) RandomAbilityId = next.Id;
    }

    public AbilityData GetRandomAbility() => database?.Get(RandomAbilityId);
    public AbilityData GetMainAbility() => database?.Get(MainAbilityId);
    public AbilityData GetSubAbility() => database?.Get(SubAbilityId);

    // ══════════════════ RPC ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplySeal(float seconds)
    {
        SealTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlaySkillMotion(int slotIndex)
    {
        var skill = GetSlot(slotIndex);
        if (skill?.Motion == null) return;
        weaponAnim?.Play(skill.Motion);
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
        if (e.Chance < 1f && Random.value > e.Chance) return;
        if (e.RequireStateId != 0 && !HasState(e.RequireStateId)) return;
        if (e.ChargeCost > 0f && !SpendCharge(e.ChargeCost)) return;

        GameObject target = ResolveTarget(e.Target, other);
        if (target == null && e.Target != TargetType.AreaAroundSelf) return;

        // 대상별 쿨타임
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
                if (target.TryGetComponent<PlayerHealth>(out var h))
                    h.RPC_TakeDamage(Mathf.RoundToInt(e.GetValue()),
                                     Object.InputAuthority, transform.position);
                break;

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
                    ac.AddCharge(e.GetValue());
                break;

            case EffectType.EnterState:
                EnterState(Mathf.RoundToInt(e.GetValue()), e.Duration);
                break;

            case EffectType.Dash:
                DoDash(new DashParams
                {
                    Distance = e.GetValue(),
                    Duration = e.Duration > 0f ? e.Duration : 0.2f,
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

        int src = SourceId.BuffBase + Runner.Tick;
        st.AddModifier(new StatModifier(e.Stat, e.ModType, e.GetValue(), src));
        StartCoroutine(RemoveAfter(st, src, e.Duration));
    }

    System.Collections.IEnumerator RemoveAfter(PlayerStats st, int src, float t)
    {
        yield return new WaitForSeconds(t);
        if (st != null) st.RemoveBySource(src);
    }

    /// <summary>능력이 기본 공격을 대체하는지 확인. PlayerAttack이 호출</summary>
    public bool TryOverrideAttack(out AttackOverride ovr)
    {
        ovr = AttackOverride.None;

        if (!HasStateAuthority) return false;

        for (int i = 0; i < _behaviours.Count; i++)
            if (_behaviours[i].TryOverrideAttack(out ovr))
                return true;

        return false;
    }
}