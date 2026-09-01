using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    [Header("Move")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float runSpeed = 10f;
    [SerializeField] private float jumpPower = 6f;
    [SerializeField] private float gravity = -20f;

    [Header("Refs")]
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private GameObject[] hideOnLocal; // 내 몸통 메시

    private CharacterController _cc;
    private float _verticalVel;
    private bool _wasGrounded = true;
    private Vector3 _localMoveDir;
    private bool _isRunning;
    private float _speed01;

    public override void Spawned()
    {
        _cc = GetComponent<CharacterController>();

        if (HasStateAuthority)
        {
            fpsCamera.Init();
        }
        else
        {
            // 남의 캐릭터는 카메라 끄기
            fpsCamera.GetComponentInChildren<Camera>().gameObject.SetActive(false);
            fpsCamera.enabled = false;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        transform.Rotate(Vector3.up * fpsCamera.ConsumeYaw());

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        _localMoveDir = new Vector3(h, 0f, v).normalized;

        _isRunning = Input.GetKey(KeyCode.LeftShift) && v > 0.1f;
        float speed = _isRunning ? runSpeed : walkSpeed;
        _speed01 = _localMoveDir.magnitude;

        Vector3 worldDir = transform.TransformDirection(_localMoveDir);

        // 중력 & 점프
        bool grounded = _cc.isGrounded;

        if (grounded && !_wasGrounded)
            fpsCamera.LandDip(Mathf.Clamp01(-_verticalVel / 12f));

        if (grounded && _verticalVel < 0f)
            _verticalVel = -2f;

        if (grounded && Input.GetKey(KeyCode.Space))
            _verticalVel = jumpPower;

        _verticalVel += gravity * Runner.DeltaTime;
        _wasGrounded = grounded;

        Vector3 motion = worldDir * speed + Vector3.up * _verticalVel;
        _cc.Move(motion * Runner.DeltaTime);
    }

    void Update()
    {
        if (!HasStateAuthority) return;
        fpsCamera.Tick(_localMoveDir, _isRunning, _speed01);
    }
}