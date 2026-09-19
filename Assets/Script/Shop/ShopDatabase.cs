using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Game/Shop Database")]
public class ShopDatabase : ScriptableObject
{
    [SerializeField] private string searchFolder = "Assets/Data/Shop";

    [Tooltip("순서가 네트워크 인덱스입니다. 중간 삽입·삭제 금지")]
    public ShopItemData[] Items;

    public int Count => Items?.Length ?? 0;

    public ShopItemData GetByIndex(int i) =>
        (Items != null && i >= 0 && i < Items.Length) ? Items[i] : null;

#if UNITY_EDITOR

    [ContextMenu("① 폴더에서 수집 (기존 순서 유지)")]
    public void Collect()
    {
        if (!AssetDatabase.IsValidFolder(searchFolder))
        {
            Debug.LogError($"[ShopDB] 폴더 없음: {searchFolder}");
            return;
        }

        var found = AssetDatabase.FindAssets($"t:{nameof(ShopItemData)}", new[] { searchFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<ShopItemData>)
            .Where(a => a != null)
            .OrderBy(a => a.Id)
            .ToList();

        // 기존 순서를 그대로 유지하고, 사라진 것만 빈 칸으로 남김
        var merged = new List<ShopItemData>();
        int kept = 0, removed = 0;

        if (Items != null)
        {
            foreach (var old in Items)
            {
                if (old == null) { merged.Add(null); continue; }

                if (found.Contains(old)) { merged.Add(old); found.Remove(old); kept++; }
                else
                {
                    merged.Add(null);      // 삭제된 자리는 비워서 인덱스 보존
                    removed++;
                    Debug.LogWarning($"[ShopDB] {old.name}이(가) 폴더에서 사라져 빈 칸으로 남깁니다. " +
                                     "인덱스 보존을 위해 자리는 유지됩니다.", this);
                }
            }
        }

        // 새로 발견된 것은 뒤에 추가
        int added = found.Count;
        merged.AddRange(found);

        Items = merged.ToArray();

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ShopDB] 수집 완료 — 유지 {kept} / 추가 {added} / 빈칸 {removed}  (전체 {Items.Length})");

        Validate();
    }

    [ContextMenu("② 검사")]
    public void Validate()
    {
        if (Items == null || Items.Length == 0)
        {
            Debug.LogWarning("[ShopDB] 목록이 비어 있음");
            return;
        }

        var seen = new Dictionary<int, ShopItemData>();
        int errors = 0;

        for (int i = 0; i < Items.Length; i++)
        {
            var a = Items[i];

            if (a == null)
            {
                Debug.LogWarning($"[ShopDB] [{i}] 빈 칸 (삭제된 아이템 자리)");
                continue;
            }

            if (a.Id == 0)
            {
                Debug.LogError($"[ShopDB] [{i}] {a.name}: ID가 0", a);
                errors++;
            }
            else if (seen.TryGetValue(a.Id, out var dup))
            {
                Debug.LogError($"[ShopDB] ID 중복 {a.Id}: {a.name} ↔ {dup.name}", a);
                errors++;
            }
            else seen[a.Id] = a;

            if (string.IsNullOrWhiteSpace(a.DisplayName))
            {
                Debug.LogError($"[ShopDB] [{i}] {a.name}: 표시 이름 없음", a);
                errors++;
            }

            if (a.BasePrice <= 0)
            {
                Debug.LogWarning($"[ShopDB] [{i}] {a.name}: 가격이 0 이하", a);
                errors++;
            }

            if (a.Kind == ShopItemKind.Stat &&
                (a.ValuePerLevel == null || a.ValuePerLevel.Length == 0))
            {
                Debug.LogError($"[ShopDB] [{i}] {a.name}: Stat인데 ValuePerLevel이 비어 있음", a);
                errors++;
            }

            if (!a.IsRepeatable && a.ValuePerLevel != null &&
                a.ValuePerLevel.Length < a.MaxLevel)
            {
                Debug.LogWarning($"[ShopDB] [{i}] {a.name}: MaxLevel {a.MaxLevel}인데 " +
                                 $"ValuePerLevel이 {a.ValuePerLevel.Length}개뿐 " +
                                 "(마지막 값이 반복됩니다)", a);
            }
        }

        if (Items.Length > PlayerShop.MaxItems)
        {
            Debug.LogError($"[ShopDB] 아이템이 {Items.Length}개로 " +
                           $"PlayerShop.MaxItems({PlayerShop.MaxItems})를 초과합니다. " +
                           "Capacity를 늘리세요.", this);
            errors++;
        }

        if (errors == 0) Debug.Log($"[ShopDB] 검사 통과 — {Items.Length}개");
        else Debug.LogError($"[ShopDB] 문제 {errors}건");
    }

    [ContextMenu("③ 다음 빈 ID")]
    public void FindNextId()
    {
        var used = new HashSet<int>(
            Items?.Where(a => a != null).Select(a => a.Id) ?? Enumerable.Empty<int>());

        int n = 5001;
        while (used.Contains(n)) n++;

        Debug.Log($"[ShopDB] 다음 ID: {n}");
    }

#endif
}