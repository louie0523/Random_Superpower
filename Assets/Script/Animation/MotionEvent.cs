using UnityEngine;

public enum MotionEventType
{
    Sound,          // 사운드 재생
    Effect,         // 프리팹 생성
    TrailStart,     // 무기 트레일 켜기
    TrailEnd,       // 무기 트레일 끄기
    CameraShake,    // 화면 흔들림
    Custom,         // 문자열 키로 코드에서 처리
}

public enum EventSpace
{
    World,          // 생성 시점의 월드 좌표에 고정
    AttachToPlayer, // 플레이어를 따라감
    AttachToWeapon, // 무기 끝을 따라감
    AttachToCamera, // 카메라 기준 (1인칭 전용)
}

[System.Serializable]
public class MotionEvent
{
    [Range(0f, 1f)]
    [Tooltip("모션 진행률 (0~1)")]
    public float Time = 0.5f;

    public MotionEventType Type = MotionEventType.Effect;

    [Header("Sound")]
    public AudioClip Clip;
    [Range(0f, 1f)] public float Volume = 0.8f;
    [Range(0.5f, 1.5f)] public float Pitch = 1f;

    [Header("Effect")]
    public GameObject Prefab;
    public EventSpace Space = EventSpace.AttachToPlayer;
    public Vector3 PositionOffset;
    public Vector3 RotationOffset;
    public float Scale = 1f;
    [Tooltip("0이면 프리팹이 스스로 정리")]
    public float Lifetime = 2f;

    [Header("Shake")]
    public float ShakeAmount = 0.3f;

    [Header("Custom")]
    public string Key;

    [Header("View")]
    [Tooltip("1인칭(본인)에서 재생")]
    public bool ShowFirstPerson = true;
    [Tooltip("3인칭(타인)에서 재생")]
    public bool ShowThirdPerson = true;
}