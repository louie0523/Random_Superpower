using UnityEngine;

/// <summary>추가 파츠(1번부터) 하나의 키 값 — 부모 기준 추가 위치 · 회전 · 크기 배율</summary>
[System.Serializable]
public class PartPose
{
    public Vector3 Pos;
    public Vector3 Rot;
    [Tooltip("크기 배율 — 0인 축은 1")]
    public Vector3 Scale;

    public PartPose Clone() => (PartPose)MemberwiseClone();
}

[System.Serializable]
public class ArmPose
{
    /// <summary>파츠 최대 수 (0번 메인 포함) — 둥둥 배열 크기</summary>
    public const int MaxParts = 8;

    [Header("Right — 손 위치(부모) / 회전(손)")]
    public Vector3 RightPos = new(0.22f, -0.18f, 0.42f);
    public Vector3 RightRot = new(5f, -8f, 0f);

    [Header("Left")]
    public bool UseLeftHand = false;
    public Vector3 LeftPos = new(-0.18f, -0.20f, 0.38f);
    public Vector3 LeftRot = new(5f, 12f, 0f);

    [Header("Weapon (추가 오프셋) — 0번 파츠(메인 모델)")]
    public Vector3 WeaponPos;
    public Vector3 WeaponRot;
    [Tooltip("무기 크기 배율 (무기 기본 크기에 곱함). 0인 축은 1로 봄 — 예전 키는 전부 0이라 그대로 1배")]
    public Vector3 WeaponScale;

    [Tooltip("추가 파츠의 추가 위치 · 회전 · 크기 — [0] = 1번 파츠, [1] = 2번 …")]
    public PartPose[] Parts;

    /// <summary>둥둥 (모션의 둥둥 구간) — 파츠별 [0 = 메인]. 저장 안 함, 재생할 때만 SwingMotion이 채움</summary>
    [System.NonSerialized] public Vector3[] FloatPos, FloatRot;

    [Header("Camera")]
    public Vector3 CamPos;
    public Vector3 CamRot;
    public float CamFov;

    [Header("Body — 기준 자세 대비 오프셋 (다른 사람에게 보임)")]
    public Vector3 BodyPos;
    public Vector3 BodyRot;

    [Header("Head — 기준 자세 대비 오프셋")]
    public Vector3 HeadPos;
    public Vector3 HeadRot;

    [System.NonSerialized]
    public HandVisibility HandOverride = HandVisibility.Auto;

    [Header("Fingers")]
    public bool UseWeaponGrip = true;
    public HandPose RightFingers;
    public HandPose LeftFingers;

    // ══════════════════ 파츠 ══════════════════

    /// <summary>파츠 i(0 = 메인)의 추가 위치</summary>
    public Vector3 PartPos(int i) => i <= 0 ? WeaponPos : ExtraPos(i - 1);
    public Vector3 PartRot(int i) => i <= 0 ? WeaponRot : ExtraRot(i - 1);
    /// <summary>파츠 i의 크기 배율 (0인 축은 1)</summary>
    public Vector3 PartScale(int i) => Scl(i <= 0 ? WeaponScale : ExtraScaleRaw(i - 1));

    /// <summary>파츠 i의 값을 넣음 (배열이 짧으면 늘림)</summary>
    public void SetPart(int i, Vector3 pos, Vector3 rot, Vector3 scale)
    {
        if (i <= 0) { WeaponPos = pos; WeaponRot = rot; WeaponScale = scale; return; }
        var e = Extra(i - 1);
        e.Pos = pos; e.Rot = rot; e.Scale = scale;
    }

    // 추가 파츠 (j = 0이 1번 파츠)
    public Vector3 ExtraPos(int j) => Parts != null && j >= 0 && j < Parts.Length && Parts[j] != null ? Parts[j].Pos : Vector3.zero;
    public Vector3 ExtraRot(int j) => Parts != null && j >= 0 && j < Parts.Length && Parts[j] != null ? Parts[j].Rot : Vector3.zero;
    Vector3 ExtraScaleRaw(int j) => Parts != null && j >= 0 && j < Parts.Length && Parts[j] != null ? Parts[j].Scale : Vector3.zero;

    /// <summary>추가 파츠 j의 값 (없으면 만듦)</summary>
    public PartPose Extra(int j)
    {
        if (j < 0) return null;
        if (Parts == null || Parts.Length <= j) System.Array.Resize(ref Parts, j + 1);
        return Parts[j] ??= new PartPose();
    }

    /// <summary>파츠 i의 둥둥 (없으면 0)</summary>
    public Vector3 PartFloatPos(int i) => FloatPos != null && i >= 0 && i < FloatPos.Length ? FloatPos[i] : Vector3.zero;
    public Vector3 PartFloatRot(int i) => FloatRot != null && i >= 0 && i < FloatRot.Length ? FloatRot[i] : Vector3.zero;

    public void AddFloat(int i, Vector3 pos, Vector3 rot)
    {
        if (i < 0 || i >= MaxParts) return;
        FloatPos ??= new Vector3[MaxParts];
        FloatRot ??= new Vector3[MaxParts];
        FloatPos[i] += pos;
        FloatRot[i] += rot;
    }

    // ══════════════════ 보간 ══════════════════

    public static ArmPose Lerp(ArmPose a, ArmPose b, float t)
    {
        if (a == null) return b;
        if (b == null) return a;

        return new ArmPose
        {
            RightPos = Vector3.Lerp(a.RightPos, b.RightPos, t),
            RightRot = LerpEuler(a.RightRot, b.RightRot, t),
            UseLeftHand = t < 0.5f ? a.UseLeftHand : b.UseLeftHand,
            LeftPos = Vector3.Lerp(a.LeftPos, b.LeftPos, t),
            LeftRot = LerpEuler(a.LeftRot, b.LeftRot, t),
            WeaponPos = Vector3.Lerp(a.WeaponPos, b.WeaponPos, t),
            WeaponRot = LerpEuler(a.WeaponRot, b.WeaponRot, t),
            WeaponScale = Vector3.Lerp(Scl(a.WeaponScale), Scl(b.WeaponScale), t),
            Parts = LerpParts(a, b, t),
            FloatPos = LerpArr(a.FloatPos, b.FloatPos, t),
            FloatRot = LerpArr(a.FloatRot, b.FloatRot, t),
            CamPos = Vector3.Lerp(a.CamPos, b.CamPos, t),
            CamRot = LerpEuler(a.CamRot, b.CamRot, t),
            CamFov = Mathf.Lerp(a.CamFov, b.CamFov, t),
            BodyPos = Vector3.Lerp(a.BodyPos, b.BodyPos, t),
            BodyRot = LerpEuler(a.BodyRot, b.BodyRot, t),
            HeadPos = Vector3.Lerp(a.HeadPos, b.HeadPos, t),
            HeadRot = LerpEuler(a.HeadRot, b.HeadRot, t),
            UseWeaponGrip = t < 0.5f ? a.UseWeaponGrip : b.UseWeaponGrip,
            RightFingers = HandPose.Lerp(a.RightFingers, b.RightFingers, t),
            LeftFingers = HandPose.Lerp(a.LeftFingers, b.LeftFingers, t),
            HandOverride = t < 0.5f ? a.HandOverride : b.HandOverride,
        };
    }

    static PartPose[] LerpParts(ArmPose a, ArmPose b, float t)
    {
        int n = Mathf.Max(a.Parts?.Length ?? 0, b.Parts?.Length ?? 0);
        if (n == 0) return null;
        var r = new PartPose[n];
        for (int j = 0; j < n; j++)
            r[j] = new PartPose
            {
                Pos = Vector3.Lerp(a.ExtraPos(j), b.ExtraPos(j), t),
                Rot = LerpEuler(a.ExtraRot(j), b.ExtraRot(j), t),
                Scale = Vector3.Lerp(Scl(a.ExtraScaleRaw(j)), Scl(b.ExtraScaleRaw(j)), t),
            };
        return r;
    }

    static Vector3[] LerpArr(Vector3[] a, Vector3[] b, float t)
    {
        if (a == null && b == null) return null;
        var r = new Vector3[MaxParts];
        for (int i = 0; i < MaxParts; i++)
        {
            Vector3 va = a != null && i < a.Length ? a[i] : Vector3.zero;
            Vector3 vb = b != null && i < b.Length ? b[i] : Vector3.zero;
            r[i] = Vector3.Lerp(va, vb, t);
        }
        return r;
    }

    /// <summary>크기 배율 — 0인 축은 1 (안 정한 키)</summary>
    public static Vector3 Scl(Vector3 s) =>
        new(Mathf.Approximately(s.x, 0f) ? 1f : s.x, Mathf.Approximately(s.y, 0f) ? 1f : s.y, Mathf.Approximately(s.z, 0f) ? 1f : s.z);

    static Vector3 LerpEuler(Vector3 a, Vector3 b, float t) =>
        Quaternion.Slerp(Quaternion.Euler(a), Quaternion.Euler(b), t).eulerAngles;

    public ArmPose Clone()
    {
        var c = (ArmPose)MemberwiseClone();
        c.RightFingers = RightFingers?.Clone();
        c.LeftFingers = LeftFingers?.Clone();
        // 배열은 따로 복사 — 재생 중 더하는 값(둥둥)이 키 원본을 바꾸지 않게
        if (Parts != null)
        {
            c.Parts = new PartPose[Parts.Length];
            for (int j = 0; j < Parts.Length; j++) c.Parts[j] = Parts[j]?.Clone();
        }
        c.FloatPos = FloatPos != null ? (Vector3[])FloatPos.Clone() : null;
        c.FloatRot = FloatRot != null ? (Vector3[])FloatRot.Clone() : null;
        return c;
    }
}

/// <summary>키프레임 하나</summary>
[System.Serializable]
public class PoseKey
{
    [Range(0f, 1f)] public float Time;
    public ArmPose Pose = new();
    [Tooltip("이 키로 향하는 보간 곡선 (Linear 보간에서만)")]
    public AnimationCurve Ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Smooth 보간 — 이 키에서 잠깐 멈춤 (치켜든 자세 등). 끄면 그대로 통과")]
    public bool Hold;
}
