using Fusion;
using UnityEngine;

/// <summary>
/// 암살자 — 대상이 나를 못 보는 상태로 때리면 피해 +100%.
///   못 보는 상태 = 내가 투명 · 대상이 암전 · 대상 시야(바라보는 방향 기준 ViewAngle) 밖
/// 스킬 '은밀' (F): 3초간 투명.
/// 피해 증가는 맞는 쪽에서 계산 (DamageRules) — 스킬 · 무기 · 지속 피해 모두 (상태이상 틱 제외).
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Assassin")]
public class AssassinAbility : AbilityBehaviour
{
    public int StealthSkill = 0;

    [Tooltip("못 보는 상태에서 때릴 때 피해 증가 (%)")]
    public float BonusPercent = 100f;
    [Tooltip("대상의 시야각 (°, 전체). 이 밖에서 때리면 '못 봄'")]
    public float ViewAngle = 110f;
    [Tooltip("은밀 — 투명 시간 (초)")]
    public float StealthDuration = 3f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject StealthFx;
    const int EvStealth = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Register() => DamageRules.Register(Rule);

    static float Rule(PlayerHealth target, NetworkObject attacker, DamageInfo info)
    {
        if (attacker == null || info.IsStatusTick) return 1f;
        if (!attacker.TryGetComponent<AbilityRuntime>(out var ar)) return 1f;

        var me = ar.FindAbility<AssassinAbility>();
        if (me == null) return 1f;

        return me.Unseen(target, attacker) ? 1f + me.BonusPercent * 0.01f : 1f;
    }

    /// <summary>target이 attacker를 못 보는가 — target 쪽 권한자에서 부름 (자기 시선을 정확히 앎)</summary>
    bool Unseen(PlayerHealth target, NetworkObject attacker)
    {
        if (attacker.TryGetComponent<PlayerStatus>(out var ast) && ast.Has(StatusType.Invisible)) return true;
        if (target.TryGetComponent<PlayerStatus>(out var tst) && tst.Has(StatusType.Blind)) return true;

        var tar = target.GetComponent<AbilityRuntime>();
        Transform eye = tar != null && tar.CameraRootTransform != null ? tar.CameraRootTransform : target.transform;

        Vector3 look = eye.forward; look.y = 0f;
        Vector3 to = attacker.transform.position - target.transform.position; to.y = 0f;
        if (look.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f) return false;

        return Vector3.Angle(look, to) > ViewAngle * 0.5f;
    }

    // ── 은밀 ──

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == StealthSkill;

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        if (Ctx.TryGetComponent<PlayerStatus>(out var st))
            st.Apply(StatusType.Invisible, 1f, StealthDuration, Ctx.Object.InputAuthority);
        Broadcast(EvStealth, Ctx.transform.position + Vector3.up);
        return true;
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key == EvStealth) Ctx.PlayEffectLocal(StealthFx, pos);
    }
}
