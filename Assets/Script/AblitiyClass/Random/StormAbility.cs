using Fusion;
using UnityEngine;

/// <summary>
/// 시공의 폭풍 — 스킬 판정 범위(데이터의 Range · 각도 · 높이) 안의 적 한 명을 맵 밖 하늘로 보내 낙사시킴.
/// 처치 판정: 시전자는 처치 관여(어시스트) 소울만. (그 직전에 누가 밀쳤다면 낙사 막타는 그 사람)
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Storm")]
public class StormAbility : AbilityBehaviour
{
    public int StormSkill = 0;

    [Tooltip("보내는 곳 — 맵 중심에서 이만큼 떨어진 X · Z (m)")]
    public float FarAway = 10000f;
    [Tooltip("낙사 높이(PlayerHealth의 낙사 Y)보다 이만큼 위에서 떨어짐 (m)")]
    public float DropHeight = 30f;
    [Tooltip("이 시간 안에 대상이 죽으면 처치 관여 인정 (초)")]
    public float CreditWindow = 15f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject VanishFx;
    const int EvVanish = 0;

    private PlayerHealth _victim;
    private float _creditUntil;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == StormSkill;

    PlayerHealth FindVictim(AbilityData.SkillDef skill)
    {
        var go = Ctx.FindTarget(skill);
        if (go == null) return null;
        var h = go.GetComponentInParent<PlayerHealth>();
        return h != null && TargetUtil.IsEnemy(Ctx, h) ? h : null;
    }

    public override bool CanUseSkill(AbilityData.SkillDef skill) => !IsMine(skill) || FindVictim(skill) != null;

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;

        var v = FindVictim(skill);
        if (v == null) return true;                 // 모션 도중 빠져나감

        Broadcast(EvVanish, TargetUtil.Chest(v));

        float y = v.FallDeathY + DropHeight;
        float side = Random.value < 0.5f ? -1f : 1f;
        v.RPC_TeleportTo(new Vector3(FarAway * side, y, FarAway));

        _victim = v;
        _creditUntil = Now + CreditWindow;

        var comms = PlayerComms.Local;
        if (comms != null) comms.Notice(NoticeKind.Log, TargetUtil.Text("notice.storm",
            "{0}이(가) {1}을(를) 시공의 폭풍 속으로 날려 보냈다", TargetUtil.NameOf(Ctx), TargetUtil.NameOf(v)));
        return true;
    }

    public override void Tick(float dt)
    {
        if (_victim == null) return;

        if (Now > _creditUntil) { _victim = null; return; }
        if (!_victim.IsDead) return;

        Ctx.Health.RPC_AddAssist(Ctx.Health.SoulPerAssist);
        _victim = null;
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key == EvVanish) Ctx.PlayEffectLocal(VanishFx, pos);
    }
}
