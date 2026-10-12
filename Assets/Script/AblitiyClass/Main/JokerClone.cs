using Fusion;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 조커 궁극기 '깜짝선물'의 분신 — 네트워크 오브젝트 (Fusion 프리팹 목록에 등록 필요).
/// 소환한 조커(SA)가 움직이고 공격하며, 위치는 NetworkTransform으로 모두에게 보임.
///   - 가장 가까운 적을 따라감 (NavMeshAgent가 있고 NavMesh 위면 길찾기, 아니면 직진)
///   - 들고 있던 무기로 공격, 피해는 주인 능력치 × 50% (주인 장비·상점 효과가 그대로 반영됨)
///   - 맞지 않음 (판정 콜라이더 없음). 수명·폭발은 JokerAbility가 관리
/// 프리팹 구성: NetworkObject + NetworkTransform + JokerClone (+ NavMeshAgent 선택, + CharacterController 선택)
///             겉모습은 플레이어 몸 모델 복사본, 손 위치에 WeaponSocket
/// </summary>
public class JokerClone : NetworkBehaviour
{
    [Networked] public PlayerRef OwnerRef { get; set; }
    [Networked, OnChangedRender(nameof(OnWeaponChanged))] public int WeaponId { get; set; }

    [Header("참조")]
    [SerializeField] private EquipmentDatabase database;
    [Tooltip("무기 모델이 붙을 손 위치")]
    [SerializeField] private Transform weaponSocket;

    [Header("이동")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float searchRadius = 40f;
    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float gravity = -25f;

    [Header("공격")]
    [Tooltip("주인 피해 대비 배율 (0.5 = 50% 감소)")]
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

        if (_agent != null)
        {
            _agent.enabled = false;
            if (HasStateAuthority && NavMesh.SamplePosition(transform.position, out var nh, 3f, NavMesh.AllAreas))
            {
                _agent.enabled = true;
                _agent.Warp(nh.position);
                _agent.speed = moveSpeed;
                _agent.angularSpeed = 720f;
                _agent.acceleration = 40f;
            }
        }

        OnWeaponChanged();
    }

    EquipmentData Weapon => database != null ? database.Get(WeaponId) : null;

    void OnWeaponChanged()
    {
        if (_shownWeapon == WeaponId) return;
        _shownWeapon = WeaponId;

        if (_weaponModel != null) Destroy(_weaponModel);
        var w = Weapon;
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
        var target = FindTarget(owner);
        var w = Weapon;

        bool ranged = w != null && w.Class == WeaponClass.Ranged;
        float reach = ranged ? rangedReach : Mathf.Max(1.5f, (w != null ? w.Range : 2f) * 0.9f);

        Vector3 pos = transform.position;
        Vector3 to = target != null ? target.transform.position - pos : transform.forward * 5f;
        to.y = 0f;
        float dist = to.magnitude;

        // 이동
        bool move = target == null || dist > reach * 0.85f;
        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = !move;
            _repath -= dt;
            if (move && _repath <= 0f)
            {
                _repath = 0.25f;
                _agent.SetDestination(target != null ? target.transform.position : pos + transform.forward * 5f);
            }
        }
        else if (move)
        {
            Vector3 step = to.normalized * moveSpeed * dt;
            _vy = (_cc != null && _cc.isGrounded) ? -2f : _vy + gravity * dt;
            if (_cc != null) _cc.Move(step + Vector3.up * _vy * dt);
            else transform.position += step;
        }

        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), turnSpeed * dt);

        // 공격
        _atkTimer -= dt;
        if (target != null && dist <= reach && _atkTimer <= 0f && owner != null)
        {
            _atkTimer = AttackInterval(w, owner);
            Attack(target, owner, w, ranged);
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

            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h; }
        }
        return best;
    }

    static float AttackInterval(EquipmentData w, NetworkObject owner)
    {
        float atkSpd = owner.TryGetComponent<PlayerStats>(out var st) ? Mathf.Max(0.1f, st.Get(StatType.AttackSpeed)) : 1f;
        if (w == null) return 1f / atkSpd;

        if (w.Class == WeaponClass.Melee)
        {
            var step = w.GetStep(0);
            return (step != null ? step.Duration : 0.6f) / atkSpd;
        }
        if (w.RangedKind == RangedType.Gun)
            return 60f / Mathf.Max(60f, w.RoundsPerMinute * atkSpd);
        return Mathf.Max(0.3f, w.MaxDrawTime) / atkSpd;
    }

    void Attack(PlayerHealth target, NetworkObject owner, EquipmentData w, bool ranged)
    {
        if (!owner.TryGetComponent<PlayerStats>(out var stats)) return;

        float mul = 1f;
        if (w != null && !ranged) { var step = w.GetStep(0); if (step != null) mul = step.DamageMultiplier; }
        if (w != null && ranged && w.RangedKind != RangedType.Gun) mul = w.GetDrawDamageMul(1f);

        var src = ranged ? DamageSource.RangedWeapon : DamageSource.MeleeWeapon;
        float dmg = DamageCalc.CalcOutgoing(stats, stats.Get(StatType.AttackPower) * mul * damageMul, src, true, out bool crit);

        Vector3 from = weaponSocket != null ? weaponSocket.position : transform.position + Vector3.up * 1.3f;
        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), OwnerRef, from);
        info.IsCrit = crit;
        info.IsMelee = !ranged;
        target.RPC_TakeDamage(info);

        RPC_AttackFx(from, target.transform.position + Vector3.up * 1.1f, ranged);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_AttackFx(Vector3 from, Vector3 to, NetworkBool ranged)
    {
        if (ranged && tracerPrefab != null)
        {
            var go = Instantiate(tracerPrefab);
            var tr = go.GetComponent<Tracer>();
            if (tr != null) tr.Show(from, to, weaponSocket);
            else { go.transform.position = to; Destroy(go, 2f); }
        }
        else if (!ranged && meleeHitFx != null)
        {
            Destroy(Instantiate(meleeHitFx, to, Quaternion.LookRotation(to - from)), 2f);
        }

        // 근접은 손을 잠깐 앞으로 내미는 정도의 간단한 동작
        if (!ranged && weaponSocket != null) _swing = 1f;
    }

    private float _swing;
    private Quaternion _socketRest;
    private bool _restCaptured;

    void LateUpdate()
    {
        if (weaponSocket == null) return;
        if (!_restCaptured) { _socketRest = weaponSocket.localRotation; _restCaptured = true; }

        _swing = Mathf.MoveTowards(_swing, 0f, Time.deltaTime * 5f);
        float a = Mathf.Sin(_swing * Mathf.PI) * 70f;
        weaponSocket.localRotation = _socketRest * Quaternion.Euler(a, 0f, -a * 0.4f);
    }
}
