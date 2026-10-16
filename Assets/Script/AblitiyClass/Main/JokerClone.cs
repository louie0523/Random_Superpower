using Fusion;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 조커 궁극기 '깜짝선물'의 분신 — 네트워크 오브젝트 (Fusion 프리팹 목록에 등록 필요).
/// 소환한 조커(SA)가 움직이고 공격하며, 위치는 NetworkTransform으로 모두에게 보임.
///   - 가장 가까운 적을 따라감 (NavMeshAgent가 있고 NavMesh 위면 길찾기, 아니면 직진)
///   - 주인과 똑같음: 능력치 · 들고 있는 무기(바꾸면 따라 바꿈) · 치명 · 인챈트 · 흡혈 · 능력의 타격 반응 · 이동속도
///     (피해만 damageMul 배율 — 1이면 완전히 같음)
///   - 주인(과 나중에 같은 팀)은 절대 노리지도, 때리지도 않음 — TeamUtil
///   - 맞지 않음 (판정 콜라이더 없음). 수명·폭발은 JokerAbility가 관리
/// 프리팹 구성: NetworkObject + NetworkTransform + JokerClone (+ NavMeshAgent 선택, + CharacterController 선택)
///   팔 리그 방식(추천): 플레이어 프리팹의 겉모습(몸 · 머리 · FPArmRig + 손)을 복사해 두고 armRig에 연결
///                     → 무기를 손에 쥐고, 플레이어와 똑같은 콤보 모션 · 활 당기기 · 총 반동을 재생
///   간단 방식: armRig 비우고 손 위치에 weaponSocket만 — 무기를 들고 살짝 내미는 정도
/// </summary>
public class JokerClone : NetworkBehaviour
{
    [Networked] public PlayerRef OwnerRef { get; set; }
    [Networked, OnChangedRender(nameof(OnWeaponChanged))] public int WeaponId { get; set; }

    [Header("참조")]
    [SerializeField] private EquipmentDatabase database;
    [Tooltip("플레이어 프리팹에서 복사한 팔 리그 (FPArmRig + 손). 있으면 플레이어와 같은 공격 모션을 재생")]
    [SerializeField] private FPArmRig armRig;
    [Tooltip("armRig가 없을 때만 — 무기 모델이 붙을 손 위치")]
    [SerializeField] private Transform weaponSocket;

    [Header("이동")]
    [Tooltip("주인 이동속도를 못 읽을 때만 쓰는 속도 — 평소엔 주인 MoveSpeed 능력치를 그대로 (NavMeshAgent에도 적용)")]
    [SerializeField] private float moveSpeed = 7f;
    [Tooltip("적이 없을 때 주인 곁에 머무는 거리 (m)")]
    [SerializeField] private float followOwnerDistance = 3f;
    [SerializeField] private float searchRadius = 40f;
    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float gravity = -25f;

    [Header("공격")]
    [Tooltip("주인 피해 대비 배율 (0.5 = 50% 감소, 1 = 똑같음)")]
    [SerializeField] private float damageMul = 0.5f;
    [Tooltip("원거리 무기 공격 거리 (m)")]
    [SerializeField] private float rangedReach = 10f;
    [Tooltip("맞힐 대상 레이어 (플레이어)")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("연출")]
    [SerializeField] private GameObject meleeHitFx;
    [Tooltip("원거리 궤적 (Tracer 컴포넌트가 있으면 사용)")]
    [SerializeField] private GameObject tracerPrefab;

    private NavMeshAgent _agent;
    private CharacterController _cc;
    private GameObject _weaponModel;
    private int _shownWeapon = -1;
    private float _atkTimer;
    private float _repath;
    private float _vy;
    private readonly Collider[] _hits = new Collider[32];

    // 공격 (SA) — 근접은 모션의 타격 시점(WindUp)에 피해
    private int _combo;
    private PlayerHealth _pendingTarget;
    private DamageInfo _pendingInfo;
    private ComboStep _pendingStep;
    private float _pendingAt = -1f;
    private float _pendingReach;

    // 팔 리그 연출 (모든 클라이언트)
    private ComboStep _swingStep;
    private float _swingT, _swingSpeed = 1f;
    private float _draw;

    public void Init(PlayerRef owner, int weaponId)
    {
        OwnerRef = owner;
        WeaponId = weaponId;
    }

    public override void Spawned()
    {
        _agent = GetComponent<NavMeshAgent>();
        _cc = GetComponent<CharacterController>();

        // 맞는 판정 없음 — 플레이어 공격이 분신을 대상으로 잡지 않게
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c != _cc) c.enabled = false;

        // 피벗이 몸 가운데여도 발이 바닥에 — 에이전트 높이를 몸 크기에 맞춤 (땅에 박히지 않게)
        float foot = FootOffset();
        if (_agent != null && foot > 0.01f && Mathf.Abs(_agent.baseOffset) < 0.001f) _agent.baseOffset = foot;

        if (_agent != null)
        {
            _agent.enabled = false;
            if (HasStateAuthority && NavMesh.SamplePosition(transform.position, out var nh, 3f, NavMesh.AllAreas))
            {
                _agent.enabled = true;
                _agent.Warp(nh.position);
                _agent.speed = OwnerMoveSpeed(OwnerObject());
                _agent.angularSpeed = 720f;
                _agent.acceleration = 40f;
            }
        }

        if (armRig != null)
        {
            // 플레이어에서 복사해 온 카메라 연결은 끊음 (분신은 카메라 없음)
            armRig.Camera = null;
            armRig.CamPivot = null;

            // 손 모델이 안 들어 있으면 손 프리팹으로 만듦 (Weapon Setup의 '손 재생성'과 같음)
            if (armRig.RightHandRig == null && armRig.RightHandPrefab != null) armRig.RebuildHands();
        }

        OnWeaponChanged();
    }

    EquipmentData Weapon => database != null ? database.Get(WeaponId) : null;

    /// <summary>루트(피벗)에서 발바닥까지 — CharacterController 크기, 없으면 모델 크기로</summary>
    float FootOffset()
    {
        if (_cc != null) return SpawnPoint.FootOffset(_cc);
        float minY = float.MaxValue;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) minY = Mathf.Min(minY, r.bounds.min.y);
        return minY < float.MaxValue ? Mathf.Max(0f, transform.position.y - minY) : 0f;
    }

    /// <summary>주인 이동속도 (능력치 · 둔화 등 그대로)</summary>
    float OwnerMoveSpeed(NetworkObject owner)
    {
        if (owner != null && owner.TryGetComponent<PlayerStats>(out var st))
        {
            float v = st.Get(StatType.MoveSpeed);
            if (v > 0.1f) return v;
        }
        return moveSpeed;
    }

    void OnWeaponChanged()
    {
        if (_shownWeapon == WeaponId) return;
        _shownWeapon = WeaponId;

        if (_weaponModel != null) Destroy(_weaponModel);
        var w = Weapon;
        _swingStep = null;
        _draw = 0f;

        if (armRig != null)
        {
            armRig.Mount(w);        // 손에 쥐기 + 그립 · 기본 자세 (플레이어와 같은 설정)
            var wt = armRig.GetWeaponTransform();
            if (wt != null)
                foreach (var c in wt.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            return;
        }

        if (w == null || w.ModelPrefab == null || weaponSocket == null) return;

        _weaponModel = Instantiate(w.ModelPrefab, weaponSocket);
        _weaponModel.transform.localPosition = w.PositionOffset;
        _weaponModel.transform.localRotation = Quaternion.Euler(w.RotationOffset);
        _weaponModel.transform.localScale = w.ScaleOverride;
        foreach (var c in _weaponModel.GetComponentsInChildren<Collider>(true)) c.enabled = false;
    }

    // ══════════════════ 행동 (SA) ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        float dt = Runner.DeltaTime;
        var owner = OwnerObject();

        // 주인이 무기를 바꾸면 따라 바꿈
        if (owner != null && owner.TryGetComponent<PlayerEquipment>(out var oe) && oe.WeaponId != WeaponId)
        {
            WeaponId = oe.WeaponId;
            _combo = 0;
        }

        var target = FindTarget(owner);
        var w = Weapon;
        float speed = OwnerMoveSpeed(owner);

        bool ranged = w != null && w.Class == WeaponClass.Ranged;
        float reach = ranged ? rangedReach : Mathf.Max(1.5f, (w != null ? w.Range : 2f) * 0.9f);

        Vector3 pos = transform.position;
        // 적이 없으면 주인 곁으로
        Vector3 goal = target != null ? target.transform.position
                     : owner != null ? owner.transform.position
                     : pos + transform.forward;
        Vector3 to = goal - pos;
        to.y = 0f;
        float dist = to.magnitude;

        // 이동
        bool move = target != null ? dist > reach * 0.85f : dist > followOwnerDistance;
        if (_agent != null && _agent.enabled)
        {
            _agent.speed = speed;
            _agent.isStopped = !move;
            _repath -= dt;
            if (move && _repath <= 0f)
            {
                _repath = 0.25f;
                _agent.SetDestination(goal);
            }
        }
        else if (move)
        {
            Vector3 step = to.normalized * speed * dt;
            _vy = (_cc != null && _cc.isGrounded) ? -2f : _vy + gravity * dt;
            if (_cc != null) _cc.Move(step + Vector3.up * _vy * dt);
            else transform.position += step;
        }

        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), turnSpeed * dt);

        // 근접 — 모션이 닿는 순간 피해 (그새 멀어졌으면 빗나감)
        if (_pendingAt >= 0f && Runner.SimulationTime >= _pendingAt)
        {
            _pendingAt = -1f;
            var t = _pendingTarget;
            _pendingTarget = null;
            if (t != null && !t.IsDead)
            {
                Vector3 d = t.transform.position - transform.position; d.y = 0f;
                if (d.magnitude <= _pendingReach * 1.3f)
                {
                    Hit(t, owner, _pendingInfo, 1f, _pendingStep, false, HandPos());
                    RPC_MeleeHitFx(HandPos(), t.transform.position + Vector3.up * 1.1f);
                }
            }
        }

        // 공격
        _atkTimer -= dt;
        if (target != null && dist <= reach && _atkTimer <= 0f && owner != null)
        {
            _atkTimer = AttackInterval(w, owner, _combo);
            Attack(target, owner, w, ranged, reach);
        }
    }

    NetworkObject OwnerObject() => Runner.GetPlayerObject(OwnerRef);

    PlayerHealth FindTarget(NetworkObject owner)
    {
        PlayerHealth best = null;
        float bestD = float.MaxValue;
        int n = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, _hits, hitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (h == null || h.IsDead) continue;
            if (owner != null && h.transform.root == owner.transform.root) continue;
            if (TeamUtil.IsFriendly(OwnerRef, h)) continue;            // 나 · 같은 팀은 절대 안 노림

            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h; }
        }
        return best;
    }

    static float AtkSpeed(NetworkObject owner) =>
        owner != null && owner.TryGetComponent<PlayerStats>(out var st) ? Mathf.Max(0.1f, st.Get(StatType.AttackSpeed)) : 1f;

    static ComboStep StepAt(EquipmentData w, int combo)
    {
        if (w == null) return null;
        int len = Mathf.Max(1, w.ComboLength);
        return w.GetStep(((combo % len) + len) % len) ?? w.GetStep(0);
    }

    static float AttackInterval(EquipmentData w, NetworkObject owner, int combo)
    {
        float atkSpd = AtkSpeed(owner);
        if (w == null) return 1f / atkSpd;

        if (w.Class == WeaponClass.Melee)
        {
            var step = StepAt(w, combo);
            return (step != null ? Mathf.Max(step.Duration, step.TotalTime) : 0.6f) / atkSpd;
        }
        if (w.RangedKind == RangedType.Gun)
            return 60f / Mathf.Max(60f, w.RoundsPerMinute * atkSpd);
        return Mathf.Max(0.3f, w.MaxDrawTime) / atkSpd;
    }

    void Attack(PlayerHealth target, NetworkObject owner, EquipmentData w, bool ranged, float reach)
    {
        if (!owner.TryGetComponent<PlayerStats>(out var stats)) return;

        int combo = _combo;
        ComboStep meleeStep = !ranged ? StepAt(w, combo) : null;
        if (!ranged) _combo++;
        float atkSpd = AtkSpeed(owner);

        float mul = 1f;
        if (meleeStep != null) mul = meleeStep.DamageMultiplier;
        if (w != null && ranged && w.RangedKind != RangedType.Gun) mul = w.GetDrawDamageMul(1f);

        var src = ranged ? DamageSource.RangedWeapon : DamageSource.MeleeWeapon;
        float dmg = DamageCalc.CalcOutgoing(stats, stats.Get(StatType.AttackPower) * mul * damageMul, src, true, out bool crit);

        Vector3 from = HandPos();
        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), OwnerRef, from);
        info.IsCrit = crit;
        info.IsMelee = !ranged;

        if (ranged || meleeStep == null)
        {
            Hit(target, owner, info, ranged ? mul : 1f, ranged ? null : meleeStep, ranged, from);
            if (!ranged) RPC_MeleeHitFx(from, target.transform.position + Vector3.up * 1.1f);
        }
        else
        {
            // 모션이 닿는 순간까지 미룸
            _pendingTarget = target;
            _pendingInfo = info;
            _pendingStep = meleeStep;
            _pendingReach = reach;
            _pendingAt = Runner.SimulationTime + Mathf.Max(0f, meleeStep.WindUp) / atkSpd;
        }

        RPC_AttackFx(from, target.transform.position + Vector3.up * 1.1f, ranged, combo, atkSpd);
    }

    /// <summary>
    /// 실제 타격 — 주인의 무기 타격과 완전히 같은 처리 (PlayerAttack.ProxyWeaponHit: 치명 · 인챈트 · 흡혈 · 능력 반응).
    /// 그 메서드가 없는 옛 PlayerAttack이면 미리 계산한 피해만
    /// </summary>
    void Hit(PlayerHealth target, NetworkObject owner, DamageInfo fallback, float extraMul, ComboStep step, bool ranged, Vector3 from)
    {
        if (target == null || target.IsDead) return;
        if (TeamUtil.IsFriendly(OwnerRef, target)) return;                 // 절대 아군 피해 X
        if (owner != null && target.transform.root == owner.transform.root) return;

        if (owner != null && owner.TryGetComponent<PlayerAttack>(out var atk))
        {
            atk.ProxyWeaponHit(target, damageMul * extraMul, step, ranged, from);
            return;
        }
        target.RPC_TakeDamage(fallback);
    }

    /// <summary>공격이 나가는 위치 — 총구 · 무기 끝 · 손</summary>
    Vector3 HandPos()
    {
        if (armRig != null)
        {
            var m = armRig.Muzzle;
            if (m != null) return m.position;
            if (armRig.RightHand != null) return armRig.RightHand.position;
        }
        return weaponSocket != null ? weaponSocket.position : transform.position + Vector3.up * 1.3f;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_AttackFx(Vector3 from, Vector3 to, NetworkBool ranged, int combo, float speed)
    {
        var w = Weapon;

        if (ranged && tracerPrefab != null)
        {
            var go = Instantiate(tracerPrefab);
            var tr = go.GetComponent<Tracer>();
            if (tr != null) tr.Show(from, to, armRig != null && armRig.Muzzle != null ? armRig.Muzzle : weaponSocket);
            else { go.transform.position = to; Destroy(go, 2f); }
        }

        if (armRig != null)
        {
            if (!ranged)
            {
                // 플레이어와 같은 콤보 모션
                _swingStep = StepAt(w, combo);
                _swingT = 0f;
                _swingSpeed = Mathf.Max(0.1f, speed);
            }
            else if (w != null && w.RangedKind == RangedType.Gun)
            {
                armRig.AddRecoil(1f);
                if (armRig.MuzzleFlash != null) armRig.MuzzleFlash.Play();
            }
            else
            {
                // 활 — 쏘고 다시 당김
                _draw = 0f;
                if (armRig.CurrentWeaponRig != null) armRig.CurrentWeaponRig.OnFired();
            }
            return;
        }

        // 간단 방식 — 손을 잠깐 앞으로 내미는 정도
        if (!ranged && weaponSocket != null) _swing = 1f;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_MeleeHitFx(Vector3 from, Vector3 to)
    {
        if (meleeHitFx != null)
            Destroy(Instantiate(meleeHitFx, to, Quaternion.LookRotation((to - from).sqrMagnitude > 0.0001f ? to - from : transform.forward)), 2f);
    }

    private float _swing;
    private Quaternion _socketRest;
    private bool _restCaptured;

    void LateUpdate()
    {
        if (armRig != null) { TickArmRig(Time.deltaTime); return; }
        if (weaponSocket == null) return;
        if (!_restCaptured) { _socketRest = weaponSocket.localRotation; _restCaptured = true; }

        _swing = Mathf.MoveTowards(_swing, 0f, Time.deltaTime * 5f);
        float a = Mathf.Sin(_swing * Mathf.PI) * 70f;
        weaponSocket.localRotation = _socketRest * Quaternion.Euler(a, 0f, -a * 0.4f);
    }

    /// <summary>팔 리그 — 기본 자세 / 콤보 모션 / 활 당기기 · 총 반동</summary>
    void TickArmRig(float dt)
    {
        var w = armRig.CurrentWeapon;

        // 활: 쏜 뒤 다시 당겨서 들고 있음
        if (w != null && w.Class == WeaponClass.Ranged && w.RangedKind != RangedType.Gun)
            _draw = Mathf.MoveTowards(_draw, 1f, dt / Mathf.Max(0.2f, w.MaxDrawTime));
        else _draw = 0f;
        armRig.SetDrawAmount(_draw, dt);        // 반동 감쇠도 여기서 같이 돎

        if (_swingStep != null && _swingStep.Motion != null && _swingStep.Motion.IsValid)
        {
            float total = Mathf.Max(0.01f, _swingStep.TotalTime);
            _swingT += dt * _swingSpeed;
            if (_swingT < total)
            {
                armRig.ApplySwing(_swingStep.Motion, _swingT / total, _swingStep.HandMode);
                return;
            }
            _swingStep = null;
        }

        armRig.ApplyPose(armRig.GetEffectiveIdle());
    }
}