using Fusion;
using UnityEngine;

public enum DeathCause
{
    Player,         // 플레이어에게 처치됨
    Environment,    // 지형/낙사/기믹
    Suicide,        // 자해
    Entity,
    Unknown,
}

public struct DamageInfo : INetworkStruct
{
    public int Amount;
    public PlayerRef Attacker;
    public Vector3 SourcePos;

    /// <summary>플레이어가 직접 가한 피해인가</summary>
    public NetworkBool FromPlayer;

    /// <summary>넉백 등으로 낙사를 유발할 수 있는 피해인가</summary>
    public NetworkBool CanCauseFall;

    /// <summary>대상 최대 체력의 N%를 추가 (대상 쪽에서 계산 — 상대 최대 체력은 상대만 정확히 앎)</summary>
    public float MaxHpPercent;

    /// <summary>방어력·받는 피해 배율을 무시하는 고정 피해</summary>
    public NetworkBool IgnoreDefense;

    /// <summary>화상 같은 지속 피해 — 피격 트리거(OnHitTaken)를 발동하지 않음</summary>
    public NetworkBool IsDot;

    public static DamageInfo FromPlayerHit(int amount, PlayerRef attacker, Vector3 pos,
                                           bool canCauseFall = false) => new()
                                           {
                                               Amount = amount,
                                               Attacker = attacker,
                                               SourcePos = pos,
                                               FromPlayer = true,
                                               CanCauseFall = canCauseFall,
                                           };

    public static DamageInfo FromEnvironment(int amount, Vector3 pos) => new()
    {
        Amount = amount,
        Attacker = PlayerRef.None,
        SourcePos = pos,
        FromPlayer = false,
        CanCauseFall = false,
    };
}