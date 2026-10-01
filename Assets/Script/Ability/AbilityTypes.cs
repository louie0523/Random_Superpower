using UnityEngine;

public enum DashDirection
{
    [Tooltip("몸통 전방. 위아래를 봐도 수평으로만")]
    Horizontal,

    [Tooltip("카메라가 보는 방향 그대로")]
    CameraForward,

    [Tooltip("이동 입력 방향 (WASD)")]
    MoveInput,

    [Tooltip("뒤로")]
    Backward,
}

[System.Serializable]
public struct DashParams
{
    [Tooltip("돌진 거리 (m)")]
    public float Distance;

    [Tooltip("돌진에 걸리는 시간 (초)")]
    public float Duration;

    [Tooltip("돌진 방향 기준")]
    public DashDirection Direction;

    [Tooltip("CameraForward일 때 위아래 각도 제한 (도). 0이면 무제한")]
    public float MaxPitch;

    [Tooltip("돌진 중 플레이어를 통과")]
    public bool PassThroughPlayers;

    [Tooltip("무언가에 부딪히면 즉시 정지")]
    public bool StopOnContact;

    [Tooltip("돌진 중 중력 무시")]
    public bool IgnoreGravity;

    public static DashParams Default => new()
    {
        Distance = 5f,
        Duration = 0.2f,
        Direction = DashDirection.Horizontal,
        MaxPitch = 45f,
        PassThroughPlayers = false,
        StopOnContact = true,
        IgnoreGravity = false,
    };
}

public struct AttackOverride
{
    public ComboStep Step;
    public float DamageMul;
    public DashParams Dash;
    public bool HasDash;

    public static AttackOverride None => new()
    {
        Step = null,
        DamageMul = 1f,
        Dash = DashParams.Default,
        HasDash = false,
    };
}