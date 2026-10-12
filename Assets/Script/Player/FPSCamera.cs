using UnityEngine;

public class FPSCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraRoot;   // 피치 회전
    [SerializeField] private Transform cameraHolder; // 헤드밥 오프셋
    [SerializeField] private Camera cam;

    [Header("Look")]
    [SerializeField] private float sensitivity = 0.3f;
    [SerializeField] private float pitchMin = -85f;
    [SerializeField] private float pitchMax = 85f;

    [Header("Head Bob")]
    [SerializeField] private float walkBobFreq = 1.8f;
    [SerializeField] private float walkBobAmpY = 0.05f;
    [SerializeField] private float walkBobAmpX = 0.05f;
    [SerializeField] private float walkBobRoll = 0.5f;

    [Header("FOV")]
    [SerializeField] private float baseFov = 75f;
    [Tooltip("이 속도에서 FOV 추가량이 최대가 됨")]
    [SerializeField] private float fovMaxSpeed = 12f;
    [Tooltip("최대 FOV 추가량")]
    [SerializeField] private float fovMaxAdd = 10f;
    [SerializeField] private float fovLerp = 6f;

    [Header("Lean / Land")]
    [SerializeField] private float strafeRoll = 2f;
    [SerializeField] private float landDipAmount = 0.15f;
    [SerializeField] private float landRecoverSpeed = 6f;

    [Header("Hit Shake")]
    [SerializeField] private float shakeDecay = 5f;
    [SerializeField] private float shakeSpeed = 30f;
    [SerializeField] private float hitPunchAmount = 6f;


    // 모션 카메라 오프셋
    private Vector3 _motionPos;
    private Vector3 _motionRot;
    private float _motionFov;
    private float _visualYaw;

    // 내부 상태
    private float _pitch;
    private float _yawAccum;
    private float _bobTimer;
    private float _landOffset;
    private float _roll;
    private float _targetFov;
    private float _kickFov;
    private float _shakeAmount;
    private Vector2 _shakeOffset;
    private Vector2 _punch;
    private Vector2 _punchVel;

    private float _abilityLean;
    private float _abilityLeanTarget;
    private float _leanSpeed = 10f;

    private float _recoilPitch;
    public float Pitch => _pitch;
    private float _zoomFov;

    private float _heightOffset;
    public void SetHeightOffset(float y) => _heightOffset = y;

    /// <summary>지속 줌 (음수 = 확대). 0이면 해제</summary>
    public void SetZoom(float fovAdd) => _zoomFov = fovAdd;

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

    /// <summary>PlayerMovement의 Update가 호출</summary>
    /// <summary>speed는 현재 실제 이동 속도 (m/s)</summary>
    public void Tick(Vector3 localMoveDir, float speed, float speed01)
    {
        HandleLook();
        HandleBob(localMoveDir, speed01);
        HandleFov(speed);
    }

    public void SetMotionFov(float fovAdd)
    {
        _motionFov = fovAdd;
    }

    /// <summary>PlayerMovement의 FixedUpdateNetwork가 호출</summary>
    public float ConsumeYaw()
    {
        float y = _yawAccum;
        _yawAccum = 0f;
        return y;
    }

    /// <summary>몸통이 회전한 만큼 시각 요에서 빼기</summary>
    public void OnBodyRotated(float degrees)
    {
        _visualYaw -= degrees;
    }

    // ---------------- 시점 ----------------

    /// <summary>3인칭 카메라용 — 피격 반동·흔들림만 (헤드밥·롤 제외)</summary>
    public Vector3 ShakeEuler => new(_punch.y + _shakeOffset.y * 2f, _punch.x + _shakeOffset.x * 2f, _shakeOffset.x * 3f);

    void HandleLook()
    {
        if (UIManager.InputBlocked) return;

        float mx = Input.GetAxisRaw("Mouse X") * sensitivity * 10f;
        float my = Input.GetAxisRaw("Mouse Y") * sensitivity * 10f;

        if (_recoilPitch > 0.01f)
        {
            float back = Mathf.Min(_recoilPitch, Time.deltaTime * 12f);
            _pitch += back;
            _recoilPitch -= back;
        }

        if (my > 0f) _recoilPitch = Mathf.Max(0f, _recoilPitch - my);

        _yawAccum += mx;
        _visualYaw += mx;

        _pitch = Mathf.Clamp(_pitch - my, pitchMin, pitchMax);
        cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }


    // ---------------- 몸기울임 ----------------
    //public void SetLean(float degrees, float speed = 10f)
    //{
    //    _abilityLeanTarget = degrees;
    //    _leanSpeed = speed;
    //}

    /// <summary>모션이 매 프레임 갱신</summary>
    public void SetMotionOffset(Vector3 pos, Vector3 rot, float fovAdd)
    {
        _motionPos = pos;
        _motionRot = rot;
        _motionFov = fovAdd;
    }

    public void ClearMotionOffset()
    {
        _motionPos = Vector3.zero;
        _motionRot = Vector3.zero;
        _motionFov = 0f;
    }

    // ---------------- 헤드밥 ----------------

    void HandleBob(Vector3 moveDir, float speed01)
    {
        float freq = walkBobFreq;
        float ampY = walkBobAmpY;
        float ampX = walkBobAmpX;
        float roll = walkBobRoll;



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

        // 착지 딥
        _landOffset = Mathf.Lerp(_landOffset, 0f, Time.deltaTime * landRecoverSpeed);
        bob.y += _landOffset;

        // 위치 적용
        cameraHolder.localPosition = Vector3.Lerp(
                  cameraHolder.localPosition, bob + _motionPos + Vector3.up * _heightOffset, Time.deltaTime * 12f);



        // 셰이크 감쇠
        _shakeAmount = Mathf.Lerp(_shakeAmount, 0f, Time.deltaTime * shakeDecay);
        if (_shakeAmount > 0.001f)
        {
            float t = Time.time * shakeSpeed;
            _shakeOffset.x = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * _shakeAmount;
            _shakeOffset.y = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * _shakeAmount;
        }
        else
        {
            _shakeOffset = Vector2.zero;
        }

        // 피격 반동 (스프링)
        _punchVel = Vector2.Lerp(_punchVel, -_punch * 30f, Time.deltaTime * 12f);
        _punch += _punchVel * Time.deltaTime;
        _punch = Vector2.Lerp(_punch, Vector2.zero, Time.deltaTime * 6f);

        // 롤
        float stepRoll = Mathf.Cos(_bobTimer * 0.5f) * roll * speed01;
        float targetRoll = -moveDir.x * strafeRoll + stepRoll;
        _roll = Mathf.Lerp(_roll, targetRoll, Time.deltaTime * 6f);

        // 최종 회전
        _abilityLean = Mathf.Lerp(_abilityLean, _abilityLeanTarget, Time.deltaTime * _leanSpeed);

        cameraHolder.localRotation = Quaternion.Euler(
            _punch.y + _shakeOffset.y * 2f + _motionRot.x,
            _punch.x + _shakeOffset.x * 2f + _motionRot.y,
            _roll + _shakeOffset.x * 3f + _abilityLean + _motionRot.z
        );
    }



    // ---------------- FOV ----------------

    void HandleFov(float speed)
    {
        float t = Mathf.Clamp01(speed / Mathf.Max(0.01f, fovMaxSpeed));
        _targetFov = baseFov + fovMaxAdd * t + _motionFov + _zoomFov;

        _kickFov = Mathf.Lerp(_kickFov, 0f, Time.deltaTime * 4f);
        cam.fieldOfView = Mathf.Lerp(
            cam.fieldOfView, _targetFov + _kickFov, Time.deltaTime * fovLerp);
    }

    // ---------------- 외부 호출 ----------------

    /// <summary>대시, 공격 등</summary>
    public void FovKick(float amount)
    {
        _kickFov = amount;
    }

    /// <summary>착지</summary>
    public void LandDip(float force01)
    {
        _landOffset = -landDipAmount * Mathf.Clamp01(force01);
    }

    /// <summary>피격. dirLocal은 맞은 방향(-1~1)</summary>
    public void HitReact(float intensity01, Vector2 dirLocal)
    {
        _shakeAmount = Mathf.Max(_shakeAmount, 1.5f * intensity01);
        _punchVel += dirLocal * hitPunchAmount * intensity01;
        FovKick(-5f * intensity01);
    }

    /// <summary>폭발 등 방향 없는 흔들림</summary>
    public void Shake(float amount)
    {
        _shakeAmount = Mathf.Max(_shakeAmount, amount);
    }

    /// <summary>총기 반동. 위로 튀고 좌우로 흔들림</summary>
    public void Recoil(float vertical, float horizontal)
    {
        _punchVel.y += vertical;
        _punchVel.x += Random.Range(-horizontal, horizontal);

        float add = vertical * 0.35f;
        _pitch = Mathf.Clamp(_pitch - add, pitchMin, pitchMax);
        _recoilPitch += add;
    }
}