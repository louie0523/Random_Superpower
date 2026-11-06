using System;
using UnityEngine;

/// <summary>
/// 상점 한정판 · 암시장 규칙 — Create → Game → Shop → Market Rules → Resources 폴더에 'MarketRules' 이름으로 (없으면 기본값).
/// 가격 = (등급 가격 + 아이템 추가 가격) × (1 + 강화당 % × 강화) × (1 + 특수 인챈트당 % × 개수) × (1 − 할인)
/// </summary>
[CreateAssetMenu(menuName = "Game/Shop/Market Rules", fileName = "MarketRules")]
public class MarketRules : ScriptableObject
{
    [Serializable]
    public class DiscountBand
    {
        [Range(0f, 0.95f)] public float Min = 0.1f;
        [Range(0f, 0.95f)] public float Max = 0.2f;
        [Min(0f)] public float Weight = 50f;
    }

    [Serializable]
    public class OfferRules
    {
        [Header("구성")]
        [Min(1)] public int Count = 5;
        [Tooltip("한 칸이 방어구일 확률 (나머지는 무기)")]
        [Range(0f, 1f)] public float ArmorChance = 0.4f;
        [Tooltip("방어구 최대 개수 (무기는 전부여도 됨)")]
        [Min(0)] public int MaxArmor = 3;

        [Header("등급 — 판 시간이 갈수록 '처음' → '후반' 확률로 (C · B · A · S)")]
        public ItemGrade MinGrade = ItemGrade.C;
        public float[] GradeWeightEarly = { 60f, 30f, 10f, 0f };
        public float[] GradeWeightLate = { 15f, 30f, 35f, 20f };
        [Tooltip("몇 분에 '후반' 확률이 되나")]
        [Min(0.1f)] public float LateMinutes = 20f;
        [Tooltip("한 번에 S급 최대 개수 (0 = 제한 없음)")]
        [Min(0)] public int MaxS = 2;

        [Header("강화")]
        [Min(0)] public int MinStage = 1;
        [Min(0)] public int MaxStage = 10;
        [Tooltip("방어구에 특수 인챈트가 붙어 나올 확률")]
        [Range(0f, 1f)] public float ArmorEnchantChance = 0.5f;

        [Header("가격")]
        [Tooltip("등급별 기본 가격 — C · B · A · S")]
        public int[] GradePrice = { 50, 110, 200, 380 };
        [Tooltip("강화 1단계마다 (0.05 = +5%)")]
        [Min(0f)] public float PerStage = 0.05f;
        [Tooltip("특수 인챈트 1개마다 (0.2 = +20%)")]
        [Min(0f)] public float PerSpecial = 0.2f;
        [Tooltip("상품마다 할인 (비우면 할인 없음) — 무게대로 하나 골라 그 범위에서")]
        public DiscountBand[] Discounts = new DiscountBand[0];
    }

    [Header("상점 한정판")]
    [Min(0.5f)] public float ShopRotateMinutes = 5f;
    public OfferRules Shop = new();

    [Header("암시장")]
    public OfferRules Market = new()
    {
        MinGrade = ItemGrade.B,
        GradeWeightEarly = new[] { 0f, 55f, 35f, 10f },
        GradeWeightLate = new[] { 0f, 20f, 40f, 40f },
        LateMinutes = 14f,
        MaxS = 0,
        MinStage = 1,
        MaxStage = 20,
        ArmorEnchantChance = 0.6f,
        PerStage = 0.02f,
        PerSpecial = 0.1f,
        Discounts = new[]
        {
            new DiscountBand { Min = 0.10f, Max = 0.20f, Weight = 50f },
            new DiscountBand { Min = 0.21f, Max = 0.35f, Weight = 40f },
            new DiscountBand { Min = 0.36f, Max = 0.50f, Weight = 10f },
        },
    };
    [Tooltip("판이 시작되고 첫 암시장이 열리기까지 (분, 최소 ~ 최대)")]
    public Vector2 FirstOpenMinutes = new(3f, 5f);
    [Tooltip("누가 사서 닫힌 뒤 다음 암시장이 열리기까지 (분, 최소 ~ 최대)")]
    public Vector2 ReopenMinutes = new(7f, 11f);

    [Header("행방불명 (낙사 구역에 떨어져 사라진 무기)")]
    [Tooltip("암시장이 열릴 때 행방불명 무기가 한 칸에 나올 확률")]
    [Range(0f, 1f)] public float LostChance = 0.25f;
    [Tooltip("행방불명 무기 추가 할인")]
    [Range(0f, 0.5f)] public float LostExtraDiscount = 0.1f;

    static MarketRules _cached;

    public static MarketRules Get()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<MarketRules>("MarketRules");
        if (_cached == null) _cached = CreateInstance<MarketRules>();
        return _cached;
    }
}
