using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>능력들이 같이 쓰는 범위 찾기 · 피해 도우미 (3차 랜덤 능력)</summary>
public static class AbilityAim
{
    static readonly Collider[] _hits = new Collider[64];
    static readonly HashSet<PlayerHealth> _seen = new();

    /// <summary>
    /// origin에서 fwd 방향 부채꼴 안의 살아 있는 적 — 가까운 순.
    /// flat이면 수평 각도 + 높이 차(height) 검사, 아니면 3D 각도 (브레스처럼 위아래로도 겨눔)
    /// </summary>
    public static List<PlayerHealth> InCone(AbilityRuntime ctx, Vector3 origin, Vector3 fwd, float range, float angle, float height, bool flat = true)
    {
        var list = new List<PlayerHealth>();
        if (ctx == null || range <= 0f) return list;

        if (flat) { fwd.y = 0f; }
        fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : ctx.transform.forward;
        float half = angle * 0.5f;

        int n = Physics.OverlapSphereNonAlloc(origin, range, _hits, ctx.HitMask, QueryTriggerInteraction.Ignore);
        _seen.Clear();
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i] != null ? _hits[i].GetComponentInParent<PlayerHealth>() : null;
            if (h == null || h.IsDead || !_seen.Add(h) || !TargetUtil.IsEnemy(ctx, h)) continue;

            Vector3 c = TargetUtil.Chest(h);
            Vector3 to = c - origin;
            if (flat)
            {
                if (Mathf.Abs(h.transform.position.y - ctx.transform.position.y) > height) continue;
                to.y = 0f;
            }
            float d = to.magnitude;
            if (d > range) continue;
            if (angle < 359f && d > 0.3f && Vector3.Angle(fwd, to) > half) continue;
            list.Add(h);
        }
        list.Sort((a, b) => (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));
        return list;
    }

    /// <summary>원 안의 살아 있는 적 (벽 상관없이)</summary>
    public static List<PlayerHealth> InRadius(AbilityRuntime ctx, Vector3 center, float radius)
    {
        var list = new List<PlayerHealth>();
        if (ctx == null || radius <= 0f) return list;
        int n = Physics.OverlapSphereNonAlloc(center, radius, _hits, ctx.HitMask, QueryTriggerInteraction.Ignore);
        _seen.Clear();
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i] != null ? _hits[i].GetComponentInParent<PlayerHealth>() : null;
            if (h == null || h.IsDead || !_seen.Add(h) || !TargetUtil.IsEnemy(ctx, h)) continue;
            list.Add(h);
        }
        // 콜라이더가 몇 개 안 잡히는 아주 큰 반경 대비 — 모든 플레이어를 거리로도 확인
        if (n >= _hits.Length)
            foreach (var h in TargetUtil.AliveEnemies(ctx))
                if (!_seen.Contains(h) && (h.transform.position - center).sqrMagnitude <= radius * radius) list.Add(h);
        return list;
    }

    /// <summary>공격력 coef% 스킬 피해 (치명 · 피해 증가 등 반영)</summary>
    public static float SkillDamage(AbilityRuntime ctx, float coefPct, out bool crit)
    {
        crit = false;
        var st = ctx != null ? ctx.Stats : null;
        if (st == null) return 0f;
        return DamageCalc.CalcOutgoing(st, st.Get(StatType.AttackPower) * coefPct * 0.01f, DamageSource.Skill, true, out crit);
    }

    /// <summary>피해 주기 (권한자) — amount는 최종값</summary>
    public static void Hit(AbilityRuntime ctx, PlayerHealth target, float amount, Vector3 from, bool crit = false, bool canCauseFall = true)
    {
        if (ctx == null || target == null || target.IsDead || amount <= 0f) return;
        var info = DamageInfo.FromPlayerHit(Mathf.Max(1, Mathf.RoundToInt(amount)), ctx.Object.InputAuthority, from, canCauseFall);
        info.IsCrit = crit;
        target.RPC_TakeDamage(info);
    }

    /// <summary>바깥 + 위로 밀기</summary>
    public static Vector3 PushAway(Vector3 from, PlayerHealth target, float push, float up, Vector3 fallbackDir)
    {
        Vector3 away = target.transform.position - from;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : fallbackDir;
        return away * push + Vector3.up * up;
    }

    /// <summary>이 능력을 지금 가진 사람인가 (랜덤 능력 1 · 2 칸)</summary>
    public static bool HasRandom(PlayerHealth h, int abilityId)
    {
        if (h == null || abilityId == 0) return false;
        var ar = h.GetComponent<AbilityRuntime>();
        if (ar == null || ar.Object == null || !ar.Object.IsValid) return false;
        return ar.RandomAbilityId == abilityId || (ar.DualRandom && ar.RandomAbilityId2 == abilityId);
    }

    /// <summary>이펙트를 대상에 붙여서 (모든 화면 로컬) — life초 뒤 사라짐</summary>
    public static GameObject Attach(GameObject prefab, Transform parent, Vector3 localPos, Quaternion localRot, float life)
    {
        if (prefab == null || parent == null) return null;
        var go = Object.Instantiate(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        if (life > 0f) Object.Destroy(go, life);
        return go;
    }
}
