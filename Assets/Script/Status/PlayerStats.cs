using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PlayerStats : NetworkBehaviour
{
    [Header("Base Stats")]
    [SerializeField] private float baseMaxHP = 100f;
    [SerializeField] private float baseAttack = 25f;
    [SerializeField] private float baseDefense = 0f;
    [SerializeField] private float baseMoveSpeed = 6f;
    [SerializeField] private float baseAttackSpeed = 1f;
    [SerializeField] private float baseCritChance = 0.05f;   // 5%
    [SerializeField] private float baseCritDamage = 1.5f;    // 150%
    [SerializeField] private float baseCooldownReduction = 0f;
    [SerializeField] private float baseLifeSteal = 0f;

    private readonly List<StatModifier> _mods = new();
    private readonly Dictionary<StatType, float> _cache = new();
    private bool _dirty = true;

    public event System.Action OnStatsChanged;

    /// <summary>
    /// 비율 스탯 — Percent 수정자를 쓰지 않고 Flat만 사용.
    /// 예: "치명타 확률 10% 증가" = Flat 0.1
    /// </summary>
    static bool IsRatioStat(StatType s) =>
        s == StatType.CritChance ||
        s == StatType.CritDamage ||
        s == StatType.CooldownReduction ||
        s == StatType.LifeSteal;

    // ---------- 조회 ----------

    public float Get(StatType stat)
    {
        if (_dirty) Recalculate();
        return _cache.TryGetValue(stat, out var v) ? v : 0f;
    }

    float GetBase(StatType stat) => stat switch
    {
        StatType.MaxHP => baseMaxHP,
        StatType.AttackPower => baseAttack,
        StatType.Defense => baseDefense,
        StatType.MoveSpeed => baseMoveSpeed,
        StatType.AttackSpeed => baseAttackSpeed,
        StatType.CritChance => baseCritChance,
        StatType.CritDamage => baseCritDamage,
        StatType.CooldownReduction => baseCooldownReduction,
        StatType.LifeSteal => baseLifeSteal,
        _ => 0f,
    };

    // ---------- 수정자 관리 ----------

    public void AddModifier(StatModifier mod)
    {
        _mods.Add(mod);
        _dirty = true;
    }

    public void AddModifiers(IEnumerable<StatModifier> mods)
    {
        _mods.AddRange(mods);
        _dirty = true;
    }

    /// <summary>특정 소스가 준 수정자 전부 제거</summary>
    public void RemoveBySource(int sourceId)
    {
        int removed = _mods.RemoveAll(m => m.SourceId == sourceId);
        if (removed > 0) _dirty = true;
    }

    public void ClearAll()
    {
        _mods.Clear();
        _dirty = true;
    }

    // ---------- 계산 ----------

    void Recalculate()
    {
        _cache.Clear();

        foreach (StatType stat in System.Enum.GetValues(typeof(StatType)))
        {
            float flat = 0f;
            float percent = 0f;   // 0.25 = +25%

            foreach (var m in _mods)
            {
                if (m.Stat != stat) continue;

                if (m.Type == ModType.Flat)
                {
                    flat += m.Value;
                }
                else
                {
                    // 인스펙터/코드에는 25로 넣고, 여기서 0.25로 변환
                    percent += m.Value * 0.01f;
                }
            }

            float result = IsRatioStat(stat)
                ? GetBase(stat) + flat                        // 비율 스탯은 Flat만
                : (GetBase(stat) + flat) * (1f + percent);

            _cache[stat] = Mathf.Max(0f, result);
        }

        _dirty = false;
        OnStatsChanged?.Invoke();
    }

    // ---------- 디버그 ----------

    [ContextMenu("Print Stats")]
    public void PrintStats()
    {
        if (_dirty) Recalculate();
        var sb = new System.Text.StringBuilder("[STATS]\n");
        foreach (var kv in _cache)
            sb.AppendLine($"  {kv.Key}: {kv.Value:F2}");
        sb.AppendLine($"  (modifiers: {_mods.Count})");
        Debug.Log(sb.ToString());
    }
}