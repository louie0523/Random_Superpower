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

    /// <summary>
    /// 이 방어구에 붙일 특수 인챈트 하나 — 방어구의 후보 배열(ArmorEnchants)이 있으면 그 안에서,
    /// 없으면 이 데이터베이스에서 칸에 맞는 것 중. 없으면 0
    /// </summary>
    public int Roll(EquipmentData d)
    {
        if (d == null) return 0;
        var c = d.ArmorEnchants;
        if (c == null || c.Length == 0) return Roll(d.Slot);

        float sum = 0f;
        foreach (var e in c) if (e != null && e.Enchant != null) sum += e.GetWeight();
        if (sum <= 0f) return 0;
        float x = Random.value * sum;
        ArmorEnchantData pick = null;
        foreach (var e in c)
        {
            if (e == null || e.Enchant == null) continue;
            float w = e.GetWeight();
            if (w <= 0f) continue;
            pick = e.Enchant;
            x -= w;
            if (x <= 0f) break;
        }
        if (pick == null) return 0;
        if (Get(pick.Id) == null)
            Debug.LogWarning($"[방어구 인챈트] '{pick.name}'({pick.Id})가 {name}의 All에 없음 — {d.name}의 후보에서 뽑혔지만 끼면 안 보임. All에 넣어 줘", pick);
        return pick.Id;
    }

    /// <summary>데이터베이스가 없어도 — 후보 배열 · Default 순</summary>
    public static int RollFor(EquipmentData d, ArmorEnchantDatabase db = null)
    {
        db = db != null ? db : Default;
        return db != null ? db.Roll(d) : 0;
    }

#if UNITY_EDITOR
    void OnValidate() => _map = null;
#endif
}
