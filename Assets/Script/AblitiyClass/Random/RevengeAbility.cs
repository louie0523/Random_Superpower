using Fusion;
using UnityEngine;

/// <summary>
/// 복수 — 나를 죽인 플레이어에게 '복수 시간'(60초). 그 안에 그 사람을 처치하면 살아 있는 동안 이동 속도 · 최대 체력 · 공격력 +100%.
/// 죽으면 랜덤 능력이 바뀌므로 복수 시간 · 버프는 능력과 따로 남는 RevengeMark가 맡음.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Revenge")]
public class RevengeAbility : AbilityBehaviour
{
    [Tooltip("복수 시간 (초) — 내가 죽은 순간부터")]
    public float Window = 60f;
    [Tooltip("성공 시 이동 속도 · 최대 체력 · 공격력 증가 (%)")]
    public float BonusPercent = 100f;

    [Header("연출 — 공용 이펙트 (PlayerComms 목록)")]
    public GameObject SuccessFx;

    private PlayerHealth _health;

    public override void OnEquip()
    {
        _health = Ctx.Health;
        if (_health != null && Ctx.HasStateAuthority) _health.Died += OnDied;
    }

    public override void OnUnequip()
    {
        if (_health != null) _health.Died -= OnDied;
    }

    void OnDied(PlayerRef killer, DeathCause cause)
    {
        if (killer == PlayerRef.None || killer == Ctx.Object.InputAuthority) return;
        RevengeMark.Begin(Ctx, killer, Window, BonusPercent, SuccessFx, Data);
    }
}
