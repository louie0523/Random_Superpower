using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 시공의 폭풍 — 스킬 판정 범위(데이터의 Range · 각도 · 높이) 안의 적 한 명을 폭풍에 휘말리게 해서
/// 맵의 '폭풍 지점'(StormPoint) 중 한 곳 하늘 위로 보냄 → 떨어지며 도착, 잠깐 암전.
///   지점은 지금 위치에서 MinDistance m 이상 떨어진 곳 중 무작위. 폭풍 지점이 없으면 부활 지점(SpawnPoint) 중에서.
///   보낸 뒤 CreditWindow초 안에 그 사람이 죽으면 시전자는 처치 관여(어시스트) 소울.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Storm")]
public class StormAbility : AbilityBehaviour
{
    public int StormSkill = 0;

    [Header("보내기")]
    [Tooltip("지금 위치에서 최소 이만큼 떨어진 지점으로 (m). 그런 곳이 없으면 가장 먼 곳")]
    public float MinDistance = 40f;
    [Tooltip("지점 위 이 높이에서 떨어짐 (m) — 천장이 있으면 그 아래까지만")]
    public float DropHeight = 18f;
    [Tooltip("천장 확인 레이어 (벽 · 지붕)")]
    public LayerMask CeilingMask = 1;

    [Header("도착 후")]
    [Tooltip("암전 시간 (초) — 0이면 없음")]
    public float BlindTime = 1.5f;
    [Tooltip("암전 시야 (m, 최소 1)")]
    public float BlindRadius = 3f;
    [Tooltip("이 시간 안에 대상이 죽으면 처치 관여 인정 (초)")]
    public float CreditWindow = 15f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject VanishFx;
    public GameObject ArriveFx;
    const int EvVanish = 0, EvArrive = 1;

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

        if (!PickDestination(v.transform.position, out Vector3 ground))
            return true;                            // 보낼 곳이 없음 (폭풍 지점 · 부활 지점 모두 없음)

        Broadcast(EvVanish, TargetUtil.Chest(v));

        Vector3 drop = ground + Vector3.up * SafeDropHeight(ground);
        v.RPC_TeleportTo(drop);
        Broadcast(EvArrive, ground + Vector3.up * 0.2f);

        if (BlindTime > 0f && v.TryGetComponent<PlayerStatus>(out var st))
            st.Apply(StatusType.Blind, Mathf.Max(PlayerStatus.MinBlindRadius, BlindRadius), BlindTime, Ctx.Object.InputAuthority);

        _victim = v;
        _creditUntil = Now + CreditWindow;

        var comms = PlayerComms.Local;
        if (comms != null) comms.Notice(NoticeKind.Log, TargetUtil.Text("notice.storm",
            "{0}이(가) {1}을(를) 시공의 폭풍 속으로 날려 보냈다", TargetUtil.NameOf(Ctx), TargetUtil.NameOf(v)));
        return true;
    }

    // ══════════════════ 보낼 곳 ══════════════════

    static readonly List<Vector3> _cands = new();

    /// <summary>폭풍 지점(없으면 부활 지점) 중 from에서 MinDistance 이상 떨어진 곳 무작위. 없으면 가장 먼 곳</summary>
    bool PickDestination(Vector3 from, out Vector3 ground)
    {
        _cands.Clear();
        foreach (var p in StormPoint.All)
            if (p != null && p.isActiveAndEnabled) _cands.Add(p.transform.position);

        if (_cands.Count == 0)
            foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
                if (sp != null && sp.isActiveAndEnabled) _cands.Add(sp.transform.position);

        ground = from;
        if (_cands.Count == 0) return false;

        var far = new List<Vector3>();
        Vector3 farthest = _cands[0];
        float best = -1f;
        foreach (var c in _cands)
        {
            float d = (c - from).sqrMagnitude;
            if (d >= MinDistance * MinDistance) far.Add(c);
            if (d > best) { best = d; farthest = c; }
        }

        ground = far.Count > 0 ? far[Random.Range(0, far.Count)] : farthest;
        return true;
    }

    /// <summary>천장이 있으면 그 아래까지만</summary>
    float SafeDropHeight(Vector3 ground)
    {
        float h = Mathf.Max(0.5f, DropHeight);
        if (Physics.Raycast(ground + Vector3.up * 0.5f, Vector3.up, out var hit, h + 2f, CeilingMask, QueryTriggerInteraction.Ignore))
            h = Mathf.Max(0.5f, hit.distance - 2.5f);
        return h;
    }

    // ══════════════════ 처치 관여 ══════════════════

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
        else if (key == EvArrive) Ctx.PlayEffectLocal(ArriveFx, pos);
    }
}
