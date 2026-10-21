using Fusion;
using UnityEngine;

/// <summary>
/// 사망회귀 — 죽으면 그 자리에서 즉시 부활 (+이펙트).
///   나를 죽인 사람은 그 처치로 얻은 소울을 전부 잃음
///   나를 뺀 모든 적의 스킬 · 궁극기 쿨타임 +8초
/// 죽음 자체는 일어난 것이라 사망 횟수 · 소울 손실은 평소대로, 랜덤 능력도 바뀜 (사망회귀가 아니게 됨).
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Death Rewind")]
public class DeathRewindAbility : AbilityBehaviour
{
    [Tooltip("적 스킬 · 궁극기 쿨타임 증가 (초)")]
    public float EnemyCooldownAdd = 8f;

    [Header("연출 — 공용 이펙트 (PlayerComms 목록에 넣을 것)")]
    public GameObject ReviveFx;

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
        var me = Ctx.Object.InputAuthority;
        var runner = Ctx.Runner;

        // 처치자가 얻은 소울 되돌리기
        if (killer != PlayerRef.None && killer != me && runner != null)
        {
            var ko = runner.GetPlayerObject(killer);
            if (ko != null && ko.TryGetComponent<PlayerHealth>(out var kh)) kh.RPC_RevokeKillGain(me);
        }

        // 적 쿨타임 +8초
        foreach (var h in TargetUtil.All(true))
            if (TargetUtil.IsEnemy(Ctx, h) && h.TryGetComponent<AbilityRuntime>(out var ar))
                ar.RPC_AddCooldownAll(EnemyCooldownAdd, true, false);

        // 그 자리에서 부활 (낙사로 죽었으면 그 자리는 허공이라 부활 지점으로)
        _health.ReviveHere();
        if (Ctx.transform.position.y < _health.FallDeathY + 2f) _health.MoveToSpawn();

        var comms = PlayerComms.Local;
        if (comms != null)
        {
            if (ReviveFx != null) comms.PlayFx(ReviveFx, Ctx.transform.position + Vector3.up);
            comms.Notice(NoticeKind.Log, TargetUtil.Text("notice.rewind", "{0}이(가) 죽음을 되돌렸다", TargetUtil.NameOf(Ctx)));
        }
    }
}
