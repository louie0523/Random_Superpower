#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Entry = SheetPushCore.Entry;

/// <summary>
/// 아이템 → 구글 시트 (Item 탭) — 장비 · 방어구 인챈트 · 일반 인챈트 · 특수 인챈트의 한국어 이름 · 설명을 키 맞춰 올림.
///   Tools → 다국어 → 아이템 → 시트 올리기
///   인스펙터 ⋮ (오른쪽 위 점 세 개) → '이 아이템 → 시트'
///   Project 창에서 여러 개 골라 → 오른쪽 클릭 → 다국어 → 선택한 아이템 → 시트
/// 키
///   장비          {weapon|armor|helmet|boots}.{Id}.name · .desc · .trait.{i}
///   방어구 인챈트  armorench.{Id}.name · .desc · .trait.{i}  (Key Prefix를 적어 두면 그 앞부분)
///   일반 인챈트    enchant.{Id}.name
///   특수 인챈트    special.{Id}.name · .desc · .tier{n}
/// - 고유 특성(Traits)의 '직접 쓴 설명(Text)'은 비어 있는 TextKey에 키를 만들어 저장 (올리기 성공 후)
/// - 키가 이미 다른 탭(UI · Stat · Ability · Item · Status)에 있으면 그 줄을 고침 — 같은 키가 두 탭에 생기지 않게
/// </summary>
public class ItemSheetPush : EditorWindow
{
    const string Pref = "ItemSheetPush.";

    // ══════════════════ 모으기 ══════════════════

    public static List<Entry> Collect(IEnumerable<UnityEngine.Object> assets)
    {
        var list = new List<Entry>();
        foreach (var o in assets)
        {
            switch (o)
            {
                case EquipmentData d when d.Id != 0:
                    Add(list, d, d.NameKey, d.DisplayName);
                    Add(list, d, d.DescKey, d.Description);
                    Traits(list, d, $"{d.LocPrefix}.{d.Id}");
                    break;

                case ArmorEnchantData a when a.Id != 0:
                    string p = string.IsNullOrEmpty(a.KeyPrefix) ? $"armorench.{a.Id}" : a.KeyPrefix;
                    Add(list, a, p + ".name", a.DisplayName);
                    Add(list, a, p + ".desc", a.Description);
                    Traits(list, a, p);
                    break;

                case EnchantData e when e.Id != 0:
                    Add(list, e, $"enchant.{e.Id}.name", e.DisplayName);
                    break;

                case SpecialEnchantData s when s.Id != 0:
                    Add(list, s, $"special.{s.Id}.name", s.DisplayName);
                    Add(list, s, $"special.{s.Id}.desc", s.Description);
                    if (s.Tiers != null)
                        for (int i = 0; i < s.Tiers.Length; i++)
                            if (s.Tiers[i] != null) Add(list, s, $"special.{s.Id}.tier{i + 1}", s.Tiers[i].Description);
                    break;
            }
        }
        return list;
    }

    static void Add(List<Entry> list, UnityEngine.Object src, string key, string ko)
    {
        if (!SheetPushCore.IsText(ko)) return;
        list.Add(new Entry { Key = key, Korean = SheetPushCore.Clean(ko), Source = src });
    }

    /// <summary>고유 특성의 직접 쓴 설명 — TextKey가 비면 키를 만들어 저장</summary>
    static void Traits(List<Entry> list, UnityEngine.Object asset, string prefix)
    {
        var so = new SerializedObject(asset);
        var arr = so.FindProperty("Traits");
        if (arr == null || !arr.isArray) return;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var t = arr.GetArrayElementAtIndex(i);
            var text = t.FindPropertyRelative("Text");
            var key = t.FindPropertyRelative("TextKey");
            if (text == null || key == null || !SheetPushCore.IsText(text.stringValue)) continue;

            bool has = !string.IsNullOrWhiteSpace(key.stringValue);
            list.Add(new Entry
            {
                Key = has ? key.stringValue.Trim() : $"{prefix}.trait.{i}",
                Korean = SheetPushCore.Clean(text.stringValue),
                Source = asset,
                KeyAsset = has ? null : asset,
                KeyPath = has ? null : key.propertyPath,
            });
        }
    }

    static IEnumerable<T> All<T>() where T : UnityEngine.Object =>
        AssetDatabase.FindAssets("t:" + typeof(T).Name)
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null);

    static int IdOf(UnityEngine.Object o) => o switch
    {
        EquipmentData d => d.Id,
        ArmorEnchantData a => a.Id,
        EnchantData e => e.Id,
        SpecialEnchantData s => s.Id,
        _ => 0,
    };

    static bool IsItem(UnityEngine.Object o) => o is EquipmentData or ArmorEnchantData or EnchantData or SpecialEnchantData;

    // ══════════════════ 열기 ══════════════════

    bool _equip = true, _armorEnch = true, _enchant = true, _special = true;
    bool _overwrite = true, _formulas = true;
    string _tab = "Item", _category = "Item", _readTabs = "UI, Stat, Ability, Item, Status";
    List<UnityEngine.Object> _only = new();
    SheetPushCore.Plan _plan;
    Vector2 _scroll;
    string _status;

    [MenuItem("Tools/다국어/아이템 → 시트 올리기")]
    public static void Open() => Open(null);

    public static void Open(IEnumerable<UnityEngine.Object> only)
    {
        var w = GetWindow<ItemSheetPush>("아이템 → 시트");
        w.minSize = new Vector2(520f, 440f);
        w._only = only != null ? only.Where(IsItem).Distinct().ToList() : new List<UnityEngine.Object>();
        w._plan = null;
        w._status = null;
    }

    [MenuItem("CONTEXT/EquipmentData/이 아이템 → 시트")]
    static void FromEquip(MenuCommand c) => Open(new[] { c.context });
    [MenuItem("CONTEXT/ArmorEnchantData/이 아이템 → 시트")]
    static void FromArmorEnch(MenuCommand c) => Open(new[] { c.context });
    [MenuItem("CONTEXT/EnchantData/이 아이템 → 시트")]
    static void FromEnchant(MenuCommand c) => Open(new[] { c.context });
    [MenuItem("CONTEXT/SpecialEnchantData/이 아이템 → 시트")]
    static void FromSpecial(MenuCommand c) => Open(new[] { c.context });

    [MenuItem("Assets/다국어/선택한 아이템 → 시트")]
    static void FromSelection() => Open(Selection.objects);
    [MenuItem("Assets/다국어/선택한 아이템 → 시트", true)]
    static bool FromSelectionValid() => Selection.objects.Any(IsItem);

    void OnEnable()
    {
        _equip = EditorPrefs.GetBool(Pref + "equip", true);
        _armorEnch = EditorPrefs.GetBool(Pref + "armorEnch", true);
        _enchant = EditorPrefs.GetBool(Pref + "enchant", true);
        _special = EditorPrefs.GetBool(Pref + "special", true);
        _overwrite = EditorPrefs.GetBool(Pref + "overwrite", true);
        _formulas = EditorPrefs.GetBool(Pref + "formulas", true);
        _tab = EditorPrefs.GetString(Pref + "tab", "Item");
        _category = EditorPrefs.GetString(Pref + "category", "Item");
        _readTabs = EditorPrefs.GetString(Pref + "readTabs", "UI, Stat, Ability, Item, Status");
    }

    void SavePrefs()
    {
        EditorPrefs.SetBool(Pref + "equip", _equip);
        EditorPrefs.SetBool(Pref + "armorEnch", _armorEnch);
        EditorPrefs.SetBool(Pref + "enchant", _enchant);
        EditorPrefs.SetBool(Pref + "special", _special);
        EditorPrefs.SetBool(Pref + "overwrite", _overwrite);
        EditorPrefs.SetBool(Pref + "formulas", _formulas);
        EditorPrefs.SetString(Pref + "tab", _tab);
        EditorPrefs.SetString(Pref + "category", _category);
        EditorPrefs.SetString(Pref + "readTabs", _readTabs);
    }

    // ══════════════════ 창 ══════════════════

    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        if (_only.Count > 0)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"고른 것만 {_only.Count}개", EditorStyles.boldLabel);
                if (GUILayout.Button("전부로", GUILayout.Width(70))) { _only.Clear(); _plan = null; }
            }
            foreach (var o in _only.Take(8)) EditorGUILayout.ObjectField(o, typeof(UnityEngine.Object), false);
            if (_only.Count > 8) GUILayout.Label("…", EditorStyles.miniLabel);
        }
        else
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("종류", GUILayout.Width(146));
                _equip = GUILayout.Toggle(_equip, "장비", "Button");
                _armorEnch = GUILayout.Toggle(_armorEnch, "방어구 인챈트", "Button");
                _enchant = GUILayout.Toggle(_enchant, "일반 인챈트", "Button");
                _special = GUILayout.Toggle(_special, "특수 인챈트", "Button");
            }

        _tab = EditorGUILayout.TextField(new GUIContent("새 키를 넣을 탭", "LangTable 탭은 식으로 합쳐 보여 주는 탭이라 X"), _tab);
        _category = EditorGUILayout.TextField(new GUIContent("A열 분류", "새 줄의 A열 값"), _category);
        _readTabs = EditorGUILayout.TextField(new GUIContent("같이 읽을 탭", "이 탭들에 키가 이미 있으면 그 줄을 고침 (쉼표로)"), _readTabs);
        _overwrite = EditorGUILayout.ToggleLeft("이미 있는 키도 한국어(C열)를 에디터 값으로 바꾸기", _overwrite);
        _formulas = EditorGUILayout.ToggleLeft("새 줄의 다른 언어 칸에 구글 자동 번역 식 넣기", _formulas);

        if (EditorGUI.EndChangeCheck()) { SavePrefs(); _plan = null; }

        GUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("1. 미리보기 (시트 읽기)", GUILayout.Height(28))) Run(false);
            using (new EditorGUI.DisabledScope(_plan == null || (_plan.Added.Count == 0 && _plan.Changed.Count == 0 && _plan.PendingKeys == 0)))
                if (GUILayout.Button("2. 시트에 올리기", GUILayout.Height(28))) Run(true);
            if (GUILayout.Button(new GUIContent("시트 → Pull", "시트 내용을 LangTable로 가져오기"), GUILayout.Height(28), GUILayout.Width(100)))
                SheetsAuthTools.PullAll();
        }

        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);

        if (_plan == null) return;
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawList($"새로 추가 {_plan.Added.Count}  → '{_plan.Target}' {_plan.TargetNextRow}행부터", _plan.Added.Select(e => $"{e.Key}   {Short(e.Korean)}"));
        DrawList($"한국어 바뀜 {_plan.Changed.Count}" + (_overwrite ? "" : "  (끔 — 안 바꿈)"),
                 _plan.Changed.Select(c => $"{c.e.Key}  [{c.at.Tab} {c.at.Row}행]   {Short(c.at.Korean)}  →  {Short(c.e.Korean)}"));
        GUILayout.Label($"같음 {_plan.Same}개", EditorStyles.miniLabel);
        if (_plan.PendingKeys > 0)
            GUILayout.Label($"에셋에 새로 적을 번역 키 {_plan.PendingKeys}개 (고유 특성 설명 — 올리면 저장)", EditorStyles.miniLabel);
        if (_plan.Duplicates.Count > 0)
            DrawList($"시트에 같은 키가 여러 줄 {_plan.Duplicates.Count} (앞 줄만 고침 — 하나는 지우는 걸 추천)", _plan.Duplicates);
        EditorGUILayout.EndScrollView();
    }

    static void DrawList(string title, IEnumerable<string> lines)
    {
        GUILayout.Label(title, EditorStyles.boldLabel);
        int n = 0;
        foreach (var l in lines)
        {
            if (++n > 300) { GUILayout.Label("…", EditorStyles.miniLabel); break; }
            GUILayout.Label(l, EditorStyles.miniLabel);
        }
    }

    static string Short(string s)
    {
        s = (s ?? "").Replace("\n", " ⏎ ");
        return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
    }

    // ══════════════════ 실행 ══════════════════

    IEnumerable<UnityEngine.Object> Targets()
    {
        if (_only.Count > 0) return _only;
        var list = new List<UnityEngine.Object>();
        if (_equip) list.AddRange(All<EquipmentData>().OrderBy(d => d.Id));
        if (_armorEnch) list.AddRange(All<ArmorEnchantData>().OrderBy(d => d.Id));
        if (_enchant) list.AddRange(All<EnchantData>().OrderBy(d => d.Id));
        if (_special) list.AddRange(All<SpecialEnchantData>().OrderBy(d => d.Id));
        return list;
    }

    void Run(bool write)
    {
        try
        {
            var ctx = SheetPushCore.Connect(out var err);
            if (ctx == null) { _status = err; return; }

            var targets = Targets().ToList();
            var dupIds = targets.GroupBy(o => (o.GetType(), IdOf(o))).Where(g => g.Count() > 1)
                                .Select(g => $"{g.Key.Item1.Name} {g.Key.Item2}: " + string.Join(", ", g.Select(o => o.name))).ToList();
            var entries = Collect(targets);

            EditorUtility.DisplayProgressBar("아이템 → 시트", "시트 읽는 중…", 0.3f);
            _plan = SheetPushCore.MakePlan(ctx, entries, _readTabs.Split(','), _tab.Trim());
            _status = $"{targets.Count}개 에셋 · 키 {entries.Count}개 — 새로 {_plan.Added.Count} · 바뀜 {_plan.Changed.Count} · 같음 {_plan.Same}" +
                      (dupIds.Count > 0 ? "\n같은 번호를 쓰는 에셋: " + string.Join(" / ", dupIds) : "");

            if (!write) return;

            if (!EditorUtility.DisplayDialog("시트에 올리기",
                    $"'{_plan.Target}' 탭에 새 줄 {_plan.Added.Count}개" + (_overwrite ? $"\n한국어 바꿈 {_plan.Changed.Count}개" : "") +
                    (_plan.PendingKeys > 0 ? $"\n에셋에 번역 키 {_plan.PendingKeys}개 저장" : "") + "\n\n올릴까요?", "올리기", "취소"))
                return;

            EditorUtility.DisplayProgressBar("아이템 → 시트", "쓰는 중…", 0.6f);
            SheetPushCore.Write(ctx, _plan, _category, _overwrite, _formulas);
            int keys = SheetPushCore.ApplyKeys(_plan.Entries);
            _status = $"올림 — 새로 {_plan.Added.Count} · 바뀜 {(_overwrite ? _plan.Changed.Count : 0)} · 에셋 키 {keys}.  Unity로 가져오려면 '시트 → Pull'";
            Debug.Log("[다국어] " + _status);
            _plan = null;
        }
        catch (Exception e)
        {
            _status = "실패: " + SheetPushCore.Root(e).Message;
            Debug.LogError("[다국어] 아이템 → 시트 실패: " + SheetPushCore.Root(e));
        }
        finally { EditorUtility.ClearProgressBar(); }
    }
}
#endif
