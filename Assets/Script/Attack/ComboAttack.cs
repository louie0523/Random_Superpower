using UnityEngine;

[System.Serializable]
public class ComboStep
{
    [Header("Info")]
    public string Name = "1타";

    [Header("Timing (초)")]
    public float WindUp = 0.15f;
    [Tooltip("판정이 열려있는 시간")]
    public float ActiveWindow = 0.10f;
    public float Recovery = 0.35f;

    [Header("Combo Window")]
    [Tooltip("이 시점 이후 입력하면 다음 타로 연결 (WindUp+Active 기준 비율)")]
    [Range(0f, 1f)] public float CancelPoint = 0.6f;
    [Tooltip("이 시간 안에 입력 없으면 콤보 초기화")]
    public float ComboResetTime = 0.8f;

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

    [Header("FX")]
    public AudioClip SwingSound;
    public GameObject TrailPrefab;

    public float TotalTime => WindUp + ActiveWindow + Recovery;
    public float CancelTime => (WindUp + ActiveWindow) * CancelPoint;
}