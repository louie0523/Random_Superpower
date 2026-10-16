using System.Collections.Generic;
using UnityEngine;

/// <summary>무기를 던지거나 날릴 때 모양</summary>
public enum ThrowStyle
{
    TipFirst,   // 날 끝이 앞으로 — 검 · 창 · 단검
    Drill,      // 날 끝이 앞으로 + 진행 방향 축으로 빙글빙글
    Tumble,     // 앞뒤로 빙글빙글 (도끼 · 망치 던지기)
}

/// <summary>
/// 무기 모델의 '날 끝 방향'과 '납작한 면 방향'을 알아냄 — 날아가는 무기가 날 끝부터 가게.
///   WeaponRig.Tip이 있으면 손잡이(모델 원점) → Tip 방향
///   없으면 메쉬 크기에서 자동: 가장 긴 축, 원점에서 먼 쪽이 날 끝
///   납작한 면 = 가장 얇은 축 (검날의 넓은 면)
/// 모델(프리팹)마다 한 번 계산해서 기억함.
/// </summary>
public static class WeaponAxis
{
    public struct Info
    {
        public Vector3 TipDir;      // 모델 로컬 — 날 끝 방향
        public Vector3 FlatNormal;  // 모델 로컬 — 넓은 면이 보는 방향
        public Vector3 TipPoint;    // 모델 로컬 — 날 끝 위치
        public Vector3 Center;      // 모델 로컬 — 메쉬 가운데
        public ThrowStyle Style;
        public float Roll;          // 날 축 기준 비틀기 (°)
        public float Spin;          // Drill · Tumble 회전 속도 (°/초)
    }

    static readonly Dictionary<GameObject, Info> _cache = new();

    /// <summary>prefab 기준으로 기억. instance = 그 프리팹을 Instantiate한 것 (계산용)</summary>
    public static Info Get(GameObject prefab, GameObject instance)
    {
        if (prefab != null && _cache.TryGetValue(prefab, out var c)) return c;
        var info = Compute(instance != null ? instance : prefab);
        if (prefab != null) _cache[prefab] = info;
        return info;
    }

    /// <summary>날 끝 → +Z, 넓은 면 → +Y 가 되게 하는 모델 회전 (부모 기준). roll은 날 축 기준 추가 비틀기</summary>
    public static Quaternion Normalize(in Info i, float extraRoll = 0f) =>
        Quaternion.AngleAxis(i.Roll + extraRoll, Vector3.forward) * Quaternion.Inverse(Quaternion.LookRotation(i.TipDir, i.FlatNormal));

    /// <summary>월드에서 날 끝이 dir을 보게 하는 회전 (넓은 면은 up 쪽)</summary>
    public static Quaternion Aim(in Info i, Vector3 dir, Vector3 up, float extraRoll = 0f)
    {
        if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(dir.normalized, up.normalized)) > 0.98f) up = Vector3.forward;
        return Quaternion.LookRotation(dir, up) * Normalize(i, extraRoll);
    }

    static Info Compute(GameObject go)
    {
        var info = new Info { TipDir = Vector3.up, FlatNormal = Vector3.forward, TipPoint = Vector3.up, Center = Vector3.zero };
        if (go == null) return info;
        var root = go.transform;

        var rig = go.GetComponentInChildren<WeaponRig>(true);
        if (rig != null)
        {
            info.Style = rig.ThrowStyle;
            info.Roll = rig.ThrowRoll;
            info.Spin = rig.ThrowSpin;
        }
        else info.Spin = 900f;

        // 메쉬 크기 (모델 로컬)
        bool any = false;
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        void Add(Transform t, Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            for (int k = 0; k < 8; k++)
            {
                var corner = c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z);
                var p = root.InverseTransformPoint(t.TransformPoint(corner));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            any = true;
        }
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null && (rig == null || rig.ProjectileVisual == null || !mf.transform.IsChildOf(rig.ProjectileVisual.transform)))
                Add(mf.transform, mf.sharedMesh.bounds);
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) Add(smr.transform, smr.sharedMesh.bounds);

        Vector3 size = any ? max - min : Vector3.one;
        Vector3 center = any ? (min + max) * 0.5f : Vector3.zero;
        info.Center = center;

        // 날 끝
        bool fromRig = false;
        if (rig != null && rig.Tip != null)
        {
            Vector3 tp = root.InverseTransformPoint(rig.Tip.position);
            if (tp.magnitude > 0.02f)
            {
                info.TipPoint = tp;
                info.TipDir = tp.normalized;
                fromRig = true;
            }
        }
        int longAxis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        if (!fromRig)
        {
            float sign = center[longAxis] >= -0.001f ? 1f : -1f;      // 원점(손잡이)에서 먼 쪽
            var axis = Vector3.zero; axis[longAxis] = sign;
            info.TipDir = axis;
            var tp = center; tp[longAxis] = sign > 0f ? max[longAxis] : min[longAxis];
            info.TipPoint = tp;
        }

        // 넓은 면 = 날 축이 아닌 두 축 중 더 얇은 쪽
        int a1 = (longAxis + 1) % 3, a2 = (longAxis + 2) % 3;
        if (fromRig)
        {
            // Tip 방향과 가장 덜 겹치는 두 축 중에서 고름
            Vector3 d = info.TipDir;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            int tipAxis = ax >= ay && ax >= az ? 0 : (ay >= az ? 1 : 2);
            a1 = (tipAxis + 1) % 3; a2 = (tipAxis + 2) % 3;
        }
        int thin = size[a1] <= size[a2] ? a1 : a2;
        var n = Vector3.zero; n[thin] = 1f;
        info.FlatNormal = n;
        return info;
    }
}
