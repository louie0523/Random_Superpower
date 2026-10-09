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

    public bool IsValid => Keys != null && Keys.Count >= 2;

    /// <summary>t는 0~1</summary>
    public ArmPose Evaluate(float t, ArmPose idle)
    {
        if (!IsValid) return idle?.Clone();

        t = Mathf.Clamp01(t);
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