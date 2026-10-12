using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ToonVfx.EditorTools
{
    /// <summary>
    /// VFX Studio — 이펙트를 씬에 직접 그리고, 블록 타임라인으로 타이밍을 맞추고, 프리팹으로 저장.
    ///   왼쪽: 이펙트 목록 · 가운데: 타임라인 · 오른쪽: 인스펙터 · 씬 뷰: 미리보기 + 그리기
    /// 편집 대상은 미리보기 씬(VfxStudio.unity)에 놓인 프리팹 인스턴스 — 저장 = 프리팹에 적용
    /// </summary>
    public partial class VfxStudioWindow : EditorWindow
    {
        public const string EffectsDir = VfxSetup.Root + "/Effects";
        public const string PresetsDir = VfxSetup.Root + "/Presets";

        enum DrawTool { Select, Arc, Ring, Pillar, Streak, Trail, Stamp, Burst, Light, Cone }

        // ── 편집 대상 ──
        [SerializeField] VfxEffect _fx;
        [SerializeField] bool _owned;               // 스튜디오가 씬에 꺼낸 것 — 다른 걸 열면 치움
        [SerializeField] Component _sel;            // VfxLayer 또는 VfxLight
        readonly List<Component> _rows = new();

        // ── 그리기 ──
        [SerializeField] DrawTool _tool = DrawTool.Select;
        [SerializeField] VfxLayerStyle _drawStyle = VfxLayerStyle.Glow;
        [SerializeField] VfxBuiltinMask _stampMask = VfxBuiltinMask.Star;
        [SerializeField] bool _drawOnGround;

        // ── 화면 ──
        [SerializeField] bool _advanced;
        [SerializeField] int _inspTab;              // 0 레이어, 1 이펙트
        [SerializeField] string _search = "";
        const float ToolbarH = 44f;
        const float LibWidth = 200f;
        const float InspWidth = 330f;
        Vector2 _libScroll, _inspScroll;

        // ── 재생 ──
        bool _playing;
        [SerializeField] bool _loopPlay = true;
        [SerializeField] float _playSpeed = 1f;
        [SerializeField] float _scrubTime = 0.15f;
        double _lastTick;

        // ── 저장 안 됨 표시 (매 프레임 계산하지 않게) ──
        bool _unsaved;
        double _unsavedCheckAt;

        // ══════════════════════ 열기 ══════════════════════

        [MenuItem("Tools/VFX Studio/VFX Studio 열기", priority = 1)]
        public static void Open()
        {
            var console = Type.GetType("UnityEditor.ConsoleWindow,UnityEditor");
            var w = console != null
                ? GetWindow<VfxStudioWindow>("VFX Studio", true, console)
                : GetWindow<VfxStudioWindow>("VFX Studio");
            w.minSize = new Vector2(920f, 300f);
            w.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("VFX Studio");
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnUpdate;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            _libDirty = true;
            CollectRows();
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnUpdate;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Tools.hidden = false;
            StopPlay();
        }

        void OnDestroy()
        {
            if (_fx != null && _owned && CheckUnsaved() &&
                EditorUtility.DisplayDialog("VFX Studio", $"'{_fx.name}'에 저장하지 않은 변경이 있어요. 저장할까요?", "저장", "저장 안 함"))
                Save();
        }

        /// <summary>
        /// 대화상자·씬 전환이 있는 작업은 GUI 그리는 도중에 하면 Unity 레이아웃 오류가 남 → 다음 틱에 실행
        /// </summary>
        void Later(Action a) => EditorApplication.delayCall += () => { if (this != null) a(); };

        void OnProjectChanged()
        {
            _libDirty = true;
            VfxStudioGUI.Invalidate();
            Repaint();
        }

        void OnHierarchyChanged()
        {
            CollectRows();
            if (_sel == null) _sel = null;      // 지워진 레이어 정리 (Unity null → 진짜 null)
            Repaint();
        }

        void OnUndoRedo()
        {
            CollectRows();
            if (_fx != null) _fx.Refresh();
            _unsavedCheckAt = 0;
            SceneView.RepaintAll();
            Repaint();
        }

        void OnSelectionChange()
        {
            var go = Selection.activeGameObject;
            if (go == null || _fx == null || !go.transform.IsChildOf(_fx.transform)) { Repaint(); return; }

            if (go == _fx.gameObject) { _sel = null; _inspTab = 1; }
            else if (go.TryGetComponent<VfxLayer>(out var l)) { _sel = l; _inspTab = 0; }
            else if (go.TryGetComponent<VfxLight>(out var li)) { _sel = li; _inspTab = 0; }
            Repaint();
        }

        // ══════════════════════ 그리기 (레이아웃) ══════════════════════

        void OnGUI()
        {
            HandleCommands();

            if (_rows.Count > 0 && (_rows[0] == null || _fx == null)) CollectRows();

            DrawToolbar(new Rect(0, 0, position.width, ToolbarH));

            var body = new Rect(0, ToolbarH, position.width, position.height - ToolbarH);
            var lib = new Rect(body.x, body.y, LibWidth, body.height);
            var insp = new Rect(body.xMax - InspWidth, body.y, InspWidth, body.height);
            var tl = new Rect(lib.xMax + 1, body.y, insp.x - lib.xMax - 2, body.height);

            DrawLibrary(lib);
            EditorGUI.DrawRect(new Rect(lib.xMax, body.y, 1, body.height), VfxStudioGUI.Separator);
            EditorGUI.DrawRect(new Rect(insp.x - 1, body.y, 1, body.height), VfxStudioGUI.Separator);

            if (_fx == null)
            {
                DrawEmptyState(tl);
                DrawEmptyInspector(insp);
                return;
            }

            DrawTimeline(tl);
            DrawInspector(insp);
        }

        void DrawEmptyState(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 20, r.width - 40, r.height - 40));
            GUILayout.Label("편집 중인 이펙트가 없어요", VfxStudioGUI.Header);
            EditorGUILayout.Space(6);
            GUILayout.Label("왼쪽 목록에서 이펙트를 열거나, 새로 만들어 보세요.\n" +
                            "새 이펙트는 프리셋에서 시작하거나 빈 이펙트에 씬에서 직접 그릴 수 있어요.", VfxStudioGUI.Hint);
            EditorGUILayout.Space(10);
            if (GUILayout.Button("+ 새 이펙트 만들기", VfxStudioGUI.BigButton, GUILayout.Width(220)))
                VfxNewEffectWindow.ShowFor(this);

            var sel = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<VfxEffect>() : null;
            if (sel != null)
            {
                EditorGUILayout.Space(6);
                if (GUILayout.Button($"씬에서 선택한 '{sel.name}' 편집", GUILayout.Width(220), GUILayout.Height(24)))
                {
                    var target = sel;
                    Later(() => EditInScene(target));
                }
            }
            GUILayout.EndArea();
        }

        void DrawEmptyInspector(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16));
            GUILayout.Label("사용법", VfxStudioGUI.Header);
            GUILayout.Label(
                "1. 새 이펙트 → 프리셋이나 빈 이펙트 선택\n" +
                "2. 위쪽 도구(호·원·기둥…)를 고르고 씬 뷰에서 드래그\n" +
                "3. 타임라인에서 블록 끝을 끌어 길이 조절\n" +
                "4. 오른쪽에서 등장·사라짐 방식, 색 고르기\n" +
                "5. 저장 → 프리팹 완성\n\n" +
                "Space 재생 · Delete 삭제 · Ctrl+D 복제 · Esc 도구 끄기", VfxStudioGUI.Hint);
            GUILayout.EndArea();
        }

        // ══════════════════════ 툴바 ══════════════════════

        void DrawToolbar(Rect r)
        {
            GUILayout.BeginArea(r);

            // ── 1줄: 파일 · 보기 ──
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("+ 새 이펙트", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    VfxNewEffectWindow.ShowFor(this);

                using (new EditorGUI.DisabledScope(_fx == null))
                {
                    bool unsaved = _fx != null && CheckUnsaved();
                    var saveLabel = _fx != null && !IsPrefabInstance ? "프리팹으로 저장" : unsaved ? "● 저장" : "저장";
                    if (GUILayout.Button(saveLabel, EditorStyles.toolbarButton, GUILayout.Width(100))) Save();
                    if (GUILayout.Button("다른 이름으로", EditorStyles.toolbarButton, GUILayout.Width(84))) Later(SaveAs);
                    if (GUILayout.Button("되돌리기", EditorStyles.toolbarButton, GUILayout.Width(60))) Later(Revert);

                    GUILayout.Space(8);
                    var varRect = GUILayoutUtility.GetRect(new GUIContent("색 바리에이션 ▾"), EditorStyles.toolbarButton, GUILayout.Width(100));
                    if (GUI.Button(varRect, "색 바리에이션 ▾", EditorStyles.toolbarButton))
                        PopupWindow.Show(varRect, new VfxVariationsPopup(this));
                    if (GUILayout.Button(new GUIContent("변형 주사위", "선택한 레이어(없으면 전체)의 각도·크기·배치를 살짝씩 흔들어 비슷하지만 다른 이펙트로"),
                            EditorStyles.toolbarButton, GUILayout.Width(76))) Randomize();
                    if (GUILayout.Button(new GUIContent("프리셋으로 저장", "새 이펙트 마법사의 '내 프리셋'에 추가"),
                            EditorStyles.toolbarButton, GUILayout.Width(96))) SaveAsPreset();
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label("보기", EditorStyles.miniLabel);
                if (GUILayout.Button("앞에서", EditorStyles.toolbarButton, GUILayout.Width(50))) ViewFront();
                if (GUILayout.Button(new GUIContent("1인칭", "더미 눈높이에서 정면 — 내 화면에서 어떻게 보이는지. 이 시점에서 그리면 1인칭 기준으로 그려짐"),
                        EditorStyles.toolbarButton, GUILayout.Width(46))) ViewFirstPerson();
                if (GUILayout.Button("위에서", EditorStyles.toolbarButton, GUILayout.Width(50))) ViewTop();
            }

            // ── 2줄: 그리기 도구 ──
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            using (new EditorGUI.DisabledScope(_fx == null))
            {
                GUILayout.Label("그리기", EditorStyles.miniLabel, GUILayout.Width(36));
                ToolButton(DrawTool.Select, "선택", "레이어를 고르고 씬의 손잡이로 크기 조절");
                ToolButton(DrawTool.Arc, "호", "칼을 휘두르듯 드래그 → 슬래시");
                ToolButton(DrawTool.Ring, "원", "가운데를 누르고 드래그 → 고리 (화면면 = 정면 충격파, 바닥면 = 바닥 고리)");
                ToolButton(DrawTool.Pillar, "기둥", "바닥을 누르고 위로 드래그 → 오라 기둥");
                ToolButton(DrawTool.Streak, "속도선", "달려가는 방향으로 드래그 → 속도선");
                ToolButton(DrawTool.Trail, "잔상", "움직이는 방향으로 드래그 → 잔상 줄");
                ToolButton(DrawTool.Cone, "원뿔", "몸에서 돌진 방향으로 드래그 → 앞을 감싸는 바람 원뿔");
                ToolButton(DrawTool.Stamp, "스탬프", "누르고 드래그해 크기 → 아래 '모양'으로 찍기");
                ToolButton(DrawTool.Burst, "뿌리기", "누르고 드래그해 퍼지는 범위 → 조각들");
                ToolButton(DrawTool.Light, "조명", "누른 자리에 순간 조명");

                GUILayout.Space(8);
                var maskRect = GUILayoutUtility.GetRect(new GUIContent("모양: " + VfxStudioGUI.MaskName(_stampMask) + " ▾"),
                    EditorStyles.toolbarDropDown, GUILayout.Width(118));
                if (GUI.Button(maskRect, new GUIContent("모양: " + VfxStudioGUI.MaskName(_stampMask), "스탬프·뿌리기에 쓸 모양"), EditorStyles.toolbarDropDown))
                    StampMaskMenu().DropDown(maskRect);

                GUILayout.Space(8);
                GUILayout.Label("스타일", EditorStyles.miniLabel);
                if (GUILayout.Toggle(_drawStyle == VfxLayerStyle.Glow, "빛", EditorStyles.toolbarButton, GUILayout.Width(32))) _drawStyle = VfxLayerStyle.Glow;
                if (GUILayout.Toggle(_drawStyle == VfxLayerStyle.Toon, "카툰", EditorStyles.toolbarButton, GUILayout.Width(40))) _drawStyle = VfxLayerStyle.Toon;

                GUILayout.Space(8);
                GUILayout.Label("그리는 면", EditorStyles.miniLabel);
                if (GUILayout.Toggle(!_drawOnGround, new GUIContent("화면", "화면과 마주 보는 면 — 보이는 그대로 그려짐"), EditorStyles.toolbarButton, GUILayout.Width(38))) _drawOnGround = false;
                if (GUILayout.Toggle(_drawOnGround, new GUIContent("바닥", "수평면 — 위에서 본 가로베기, 바닥 고리"), EditorStyles.toolbarButton, GUILayout.Width(38))) _drawOnGround = true;

                GUILayout.FlexibleSpace();
            }

            GUILayout.EndArea();
        }

        void ToolButton(DrawTool t, string label, string tip)
        {
            bool on = _tool == t;
            bool now = GUILayout.Toggle(on, new GUIContent(label, tip), EditorStyles.toolbarButton, GUILayout.Width(label.Length > 2 ? 48 : 34));
            if (now != on)
            {
                _tool = now ? t : DrawTool.Select;
                if (_tool != DrawTool.Select) StopPlay();
                SceneView.RepaintAll();
            }
        }

        GenericMenu StampMaskMenu()
        {
            var m = new GenericMenu();
            foreach (var k in VfxStudioGUI.AllMasks)
            {
                var kk = k;
                m.AddItem(new GUIContent(VfxStudioGUI.MaskName(k)), _stampMask == k, () => { _stampMask = kk; Repaint(); });
            }
            return m;
        }

        // ══════════════════════ 목록 ══════════════════════

        struct LibItem { public string Path, Label, Group; }
        readonly List<LibItem> _lib = new();
        bool _libDirty = true;

        void RefreshLibrary()
        {
            _lib.Clear();
            foreach (var dir in new[] { EffectsDir, PresetsDir })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null || go.GetComponent<VfxEffect>() == null) continue;

                    string rel = Path.GetDirectoryName(path).Replace('\\', '/');
                    string group = rel == EffectsDir ? "이펙트"
                        : rel.StartsWith(EffectsDir + "/") ? rel.Substring(EffectsDir.Length + 1)
                        : rel.StartsWith(PresetsDir) ? "내 프리셋" : rel;
                    _lib.Add(new LibItem { Path = path, Label = Path.GetFileNameWithoutExtension(path), Group = group });
                }
            }
            _lib.Sort((a, b) => a.Group != b.Group
                ? (a.Group == "이펙트" ? -1 : b.Group == "이펙트" ? 1 : string.CompareOrdinal(a.Group, b.Group))
                : string.CompareOrdinal(a.Label, b.Label));
            _libDirty = false;
        }

        void DrawLibrary(Rect r)
        {
            if (_libDirty) RefreshLibrary();

            GUILayout.BeginArea(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8));
            if (GUILayout.Button("+ 새 이펙트", GUILayout.Height(24))) VfxNewEffectWindow.ShowFor(this);
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

            string current = CurrentPrefabPath;
            _libScroll = EditorGUILayout.BeginScrollView(_libScroll);
            string lastGroup = null;
            foreach (var it in _lib)
            {
                if (!string.IsNullOrEmpty(_search) && it.Label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (it.Group != lastGroup)
                {
                    lastGroup = it.Group;
                    EditorGUILayout.Space(4);
                    GUILayout.Label(it.Group, EditorStyles.miniBoldLabel);
                }

                var row = EditorGUILayout.GetControlRect(false, 18);
                bool isCurrent = it.Path == current;
                if (isCurrent) EditorGUI.DrawRect(row, VfxStudioGUI.SelectedRow);
                GUI.Label(new Rect(row.x + 6, row.y, row.width - 6, row.height), it.Label, isCurrent ? EditorStyles.boldLabel : EditorStyles.label);

                var e = Event.current;
                if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
                {
                    if (e.button == 0)
                    {
                        var path = it.Path;
                        if (e.clickCount == 2 || !isCurrent) Later(() => OpenEffect(path));
                        e.Use();
                    }
                    else if (e.button == 1)
                    {
                        var path = it.Path;
                        var m = new GenericMenu();
                        m.AddItem(new GUIContent("열기"), false, () => OpenEffect(path));
                        m.AddItem(new GUIContent("프로젝트에서 보기"), false,
                            () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path)));
                        m.ShowAsContext();
                        e.Use();
                    }
                }
            }
            if (_lib.Count == 0)
                GUILayout.Label($"{EffectsDir}에 아직 이펙트가 없어요.", VfxStudioGUI.Hint);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("새로고침", EditorStyles.miniButton)) _libDirty = true;
            GUILayout.EndArea();
        }

        // ══════════════════════ 열기 · 저장 ══════════════════════

        bool IsPrefabInstance => _fx != null && PrefabUtility.IsOutermostPrefabInstanceRoot(_fx.gameObject);

        string CurrentPrefabPath => IsPrefabInstance ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_fx.gameObject) : null;

        bool CheckUnsaved()
        {
            if (_fx == null) return false;
            if (EditorApplication.timeSinceStartup < _unsavedCheckAt) return _unsaved;
            _unsavedCheckAt = EditorApplication.timeSinceStartup + 0.5;
            _unsaved = !IsPrefabInstance || PrefabUtility.HasPrefabInstanceAnyOverrides(_fx.gameObject, false);
            return _unsaved;
        }

        void MarkDirty() => _unsavedCheckAt = 0;

        /// <summary>지금 열린 이펙트를 떠나도 되는지 — 저장 안 한 게 있으면 물어봄</summary>
        bool ConfirmLeave()
        {
            if (_fx == null || !_owned || !CheckUnsaved()) return true;
            int r = EditorUtility.DisplayDialogComplex("VFX Studio", $"'{_fx.name}'에 저장하지 않은 변경이 있어요.", "저장", "취소", "저장 안 함");
            if (r == 1) return false;
            if (r == 0) Save();
            return true;
        }

        bool CanEdit()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            ShowNotification(new GUIContent("플레이 중에는 편집할 수 없어요"));
            return false;
        }

        /// <summary>미리보기 씬이 아니면 열기 (저장 여부는 Unity가 물어봄)</summary>
        static bool EnsureStudioScene()
        {
            if (SceneManager.GetActiveScene().path == VfxSetup.ScenePath) return true;
            VfxSetup.OpenPreviewScene();
            return SceneManager.GetActiveScene().path == VfxSetup.ScenePath;
        }

        void CloseCurrent()
        {
            StopPlay();
            if (_fx != null && _owned) DestroyImmediate(_fx.gameObject);
            _fx = null;
            _sel = null;
            _owned = false;
            CollectRows();
        }

        public void OpenEffect(string path)
        {
            if (!CanEdit() || !ConfirmLeave()) return;
            if (!EnsureStudioScene()) return;
            CloseCurrent();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            var fx = go.GetComponent<VfxEffect>();
            go.transform.position = VfxPresets.AnchorPosition(fx.PreviewAnchor);
            Adopt(fx, true);
        }

        /// <summary>씬에 이미 있는 이펙트를 편집 (스튜디오가 치우지 않음)</summary>
        public void EditInScene(VfxEffect fx)
        {
            if (!CanEdit() || !ConfirmLeave()) return;
            CloseCurrent();
            Adopt(fx, false);
        }

        void Adopt(VfxEffect fx, bool owned)
        {
            _fx = fx;
            _owned = owned;
            _sel = null;
            _inspTab = 1;
            _tool = DrawTool.Select;
            MarkDirty();
            CollectRows();
            SetScrub(0.15f);
            Selection.activeGameObject = fx.gameObject;
            Repaint();
        }

        /// <summary>마법사에서 — 프리셋·내 프리셋·빈 이펙트로 새로 만들고 바로 프리팹으로 저장</summary>
        public void CreateNew(string name, VfxPreset preset, string userPresetPath, VfxPalette palette,
                              VfxPreviewAnchor anchor, VfxPlayMode mode)
        {
            if (!CanEdit() || !ConfirmLeave()) return;
            if (!EnsureStudioScene()) return;
            CloseCurrent();

            name = SafeName(name);
            VfxEffect fx;
            float previewTime = 0.15f;

            if (!string.IsNullOrEmpty(userPresetPath))
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(userPresetPath);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src, SceneManager.GetActiveScene());
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = name;
                fx = go.GetComponent<VfxEffect>();
                if (palette != null) fx.Palette = palette;
                go.transform.position = VfxPresets.AnchorPosition(fx.PreviewAnchor);
            }
            else if (preset != null)
            {
                fx = VfxPresets.Create(preset, name, palette);
                previewTime = preset.PreviewTime;
            }
            else
            {
                fx = VfxPresets.CreateEmpty(name, anchor, palette != null ? palette : VfxPresets.LoadPalette("Palette_Dark"));
                fx.PlayMode = mode;
            }

            VfxSetup.EnsureFolder(EffectsDir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{EffectsDir}/{name}.prefab");
            PrefabUtility.SaveAsPrefabAssetAndConnect(fx.gameObject, path, InteractionMode.AutomatedAction);

            Adopt(fx, true);
            SetScrub(previewTime);
            _libDirty = true;

            if (preset == null && string.IsNullOrEmpty(userPresetPath))
            {
                _tool = DrawTool.Arc;
                ShowNotification(new GUIContent("씬 뷰에서 드래그해서 그려 보세요"));
            }
            if (anchor == VfxPreviewAnchor.Feet || (preset != null && preset.Anchor == VfxPreviewAnchor.Feet)) ViewFront();
        }

        void Save()
        {
            if (_fx == null) return;
            StopPlay();

            if (IsPrefabInstance)
            {
                PrefabUtility.ApplyPrefabInstance(_fx.gameObject, InteractionMode.UserAction);
            }
            else
            {
                VfxSetup.EnsureFolder(EffectsDir);
                string path = AssetDatabase.GenerateUniqueAssetPath($"{EffectsDir}/{SafeName(_fx.name)}.prefab");
                PrefabUtility.SaveAsPrefabAssetAndConnect(_fx.gameObject, path, InteractionMode.UserAction);
                _owned = true;
            }

            AssetDatabase.SaveAssets();
            MarkDirty();
            _libDirty = true;
            ShowNotification(new GUIContent("저장했어요"));
        }

        void SaveAs()
        {
            if (_fx == null) return;
            string path = EditorUtility.SaveFilePanelInProject("다른 이름으로 저장", _fx.name + " 복사", "prefab", "", EffectsDir);
            if (string.IsNullOrEmpty(path)) return;
            StopPlay();

            // 연결 없는 복제본을 새 프리팹으로 — 원래 프리팹은 그대로
            var copy = Instantiate(_fx.gameObject, _fx.transform.position, _fx.transform.rotation);
            copy.name = Path.GetFileNameWithoutExtension(path);
            PrefabUtility.SaveAsPrefabAssetAndConnect(copy, path, InteractionMode.AutomatedAction);

            if (_owned) DestroyImmediate(_fx.gameObject);
            Adopt(copy.GetComponent<VfxEffect>(), true);
            _libDirty = true;
        }

        void Revert()
        {
            if (_fx == null || !IsPrefabInstance) return;
            if (!EditorUtility.DisplayDialog("VFX Studio", "마지막으로 저장한 상태로 되돌릴까요?", "되돌리기", "취소")) return;
            StopPlay();
            PrefabUtility.RevertPrefabInstance(_fx.gameObject, InteractionMode.UserAction);
            _sel = null;
            CollectRows();
            _fx.Refresh();
            MarkDirty();
        }

        void SaveAsPreset()
        {
            if (_fx == null) return;
            if (CheckUnsaved()) Save();
            string src = CurrentPrefabPath;
            if (string.IsNullOrEmpty(src)) return;

            VfxSetup.EnsureFolder(PresetsDir);
            string dst = AssetDatabase.GenerateUniqueAssetPath($"{PresetsDir}/{Path.GetFileName(src)}");
            AssetDatabase.CopyAsset(src, dst);
            _libDirty = true;
            ShowNotification(new GUIContent("내 프리셋에 추가했어요"));
        }

        /// <summary>색 바리에이션 — 팔레트만 바꾼 프리팹 변형(Variant). 원본을 고치면 같이 바뀜</summary>
        public void MakeVariants(List<VfxPalette> palettes)
        {
            if (_fx == null || palettes.Count == 0) return;
            if (!IsPrefabInstance || CheckUnsaved()) Save();
            string basePath = CurrentPrefabPath;
            if (string.IsNullOrEmpty(basePath)) return;

            var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            string dir = Path.GetDirectoryName(basePath).Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(basePath);
            int made = 0, skipped = 0;

            foreach (var pal in palettes)
            {
                string suffix = pal.name.StartsWith("Palette_") ? pal.name.Substring(8) : pal.name;
                string path = $"{dir}/{baseName}_{suffix}.prefab";
                if (File.Exists(path)) { skipped++; continue; }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
                inst.GetComponent<VfxEffect>().Palette = pal;
                PrefabUtility.SaveAsPrefabAsset(inst, path);
                DestroyImmediate(inst);
                made++;
            }

            AssetDatabase.SaveAssets();
            _libDirty = true;
            ShowNotification(new GUIContent(skipped > 0 ? $"{made}개 만듦 · 이미 있는 {skipped}개는 건너뜀" : $"{made}개 만들었어요"));
        }

        static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "새 이펙트";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        // ══════════════════════ 레이어 목록 · 선택 ══════════════════════

        void CollectRows()
        {
            _rows.Clear();
            if (_fx == null) return;
            foreach (var c in _fx.GetComponentsInChildren<Component>(true))
                if (c is VfxLayer || c is VfxLight) _rows.Add(c);
            _fx.Collect();
        }

        void Select(Component c)
        {
            _sel = c;
            _inspTab = c != null ? 0 : 1;
            Selection.activeGameObject = c != null ? c.gameObject : _fx != null ? _fx.gameObject : null;
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>값을 바꾼 뒤 공통 처리 — 프리팹 변경 기록, 다시 그리기</summary>
        void Changed(Object target)
        {
            if (target != null) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            MarkDirty();
            if (_fx != null && !_playing) SetScrub(_scrubTime);
            SceneView.RepaintAll();
            Repaint();
        }

        string UniqueName(string baseName)
        {
            if (_fx == null) return baseName;
            for (int i = 1; ; i++)
            {
                string n = $"{baseName} {i}";
                if (_fx.transform.Find(n) == null) return n;
            }
        }

        void DeleteSelected()
        {
            if (_sel == null || _fx == null) return;
            var go = _sel.gameObject;
            _sel = null;
            Undo.DestroyObjectImmediate(go);
            CollectRows();
            Changed(null);
        }

        void DuplicateSelected()
        {
            if (_sel == null || _fx == null) return;
            var src = _sel.gameObject;
            var copy = Instantiate(src, src.transform.parent);
            copy.name = UniqueName(Strip(src.name));
            Undo.RegisterCreatedObjectUndo(copy, "VFX 레이어 복제");
            CollectRows();
            Component c = copy.GetComponent<VfxLayer>();
            if (c == null) c = copy.GetComponent<VfxLight>();
            Select(c);
            Changed(null);
        }

        static string Strip(string name)
        {
            int i = name.LastIndexOf(' ');
            return i > 0 && int.TryParse(name.Substring(i + 1), out _) ? name.Substring(0, i) : name;
        }

        void ToggleActive(Component c)
        {
            Undo.RecordObject(c.gameObject, "VFX 레이어 켜기/끄기");
            c.gameObject.SetActive(!c.gameObject.activeSelf);
            Changed(c.gameObject);
        }

        void Randomize()
        {
            if (_fx == null) return;
            var targets = _sel is VfxLayer one ? new[] { one } : _fx.GetComponentsInChildren<VfxLayer>(true);
            if (targets.Length == 0) return;

            Undo.RecordObjects(targets, "VFX 변형 주사위");
            foreach (var l in targets)
            {
                float J(float amount) => 1f + UnityEngine.Random.Range(-amount, amount);
                switch (l.Shape)
                {
                    case VfxShape.Arc:
                        l.Arc.Angle = Mathf.Clamp(l.Arc.Angle * J(0.1f), 10f, 360f);
                        l.Arc.Radius *= J(0.08f);
                        l.Arc.Width *= J(0.12f);
                        l.Arc.HeadBias = Mathf.Clamp(l.Arc.HeadBias + UnityEngine.Random.Range(-0.08f, 0.08f), 0.1f, 0.9f);
                        break;
                    case VfxShape.Ring:
                        l.Ring.Radius *= J(0.1f);
                        l.Ring.Width *= J(0.15f);
                        break;
                    case VfxShape.Pillar:
                        l.Pillar.Height *= J(0.1f);
                        l.Pillar.Radius *= J(0.08f);
                        break;
                    case VfxShape.Burst:
                        l.Burst.Seed = UnityEngine.Random.Range(1, 9999);
                        l.Burst.Speed *= J(0.1f);
                        break;
                    case VfxShape.Streak:
                        l.Streak.Seed = UnityEngine.Random.Range(1, 9999);
                        break;
                    case VfxShape.Card:
                        l.Card.Size *= J(0.1f);
                        break;
                    case VfxShape.Cone:
                        l.Cone.Twist += UnityEngine.Random.Range(-0.1f, 0.1f);
                        l.Cone.BaseRadius *= J(0.08f);
                        l.Cone.StreakCount = Mathf.Clamp(l.Cone.StreakCount + UnityEngine.Random.Range(-2, 3), 1, 48);
                        break;
                }
                if (l.StartTime > 0.001f) l.StartTime = Mathf.Max(0f, l.StartTime + UnityEngine.Random.Range(-0.01f, 0.01f));
                l.FlameSpeed *= J(0.2f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(l);
            }
            Changed(null);
        }

        // ══════════════════════ 재생 ══════════════════════

        void TogglePlay()
        {
            if (_playing) StopPlay();
            else StartPlay();
        }

        void StartPlay()
        {
            if (_fx == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            _fx.LinearPreview = false;
            VfxEffect.CameraOverride = SceneCam();
            _fx.Play();
            _playing = true;
            _lastTick = EditorApplication.timeSinceStartup;
            Repaint();
        }

        void StopPlay()
        {
            if (!_playing) return;
            _playing = false;
            if (_fx != null)
            {
                _fx.Pause();
                SetScrub(_scrubTime);
            }
            Repaint();
        }

        /// <summary>타임라인 시각으로 미리보기 (반복 이펙트도 블록 순서대로 한 번)</summary>
        void SetScrub(float t)
        {
            _scrubTime = Mathf.Max(0f, t);
            if (_fx == null) return;
            _fx.LinearPreview = true;
            VfxEffect.CameraOverride = SceneCam();
            _fx.SetTime(_scrubTime);
            SceneView.RepaintAll();
        }

        void OnUpdate()
        {
            if (!_playing) return;
            if (_fx == null || EditorApplication.isPlayingOrWillChangePlaymode) { _playing = false; Repaint(); return; }

            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min((float)(now - _lastTick), 0.1f);
            _lastTick = now;

            VfxEffect.CameraOverride = SceneCam();
            _fx.Tick(dt * _playSpeed);

            if (!_fx.IsPlaying)
            {
                if (_loopPlay) _fx.Play();
                else StopPlay();
            }

            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            Repaint();
        }

        static Camera SceneCam() =>
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;

        // ══════════════════════ 단축키 ══════════════════════

        void HandleCommands()
        {
            var e = Event.current;
            if (_fx == null) return;

            // Unity 명령 (Edit 메뉴 단축키) — 이 창에 포커스가 있을 때
            if (e.type == EventType.ValidateCommand && (e.commandName == "Duplicate" || e.commandName == "SoftDelete" || e.commandName == "Delete"))
            {
                if (_sel != null) e.Use();
                return;
            }
            if (e.type == EventType.ExecuteCommand)
            {
                if (e.commandName == "Duplicate") { DuplicateSelected(); e.Use(); }
                else if (e.commandName == "SoftDelete" || e.commandName == "Delete") { DeleteSelected(); e.Use(); }
                return;
            }

            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return;
            switch (e.keyCode)
            {
                case KeyCode.Space:
                    TogglePlay();
                    e.Use();
                    break;
                case KeyCode.Delete:
                    DeleteSelected();
                    e.Use();
                    break;
                case KeyCode.D when e.control || e.command:
                    DuplicateSelected();
                    e.Use();
                    break;
                case KeyCode.Escape:
                    _tool = DrawTool.Select;
                    SceneView.RepaintAll();
                    e.Use();
                    break;
            }
        }

        // ══════════════════════ 보기 ══════════════════════

        static readonly Vector3 DummyEye = new(0f, 1.6f, 0.05f);

        void ViewFront()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.LookAt(new Vector3(0f, 1.1f, 0.4f), Quaternion.Euler(12f, 215f, 0f), 2.6f);
            sv.Repaint();
        }

        void ViewTop()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.LookAt(new Vector3(0f, 1f, 0.6f), Quaternion.Euler(80f, 180f, 0f), 2.8f);
            sv.Repaint();
        }

        void ViewFirstPerson()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            var tmp = new GameObject("__eye") { hideFlags = HideFlags.HideAndDontSave };
            tmp.transform.SetPositionAndRotation(DummyEye, Quaternion.identity);
            sv.AlignViewToObject(tmp.transform);
            DestroyImmediate(tmp);
            sv.Repaint();
        }
    }
}
