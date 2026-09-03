using UnityEngine;

/// <summary>한 시점의 양손 + 무기 상태</summary>
[System.Serializable]
public class ArmPose
{
    [Header("Right Hand (local to ArmRoot)")]
    public Vector3 RightPos = new(0.22f, -0.18f, 0.42f);
    public Vector3 RightRot = new(5f, -8f, 0f);

    [Header("Left Hand")]
    public bool UseLeftHand = false;
    public Vector3 LeftPos = new(-0.18f, -0.20f, 0.38f);
    public Vector3 LeftRot = new(5f, 12f, 0f);

    [Header("Weapon (local to grip hand)")]
    public Vector3 WeaponPos;
    public Vector3 WeaponRot;

    public static ArmPose Lerp(ArmPose a, ArmPose b, float t)
    {
        return new ArmPose
        {
            RightPos = Vector3.Lerp(a.RightPos, b.RightPos, t),
            RightRot = LerpEuler(a.RightRot, b.RightRot, t),
            UseLeftHand = t < 0.5f ? a.UseLeftHand : b.UseLeftHand,
            LeftPos = Vector3.Lerp(a.LeftPos, b.LeftPos, t),
            LeftRot = LerpEuler(a.LeftRot, b.LeftRot, t),
            WeaponPos = Vector3.Lerp(a.WeaponPos, b.WeaponPos, t),
            WeaponRot = LerpEuler(a.WeaponRot, b.WeaponRot, t),
        };
    }

    static Vector3 LerpEuler(Vector3 a, Vector3 b, float t) =>
        Quaternion.Slerp(Quaternion.Euler(a), Quaternion.Euler(b), t).eulerAngles;

    public ArmPose Clone() => (ArmPose)MemberwiseClone();
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