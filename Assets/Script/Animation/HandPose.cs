using UnityEngine;

[System.Serializable]
public class ArmPose
{
    [Header("Right Hand")]
    public Vector3 RightPos = new(0.22f, -0.18f, 0.42f);
    public Vector3 RightRot = new(5f, -8f, 0f);

    [Header("Left Hand")]
    public bool UseLeftHand = false;
    public bool LeftFollowsWeapon = false;
    public Vector3 LeftPos = new(-0.18f, -0.20f, 0.38f);
    public Vector3 LeftRot = new(5f, 12f, 0f);

    [Header("Weapon")]
    public Vector3 WeaponPos;
    public Vector3 WeaponRot;

    [Header("Camera")]
    [Tooltip("카메라 위치 오프셋 (m). Z-가 뒤로 빠짐")]
    public Vector3 CamPos;
    [Tooltip("카메라 회전 오프셋 (도). X-가 위를 봄, Z가 롤")]
    public Vector3 CamRot;
    [Tooltip("FOV 추가량")]
    public float CamFov;

    public static ArmPose Lerp(ArmPose a, ArmPose b, float t)
    {
        return new ArmPose
        {
            RightPos = Vector3.Lerp(a.RightPos, b.RightPos, t),
            RightRot = LerpEuler(a.RightRot, b.RightRot, t),
            UseLeftHand = t < 0.5f ? a.UseLeftHand : b.UseLeftHand,
            LeftFollowsWeapon = t < 0.5f ? a.LeftFollowsWeapon : b.LeftFollowsWeapon,
            LeftPos = Vector3.Lerp(a.LeftPos, b.LeftPos, t),
            LeftRot = LerpEuler(a.LeftRot, b.LeftRot, t),
            WeaponPos = Vector3.Lerp(a.WeaponPos, b.WeaponPos, t),
            WeaponRot = LerpEuler(a.WeaponRot, b.WeaponRot, t),
            CamPos = Vector3.Lerp(a.CamPos, b.CamPos, t),
            CamRot = LerpEuler(a.CamRot, b.CamRot, t),
            CamFov = Mathf.Lerp(a.CamFov, b.CamFov, t),
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