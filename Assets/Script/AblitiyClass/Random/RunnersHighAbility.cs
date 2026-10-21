using UnityEngine;

/// <summary>
/// 러너스 하이 — 이동 속도 +10 (데이터 패시브 StatMod), 대신 멈춰 있으면 매초 최대 체력 % 고정 피해.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Runners High")]
public class RunnersHighAbility : AbilityBehaviour
{
    [Tooltip("이 시간(초) 넘게 멈춰 있으면 피해 시작")]
    public float Grace = 0.3f;
    [Tooltip("멈춰 있는 동안 피해 간격 (초)")]
    public float Interval = 1f;
    [Tooltip("최대 체력의 몇 % 고정 피해")]
    public float MaxHpPercent = 8f;

    private float _still;
    private float _next;

    public override void OnEquip()
    {
        _still = 0f;
        _next = 0f;
    }

    public override void Tick(float dt)
    {
        var h = Ctx.Health;
        if (h == null || h.IsDead || ClassPickPanel.IsShowing || Ctx.IsMoving)
        {
            _still = 0f;
            _next = 0f;
            return;
        }

        _still += dt;
        if (_still < Grace) return;

        _next -= dt;
        if (_next > 0f) return;
        _next = Interval;

        var info = DamageInfo.FromEnvironment(0, Ctx.transform.position);
        info.MaxHpPercent = MaxHpPercent;
        info.IgnoreDefense = true;
        info.IsDot = true;
        h.RPC_TakeDamage(info);
    }
}
