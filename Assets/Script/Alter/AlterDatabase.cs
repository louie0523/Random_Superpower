using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Game/Alter Database")]
public class AlterDatabase : ScriptableObject
{
    [SerializeField] private string searchFolder = "Assets/Data/Alters";
    public AlterData[] All;

    private Dictionary<int, AlterData> _lookup;

    public AlterData Get(int id)
    {
        if (id == 0) return null;
        if (_lookup == null)
        {
            _lookup = new Dictionary<int, AlterData>();
            if (All != null)
                foreach (var a in All)
                    if (a != null && !_lookup.TryAdd(a.Id, a))
                        Debug.LogError($"[AlterDB] ID 중복 {a.Id}: {a.name}", a);
        }
        return _lookup.TryGetValue(id, out var d) ? d : null;
    }

#if UNITY_EDITOR
    void OnValidate() => _lookup = null;

    [ContextMenu("폴더에서 수집")]
    public void Collect()
    {
        if (!AssetDatabase.IsValidFolder(searchFolder))
        {
            Debug.LogError($"[AlterDB] 폴더 없음: {searchFolder}");
            return;
        }

        All = AssetDatabase.FindAssets($"t:{nameof(AlterData)}", new[] { searchFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AlterData>)
            .Where(a => a != null)
            .OrderBy(a => a.Id)
            .ToArray();

        _lookup = null;
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AlterDB] {All.Length}개 수집");

        foreach (var a in All)
            if (a.Id == 0) Debug.LogError($"[AlterDB] {a.name}: ID가 0", a);
    }
#endif
}
