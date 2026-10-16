using System;
using Fusion;
using UnityEngine;

/// <summary>
/// 같은 편 판정 — 지금은 '나 자신'만 같은 편. 팀전이 생기면 AlliesProvider만 연결하면
/// 조커 분신 · 폭발 등 이걸 쓰는 곳이 전부 팀을 따라감.
///   예) TeamUtil.AlliesProvider = (a, b) => TeamManager.TeamOf(a) == TeamManager.TeamOf(b);
/// 주인이 없는 대상(더미 등, InputAuthority = None)은 항상 적.
/// </summary>
public static class TeamUtil
{
    /// <summary>팀 시스템 연결 지점 — (a, b)가 같은 팀이면 true</summary>
    public static Func<PlayerRef, PlayerRef, bool> AlliesProvider;

    public static bool AreAllies(PlayerRef a, PlayerRef b)
    {
        if (a == PlayerRef.None || b == PlayerRef.None) return false;
        if (a == b) return true;
        return AlliesProvider != null && AlliesProvider(a, b);
    }

    /// <summary>이 대상의 주인 (플레이어 오브젝트면 그 플레이어, 아니면 None)</summary>
    public static PlayerRef OwnerOf(Component target)
    {
        if (target == null) return PlayerRef.None;
        var no = target.GetComponentInParent<NetworkObject>();
        if (no == null || !no.IsValid) return PlayerRef.None;
        return no.InputAuthority;
    }

    /// <summary>owner 편에서 이 대상을 공격하면 안 되는지 (나 자신 · 같은 팀)</summary>
    public static bool IsFriendly(PlayerRef owner, Component target) => AreAllies(owner, OwnerOf(target));
}
