#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tools → UI → UI 프리팹 만들기 창
/// 프리팹마다 유지 / 글꼴만 / 교체를 골라서 Assets/UI/Game 에 만들고, Resources/UiPrefabSet에 연결.
///   유지   = 손댄 프리팹 그대로
///   글꼴만 = 꾸민 건 그대로 두고 글자(TMP)의 글꼴만 바꿈
///   교체   = 코드 기본 모양으로 새로 (꾸민 내용 사라짐 — '백업' 켜면 Backup 폴더에 복사해 둠)
/// 없는 프리팹은 항상 새로 만듦.
/// 한 줄 프리팹(예: PlayerPickRow)을 교체해도, 유지한 화면이 새 줄을 쓰게 다시 연결함.
/// </summary>
public class GameUiPrefabMaker : EditorWindow
{
    const string Folder = "Assets/UI/Game";
    const string SetFolder = Folder + "/Resources";
    const string SetPath = SetFolder + "/UiPrefabSet.asset";
    const string BackupFolder = Folder + "/Backup";
    const string Pref = "GameUiMaker.";

    enum Mode { Keep, FontOnly, Replace }
    static readonly string[] ModeNames = { "유지", "글꼴만", "교체" };

    class Entry
    {
        public string Name, Label;
        public bool Row;
        public Mode Mode;
        public string Path => $"{Folder}/{Name}.prefab";
        public bool Exists => File.Exists(Path);
        /// <summary>없으면 무조건 새로</summary>
        public Mode Effective => Exists ? Mode : Mode.Replace;
    }

    List<Entry> _entries;
    TMP_FontAsset _font;
    bool _backup = true;
    Vector2 _scroll;

    [MenuItem("Tools/UI/UI 프리팹 만들기 창")]
    static void Open()
    {
        var w = GetWindow<GameUiPrefabMaker>("UI 프리팹 만들기");
        w.minSize = new Vector2(460f, 560f);
    }

    void OnEnable()
    {
        _entries = new List<Entry>
        {
            new() { Name = "PlayerPickView", Label = "대상 선택 창 (기본)" },
            new() { Name = "PlayerPickView_DeathNote", Label = "대상 선택 — 데스 노트" },
            new() { Name = "PlayerPickView_Voodoo", Label = "대상 선택 — 저주 인형" },
            new() { Name = "VoiceSettingsView", Label = "보이스 설정 창" },
            new() { Name = "RoomScreenView", Label = "방 목록 · 대기실" },
            new() { Name = "SettingsView", Label = "설정 창" },
            new() { Name = "EscMenuView", Label = "ESC 메뉴" },

            new() { Name = "PlayerPickRow", Label = "대상 선택 한 줄", Row = true },
            new() { Name = "VoiceMicOption", Label = "마이크 한 줄", Row = true },
            new() { Name = "RoomRow", Label = "방 목록 한 줄", Row = true },
            new() { Name = "RoomPlayerRow", Label = "대기실 플레이어 한 줄", Row = true },
            new() { Name = "BanToggle", Label = "금지 직업 칸", Row = true },
            new() { Name = "PresetRow", Label = "프리셋 한 줄", Row = true },
        };
        foreach (var e in _entries) e.Mode = (Mode)EditorPrefs.GetInt(Pref + e.Name, (int)Mode.Keep);

        _backup = EditorPrefs.GetBool(Pref + "backup", true);

        string fontGuid = EditorPrefs.GetString(Pref + "font", "");
        if (!string.IsNullOrEmpty(fontGuid))
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(fontGuid));
        if (_font == null)
        {
            var set = AssetDatabase.LoadAssetAtPath<UiPrefabSet>(SetPath);
            if (set != null) _font = set.Font;
        }
    }

    // ══════════════════ 창 ══════════════════

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "유지 = 손댄 프리팹 그대로\n글꼴만 = 꾸민 건 두고 글꼴만 바꿈\n교체 = 코드 기본 모양으로 새로 (꾸민 내용 사라짐)\n없는 프리팹은 항상 새로 만듦",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        _font = (TMP_FontAsset)EditorGUILayout.ObjectField("글꼴 (한글 TMP)", _font, typeof(TMP_FontAsset), false);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetString(Pref + "font", _font != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_font)) : "");
        if (_font == null)
            EditorGUILayout.HelpBox("글꼴이 비어 있으면 TMP 기본 글꼴로 만듦 (한글 깨짐) · '글꼴만'은 건너뜀", MessageType.Warning);

        EditorGUI.BeginChangeCheck();
        _backup = EditorGUILayout.ToggleLeft("교체 · 글꼴만 전에 백업 (" + BackupFolder + "/날짜시간/)", _backup);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(Pref + "backup", _backup);

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("한 번에:", GUILayout.Width(60f));
            for (int m = 0; m < ModeNames.Length; m++)
                if (GUILayout.Button("전부 " + ModeNames[m])) SetAll((Mode)m);
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        Section("화면", false);
        EditorGUILayout.Space(6f);
        Section("목록 한 줄 (화면 안에서 반복되는 칸)", true);
        EditorGUILayout.HelpBox("한 줄을 교체해도 '유지'한 화면은 새 줄을 쓰게 저절로 다시 연결됨", MessageType.None);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6f);
        int rep = _entries.Count(e => e.Effective == Mode.Replace);
        int fnt = _entries.Count(e => e.Effective == Mode.FontOnly);
        GUILayout.Label($"새로 · 교체 {rep}개 · 글꼴만 {fnt}개 · 유지 {_entries.Count - rep - fnt}개", EditorStyles.miniLabel);
        if (GUILayout.Button("만들기", GUILayout.Height(34f))) Run();
        if (GUILayout.Button("UI 고르기 에셋(UiPrefabSet) 선택")) SelectSet();
    }

    void Section(string title, bool rows)
    {
        GUILayout.Label(title, EditorStyles.boldLabel);
        foreach (var e in _entries)
        {
            if (e.Row != rows) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(e.Label, GUILayout.Width(170f));
                if (e.Exists)
                {
                    EditorGUI.BeginChangeCheck();
                    var m = (Mode)GUILayout.Toolbar((int)e.Mode, ModeNames);
                    if (EditorGUI.EndChangeCheck())
                    {
                        e.Mode = m;
                        EditorPrefs.SetInt(Pref + e.Name, (int)m);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                        GUILayout.Label("없음 → 새로 만듦", EditorStyles.miniLabel);
                }
            }
        }
    }

    void SetAll(Mode m)
    {
        foreach (var e in _entries)
        {
            e.Mode = m;
            EditorPrefs.SetInt(Pref + e.Name, (int)m);
        }
    }

    // ══════════════════ 만들기 ══════════════════

    Entry Get(string name) => _entries.First(e => e.Name == name);

    void Run()
    {
        var replacing = _entries.Where(e => e.Exists && e.Mode == Mode.Replace).ToList();
        if (replacing.Count > 0 &&
            !EditorUtility.DisplayDialog("교체",
                "아래 프리팹을 코드 기본 모양으로 다시 만듦 (꾸민 내용 사라짐):\n\n• " +
                string.Join("\n• ", replacing.Select(e => e.Label)) +
                (_backup ? "\n\n교체 전에 Backup 폴더에 복사해 둠" : "\n\n백업 꺼짐!"),
                "교체", "취소"))
            return;

        if (_font == null &&
            !EditorUtility.DisplayDialog("글꼴", "글꼴이 비어 있어서 TMP 기본 글꼴로 만듦 (한글 깨짐). 계속할까요?", "계속", "취소"))
            return;

        EnsureFolder(Folder);
        EnsureFolder(SetFolder);

        // 백업
        if (_backup)
        {
            string dir = $"{BackupFolder}/{DateTime.Now:yyyyMMdd_HHmmss}";
            var toBackup = _entries.Where(e => e.Exists && e.Mode != Mode.Keep).ToList();
            if (toBackup.Count > 0)
            {
                EnsureFolder(dir);
                foreach (var e in toBackup) AssetDatabase.CopyAsset(e.Path, $"{dir}/{e.Name}.prefab");
            }
        }

        // 교체할 한 줄을 가리키는 '남겨 둘 화면'의 연결 기억 → 줄을 새로 만든 뒤 다시 연결
        var rebuiltRows = _entries.Where(e => e.Row && e.Exists && e.Mode == Mode.Replace).ToList();
        var links = rebuiltRows.Count > 0 ? CollectLinks(rebuiltRows) : new List<Link>();

        int R = AbilityUi.RoundedRadius;
        AbilityUi.RoundedOverride = SaveSprite("Rounded", AbilityUi.RoundedTexture(), new Vector4(R, R, R, R));
        AbilityUi.CircleOverride = SaveSprite("Circle", AbilityUi.CircleTexture(), Vector4.zero);

        var holder = new GameObject("__GameUiMaker", typeof(RectTransform));
        var font = _font;
        try
        {
            // ── 한 줄 ──
            var pickRow = MakeRow(Get("PlayerPickRow"), () => GameUiBuilders.PickRow(holder.transform, font));
            var micOpt = MakeRow(Get("VoiceMicOption"), () => GameUiBuilders.MicOption(holder.transform, font));
            var roomRow = MakeRow(Get("RoomRow"), () => GameUiBuilders.RoomRow(holder.transform, font));
            var playerRow = MakeRow(Get("RoomPlayerRow"), () => GameUiBuilders.PlayerRow(holder.transform, font));
            var banOpt = MakeRow(Get("BanToggle"), () => GameUiBuilders.BanToggle(holder.transform, font));
            var presetRow = MakeRow(Get("PresetRow"), () => GameUiBuilders.PresetRow(holder.transform, font));

            // ── 화면 ──
            MakeView(Get("PlayerPickView"), go =>
            {
                var v = GameUiBuilders.PlayerPick(go, font, pickRow);
                for (int i = 0; i < 3; i++) Preview(pickRow, v.ListContent);
            });
            MakeView(Get("PlayerPickView_DeathNote"), go =>
            {
                var v = GameUiBuilders.DeathNotePick(go, font, pickRow);
                for (int i = 0; i < 3; i++) Preview(pickRow, v.ListContent);
            });
            MakeView(Get("PlayerPickView_Voodoo"), go =>
            {
                var v = GameUiBuilders.VoodooPick(go, font, pickRow);
                for (int i = 0; i < 3; i++) Preview(pickRow, v.ListContent);
            });
            MakeView(Get("VoiceSettingsView"), go =>
            {
                var v = GameUiBuilders.Voice(go, font, micOpt);
                for (int i = 0; i < 2; i++) Preview(micOpt, v.MicListContent);
            });
            MakeView(Get("RoomScreenView"), go =>
            {
                var v = GameUiBuilders.Room(go, font, roomRow, playerRow, banOpt, presetRow);
                for (int i = 0; i < 3; i++) Preview(roomRow, v.RoomListContent);
                for (int i = 0; i < 3; i++) Preview(playerRow, v.PlayerListContent);
                for (int i = 0; i < 4; i++) Preview(banOpt, v.ClassBanContent);
                Preview(presetRow, v.PresetListContent);
            });
            MakeView(Get("SettingsView"), go => SettingsUiBuilders.Settings(go, font));
            MakeView(Get("EscMenuView"), go => SettingsUiBuilders.EscMenu(go, font));
        }
        finally
        {
            Object.DestroyImmediate(holder);
            AbilityUi.RoundedOverride = AbilityUi.CircleOverride = null;
        }

        Relink(links);

        // ── 고르기 에셋 (Resources/UiPrefabSet) — 'Use' 칸은 그대로 둠 ──
        var set = AssetDatabase.LoadAssetAtPath<UiPrefabSet>(SetPath);
        if (set == null)
        {
            set = CreateInstance<UiPrefabSet>();
            AssetDatabase.CreateAsset(set, SetPath);
        }
        set.PlayerPick = Load<PlayerPickView>("PlayerPickView");
        set.VoiceSettings = Load<VoiceSettingsView>("VoiceSettingsView");
        set.Room = Load<RoomScreenView>("RoomScreenView");
        set.Settings = Load<SettingsView>("SettingsView");
        set.EscMenu = Load<EscMenuView>("EscMenuView");
        if (font != null) set.Font = font;
        EditorUtility.SetDirty(set);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = set;
        EditorGUIUtility.PingObject(set);

        string Names(Mode m) => string.Join(", ", _entries.Where(e => e.Effective == m).Select(e => e.Name));
        Debug.Log($"[GameUI] 끝 — 새로·교체: {Names(Mode.Replace)} / 글꼴만: {Names(Mode.FontOnly)} / 유지: {Names(Mode.Keep)}" +
                  (links.Count > 0 ? $" / 다시 연결 {links.Count}곳" : ""));
    }

    /// <summary>한 줄 프리팹 — 교체면 새로 저장, 아니면 있는 걸 씀 (글꼴만이면 글꼴 바꾸고)</summary>
    T MakeRow<T>(Entry e, Func<T> build) where T : Component
    {
        if (e.Effective == Mode.Replace)
        {
            var built = build();
            built.transform.SetParent(null, false);
            var asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, e.Path).GetComponent<T>();
            Object.DestroyImmediate(built.gameObject);
            return asset;
        }

        if (e.Effective == Mode.FontOnly) SwapFont(e.Path, _font);

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(e.Path);
        var c = go != null ? go.GetComponent<T>() : null;
        if (c == null) Debug.LogWarning($"[GameUI] {e.Path}에 {typeof(T).Name}이 없음 — '교체'로 다시 만들어 줘");
        return c;
    }

    /// <summary>화면 프리팹 — 교체면 새로, 글꼴만이면 글꼴만, 유지면 그대로</summary>
    void MakeView(Entry e, Action<GameObject> build)
    {
        switch (e.Effective)
        {
            case Mode.Replace:
                var go = new GameObject(e.Name);
                try
                {
                    build(go);
                    PrefabUtility.SaveAsPrefabAsset(go, e.Path);
                }
                finally { Object.DestroyImmediate(go); }
                break;
            case Mode.FontOnly:
                SwapFont(e.Path, _font);
                break;
        }
    }

    static T Load<T>(string name) where T : Component
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{name}.prefab");
        return go != null ? go.GetComponent<T>() : null;
    }

    /// <summary>프리팹 안 모든 TMP 글자의 글꼴 바꾸기 (모양 보기용 줄 — 다른 프리팹 복사본 — 은 건너뜀)</summary>
    static void SwapFont(string path, TMP_FontAsset font)
    {
        if (font == null) { Debug.LogWarning($"[GameUI] 글꼴이 비어서 '글꼴만' 건너뜀: {path}"); return; }

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(t)) continue;
                if (t.font == font) continue;
                t.font = font;      // 글꼴 재질도 그 글꼴 기본으로 바뀜
                n++;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[GameUI] 글꼴만 바꿈: {path} ({n}개)");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ══════════════════ 줄 교체 후 다시 연결 ══════════════════

    struct Link
    {
        public string View;         // 화면 프리팹 경로
        public string ObjPath;      // 루트부터 형제 번호 (예: "0/3/1", 루트는 "")
        public string CompType;
        public int CompIndex;
        public string Prop;
        public string RowPath;
        public string RefType;      // null = GameObject 참조
    }

    List<Link> CollectLinks(List<Entry> rebuiltRows)
    {
        var rowPaths = new HashSet<string>(rebuiltRows.Select(r => r.Path));
        var links = new List<Link>();

        foreach (var e in _entries)
        {
            if (e.Row || !e.Exists || e.Mode == Mode.Replace) continue;      // 다시 만드는 화면은 새 줄을 바로 씀

            var root = PrefabUtility.LoadPrefabContents(e.Path);
            try
            {
                foreach (var comp in root.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null || PrefabUtility.IsPartOfPrefabInstance(comp)) continue;

                    var so = new SerializedObject(comp);
                    var it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var o = it.objectReferenceValue;
                        if (o == null) continue;
                        string p = AssetDatabase.GetAssetPath(o);
                        if (!rowPaths.Contains(p)) continue;

                        var same = comp.GetComponents(comp.GetType());
                        links.Add(new Link
                        {
                            View = e.Path,
                            ObjPath = PathOf(comp.transform, root.transform),
                            CompType = comp.GetType().AssemblyQualifiedName,
                            CompIndex = Array.IndexOf(same, comp),
                            Prop = it.propertyPath,
                            RowPath = p,
                            RefType = o is GameObject ? null : o.GetType().AssemblyQualifiedName,
                        });
                    }
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return links;
    }

    static void Relink(List<Link> links)
    {
        foreach (var group in links.GroupBy(l => l.View))
        {
            var root = PrefabUtility.LoadPrefabContents(group.Key);
            try
            {
                foreach (var l in group)
                {
                    var tr = FindByPath(root.transform, l.ObjPath);
                    var type = Type.GetType(l.CompType);
                    if (tr == null || type == null) continue;
                    var comps = tr.GetComponents(type);
                    if (l.CompIndex < 0 || l.CompIndex >= comps.Length) continue;

                    var rowGo = AssetDatabase.LoadAssetAtPath<GameObject>(l.RowPath);
                    if (rowGo == null) continue;
                    Object target = l.RefType == null ? rowGo : rowGo.GetComponent(Type.GetType(l.RefType));
                    if (target == null) continue;

                    var so = new SerializedObject(comps[l.CompIndex]);
                    var prop = so.FindProperty(l.Prop);
                    if (prop == null) continue;
                    prop.objectReferenceValue = target;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, group.Key);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (; t != null && t != root; t = t.parent) parts.Add(t.GetSiblingIndex().ToString());
        parts.Reverse();
        return string.Join("/", parts);
    }

    static Transform FindByPath(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        var t = root;
        foreach (var s in path.Split('/'))
        {
            int i = int.Parse(s);
            if (i >= t.childCount) return null;
            t = t.GetChild(i);
        }
        return t;
    }

    // ══════════════════ 도우미 ══════════════════

    [MenuItem("Tools/UI/UI 고르기 에셋 선택 (UiPrefabSet)")]
    static void SelectSet()
    {
        var set = Resources.Load<UiPrefabSet>("UiPrefabSet");
        if (set == null)
        {
            Debug.Log("[GameUI] UiPrefabSet이 아직 없어 — 먼저 'UI 프리팹 만들기 창'에서 만들기");
            return;
        }
        Selection.activeObject = set;
        EditorGUIUtility.PingObject(set);
    }

    /// <summary>모양 보기용 줄 (게임 시작하면 지워지고 코드가 다시 만듦)</summary>
    static void Preview(Component prefab, Transform parent)
    {
        if (prefab == null || parent == null) return;
        PrefabUtility.InstantiatePrefab(prefab.gameObject, parent);
    }

    static Sprite SaveSprite(string name, Texture2D tex, Vector4 border)
    {
        string path = $"{Folder}/{name}.png";
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.spriteBorder = border;
            ti.SaveAndReimport();
        }
        Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}

/// <summary>Tools → 멀티 테스트 → 관전자 보이스 프리팹 만들기</summary>
public static class SpectatorVoicePrefabMaker
{
    const string Path0 = "Assets/Prefabs/Network/SpectatorVoice.prefab";

    [MenuItem("Tools/멀티 테스트/관전자 보이스 프리팹 만들기")]
    static void Make()
    {
        if (File.Exists(Path0) &&
            !EditorUtility.DisplayDialog("덮어쓰기", Path0 + "이 이미 있습니다. 다시 만들까요?", "다시 만들기", "취소"))
            return;

        string dir = Path.GetDirectoryName(Path0).Replace('\\', '/');
        EnsureFolder(dir);

        var root = new GameObject("SpectatorVoice");
        try
        {
            root.AddComponent<Fusion.NetworkObject>();
            root.AddComponent<Photon.Voice.Fusion.VoiceNetworkObject>();
            root.AddComponent<SpectatorVoice>();

            var voice = new GameObject("Voice");
            voice.transform.SetParent(root.transform, false);
            voice.AddComponent<Photon.Voice.Unity.Recorder>();
            voice.AddComponent<Photon.Voice.Unity.Speaker>();
            var src = voice.GetComponent<AudioSource>();
            if (src == null) src = voice.AddComponent<AudioSource>();
            src.playOnAwake = false;

            var asset = PrefabUtility.SaveAsPrefabAsset(root, Path0);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[SpectatorVoice] {Path0} 만듦 — ① 자식 Voice의 Recorder를 플레이어 프리팹 Voice와 같게 (Transmit Enabled 끄기 · Record When Joined 켜기 · Voice Detection 끄기 · Unity 마이크) " +
                  "② Fusion 프리팹 목록 다시 빌드 ③ 게임 씬 BasicSpawner의 Spectator Voice Prefab 칸에 넣기");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
