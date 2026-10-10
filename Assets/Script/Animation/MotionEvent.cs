using UnityEngine;

/// <summary>순서가 저장값 — 중간 삽입 금지, 뒤에만 추가</summary>
public enum MotionEventType
{
    Sound,          // 사운드 재생
    Effect,         // 프리팹 생성
    TrailStart,     // 무기 트레일 켜기
    TrailEnd,       // 무기 트레일 끄기
    CameraShake,    // 화면 흔들림
    Custom,         // 문자열 키로 코드에서 처리

    // ── 추가 ──
    RandomSound,    // 여러 소리 중 하나 (피치 랜덤)
    ScreenFlash,    // 화면 번쩍임 (본인만)
    FovKick,        // 시야각 순간 변화 (본인만)
    LightFlash,     // 순간 조명
    GroundEffect,   // 발밑 지면에 프리팹 (충격파·자국)
    Vfx,            // VFX Studio 이펙트 (풀링, 판정 범위에 맞추기)
}

/// <summary>순서가 저장값 — 뒤에만 추가</summary>
public enum EventSpace
{
    World,          // 생성 시점의 월드 좌표에 고정
    AttachToPlayer, // 플레이어를 따라감
    AttachToWeapon, // 무기 끝을 따라감
    AttachToCamera, // 카메라 기준 (1인칭 전용)

    // ── 추가 ──
    AttachToMuzzle, // 총구·발사 지점
    AttachToBody,   // 몸통 (FPArmRig.Body)
    AttachToHead,   // 머리 (FPArmRig.Head)
    AttachToRightHand,
    AttachToLeftHand,
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

    [Header("Random Sound")]
    public AudioClip[] Clips;
    [Tooltip("피치 랜덤 폭 (±)")]
    [Range(0f, 0.5f)] public float PitchJitter = 0.08f;

    [Header("Effect · Ground")]
    public GameObject Prefab;
    public EventSpace Space = EventSpace.AttachToPlayer;
    public Vector3 PositionOffset;
    public Vector3 RotationOffset;
    public float Scale = 1f;
    [Tooltip("0이면 프리팹이 스스로 정리")]
    public float Lifetime = 2f;

    [Header("VFX")]
    [Tooltip("VFX Studio로 만든 이펙트. 붙을 곳·위치·회전·크기는 위 Effect 칸을 같이 씀")]
    public ToonVfx.VfxEffect Vfx;
    [Tooltip("켜면 호의 반지름·각도를 이 타격의 실제 판정 범위에 맞춤 (사거리 버프도 반영)")]
    public bool FitToHit = true;
    [Tooltip("비우면 이펙트 기본 색")]
    public ToonVfx.VfxPalette VfxPalette;

    [Header("Shake")]
    public float ShakeAmount = 0.3f;

    [Header("Flash · Light")]
    public Color FlashColor = new(1f, 1f, 1f, 0.35f);
    [Tooltip("번쩍임·조명 지속 (초)")]
    public float FlashDuration = 0.12f;
    public float LightIntensity = 4f;
    public float LightRange = 6f;

    [Header("FOV")]
    [Tooltip("+ 넓어짐 / − 좁아짐")]
    public float FovAmount = -4f;

    [Header("Custom")]
    public string Key;

    [Header("View")]
    [Tooltip("1인칭(본인)에서 재생")]
    public bool ShowFirstPerson = true;
    [Tooltip("3인칭(타인)에서 재생")]
    public bool ShowThirdPerson = true;

    public MotionEvent Clone()
    {
        var c = (MotionEvent)MemberwiseClone();
        c.Clips = Clips != null ? (AudioClip[])Clips.Clone() : null;
        return c;
    }
}