using UnityEngine;

/// <summary>
/// 무기 슬롯에 저장되는 인챈트 값의 규칙.
///   일반 인챈트 = EnchantData.Id 그대로 (9000번대 등)
///   특수 인챈트 = 1,000,000 + 특수ID × 10 + 등급(1~9)
/// 같은 특수 인챈트를 업그레이드하면 더 높은 등급 값이 새 단계로 추가되고, 효과는 가장 높은 등급만 적용.
/// </summary>
public static class EnchantCode
{
    public const int SpecialFlag = 1_000_000;
    public const int MaxTier = 9;

    public static bool IsSpecial(int code) => code >= SpecialFlag;

    public static int Make(int specialId, int tier) =>
        SpecialFlag + specialId * 10 + Mathf.Clamp(tier, 1, MaxTier);

    public static int SpecialId(int code) => (code - SpecialFlag) / 10;
    public static int Tier(int code) => (code - SpecialFlag) % 10;
}
