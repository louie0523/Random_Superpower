using Fusion;
using UnityEngine;

/// <summary>3인칭 캐릭터 애니메이션. 남들이 보는 모습만 담당.</summary>
public class PlayerAnimator : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform chestBone;   // B-chest

    [Header("Aim")]
    [SerializeField] private float chestPitchScale = 0.5f;

    [Networked] public float MoveSpeed01 { get; set; }
    [Networked] public float AimPitch { get; set; }
    [Networked] public NetworkBool IsGrounded { get; set; }

    private static readonly int PSpeed = Animator.StringToHash("Speed");
    private static readonly int PGround = Animator.StringToHash("Grounded");
    private static readonly int PCombo = Animator.StringToHash("ComboIndex");
    private static readonly int PAtkSpd = Animator.StringToHash("AttackSpeed");
    private static readonly int TAttack = Animator.StringToHash("Attack");
    private static readonly int TDeath = Animator.StringToHash("Death");

    public override void Spawned()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
    }

    /// <summary>PlayerMovement가 매 틱 호출</summary>
    public void SetMovement(float speed01, bool grounded, float pitch)
    {
        if (!HasStateAuthority) return;
        MoveSpeed01 = speed01;
        IsGrounded = grounded;
        AimPitch = pitch;
    }

    public override void Render()
    {
        if (animator == null) return;

        animator.SetFloat(PSpeed, MoveSpeed01, 0.12f, Time.deltaTime);
        animator.SetBool(PGround, IsGrounded);
    }

    void LateUpdate()
    {
        // Animator가 본을 쓴 뒤에 덮어써야 함
        if (chestBone == null) return;
        chestBone.localRotation *= Quaternion.Euler(AimPitch * chestPitchScale, 0f, 0f);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayAttack(int comboIndex, float attackSpeed)
    {
        if (animator == null) return;
        animator.SetInteger(PCombo, comboIndex);
        animator.SetFloat(PAtkSpd, attackSpeed);
        animator.SetTrigger(TAttack);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayDeath()
    {
        if (animator != null) animator.SetTrigger(TDeath);
    }
}