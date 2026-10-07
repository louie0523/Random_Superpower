using UnityEngine;

[System.Serializable]
public class ArmPose
{
    [Header("Right — 손 위치(부모) / 회전(손)")]
    public Vector3 RightPos = new(0.22f, -0.18f, 0.42f);
    public Vector3 RightRot = new(5f, -8f, 0f);

    [Header("Left")]
    public bool UseLeftHand = false;
    public Vector3 LeftPos = new(-0.18f, -0.20f, 0.38f);
    public Vector3 LeftRot = new(5f, 12f, 0f);

    [Header("Weapon (추가 오프셋)")]
    public Vector3 WeaponPos;
    public Vector3 WeaponRot;

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

    static Vector3 LerpEuler(Vector3 a, Vector3 b, float t) =>
        Quaternion.Slerp(Quaternion.Euler(a), Quaternion.Euler(b), t).eulerAngles;

    public ArmPose Clone()
    {
        var c = (ArmPose)MemberwiseClone();
        c.RightFingers = RightFingers?.Clone();
        c.LeftFingers = LeftFingers?.Clone();
        return c;
    }
}

/// <summary>키프레임 하나</summary>
[System.Serializable]
public class PoseKey
{
    [Range(0f, 1f)] public float Time;
    public ArmPose Pose = new();
    [Tooltip("이 키로 향하는 보간 곡선")]
    public AnimationCurve Ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
}