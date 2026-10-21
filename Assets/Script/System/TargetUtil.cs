using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>능력들이 같이 쓰는 대상 찾기 · 이름</summary>
public static class TargetUtil
{
    static readonly List<PlayerHealth> _tmp = new();

    /// <summary>씬의 모든 플레이어 (더미 포함). 죽은 사람은 includeDead일 때만</summary>
    public static List<PlayerHealth> All(bool includeDead = false)
    {
        var list = new List<PlayerHealth>();
        foreach (var h in Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            if (h != null && (includeDead || !h.IsDead)) list.Add(h);
        return list;
    }

    /// <summary>owner 기준 적 (나 · 같은 팀 제외)</summary>
    public static bool IsEnemy(AbilityRuntime owner, PlayerHealth h)
    {
        if (owner == null || h == null) return false;
        if (h.transform.root == owner.transform.root) return false;
        return !TeamUtil.IsFriendly(owner.Object.InputAuthority, h);
    }

    /// <summary>살아 있는 적 전부</summary>
    public static List<PlayerHealth> AliveEnemies(AbilityRuntime owner)
    {
        var list = All();
        list.RemoveAll(h => !IsEnemy(owner, h));
        return list;
    }

    /// <summary>
    /// 조준선에 가장 가까운 살아 있는 적 — range 안 · 조준선과 maxAngle 안 · (needLos면) 가려지지 않음.
    /// 없으면 null
    /// </summary>
    public static PlayerHealth LookTarget(AbilityRuntime owner, float range, float maxAngle, bool needLos = true)
    {
        if (owner == null) return null;
        var cam = owner.CameraRootTransform != null ? owner.CameraRootTransform : owner.transform;
        Vector3 eye = cam.position;
        Vector3 fwd = cam.forward;

        PlayerHealth best = null;
        float bestAngle = maxAngle;

        _tmp.Clear();
        _tmp.AddRange(AliveEnemies(owner));
        foreach (var h in _tmp)
        {
            Vector3 aim = Chest(h);
            Vector3 to = aim - eye;
            if (to.sqrMagnitude > range * range) continue;

            float a = Vector3.Angle(fwd, to);
            if (a > bestAngle) continue;
            if (needLos && !CanSee(owner.transform.root, eye, h)) continue;

            bestAngle = a;
            best = h;
        }
        return best;
    }

    /// <summary>eye에서 h가 보이는가 — 사이를 막는 게 없음 (내 몸은 무시)</summary>
    public static bool CanSee(Transform ignoreRoot, Vector3 eye, PlayerHealth h)
    {
        Vector3 aim = Chest(h);
        Vector3 to = aim - eye;
        float dist = to.magnitude;
        if (dist < 0.01f) return true;

        if (!CastUtil.Raycast(eye, to / dist, dist, Physics.DefaultRaycastLayers, ignoreRoot, out var hit)) return true;
        return hit.collider != null && hit.collider.transform.root == h.transform.root;
    }

    public static Vector3 Chest(PlayerHealth h) => h.transform.position + Vector3.up * 1.2f;

    /// <summary>표시 이름 — 머리 위 이름과 같은 규칙</summary>
    public static string NameOf(Component c)
    {
        if (c == null) return "?";
        var o = c.GetComponentInParent<NetworkObject>();
        if (o == null) return c.name;

        if (OverheadUI.NameProvider != null)
        {
            string n = OverheadUI.NameProvider(o);
            if (!string.IsNullOrEmpty(n)) return n;
        }
        var runner = o.Runner;
        if (runner != null && o.InputAuthority != PlayerRef.None && runner.GetPlayerObject(o.InputAuthority) == o)
            return LocUtil.Get("ui.score.player", o.InputAuthority.PlayerId);
        return o.name.Replace("(Clone)", "").Trim();
    }

    public static string NameOf(NetworkRunner runner, PlayerRef p)
    {
        if (runner == null || p == PlayerRef.None) return "?";
        var o = runner.GetPlayerObject(p);
        return o != null ? NameOf(o) : LocUtil.Get("ui.score.player", p.PlayerId);
    }

    /// <summary>번역 키가 있으면 그 글, 없으면 기본 글 — {0} {1}에 값</summary>
    public static string Text(string key, string fallback, params object[] args)
    {
        string s = LocUtil.GetOr(key, fallback);
        if (args == null || args.Length == 0) return s;
        try { return string.Format(s, args); }
        catch (System.FormatException) { return s; }
    }

    public static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
