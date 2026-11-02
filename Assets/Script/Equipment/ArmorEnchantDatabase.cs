using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방어구 특수 인챈트 목록. PlayerEquipment · 상자 · 암시장에 연결하거나,
/// Resources 폴더에 'ArmorEnchantDatabase' 이름으로 두면 연결 안 해도 찾아 씀.
/// </summary>
[CreateAssetMenu(menuName = "Game/Armor Enchant Database")]
public class ArmorEnchantDatabase : ScriptableObject
{
    public ArmorEnchantData[] All;

    Dictionary<int, ArmorEnchantData> _map;

    static ArmorEnchantDatabase _default;
    /// <summary>Resources/ArmorEnchantDatabase — 연결 안 한 곳에서 씀</summary>
    public static ArmorEnchantDatabase Default => _default != null ? _default : (_default = Resources.Load<ArmorEnchantDatabase>("ArmorEnchantDatabase"));

    public ArmorEnchantData Get(int id)
    {
        if (id == 0 || All == null) return null;
        if (_map == null || _map.Count != All.Length)
        {
            _map = new Dictionary<int, ArmorEnchantData>();
            foreach (var e in All) if (e != null) _map[e.Id] = e;
        }
        return _map.TryGetValue(id, out var d) ? d : null;
    }

    static readonly List<ArmorEnchantData> _pool = new();

    /// <summary>이 칸에 붙을 수 있는 것 중 가중치로 하나 — 없으면 0</summary>
    public int Roll(EquipSlot slot)
    {
        if (All == null) return 0;
        _pool.Clear();
        float sum = 0f;
        foreach (var e in All)
            if (e != null && e.Weight > 0f && e.Fits(slot)) { _pool.Add(e); sum += e.Weight; }
        if (_pool.Count == 0) return 0;

        float x = Random.value * sum;
        foreach (var e in _pool) { x -= e.Weight; if (x <= 0f) return e.Id; }
        return _pool[^1].Id;
    }

#if UNITY_EDITOR
    void OnValidate() => _map = null;
#endif
}
