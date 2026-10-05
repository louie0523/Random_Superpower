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

/// <summary>
/// 피해 정보. 대상의 정확한 체력·방어력은 대상만 알기 때문에
/// %·조건부 계산(최대체력 %, 처형, 관통)은 값만 실어 보내고 대상 쪽(PlayerHealth.RPC_TakeDamage)에서 계산.
/// </summary>
public struct DamageInfo : INetworkStruct
{
    public int Amount;
    public PlayerRef Attacker;
    public Vector3 SourcePos;

    /// <summary>플레이어가 직접 가한 피해인가</summary>
    public NetworkBool FromPlayer;

    /// <summary>넉백 등으로 낙사를 유발할 수 있는 피해인가</summary>
    public NetworkBool CanCauseFall;

    /// <summary>대상 최대 체력의 N%를 추가</summary>
    public float MaxHpPercent;

    /// <summary>방어력·받는 피해 배율을 무시하는 고정 피해</summary>
    public NetworkBool IgnoreDefense;

    /// <summary>지속 피해·부가 피해 — 피격 트리거(OnHitTaken)를 발동하지 않음</summary>
    public NetworkBool IsDot;

    /// <summary>방어력 관통 비율 (0.2 = 방어력 20% 무시)</summary>
    public float DefensePen;

    /// <summary>처형 — 대상 체력이 최대의 이 비율 이하면 (0.3 = 30%)</summary>
    public float ExecuteBelow;

    /// <summary>처형 — 그때 피해 증가 비율 (0.3 = +30%)</summary>
    public float ExecuteBonus;

    /// <summary>치명타였는가 (데미지 숫자 표시용)</summary>
    public NetworkBool IsCrit;

    /// <summary>상태이상 지속 피해 틱 (화상·중독) — 회피 중에도 들어감</summary>
    public NetworkBool IsStatusTick;

    /// <summary>IsStatusTick일 때 어떤 상태이상인지 (StatusType 값, 숫자 색 구분용)</summary>
    public int TickStatus;

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