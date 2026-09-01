using UnityEngine;

public class FPSCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraRoot;   // 피치 회전
    [SerializeField] private Transform cameraHolder; // 헤드밥 오프셋 (= PlayerCamera)
    [SerializeField] private Camera cam;

    [Header("Look")]
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float pitchMin = -85f;
    [SerializeField] private float pitchMax = 85f;

    [Header("Head Bob")]
    [SerializeField] private float walkBobFreq = 1.8f;
    [SerializeField] private float walkBobAmpY = 0.05f;
    [SerializeField] private float walkBobAmpX = 0.03f;
    [SerializeField] private float runBobFreq = 2.6f;
    [SerializeField] private float runBobAmpY = 0.09f;
    [SerializeField] private float runBobAmpX = 0.05f;

    [Header("FOV")]
    [SerializeField] private float baseFov = 70f;
    [SerializeField] private float runFovAdd = 10f;
    [SerializeField] private float fovLerp = 8f;

    [Header("Lean / Land")]
    [SerializeField] private float strafeRoll = 2f;
    [SerializeField] private float landDipAmount = 0.15f;
    [SerializeField] private float landRecoverSpeed = 6f;

    private float _pitch;
    private float _bobTimer;
    private float _landOffset;
    private float _roll;
    private float _targetFov;
    private float _kickFov;
    private float _yawAccum;

    void Awake()
    {
        _targetFov = baseFov;
        if (cam != null) cam.fieldOfView = baseFov;
    }

    public void Init()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>PlayerMovement가 매 프레임 호출</summary>
    public void Tick(Vector3 localMoveDir, bool isRunning, float speed01)
    {
        HandleLook();
        HandleBob(localMoveDir, isRunning, speed01);
        HandleFov(isRunning);
    }

    void HandleLook()
    {
        float mx = Input.GetAxisRaw("Mouse X") * sensitivity * 10f;
        float my = Input.GetAxisRaw("Mouse Y") * sensitivity * 10f;

        _yawAccum += mx;   // 쌓아두기만

        _pitch = Mathf.Clamp(_pitch - my, pitchMin, pitchMax);
        cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    public float ConsumeYaw()
    {
        float y = _yawAccum;
        _yawAccum = 0f;
        return y;
    }


    void HandleBob(Vector3 moveDir, bool isRunning, float speed01)
    {
        float freq = isRunning ? runBobFreq : walkBobFreq;
        float ampY = isRunning ? runBobAmpY : walkBobAmpY;
        float ampX = isRunning ? runBobAmpX : walkBobAmpX;

        Vector3 bob = Vector3.zero;

        if (speed01 > 0.1f)
        {
            _bobTimer += Time.deltaTime * freq * Mathf.PI * 2f;
            bob.y = Mathf.Sin(_bobTimer) * ampY * speed01;
            bob.x = Mathf.Cos(_bobTimer * 0.5f) * ampX * speed01;
        }
        else
        {
            _bobTimer = Mathf.Lerp(_bobTimer, 0f, Time.deltaTime * 4f);
        }

        // 착지 딥 복구
        _landOffset = Mathf.Lerp(_landOffset, 0f, Time.deltaTime * landRecoverSpeed);
        bob.y += _landOffset;

        cameraHolder.localPosition = Vector3.Lerp(
            cameraHolder.localPosition, bob, Time.deltaTime * 12f);

        // 좌우 이동 시 롤
        float targetRoll = -moveDir.x * strafeRoll;
        _roll = Mathf.Lerp(_roll, targetRoll, Time.deltaTime * 6f);
        cameraHolder.localRotation = Quaternion.Euler(0f, 0f, _roll);
    }

    void HandleFov(bool isRunning)
    {
        _targetFov = baseFov + (isRunning ? runFovAdd : 0f);
        _kickFov = Mathf.Lerp(_kickFov, 0f, Time.deltaTime * 4f);
        cam.fieldOfView = Mathf.Lerp(
            cam.fieldOfView, _targetFov + _kickFov, Time.deltaTime * fovLerp);
    }

    /// <summary>대시, 스킬 등에서 호출</summary>
    public void FovKick(float amount)
    {
        _kickFov = amount;
    }

    /// <summary>착지 시 호출</summary>
    public void LandDip(float force01)
    {
        _landOffset = -landDipAmount * Mathf.Clamp01(force01);
    }
}