using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>키 사이를 잇는 방식</summary>
public enum RigEase
{
    Smooth,     // 부드럽게 이어짐 (멈추지 않고 휙) — 기본
    EaseInOut,  // 키마다 살짝 멈췄다 출발
    Linear,     // 일정한 속도
    Hold,       // 다음 키까지 그대로 (뚝 끊김)
}

/// <summary>본 전체의 자세 — RigAnimData.Bones 순서</summary>
[Serializable]
public class RigPose
{
    public Quaternion[] Rot = new Quaternion[0];
    public Vector3[] Pos = new Vector3[0];

    public int Count => Rot != null ? Rot.Length : 0;

    public RigPose Clone() => new()
    {
        Rot = Rot != null ? (Quaternion[])Rot.Clone() : new Quaternion[0],
        Pos = Pos != null ? (Vector3[])Pos.Clone() : new Vector3[0],
    };

    /// <summary>길이 맞추기 — 늘어난 칸은 fill(기본 자세)로</summary>
    public void Resize(int n, RigPose fill)
    {
        int old = Count;
        if (old == n && Pos != null && Pos.Length == n) return;
        var r = new Quaternion[n];
        var p = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            bool have = i < old && Pos != null && i < Pos.Length;
            r[i] = have ? Rot[i] : (fill != null && i < fill.Count ? fill.Rot[i] : Quaternion.identity);
            p[i] = have ? Pos[i] : (fill != null && i < fill.Count ? fill.Pos[i] : Vector3.zero);
            if (r[i].x == 0f && r[i].y == 0f && r[i].z == 0f && r[i].w == 0f) r[i] = Quaternion.identity;
        }
        Rot = r;
        Pos = p;
    }

    public void CopyFrom(RigPose o)
    {
        Resize(o.Count, null);
        Array.Copy(o.Rot, Rot, o.Count);
        Array.Copy(o.Pos, Pos, o.Count);
    }
}

[Serializable]
public class RigKey
{
    public float Time;
    [Tooltip("이 키에서 다음 키까지 잇는 방식")]
    public RigEase Ease = RigEase.Smooth;
    public RigPose Pose = new();
    [Tooltip("이 키에서 빠진 본 (본 번호) — 그 본은 앞뒤 키 사이로 그냥 이어짐. 비어 있으면 모든 본이 키")]
    public List<int> Free = new();

    /// <summary>이 키가 그 본의 키인지 (빠진 본이 아닌지)</summary>
    public bool Has(int bone) => Free == null || Free.Count == 0 || !Free.Contains(bone);

    public void SetHas(int bone, bool on)
    {
        Free ??= new List<int>();
        if (on) Free.Remove(bone);
        else if (!Free.Contains(bone)) Free.Add(bone);
    }

    public RigKey Clone() => new() { Time = Time, Ease = Ease, Pose = Pose.Clone(), Free = Free != null ? new List<int>(Free) : new List<int>() };
}

/// <summary>상태 하나 = 애니메이션 하나 (Idle · Walk · Grab · Charge …)</summary>
[Serializable]
public class RigState
{
    public string Name = "Idle";
    [Min(0.05f)] public float Length = 1f;
    [Tooltip("반복 — Idle · Walk 같은 것. 끄면 한 번 재생 후 기본 상태로")]
    public bool Loop = true;
    [Min(0.01f)] public float Speed = 1f;
    [Tooltip("이 상태로 넘어올 때 섞이는 시간 (초)")]
    [Min(0f)] public float BlendIn = 0.15f;
    public List<RigKey> Keys = new();

    public void SortKeys() => Keys.Sort((a, b) => a.Time.CompareTo(b.Time));

    // ── 본별 키 목록 (일부 본만 키인 경우) — 키가 바뀌면 자동으로 다시 만듦 ──
    [NonSerialized] private int[][] _tracks;
    [NonSerialized] private int _trackSig = int.MinValue;

    /// <summary>그 본이 키로 들어간 키 번호들 (시간 순)</summary>
    public int[] Track(int bone, int boneCount)
    {
        int sig = boneCount * 397 ^ Keys.Count;
        for (int k = 0; k < Keys.Count; k++)
        {
            var f = Keys[k].Free;
            sig = sig * 31 + (f != null ? f.Count : 0);
            if (f != null) foreach (int x in f) sig = sig * 7 + x;
        }

        if (_tracks == null || _trackSig != sig || _tracks.Length != boneCount)
        {
            _trackSig = sig;
            _tracks = new int[boneCount][];
            var tmp = new List<int>();
            for (int b = 0; b < boneCount; b++)
            {
                tmp.Clear();
                for (int k = 0; k < Keys.Count; k++) if (Keys[k].Has(b)) tmp.Add(k);
                _tracks[b] = tmp.ToArray();
            }
        }
        return bone < _tracks.Length ? _tracks[bone] : System.Array.Empty<int>();
    }
}

/// <summary>IK 묶음 — 루트 → 끝 순서의 본 이름</summary>
[Serializable]
public class RigIKChain
{
    public string Name = "IK";
    public string[] Bones = new string[0];
    [Tooltip("게임 중 목표를 향해 뻗음 (RigAnimator.AimAt) — 그림자 손이 적을 붙잡을 때 등")]
    public bool FollowRuntimeTarget;
    [Range(0f, 1f)] public float RuntimeWeight = 1f;

    public string Tip => Bones != null && Bones.Length > 0 ? Bones[Bones.Length - 1] : null;
}

/// <summary>
/// 생성물(탈것 · 그림자 손 등)의 애니메이션 세트. Tools → Rig Animator에서 만들고 편집.
/// 모델에 RigAnimator를 붙이고 이 에셋을 넣으면 상태별로 재생됨.
/// 본은 이름으로 찾음 — 본 이름을 바꿀 땐 Rig Animator의 '본 정리'에서 바꾸면 여기도 같이 바뀜.
/// </summary>
[CreateAssetMenu(menuName = "Game/Rig Anim Data")]
public class RigAnimData : ScriptableObject
{
    public List<string> Bones = new();
    [Tooltip("기본 자세 — 키가 없는 본은 이 자세")]
    public RigPose Rest = new();
    public List<RigIKChain> Chains = new();
    public List<RigState> States = new();

    [Header("자동 전환")]
    [Tooltip("가만히 있을 때")]
    public string IdleState = "Idle";
    [Tooltip("움직일 때 (RigAnimator.Moving = true). 없으면 Idle")]
    public string MoveState = "Walk";
    [Tooltip("이 속도(m/초)로 움직일 때 Walk가 1배속. 더 빠르면 빨리 재생")]
    public float WalkRefSpeed = 5f;

    [Tooltip("빨리 움직일 때. 없으면 Walk")]
    public string RunState = "Run";
    [Tooltip("이 속도(m/초) 이상이면 Run (조금 느려져도 바로 Walk로 안 떨어지게 15% 여유)")]
    public float RunThreshold = 8f;
    [Tooltip("이 속도로 달릴 때 Run이 1배속")]
    public float RunRefSpeed = 10f;

    public int BoneIndex(string name) => Bones.IndexOf(name);

    public RigState Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var s in States) if (s != null && s.Name == name) return s;
        return null;
    }

    public RigIKChain FindChain(string name)
    {
        foreach (var c in Chains) if (c != null && c.Name == name) return c;
        return null;
    }

    /// <summary>본을 목록에 추가 (없을 때만) — 기본 자세·모든 키에 칸을 늘림</summary>
    public int EnsureBone(string name, Quaternion restRot, Vector3 restPos)
    {
        int i = Bones.IndexOf(name);
        if (i >= 0) return i;

        Bones.Add(name);
        int n = Bones.Count;
        Rest.Resize(n, null);
        Rest.Rot[n - 1] = restRot;
        Rest.Pos[n - 1] = restPos;
        foreach (var s in States)
            foreach (var k in s.Keys) k.Pose.Resize(n, Rest);
        return n - 1;
    }

    // ══════════════════ 재생 ══════════════════

    /// <summary>상태의 time초 자세를 into에 (into는 Bones 길이로 맞춰짐)</summary>
    public void Sample(RigState s, float time, RigPose into)
    {
        int nb = Bones.Count;
        into.Resize(nb, Rest);

        if (s == null || s.Keys.Count == 0)
        {
            CopyRest(into, nb);
            return;
        }

        float len = Mathf.Max(0.05f, s.Length);
        for (int b = 0; b < nb; b++)
        {
            var tr = s.Track(b, nb);
            SampleBone(s, tr, b, time, len, out into.Rot[b], out into.Pos[b]);
        }
    }

    /// <summary>본 하나 — 그 본이 키로 들어간 키들 사이만 보고 이음</summary>
    void SampleBone(RigState s, int[] tr, int b, float time, float len, out Quaternion rot, out Vector3 pos)
    {
        var keys = s.Keys;
        int n = tr.Length;
        if (n == 0) { rot = GetR(null, b); pos = GetP(null, b); return; }
        if (n == 1) { rot = GetR(keys[tr[0]].Pose, b); pos = GetP(keys[tr[0]].Pose, b); return; }

        int i, j, ip, jn;
        float t0, t1, t = time;

        if (s.Loop)
        {
            t = Mathf.Repeat(t, len);
            i = n - 1;
            for (int k = 0; k < n - 1; k++)
                if (t >= keys[tr[k]].Time && t < keys[tr[k + 1]].Time) { i = k; break; }

            j = (i + 1) % n;
            ip = (i - 1 + n) % n;
            jn = (j + 1) % n;
            t0 = keys[tr[i]].Time;
            t1 = keys[tr[j]].Time;
            if (j == 0)                          // 끝 → 처음으로 넘어가는 구간
            {
                t1 += len;
                if (t < t0) t += len;
            }
        }
        else
        {
            var first = keys[tr[0]];
            var last = keys[tr[n - 1]];
            if (t <= first.Time) { rot = GetR(first.Pose, b); pos = GetP(first.Pose, b); return; }
            if (t >= last.Time) { rot = GetR(last.Pose, b); pos = GetP(last.Pose, b); return; }
            i = 0;
            for (int k = 0; k < n - 1; k++)
                if (t >= keys[tr[k]].Time && t < keys[tr[k + 1]].Time) { i = k; break; }
            j = i + 1;
            ip = Mathf.Max(0, i - 1);
            jn = Mathf.Min(n - 1, j + 1);
            t0 = keys[tr[i]].Time;
            t1 = keys[tr[j]].Time;
        }

        float span = t1 - t0;
        float w = span > 0.0001f ? Mathf.Clamp01((t - t0) / span) : 1f;

        var A = keys[tr[i]].Pose;
        var B = keys[tr[j]].Pose;
        Quaternion qa = GetR(A, b), qb = GetR(B, b);
        Vector3 pa = GetP(A, b), pb = GetP(B, b);

        switch (keys[tr[i]].Ease)
        {
            case RigEase.Hold:
                rot = qa; pos = pa;
                break;
            case RigEase.Linear:
                rot = Quaternion.Slerp(qa, qb, w); pos = Vector3.Lerp(pa, pb, w);
                break;
            case RigEase.EaseInOut:
                float e = w * w * (3f - 2f * w);
                rot = Quaternion.Slerp(qa, qb, e); pos = Vector3.Lerp(pa, pb, e);
                break;
            default:    // Smooth — 앞뒤 키까지 보고 곡선으로
                var P = keys[tr[ip]].Pose;
                var N = keys[tr[jn]].Pose;
                rot = RigMath.Squad(GetR(P, b), qa, qb, GetR(N, b), w);
                pos = RigMath.CatmullRom(GetP(P, b), pa, pb, GetP(N, b), w);
                break;
        }
    }

    Quaternion GetR(RigPose p, int b) =>
        p != null && b < p.Count ? p.Rot[b] : (b < Rest.Count ? Rest.Rot[b] : Quaternion.identity);

    Vector3 GetP(RigPose p, int b) =>
        p != null && p.Pos != null && b < p.Pos.Length ? p.Pos[b] : (b < Rest.Pos.Length ? Rest.Pos[b] : Vector3.zero);

    void CopyRest(RigPose into, int nb)
    {
        for (int b = 0; b < nb; b++) { into.Rot[b] = GetR(null, b); into.Pos[b] = GetP(null, b); }
    }

    void CopyKey(RigPose k, RigPose into, int nb)
    {
        for (int b = 0; b < nb; b++) { into.Rot[b] = GetR(k, b); into.Pos[b] = GetP(k, b); }
    }
}

/// <summary>곡선 보간 도우미</summary>
public static class RigMath
{
    public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>회전 곡선 (Squad) — 앞뒤 키를 보고 속도가 끊기지 않게</summary>
    public static Quaternion Squad(Quaternion q0, Quaternion q1, Quaternion q2, Quaternion q3, float t)
    {
        // 가까운 쪽으로 부호 맞춤 (반대쪽으로 한 바퀴 도는 것 방지)
        q0 = Align(q0, q1);
        q2 = Align(q2, q1);
        q3 = Align(q3, q2);

        Quaternion s1 = Tangent(q0, q1, q2);
        Quaternion s2 = Tangent(q1, q2, q3);
        return Quaternion.SlerpUnclamped(
            Quaternion.SlerpUnclamped(q1, q2, t),
            Quaternion.SlerpUnclamped(s1, s2, t),
            2f * t * (1f - t));
    }

    static Quaternion Align(Quaternion q, Quaternion to) =>
        Quaternion.Dot(q, to) < 0f ? new Quaternion(-q.x, -q.y, -q.z, -q.w) : q;

    static Quaternion Tangent(Quaternion prev, Quaternion cur, Quaternion next)
    {
        var inv = Quaternion.Inverse(cur);
        var a = Log(inv * next);
        var b = Log(inv * prev);
        var sum = new Quaternion(-(a.x + b.x) * 0.25f, -(a.y + b.y) * 0.25f, -(a.z + b.z) * 0.25f, 0f);
        return cur * Exp(sum);
    }

    static Quaternion Log(Quaternion q)
    {
        float w = Mathf.Clamp(q.w, -1f, 1f);
        float a = Mathf.Acos(w);
        float s = Mathf.Sin(a);
        if (Mathf.Abs(s) < 1e-5f) return new Quaternion(0f, 0f, 0f, 0f);
        float k = a / s;
        return new Quaternion(q.x * k, q.y * k, q.z * k, 0f);
    }

    static Quaternion Exp(Quaternion q)
    {
        float a = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
        float s = Mathf.Sin(a);
        float k = a < 1e-5f ? 1f : s / a;
        return new Quaternion(q.x * k, q.y * k, q.z * k, Mathf.Cos(a));
    }
}