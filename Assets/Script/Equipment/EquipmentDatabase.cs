using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Game/Equipment Database")]
public class EquipmentDatabase : ScriptableObject
{
    [Header("Auto Collect")]
    [Tooltip("이 폴더 아래를 모두 훑습니다. 예: Assets/Data/Equipment")]
    [SerializeField] private string searchFolder = "Assets/Data/Equipment";

    public EquipmentData[] All;

    private Dictionary<int, EquipmentData> _lookup;

    public EquipmentData Get(int id)
    {
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out var d) ? d : null;
    }

    void BuildLookup()
    {
        _lookup = new Dictionary<int, EquipmentData>();
        if (All == null) return;

        foreach (var e in All)
        {
            if (e == null) continue;
            if (!_lookup.TryAdd(e.Id, e))
                Debug.LogError($"[EquipDB] ID 중복 {e.Id}: {e.name}");
        }
    }

#if UNITY_EDITOR

    [ContextMenu("① 폴더에서 모두 수집")]
    public void CollectFromFolder()
    {
        if (!AssetDatabase.IsValidFolder(searchFolder))
        {
            Debug.LogError($"[EquipDB] 폴더 없음: {searchFolder}");
            return;
        }

        string[] guids = AssetDatabase.FindAssets(
            $"t:{nameof(EquipmentData)}", new[] { searchFolder });

        All = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<EquipmentData>)
            .Where(e => e != null)
            .OrderBy(e => e.Id)
            .ToArray();

        _lookup = null;

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        Debug.Log($"[EquipDB] {All.Length}개 수집 완료");
        ValidateIds();
    }

    [ContextMenu("② ID 검사")]
    public void ValidateIds()
    {
        if (All == null || All.Length == 0)
        {
            Debug.LogWarning("[EquipDB] 목록이 비어 있음");
            return;
        }

        var seen = new Dictionary<int, EquipmentData>();
        int errors = 0;

        foreach (var e in All)
        {
            if (e == null)
            {
                Debug.LogError("[EquipDB] 빈 슬롯 있음");
                errors++;
                continue;
            }

            if (e.Id == 0)
            {
                Debug.LogError($"[EquipDB] {e.name}: ID가 0 (0은 '장비 없음' 예약)", e);
                errors++;
            }

            if (seen.TryGetValue(e.Id, out var dup))
            {
                Debug.LogError($"[EquipDB] ID 중복 {e.Id}: {e.name} ↔ {dup.name}", e);
                errors++;
            }
            else seen[e.Id] = e;

            int band = e.Id / 1000;
            int expected = e.Slot switch
            {
                EquipSlot.Weapon => 1,
                EquipSlot.Armor => 2,
                EquipSlot.Helmet => 3,
                EquipSlot.Boots => 4,
                _ => -1
            };

            if (band != expected)
            {
                Debug.LogWarning(
                    $"[EquipDB] {e.name}: ID {e.Id}가 슬롯({e.Slot})과 불일치. " +
                    $"{expected}000번대여야 함", e);
                errors++;
            }
        }

        if (errors == 0)
            Debug.Log($"[EquipDB] 검사 통과 — {All.Length}개 이상 없음");
        else
            Debug.LogError($"[EquipDB] 문제 {errors}건");
    }

    [ContextMenu("③ 다음 빈 ID 찾기")]
    public void FindNextIds()
    {
        var used = new HashSet<int>(All?.Where(e => e != null).Select(e => e.Id)
                                    ?? Enumerable.Empty<int>());

        string result = "[EquipDB] 다음 사용 가능 ID\n";
        foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
        {
            int band = slot switch
            {
                EquipSlot.Weapon => 1000,
                EquipSlot.Armor => 2000,
                EquipSlot.Helmet => 3000,
                EquipSlot.Boots => 4000,
                _ => 0
            };
            int next = band + 1;
            while (used.Contains(next)) next++;
            result += $"  {slot}: {next}\n";
        }
        Debug.Log(result);
    }

#endif
}