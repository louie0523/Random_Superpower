using System;
using System.Collections.Generic;
using UnityEngine;

public enum MotionInterp
{
    [Tooltip("키 사이를 직선 + 최단 회전. 키마다 곡선(Ease)으로 가감속 — 키에서 속도가 0이 되기 쉬움")]
    Linear,
    [Tooltip("모든 키를 지나는 부드러운 곡선. 중간 키에서 멈추지 않고 그대로 통과 (휘두르기용)")]
    Smooth,
}

public enum ShakeHand
{
    Right,
    Left,
    Both,
}

/// <summary>떨림 구간 — 이 구간 동안 손이 바르르 떨림 (힘주는 느낌). 키 자세 위에 더해짐</summary>
[Serializable]
public class MotionShake
{
    [Tooltip("시작 (모션 전체 0~1)")]
    [Range(0f, 1f)] public float Start = 0.4f;
    [Tooltip("끝 (모션 전체 0~1)")]
    [Range(0f, 1f)] public float End = 0.6f;

    [Tooltip("떨리는 손")]
    public ShakeHand Hands = ShakeHand.Right;
    [Tooltip("양손일 때 따로 떨기 — 끄면 두 손이 같이 떨림 (한 무기를 같이 쥐고 있을 때 자연스러움)")]
    public bool Independent = false;

    [Tooltip("위치 떨림 (cm)")]
    [Min(0f)] public float PosAmount = 1f;
    [Tooltip("회전 떨림 (°)")]
    [Min(0f)] public float RotAmount = 4f;
    [Tooltip("화면(카메라)도 같이 떨림 (°) — 0이면 손만")]
    [Min(0f)] public float CamAmount = 0f;
    [Tooltip("초당 떨림 — 클수록 잘게 (15~30 추천)")]
    [Min(0.1f)] public float Frequency = 22f;

    [Tooltip("들어오고 빠지는 부드러움 (구간 길이 대비)")]
    [Range(0f, 0.5f)] public float Fade = 0.2f;
    [Tooltip("점점 세지기 — 0 = 일정, 1 = 처음엔 약하고 끝으로 갈수록 최대 (버티다 힘이 실리는 느낌)")]
    [Range(0f, 1f)] public float Build = 0.3f;

    [Tooltip("떨림 모양 (바꾸면 다른 무늬)")]
    public int Seed = 0;
}

/// <summary>
/// 둥둥 — 무기가 제자리를 중심으로 천천히 떠다니는 흔들림 (떨림보다 느리고 크게).
/// 무기 설정(항상)과 모션의 둥둥 구간이 같이 씀
/// </summary>
[Serializable]
public class FloatWave
{
    [Tooltip("위치 흔들림 (cm) — 축마다. 위아래로만 둥둥이면 Y만")]
    public Vector3 PosAmount = new(0.6f, 1.5f, 0.6f);
    [Tooltip("회전 흔들림 (°) — 축마다")]
    public Vector3 RotAmount = new(3f, 4f, 6f);
    [Tooltip("빠르기 (초당 왕복) — 0.3~0.8 = 둥둥, 2 이상 = 붕붕")]
    [Min(0.01f)] public float Speed = 0.5f;
    [Tooltip("불규칙함 — 0 = 일정한 물결, 1 = 제멋대로 떠돎")]
    [Range(0f, 1f)] public float Wander = 0.35f;
    [Tooltip("무늬 (바꾸면 다른 모양)")]
    public int Seed = 0;

    /// <summary>sec초일 때 흔들림 — 위치(m) · 회전(°)</summary>
    public void Sample(float sec, out Vector3 pos, out Vector3 rot)
    {
        float ph = sec * Speed * Mathf.PI * 2f;
        float o = Seed * 1.618f;
        float u = sec * Speed;

        Vector3 a = Vector3.Lerp(Wave(ph, o), SwingMotion.Noise3(u * 1.3f, Seed * 17 + 3), Wander);
        Vector3 b = Vector3.Lerp(Wave(ph * 0.9f + 1.7f, o + 3.1f), SwingMotion.Noise3(u * 1.1f, Seed * 17 + 11), Wander);
        pos = Vector3.Scale(a, PosAmount) * 0.01f;
        rot = Vector3.Scale(b, RotAmount);
    }

    // 축마다 조금씩 다른 속도 — 같이 오르내리지 않고 물에 뜬 것처럼
    static Vector3 Wave(float ph, float o) =>
        new(Mathf.Sin(ph * 0.83f + o), Mathf.Sin(ph + o * 2.3f), Mathf.Sin(ph * 0.71f + o * 3.7f));
}

/// <summary>둥둥 구간 — 이 구간 동안 무기가 제 자리(키 위치)를 중심으로 떠다님. 키 자세 위에 더해짐</summary>
[Serializable]
public class MotionFloat
{
    [Tooltip("시작 (모션 전체 0~1)")]
    [Range(0f, 1f)] public float Start = 0f;
    [Tooltip("끝 (모션 전체 0~1)")]
    [Range(0f, 1f)] public float End = 1f;
    [Tooltip("들어오고 빠지는 부드러움 (구간 길이 대비)")]
    [Range(0f, 0.5f)] public float Fade = 0.15f;
    [Tooltip("떠다닐 파츠 — -1 = 전부, 0 = 0번(메인), 1 = 1번 …")]
    public int Part = -1;
    public FloatWave Wave = new();
}

[Serializable]
public class SwingMotion
{
    [Tooltip("시간순 키프레임. 최소 2개")]
    public List<PoseKey> Keys = new();

    [Tooltip("전체 진폭 배율")]
    public float Amplitude = 1f;

    [Tooltip("Linear = 기존 방식 (키마다 Ease)\n" +
             "Smooth = 키를 곡선으로 이어서 중간 키를 멈춤 없이 통과. 회전도 입력한 축 방향 그대로 돎 (최단 경로로 꺾이지 않음)")]
    public MotionInterp Interp = MotionInterp.Linear;

    [Tooltip("Smooth 전용 — 곡선의 탄력. 0 = 키마다 멈춤(직선), 1 = 자연스럽게 통과, 1 초과 = 더 크게 휘어짐")]
    [Range(0f, 1.5f)] public float Smoothness = 1f;

    [Tooltip("떨림 구간들 — 손이 잠깐 바르르 떨림 (힘주기)")]
    public List<MotionShake> Shakes = new();

    [Tooltip("둥둥 구간들 — 무기가 제자리를 중심으로 떠다님")]
    public List<MotionFloat> Floats = new();

    [HideInInspector]
    [Tooltip("모션 길이 (초) — 떨림 속도(초당)를 맞추려고 편집 창이 넣어 둠")]
    public float LengthHint = 1f;

    public bool IsValid => Keys != null && Keys.Count >= 2;

    /// <summary>t는 0~1</summary>
    public ArmPose Evaluate(float t, ArmPose idle)
    {
        if (!IsValid) return idle?.Clone();

        t = Mathf.Clamp01(t);
        return ApplyFloats(ApplyShakes(EvaluateKeys(t), t), t);
    }

    ArmPose EvaluateKeys(float t)
    {
        if (t <= Keys[0].Time) return Keys[0].Pose.Clone();
        if (t >= Keys[^1].Time) return Keys[^1].Pose.Clone();

        int seg = 0;
        for (int i = 0; i < Keys.Count - 1; i++)
            if (t >= Keys[i].Time && t <= Keys[i + 1].Time) { seg = i; break; }

        var a = Keys[seg];
        var b = Keys[seg + 1];
        float span = Mathf.Max(0.0001f, b.Time - a.Time);
        float local = (t - a.Time) / span;

        if (Interp == MotionInterp.Linear)
        {
            float eased = b.Ease != null ? b.Ease.Evaluate(local) : local;
            return ArmPose.Lerp(a.Pose, b.Pose, eased);
        }

        return EvaluateSmooth(seg, local);
    }

    public void SortKeys() => Keys.Sort((a, b) => a.Time.CompareTo(b.Time));

    // ══════════════════ 떨림 ══════════════════

    ArmPose ApplyShakes(ArmPose p, float t)
    {
        if (Shakes == null || Shakes.Count == 0 || p == null) return p;
        float sec = t * Mathf.Max(0.05f, LengthHint);

        for (int i = 0; i < Shakes.Count; i++)
        {
            var s = Shakes[i];
            if (s == null) continue;
            float a = Envelope(s, t);
            if (a <= 0f) continue;

            float time = sec * s.Frequency;
            int seed = s.Seed * 97 + i * 31;

            Vector3 pos = Noise3(time, seed) * (s.PosAmount * 0.01f * a);
            Vector3 rot = Noise3(time, seed + 7) * (s.RotAmount * a);

            if (s.Hands != ShakeHand.Left)
            {
                p.RightPos += pos;
                p.RightRot += rot;
            }
            if (s.Hands != ShakeHand.Right)
            {
                if (s.Hands == ShakeHand.Both && s.Independent)
                {
                    p.LeftPos += Noise3(time, seed + 13) * (s.PosAmount * 0.01f * a);
                    p.LeftRot += Noise3(time, seed + 19) * (s.RotAmount * a);
                }
                else
                {
                    p.LeftPos += pos;
                    p.LeftRot += rot;
                }
            }

            if (s.CamAmount > 0f) p.CamRot += Noise3(time * 0.8f, seed + 23) * (s.CamAmount * a);
        }
        return p;
    }

    // ══════════════════ 둥둥 ══════════════════

    ArmPose ApplyFloats(ArmPose p, float t)
    {
        if (Floats == null || Floats.Count == 0 || p == null) return p;
        float sec = t * Mathf.Max(0.05f, LengthHint);

        foreach (var f in Floats)
        {
            if (f == null || f.Wave == null) continue;
            float a = Envelope(f.Start, f.End, f.Fade, t);
            if (a <= 0f) continue;

            if (f.Part >= 0)
            {
                f.Wave.Sample(sec, out var pos, out var rot);
                p.AddFloat(f.Part, pos * a, rot * a);
            }
            else
            {
                // 전부 — 파츠마다 조금씩 다르게 (같이 오르내리지 않게)
                for (int i = 0; i < ArmPose.MaxParts; i++)
                {
                    f.Wave.Sample(sec + i * 1.37f, out var pos, out var rot);
                    p.AddFloat(i, pos * a, rot * a);
                }
            }
        }
        return p;
    }

    /// <summary>구간 세기 0~1 — 페이드 인/아웃만. 구간이 모션 처음/끝에 붙어 있으면 그쪽은 페이드 없이</summary>
    public static float Envelope(float start, float end, float fade, float t)
    {
        float s = Mathf.Min(start, end), e = Mathf.Max(start, end);
        if (t < s || t > e || e - s < 0.0001f) return 0f;
        float u = (t - s) / (e - s);
        float f = Mathf.Max(0.0001f, fade);
        float inA = s <= 0.0001f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / f));
        float outA = e >= 0.9999f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / f));
        return inA * outA;
    }

    /// <summary>구간 안 세기 0~1 — 페이드 인/아웃 + 점점 세지기</summary>
    public static float Envelope(MotionShake s, float t)
    {
        float start = Mathf.Min(s.Start, s.End), end = Mathf.Max(s.Start, s.End);
        if (t <= start || t >= end || end - start < 0.0001f) return 0f;

        float u = (t - start) / (end - start);
        float f = Mathf.Max(0.0001f, s.Fade);
        float inA = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / f));
        float outA = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / f));
        float build = Mathf.Lerp(1f - s.Build, 1f, u);
        return inA * outA * build;
    }

    /// <summary>-1 ~ 1 부드러운 잡음 (축마다 다름). 펄린 잡음은 실제로 ±0.5 근처만 나와서 넓혀 줌 → 설정한 세기가 거의 그대로 나옴</summary>
    public static Vector3 Noise3(float time, int seed)
    {
        float o = seed * 13.37f;
        return new Vector3(
            N(Mathf.PerlinNoise(time, o + 1.3f)),
            N(Mathf.PerlinNoise(o + 5.1f, time)),
            N(Mathf.PerlinNoise(time + o * 0.5f, o + 9.7f)));
    }

    static float N(float p) => Mathf.Clamp((p - 0.5f) * 3.2f, -1f, 1f);

    // ══════════════════ Smooth (시간 기준 Hermite 스플라인) ══════════════════
    //  - 위치: 키를 모두 지나는 곡선. 중간 키에서 속도가 이어짐
    //  - 회전: 오일러 각을 키마다 이어붙인 뒤(언랩) 같은 곡선 → 입력한 축 그대로 돎
    //  - 첫·끝 키, 'Hold' 키에서는 속도 0 (거기서 잠깐 멈춤)

    static readonly Func<ArmPose, Vector3>[] Pos =
    {
        p => p.RightPos, p => p.LeftPos, p => p.WeaponPos, p => p.CamPos, p => p.BodyPos, p => p.HeadPos,
    };
    static readonly Func<ArmPose, Vector3>[] Rot =
    {
        p => p.RightRot, p => p.LeftRot, p => p.WeaponRot, p => p.CamRot, p => p.BodyRot, p => p.HeadRot,
    };

    [NonSerialized] private Vector3[] _unwrap;

    ArmPose EvaluateSmooth(int seg, float s)
    {
        var a = Keys[seg];
        var b = Keys[seg + 1];

        // 손가락·bool 등 곡선이 필요 없는 것은 기존 보간
        var r = ArmPose.Lerp(a.Pose, b.Pose, s);

        Vector3 v;
        v = Spline(Pos[0], false, seg, s); r.RightPos = v;
        v = Spline(Pos[1], false, seg, s); r.LeftPos = v;
        v = Spline(Pos[2], false, seg, s); r.WeaponPos = v;
        v = Spline(Pos[3], false, seg, s); r.CamPos = v;
        v = Spline(Pos[4], false, seg, s); r.BodyPos = v;
        v = Spline(Pos[5], false, seg, s); r.HeadPos = v;

        v = Spline(Rot[0], true, seg, s); r.RightRot = v;
        v = Spline(Rot[1], true, seg, s); r.LeftRot = v;
        v = Spline(Rot[2], true, seg, s); r.WeaponRot = v;
        v = Spline(Rot[3], true, seg, s); r.CamRot = v;
        v = Spline(Rot[4], true, seg, s); r.BodyRot = v;
        v = Spline(Rot[5], true, seg, s); r.HeadRot = v;

        // 추가 파츠 — 키 중 가장 많이 가진 수만큼
        int np = 0;
        foreach (var k in Keys) if (k?.Pose?.Parts != null) np = Mathf.Max(np, k.Pose.Parts.Length);
        for (int j = 0; j < np; j++)
        {
            int jj = j;
            var e = r.Extra(jj);
            e.Pos = Spline(p => p.ExtraPos(jj), false, seg, s);
            e.Rot = Spline(p => p.ExtraRot(jj), true, seg, s);
        }

        r.CamFov = SplineF(seg, s);
        return r;
    }

    /// <summary>키 i의 값. 회전이면 키 0부터 이어붙인(언랩) 값 — 350° → -10° 같은 꺾임 방지</summary>
    Vector3 Value(Func<ArmPose, Vector3> get, bool rot, int i)
    {
        if (!rot) return get(Keys[i].Pose);
        return _unwrap[i];
    }

    void BuildUnwrap(Func<ArmPose, Vector3> get)
    {
        int n = Keys.Count;
        if (_unwrap == null || _unwrap.Length < n) _unwrap = new Vector3[n];

        _unwrap[0] = get(Keys[0].Pose);
        for (int i = 1; i < n; i++)
        {
            Vector3 prev = _unwrap[i - 1];
            Vector3 raw = get(Keys[i].Pose);
            _unwrap[i] = new Vector3(
                prev.x + Mathf.DeltaAngle(prev.x, raw.x),
                prev.y + Mathf.DeltaAngle(prev.y, raw.y),
                prev.z + Mathf.DeltaAngle(prev.z, raw.z));
        }
    }

    /// <summary>키 i에서의 기울기 (단위: 값 / 정규화 시간)</summary>
    Vector3 Tangent(Func<ArmPose, Vector3> get, bool rot, int i)
    {
        int last = Keys.Count - 1;
        if (i <= 0 || i >= last || Keys[i].Hold) return Vector3.zero;

        float dt = Keys[i + 1].Time - Keys[i - 1].Time;
        if (dt < 0.0001f) return Vector3.zero;

        return (Value(get, rot, i + 1) - Value(get, rot, i - 1)) / dt * Smoothness;
    }

    Vector3 Spline(Func<ArmPose, Vector3> get, bool rot, int seg, float s)
    {
        if (rot) BuildUnwrap(get);

        float h = Keys[seg + 1].Time - Keys[seg].Time;
        Vector3 p0 = Value(get, rot, seg), p1 = Value(get, rot, seg + 1);
        Vector3 m0 = Tangent(get, rot, seg) * h, m1 = Tangent(get, rot, seg + 1) * h;
        return Hermite(p0, m0, p1, m1, s);
    }

    float SplineF(int seg, float s)
    {
        int last = Keys.Count - 1;
        float F(int i) => Keys[i].Pose.CamFov;
        float T(int i)
        {
            if (i <= 0 || i >= last || Keys[i].Hold) return 0f;
            float dt = Keys[i + 1].Time - Keys[i - 1].Time;
            return dt < 0.0001f ? 0f : (F(i + 1) - F(i - 1)) / dt * Smoothness;
        }

        float h = Keys[seg + 1].Time - Keys[seg].Time;
        Vector3 r = Hermite(new Vector3(F(seg), 0, 0), new Vector3(T(seg) * h, 0, 0),
                            new Vector3(F(seg + 1), 0, 0), new Vector3(T(seg + 1) * h, 0, 0), s);
        return r.x;
    }

    static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float s)
    {
        float s2 = s * s, s3 = s2 * s;
        return (2f * s3 - 3f * s2 + 1f) * p0 + (s3 - 2f * s2 + s) * m0
             + (-2f * s3 + 3f * s2) * p1 + (s3 - s2) * m1;
    }
}