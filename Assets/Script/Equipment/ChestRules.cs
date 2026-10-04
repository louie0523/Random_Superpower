using UnityEngine;

public enum ChestTier { Common, Rare, Epic }

/// <summary>
/// 상자 공통 규칙 — 시간에 따른 상자 등급 확률, 상자 등급별 내용물.
/// 특정 상자만 다르게 하려면 LootChest의 '이 상자만 조정' 칸을 쓰거나 다른 규칙 에셋을 연결.
/// </summary>
[CreateAssetMenu(menuName = "Game/Chest Rules")]
public class ChestRules : ScriptableObject
{
    [System.Serializable]
    public class TierPhase
    {
        [Tooltip("라운드 시작 후 이 시간(분)부터 적용")]
        [Min(0)] public float FromMinute;

        [Tooltip("재충전될 때 이 등급이 될 가중치")]
        [Min(0)] public float Common = 80f;
        [Min(0)] public float Rare = 18f;
        [Min(0)] public float Epic = 2f;

        public float Get(ChestTier t) => t switch
        {
            ChestTier.Rare => Rare,
            ChestTier.Epic => Epic,
            _ => Common,
        };
    }

    [System.Serializable]
    public class TierConfig
    {
        [Header("무기 등급 가중치")]
        [Min(0)] public float C = 70f;
        [Min(0)] public float B = 25f;
        [Min(0)] public float A = 4f;
        [Min(0)] public float S = 1f;

        [Header("내용물")]
        [Tooltip("떨어지는 무기 수")]
        [Min(0)] public int DropCount = 1;

        [Tooltip("연 사람이 받는 인챈트 스톤 (최소~최대)")]
        [Min(0)] public int StoneMin = 3;
        [Min(0)] public int StoneMax = 6;

        [Tooltip("여는 데 걸리는 시간(초). 0이면 상자 기본값")]
        [Min(0)] public float OpenTime = 0f;

        public float Grade(ItemGrade g) => g switch
        {
            ItemGrade.B => B,
            ItemGrade.A => A,
            ItemGrade.S => S,
            _ => C,
        };
    }

    [System.Serializable]
    public class WeaponBoost
    {
        public EquipmentData Weapon;

        [Tooltip("같은 등급 안에서의 배정 배율. 2 = 두 배 잘 나옴, 0 = 안 나옴")]
        [Min(0)] public float Multiplier = 2f;
    }

    [Header("상자 등급 — 시간에 따라")]
    [Tooltip("재충전될 때마다 그 시점의 가중치로 다음 등급을 뽑음. FromMinute 순서대로")]
    public TierPhase[] Schedule =
    {
        new() { FromMinute = 0f, Common = 80f, Rare = 18f, Epic = 2f },
        new() { FromMinute = 3f, Common = 50f, Rare = 40f, Epic = 10f },
        new() { FromMinute = 6f, Common = 20f, Rare = 50f, Epic = 30f },
    };

    [Header("상자 등급별 내용물")]
    public TierConfig CommonChest = new() { C = 70, B = 25, A = 4, S = 1, DropCount = 1, StoneMin = 3, StoneMax = 6 };
    public TierConfig RareChest = new() { C = 30, B = 45, A = 20, S = 5, DropCount = 1, StoneMin = 5, StoneMax = 10, OpenTime = 4 };
    public TierConfig EpicChest = new() { C = 5, B = 30, A = 45, S = 20, DropCount = 2, StoneMin = 8, StoneMax = 15, OpenTime = 5 };

    [Header("무기 배정 — 모든 상자 공통")]
    [Tooltip("특정 무기가 모든 상자에서 더(덜) 나오게")]
    public WeaponBoost[] WeaponBoosts;

    /// <summary>지금 시간(분)에 적용되는 등급 가중치</summary>
    public TierPhase PhaseAt(float minutes)
    {
        TierPhase best = null;
        if (Schedule != null)
            foreach (var p in Schedule)
                if (p != null && p.FromMinute <= minutes && (best == null || p.FromMinute > best.FromMinute))
                    best = p;
        return best;
    }

    public TierConfig Config(ChestTier t) => t switch
    {
        ChestTier.Rare => RareChest,
        ChestTier.Epic => EpicChest,
        _ => CommonChest,
    };

    public float GlobalBoost(EquipmentData w)
    {
        float m = 1f;
        if (WeaponBoosts != null)
            foreach (var b in WeaponBoosts)
                if (b != null && b.Weapon == w) m *= b.Multiplier;
        return m;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        Fix(CommonChest);
        Fix(RareChest);
        Fix(EpicChest);
    }

    static void Fix(TierConfig c)
    {
        if (c != null && c.StoneMax < c.StoneMin) c.StoneMax = c.StoneMin;
    }
#endif
}
