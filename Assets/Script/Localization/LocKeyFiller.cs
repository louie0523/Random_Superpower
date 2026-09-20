using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;

public static class LocKeyFiller
{
    const string TableName = "Game";

    [MenuItem("Tools/Localization/에셋에 키 채우기")]
    public static void Fill()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
        if (collection == null)
        {
            Debug.LogError($"[Loc] '{TableName}' 테이블 없음");
            return;
        }

        var shared = collection.SharedData;
        int added = 0;

        // 무기
        foreach (var guid in AssetDatabase.FindAssets("t:EquipmentData"))
        {
            var a = AssetDatabase.LoadAssetAtPath<EquipmentData>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (a == null) continue;

            string prefix = a.Slot switch
            {
                EquipSlot.Weapon => "weapon",
                EquipSlot.Armor => "armor",
                EquipSlot.Helmet => "helmet",
                EquipSlot.Boots => "boots",
                _ => "equip",
            };

            EnsureKey(collection, shared, $"{prefix}.{a.Id}.name", a.DisplayName, ref added);
            a.NameEntry = MakeRef(shared, $"{prefix}.{a.Id}.name");

            if (!string.IsNullOrWhiteSpace(a.Description))
            {
                EnsureKey(collection, shared, $"{prefix}.{a.Id}.desc", a.Description, ref added);
                a.DescEntry = MakeRef(shared, $"{prefix}.{a.Id}.desc");
            }

            EditorUtility.SetDirty(a);
        }

        // 능력, 인챈트, 상점도 같은 식으로...

        EditorUtility.SetDirty(shared);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Loc] 키 {added}개 추가");
    }

    static void EnsureKey(StringTableCollection col, SharedTableData shared,
                          string key, string korean, ref int added)
    {
        var entry = shared.GetEntry(key);
        if (entry != null) return;

        entry = shared.AddKey(key);
        added++;

        // 한국어 채우기
        var koTable = col.GetTable(new UnityEngine.Localization.LocaleIdentifier
            ("ko")) as StringTable;

        if (koTable != null && !string.IsNullOrEmpty(korean))
            koTable.AddEntry(entry.Id, korean);
    }

    static LocalizedString MakeRef(SharedTableData shared, string key)
    {
        var entry = shared.GetEntry(key);
        return new LocalizedString
        {
            TableReference = shared.TableCollectionName,
            TableEntryReference = entry.Id,
        };
    }
}