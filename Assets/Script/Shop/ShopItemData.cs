using UnityEngine;

public enum ShopItemKind
{
    Stat,          // 스탯 영구 증가
    SkillRank,     // 능력 스킬 랭크업
    InstantHeal,   // 즉시 회복 (레벨 없음, 반복 구매)
    ResetEnchant,  // 인첸트 초기화
}

[CreateAssetMenu(menuName = "Game/Shop Item")]
public class ShopItemData : ScriptableObject
{
    [Header("Identity")]
    public int Id;
    public string DisplayName;
    public Sprite Icon;
    [TextArea(2, 3)] public string Description;

    public ShopItemKind Kind = ShopItemKind.Stat;

    [Header("가격")]
    public int BasePrice = 20;
    [Tooltip("레벨당 가격 배율")]
    public float PriceGrowth = 1.5f;
    [Range(1, 5)] public int MaxLevel = 3;

    [Header("Stat 전용")]
    public StatType Stat;
    public ModType ModType = ModType.Flat;
    [Tooltip("레벨별 최종 수치. 누적이 아니라 대체")]
    public float[] ValuePerLevel = { 10f };

    [Header("InstantHeal 전용")]
    public int HealAmount = 40;

    [Header("ResetEnchant 전용")]
    [Tooltip("단계당 추가 비용")]
    public int PricePerStage = 8;

    public int GetPrice(int currentLevel) =>
        Mathf.RoundToInt(BasePrice * Mathf.Pow(PriceGrowth, currentLevel));

    /// <summary>레벨 1부터 시작</summary>
    public float GetValue(int level)
    {
        if (ValuePerLevel == null || ValuePerLevel.Length == 0) return 0f;
        return ValuePerLevel[Mathf.Clamp(level - 1, 0, ValuePerLevel.Length - 1)];
    }

    public bool IsRepeatable => Kind == ShopItemKind.InstantHeal;
}