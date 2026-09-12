using UnityEngine;

public enum HandVisibility
{
    Auto,           // 무기 그립 설정을 따름 (기본)
    RightOnly,      // 오른손만
    LeftOnly,       // 왼손만
    Both,           // 양손
}

[System.Serializable]
public class ComboStep
{
    [Header("Info")]
    public string Name = "1타";

    [Header("Timing")]
    [Tooltip("공격 전체 길이 (초)")]
    public float Duration = 0.55f;

    [Tooltip("판정 시작 지점 (0~1, 전체 대비 비율)")]
    [Range(0f, 1f)] public float HitStart = 0.27f;

    [Tooltip("판정 종료 지점 (0~1)")]
    [Range(0f, 1f)] public float HitEnd = 0.45f;

    [Tooltip("다음 타로 연결 가능한 지점 (0~1)")]
    [Range(0f, 1f)] public float CancelPoint = 0.75f;
    [Tooltip("이 시간 안에 입력 없으면 콤보 초기화")]
    public float ComboResetTime = 0.8f;

    [Header("Hit Override")]
    [Tooltip("체크하면 이 타만 별도 판정 범위 사용")]
    public bool OverrideHitShape = false;

    public WeaponHitMode HitMode = WeaponHitMode.ForwardArc;
    public float Range = 3f;
    [Range(20f, 360f)] public float HitAngle = 120f;
    public float HitHeight = 1.2f;
    public float MinRange = 0f;

    [Header("Damage")]
    [Tooltip("무기 공격력 대비 배율")]
    public float DamageMultiplier = 1f;
    public float RangeMultiplier = 1f;
    public float RadiusMultiplier = 1f;
    public int MaxTargets = 1;

    [Header("Motion")]
    public SwingMotion Motion = new();

    [Header("Feel")]
    [Tooltip("맞았을 때 화면 정지 시간")]
    public float HitStopDuration = 0.05f;
    public float CameraKickFov = 4f;
    public float CameraShake = 0.3f;
    [Tooltip("전진 거리 (m)")]
    public float LungeDistance = 0f;

    [Header("Hand")]
    [Tooltip("이 모션 동안 손 표시 방식")]
    public HandVisibility HandMode = HandVisibility.Auto;

    [Header("FX")]
    public AudioClip SwingSound;
    public GameObject TrailPrefab;

    [Header("Events")]
    public MotionEvent[] Events;

    public float TotalTime => Duration;
    public float WindUp => Duration * HitStart;
    public float ActiveWindow => Duration * (HitEnd - HitStart);
    public float Recovery => Duration * (1f - HitEnd);
    public float CancelTime => Duration * CancelPoint;

    /// <summary>이 타가 실제로 쓸 판정 값</summary>
    public void ResolveHitShape(EquipmentData weapon, out WeaponHitMode mode, out float range, out float angle, out float height, out float minRange)
    {
        if (OverrideHitShape)
        {
            mode = HitMode;
            range = Range;
            angle = HitAngle;
            height = HitHeight;
            minRange = MinRange;
        }
        else
        {
            mode = weapon.HitMode;
            range = weapon.Range * RangeMultiplier;
            angle = weapon.HitAngle;
            height = weapon.HitHeight;
            minRange = weapon.MinRange;
        }
    }
}