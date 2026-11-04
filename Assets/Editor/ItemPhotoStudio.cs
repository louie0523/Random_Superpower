#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 아이템 사진관 — Tools → UI → 아이템 사진관 (모션 셋업 '무기 · 손' 탭의 버튼으로도 열림)
///   왼쪽: 아이템 목록 (찍음 · 아직 · 모델 바뀜) · 여러 개 골라서 한꺼번에
///   가운데: 미리보기 — 왼쪽 끌기 = 돌리기, 오른쪽 · 가운데 끌기 = 옮기기, 휠 = 확대
///   오른쪽: 각도(대각선 · 가로 · 세로 · 정면 · 직접) · 화면 돌림 · 확대 · 위치 · 조명 3개 + 주변광
/// 고치는 대상: '이 아이템만' 또는 '종류 기본값' (총 · 근접 · 활 · 갑옷 · 투구 · 신발)
/// 저장: Assets/UI/Game/Resources/ItemShots/{Id}.png (게임은 이 그림을 씀 — ItemThumbnail)
/// 설정: Assets/UI/Game/ItemShots/ItemShotSettings.asset (각도 · 조명을 기억 → '전부 다시 찍기'도 똑같이)
/// </summary>
public class ItemPhotoStudio : EditorWindow
{
    const string SettingsPath = "Assets/UI/Game/ItemShots/ItemShotSettings.asset";

    ItemShotSettings _set;
    ItemShotStage _stage;
    List<EquipmentData> _items = new();
    readonly HashSet<int> _picked = new();
    EquipmentData _cur;
    bool _editKind;                 // true = 종류 기본값을 고침
    bool _snap = true;
    string _search = "";
    int _filter = -1;               // -1 = 전부, 그 외 ShotKind
    bool _onlyTodo;
    Vector2 _listScroll, _ctrlScroll;
    bool _foldLight = true, _foldSave;
    Texture2D _checker;
    readonly Dictionary<int, Texture2D> _thumbs = new();

    [MenuItem("Tools/UI/아이템 사진관")]
    public static void Open() => Open(null);

    /// <summary>이 아이템을 고른 채로 열기 (모션 셋업 버튼 · 장비 에셋 메뉴)</summary>
    public static void Open(EquipmentData d)
    {
        var w = GetWindow<ItemPhotoStudio>("아이템 사진관");
        w.minSize = new Vector2(1040f, 620f);
        w.Reload();
        if (d != null) w.Pick(d);
        w.Focus();
    }

    [MenuItem("CONTEXT/EquipmentData/아이템 사진관에서 열기")]
    static void OpenContext(MenuCommand cmd) => Open(cmd.context as EquipmentData);

    void OnEnable()
    {
        _stage = new ItemShotStage();
        Reload();
        Undo.undoRedoPerformed += Repaint;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
        _stage?.Dispose();
        _stage = null;
        if (_checker != null) DestroyImmediate(_checker);
        _thumbs.Clear();
    }

    // ══════════════════ 설정 · 목록 ══════════════════

    public static ItemShotSettings LoadSettings(bool create)
    {
        var s = AssetDatabase.LoadAssetAtPath<ItemShotSettings>(SettingsPath);
        if (s != null || !create) return s;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
        AssetDatabase.Refresh();
        s = CreateInstance<ItemShotSettings>();
        AssetDatabase.CreateAsset(s, SettingsPath);
        AssetDatabase.SaveAssets();
        return s;
    }

    void Reload()
    {
        _set = LoadSettings(true);
        _items = AssetDatabase.FindAssets("t:EquipmentData")
            .Select(g => AssetDatabase.LoadAssetAtPath<EquipmentData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null)
            .OrderBy(d => (int)ItemShotSettings.KindOf(d)).ThenBy(d => d.Id)
            .ToList();
        _thumbs.Clear();
        if (_cur == null && _items.Count > 0) Pick(_items.FirstOrDefault(d => d.ModelPrefab != null) ?? _items[0]);
    }

    void Pick(EquipmentData d)
    {
        _cur = d;
        _stage?.SetModel(d != null ? d.ModelPrefab : null, true);
        Repaint();
    }

    static string PngPath(EquipmentData d) => $"{ItemShotTool.Folder}/{d.Id}.png";

    public static string ModelHash(GameObject prefab)
    {
        if (prefab == null) return "";
        string path = AssetDatabase.GetAssetPath(prefab);
        return string.IsNullOrEmpty(path) ? "" : AssetDatabase.GetAssetDependencyHash(path).ToString();
    }

    enum State { NoModel, Todo, Changed, Done }

    State StateOf(EquipmentData d)
    {
        if (d.ModelPrefab == null) return State.NoModel;
        if (!File.Exists(PngPath(d))) return State.Todo;
        var e = _set.Find(d.Id, false);
        if (e != null && !string.IsNullOrEmpty(e.ModelHash) && e.ModelHash != ModelHash(d.ModelPrefab)) return State.Changed;
        return State.Done;
    }

    Texture2D Thumb(EquipmentData d)
    {
        if (_thumbs.TryGetValue(d.Id, out var t)) return t;
        t = AssetDatabase.LoadAssetAtPath<Texture2D>(PngPath(d));
        _thumbs[d.Id] = t;
        return t;
    }

    // ══════════════════ 고치는 모습 ══════════════════

    /// <summary>지금 고치는 모습 — '이 아이템만'이면 아이템 것 (없으면 종류 기본값을 복사해서 만듦)</summary>
    ShotLook EditLook(bool forWrite)
    {
        if (_cur == null) return null;
        var kind = ItemShotSettings.KindOf(_cur);
        if (_editKind) return _set.KindLook(kind);
        var e = _set.Find(_cur.Id, forWrite);
        if (e != null && e.OwnLook) return e.Look;
        if (!forWrite) return _set.KindLook(kind);
        e.OwnLook = true;
        e.Look = _set.KindLook(kind).Clone();
        return e.Look;
    }

    void Touch(string what)
    {
        Undo.RecordObject(_set, "사진관: " + what);
        EditorUtility.SetDirty(_set);
    }

    // ══════════════════ 창 ══════════════════

    void OnGUI()
    {
        if (_set == null || _stage == null) { Reload(); if (_stage == null) _stage = new ItemShotStage(); }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(300f))) DrawList();
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) DrawPreview();
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(330f))) DrawControls();
        }
    }

    // ── 왼쪽: 목록 ──

    void DrawList()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("새로고침", GUILayout.Width(64f))) Reload();
        }
        var kinds = new[] { "전부" }.Concat(System.Enum.GetValues(typeof(ShotKind)).Cast<ShotKind>().Select(ItemShotSettings.KindName)).ToArray();
        _filter = EditorGUILayout.Popup(_filter + 1, kinds) - 1;
        _onlyTodo = EditorGUILayout.ToggleLeft("아직 안 찍은 것 · 모델 바뀐 것만", _onlyTodo);

        var shown = _items.Where(d =>
            (_filter < 0 || (int)ItemShotSettings.KindOf(d) == _filter) &&
            (string.IsNullOrEmpty(_search) || d.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
             (d.DisplayName ?? "").Contains(_search) || d.Id.ToString() == _search) &&
            (!_onlyTodo || StateOf(d) is State.Todo or State.Changed)).ToList();

        _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
        foreach (var d in shown)
        {
            var st = StateOf(d);
            var r = EditorGUILayout.GetControlRect(false, 46f);
            if (d == _cur) EditorGUI.DrawRect(r, new Color(0.24f, 0.45f, 0.7f, 0.35f));

            bool on = _picked.Contains(d.Id);
            bool now = EditorGUI.Toggle(new Rect(r.x + 2f, r.y + 14f, 18f, 18f), on);
            if (now != on) { if (now) _picked.Add(d.Id); else _picked.Remove(d.Id); }

            var tr = new Rect(r.x + 24f, r.y + 3f, 40f, 40f);
            EditorGUI.DrawRect(tr, new Color(0.16f, 0.16f, 0.17f));
            var th = Thumb(d);
            if (th != null) GUI.DrawTexture(tr, th, ScaleMode.ScaleToFit, true);

            string label = string.IsNullOrEmpty(d.DisplayName) ? d.name : d.DisplayName;
            GUI.Label(new Rect(r.x + 70f, r.y + 4f, r.width - 74f, 18f), $"{label}", EditorStyles.boldLabel);
            var (txt, col) = st switch
            {
                State.NoModel => ("모델 없음", new Color(0.6f, 0.6f, 0.6f)),
                State.Todo => ("아직 안 찍음", new Color(1f, 0.75f, 0.3f)),
                State.Changed => ("모델 바뀜 — 다시 찍기", new Color(1f, 0.45f, 0.45f)),
                _ => ("찍음", new Color(0.45f, 0.9f, 0.6f)),
            };
            var old = GUI.color;
            GUI.color = col;
            GUI.Label(new Rect(r.x + 70f, r.y + 24f, r.width - 74f, 18f), $"#{d.Id} · {ItemShotSettings.KindName(ItemShotSettings.KindOf(d))} · {txt}", EditorStyles.miniLabel);
            GUI.color = old;

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition) && Event.current.mousePosition.x > r.x + 22f)
            {
                Pick(d);
                Event.current.Use();
            }
        }
        EditorGUILayout.EndScrollView();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("보이는 것 전부 고르기")) foreach (var d in shown) _picked.Add(d.Id);
            if (GUILayout.Button("고르기 풀기")) _picked.Clear();
        }
        using (new EditorGUI.DisabledScope(_picked.Count == 0))
            if (GUILayout.Button($"고른 것 찍기 ({_picked.Count})", GUILayout.Height(24f)))
                ShootMany(_items.Where(d => _picked.Contains(d.Id)));
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("안 찍은 것 · 바뀐 것 전부")) ShootMany(_items.Where(d => StateOf(d) is State.Todo or State.Changed));
            if (GUILayout.Button("전부 다시 찍기") &&
                EditorUtility.DisplayDialog("전부 다시 찍기", $"모델이 있는 아이템 {_items.Count(d => d.ModelPrefab != null)}개를 지금 설정으로 다시 찍을까요?", "찍기", "취소"))
                ShootMany(_items);
        }
    }

    // ── 가운데: 미리보기 ──

    void DrawPreview()
    {
        if (_cur == null) { EditorGUILayout.HelpBox("왼쪽에서 아이템을 골라 줘", MessageType.Info); return; }

        var look = EditLook(false);
        string label = string.IsNullOrEmpty(_cur.DisplayName) ? _cur.name : _cur.DisplayName;
        EditorGUILayout.LabelField($"{label}  (#{_cur.Id})", EditorStyles.largeLabel);

        var area = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        float side = Mathf.Min(area.width, area.height) - 8f;
        var r = new Rect(area.x + (area.width - side) * 0.5f, area.y + 4f, side, side);

        DrawChecker(r);
        if (_cur.ModelPrefab == null) { GUI.Label(r, "모델(ModelPrefab)이 없어", EditorStyles.centeredGreyMiniLabel); return; }
        if (_stage.Prefab != _cur.ModelPrefab) _stage.SetModel(_cur.ModelPrefab, true);
        if (!_stage.HasModel) { GUI.Label(r, "그릴 메시가 없어 (MeshRenderer · SkinnedMeshRenderer)", EditorStyles.centeredGreyMiniLabel); return; }

        if (Event.current.type == EventType.Repaint)
        {
            var tex = _stage.Draw(r, look);
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
        }
        HandleMouse(r, look);

        // 저장된 그림 (오른쪽 아래 작게 — 비교용)
        var saved = Thumb(_cur);
        if (saved != null)
        {
            var sr = new Rect(r.xMax - 108f, r.yMax - 108f, 100f, 100f);
            EditorGUI.DrawRect(sr, new Color(0f, 0f, 0f, 0.5f));
            GUI.DrawTexture(sr, saved, ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(sr.x, sr.y - 16f, sr.width, 16f), "저장된 그림", EditorStyles.miniBoldLabel);
        }
        GUI.Label(new Rect(r.x + 6f, r.y + 4f, r.width, 18f), "왼쪽 끌기 = 돌리기 · 오른쪽 끌기 = 옮기기 · 휠 = 확대", EditorStyles.miniLabel);
    }

    void DrawChecker(Rect r)
    {
        if (_checker == null)
        {
            _checker = new Texture2D(2, 2) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            var a = new Color(0.22f, 0.22f, 0.24f); var b = new Color(0.28f, 0.28f, 0.3f);
            _checker.SetPixels(new[] { a, b, b, a });
            _checker.Apply();
        }
        GUI.DrawTextureWithTexCoords(r, _checker, new Rect(0, 0, r.width / 16f, r.height / 16f));
    }

    void HandleMouse(Rect r, ShotLook view)
    {
        var e = Event.current;
        if (!r.Contains(e.mousePosition) && e.type != EventType.MouseDrag && e.type != EventType.MouseUp) return;

        if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition))
        {
            var k = EditLook(true);
            Touch("확대");
            k.Zoom = Mathf.Clamp(k.Zoom * (e.delta.y > 0 ? 0.93f : 1.075f), 0.4f, 2.5f);
            e.Use(); Repaint();
        }
        else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == 0 && r.Contains(e.mousePosition) || e.type == EventType.MouseDrag && _dragging)
        {
            _dragging = true;
            var k = EditLook(true);
            if (e.button == 0)
            {
                Touch("돌리기");
                var q = _stage.Rotation(k);
                q = Quaternion.AngleAxis(-e.delta.x * 0.6f, Vector3.up) * Quaternion.AngleAxis(-e.delta.y * 0.6f, Vector3.right) * q;
                k.Angle = ShotAngle.Custom;
                k.Spin = 0f;
                k.Euler = q.eulerAngles;
            }
            else
            {
                Touch("옮기기");
                k.Offset += new Vector2(e.delta.x, -e.delta.y) * (2f / r.width);
                k.Offset = new Vector2(Mathf.Clamp(k.Offset.x, -1f, 1f), Mathf.Clamp(k.Offset.y, -1f, 1f));
            }
            e.Use(); Repaint();
        }
        else if (e.type == EventType.MouseUp && _dragging)
        {
            _dragging = false;
            if (_snap)
            {
                var k = EditLook(true);
                if (k.Angle == ShotAngle.Custom) k.Euler = SnapEuler(k.Euler);
            }
            e.Use(); Repaint();
        }
    }
    bool _dragging;

    static Vector3 SnapEuler(Vector3 e) => new(Snap(e.x), Snap(e.y), Snap(e.z));
    static float Snap(float a) => Mathf.Round(a / 15f) * 15f;

    // ── 오른쪽: 조절 ──

    void DrawControls()
    {
        if (_cur == null) return;
        _ctrlScroll = EditorGUILayout.BeginScrollView(_ctrlScroll);
        var kind = ItemShotSettings.KindOf(_cur);
        var entry = _set.Find(_cur.Id, false);
        bool own = entry != null && entry.OwnLook;

        // 고치는 대상
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("고치는 대상", EditorStyles.boldLabel);
            int sel = GUILayout.Toolbar(_editKind ? 1 : 0, new[] { "이 아이템만", $"{ItemShotSettings.KindName(kind)} 기본값" });
            _editKind = sel == 1;
            if (_editKind)
                EditorGUILayout.LabelField($"'{ItemShotSettings.KindName(kind)}'에서 따로 안 맞춘 아이템이 전부 따라감", EditorStyles.wordWrappedMiniLabel);
            else
            {
                EditorGUILayout.LabelField(own ? "이 아이템만 따로 맞춘 모습" : "지금은 종류 기본값 — 고치면 이 아이템만 따로 저장", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!own))
                    if (GUILayout.Button("종류 기본값으로 되돌리기"))
                    {
                        Touch("기본값으로");
                        entry.OwnLook = false;
                    }
            }
        }

        var look = EditLook(false);

        // 각도
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("각도", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                AngleButton("↗ 대각선", ShotAngle.Diagonal, look);
                AngleButton("→ 가로", ShotAngle.Horizontal, look);
                AngleButton("↑ 세로", ShotAngle.Vertical, look);
                AngleButton("정면", ShotAngle.Front, look);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("⟲ 45°")) Edit("화면 돌림", k => k.Spin += 45f);
                if (GUILayout.Button("⟳ 45°")) Edit("화면 돌림", k => k.Spin -= 45f);
                if (GUILayout.Button("좌우 뒤집기")) Edit("뒤집기", k =>
                {
                    var q = Quaternion.AngleAxis(180f, Vector3.up) * _stage.Rotation(k);
                    k.Angle = ShotAngle.Custom; k.Spin = 0f; k.Euler = q.eulerAngles;
                });
            }

            EditorGUI.BeginChangeCheck();
            float spin = EditorGUILayout.Slider("화면 돌림", look.Spin, -180f, 180f);
            Vector3 eul = look.Euler;
            using (new EditorGUI.DisabledScope(look.Angle != ShotAngle.Custom))
                eul = EditorGUILayout.Vector3Field("직접 회전", look.Euler);
            if (EditorGUI.EndChangeCheck()) Edit("각도", k => { k.Spin = spin; k.Euler = eul; });
            _snap = EditorGUILayout.ToggleLeft("끌어서 돌린 뒤 15° 단위로 맞추기", _snap);
        }

        // 확대 · 위치
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("확대 · 위치", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float zoom = EditorGUILayout.Slider("확대", look.Zoom, 0.4f, 2.5f);
            float ox = EditorGUILayout.Slider("가로 위치", look.Offset.x, -1f, 1f);
            float oy = EditorGUILayout.Slider("세로 위치", look.Offset.y, -1f, 1f);
            if (EditorGUI.EndChangeCheck()) Edit("확대 · 위치", k => { k.Zoom = zoom; k.Offset = new Vector2(ox, oy); });
            if (GUILayout.Button("가운데 · 꽉 차게")) Edit("가운데", k => { k.Zoom = 1f; k.Offset = Vector2.zero; });
        }

        // 조명
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _foldLight = EditorGUILayout.Foldout(_foldLight, "조명", true, EditorStyles.foldoutHeader);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("카툰 기본")) Edit("조명", k => k.Light = LightPreset(0));
                if (GUILayout.Button("밝게")) Edit("조명", k => k.Light = LightPreset(1));
                if (GUILayout.Button("진하게")) Edit("조명", k => k.Light = LightPreset(2));
                if (GUILayout.Button("뒤 빛")) Edit("조명", k => k.Light = LightPreset(3));
            }
            if (_foldLight)
            {
                var l = look.Light ?? new ShotLight();
                EditorGUI.BeginChangeCheck();
                var n = l.Clone();
                EditorGUILayout.LabelField("정면", EditorStyles.miniBoldLabel);
                n.KeyYaw = EditorGUILayout.Slider("좌우", n.KeyYaw, -180f, 180f);
                n.KeyPitch = EditorGUILayout.Slider("위아래", n.KeyPitch, -89f, 89f);
                n.KeyIntensity = EditorGUILayout.Slider("세기", n.KeyIntensity, 0f, 3f);
                n.KeyColor = EditorGUILayout.ColorField("색", n.KeyColor);
                EditorGUILayout.LabelField("보조", EditorStyles.miniBoldLabel);
                n.FillYaw = EditorGUILayout.Slider("좌우", n.FillYaw, -180f, 180f);
                n.FillPitch = EditorGUILayout.Slider("위아래", n.FillPitch, -89f, 89f);
                n.FillIntensity = EditorGUILayout.Slider("세기", n.FillIntensity, 0f, 3f);
                n.FillColor = EditorGUILayout.ColorField("색", n.FillColor);
                EditorGUILayout.LabelField("뒤 테두리", EditorStyles.miniBoldLabel);
                n.RimYaw = EditorGUILayout.Slider("좌우", n.RimYaw, -180f, 180f);
                n.RimPitch = EditorGUILayout.Slider("위아래", n.RimPitch, -89f, 89f);
                n.RimIntensity = EditorGUILayout.Slider("세기", n.RimIntensity, 0f, 3f);
                n.RimColor = EditorGUILayout.ColorField("색", n.RimColor);
                n.Ambient = EditorGUILayout.ColorField("주변광", n.Ambient);
                if (EditorGUI.EndChangeCheck()) Edit("조명", k => k.Light = n);
            }
        }

        // 저장 방식
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _foldSave = EditorGUILayout.Foldout(_foldSave, "저장 방식 (모든 아이템 공통)", true, EditorStyles.foldoutHeader);
            if (_foldSave)
            {
                EditorGUI.BeginChangeCheck();
                int size = EditorGUILayout.IntPopup("크기", _set.Size, new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
                int ss = EditorGUILayout.IntSlider("크게 찍고 줄이기", _set.Supersample, 1, 4);
                bool ol = EditorGUILayout.Toggle("카툰 외곽선", _set.Outline);
                float ow = _set.OutlineWidth;
                Color oc = _set.OutlineColor;
                using (new EditorGUI.DisabledScope(!ol))
                {
                    ow = EditorGUILayout.Slider("외곽선 두께 (px)", ow, 1f, 20f);
                    oc = EditorGUILayout.ColorField("외곽선 색", oc);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Touch("저장 방식");
                    _set.Size = size; _set.Supersample = ss; _set.Outline = ol; _set.OutlineWidth = ow; _set.OutlineColor = oc;
                }
                if (GUILayout.Button("설정 에셋 선택")) { Selection.activeObject = _set; EditorGUIUtility.PingObject(_set); }
            }
        }

        EditorGUILayout.Space(6f);
        using (new EditorGUI.DisabledScope(_cur.ModelPrefab == null))
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.85f, 0.25f);
            if (GUILayout.Button("찍어서 저장", GUILayout.Height(40f))) ShootOne(_cur, true);
            GUI.backgroundColor = old;
        }
        if (File.Exists(PngPath(_cur)) && GUILayout.Button("저장된 그림 선택"))
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(PngPath(_cur)));

        EditorGUILayout.EndScrollView();
    }

    void AngleButton(string label, ShotAngle a, ShotLook look)
    {
        bool on = look.Angle == a;
        if (GUILayout.Toggle(on, label, EditorStyles.miniButton) && !on)
            Edit("각도", k => { k.Angle = a; k.Spin = 0f; });
    }

    void Edit(string what, System.Action<ShotLook> change)
    {
        Touch(what);
        var k = EditLook(true);
        change(k);
        Repaint();
    }

    public static ShotLight LightPreset(int i)
    {
        var l = new ShotLight();
        switch (i)
        {
            case 1:     // 밝게
                l.KeyYaw = -25f; l.KeyPitch = 30f; l.KeyIntensity = 1.0f;
                l.FillIntensity = 0.8f; l.RimIntensity = 0.5f; l.Ambient = new Color(0.6f, 0.6f, 0.64f);
                break;
            case 2:     // 진하게
                l.KeyYaw = -65f; l.KeyPitch = 45f; l.KeyIntensity = 1.5f;
                l.FillIntensity = 0.15f; l.RimIntensity = 1.3f; l.Ambient = new Color(0.22f, 0.22f, 0.27f);
                break;
            case 3:     // 뒤 빛
                l.KeyIntensity = 0.9f; l.FillIntensity = 0.3f;
                l.RimYaw = 170f; l.RimPitch = 35f; l.RimIntensity = 1.8f; l.Ambient = new Color(0.35f, 0.35f, 0.4f);
                break;
        }
        return l;
    }

    // ══════════════════ 찍기 ══════════════════

    void ShootOne(EquipmentData d, bool ping)
    {
        var sp = Shoot(d, _set, _stage, null, ping);
        _thumbs.Remove(d.Id);
        _stage.SetModel(_cur != null ? _cur.ModelPrefab : null, true);
        if (sp != null) ShowNotification(new GUIContent("저장함"));
    }

    void ShootMany(IEnumerable<EquipmentData> list)
    {
        var arr = list.Where(d => d != null && d.ModelPrefab != null).ToList();
        int n = 0;
        try
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("아이템 사진관", arr[i].name, i / (float)arr.Count)) break;
                if (Shoot(arr[i], _set, _stage, null, false) != null) n++;
                _thumbs.Remove(arr[i].Id);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        _stage.SetModel(_cur != null ? _cur.ModelPrefab : null, true);
        AssetDatabase.Refresh();
        ShowNotification(new GUIContent($"{n}개 찍음"));
        Debug.Log($"[아이템 사진관] {n}개 찍음 → {ItemShotTool.Folder}");
    }

    /// <summary>
    /// 찍어서 PNG로 저장 (설정의 모습 그대로) — stage가 없으면 잠깐 만들어서 씀.
    /// lookOverride = 이 모습으로 (null이면 설정에서 아이템 · 종류 모습)
    /// </summary>
    public static Sprite Shoot(EquipmentData d, ItemShotSettings set, ItemShotStage stage, ShotLook lookOverride, bool ping)
    {
        if (d == null || d.ModelPrefab == null)
        {
            Debug.LogWarning($"[아이템 사진관] {(d != null ? d.name : "?")} — 모델(ModelPrefab)이 없음");
            return null;
        }
        if (set == null) set = LoadSettings(true);
        bool temp = stage == null;
        if (temp) stage = new ItemShotStage();
        try
        {
            stage.SetModel(d.ModelPrefab, true);
            if (!stage.HasModel) { Debug.LogWarning($"[아이템 사진관] {d.name} — 그릴 메시가 없음"); return null; }
            var tex = stage.Capture(lookOverride ?? set.LookFor(d), set);
            if (tex == null) return null;

            Directory.CreateDirectory(ItemShotTool.Folder);
            string path = PngPath(d);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();

            Undo.RecordObject(set, "사진 찍기");
            var e = set.Find(d.Id, true);
            e.ModelHash = ModelHash(d.ModelPrefab);
            EditorUtility.SetDirty(set);

            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (ping && sp != null) EditorGUIUtility.PingObject(sp);
            return sp;
        }
        finally
        {
            if (temp) stage.Dispose();
        }
    }
}
#endif
