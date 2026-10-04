using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Game/Enchant Database")]
public class EnchantDatabase : ScriptableObject
{
    [SerializeField] private string searchFolder = "Assets/Data/Enchants";

    [Tooltip("일반 인챈트")]
    public EnchantData[] All;

    [Tooltip("특수 인챈트 (둔화, 화상 …)")]
    public SpecialEnchantData[] Specials;

    private Dictionary<int, EnchantData> _lookup;
    private Dictionary<int, SpecialEnchantData> _specialLookup;

    /// <summary>일반 인챈트. 특수 인챈트 값(EnchantCode)이면 null</summary>
    public EnchantData Get(int id)
    {
        if (id == 0 || EnchantCode.IsSpecial(id)) return null;
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>특수 인챈트 — 특수 ID로 (EnchantCode.SpecialId)</summary>
    public SpecialEnchantData GetSpecial(int specialId)
    {
        if (specialId == 0) return null;
        if (_specialLookup == null) BuildLookup();
        return _specialLookup.TryGetValue(specialId, out var d) ? d : null;
    }

    void BuildLookup()
    {
        _lookup = new Dictionary<int, EnchantData>();
        _specialLookup = new Dictionary<int, SpecialEnchantData>();

        if (All != null)
            foreach (var e in All)
            {
                if (e == null) continue;
                if (!_lookup.TryAdd(e.Id, e))
                    Debug.LogError($"[EnchantDB] ID 중복 {e.Id}: {e.name}", e);
            }

        if (Specials != null)
            foreach (var s in Specials)
            {
                if (s == null) continue;
                if (!_specialLookup.TryAdd(s.Id, s))
                    Debug.LogError($"[EnchantDB] 특수 ID 중복 {s.Id}: {s.name}", s);
            }
    }

#if UNITY_EDITOR
    [ContextMenu("폴더에서 수집")]
    public void Collect()
    {
        if (!AssetDatabase.IsValidFolder(searchFolder))
        {
            Debug.LogError($"[EnchantDB] 폴더 없음: {searchFolder}");
            return;
        }

        All = AssetDatabase.FindAssets($"t:{nameof(EnchantData)}", new[] { searchFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<EnchantData>)
            .Where(e => e != null)
            .OrderBy(e => e.Id)
            .ToArray();

        Specials = AssetDatabase.FindAssets($"t:{nameof(SpecialEnchantData)}", new[] { searchFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<SpecialEnchantData>)
            .Where(e => e != null)
            .OrderBy(e => e.Id)
            .ToArray();

        _lookup = null;
        _specialLookup = null;
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        Debug.Log($"[EnchantDB] 일반 {All.Length}개 · 특수 {Specials.Length}개 수집");

        foreach (var e in All)
            if (e.Id == 0) Debug.LogError($"[EnchantDB] {e.name}: ID가 0", e);

        foreach (var s in Specials)
        {
            if (s.Id == 0) Debug.LogError($"[EnchantDB] {s.name}: 특수 ID가 0", s);
            if (s.MaxTier == 0) Debug.LogWarning($"[EnchantDB] {s.name}: 등급이 비어 있음", s);
        }
    }
#endif
}