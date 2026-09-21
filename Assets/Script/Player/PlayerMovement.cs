using Fusion;
using UnityEngine;
using static AbilityRuntime;

public class PlayerMovement : NetworkBehaviour
{
    [Header("View")]
    [SerializeField] private GameObject bodyRoot;
    [SerializeField] private GameObject armRoot;

    [Header("Move")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float runSpeed = 10f;
    [SerializeField] private float jumpPower = 6f;
    [SerializeField] private float gravity = -20f;

    [Header("Refs")]
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private GameObject[] hideOnLocal; // 내 몸통 메시

    [Header("Debug")]
    public bool IsDummy = false;

    private CharacterController _cc;
    private float _verticalVel;
    private bool _wasGrounded = true;
    private Vector3 _localMoveDir;
    private bool _isRunning;
    private float _speed01;
    private PlayerStats _stats;
    [SerializeField] private WeaponAnimator weaponAnim;
    private PlayerAnimator _anim;

    private float _currentSpeed;

    public bool IsMoving => _speed01 > 0.1f;
    public Vector3 LocalMoveDir => _localMoveDir;
    private AbilityRuntime _ability;
    private Vector3 _smoothMoveDir;
    public Vector3 SmoothMoveDir => _smoothMoveDir;

    private PlayerShop _shop;
    private PlayerEquipment _equip;


    public override void Spawned()
    {
        _cc = GetComponent<CharacterController>();
        _stats = GetComponent<PlayerStats>();
        _anim = GetComponent<PlayerAnimator>();
        _ability = GetComponent<AbilityRuntime>();
        _shop = GetComponent<PlayerShop>();
        _equip = GetComponent<PlayerEquipment>();

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

        if (_shop != null && _shop.IsShopping)
        {
            _localMoveDir = Vector3.zero;
            _speed01 = 0f;
            _currentSpeed = 0f;
            _verticalVel += gravity * Runner.DeltaTime;
            _cc.Move(Vector3.up * _verticalVel * Runner.DeltaTime);
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        if (_ability != null)
        {
            switch (_ability.MoveLock)
            {
                case MoveLockMode.StrafeOnly: v = 0f; break;   // 꽃게
                case MoveLockMode.ForwardOnly: h = 0f; break;
            }
        }

        _localMoveDir = new Vector3(h, 0f, v).normalized;

        float speed = _stats.Get(StatType.MoveSpeed);
        _speed01 = _localMoveDir.magnitude;
        _currentSpeed = speed * _speed01;

        Vector3 worldDir = transform.TransformDirection(_localMoveDir);

        bool grounded = _cc.isGrounded;

        if (grounded && !_wasGrounded)
            fpsCamera.LandDip(Mathf.Clamp01(-_verticalVel / 12f));

        if (grounded && _verticalVel < 0f) _verticalVel = -2f;
        if (grounded && Input.GetKey(KeyCode.Space)) _verticalVel = jumpPower;

        bool dashNoGravity = _ability != null && _ability.DashIgnoresGravity;

        if (!dashNoGravity)
        {
            _verticalVel += gravity * Runner.DeltaTime;
        }
        else
        {
            _verticalVel = 0f;
        }

        Vector3 motion = worldDir * speed + Vector3.up * _verticalVel;

        if (_ability != null && _ability.IsDashing)
            motion = dashNoGravity ? Vector3.zero : Vector3.up * _verticalVel;

        _cc.Move(motion * Runner.DeltaTime);

        _anim?.SetMovement(_speed01, _cc.isGrounded, fpsCamera.Pitch);




    }



    void Update()
    {
        if (HasStateAuthority)
        {
            var ui = UIManager.Instance;

            if (Input.GetKeyDown(KeyCode.F1)) ui?.ToggleAbility();
            if (Input.GetKeyDown(KeyCode.I)) ui?.ToggleInventory();

            if (Input.GetKeyDown(KeyCode.B))
            {
                // 상점은 전투 중 제한
                if (_shop != null && _shop.CanOpenShop) ui?.ToggleShop();
            }

            if (Input.GetKeyDown(KeyCode.Escape) && ui != null && ui.AnyPanelOpen)
                ui.CloseAll();

            if (Input.GetKeyDown(KeyCode.G)) _equip?.DropActiveWeapon();

            if (Input.GetKeyDown(KeyCode.Alpha1)) _equip?.SwitchWeapon(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) _equip?.SwitchWeapon(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) _equip?.SwitchWeapon(2);



            float wheel = Input.GetAxisRaw("Mouse ScrollWheel");
            if (wheel > 0.01f) _equip?.CycleWeapon(1);
            else if (wheel < -0.01f) _equip?.CycleWeapon(-1);

            _smoothMoveDir = Vector3.Lerp(_smoothMoveDir, _localMoveDir,
                                          Time.deltaTime * 8f);

            fpsCamera.Tick(_smoothMoveDir, _currentSpeed, _speed01);
            weaponAnim?.Tick(_smoothMoveDir, _speed01);

        }
        else
        {
            weaponAnim?.TickRemote();
        }
    }


}