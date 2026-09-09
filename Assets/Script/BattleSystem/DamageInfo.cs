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