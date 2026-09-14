using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private WeaponAnimator weaponAnim;
    [SerializeField] private LayerMask hitMask;
    [SerializeField] private LayerMask projectileMask;

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
    private MotionEventPlayer _events;

    // 콤보 상태
    private int _comboIndex;
    private float _attackTimer = -1f;
    private float _comboExpire;
    private bool _queuedInput;
    private bool _attackQueued;
    private bool _hitApplied;
    private ComboStep _current;

    private float _pendingDamageMul = 1f;

    private readonly HashSet<int> _hitThisAttack = new();
    private bool _isDashAttack;
    private bool _dashStarted;
    private AttackOverride _override;
    public bool IsAttacking => _attackTimer >= 0f;

    // 원거리 상태
    private float _drawTime = -1f;      // 시위 당긴 시간. -1이면 안 당기는 중
    private bool _releaseQueued;
    public bool IsDrawing => _drawTime >= 0f;


    // ══════════════════ 초기화 ══════════════════

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _equip = GetComponent<PlayerEquipment>();
        _ability = GetComponent<AbilityRuntime>();
        _anim = GetComponent<PlayerAnimator>();
        _cc = GetComponent<CharacterController>();
        _events = GetComponent<MotionEventPlayer>();

        if (weaponAnim == null) weaponAnim = GetComponentInChildren<WeaponAnimator>(true);

        if (cameraRoot == null) Debug.LogError("[Attack] cameraRoot 비었음", this);
        if (fpsCamera == null) Debug.LogError("[Attack] fpsCamera 비었음", this);
        if (_stats == null) Debug.LogError("[Attack] PlayerStats 없음", this);

        Debug.Log($"[Attack] weaponAnim id={weaponAnim?.GetInstanceID()}");
    }



    void Update()
    {
        if (!HasStateAuthority) return;

        var weapon = _equip != null ? _equip.GetWeapon() : null;
        bool ranged = weapon != null && weapon.Class == WeaponClass.Ranged;

        if (ranged)
        {
            if (Input.GetMouseButtonUp(0)) _releaseQueued = true;
        }
        else
        {
            if (Input.GetMouseButtonDown(0)) _attackQueued = true;
        }
    }

    // ══════════════════ 메인 루프 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (_health.IsDead)
        {
            ResetCombo();
            _attackQueued = false;
            CancelDraw();
            return;
        }

        // 스킬 시전 중이면 공격 불가
        if (_ability != null && _ability.IsCasting)
        {
            _attackQueued = false;
            return;
        }


        float dt = Runner.DeltaTime;

        // ── 원거리 ──
        var wpn = _equip.GetWeapon();
        if (wpn != null && wpn.Class == WeaponClass.Ranged)
        {
            TickRanged(wpn, dt);
            return;
        }

        // ── 근거리 ──
        bool pressed = _attackQueued;
        _attackQueued = false;

        if (_attackTimer < 0f && _comboIndex > 0 &&
            Runner.SimulationTime > _comboExpire)
            _comboIndex = 0;

        if (_attackTimer >= 0f) TickAttack(dt, pressed);
        else if (pressed) StartAttack();
    }

    void TickRanged(EquipmentData weapon, float dt)
    {
        // ── 발사 (홀드 해제보다 먼저 처리) ──
        if (_releaseQueued)
        {
            _releaseQueued = false;

            if (_drawTime >= 0f)
            {
                if (weapon.CanRelease(_drawTime)) Fire(weapon);
                CancelDraw();
            }
            return;
        }

        // ── 시위 당기는 중 ──
        if (Input.GetMouseButton(0))
        {
            if (_drawTime < 0f) _drawTime = 0f;
            _drawTime += dt;
        }

        weaponAnim?.SetDraw(DrawRatio01);
        Debug.Log($"[SetDraw] ratio={DrawRatio01:F2} anim={(weaponAnim == null ? "NULL" : "OK")}");
    }

    void CancelDraw()
    {
        _drawTime = -1f;
        _releaseQueued = false;
        weaponAnim?.SetDraw(0f);
    }

    void Fire(EquipmentData weapon)
    {
        if (weapon.ProjectilePrefab == null)
        {
            Debug.LogWarning($"[Attack] {weapon.DisplayName}에 투사체 프리팹이 없습니다.");
            return;
        }

        float ratio = weapon.GetDrawRatio(_drawTime);
        float speed = weapon.GetDrawSpeed(ratio);
        float dmgMul = weapon.GetDrawDamageMul(ratio);

        Vector3 origin = GetMuzzlePos();
        Vector3 dir = GetAimDirection(origin);      // ★

        float baseDmg = _stats.Get(StatType.AttackPower) * dmgMul;

        SpawnProjectile(weapon, origin, dir, speed, baseDmg, true);
        RPC_PlayProjectile(origin, dir, speed);

        if (fpsCamera != null) fpsCamera.FovKick(-3f * ratio);
    }

    /// <summary>화면 중앙이 가리키는 지점을 향하는 방향</summary>
    Vector3 GetAimDirection(Vector3 from)
    {
        Vector3 camPos = cameraRoot.position;
        Vector3 camFwd = cameraRoot.forward;

        const float maxAim = 200f;

        Vector3 aimPoint;

        if (Physics.Raycast(camPos, camFwd, out var hit, maxAim,
                            projectileMask, QueryTriggerInteraction.Ignore)
            && hit.collider.gameObject != gameObject)
        {
            aimPoint = hit.point;
        }
        else
        {
            aimPoint = camPos + camFwd * maxAim;
        }

        Vector3 dir = aimPoint - from;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : camFwd;
    }

    Vector3 GetMuzzlePos()
    {
        var armRig = weaponAnim != null ? weaponAnim.ArmRig : null;
        var muzzle = armRig != null ? armRig.Muzzle : null;
        return muzzle != null ? muzzle.position : cameraRoot.position + cameraRoot.forward * 0.5f;
    }

    void SpawnProjectile(EquipmentData weapon, Vector3 pos, Vector3 dir,
                     float speed, float baseDmg, bool authoritative)
    {
        var go = Instantiate(weapon.ProjectilePrefab, pos, Quaternion.LookRotation(dir));
        var proj = go.GetComponent<Projectile>();

        if (proj == null)
        {
            Debug.LogWarning($"[Attack] {weapon.ProjectilePrefab.name}에 Projectile이 없습니다.");
            Destroy(go, 3f);
            return;
        }

        proj.Launch(
            pos, dir, speed,
            weapon.ProjectileGravity,
            weapon.ProjectileLifetime,
            authoritative,
            projectileMask,
            weapon.PierceCount,
            weapon.PierceDamageFalloff,
            _cc,
            authoritative
                ? (col, point, mul) => OnProjectileHit(col, point, baseDmg * mul)
                : null);
    }

    void OnProjectileHit(Collider col, Vector3 point, float baseDmg)
    {
        if (col == null) return;
        if (!col.TryGetComponent<PlayerHealth>(out var target)) return;   // 지형이면 그냥 멈춤
        if (target.IsDead) return;
        if (col.gameObject == gameObject) return;

        float outgoing = DamageCalc.CalcOutgoing(
            _stats, baseDmg, DamageSource.RangedWeapon, true, out bool crit);

        target.RPC_TakeDamage(DamageInfo.FromPlayerHit(
            Mathf.RoundToInt(outgoing), Object.InputAuthority, point));

        _ability?.FireTrigger(TriggerType.OnHit, col.gameObject);

        float lifeSteal = _stats.Get(StatType.LifeSteal);
        if (lifeSteal > 0f)
        {
            int heal = Mathf.RoundToInt(outgoing * lifeSteal);
            if (heal > 0) _health.RPC_Heal(heal);
        }

        if (fpsCamera != null) fpsCamera.Shake(0.2f);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayProjectile(Vector3 pos, Vector3 dir, float speed)
    {
        if (HasStateAuthority) return;   // 발사자는 이미 로컬로 만들었음

        var weapon = _equip != null ? _equip.GetWeapon() : null;
        if (weapon == null) return;

        SpawnProjectile(weapon, pos, dir, speed, 0f, false);
    }

    public float DrawRatio01
    {
        get
        {
            var w = _equip != null ? _equip.GetWeapon() : null;
            if (w == null || _drawTime < 0f) return 0f;
            return Mathf.Clamp01(_drawTime / Mathf.Max(0.01f, w.MaxDrawTime));
        }
    }   

    void TickAttack(float dt, bool pressed)
    {
        _attackTimer += dt;

        float activeStart = _current.WindUp;
        float activeEnd = _current.WindUp + _current.ActiveWindow;
        bool inWindow = _attackTimer >= activeStart && _attackTimer <= activeEnd;

        if (logHits)
            Debug.Log($"[Tick] 콤보{_comboIndex} t={_attackTimer:F3} " +
                      $"구간={activeStart:F3}~{activeEnd:F3} " +
                      $"window={inWindow} applied={_hitApplied} dash={_isDashAttack}");

        // ── 히트 판정 ──
        if (_isDashAttack)
        {
            if (inWindow && !_dashStarted)
            {
                _dashStarted = true;
                _ability.DoDash(_override.Dash);

            }

            if (inWindow || _ability.IsDashing) SweepDashHit(_current);
        }
        else
        {
            // 일반 공격: 한 번만. 구간을 놓쳤으면 늦게라도 발동
            bool missed = !_hitApplied && _attackTimer > activeEnd
                          && _attackTimer < _current.TotalTime;

            if (inWindow || missed) DoHitCheck(_current);
        }

        // ── 전진 (돌진 공격은 DoDash가 담당) ──
        if (!_isDashAttack && _current.LungeDistance > 0f && inWindow)
        {
            float speed = _current.LungeDistance / Mathf.Max(0.01f, _current.ActiveWindow);
            _cc.Move(transform.forward * speed * dt);
        }

        // ── 다음 입력 예약 (판정 종료 후부터) ──
        if (pressed && _attackTimer >= activeEnd)
            _queuedInput = true;

        // ── 종료 판정 ──
        var weapon = _equip.GetWeapon();
        bool isLastHit = weapon != null && _comboIndex >= weapon.ComboLength;

        // 돌진 공격과 마지막 타는 캔슬 불가
        float cancelAt = (_isDashAttack || isLastHit)
            ? _current.TotalTime
            : _current.CancelTime;

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

        _pendingDamageMul = 1f;
        _isDashAttack = false;
        _dashStarted = false;
        _override = AttackOverride.None;
        _hitThisAttack.Clear();

        if (_ability != null && _ability.TryOverrideAttack(out _override))
        {
            _pendingDamageMul = _override.DamageMul;
            _current = _override.Step ?? weapon.GetStep(0);
            if (_current == null) return;

            _isDashAttack = _override.HasDash;
            _comboIndex = 0;
        }
        else
        {
            _comboIndex %= weapon.ComboLength;
            _current = weapon.GetStep(_comboIndex);
            if (_current == null) return;
            _comboIndex++;
        }

        _attackTimer = 0f;
        _hitApplied = false;

        if (fpsCamera != null && !_isDashAttack)
            fpsCamera.FovKick(_current.CameraKickFov);

        RPC_PlaySwing(_isDashAttack ? -1 : _comboIndex - 1);
    }

    void ResetCombo()
    {
        _attackTimer = -1f;
        _comboIndex = 0;
        _queuedInput = false;
        _hitApplied = false;
        _pendingDamageMul = 1f;
        _current = null;
        _dashStarted = false;
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
        var weapon = _equip.GetWeapon();
        var src = (weapon != null && weapon.Class == WeaponClass.Ranged)
            ? DamageSource.RangedWeapon
            : DamageSource.MeleeWeapon;

        float baseDmg = _stats.Get(StatType.AttackPower)
                      * step.DamageMultiplier
                      * _pendingDamageMul;

        float outgoing = DamageCalc.CalcOutgoing(_stats, baseDmg, src, true, out bool crit);

        target.RPC_TakeDamage(DamageInfo.FromPlayerHit(
            Mathf.RoundToInt(outgoing), Object.InputAuthority, cameraRoot.position));

        _ability?.FireTrigger(TriggerType.OnHit, targetObj);

        float lifeSteal = _stats.Get(StatType.LifeSteal);
        if (lifeSteal > 0f)
        {
            int heal = Mathf.RoundToInt(outgoing * lifeSteal);
            if (heal > 0) _health.RPC_Heal(heal);
        }

        if (fpsCamera != null) fpsCamera.Shake(step.CameraShake);
        if (step.HitStopDuration > 0f)
            HitStop.Do(crit ? step.HitStopDuration * 1.5f : step.HitStopDuration);
    }

    //int CalcDamage(float mult)
    //{
    //    float atk = _stats.Get(StatType.AttackPower) * mult;

    //    if (Random.value < _stats.Get(StatType.CritChance))
    //        atk *= _stats.Get(StatType.CritDamage);

    //    return Mathf.Max(1, Mathf.RoundToInt(atk));
    //}

    // ══════════════════ 연출 동기화 ══════════════════

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlaySwing(int comboIndex)
    {
        ComboStep step;

        if (comboIndex < 0)
        {
            // 능력 대체 공격 (일격 등)
            var b = _ability != null ? _ability.FindAbility<EmpoweredStrikeAbility>() : null;
            step = b?.GetStrikeStep();
        }
        else
        {
            var weapon = _equip != null ? _equip.GetWeapon() : null;
            step = weapon?.GetStep(comboIndex);
        }

        if (step == null) return;

        weaponAnim?.Play(step);
        _events?.Play(step, HasStateAuthority);
    }

    void SweepDashHit(ComboStep step)
    {
        var weapon = _equip.GetWeapon();
        if (weapon == null) return;

        step.ResolveHitShape(weapon, out var mode, out float range,
                             out float angle, out float height, out float minRange);

        Vector3 origin = transform.position;
        Vector3 flatFwd = new Vector3(cameraRoot.forward.x, 0f, cameraRoot.forward.z).normalized;
        if (flatFwd.sqrMagnitude < 0.001f) flatFwd = transform.forward;

        Collider[] cols = Physics.OverlapSphere(origin, range, hitMask);
        float halfAngle = angle * 0.5f;

        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;

            int id = col.gameObject.GetInstanceID();
            if (_hitThisAttack.Contains(id)) continue;      // ★ 중복 방지

            if (!col.TryGetComponent<PlayerHealth>(out var target)) continue;
            if (target.IsDead) continue;

            Vector3 to = col.transform.position - origin;
            if (Mathf.Abs(to.y) > height) continue;

            Vector3 flat = new Vector3(to.x, 0f, to.z);
            float d = flat.magnitude;
            if (d > range || d < minRange) continue;

            if (mode == WeaponHitMode.ForwardArc && d > 0.01f)
                if (Vector3.Angle(flatFwd, flat.normalized) > halfAngle) continue;

            _hitThisAttack.Add(id);
            ApplyHit(target, col.gameObject, step);
        }
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