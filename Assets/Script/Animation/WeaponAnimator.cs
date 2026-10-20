using UnityEngine;

/// <summary>1인칭 팔 전체의 스웨이/바빙. 포즈는 FPArmRig가 담당.</summary>
public class WeaponAnimator : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private FPArmRig armRig;

    [Header("Sway")]
    [SerializeField] private float swayAmount = 0.02f;
    [SerializeField] private float swayRotAmount = 4f;
    [SerializeField] private float swaySmooth = 8f;
    [SerializeField] private float swayMaxOffset = 0.06f;

    [Header("Bob")]
    [SerializeField] private float bobAmount = 0.015f;
    [SerializeField] private float bobRotAmount = 1.5f;
    [SerializeField] private float walkBobFreq = 1.8f;
    [SerializeField] private float runBobFreq = 3.0f;

    [Header("Idle Breath")]
    [SerializeField] private float breathAmount = 0.006f;
    [SerializeField] private float breathSpeed = 1.2f;

    [Header("Blend")]
    [Tooltip("모션 종료 후 Idle로 돌아오는 시간 (초)")]
    [SerializeField] private float blendOutTime = 0.15f;

    [Tooltip("모션 시작 시 현재 자세에서 섞이는 시간 (초)")]
    [SerializeField] private float blendInTime = 0.08f;

    private ArmPose _blendFrom;
    private float _blendTimer;
    private float _blendDuration;

    private Vector3 _swayPos, _swayRot;
    private float _bobTimer;

    // 스윙 상태
    private ComboStep _step;
    private float _t = -1f;

    public bool IsAttacking => _t >= 0f;
    public float Progress01 => _step == null ? 0f : Mathf.Clamp01(_t / _step.TotalTime);

    private float _freezeTimer;

    private float _drawAmount;

    private float _bobWeight;
    private float _speed = 1f;


    public FPArmRig ArmRig => armRig;
    public void SetDraw(float t01) => _drawAmount = t01;

    public void Freeze(float d) => _freezeTimer = Mathf.Max(_freezeTimer, d);

    void Awake()
    {
        if (armRig == null) armRig = GetComponentInChildren<FPArmRig>();
    }

    /// <summary>공격 시작</summary>

    public void Play(ComboStep step, float speed = 1f)
    {
        _blendFrom = CaptureCurrent();
        _blendTimer = blendInTime;
        _blendDuration = blendInTime;

        _freezeTimer = 0f;
        _speed = Mathf.Max(0.1f, speed);

        if (armRig != null)
        {
            if (step != null && step.WeaponOverride != null) armRig.BeginWeaponOverride(step.WeaponOverride);
            else armRig.EndWeaponOverride();
            armRig.SetWeaponHidden(step != null && step.HideWeapon);
        }

        _step = step;
        _t = 0f;
    }


    ArmPose CaptureCurrent()
    {
        var p = armRig.GetEffectiveIdle().Clone();

        if (armRig.RightHand != null)
        {
            p.RightPos = armRig.RightHand.localPosition;
            p.RightRot = armRig.RightHand.localRotation.eulerAngles;
        }
        if (armRig.LeftHand != null)
        {
            p.LeftPos = armRig.LeftHand.localPosition;
            p.LeftRot = armRig.LeftHand.localRotation.eulerAngles;
        }

        armRig.GetBodyOffset(out p.BodyPos, out p.BodyRot, out p.HeadPos, out p.HeadRot);
        return p;
    }



    /// <summary>원격 플레이어용. 스윙 포즈만 재생 (스웨이/바빙 제외)</summary>
    public void Tick(Vector3 moveDir, float speed01)
    {
        if (armRig == null) return;

        HandleSwingPose();
        HandleArmOffset(speed01);
    }
    public void StopSwing()
    {
        armRig?.EndWeaponOverride();
        armRig?.SetWeaponHidden(false);
        _t = -1f;
        _step = null;
        if (armRig != null) armRig.ApplyPose(armRig.IdlePose);
    }

    public void TickRemote()
    {
        if (armRig == null) return;

        HandleSwingPose();
        armRig.ApplyOffset(Vector3.zero, Vector3.zero);
    }

    // ── 스윙 포즈 (손/무기) ──

    void HandleSwingPose()
    {   
        float dt = Time.deltaTime;

        // ── 모션 없음: 블렌드 아웃 중이거나 Idle ──
        if (_t < 0f || _step == null)
        {
            armRig.EndWeaponOverride();
            armRig.SetWeaponHidden(false);

            armRig.SetDrawAmount(_drawAmount, dt);
            var idle = armRig.GetEffectiveIdle();

          //  Debug.Log($"[Swing] draw={_drawAmount:F2} " +
          //$"left={idle.LeftPos} right={idle.RightPos} " +
          //$"blendTimer={_blendTimer:F2}");

            if (_blendTimer > 0f && _blendFrom != null)
            {
                _blendTimer -= dt;
                float t = 1f - Mathf.Clamp01(_blendTimer / Mathf.Max(0.01f, _blendDuration));
                t = Mathf.SmoothStep(0f, 1f, t);

                armRig.ApplyPose(ArmPose.Lerp(_blendFrom, idle, t));
                if (_blendTimer <= 0f) _blendFrom = null;
            }
            else
            {
                armRig.ApplyPose(idle);
            }
            return;
        }

        // ── 모션 재생 ──
        armRig.SetWeaponHidden(_step.HideWeapon);
        if (_freezeTimer > 0f) _freezeTimer -= dt;
        else _t += dt * _speed;

        if (_t >= _step.TotalTime)
        {
            // 종료 → 블렌드 아웃 시작
            _blendFrom = CaptureCurrent();
            _blendTimer = blendOutTime;
            _blendDuration = blendOutTime;

            _t = -1f;
            _step = null;

            armRig.ApplyPose(_blendFrom);
            return; 
        }

        var pose = _step.Motion != null && _step.Motion.IsValid
            ? _step.Motion.Evaluate(_t / _step.TotalTime, armRig.GetEffectiveIdle())
            : armRig.GetEffectiveIdle();

        pose.HandOverride = _step.HandMode;

        // ── 블렌드 인 ──
        if (_blendTimer > 0f && _blendFrom != null)
        {
            _blendTimer -= dt;
            float t = 1f - Mathf.Clamp01(_blendTimer / Mathf.Max(0.01f, _blendDuration));
            t = Mathf.SmoothStep(0f, 1f, t);

            pose = ArmPose.Lerp(_blendFrom, pose, t);
            pose.HandOverride = _step.HandMode;

            if (_blendTimer <= 0f) _blendFrom = null;
        }

        armRig.SetDrawAmount(_drawAmount, dt);
        armRig.ApplyPose(pose);
    }

    // ── 스웨이 / 바빙 (ArmRoot 전체) ──

    void HandleArmOffset(float speed01)
    {
        bool blocked = UIManager.InputBlocked;
        float mx = blocked ? 0f : Input.GetAxisRaw("Mouse X");
        float my = blocked ? 0f : Input.GetAxisRaw("Mouse Y");

        Vector3 targetSwayPos = new(
            Mathf.Clamp(-mx * swayAmount, -swayMaxOffset, swayMaxOffset),
            Mathf.Clamp(-my * swayAmount, -swayMaxOffset, swayMaxOffset),
            0f);

        Vector3 targetSwayRot = new(
            my * swayRotAmount,
            mx * swayRotAmount,
            -mx * swayRotAmount);

        _swayPos = Vector3.Lerp(_swayPos, targetSwayPos, Time.deltaTime * swaySmooth);
        _swayRot = Vector3.Lerp(_swayRot, targetSwayRot, Time.deltaTime * swaySmooth);

        float targetWeight = speed01 > 0.1f ? 1f : 0f;
        _bobWeight = Mathf.MoveTowards(_bobWeight, targetWeight, Time.deltaTime * 4f);

        Vector3 bobPos = Vector3.zero;
        Vector3 bobRot = Vector3.zero;

        if (_bobWeight > 0.001f)
        {
            _bobTimer += Time.deltaTime * walkBobFreq * Mathf.PI * 2f;

            bobPos.x = Mathf.Cos(_bobTimer * 0.5f) * bobAmount * _bobWeight;
            bobPos.y = Mathf.Sin(_bobTimer) * bobAmount * 0.6f * _bobWeight;
            bobRot.z = Mathf.Cos(_bobTimer * 0.5f) * bobRotAmount * _bobWeight;
            bobRot.x = Mathf.Sin(_bobTimer) * bobRotAmount * 0.4f * _bobWeight;
        }

        // 숨쉬기는 바빙이 약할 때만
        float breathWeight = 1f - _bobWeight;
        if (breathWeight > 0.001f)
        {
            float t = Time.time * breathSpeed;
            bobPos.y += Mathf.Sin(t) * breathAmount * breathWeight;
            bobRot.x += Mathf.Sin(t * 0.8f) * 0.5f * breathWeight;
        }

        armRig.ApplyOffset(_swayPos + bobPos, _swayRot + bobRot);
    }
}