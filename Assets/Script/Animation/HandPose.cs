using UnityEngine;

/// <summary>손가락 하나 — 관절별 로컬 회전 오프셋</summary>
[System.Serializable]
public class FingerPose
{
    /// <summary>뿌리 → 끝 순서. rest 자세 기준 오프셋(도)</summary>
    public Vector3[] Joints = new Vector3[3];

    public FingerPose() { }

    public FingerPose(int jointCount)
    {
        Joints = new Vector3[Mathf.Max(1, jointCount)];
    }

    public void EnsureSize(int n)
    {
        if (Joints == null || Joints.Length != n)
        {
            var old = Joints;
            Joints = new Vector3[n];
            if (old != null)
                for (int i = 0; i < Mathf.Min(old.Length, n); i++) Joints[i] = old[i];
        }
    }

    public static FingerPose Lerp(FingerPose a, FingerPose b, float t)
    {
        if (a == null) return b?.Clone();
        if (b == null) return a.Clone();

        int n = Mathf.Max(a.Joints.Length, b.Joints.Length);
        var r = new FingerPose(n);

        for (int i = 0; i < n; i++)
        {
            Vector3 va = i < a.Joints.Length ? a.Joints[i] : Vector3.zero;
            Vector3 vb = i < b.Joints.Length ? b.Joints[i] : Vector3.zero;
            r.Joints[i] = Vector3.Lerp(va, vb, t);
        }
        return r;
    }

    public FingerPose Clone()
    {
        var c = new FingerPose(Joints.Length);
        System.Array.Copy(Joints, c.Joints, Joints.Length);
        return c;
    }

    /// <summary>단순 굽힘 (0~1)</summary>
    public void SetCurl(float curl, Vector3 axis, float[] weights = null)
    {
        for (int i = 0; i < Joints.Length; i++)
        {
            float w = (weights != null && i < weights.Length) ? weights[i] : 1f;
            Joints[i] = axis * (curl * w);
        }
    }
}

[System.Serializable]
public class HandPose
{
    public const int FingerCount = 4;   // 엄지 검지 중지 약지

    public FingerPose[] Fingers = new FingerPose[FingerCount];

    public enum MirrorAxis { X, Y, Z }

    public HandPose()
    {
        for (int i = 0; i < FingerCount; i++) Fingers[i] = new FingerPose(3);
    }

    public FingerPose Get(int i) =>
        (Fingers != null && i >= 0 && i < Fingers.Length) ? Fingers[i] : null;

    public void EnsureValid()
    {
        if (Fingers == null || Fingers.Length != FingerCount)
        {
            var old = Fingers;
            Fingers = new FingerPose[FingerCount];
            for (int i = 0; i < FingerCount; i++)
                Fingers[i] = (old != null && i < old.Length && old[i] != null)
                    ? old[i] : new FingerPose(3);
        }

        for (int i = 0; i < FingerCount; i++)
            Fingers[i] ??= new FingerPose(3);
    }

    public static HandPose Lerp(HandPose a, HandPose b, float t)
    {
        if (a == null) return b?.Clone();
        if (b == null) return a.Clone();

        var r = new HandPose();
        for (int i = 0; i < FingerCount; i++)
            r.Fingers[i] = FingerPose.Lerp(a.Get(i), b.Get(i), t);
        return r;
    }

    public HandPose Clone()
    {
        var c = new HandPose();
        for (int i = 0; i < FingerCount; i++)
            c.Fingers[i] = Get(i)?.Clone() ?? new FingerPose(3);
        return c;
    }

    
    public HandPose Mirrored(MirrorAxis axis = MirrorAxis.X)
    {
        var m = new HandPose();
        EnsureValid();

        for (int f = 0; f < FingerCount; f++)
        {
            var src = Get(f);
            var dst = new FingerPose(src.Joints.Length);

            for (int j = 0; j < src.Joints.Length; j++)
            {
                Vector3 e = src.Joints[j];

                dst.Joints[j] = axis switch
                {
                    MirrorAxis.X => new Vector3(e.x, -e.y, -e.z),
                    MirrorAxis.Y => new Vector3(-e.x, e.y, -e.z),
                    _            => new Vector3(-e.x, -e.y, e.z),
                };
            }

            m.Fingers[f] = dst;
        }

        return m;
    }

    // ── 프리셋 ──

    public static HandPose Uniform(float curl, Vector3 axis)
    {
        var h = new HandPose();
        float[] w = { 1f, 0.95f, 0.85f };
        for (int i = 0; i < FingerCount; i++)
            h.Fingers[i].SetCurl(curl, axis, w);
        return h;
    }

    public static HandPose Open(Vector3 axis) => Uniform(0f, axis);
    public static HandPose Relaxed(Vector3 axis) => Uniform(0.25f, axis);
    public static HandPose Fist(Vector3 axis) => Uniform(1f, axis);

    public static HandPose Grip(Vector3 axis)
    {
        var h = new HandPose();
        float[] w = { 1f, 0.95f, 0.85f };
        h.Fingers[0].SetCurl(0.55f, axis, w);   // 엄지
        h.Fingers[1].SetCurl(0.85f, axis, w);   // 검지
        h.Fingers[2].SetCurl(0.9f, axis, w);    // 중지
        h.Fingers[3].SetCurl(0.85f, axis, w);   // 약지
        return h;
    }

    public static HandPose Point(Vector3 axis)
    {
        var h = new HandPose();
        float[] w = { 1f, 0.95f, 0.85f };
        h.Fingers[0].SetCurl(0.5f, axis, w);
        h.Fingers[1].SetCurl(0f, axis, w);
        h.Fingers[2].SetCurl(1f, axis, w);
        h.Fingers[3].SetCurl(1f, axis, w);
        return h;
    }
}