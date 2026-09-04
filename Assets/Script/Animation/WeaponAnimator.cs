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

    private Vector3 _swayPos, _swayRot;
    private float _bobTimer;

    // 스윙 상태
    private ComboStep _step;
    private float _t = -1f;

    public bool IsAttacking => _t >= 0f;
    public float Progress01 => _step == null ? 0f : Mathf.Clamp01(_t / _step.TotalTime);

    void Awake()
    {
        if (armRig == null) armRig = GetComponentInChildren<FPArmRig>();
    }

    /// <summary>공격 시작</summary>
    public void Play(ComboStep step)
    {
        _step = step;
        _t = 0f;
    }


    /// <summary>원격 플레이어용. 스윙 포즈만 재생 (스웨이/바빙 제외)</summary>
    public void TickRemote()
    {
        if (armRig == null) return;
        HandleSwingPose();
        armRig.ApplyOffset(Vector3.zero, Vector3.zero);
    }
    public void StopSwing()
    {
        _t = -1f;
        _step = null;
        if (armRig != null) armRig.ApplyPose(armRig.IdlePose);
    }

    /// <summary>PlayerMovement의 Update에서 호출</summary>
    public void Tick(Vector3 moveDir, bool isRunning, float speed01)
    {
        if (armRig == null) return;

        HandleSwingPose();
        HandleArmOffset(isRunning, speed01);
    }

    // ── 스윙 포즈 (손/무기) ──

    void HandleSwingPose()
    {
        if (_t < 0f || _step == null)
        {
            armRig.ApplyPose(armRig.IdlePose);
            return;
        }

        _t += Time.deltaTime;

        if (_t >= _step.TotalTime)
        {
            _t = -1f;
            _step = null;
            armRig.ApplyPose(armRig.IdlePose);
            return;
        }

        float p = _t / _step.TotalTime;
        var pose = _step.Motion.Evaluate(p, armRig.IdlePose);
        Debug.Log($"[Swing] p={p:F2} keys={_step.Motion.Keys?.Count} pos={pose.RightPos}");

        armRig.ApplySwing(_step.Motion, p);
    }

    // ── 스웨이 / 바빙 (ArmRoot 전체) ──

    void HandleArmOffset(bool isRunning, float speed01)
    {
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");

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

        Vector3 bobPos = Vector3.zero;
        Vector3 bobRot = Vector3.zero;

        if (speed01 > 0.1f)
        {
            float freq = isRunning ? runBobFreq : walkBobFreq;
            float amp = isRunning ? bobAmount * 1.8f : bobAmount;

            _bobTimer += Time.deltaTime * freq * Mathf.PI * 2f;

            bobPos.x = Mathf.Cos(_bobTimer * 0.5f) * amp;
            bobPos.y = Mathf.Sin(_bobTimer) * amp * 0.6f;
            bobRot.z = Mathf.Cos(_bobTimer * 0.5f) * bobRotAmount * (isRunning ? 1.8f : 1f);
            bobRot.x = Mathf.Sin(_bobTimer) * bobRotAmount * 0.4f;
        }
        else
        {
            float t = Time.time * breathSpeed;
            bobPos.y = Mathf.Sin(t) * breathAmount;
            bobRot.x = Mathf.Sin(t * 0.8f) * 0.5f;
        }

        armRig.ApplyOffset(_swayPos + bobPos, _swayRot + bobRot);
    }
}