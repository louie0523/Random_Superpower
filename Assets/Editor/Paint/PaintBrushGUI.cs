#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>에디터 칠하기 도구들이 같이 쓰는 붓 패널 (붓 · 색 · 크기 · 보정) — 설정은 에디터에 기억</summary>
public static class PaintBrushGUI
{
    const string Pref = "PaintTools.Brush";
    static PaintBrush _brush;
    static readonly string[] Kinds = { "스프레이", "마커", "지우개" };

    /// <summary>에디터 도구 공용 붓 (창 · 씬이 같은 붓)</summary>
    public static PaintBrush Brush
    {
        get
        {
            if (_brush != null) return _brush;
            _brush = new PaintBrush();
            string s = EditorPrefs.GetString(Pref, "");
            if (!string.IsNullOrEmpty(s)) EditorJsonUtility.FromJsonOverwrite(s, _brush);
            return _brush;
        }
    }

    /// <summary>붓 패널 — 바뀌면 true</summary>
    public static bool Draw(string sizeHint = null)
    {
        var b = Brush;
        EditorGUI.BeginChangeCheck();

        b.Kind = (BrushKind)GUILayout.Toolbar((int)b.Kind, Kinds, GUILayout.Height(24));
        EditorGUILayout.Space(4);

        // 물감 색 — 테마 색 바로 고르기 + 직접
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (var c in PaintBrush.Palette)
            {
                var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
                EditorGUI.DrawRect(r, Color.black);
                EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), c);
                if (Same(c, b.Color)) EditorGUI.DrawRect(new Rect(r.x, r.yMax - 3, r.width, 3), new Color(1f, 0.84f, 0.2f));
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    b.Color = c;
                    if (b.IsEraser) b.Kind = BrushKind.Spray;
                    GUI.changed = true;
                    Event.current.Use();
                }
            }
        }
        b.Color = EditorGUILayout.ColorField("색", b.Color);
        b.Size = EditorGUILayout.Slider(new GUIContent("크기", sizeHint ?? "[ ] 키로도"), b.Size, 0f, 1f);
        b.Opacity = EditorGUILayout.Slider("진하기", b.Opacity, 0.05f, 1f);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("보정", EditorStyles.boldLabel);
        b.Stabilize = EditorGUILayout.ToggleLeft("손떨림 보정", b.Stabilize);
        if (b.Stabilize) b.StabilizeAmount = EditorGUILayout.Slider("   세기", b.StabilizeAmount, 0f, 1f);
        b.Drips = EditorGUILayout.ToggleLeft("흘러내림 (스프레이)", b.Drips);
        b.Outline = EditorGUILayout.ToggleLeft("카툰 외곽선", b.Outline);
        b.Snap = EditorGUILayout.ToggleLeft("반듯하게 (그리고 잠깐 멈추면 직선 · 원)", b.Snap);

        bool changed = EditorGUI.EndChangeCheck();
        if (changed) Save();
        return changed;
    }

    public static void Nudge(float d)
    {
        Brush.Size = Mathf.Clamp01(Brush.Size + d);
        Save();
    }

    static void Save() => EditorPrefs.SetString(Pref, EditorJsonUtility.ToJson(_brush));

    static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    /// <summary>붓 → 획 보정기 설정</summary>
    public static void Setup(StrokeShaper s, PaintBrush b, float radiusPx, Vector2 dripDir)
    {
        s.Radius = radiusPx;
        s.Stabilize = b.Stabilize;
        s.StabilizeAmount = b.StabilizeAmount;
        s.Spray = b.IsSpray;
        s.Drips = b.Drips;
        s.Snap = b.Snap;
        s.Eraser = b.IsEraser;
        s.Outline = b.Outline;
        s.DripDir = dripDir;
    }
}
#endif
