using Fusion;
using UnityEngine;
using static AbilityRuntime;

public class PlayerMovement : NetworkBehaviour
{
    [Header("View")]
    [SerializeField] private GameObject bodyRoot;
    [SerializeField] private GameObject armRoot;

    [Header("Move")]
    [SerializeField] private float jumpPower = 6f;
    [SerializeField] private float gravity = -20f;

    [Header("Refs")]
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private GameObject[] hideOnLocal; // 내 몸통 메시

    [Header("넉백")]
    [SerializeField] private float knockbackDecay = 18f;
    private Vector3 _push;

    [Header("Debug")]
    public bool IsDummy = false;

    private CharacterController _cc;
    private PlayerStatus _status;
    private float _verticalVel;
    private bool _wasGrounded = true;
    private Vector3 _localMoveDir;
    private bool _isRunning;
    private float _speed01;
    private PlayerStats _stats;
    [SerializeField] private WeaponAnimator weaponAnim;
    private PlayerAnimator _anim;

    private float _currentSpeed;
    /// <summary>이 시각(Time.time)까지 이동 · 점프 막음 — 착지 동작 등</summary>
    public float LockMoveUntil { get; set; }

    public bool IsMoving => _speed01 > 0.1f;
    public Vector3 LocalMoveDir => _localMoveDir;
    private AbilityRuntime _ability;
    private Vector3 _smoothMoveDir;
    public Vector3 SmoothMoveDir => _smoothMoveDir;

    private PlayerShop _shop;
    private PlayerEquipment _equip;
    /// <summary>능력이 잠시 이동 · 점프를 막음 (거신병 도약 중 등)</summary>
    public bool InputLocked { get; set; }
    /// <summary>능력이 잠시 정하는 '내려올 때 중력 배율' (0 = 안 씀 → AlterData Fall Gravity). 거신병 도약 등</summary>
    public float FallGravityOverride { get; set; }
    private float _jumpAt;              // 준비 동작 뒤 뜰 시각 (0 = 없음) — 거신병 점프 이륙 프레임
    private AlterClipAnimator _clipAnim;
    /// <summary>점프 준비 동작 중 (아직 땅이지만 곧 뜸)</summary>
    public bool JumpPending => _jumpAt > 0f;
    private PlayerAttack _attackRef;
    private PlayerAlter _alterRef;

    /// <summary>비행 중 — 중력 대신 FlyUpSpeed로 올라감 (변신 능력이 켬)</summary>
    public bool Flying { get; set; }
    public float FlyUpSpeed { get; set; } = 6f;
    public bool Grounded => _cc != null && _cc.isGrounded;
    /// <summary>공중 — 땅에서 떨어졌거나, 막 뛰어서 올라가는 중 (뛴 틱엔 isGrounded가 아직 true라서)</summary>
    public bool IsAirborne => _cc != null && (!_cc.isGrounded || _verticalVel > 0f || _jumpAt > 0f);
    /// <summary>Space를 누르고 있는가 (내 캐릭터)</summary>
    public bool JumpHeld => HasInputAuthority && !UIManager.InputBlocked && Input.GetKey(KeyCode.Space);
    /// <summary>점프한 순간 (내 캐릭터) — 변신 모델이 점프 동작을 바로 틀게</summary>
    public event System.Action Jumped;


    public override void Spawned()
    {
        _cc = GetComponent<CharacterController>();
        _stats = GetComponent<PlayerStats>();
        _anim = GetComponent<PlayerAnimator>();
        _ability = GetComponent<AbilityRuntime>();
        _shop = GetComponent<PlayerShop>();
        _equip = GetComponent<PlayerEquipment>();
        _status = GetComponent<PlayerStatus>();
        _alterRef = GetComponent<PlayerAlter>();
        _attackRef = GetComponent<PlayerAttack>();

        if (armRoot != null) armRoot.SetActive(true);   // 항상 켬

        if (HasInputAuthority)
        {
            fpsCamera.Init();
            HitStop.Register(weaponAnim);
            var cam = fpsCamera.GetComponentInChildren<Camera>(true);
            UIManager.Instance?.BindLocalPlayer(Object, cam);
        }
        else
        {
            // 오브젝트가 아니라 Camera 컴포넌트만 비활성화
            var cam = fpsCamera.GetComponentInChildren<Camera>(true);
            if (cam != null) cam.enabled = false;

            var listener = fpsCamera.GetComponentInChildren<AudioListener>(true);
            if (listener != null) listener.enabled = false;

            fpsCamera.enabled = false;
        }

        Debug.Log($"[Movement] weaponAnim id={weaponAnim?.GetInstanceID()}");
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        //transform.Rotate(Vector3.up * fpsCamera.ConsumeYaw());

        float yaw = fpsCamera.ConsumeYaw();
        transform.Rotate(Vector3.up * yaw);
        fpsCamera.OnBodyRotated(yaw);
        fpsCamera.SetBodyYaw(transform.eulerAngles.y);

        if ((_shop != null && _shop.IsShopping) || UIManager.InputBlocked)
        {
            _localMoveDir = Vector3.zero;
            _speed01 = 0f;
            _currentSpeed = 0f;
            _verticalVel += gravity * Runner.DeltaTime;
            _cc.Move(Vector3.up * _verticalVel * Runner.DeltaTime);
            return;
        }

        bool blocked = UIManager.InputBlocked || (_status != null && (_status.IsStunned || _status.IsRooted));
        float h = HasInputAuthority ? Input.GetAxisRaw("Horizontal") : 0f;
        float v = HasInputAuthority ? Input.GetAxisRaw("Vertical") : 0f;

        if (_status != null && (_status.IsStunned || _status.IsRooted)) { h = 0f; v = 0f; }

        if (_ability != null)
        {
            switch (_ability.MoveLock)
            {
                case MoveLockMode.StrafeOnly: v = 0f; break;   // 꽃게
                case MoveLockMode.ForwardOnly: h = 0f; break;
            }
        }

        // 능력 · 착지 동작 · 발 묶이는 공격 — 이동 · 점프 막기
        if (_attackRef == null) _attackRef = GetComponent<PlayerAttack>();
        if (_alterRef == null) _alterRef = GetComponent<PlayerAlter>();
        if (InputLocked || Time.time < LockMoveUntil || (_attackRef != null && _attackRef.AttackRooted) || _jumpAt > 0f)
        { h = 0f; v = 0f; blocked = true; }

        // 땅에서만 공격하는 변신 — 공격 중엔 점프 못 함 (공중 공격이 되지 않게)
        if (_attackRef != null && _attackRef.IsAttacking && _alterRef != null && _alterRef.GroundOnlyAttack)
            blocked = true;

        _localMoveDir = new Vector3(h, 0f, v).normalized;

        float speed = _stats.Get(StatType.MoveSpeed);
        _speed01 = _localMoveDir.magnitude;
        _currentSpeed = speed * _speed01;

        Vector3 worldDir = transform.TransformDirection(_localMoveDir);

        bool grounded = _cc.isGrounded;

        if (grounded && !_wasGrounded)
            fpsCamera.LandDip(Mathf.Clamp01(-_verticalVel / 12f));

        if (grounded && _verticalVel < 0f) _verticalVel = -2f;
        if (_jumpAt > 0f)
        {
            // 준비 동작 중 — 기절 · 떨어짐 · 비행이면 취소, 이륙 프레임이 되면 뜸
            if (!grounded || Flying || (_status != null && (_status.IsStunned || _status.IsRooted))) _jumpAt = 0f;
            else if (Runner.SimulationTime >= _jumpAt) { _jumpAt = 0f; _verticalVel = jumpPower * JumpMul(); }
        }
        else if (HasInputAuthority && grounded && !blocked && !Flying && Input.GetKey(KeyCode.Space))
        {
            float wait = JumpWait();
            if (wait > 0.001f) _jumpAt = Runner.SimulationTime + wait;      // 준비 동작 먼저
            else _verticalVel = jumpPower * JumpMul();
            Jumped?.Invoke();
        }

        bool dashNoGravity = _ability != null && _ability.DashIgnoresGravity;

        if (Flying)
        {
            _verticalVel = FlyUpSpeed;
        }
        else if (!dashNoGravity)
        {
            _verticalVel += gravity * (_verticalVel < 0f ? FallMul() : 1f) * Runner.DeltaTime;   // 내려올 때만 배율
        }
        else
        {
            _verticalVel = 0f;
        }

        Vector3 motion = worldDir * speed + Vector3.up * _verticalVel;

        if (_ability != null && _ability.IsDashing)
            motion = dashNoGravity ? Vector3.zero : Vector3.up * _verticalVel;

        motion += _push;
        _push = Vector3.MoveTowards(_push, Vector3.zero, knockbackDecay * Runner.DeltaTime);

        _cc.Move(motion * Runner.DeltaTime);

        _anim?.SetMovement(_speed01, _cc.isGrounded, fpsCamera.Pitch);




    }

    /// <summary>내려올 때 중력 배율 — 능력이 정한 값 우선, 없으면 붙은 변화(AlterData)</summary>
    float FallMul()
    {
        if (FallGravityOverride > 0f) return FallGravityOverride;
        if (_alterRef == null) _alterRef = GetComponent<PlayerAlter>();
        return _alterRef != null ? _alterRef.FallGravityMultiplier : 1f;
    }

    /// <summary>변신 모델(AlterClipAnimator)의 점프 준비 시간 — Leap 상태 Hit Frame. 없으면 0</summary>
    float JumpWait()
    {
        if (_clipAnim == null || !_clipAnim.isActiveAndEnabled) _clipAnim = GetComponentInChildren<AlterClipAnimator>();
        return _clipAnim != null ? _clipAnim.JumpTakeoff() : 0f;
    }

    float JumpMul()
    {
        if (_alterRef == null) _alterRef = GetComponent<PlayerAlter>();
        return _alterRef != null ? _alterRef.JumpMultiplier : 1f;
    }



    void Update()
    {
        if (!HasInputAuthority) { weaponAnim?.TickRemote(); return; }


        if (UIManager.InputBlocked)
        {
            _localMoveDir = Vector3.zero;        // 이동 입력 해제
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.G)) _equip?.DropActiveWeapon();

            for (int k = 0; k < PlayerEquipment.WeaponSlots; k++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + k)) _equip?.SwitchWeapon(k);

            float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
            if (wheel > 0.01f) _equip?.CycleWeapon(1);
            else if (wheel < -0.01f) _equip?.CycleWeapon(-1);
        }

        _smoothMoveDir = Vector3.Lerp(_smoothMoveDir, _localMoveDir, Time.deltaTime * 8f);

        fpsCamera.Tick(_smoothMoveDir, _currentSpeed, _speed01);
        weaponAnim?.Tick(_smoothMoveDir, _speed01);
    }

    /// <summary>밀쳐내기 (어디서 불러도 됨). y는 위로 띄우는 속도</summary>
    public void AddKnockback(Vector3 velocity)
    {
        if (HasStateAuthority) ApplyKnockback(velocity);
        else RPC_Knockback(velocity);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Knockback(Vector3 velocity) => ApplyKnockback(velocity);

    void ApplyKnockback(Vector3 v)
    {
        _push += new Vector3(v.x, 0f, v.z);
        if (v.y > 0f) _verticalVel = Mathf.Max(_verticalVel, v.y);
    }

}