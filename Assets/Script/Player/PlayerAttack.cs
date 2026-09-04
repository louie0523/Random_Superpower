using Fusion;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private WeaponAnimator weaponAnim;
    [SerializeField] private LayerMask hitMask;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logHits = false;

    // 컴포넌트
    private PlayerStats _stats;
    private PlayerHealth _health;
    private PlayerEquipment _equip;
    private AbilityRuntime _ability;
    private PlayerAnimator _anim;
    private CharacterController _cc;

    // 콤보 상태
    private int _comboIndex;
    private float _attackTimer = -1f;
    private float _comboExpire;
    private bool _queuedInput;
    private bool _attackQueued;
    private bool _hitApplied;
    private ComboStep _current;

    // ══════════════════ 초기화 ══════════════════

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _equip = GetComponent<PlayerEquipment>();
        _ability = GetComponent<AbilityRuntime>();
        _anim = GetComponent<PlayerAnimator>();
        _cc = GetComponent<CharacterController>();

        if (weaponAnim == null) weaponAnim = GetComponentInChildren<WeaponAnimator>(true);

        if (cameraRoot == null) Debug.LogError("[Attack] cameraRoot 비었음", this);
        if (fpsCamera == null) Debug.LogError("[Attack] fpsCamera 비었음", this);
        if (_stats == null) Debug.LogError("[Attack] PlayerStats 없음", this);
    }

    void Update()
    {
        if (!HasStateAuthority) return;
        if (Input.GetMouseButtonDown(0)) _attackQueued = true;
    }

    // ══════════════════ 메인 루프 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (_health.IsDead)
        {
            ResetCombo();
            _attackQueued = false;
            return;
        }

        float dt = Runner.DeltaTime;
        bool pressed = _attackQueued;
        _attackQueued = false;

        if (_attackTimer < 0f && _comboIndex > 0 &&
            Runner.SimulationTime > _comboExpire)
            _comboIndex = 0;

        if (_attackTimer >= 0f) TickAttack(dt, pressed);
        else if (pressed) StartAttack();
    }

    void TickAttack(float dt, bool pressed)
    {
        _attackTimer += dt;

        float activeStart = _current.WindUp;
        float activeEnd = _current.WindUp + _current.ActiveWindow;

        bool inWindow = _attackTimer >= activeStart && _attackTimer <= activeEnd;

        // 구간을 지나쳤는데 아직 안 때렸으면 즉시 판정
        bool missed = !_hitApplied && _attackTimer > activeEnd
                      && _attackTimer < _current.TotalTime;

        if (inWindow || missed) DoHitCheck(_current);

        // 전진은 정상 구간에서만
        if (_current.LungeDistance > 0f && inWindow)
        {
            float speed = _current.LungeDistance / Mathf.Max(0.01f, _current.ActiveWindow);
            _cc.Move(transform.forward * speed * dt);
        }
        bool inActive = _attackTimer >= activeStart && _attackTimer <= activeEnd;

        if (logHits)
            Debug.Log($"[Tick] 콤보{_comboIndex} t={_attackTimer:F3} " +
                      $"구간={activeStart:F3}~{activeEnd:F3} active={inActive} applied={_hitApplied}");

        if (inActive) DoHitCheck(_current);

        // 전진
        if (_current.LungeDistance > 0f && inActive)
        {
            float speed = _current.LungeDistance / Mathf.Max(0.01f, _current.ActiveWindow);
            _cc.Move(transform.forward * speed * dt);
        }

        // 다음 입력 예약 — 판정이 끝난 뒤부터
        if (pressed && _attackTimer >= activeEnd)
            _queuedInput = true;

        // 마지막 타는 캔슬 불가
        var weapon = _equip.GetWeapon();
        bool isLastHit = weapon != null && _comboIndex >= weapon.ComboLength;
        float cancelAt = isLastHit ? _current.TotalTime : _current.CancelTime;

        if (_attackTimer >= _current.TotalTime ||
            (_attackTimer >= cancelAt && _queuedInput))
        {
            _attackTimer = -1f;

            if (_queuedInput)
            {
                _queuedInput = false;
                StartAttack();
            }
            else
            {
                _comboExpire = Runner.SimulationTime + _current.ComboResetTime;
            }
        }
    }

    void StartAttack()
    {
        var weapon = _equip != null ? _equip.GetWeapon() : null;
        if (weapon == null || weapon.ComboLength == 0) return;

        if (logHits)
            Debug.Log($"[Start] 콤보{_comboIndex} " +
                      $"Duration={_current.Duration:F2} WindUp={_current.WindUp:F3} " +
                      $"Active={_current.ActiveWindow:F3}");

        _comboIndex %= weapon.ComboLength;
        _current = weapon.GetStep(_comboIndex);
        if (_current == null) return;

        _attackTimer = 0f;
        _hitApplied = false;
        _comboIndex++;

        if (fpsCamera != null) fpsCamera.FovKick(_current.CameraKickFov);

        RPC_PlaySwing(_comboIndex - 1);

        if (_anim != null)
        {
            float atkSpd = 1f / Mathf.Max(0.1f, _current.TotalTime);
            _anim.RPC_PlayAttack(_comboIndex - 1, atkSpd);
        }
    }

    void ResetCombo()
    {
        _attackTimer = -1f;
        _comboIndex = 0;
        _queuedInput = false;
        _hitApplied = false;
        _current = null;
    }

    // ══════════════════ 히트 판정 ══════════════════

    void DoHitCheck(ComboStep step)
    {
        if (_hitApplied) return;
        _hitApplied = true;

        var weapon = _equip.GetWeapon();
        if (weapon == null || cameraRoot == null) return;

        step.ResolveHitShape(weapon, out var mode, out float range,
                             out float angle, out float height, out float minRange);

        Vector3 origin = transform.position;          // 캐릭터 중심
        Vector3 forward = cameraRoot.forward;         // 조준 방향

        Collider[] cols = Physics.OverlapSphere(origin, range, hitMask);
        if (cols.Length == 0) return;

        System.Array.Sort(cols, (a, b) =>
            (a.transform.position - origin).sqrMagnitude
            .CompareTo((b.transform.position - origin).sqrMagnitude));

        float halfAngle = angle * 0.5f;
        Vector3 flatFwd = new Vector3(forward.x, 0f, forward.z).normalized;
        if (flatFwd.sqrMagnitude < 0.001f) flatFwd = transform.forward;

        int hits = 0;

        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;
            if (!col.TryGetComponent<PlayerHealth>(out var target)) continue;
            if (target.IsDead) continue;

            Vector3 toTarget = col.transform.position - origin;

            // 수직
            if (Mathf.Abs(toTarget.y) > height)
            {
                if (logHits) Debug.Log($"[Hit] {col.name} 높이초과 {toTarget.y:F2}");
                continue;
            }

            // 수평 거리
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float flatDist = flat.magnitude;

            if (flatDist < minRange) continue;
            if (flatDist > range) continue;

            // 수평 각도
            if (mode == WeaponHitMode.ForwardArc && flatDist > 0.01f)
            {
                float a = Vector3.Angle(flatFwd, flat.normalized);
                if (a > halfAngle)
                {
                    if (logHits) Debug.Log($"[Hit] {col.name} 각도밖 {a:F0}° > {halfAngle:F0}°");
                    continue;
                }
            }

            ApplyHit(target, col.gameObject, step);
            if (++hits >= step.MaxTargets) break;
        }
    }

    void ApplyHit(PlayerHealth target, GameObject targetObj, ComboStep step)
    {
        int dmg = CalcDamage(step.DamageMultiplier);

        target.RPC_TakeDamage(dmg, Object.InputAuthority, cameraRoot.position);
        _ability?.FireTrigger(TriggerType.OnHit, targetObj);

        // 흡혈
        float lifeSteal = _stats.Get(StatType.LifeSteal);
        if (lifeSteal > 0f)
        {
            int heal = Mathf.RoundToInt(dmg * lifeSteal);
            if (heal > 0) _health.RPC_Heal(heal);
        }

        if (fpsCamera != null) fpsCamera.Shake(step.CameraShake);
        if (step.HitStopDuration > 0f) HitStop.Do(step.HitStopDuration);
    }

    int CalcDamage(float mult)
    {
        float atk = _stats.Get(StatType.AttackPower) * mult;

        if (Random.value < _stats.Get(StatType.CritChance))
            atk *= _stats.Get(StatType.CritDamage);

        return Mathf.Max(1, Mathf.RoundToInt(atk));
    }

    // ══════════════════ 연출 동기화 ══════════════════

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlaySwing(int comboIndex)
    {
        var weapon = _equip != null ? _equip.GetWeapon() : null;
        if (weapon == null) return;

        var step = weapon.GetStep(comboIndex);
        if (step == null) return;

        weaponAnim?.Play(step);

        if (step.SwingSound != null)
            AudioSource.PlayClipAtPoint(step.SwingSound, transform.position, 0.7f);
    }

    // ══════════════════ 디버그 ══════════════════

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || cameraRoot == null || _equip == null) return;

        var weapon = _equip.GetWeapon();
        if (weapon == null) return;

        var step = _current ?? weapon.GetStep(0);
        if (step == null) return;

        step.ResolveHitShape(weapon, out var mode, out float range,
                             out float angle, out float height, out float minRange);

        Vector3 o = transform.position;
        Vector3 f = new Vector3(cameraRoot.forward.x, 0f, cameraRoot.forward.z).normalized;
        if (f.sqrMagnitude < 0.001f) f = transform.forward;

        bool active = _attackTimer >= 0f && _hitApplied;
        Gizmos.color = active ? Color.red : new Color(1f, 0.5f, 0.2f, 0.6f);

        if (mode == WeaponHitMode.ForwardSphere)
        {
            Gizmos.DrawWireSphere(o, range);
        }
        else
        {
            float half = angle * 0.5f;

            Gizmos.DrawLine(o, o + Quaternion.Euler(0f, -half, 0f) * f * range);
            Gizmos.DrawLine(o, o + Quaternion.Euler(0f, +half, 0f) * f * range);

            int seg = 20;
            Vector3 prev = o + Quaternion.Euler(0f, -half, 0f) * f * range;
            for (int i = 1; i <= seg; i++)
            {
                float a = -half + angle * i / seg;
                Vector3 p = o + Quaternion.Euler(0f, a, 0f) * f * range;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        // 수직 경계
        if (height < 50f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(o + Vector3.up * height, range * 0.1f);
            Gizmos.DrawWireSphere(o - Vector3.up * height, range * 0.1f);
            Gizmos.DrawLine(o + Vector3.up * height, o - Vector3.up * height);
        }

        // 최소 사거리
        if (minRange > 0.01f)
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            Gizmos.DrawWireSphere(o, minRange);
        }
    }
}