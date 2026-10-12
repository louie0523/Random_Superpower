using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ToonVfx.EditorTools
{
    /// <summary>새 이펙트 마법사 — 이름 → 어디서 시작할지 (빈 이펙트 / 기본 프리셋 / 내 프리셋) → 색</summary>
    public class VfxNewEffectWindow : EditorWindow
    {
        VfxStudioWindow _owner;
        string _name = "새 이펙트";
        bool _nameTouched;
        int _tab;
        VfxPreset _preset;
        string _userPreset;
        VfxPalette _palette;            // null = 프리셋 기본 색
        VfxPreviewAnchor _anchor = VfxPreviewAnchor.Chest;
        VfxPlayMode _mode = VfxPlayMode.Once;
        Vector2 _scroll;

        string[] _tabs;
        readonly List<string> _userPresets = new();

        public static void ShowFor(VfxStudioWindow owner)
        {
            var w = CreateInstance<VfxNewEffectWindow>();
            w._owner = owner;
            w.titleContent = new GUIContent("새 이펙트");
            var size = new Vector2(500f, 580f);
            var main = EditorGUIUtility.GetMainWindowPosition();
            w.position = new Rect(main.center - size * 0.5f, size);
            w.minSize = size;
            w.ShowUtility();
        }

        void OnEnable()
        {
            var list = new List<string> { "빈 이펙트" };
            list.AddRange(VfxPresets.Categories);
            list.Add("내 프리셋");
            _tabs = list.ToArray();

            _userPresets.Clear();
            if (AssetDatabase.IsValidFolder(VfxStudioWindow.PresetsDir))
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { VfxStudioWindow.PresetsDir }))
                    _userPresets.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        bool IsEmptyTab => _tab == 0;
        bool IsUserTab => _tab == _tabs.Length - 1;
        string Category => !IsEmptyTab && !IsUserTab ? _tabs[_tab] : null;

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            GUILayout.Label("새 이펙트 만들기", VfxStudioGUI.Header);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _name = EditorGUILayout.TextField("이름", _name);
            if (EditorGUI.EndChangeCheck()) _nameTouched = true;

            EditorGUILayout.Space(8);
            GUILayout.Label("어디서 시작할까요?", EditorStyles.boldLabel);
            int tab = GUILayout.SelectionGrid(_tab, _tabs, 4, GUILayout.Height(44));
            if (tab != _tab)
            {
                _tab = tab;
                _preset = null;
                _userPreset = null;
                if (Category != null) foreach (var p in VfxPresets.InCategory(Category)) { PickPreset(p); break; }
            }

            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.Height(230));
            if (IsEmptyTab) DrawEmpty();
            else if (IsUserTab) DrawUserPresets();
            else
                foreach (var p in VfxPresets.InCategory(Category))
                    if (Item(p.Name, p.Description, _preset == p)) PickPreset(p);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            GUILayout.Label(IsEmptyTab ? "색" : "색 (비우면 프리셋 기본 색)", EditorStyles.boldLabel);
            DrawPalettes();

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("취소", GUILayout.Width(90), GUILayout.Height(28))) Close();
                bool ready = IsEmptyTab || (IsUserTab ? !string.IsNullOrEmpty(_userPreset) : _preset != null);
                using (new EditorGUI.DisabledScope(!ready))
                    if (GUILayout.Button("만들기", GUILayout.Width(120), GUILayout.Height(28))) Create();
            }
            EditorGUILayout.Space(6);
        }

        void PickPreset(VfxPreset p)
        {
            _preset = p;
            if (!_nameTouched) _name = p.Name;
        }

        void DrawEmpty()
        {
            GUILayout.Label("레이어 없이 시작해요. 만들고 나면 '호' 도구가 켜져 있으니 씬 뷰에서 바로 드래그해 그려 보세요.", VfxStudioGUI.Hint);
            EditorGUILayout.Space(6);
            _anchor = (VfxPreviewAnchor)EditorGUILayout.EnumPopup(new GUIContent("놓는 곳", "스튜디오에서 더미의 어디에 놓을지"), _anchor);
            _mode = (VfxPlayMode)EditorGUILayout.EnumPopup(new GUIContent("재생 방식", "1회: 슬래시·폭발 / 시작 → 반복 → 끝: 버프"), _mode);
            EditorGUILayout.Space(6);
            GUILayout.Label("· 슬래시·타격 → 몸 (가슴 높이), 1회\n· 버프·오라 → 발밑, 시작 → 반복 → 끝\n· 폭발·착탄 → 앞 (타격 지점), 1회", VfxStudioGUI.Hint);
        }

        void DrawUserPresets()
        {
            if (_userPresets.Count == 0)
            {
                GUILayout.Label("아직 내 프리셋이 없어요. VFX Studio 위쪽의 '프리셋으로 저장'을 누르면 여기 생겨요.", VfxStudioGUI.Hint);
                return;
            }
            foreach (var path in _userPresets)
            {
                string label = Path.GetFileNameWithoutExtension(path);
                if (Item(label, path, _userPreset == path))
                {
                    _userPreset = path;
                    if (!_nameTouched) _name = label;
                }
            }
        }

        static GUIStyle _itemTitle;
        static GUIStyle ItemTitle => _itemTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

        /// <summary>선택 가능한 목록 한 줄 (제목 + 설명). 눌렀으면 true</summary>
        static bool Item(string title, string desc, bool selected)
        {
            var r = EditorGUILayout.GetControlRect(false, 38);
            if (selected) EditorGUI.DrawRect(r, VfxStudioGUI.SelectedRow);
            GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 12, 18), title, ItemTitle);
            GUI.Label(new Rect(r.x + 6, r.y + 19, r.width - 12, 16), desc, EditorStyles.miniLabel);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void DrawPalettes()
        {
            var list = VfxStudioGUI.Palettes;
            const float w = 76f, h = 36f;
            float avail = position.width - 20f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(avail / w));
            int count = list.Count + (IsEmptyTab ? 0 : 1);
            int rows = Mathf.CeilToInt(count / (float)perRow);
            var grid = GUILayoutUtility.GetRect(avail, Mathf.Max(1, rows) * h);

            int i = 0;
            if (!IsEmptyTab)
            {
                var r0 = new Rect(grid.x, grid.y, w - 4f, h - 2f);
                if (VfxStudioGUI.PaletteChip(r0, null, _palette == null)) _palette = null;
                i = 1;
            }
            foreach (var p in list)
            {
                var r = new Rect(grid.x + (i % perRow) * w, grid.y + (i / perRow) * h, w - 4f, h - 2f);
                if (VfxStudioGUI.PaletteChip(r, p, _palette == p)) _palette = p;
                i++;
            }
        }

        void Create()
        {
            if (_owner == null) _owner = GetWindow<VfxStudioWindow>("VFX Studio");
            var owner = _owner;
            string name = _name;
            var preset = IsEmptyTab || IsUserTab ? null : _preset;
            string user = IsUserTab ? _userPreset : null;
            var palette = _palette;
            var anchor = _anchor;
            var mode = _mode;

            Close();
            // 씬 전환·저장 확인 대화상자가 뜰 수 있어서 GUI 밖(다음 틱)에서
            EditorApplication.delayCall += () =>
            {
                if (owner == null) return;
                owner.CreateNew(name, preset, user, palette, anchor, mode);
                owner.Focus();
            };
        }
    }

    /// <summary>색 바리에이션 — 팔레트를 골라 프리팹 변형(Variant)을 한 번에</summary>
    public class VfxVariationsPopup : PopupWindowContent
    {
        readonly VfxStudioWindow _w;
        readonly List<VfxPalette> _pals;
        readonly bool[] _on;

        public VfxVariationsPopup(VfxStudioWindow w)
        {
            _w = w;
            _pals = new List<VfxPalette>(VfxStudioGUI.Palettes);
            _on = new bool[_pals.Count];
        }

        public override Vector2 GetWindowSize() => new(270f, 86f + _pals.Count * 20f + 34f);

        public override void OnGUI(Rect rect)
        {
            GUILayout.Label("색 바리에이션", EditorStyles.boldLabel);
            GUILayout.Label("팔레트만 바꾼 프리팹 변형(Variant)을 같은 폴더에 만들어요. 원본을 고치면 같이 바뀌어요.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            for (int i = 0; i < _pals.Count; i++)
            {
                var p = _pals[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    _on[i] = EditorGUILayout.ToggleLeft(VfxStudioGUI.PaletteLabel(p), _on[i], GUILayout.Width(150));
                    var sw = GUILayoutUtility.GetRect(72f, 14f, GUILayout.Width(72f));
                    float w = sw.width / 3f;
                    EditorGUI.DrawRect(new Rect(sw.x, sw.y + 2, w, sw.height - 2), p.Core);
                    EditorGUI.DrawRect(new Rect(sw.x + w, sw.y + 2, w, sw.height - 2), p.Mid);
                    EditorGUI.DrawRect(new Rect(sw.x + w * 2, sw.y + 2, w, sw.height - 2), p.Edge);
                }
            }

            GUILayout.FlexibleSpace();
            var picked = new List<VfxPalette>();
            for (int i = 0; i < _pals.Count; i++) if (_on[i]) picked.Add(_pals[i]);

            using (new EditorGUI.DisabledScope(picked.Count == 0))
                if (GUILayout.Button(picked.Count > 0 ? $"{picked.Count}개 만들기" : "팔레트를 고르세요", GUILayout.Height(24)))
                {
                    editorWindow.Close();
                    _w.MakeVariants(picked);
                }
        }
    }
}
