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

    public EnchantData[] All;

    private Dictionary<int, EnchantData> _lookup;

    public EnchantData Get(int id)
    {
        if (id == 0) return null;
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out var d) ? d : null;
    }

    void BuildLookup()
    {
        _lookup = new Dictionary<int, EnchantData>();
        if (All == null) return;

        foreach (var e in All)
        {
            if (e == null) continue;
            if (!_lookup.TryAdd(e.Id, e))
                Debug.LogError($"[EnchantDB] ID 중복 {e.Id}: {e.name}", e);
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

        _lookup = null;
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        Debug.Log($"[EnchantDB] {All.Length}개 수집");

        foreach (var e in All)
            if (e.Id == 0) Debug.LogError($"[EnchantDB] {e.name}: ID가 0", e);
    }
#endif
}