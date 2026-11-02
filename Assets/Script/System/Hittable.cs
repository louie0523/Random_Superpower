using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어(PlayerHealth)가 아닌데 맞을 수 있는 것 — 조커 분신 등 소환물.
/// 이걸 구현한 컴포넌트는 Spawned에서 Hittables.Add, Despawned에서 Hittables.Remove.
/// 체력바(OverheadUI) · 무기 공격(PlayerAttack) · 충격파(Shockwave) · 조커 폭발이 알아서 찾음.
/// </summary>
public interface IHittable
{
    /// <summary>살아 있고 맞을 수 있음</summary>
    bool HitAlive { get; }
    /// <summary>누구 편인가 (주인). 이 사람 · 같은 팀은 못 때리고, 총알 · 화살도 그냥 지나감</summary>
    PlayerRef HitOwner { get; }
    Transform HitTransform { get; }
    /// <summary>발 위치 (데미지 숫자 · 체력바 기준)</summary>
    Vector3 HitFeet { get; }
    int HitHP { get; }
    int HitMaxHP { get; }
    /// <summary>체력바를 보일지 (전투 중)</summary>
    bool HitShowBar { get; }
    /// <summary>머리 위 이름</summary>
    string HitName { get; }
    /// <summary>아무 클라이언트에서 불러도 됨 — 안에서 권한자에게 RPC</summary>
    void TakeHit(DamageInfo info);
}

public static class Hittables
{
    static readonly List<IHittable> _all = new();
    static readonly Collider[] _buf = new Collider[64];
    static readonly HashSet<IHittable> _seen = new();

    public static IReadOnlyList<IHittable> All => _all;

    public static void Add(IHittable h) { if (h != null && !_all.Contains(h)) _all.Add(h); }
    public static void Remove(IHittable h) => _all.Remove(h);

    /// <summary>이 콜라이더가 속한 소환물 (없으면 null)</summary>
    public static IHittable Find(Collider c) => c != null ? c.GetComponentInParent<IHittable>() : null;

    /// <summary>attacker가 때릴 수 있나 — 살아 있고 · 내 것 · 같은 팀 것이 아님</summary>
    public static bool CanHit(IHittable h, PlayerRef attacker) =>
        h != null && (h as Component) != null && h.HitAlive && !TeamUtil.AreAllies(attacker, h.HitOwner);

    /// <summary>attacker 편의 소환물 콜라이더 — 내 총알 · 화살이 그냥 지나감</summary>
    public static bool IsFriendlyCollider(Collider c, PlayerRef attacker)
    {
        var h = Find(c);
        return h != null && (h as Component) != null && TeamUtil.AreAllies(attacker, h.HitOwner);
    }

    /// <summary>범위 안 적 소환물에 피해 (스킬 범위 공격용). 맞힌 수</summary>
    public static int DamageSphere(Vector3 center, float radius, LayerMask mask, DamageInfo info)
    {
        if (_all.Count == 0 || radius <= 0f) return 0;
        int n = Physics.OverlapSphereNonAlloc(center, radius, _buf, mask, QueryTriggerInteraction.Ignore);
        _seen.Clear();
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var h = Find(_buf[i]);
            if (h == null || !_seen.Add(h) || !CanHit(h, info.Attacker)) continue;
            h.TakeHit(info);
            hits++;
        }
        return hits;
    }
}
