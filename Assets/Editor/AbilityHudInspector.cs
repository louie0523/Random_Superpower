#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// AbilityData 인스펙터 '기본' 탭에 붙는 HUD 표시 칸 — 번호 · 요약 · 색 + 미리보기.
/// AbilityDataEditor.DrawBasicTab() 안에 한 줄:  AbilityHudInspector.Draw(serializedObject, (AbilityData)target);
/// </summary>
public static class AbilityHudInspector
{
    static readonly Color DefAccent = new(0.82f, 0.16f, 0.18f, 1f);
    static readonly Color DefName = Color.white;
    static readonly Color DefNumber = new(1f, 0.84f, 0.22f, 1f);

    static AbilityDatabase _db;
    static GUIStyle _plate, _num, _sum, _hint;

    public static void Draw(SerializedObject so, AbilityData data)
    {
        var number = so.FindProperty("Number");
        var summary = so.FindProperty("Summary");
        var accent = so.FindProperty("UiAccent");
        var nameCol = so.FindProperty("UiNameColor");
        var numCol = so.FindProperty("UiNumberColor");
        if (number == null || summary == null || accent == null || nameCol == null || numCol == null)
        {
            EditorGUILayout.HelpBox("AbilityData에 'HUD 표시' 칸(Number · Summary · UiAccent · UiNameColor · UiNumberColor)이 없습니다", MessageType.Warning);
            return;
        }

        Styles();
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("HUD 표시 (랜덤 능력 HUD · 클래스 카드)", EditorStyles.boldLabel);

        // ── 번호 ──
        var db = FindDb();
        AbilityUi.InvalidateNumbers();          // 에디터에선 진화 연결이 바뀔 수 있으니 매번 다시
        AbilityUi.GetNumber(data, db, out int shownNo, out int stage);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(number, new GUIContent("번호",
            "NO. N에 쓸 번호. 0(비움)이면 ID 뒷자리 2개 (6012 → 12).\n진화한 능력은 처음 능력의 번호 + 단계로 자동 (NO. 12 - 1)"));
        string auto = stage > 0
            ? $"→ NO. {shownNo} - {stage}  (진화 {stage}단계)"
            : number.intValue > 0 ? $"→ NO. {shownNo}" : $"→ NO. {shownNo}  (ID 뒷자리)";
        GUILayout.Label(auto, _hint, GUILayout.Width(190));
        EditorGUILayout.EndHorizontal();
        if (stage > 0 && number.intValue > 0)
            EditorGUILayout.HelpBox("진화한 능력은 처음 능력의 번호를 씀 — 여기 번호는 무시됨", MessageType.None);
        if (db == null)
            EditorGUILayout.HelpBox("AbilityDatabase를 못 찾아서 진화 단계는 계산 못 함", MessageType.None);

        // ── 요약 ──
        EditorGUILayout.LabelField(new GUIContent("한 줄 요약",
            "HUD · 카드에 나오는 짧은 설명. 비우면 플레이버.\n시트에 ability.{Id}.summary가 있으면 그게 우선"));
        EditorGUI.BeginChangeCheck();
        string newSummary = EditorGUILayout.TextArea(summary.stringValue ?? "", EditorStyles.textArea, GUILayout.MinHeight(36));
        if (EditorGUI.EndChangeCheck()) summary.stringValue = newSummary;

        // ── 색 ──
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(accent, new GUIContent("이름표 · 테두리", "이름표 배경 · 카드 테두리 · 아이콘 원"));
        if (GUILayout.Button("기본", EditorStyles.miniButton, GUILayout.Width(40))) accent.colorValue = DefAccent;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(nameCol, new GUIContent("이름 글자"));
        if (GUILayout.Button("기본", EditorStyles.miniButton, GUILayout.Width(40))) nameCol.colorValue = DefName;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(numCol, new GUIContent("번호 글자"));
        if (GUILayout.Button("기본", EditorStyles.miniButton, GUILayout.Width(40))) numCol.colorValue = DefNumber;
        EditorGUILayout.EndHorizontal();

        // ── 미리보기 ──
        EditorGUILayout.Space(4);
        string name = AbilityUi.Name(data);
        string sum = string.IsNullOrWhiteSpace(summary.stringValue) ? AbilityUi.Summary(data) : summary.stringValue;
        string noText = stage > 0 ? $"NO. {shownNo} - {stage}" : $"NO. {shownNo}";
        DrawPreview(data != null ? data.Icon : null, name, noText, sum, accent.colorValue, nameCol.colorValue, numCol.colorValue);
    }

    static void DrawPreview(Sprite icon, string name, string no, string summary, Color accent, Color nameCol, Color numCol)
    {
        var r = GUILayoutUtility.GetRect(10f, 58f, GUILayout.ExpandWidth(true));
        r.width = Mathf.Min(r.width, 520f);

        // 배경 — 왼쪽 진하고 오른쪽으로 옅게
        int steps = 24;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            float a = t < 0.55f ? 0.85f : Mathf.Lerp(0.85f, 0f, (t - 0.55f) / 0.45f);
            EditorGUI.DrawRect(new Rect(r.x + r.width * t, r.y, r.width / steps + 1f, r.height), new Color(0.05f, 0.06f, 0.09f, a));
        }

        // 아이콘
        var ir = new Rect(r.x + 6f, r.y + 7f, 44f, 44f);
        if (icon != null && icon.texture != null)
        {
            var tr = icon.textureRect;
            var tex = icon.texture;
            GUI.DrawTextureWithTexCoords(ir, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
        }
        else EditorGUI.DrawRect(ir, new Color(1f, 1f, 1f, 0.12f));

        // 이름표
        _plate.normal.textColor = nameCol;
        float w = Mathf.Clamp(_plate.CalcSize(new GUIContent(name)).x + 22f, 56f, 220f);
        var pr = new Rect(ir.xMax + 8f, r.y + 7f, w, 44f);
        EditorGUI.DrawRect(pr, accent);
        GUI.Label(pr, name, _plate);

        // 번호 · 요약
        float tx = pr.xMax + 10f;
        _num.normal.textColor = numCol;
        GUI.Label(new Rect(tx, r.y + 6f, r.xMax - tx, 20f), no, _num);
        GUI.Label(new Rect(tx, r.y + 26f, r.xMax - tx, 26f), summary, _sum);
    }

    static AbilityDatabase FindDb()
    {
        if (_db != null) return _db;
        var guids = AssetDatabase.FindAssets("t:AbilityDatabase");
        if (guids.Length > 0) _db = AssetDatabase.LoadAssetAtPath<AbilityDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
        return _db;
    }

    static void Styles()
    {
        if (_plate != null) return;
        _plate = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
        _num = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
        _sum = new GUIStyle(EditorStyles.label) { fontSize = 13, clipping = TextClipping.Clip };
        _sum.normal.textColor = Color.white;
        _hint = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
    }
}
#endif
