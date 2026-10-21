using Fusion;
using UnityEngine;

/// <summary>
/// 무승부 — 15m 안 시야에 보이는 적이 있어야 사용. 모션이 끝나면 50% 확률로 내 소울 = 그 적의 소울.
/// (상대가 더 적으면 내 소울이 내려감 — 도박)
/// 데이터: 효과 타이밍 = 모션 종료 권장.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Draw")]
public class DrawAbility : AbilityBehaviour
{
    public int DrawSkill = 0;

    [Tooltip("대상 거리 (m)")]
    public float Range = 15f;
    [Tooltip("시야 — 조준선에서 이 각도 안 (°)")]
    public float ViewAngle = 50f;
    [Tooltip("성공 확률 (0~1)")]
    [Range(0f, 1f)] public float Chance = 0.5f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject SuccessFx;
    public GameObject FailFx;
    const int EvSuccess = 0, EvFail = 1;

    private PlayerHealth _target;

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == DrawSkill;

    public override bool CanUseSkill(AbilityData.SkillDef skill) =>
        !IsMine(skill) || TargetUtil.LookTarget(Ctx, Range, ViewAngle) != null;

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        _target = TargetUtil.LookTarget(Ctx, Range, ViewAngle);
        return false;
    }

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;

        var t = _target;
        _target = null;
        if (t == null || Ctx.Health == null) return true;

        bool win = Random.value < Chance;
        var comms = PlayerComms.Local;
        Vector3 pos = Ctx.transform.position + Vector3.up * 1.6f;

        if (win)
        {
            int before = Ctx.Health.Soul;
            Ctx.Health.Soul = Mathf.Max(0, t.Soul);
            Broadcast(EvSuccess, pos);
            if (comms != null) comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.draw.win",
                "무승부! 소울 {0} → {1}", before, Ctx.Health.Soul), Vector3.zero, 0f, Ctx.Object.InputAuthority, false);
        }
        else
        {
            Broadcast(EvFail, pos);
            if (comms != null) comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.draw.lose", "무승부 실패…"),
                Vector3.zero, 0f, Ctx.Object.InputAuthority, false);
        }
        return true;
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        Ctx.PlayEffectLocal(key == EvSuccess ? SuccessFx : FailFx, pos);
    }
}
