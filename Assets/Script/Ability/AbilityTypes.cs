using UnityEngine;

[System.Serializable]
public struct DashParams
{
    [Tooltip("돌진 거리 (m)")]
    public float Distance;

    [Tooltip("돌진에 걸리는 시간 (초)")]
    public float Duration;

    [Tooltip("돌진 중 플레이어를 통과")]
    public bool PassThroughPlayers;

    [Tooltip("무언가에 부딪히면 즉시 정지")]
    public bool StopOnContact;

    public static DashParams Default => new()
    {
        Distance = 5f,
        Duration = 0.2f,
        PassThroughPlayers = false,
        StopOnContact = true,
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