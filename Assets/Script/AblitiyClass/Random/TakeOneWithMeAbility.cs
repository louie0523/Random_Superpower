using Fusion;
using UnityEngine;

/// <summary>
/// 갈땐 가더라도.. — 내가 죽으면 살아 있는 적 한 명을 무작위로 처치 (내 처치로 인정 — 평소 처치 소울).
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Take One With Me")]
public class TakeOneWithMeAbility : AbilityBehaviour
{
    [Header("연출 — 모든 클라이언트 (플레이어 프리팹 PlayerComms의 '공용 이펙트'에 넣을 것)")]
    public GameObject VictimFx;

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
        var list = TargetUtil.AliveEnemies(Ctx);
        if (list.Count == 0) return;

        var victim = list[Random.Range(0, list.Count)];
        var me = Ctx.Object.InputAuthority;

        var comms = PlayerComms.Local;
        if (comms != null)
        {
            comms.Notice(NoticeKind.Log, TargetUtil.Text("notice.takeone",
                "{0}이(가) {1}을(를) 길동무로 데려갔다", TargetUtil.NameOf(Ctx), TargetUtil.NameOf(victim)));
            if (VictimFx != null) comms.PlayFx(VictimFx, TargetUtil.Chest(victim));
        }

        victim.RPC_ForceKill(me, false);
    }
}
