using UnityEngine;

/// <summary>
/// 애교부리기 — 15m 안 바라보는 적이 있어야 사용. 그 적이 '약화 100%'(가하는 피해 100% 감소)를 오래 받음 (죽으면 풀림).
/// 스킬: F. 쿨타임 25초는 데이터에서. 모션이 있으면 판정 프레임에 걸림.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Charm")]
public class CharmAbility : AbilityBehaviour
{
    public int CharmSkill = 0;

    [Tooltip("대상 거리 (m)")]
    public float Range = 15f;
    [Tooltip("조준선에서 이 각도 안에 있어야 '바라보는 대상' (°)")]
    public float AimAngle = 20f;
    [Tooltip("가하는 피해 감소 (%) — 100이면 피해 없음")]
    public float WeakenPercent = 100f;
    [Tooltip("지속 (초) — 죽으면 풀림")]
    public float Duration = 9999f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject CharmFx;

    const int EvCharm = 0;
    private PlayerHealth _target;

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == CharmSkill;

    public override bool CanUseSkill(AbilityData.SkillDef skill) =>
        !IsMine(skill) || TargetUtil.LookTarget(Ctx, Range, AimAngle) != null;

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        _target = TargetUtil.LookTarget(Ctx, Range, AimAngle);
        return false;       // 모션 · 효과 타이밍은 데이터대로 → OnSkillEffect
    }

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;

        var t = _target;
        _target = null;
        if (t == null || t.IsDead) return true;

        if (t.TryGetComponent<PlayerStatus>(out var st))
            st.Apply(StatusType.Weaken, WeakenPercent, Duration, Ctx.Object.InputAuthority);

        Broadcast(EvCharm, TargetUtil.Chest(t));
        return true;
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key == EvCharm) Ctx.PlayEffectLocal(CharmFx, pos);
    }
}
