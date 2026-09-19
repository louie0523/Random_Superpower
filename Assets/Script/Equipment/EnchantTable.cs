using UnityEngine;

[System.Serializable]
public class EnchantStage
{
    public string Label = "1단계";

    [Tooltip("이 단계에서 나올 수 있는 인챈트들")]
    public EnchantData[] Pool;
}

[CreateAssetMenu(menuName = "Game/Enchant Table")]
public class EnchantTable : ScriptableObject
{
    public EnchantStage[] Stages;

    public int MaxStage => Stages?.Length ?? 0;

    public EnchantData Roll(int stage)
    {
        if (Stages == null || stage < 0 || stage >= Stages.Length) return null;

        var pool = Stages[stage].Pool;
        if (pool == null || pool.Length == 0) return null;

        float total = 0f;
        foreach (var e in pool) if (e != null) total += Mathf.Max(0.01f, e.RollWeight);
        if (total <= 0f) return null;

        float r = Random.value * total;
        foreach (var e in pool)
        {
            if (e == null) continue;
            r -= Mathf.Max(0.01f, e.RollWeight);
            if (r <= 0f) return e;
        }

        return pool[^1];
    }
}