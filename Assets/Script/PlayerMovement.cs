using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    private CharacterController _cc;

    public override void Spawned()
    {
        _cc = GetComponent<CharacterController>();

        // 내 캐릭터만 색깔 다르게 (누가 나인지 확인용)
        if (HasStateAuthority)
            GetComponent<Renderer>().material.color = Color.cyan;
    }

    public override void FixedUpdateNetwork()
    {
        // 내 캐릭터가 아니면 움직이지 않음
        if (!HasStateAuthority) return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 dir = new Vector3(h, 0f, v).normalized;

        _cc.Move(dir * moveSpeed * Runner.DeltaTime);
        _cc.Move(Vector3.down * 9.8f * Runner.DeltaTime); // 임시 중력
    }
}