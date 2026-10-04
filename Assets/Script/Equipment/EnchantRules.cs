using UnityEngine;

/// <summary>
/// 인챈트 공통 규칙 — 등급·단계별 스톤 비용, 한계 해제(대장장이), 특수 인챈트 등급 확률.
/// 프로젝트에 하나만 만들어 PlayerEnchant에 연결.
/// </summary>
[CreateAssetMenu(menuName = "Game/Enchant Rules")]
public class EnchantRules : ScriptableObject
{
    [System.Serializable]
    public class CostBand
    {
        [Tooltip("이 강화 단계부터 적용. 1 = 1강을 만들 때부터")]
        [Min(1)] public int FromStage = 1;

        [Tooltip("등급별 스톤 비용 — [0] C, [1] B, [2] A, [3] S")]
        public int[] CostByGrade = { 1, 2, 2, 3 };

        public int Get(ItemGrade g) =>
            CostByGrade == null || CostByGrade.Length == 0
                ? 1
                : Mathf.Max(1, CostByGrade[Mathf.Clamp((int)g, 0, CostByGrade.Length - 1)]);
    }

    [Header("스톤 비용")]
    [Tooltip("만들려는 단계가 속하는 구간(FromStage가 그 단계 이하인 것 중 가장 뒤)의 비용을 씀.\n" +
             "예) 1강~ : 1,2,2,3  /  6강~ : 2,3,4,4  →  7강을 만들 때 A급은 4개")]
    public CostBand[] CostBands =
    {
        new() { FromStage = 1, CostByGrade = new[] { 1, 2, 2, 3 } },
        new() { FromStage = 6, CostByGrade = new[] { 2, 3, 4, 4 } },
    };

    [Header("한계 해제 (대장장이)")]
    [Tooltip("무기 한계를 무시할 때의 최대 단계. PlayerEnchant.MaxEnchants를 넘을 수 없음")]
    [Min(1)] public int UnlockedMaxStage = 100;

    [Tooltip("대장장이: 무기의 마지막 특수 단계 이후 이 간격마다 특수 단계. 0 = 추가 없음")]
    [Min(0)] public int UnlockedSpecialInterval = 10;

    [Header("대장장이 — 다음 인챈트 지정 (스톤 비용 + 소울)")]
    [Tooltip("일반 인챈트 지정 소울")]
    [Min(0)] public int PickNormalSoul = 10;

    [Tooltip("특수 인챈트 지정 소울 — [0] I, [1] II, [2] III … 넘는 등급은 마지막 값")]
    public int[] PickSpecialSoulByTier = { 15, 30, 50 };


    [Header("특수 인챈트")]
    [Tooltip("새로 얻는 특수 인챈트의 등급 확률 — [0] I, [1] II, [2] III … 모든 무기 공통.\n" +
             "그 인챈트에 없는 등급은 빼고 다시 나눔")]
    public float[] SpecialTierWeights = { 60f, 30f, 10f };

    /// <param name="targetStage">이번에 만들 단계 (1강이면 1)</param>
    public int GetCost(ItemGrade grade, int targetStage)
    {
        CostBand best = null;

        if (CostBands != null)
            foreach (var b in CostBands)
                if (b != null && b.FromStage <= targetStage && (best == null || b.FromStage > best.FromStage))
                    best = b;

        return best != null ? best.Get(grade) : 1;
    }

    /// <summary>1 ~ maxTier 사이 등급 추첨 (공통 확률표)</summary>
    public int RollTier(int maxTier)
    {
        if (maxTier <= 1) return 1;

        float total = 0f;
        for (int t = 1; t <= maxTier; t++) total += TierWeight(t);
        if (total <= 0f) return 1;

        float r = Random.value * total;
        for (int t = 1; t <= maxTier; t++)
        {
            r -= TierWeight(t);
            if (r <= 0f) return t;
        }
        return maxTier;
    }

    float TierWeight(int tier) =>
        SpecialTierWeights != null && tier - 1 < SpecialTierWeights.Length
            ? Mathf.Max(0f, SpecialTierWeights[tier - 1])
            : 0f;

    public int GetPickSpecialSoul(int tier)
    {
        if (PickSpecialSoulByTier == null || PickSpecialSoulByTier.Length == 0) return 0;
        return Mathf.Max(0, PickSpecialSoulByTier[Mathf.Clamp(tier - 1, 0, PickSpecialSoulByTier.Length - 1)]);
    }
}