#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PaintSurface 인스펙터 — '씬에서 칠하기'를 켜고 Scene 뷰에서 벽 · 바닥에 기본 그래피티를 그림 → '기본 그래피티 저장'.
/// 바탕(빈 면)은 안 건드리고, 그 위 칠 그림(투명 PNG = Default Paint)만 고침 → 플레이어가 지우개로 지울 수 있는 그림.
///   Alt + 끌기 = 화면 돌리기 그대로 · Ctrl+Z / Ctrl+Y · [ ] = 크기
/// </summary>
[CustomEditor(typeof(PaintSurface))]
public class PaintSurfaceEditor : Editor
{
    static PaintSurfaceEditor _active;

    readonly StrokeShaper _shaper = new();
    readonly List<PaintDab> _dabs = new();
    bool _editing, _drawing, _dirty;
    int _seed = 1;

    PaintSurface S => (PaintSurface)target;
    PaintCanvas C => S != null ? S.Canvas : null;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("기본 그래피티 칠하기 (개발자)", EditorStyles.boldLabel);

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 중엔 타이틀 '벽 칠하기'로.", MessageType.None);
            return;
        }

        var on = GUILayout.Toggle(_editing, _editing ? "씬에서 칠하는 중 — 끄기" : "씬에서 칠하기", "Button", GUILayout.Height(28));
        if (on != _editing)
        {
            if (on) Begin();
            else Stop(true);
        }
        if (!_editing || C == null) return;

        EditorGUILayout.Space(4);
        PaintBrushGUI.Draw("[ ] 키로도 · 반지름 0.03 ~ 0.6m");
        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = C.CanUndo;
            if (GUILayout.Button("되돌리기")) { C.Undo(); _dirty = true; }
            GUI.enabled = C.CanRedo;
            if (GUILayout.Button("다시")) { C.Redo(); _dirty = true; }
            GUI.enabled = true;
            if (GUILayout.Button("전부 지우기")) { C.ClearPaint(); _dirty = true; }
        }
        GUI.backgroundColor = _dirty ? new Color(1f, 0.84f, 0.2f) : Color.white;
        if (GUILayout.Button(_dirty ? "기본 그래피티 저장 *" : "기본 그래피티 저장", GUILayout.Height(30))) Save();
        GUI.backgroundColor = Color.white;
        EditorGUILayout.HelpBox("Scene 뷰에서 왼쪽 마우스로 칠하기 · 지우개는 바탕(빈 면)까지만.", MessageType.None);
    }

    void OnDisable()
    {
        if (_editing) Stop(true);
    }

    void Begin()
    {
        if (_active != null && _active != this) _active.Stop(true);
        _active = this;
        if (C == null) S.RebuildPreview();
        if (C == null) { Debug.LogWarning("[칠하기] 미리보기를 못 만듦 — 셰이더 · 렌더러 확인", S); return; }
        S.EditorPainting = true;
        _editing = true;
        _dirty = false;
        SceneView.duringSceneGui += OnScene;
        EditorApplication.update += Tick;
        Tools.hidden = true;
    }

    void Stop(bool ask)
    {
        if (!_editing) return;
        if (ask && _dirty && EditorUtility.DisplayDialog("칠하기", "저장 안 한 그래피티가 있어. 저장할까?", "저장", "버리기"))
            Save();
        SceneView.duringSceneGui -= OnScene;
        EditorApplication.update -= Tick;
        Tools.hidden = false;
        _editing = _drawing = false;
        if (S != null)
        {
            S.EditorPainting = false;
            S.RebuildPreview();        // 저장한 그림(또는 버리기 전 그림)으로
        }
        if (_active == this) _active = null;
        Repaint();
    }

    /// <summary>칠 그림(투명) → Default Paint PNG</summary>
    void Save()
    {
        var s = S;
        string path = s.DefaultPaint != null ? AssetDatabase.GetAssetPath(s.DefaultPaint) : null;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
        {
            string scene = s.gameObject.scene.path;
            string dir = string.IsNullOrEmpty(scene) ? "Assets" : Path.GetDirectoryName(scene).Replace('\\', '/');
            string folder = $"{dir}/{s.gameObject.scene.name}Art";
            Directory.CreateDirectory(folder);
            path = $"{folder}/{s.Id}_paint.png";
        }

        var t = C.ReadPaint();
        File.WriteAllBytes(path, t.EncodeToPNG());
        DestroyImmediate(t);
        AssetDatabase.Refresh();
        ImportPaint(path);

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        Undo.RecordObject(s, "기본 그래피티 저장");
        s.DefaultPaint = tex;
        EditorUtility.SetDirty(s);
        _dirty = false;
        Debug.Log($"[칠하기] 기본 그래피티 저장 → {path}", tex);
    }

    /// <summary>투명 칠 그림 가져오기 설정 (Default · Clamp · 알파)</summary>
    public static void ImportPaint(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is TextureImporter ti &&
            (ti.textureType != TextureImporterType.Default || ti.wrapMode != TextureWrapMode.Clamp || !ti.alphaIsTransparency))
        {
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }
    }

    // ══════════════════ Scene 뷰 ══════════════════

    void Tick()
    {
        if (!_drawing || C == null) return;
        _shaper.Tick((float)EditorApplication.timeSinceStartup);
        Draw();
        SceneView.RepaintAll();
    }

    void OnScene(SceneView view)
    {
        if (!_editing || S == null || C == null) return;
        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
        float t = (float)EditorApplication.timeSinceStartup;
        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        bool hit = S.Hit(ray, out var px);

        if (hit && S.Collider.Raycast(ray, out var h, 500f))
        {
            Handles.color = new Color(1f, 1f, 1f, 0.9f);
            Handles.DrawWireDisc(h.point, h.normal, RadiusMeters());
            if (e.type == EventType.MouseMove) view.Repaint();
        }

        if (e.alt) return;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown when e.button == 0 && hit:
                GUIUtility.hotControl = id;
                PaintBrushGUI.Setup(_shaper, PaintBrushGUI.Brush, RadiusMeters() * S.PixelsPerMeter, S.DripDirection);
                _shaper.Begin(px, t, _seed++);
                _drawing = true;
                Draw();
                e.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                if (hit) _shaper.Move(px, t);
                else _shaper.Tick(t);
                Draw();
                e.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                _shaper.Finish(t);
                Draw();
                C.Commit();
                _drawing = false;
                _dirty = true;
                Repaint();
                e.Use();
                break;
            case EventType.KeyDown:
                bool ctrl = e.control || e.command;
                if (ctrl && e.keyCode == KeyCode.Z) { if (e.shift) C.Redo(); else C.Undo(); _dirty = true; e.Use(); }
                else if (ctrl && e.keyCode == KeyCode.Y) { C.Redo(); _dirty = true; e.Use(); }
                else if (e.keyCode == KeyCode.LeftBracket) { PaintBrushGUI.Nudge(-0.06f); Repaint(); e.Use(); }
                else if (e.keyCode == KeyCode.RightBracket) { PaintBrushGUI.Nudge(0.06f); Repaint(); e.Use(); }
                break;
        }
    }

    static float RadiusMeters() => PaintBrushGUI.Brush.Radius(0.03f, 0.6f);

    void Draw()
    {
        _shaper.Build(_dabs);
        C.DrawStroke(PaintBrushGUI.Brush, _dabs, PaintBrush.OutlinePx(_shaper.Radius));
    }
}
#endif
