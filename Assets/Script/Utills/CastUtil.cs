using UnityEngine;

/// <summary>
/// 자기 몸(콜라이더 · 피격 판정)을 무시하는 레이 · 구 캐스트.
/// 순간이동 거리 계산에서 내 등 뒤 피격 콜라이더에 막혀 '제자리 순간이동' 되던 것 방지.
/// </summary>
public static class CastUtil
{
    static readonly RaycastHit[] _buf = new RaycastHit[32];

    static bool Own(Collider c, Transform ignoreRoot) =>
        c == null || (ignoreRoot != null && c.transform.root == ignoreRoot);

    public static bool Raycast(Vector3 origin, Vector3 dir, float dist, LayerMask mask, Transform ignoreRoot, out RaycastHit hit)
    {
        hit = default;
        if (dir.sqrMagnitude < 1e-8f) return false;
        int n = Physics.RaycastNonAlloc(origin, dir.normalized, _buf, dist, mask, QueryTriggerInteraction.Ignore);
        return Nearest(n, ignoreRoot, out hit);
    }

    public static bool SphereCast(Vector3 origin, float radius, Vector3 dir, float dist, LayerMask mask, Transform ignoreRoot, out RaycastHit hit)
    {
        hit = default;
        if (dir.sqrMagnitude < 1e-8f) return false;
        int n = Physics.SphereCastNonAlloc(origin, radius, dir.normalized, _buf, dist, mask, QueryTriggerInteraction.Ignore);
        return Nearest(n, ignoreRoot, out hit);
    }

    static bool Nearest(int n, Transform ignoreRoot, out RaycastHit hit)
    {
        hit = default;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = _buf[i];
            if (Own(h.collider, ignoreRoot)) continue;
            if (h.distance < best) { best = h.distance; hit = h; found = true; }
        }
        return found;
    }
}
