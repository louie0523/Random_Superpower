using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private WeaponAnimator weaponAnim;
    [SerializeField] private FPArmRig armRig;
    [SerializeField] private LayerMask hitMask;

    [Header("Sweep")]
    [Tooltip("한 틱을 몇 단계로 나눠 검사할지 (터널링 방지)")]
    [SerializeField] private int sweepSubSteps = 4;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // 컴포넌트
    private PlayerStats _stats;
    private PlayerHealth _health;
    private PlayerEquipment _equip;
    private AbilityRuntime _ability;
    private CharacterController _cc;

    // 콤보 상태
    private int _comboIndex;
    private float _attackTimer = -1f;
    private float _comboExpire;
    private bool _queuedInput;
    private ComboStep _current;

    // 스윕 상태
    private bool _sweepStarted;
    private bool _sphereHitApplied;
    private Vector3 _prevBladeStart, _prevBladeEnd;
    private readonly HashSet<int> _hitThisSwing = new();

    private Vector3 _prevCenter;
    private Quaternion _prevRot;

    // ══════════════════ 초기화 ══════════════════

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _equip = GetComponent<PlayerEquipment>();
        _ability = GetComponent<AbilityRuntime>();
        _cc = GetComponent<CharacterController>();

        if (armRig == null) armRig = GetComponentInChildren<FPArmRig>();
        if (weaponAnim == null) weaponAnim = GetComponentInChildren<WeaponAnimator>();

        if (cameraRoot == null) Debug.LogError("[Attack] cameraRoot 비었음", this);
        if (fpsCamera == null) Debug.LogError("[Attack] fpsCamera 비었음", this);
        if (armRig == null) Debug.LogError("[Attack] armRig 비었음", this);
    }

    // ══════════════════ 메인 루프 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (_health.IsDead)
        {
            ResetCombo();
            return;
        }

        float dt = Runner.DeltaTime;
        bool pressed = Input.GetMouseButtonDown(0);

        // 콤보 만료
        if (_attackTimer < 0f && _comboIndex > 0 &&
            Runner.SimulationTime > _comboExpire)
            _comboIndex = 0;

        if (_attackTimer >= 0f)
        {
            TickAttack(dt, pressed);
        }
        else if (pressed)
        {
            StartAttack();
        }
    }

    void TickAttack(float dt, bool pressed)
    {
        _attackTimer += dt;

        float activeStart = _current.WindUp;
        float activeEnd = _current.WindUp + _current.ActiveWindow;
        bool inActive = _attackTimer >= activeStart && _attackTimer <= activeEnd;

        // 히트 판정
        if (inActive) DoHitCheck(_current);
        else if (_sweepStarted && _attackTimer > activeEnd) EndSweep();

        // 전진
        if (_current.LungeDistance > 0f && inActive)
        {
            float speed = _current.LungeDistance / Mathf.Max(0.01f, _current.ActiveWindow);
            _cc.Move(transform.forward * speed * dt);
        }

        // 다음 입력 예약
        if (pressed && _attackTimer >= _current.CancelTime)
            _queuedInput = true;

        // 종료 판정
        bool canCancel = _attackTimer >= _current.CancelTime;
        if (_attackTimer >= _current.TotalTime || (canCancel && _queuedInput))
        {
            EndSweep();
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

        _comboIndex %= weapon.ComboLength;
        _current = weapon.GetStep(_comboIndex);
        if (_current == null) return;

        _attackTimer = 0f;
        _sphereHitApplied = false;
        EndSweep();
        _comboIndex++;

        if (weaponAnim != null) weaponAnim.Play(_current);
        if (fpsCamera != null) fpsCamera.FovKick(_current.CameraKickFov);

        RPC_PlaySwingFx(_comboIndex - 1);
    }

    void ResetCombo()
    {
        _attackTimer = -1f;
        _comboIndex = 0;
        _queuedInput = false;
        _current = null;
        EndSweep();
    }

    // ══════════════════ 히트 판정 ══════════════════

    void DoHitCheck(ComboStep step)
    {
        var weapon = _equip.GetWeapon();
        if (weapon == null) return;

        if (weapon.HitMode == WeaponHitMode.ForwardSphere)
        {
            if (!_sphereHitApplied)
            {
                _sphereHitApplied = true;
                SphereHit(weapon, step);
            }
            return;
        }
        else
        {
            if (!armRig.GetHitBox(weapon, out Vector3 c, out Vector3 half, out Quaternion rot))
                return;

            Gizmos.color = _sweepStarted ? Color.red : new Color(0.6f, 0.6f, 0.6f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(c, rot, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, half * 2f);
            Gizmos.matrix = Matrix4x4.identity;
        }

        SweepHit(weapon, step);
    }

    // ── 무기 궤적 판정 ──

    void SweepHit(EquipmentData weapon, ComboStep step)
    {
        if (armRig == null) return;
        if (!armRig.GetHitBox(weapon, out Vector3 c, out Vector3 half, out Quaternion rot))
            return;

        half *= step.RadiusMultiplier;

        if (!_sweepStarted)
        {
            _sweepStarted = true;
            _hitThisSwing.Clear();
            _prevCenter = c;
            _prevRot = rot;
            return;
        }

        int sub = Mathf.Max(1, sweepSubSteps);
        for (int i = 1; i <= sub; i++)
        {
            float t = (float)i / sub;
            Vector3 pos = Vector3.Lerp(_prevCenter, c, t);
            Quaternion r = Quaternion.Slerp(_prevRot, rot, t);

            CheckBox(pos, half, r, step);
            if (_hitThisSwing.Count >= step.MaxTargets) break;
        }

        _prevCenter = c;
        _prevRot = rot;
    }

    void CheckBox(Vector3 center, Vector3 half, Quaternion rot, ComboStep step)
    {
        Collider[] cols = Physics.OverlapBox(center, half, rot, hitMask);

        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;

            int id = col.gameObject.GetInstanceID();
            if (_hitThisSwing.Contains(id)) continue;

            if (!col.TryGetComponent<PlayerHealth>(out var target)) continue;
            if (target.IsDead) continue;

            _hitThisSwing.Add(id);
            ApplyHit(target, col.gameObject, step);

            if (_hitThisSwing.Count >= step.MaxTargets) return;
        }
    }


    void CheckCapsule(Vector3 a, Vector3 b, float radius, ComboStep step)
    {
        Collider[] cols = Physics.OverlapCapsule(a, b, radius, hitMask);

        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;

            int id = col.gameObject.GetInstanceID();
            if (_hitThisSwing.Contains(id)) continue;   // 한 스윙에 1회만

            if (!col.TryGetComponent<PlayerHealth>(out var target)) continue;
            if (target.IsDead) continue;

            _hitThisSwing.Add(id);
            ApplyHit(target, col.gameObject, step);

            if (_hitThisSwing.Count >= step.MaxTargets) return;
        }
    }

    // ── 정면 구체 판정 (마법/범위기용) ──

    void SphereHit(EquipmentData weapon, ComboStep step)
    {
        float range = weapon.Range * step.RangeMultiplier;
        float radius = weapon.HitRadius * step.RadiusMultiplier;

        Vector3 center = cameraRoot.position + cameraRoot.forward * (range * 0.5f);
        Collider[] cols = Physics.OverlapSphere(center, radius + range * 0.3f, hitMask);

        int hits = 0;
        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;
            if (!col.TryGetComponent<PlayerHealth>(out var target)) continue;
            if (target.IsDead) continue;

            ApplyHit(target, col.gameObject, step);

            if (++hits >= step.MaxTargets) break;
        }
    }

    // ── 공통 히트 처리 ──

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

    void EndSweep()
    {
        _sweepStarted = false;
        _hitThisSwing.Clear();
    }

    // ══════════════════ 연출 동기화 ══════════════════

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlaySwingFx(int comboIndex)
    {
        var weapon = _equip != null ? _equip.GetWeapon() : null;
        if (weapon == null) return;

        var step = weapon.GetStep(comboIndex);
        if (step == null) return;

        if (step.SwingSound != null)
            AudioSource.PlayClipAtPoint(step.SwingSound, transform.position, 0.7f);
    }

    // ══════════════════ 디버그 ══════════════════

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        if (armRig == null || _equip == null) return;

        var weapon = _equip.GetWeapon();
        if (weapon == null) return;

        var step = _current ?? weapon.GetStep(0);
        if (step == null) return;

        if (weapon.HitMode == WeaponHitMode.ForwardSphere)
        {
            if (cameraRoot == null) return;

            float range = weapon.Range * step.RangeMultiplier;
            float radius = weapon.HitRadius * step.RadiusMultiplier;
            Vector3 center = cameraRoot.position + cameraRoot.forward * (range * 0.5f);

            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.25f);
            Gizmos.DrawSphere(center, radius + range * 0.3f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(center, radius + range * 0.3f);
        }
        else
        {
            if (!armRig.GetHitBox(weapon, out Vector3 c, out Vector3 half, out Quaternion rot))
                return;

            half *= step.RadiusMultiplier;

            Gizmos.color = _sweepStarted ? Color.red : new Color(0.6f, 0.6f, 0.6f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(c, rot, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, half * 2f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}