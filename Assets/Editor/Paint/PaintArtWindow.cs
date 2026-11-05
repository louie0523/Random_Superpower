#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → UI → UI 그림 그리기 창 — 스프레이 · 물감 · 판 그림을 직접 그려 PNG로.
///   새로: 크기 골라 투명 그림판 / 열기: UiArt 목록 · 선택한 그림 (원래 그림도 지우개로 지울 수 있음)
///   저장: 연 파일에 덮어씀 (UiArt의 Spray_v8.png 등을 덮으면 'UI 프리팹 만들기'가 그 그림을 씀) / 다른 이름: 새 스프라이트
///   보정 · 붓은 타이틀 벽 칠하기와 같음. Ctrl+Z / Ctrl+Y · [ ] = 크기 · 휠 = 확대
/// </summary>
public class PaintArtWindow : EditorWindow
{
    const string ArtFolder = "Assets/UI/Game/UiArt";

    static readonly (string label, Vector2Int size)[] Presets =
    {
        ("긴 스프레이 1024×256", new Vector2Int(1024, 256)),
        ("흘러내림 스프레이 512×256", new Vector2Int(512, 256)),
        ("밑줄 스프레이 512×128", new Vector2Int(512, 128)),
        ("물감 256×256", new Vector2Int(256, 256)),
        ("큰 그림 512×512", new Vector2Int(512, 512)),
        ("판 · 9칸 128×128", new Vector2Int(128, 128)),
        ("배경 1920×1080", new Vector2Int(1920, 1080)),
    };

    PaintCanvas _canvas;
    readonly StrokeShaper _shaper = new();
    readonly List<PaintDab> _dabs = new();
    string _path;
    bool _drawing, _dirty;
    int _seed = 1;
    float _zoom = 1f;
    int _bg;
    Rect _area, _img;
    Vector2 _scroll;
    static Texture2D _checker;

    [MenuItem("Tools/UI/UI 그림 그리기 창")]
    public static void Open() => GetWindow<PaintArtWindow>("UI 그림 그리기").minSize = new Vector2(760, 460);

    /// <summary>이 PNG를 열어서 (다른 도구에서)</summary>
    public static void OpenFile(string path)
    {
        var w = GetWindow<PaintArtWindow>("UI 그림 그리기");
        w.Load(path);
    }

    void OnEnable() => wantsMouseMove = true;

    void OnDisable()
    {
        _canvas?.Dispose();
        _canvas = null;
    }

    void Update()
    {
        if (!_drawing || _canvas == null) return;
        _shaper.Tick((float)EditorApplication.timeSinceStartup);
        Draw();
        Repaint();
    }

    // ══════════════════ 화면 ══════════════════

    void OnGUI()
    {
        Toolbar();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(270)))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.LabelField("붓", EditorStyles.boldLabel);
                PaintBrushGUI.Draw("[ ] 키로도 · 그림 짧은 변 기준");
                EditorGUILayout.Space(10);
                Info();
                EditorGUILayout.EndScrollView();
            }
            _area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            CanvasArea();
        }
        Keys();
    }

    void Toolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (EditorGUILayout.DropdownButton(new GUIContent("새로"), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(60)))
            {
                var m = new GenericMenu();
                foreach (var (label, size) in Presets) m.AddItem(new GUIContent(label), false, () => New(size));
                m.ShowAsContext();
            }
            if (EditorGUILayout.DropdownButton(new GUIContent("열기"), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(60)))
            {
                var m = new GenericMenu();
                var sel = Selection.activeObject as Texture2D;
                if (sel != null) m.AddItem(new GUIContent("선택한 그림: " + sel.name), false, () => Load(AssetDatabase.GetAssetPath(sel)));
                else m.AddDisabledItem(new GUIContent("선택한 그림 (Project 창에서 PNG 선택)"));
                m.AddSeparator("");
                foreach (var p in ArtFiles()) m.AddItem(new GUIContent(p.Substring(ArtFolder.Length + 1).Replace('/', '∕')), false, () => Load(p));
                m.ShowAsContext();
            }
            GUI.enabled = _canvas != null;
            if (GUILayout.Button(_dirty ? "저장 *" : "저장", EditorStyles.toolbarButton, GUILayout.Width(60))) Save(false);
            if (GUILayout.Button("다른 이름으로", EditorStyles.toolbarButton, GUILayout.Width(90))) Save(true);
            GUILayout.Space(12);
            GUI.enabled = _canvas != null && _canvas.CanUndo;
            if (GUILayout.Button("되돌리기", EditorStyles.toolbarButton, GUILayout.Width(64))) { _canvas.Undo(); _dirty = true; }
            GUI.enabled = _canvas != null && _canvas.CanRedo;
            if (GUILayout.Button("다시", EditorStyles.toolbarButton, GUILayout.Width(44))) { _canvas.Redo(); _dirty = true; }
            GUI.enabled = _canvas != null;
            if (GUILayout.Button("전부 지우기", EditorStyles.toolbarButton, GUILayout.Width(80)) &&
                EditorUtility.DisplayDialog("전부 지우기", "그림을 전부 지울까? (되돌리기 됨)", "지우기", "취소"))
            {
                _canvas.ClearPaint();
                _dirty = true;
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            _bg = GUILayout.Toolbar(_bg, new[] { "격자", "어둡게", "밝게" }, EditorStyles.toolbarButton, GUILayout.Width(150));
            if (GUILayout.Button("맞춤", EditorStyles.toolbarButton, GUILayout.Width(44))) _zoom = 1f;
        }
    }

    void Info()
    {
        EditorGUILayout.LabelField("그림", EditorStyles.boldLabel);
        if (_canvas == null)
        {
            EditorGUILayout.HelpBox("'새로'로 그림판을 만들거나 '열기'로 그림을 불러와.", MessageType.Info);
            return;
        }
        EditorGUILayout.LabelField("크기", $"{_canvas.Width} × {_canvas.Height}");
        EditorGUILayout.LabelField("파일", string.IsNullOrEmpty(_path) ? "(아직 저장 안 함)" : Path.GetFileName(_path));
        if (!string.IsNullOrEmpty(_path) && GUILayout.Button("Project 창에서 보기"))
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(_path));
        EditorGUILayout.HelpBox("그리고 손 떼기 전에 잠깐 멈추면 직선 · 원으로 반듯해져 (그대로 끌면 크기 조절).\nUiArt의 PNG를 덮어쓰면 'UI 프리팹 만들기'가 그 그림을 씀.", MessageType.None);
    }

    void CanvasArea()
    {
        EditorGUI.DrawRect(_area, new Color(0.16f, 0.16f, 0.17f));
        if (_canvas == null) return;

        float fit = Mathf.Min((_area.width - 24f) / _canvas.Width, (_area.height - 24f) / _canvas.Height);
        float s = Mathf.Max(0.05f, fit * _zoom);
        var size = new Vector2(_canvas.Width * s, _canvas.Height * s);
        _img = new Rect(_area.center - size * 0.5f, size);

        GUI.BeginClip(_area);
        var local = new Rect(_img.position - _area.position, _img.size);
        if (_bg == 0) GUI.DrawTextureWithTexCoords(local, Checker, new Rect(0, 0, local.width / 16f, local.height / 16f));
        else EditorGUI.DrawRect(local, _bg == 1 ? new Color(0.1f, 0.09f, 0.14f) : new Color(0.93f, 0.92f, 0.9f));
        GUI.DrawTexture(local, _canvas.Display, ScaleMode.StretchToFill, true);

        // 붓 크기 표시
        var e = Event.current;
        if (_area.Contains(e.mousePosition + _area.position) || _img.Contains(e.mousePosition + _area.position))
        {
            float r = RadiusPx() * s;
            Handles.color = new Color(1f, 1f, 1f, 0.8f);
            Handles.DrawWireDisc(e.mousePosition, Vector3.forward, r);
            Handles.color = new Color(0f, 0f, 0f, 0.6f);
            Handles.DrawWireDisc(e.mousePosition, Vector3.forward, r + 1f);
        }
        GUI.EndClip();

        Mouse();
    }

    // ══════════════════ 입력 ══════════════════

    float RadiusPx()
    {
        float shortSide = Mathf.Min(_canvas.Width, _canvas.Height);
        return PaintBrushGUI.Brush.Radius(0.006f, 0.22f) * Mathf.Max(64f, shortSide);
    }

    Vector2 ToPx(Vector2 gui)
    {
        var u = (gui - _img.position) / _img.size;
        return new Vector2(u.x * _canvas.Width, (1f - u.y) * _canvas.Height);
    }

    void Mouse()
    {
        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        float t = (float)EditorApplication.timeSinceStartup;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown when e.button == 0 && _img.Contains(e.mousePosition):
                GUIUtility.hotControl = id;
                PaintBrushGUI.Setup(_shaper, PaintBrushGUI.Brush, RadiusPx(), new Vector2(0f, -1f));
                _shaper.Begin(ToPx(e.mousePosition), t, _seed++);
                _drawing = true;
                Draw();
                e.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                _shaper.Move(ToPx(e.mousePosition), t);
                Draw();
                e.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                _shaper.Finish(t);
                Draw();
                _canvas.Commit();
                _drawing = false;
                _dirty = true;
                e.Use();
                break;
            case EventType.ScrollWheel when _area.Contains(e.mousePosition):
                _zoom = Mathf.Clamp(_zoom * (e.delta.y < 0 ? 1.12f : 0.89f), 0.25f, 8f);
                e.Use();
                break;
            case EventType.MouseMove:
                Repaint();
                break;
        }
    }

    void Keys()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || _canvas == null) return;
        bool ctrl = e.control || e.command;
        if (ctrl && e.keyCode == KeyCode.Z) { if (e.shift) _canvas.Redo(); else _canvas.Undo(); _dirty = true; e.Use(); }
        else if (ctrl && e.keyCode == KeyCode.Y) { _canvas.Redo(); _dirty = true; e.Use(); }
        else if (ctrl && e.keyCode == KeyCode.S) { Save(false); e.Use(); }
        else if (e.keyCode == KeyCode.LeftBracket) { PaintBrushGUI.Nudge(-0.06f); e.Use(); }
        else if (e.keyCode == KeyCode.RightBracket) { PaintBrushGUI.Nudge(0.06f); e.Use(); }
        Repaint();
    }

    void Draw()
    {
        if (_canvas == null) return;
        _shaper.Build(_dabs);
        _canvas.DrawStroke(PaintBrushGUI.Brush, _dabs, Mathf.Max(2f, _shaper.Radius * 0.22f));
    }

    // ══════════════════ 파일 ══════════════════

    void New(Vector2Int size)
    {
        if (!ConfirmDiscard()) return;
        _canvas?.Dispose();
        _canvas = new PaintCanvas(size.x, size.y, null, Color.clear, 40);
        _path = null;
        _dirty = false;
        _zoom = 1f;
    }

    void Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !ConfirmDiscard()) return;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) { EditorUtility.DisplayDialog("열기", "그림을 못 읽음: " + path, "확인"); return; }
        _canvas?.Dispose();
        _canvas = new PaintCanvas(tex.width, tex.height, null, Color.clear, 40);
        _canvas.LoadPaint(tex);          // 원래 그림도 칠 그림으로 → 지우개로 지울 수 있음
        _path = path;
        _dirty = false;
        _zoom = 1f;
        Repaint();
    }

    void Save(bool asNew)
    {
        if (_canvas == null) return;
        string path = _path;
        bool fresh = asNew || string.IsNullOrEmpty(path);
        if (fresh)
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder)) Directory.CreateDirectory(ArtFolder);
            path = EditorUtility.SaveFilePanelInProject("그림 저장", string.IsNullOrEmpty(_path) ? "Painted" : Path.GetFileNameWithoutExtension(_path) + "_copy",
                                                         "png", "PNG로 저장 (스프라이트로 가져옴)", ArtFolder);
            if (string.IsNullOrEmpty(path)) return;
            fresh = !File.Exists(path);
        }

        var t = _canvas.ReadDisplay();
        File.WriteAllBytes(path, t.EncodeToPNG());
        DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (fresh && AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(st);
            ti.SaveAndReimport();
        }
        _path = path;
        _dirty = false;
        Debug.Log($"[UI 그림] 저장 → {path}", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
    }

    bool ConfirmDiscard() =>
        !_dirty || EditorUtility.DisplayDialog("UI 그림", "저장 안 한 그림이 있어. 버릴까?", "버리기", "취소");

    static IEnumerable<string> ArtFiles()
    {
        if (!Directory.Exists(ArtFolder)) return Enumerable.Empty<string>();
        return Directory.GetFiles(ArtFolder, "*.png", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).OrderBy(p => p);
    }

    static Texture2D Checker
    {
        get
        {
            if (_checker != null) return _checker;
            _checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            var a = new Color(0.32f, 0.32f, 0.34f);
            var b = new Color(0.25f, 0.25f, 0.27f);
            _checker.SetPixels(new[] { a, b, b, a });
            _checker.Apply();
            return _checker;
        }
    }
}
#endif
