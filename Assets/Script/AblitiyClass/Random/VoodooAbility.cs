using UnityEngine;

/// <summary>
/// 부두 인형 — 플레이어 선택 창에서 지정 → 대상이 '저주'(20초: 채팅 · 보이스 불가) + 봉인 999초(스킬 불가, 죽으면 풀림).
/// 창을 닫으면(취소) 쿨타임이 돌아옴.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Voodoo")]
public class VoodooAbility : AbilityBehaviour
{
    public int DollSkill = 0;

    [Tooltip("저주 — 채팅 · 보이스 금지 (초)")]
    public float CurseDuration = 20f;
    [Tooltip("봉인 (초) — 죽으면 풀림")]
    public float SealDuration = 999f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject CurseFx;
    const int EvCurse = 0;

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == DollSkill;

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        if (!Ctx.HasInputAuthority) return true;

        var ctx = Ctx;
        PlayerPickPanel.Open(TargetUtil.Text("ability.voodoo.pick", "저주할 사람"), true,
            picked => Curse(picked),
            () => ctx.ResetCooldown(slot));
        return true;
    }

    void Curse(PlayerHealth target)
    {
        if (target == null || target.IsDead) return;
        if (!target.TryGetComponent<PlayerStatus>(out var st)) return;

        var me = Ctx.Object.InputAuthority;
        st.Apply(StatusType.Curse, 1f, CurseDuration, me);
        st.Apply(StatusType.Seal, 1f, SealDuration, me);
        Broadcast(EvCurse, TargetUtil.Chest(target));
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key == EvCurse) Ctx.PlayEffectLocal(CurseFx, pos);
    }
}
