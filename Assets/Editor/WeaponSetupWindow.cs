using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class WeaponSetupWindow : EditorWindow
{
    // ── 대상 ──
    private EquipmentData _weapon;
    private FPArmRig _rig;

    // ── 상태 ──
    private bool _firstPerson = true;
    private bool _autoApply = true;
    private int _comboIndex;
    private int _selectedKey = -1;
    private SwingPresets.Kind _presetKind = SwingPresets.Kind.DiagonalSlash;

    // ── 재생 ──
    private bool _playing;
    private float _playT;
    private float _playSpeed = 1f;
    private bool _loop;
    private double _lastTime;

    // ── UI ──
    private Vector2 _scroll;
    private bool _foldTransform = true;
    private bool _foldMotion = true;
    private bool _foldTools;

    // ── 씬 표시 ──
    private bool _showHitbox = true;
    private bool _showTrail = true;
    private bool _showOnlyDuringActive = false;
    private readonly List<Vector3> _tipTrail = new();

    private int _dragHandle = -1;

    [MenuItem("Tools/Weapon Setup")]
    static void Open()
    {
        var w = GetWindow<WeaponSetupWindow>("Weapon Setup");
        w.minSize = new Vector2(360, 560);
    }

    void OnEnable()
    {
        FindRig();
        EditorApplication.update += OnEditorUpdate;
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndo;
        _lastTime = EditorApplication.timeSinceStartup;
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndo;
        if (_rig != null) _rig.Mount(null);
    }

    void OnUndo()
    {
        RefreshPreview();
        Repaint();
    }

    void FindRig()
    {
        if (_rig == null) _rig = FindFirstObjectByType<FPArmRig>();
    }

    // ══════════════════ GUI ══════════════════

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawTarget();

        if (_weapon != null && _rig != null && _weapon.Slot == EquipSlot.Weapon)
        {
            EditorGUILayout.Space(6);
            _foldTransform = EditorGUILayout.Foldout(_foldTransform, "무기 배치", true, EditorStyles.foldoutHeader);
            if (_foldTransform) DrawTransform();

            EditorGUILayout.Space(6);
            _foldMotion = EditorGUILayout.Foldout(_foldMotion, "모션 편집", true, EditorStyles.foldoutHeader);
            if (_foldMotion) DrawMotion();

            EditorGUILayout.Space(6);
            _foldTools = EditorGUILayout.Foldout(_foldTools, "도구", true, EditorStyles.foldoutHeader);
            if (_foldTools) DrawTools();
        }

        EditorGUILayout.EndScrollView();
    }

    // ─────────── 대상 선택 ───────────

    void DrawTarget()
    {
        EditorGUILayout.LabelField("대상", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _weapon = (EquipmentData)EditorGUILayout.ObjectField(
            "무기", _weapon, typeof(EquipmentData), false);
        if (EditorGUI.EndChangeCheck()) SpawnWeapon();

        _rig = (FPArmRig)EditorGUILayout.ObjectField(
            "팔 리그", _rig, typeof(FPArmRig), true);

        if (_rig == null)
        {
            EditorGUILayout.HelpBox(
                "씬에 FPArmRig가 없습니다.\n\n" +
                "PreviewCamera\n" +
                " └ ArmRoot  (FPArmRig)\n" +
                "    ├ RightHand\n" +
                "    └ LeftHand",
                MessageType.Warning);

            if (GUILayout.Button("씬에서 찾기")) FindRig();
            return;
        }

        if (_weapon == null)
        {
            EditorGUILayout.HelpBox("무기 EquipmentData를 선택하세요.", MessageType.Info);
            return;
        }

        if (_weapon.Slot != EquipSlot.Weapon)
        {
            EditorGUILayout.HelpBox($"{_weapon.DisplayName}은(는) 무기가 아닙니다. (슬롯: {_weapon.Slot})", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("다시 로드")) SpawnWeapon();
        if (GUILayout.Button("치우기")) ClearPreview();
        if (GUILayout.Button("손 정리")) { _rig.CleanHands(); SceneView.RepaintAll(); }
        EditorGUILayout.EndHorizontal();
    }

    // ─────────── 무기 배치 ───────────

    void DrawTransform()
    {
        EditorGUI.BeginChangeCheck();
        _firstPerson = EditorGUILayout.Toggle("1인칭 오프셋 편집", _firstPerson);
        if (EditorGUI.EndChangeCheck()) RefreshPreview();

        var so = new SerializedObject(_weapon);
        so.Update();

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(so.FindProperty("Grip"));

        string posField = _firstPerson ? "FpPositionOffset" : "PositionOffset";
        string rotField = _firstPerson ? "FpRotationOffset" : "RotationOffset";

        EditorGUILayout.PropertyField(so.FindProperty(posField), new GUIContent("위치"));
        EditorGUILayout.PropertyField(so.FindProperty(rotField), new GUIContent("회전"));
        EditorGUILayout.PropertyField(so.FindProperty("ScaleOverride"), new GUIContent("크기"));

        bool changed = EditorGUI.EndChangeCheck();
        so.ApplyModifiedProperties();

        EditorGUILayout.Space(2);
        _autoApply = EditorGUILayout.Toggle("자동 적용", _autoApply);

        if (changed && _autoApply) SpawnWeapon();

        using (new EditorGUI.DisabledScope(_autoApply))
        {
            if (GUILayout.Button("적용", GUILayout.Height(24))) SpawnWeapon();
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("씬 위치를 저장")) CaptureWeaponTransform();
        if (GUILayout.Button("초기화")) ResetWeaponTransform();
        EditorGUILayout.EndHorizontal();

        // ══════ 히트 판정 ══════

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("히트 판정", EditorStyles.boldLabel);

        var so2 = new SerializedObject(_weapon);
        so2.Update();

        EditorGUILayout.PropertyField(so2.FindProperty("HitMode"), new GUIContent("판정 방식"));
        EditorGUILayout.PropertyField(so2.FindProperty("Range"), new GUIContent("사거리 (m)"));

        if (_weapon.HitMode == WeaponHitMode.ForwardArc)
            EditorGUILayout.PropertyField(so2.FindProperty("HitAngle"), new GUIContent("판정 각도 (°)"));

        EditorGUILayout.PropertyField(so2.FindProperty("MinRange"), new GUIContent("최소 사거리"));
        EditorGUILayout.PropertyField(so2.FindProperty("HitHeight"), new GUIContent("수직 허용 (m)"));

        so2.ApplyModifiedProperties();

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("프리셋", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("단검", EditorStyles.miniButtonLeft)) SetHitPreset(2.0f, 70f);
        if (GUILayout.Button("검", EditorStyles.miniButtonMid)) SetHitPreset(3.0f, 110f);
        if (GUILayout.Button("대검", EditorStyles.miniButtonMid)) SetHitPreset(3.5f, 160f);
        if (GUILayout.Button("망치", EditorStyles.miniButtonMid)) SetHitPreset(2.5f, 90f);
        if (GUILayout.Button("창", EditorStyles.miniButtonRight)) SetHitPreset(4.5f, 45f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "판정은 카메라 정면 부채꼴입니다. 무기 모션과 무관하게 동작하니 " +
            "모션은 보기 좋게만 만드시면 됩니다.",
            MessageType.None);
    }

    void SetHitPreset(float range, float angle)
    {
        Undo.RecordObject(_weapon, "Hit Preset");
        _weapon.Range = range;
        _weapon.HitAngle = angle;
        EditorUtility.SetDirty(_weapon);
        SceneView.RepaintAll();
    }

    // ─────────── 모션 편집 ───────────

    void DrawMotion()
    {
        int len = _weapon.ComboLength;
        if (len == 0)
        {
            EditorGUILayout.HelpBox("콤보 배열이 비어 있습니다.", MessageType.Warning);
            if (GUILayout.Button("1타 추가"))
            {
                Undo.RecordObject(_weapon, "Add Combo");
                _weapon.Combo = new[] { new ComboStep { Name = "1타" } };
                EditorUtility.SetDirty(_weapon);
            }
            return;
        }

        // 콤보 탭
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < len; i++)
        {
            var s = _weapon.GetStep(i);
            bool on = _comboIndex == i;
            var style = len == 1 ? EditorStyles.miniButton
                : i == 0 ? EditorStyles.miniButtonLeft
                : i == len - 1 ? EditorStyles.miniButtonRight
                : EditorStyles.miniButtonMid;

            if (GUILayout.Toggle(on, s?.Name ?? $"{i + 1}타", style) && !on)
            {
                _comboIndex = i;
                _selectedKey = -1;
                _playT = 0f;
                _playing = false;
                RefreshPreview();
            }
        }
        EditorGUILayout.EndHorizontal();

        var step = _weapon.GetStep(_comboIndex);
        if (step == null) return;

        EditorGUILayout.Space(4);

        // 프리셋
        EditorGUILayout.BeginHorizontal();
        _presetKind = (SwingPresets.Kind)EditorGUILayout.EnumPopup("모션 프리셋", _presetKind);

        if (GUILayout.Button("적용", GUILayout.Width(52)))
        {
            Undo.RecordObject(_weapon, "Apply Swing Preset");
            step.Motion = SwingPresets.Create(_presetKind, _rig.IdlePose,
                                              _weapon.Grip == GripType.TwoHand);
            EditorUtility.SetDirty(_weapon);
            _selectedKey = -1;
            _playT = 0f;
            RefreshPreview();
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("현재 씬 포즈를 기준으로 프리셋 적용"))
        {
            Undo.RecordObject(_weapon, "Apply Preset From Scene");
            step.Motion = SwingPresets.Create(_presetKind, CaptureCurrentPose(),
                                              _weapon.Grip == GripType.TwoHand);
            EditorUtility.SetDirty(_weapon);
            _selectedKey = -1;
            _playT = 0f;
            RefreshPreview();
        }

        EditorGUILayout.Space(4);

        // 콤보 추가/삭제
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ 타 추가", EditorStyles.miniButton))
        {
            Undo.RecordObject(_weapon, "Add Combo Step");

            var list = new List<ComboStep>(_weapon.Combo ?? new ComboStep[0]);
            var src = list.Count > 0 ? list[list.Count - 1] : null;

            var next = new ComboStep
            {
                Name = $"{list.Count + 1}타",

                Duration = src?.Duration ?? 0.55f,
                HitStart = src?.HitStart ?? 0.27f,
                HitEnd = src?.HitEnd ?? 0.45f,
                CancelPoint = src?.CancelPoint ?? 0.75f,

                ComboResetTime = src?.ComboResetTime ?? 0.8f,
                DamageMultiplier = (src?.DamageMultiplier ?? 1f) + 0.15f,
                RangeMultiplier = src?.RangeMultiplier ?? 1f,
                RadiusMultiplier = src?.RadiusMultiplier ?? 1f,
                MaxTargets = src?.MaxTargets ?? 1,
                HitStopDuration = src?.HitStopDuration ?? 0.05f,
                CameraKickFov = src?.CameraKickFov ?? 4f,
                CameraShake = src?.CameraShake ?? 0.3f,
                LungeDistance = src?.LungeDistance ?? 0f,
            };

            var kind = (list.Count % 2 == 0)
                ? SwingPresets.Kind.DiagonalSlash
                : SwingPresets.Kind.BackhandReturn;
            next.Motion = SwingPresets.Create(kind, _rig.IdlePose,
                                              _weapon.Grip == GripType.TwoHand);

            list.Add(next);
            _weapon.Combo = list.ToArray();
            EditorUtility.SetDirty(_weapon);

            _comboIndex = list.Count - 1;
            _selectedKey = -1;
            _playT = 0f;
            RefreshPreview();
        }

        using (new EditorGUI.DisabledScope(len <= 1))
        {
            if (GUILayout.Button("− 현재 타 삭제", EditorStyles.miniButton))
            {
                if (EditorUtility.DisplayDialog("확인",
                    $"{_weapon.GetStep(_comboIndex)?.Name}를 삭제할까요?", "삭제", "취소"))
                {
                    Undo.RecordObject(_weapon, "Delete Combo Step");

                    var list = new List<ComboStep>(_weapon.Combo);
                    list.RemoveAt(_comboIndex);
                    _weapon.Combo = list.ToArray();
                    EditorUtility.SetDirty(_weapon);

                    _comboIndex = Mathf.Clamp(_comboIndex, 0, Mathf.Max(0, list.Count - 1));
                    _selectedKey = -1;
                    RefreshPreview();
                }
            }
        }

        if (GUILayout.Button("복제", EditorStyles.miniButton, GUILayout.Width(46)))
        {
            Undo.RecordObject(_weapon, "Duplicate Combo Step");

            var list = new List<ComboStep>(_weapon.Combo);
            var copy = JsonUtility.FromJson<ComboStep>(JsonUtility.ToJson(list[_comboIndex]));
            copy.Name = $"{list.Count + 1}타";

            list.Insert(_comboIndex + 1, copy);
            _weapon.Combo = list.ToArray();
            EditorUtility.SetDirty(_weapon);

            _comboIndex++;
            RefreshPreview();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawStepTiming(step);

        EditorGUILayout.Space(4);
        DrawTimelineBar(step);

        EditorGUILayout.Space(6);
        DrawPlayback(step);

        EditorGUILayout.Space(6);
        DrawKeyList(step);
    }

    void DrawStepTiming(ComboStep step)
    {
        var so = new SerializedObject(_weapon);
        so.Update();

        var arr = so.FindProperty("Combo");
        if (arr == null || _comboIndex >= arr.arraySize) return;
        var sp = arr.GetArrayElementAtIndex(_comboIndex);

        EditorGUILayout.PropertyField(sp.FindPropertyRelative("Name"), new GUIContent("이름"));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("타이밍", EditorStyles.miniBoldLabel);

        EditorGUILayout.PropertyField(sp.FindPropertyRelative("Duration"),
            new GUIContent("전체 길이 (초)"));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("HitStart"), new GUIContent("판정 시작"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("HitEnd"), new GUIContent("판정 끝"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.PropertyField(sp.FindPropertyRelative("CancelPoint"), new GUIContent("캔슬 지점"));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("피해", EditorStyles.miniBoldLabel);

        EditorGUILayout.Space(2);

        var ovr = sp.FindPropertyRelative("OverrideHitShape");
        EditorGUILayout.PropertyField(ovr, new GUIContent("판정 범위 개별 설정"));

        if (ovr.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(sp.FindPropertyRelative("HitMode"), new GUIContent("방식"));
            EditorGUILayout.PropertyField(sp.FindPropertyRelative("Range"), new GUIContent("사거리"));

            if (step.HitMode == WeaponHitMode.ForwardArc)
                EditorGUILayout.PropertyField(sp.FindPropertyRelative("HitAngle"), new GUIContent("각도"));

            EditorGUILayout.PropertyField(sp.FindPropertyRelative("HitHeight"), new GUIContent("수직 허용"));
            EditorGUILayout.PropertyField(sp.FindPropertyRelative("MinRange"), new GUIContent("최소 사거리"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("무기 값 복사", EditorStyles.miniButton))
            {
                Undo.RecordObject(_weapon, "Copy Hit Shape");
                step.HitMode = _weapon.HitMode;
                step.Range = _weapon.Range;
                step.HitAngle = _weapon.HitAngle;
                step.HitHeight = _weapon.HitHeight;
                step.MinRange = _weapon.MinRange;
                EditorUtility.SetDirty(_weapon);
            }
            if (GUILayout.Button("넓게", EditorStyles.miniButton))
            {
                Undo.RecordObject(_weapon, "Wide Preset");
                step.Range = _weapon.Range * 1.2f;
                step.HitAngle = Mathf.Min(360f, _weapon.HitAngle * 1.5f);
                EditorUtility.SetDirty(_weapon);
            }
            if (GUILayout.Button("찌르기", EditorStyles.miniButton))
            {
                Undo.RecordObject(_weapon, "Thrust Preset");
                step.Range = _weapon.Range * 1.4f;
                step.HitAngle = 40f;
                EditorUtility.SetDirty(_weapon);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }
        else
        {
            EditorGUILayout.LabelField(
                $"무기 값 사용  ({_weapon.Range * step.RangeMultiplier:F2}m / {_weapon.HitAngle:F0}°)",
                EditorStyles.miniLabel);
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("DamageMultiplier"), new GUIContent("피해×"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("RangeMultiplier"), new GUIContent("사거리×"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("MaxTargets"), new GUIContent("최대 대상"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("LungeDistance"), new GUIContent("전진 (m)"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("연출", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("HitStopDuration"), new GUIContent("히트스톱"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("CameraKickFov"), new GUIContent("FOV킥"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("CameraShake"), new GUIContent("셰이크"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("ComboResetTime"), new GUIContent("콤보 유지"));
        EditorGUILayout.EndHorizontal();

        var snd = sp.FindPropertyRelative("SwingSound");
        if (snd != null) EditorGUILayout.PropertyField(snd, new GUIContent("스윙 사운드"));

        so.ApplyModifiedProperties();

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(
            $"선딜 {step.WindUp:F2}s  ·  판정 {step.ActiveWindow:F2}s  ·  후딜 {step.Recovery:F2}s",
            EditorStyles.miniLabel);

        if (step.ActiveWindow < 0.05f)
        {
            EditorGUILayout.HelpBox(
                $"판정 구간이 {step.ActiveWindow:F3}초로 너무 짧습니다. " +
                "네트워크 틱(약 0.017초)을 건너뛸 수 있으니 0.08초 이상으로 늘리세요.",
                MessageType.Warning);
        }
        EditorGUILayout.LabelField(
            $"캔슬 {step.CancelTime:F2}s 부터  ·  DPS 참고 {(step.DamageMultiplier / Mathf.Max(0.01f, step.Duration)):F2}×/s",
            EditorStyles.miniLabel);
    }

    void DrawTimelineBar(ComboStep step)
    {
        Rect r = GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0.14f, 0.14f, 0.14f));

        float xs = r.x + r.width * step.HitStart;
        float xe = r.x + r.width * step.HitEnd;
        float xc = r.x + r.width * step.CancelPoint;

        EditorGUI.DrawRect(new Rect(r.x, r.y, xs - r.x, r.height), new Color(0.26f, 0.31f, 0.45f));
        EditorGUI.DrawRect(new Rect(xs, r.y, xe - xs, r.height), new Color(0.80f, 0.26f, 0.18f));
        EditorGUI.DrawRect(new Rect(xe, r.y, r.x + r.width - xe, r.height), new Color(0.22f, 0.22f, 0.24f));

        DrawHandle(xs, r, new Color(1f, 0.9f, 0.3f));
        DrawHandle(xe, r, new Color(1f, 0.9f, 0.3f));
        DrawHandle(xc, r, new Color(0.4f, 0.95f, 0.5f));

        if (step.Motion?.Keys != null)
        {
            foreach (var k in step.Motion.Keys)
            {
                float kx = r.x + r.width * Mathf.Clamp01(k.Time);
                EditorGUI.DrawRect(new Rect(kx - 1, r.y + r.height - 6, 2, 6),
                    new Color(1f, 1f, 1f, 0.7f));
            }
        }

        float p = Mathf.Clamp01(_playT / Mathf.Max(0.01f, step.Duration));
        EditorGUI.DrawRect(new Rect(r.x + r.width * p - 1, r.y, 2, r.height), Color.cyan);

        HandleTimelineInput(r, step);

        GUI.Label(new Rect(r.x + 4, r.y + 2, r.width, 14),
            $"판정 {step.WindUp:F2}s ~ {(step.WindUp + step.ActiveWindow):F2}s / 전체 {step.Duration:F2}s",
            EditorStyles.miniLabel);
    }

    void DrawHandle(float x, Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(x - 2, r.y, 4, r.height), c);
        EditorGUI.DrawRect(new Rect(x - 6, r.y + r.height - 12, 12, 12),
            new Color(c.r, c.g, c.b, 0.85f));
        EditorGUIUtility.AddCursorRect(new Rect(x - 6, r.y, 12, r.height),
            MouseCursor.ResizeHorizontal);
    }

    void HandleTimelineInput(Rect r, ComboStep step)
    {
        Event e = Event.current;
        float t = Mathf.Clamp01((e.mousePosition.x - r.x) / r.width);

        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
        {
            float dS = Mathf.Abs(t - step.HitStart);
            float dE = Mathf.Abs(t - step.HitEnd);
            float dC = Mathf.Abs(t - step.CancelPoint);
            float min = Mathf.Min(dS, Mathf.Min(dE, dC));

            if (min < 0.03f)
            {
                _dragHandle = min == dS ? 0 : (min == dE ? 1 : 2);
                Undo.RecordObject(_weapon, "Edit Timing");
            }
            else
            {
                _playing = false;
                _playT = t * step.Duration;
                RefreshPreview();
            }
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && _dragHandle >= 0)
        {
            switch (_dragHandle)
            {
                case 0: step.HitStart = Mathf.Min(t, step.HitEnd - 0.02f); break;
                case 1: step.HitEnd = Mathf.Max(t, step.HitStart + 0.02f); break;
                case 2: step.CancelPoint = t; break;
            }
            step.HitStart = Mathf.Clamp01(step.HitStart);
            step.HitEnd = Mathf.Clamp01(step.HitEnd);

            EditorUtility.SetDirty(_weapon);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && _dragHandle >= 0)
        {
            _dragHandle = -1;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && _dragHandle < 0 && r.Contains(e.mousePosition))
        {
            _playing = false;
            _playT = t * step.Duration;
            RefreshPreview();
            e.Use();
            Repaint();
        }
    }

    void DrawPlayback(ComboStep step)
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(_playing ? "■ 정지" : "▶ 재생", GUILayout.Height(26)))
        {
            _playing = !_playing;
            if (_playing) { _playT = 0f; _tipTrail.Clear(); }
            else RefreshPreview();
        }

        if (GUILayout.Button("▶▶ 전체", GUILayout.Height(26)))
        {
            _playing = true;
            _comboIndex = 0;
            _playT = 0f;
            _tipTrail.Clear();
        }

        _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.miniButton,
            GUILayout.Width(46), GUILayout.Height(26));

        EditorGUILayout.EndHorizontal();

        _playSpeed = EditorGUILayout.Slider("재생 속도", _playSpeed, 0.05f, 2f);

        EditorGUI.BeginChangeCheck();
        float p = EditorGUILayout.Slider("진행도",
            _playT / Mathf.Max(0.01f, step.TotalTime), 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            _playing = false;
            _playT = p * step.TotalTime;
            RefreshPreview();
        }
    }

    void DrawKeyList(ComboStep step)
    {
        var motion = step.Motion;
        if (motion == null)
        {
            step.Motion = motion = new SwingMotion();
            EditorUtility.SetDirty(_weapon);
        }
        motion.Keys ??= new List<PoseKey>();

        EditorGUILayout.LabelField($"키프레임 ({motion.Keys.Count})", EditorStyles.boldLabel);

        for (int i = 0; i < motion.Keys.Count; i++)
        {
            var k = motion.Keys[i];
            bool sel = _selectedKey == i;

            EditorGUILayout.BeginHorizontal();

            bool newSel = GUILayout.Toggle(sel, $"{i}", EditorStyles.miniButtonLeft, GUILayout.Width(26));
            if (newSel != sel)
            {
                _selectedKey = newSel ? i : -1;
                if (_selectedKey >= 0) PreviewPose(k.Pose);
            }

            EditorGUI.BeginChangeCheck();
            k.Time = EditorGUILayout.Slider(k.Time, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_weapon);
                motion.SortKeys();
            }

            if (GUILayout.Button("보기", EditorStyles.miniButtonMid, GUILayout.Width(34)))
                PreviewPose(k.Pose);

            if (GUILayout.Button("−", EditorStyles.miniButtonRight, GUILayout.Width(22)))
            {
                Undo.RecordObject(_weapon, "Delete Pose Key");
                motion.Keys.RemoveAt(i);
                EditorUtility.SetDirty(_weapon);
                _selectedKey = -1;
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();

            if (_selectedKey == i) DrawKeyDetail(k);
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ 현재 포즈로 키 추가"))
        {
            Undo.RecordObject(_weapon, "Add Pose Key");
            float t = Mathf.Clamp01(_playT / Mathf.Max(0.01f, step.TotalTime));
            motion.Keys.Add(new PoseKey { Time = t, Pose = CaptureCurrentPose() });
            motion.SortKeys();
            EditorUtility.SetDirty(_weapon);
            _selectedKey = motion.Keys.FindIndex(x => Mathf.Approximately(x.Time, t));
        }

        if (GUILayout.Button("정렬", GUILayout.Width(50)))
        {
            motion.SortKeys();
            EditorUtility.SetDirty(_weapon);
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawKeyDetail(PoseKey k)
    {
        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();

        var p = k.Pose;

        EditorGUILayout.LabelField("오른손", EditorStyles.miniBoldLabel);
        p.RightPos = EditorGUILayout.Vector3Field("위치", p.RightPos);
        p.RightRot = EditorGUILayout.Vector3Field("회전", p.RightRot);

        p.UseLeftHand = EditorGUILayout.Toggle("왼손 표시", p.UseLeftHand);
        if (p.UseLeftHand)
        {
            p.LeftPos = EditorGUILayout.Vector3Field("왼손 위치", p.LeftPos);
            p.LeftRot = EditorGUILayout.Vector3Field("왼손 회전", p.LeftRot);
        }

        EditorGUILayout.LabelField("무기 추가 오프셋", EditorStyles.miniBoldLabel);
        p.WeaponPos = EditorGUILayout.Vector3Field("위치", p.WeaponPos);
        p.WeaponRot = EditorGUILayout.Vector3Field("회전", p.WeaponRot);

        k.Ease = EditorGUILayout.CurveField("보간 곡선", k.Ease);

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_weapon);
            PreviewPose(p);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("씬 포즈 저장"))
        {
            Undo.RecordObject(_weapon, "Capture Pose");
            k.Pose = CaptureCurrentPose();
            EditorUtility.SetDirty(_weapon);
            AssetDatabase.SaveAssets();
        }
        if (GUILayout.Button("Idle 복사"))
        {
            Undo.RecordObject(_weapon, "Copy Idle Pose");
            k.Pose = _rig != null ? _rig.IdlePose.Clone() : new ArmPose();
            EditorUtility.SetDirty(_weapon);
            PreviewPose(k.Pose);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    // ─────────── 도구 ───────────

    void DrawTools()
    {
        if (GUILayout.Button("씬 뷰를 무기에 맞추기")) FrameWeapon();
        if (GUILayout.Button("1인칭 시점으로 보기")) LookThroughEye();

        EditorGUILayout.Space(2);

        if (GUILayout.Button("EquipmentData 선택"))
            Selection.activeObject = _weapon;

        if (_rig != null && GUILayout.Button("FPArmRig 선택"))
            Selection.activeGameObject = _rig.gameObject;

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Idle 포즈를 씬에서 저장") && _rig != null)
        {
            Undo.RecordObject(_rig, "Capture Idle Pose");
            _rig.IdlePose = CaptureCurrentPose();
            EditorUtility.SetDirty(_rig);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("씬 표시", EditorStyles.miniBoldLabel);
        _showHitbox = EditorGUILayout.Toggle("판정 범위", _showHitbox);
        _showTrail = EditorGUILayout.Toggle("무기 궤적", _showTrail);
        _showOnlyDuringActive = EditorGUILayout.Toggle("판정 중에만", _showOnlyDuringActive);

        if (GUILayout.Button("궤적 지우기")) _tipTrail.Clear();
    }

    // ══════════════════ 동작 ══════════════════

    void SpawnWeapon()
    {
        FindRig();
        if (_rig == null) return;

        if (_weapon == null || _weapon.Slot != EquipSlot.Weapon)
        {
            _rig.Mount(null);
            SceneView.RepaintAll();
            return;
        }

        if (_weapon.ModelPrefab == null)
            Debug.LogWarning($"[WeaponSetup] {_weapon.name}: ModelPrefab이 비어 있습니다.");

        _rig.Mount(_weapon);
        MarkPreviewObjects();

        _playing = false;
        _playT = 0f;
        _selectedKey = -1;

        _rig.ApplyPose(_rig.IdlePose);
        SceneView.RepaintAll();
    }

    void MarkPreviewObjects()
    {
        if (_rig == null || _weapon == null) return;

        Transform hand = _weapon.Grip == GripType.LeftHand ? _rig.LeftHand : _rig.RightHand;
        if (hand == null) return;

        for (int i = 0; i < hand.childCount; i++)
            hand.GetChild(i).gameObject.hideFlags = HideFlags.DontSave;
    }

    void ClearPreview()
    {
        if (_rig == null) return;
        _rig.Mount(null);
        SceneView.RepaintAll();
    }

    void RefreshPreview()
    {
        if (_rig == null || _weapon == null) return;

        var step = _weapon.GetStep(_comboIndex);
        if (step?.Motion != null && step.Motion.IsValid)
            _rig.ApplySwing(step.Motion, _playT / Mathf.Max(0.01f, step.TotalTime));
        else
            _rig.ApplyPose(_rig.IdlePose);

        SceneView.RepaintAll();
    }

    void PreviewPose(ArmPose pose)
    {
        if (_rig == null) return;
        _rig.ApplyPose(pose);
        SceneView.RepaintAll();
    }

    ArmPose CaptureCurrentPose()
    {
        var p = new ArmPose();
        if (_rig == null) return p;

        if (_rig.RightHand != null)
        {
            p.RightPos = _rig.RightHand.localPosition;
            p.RightRot = _rig.RightHand.localRotation.eulerAngles;
        }
        if (_rig.LeftHand != null)
        {
            p.UseLeftHand = _rig.LeftHand.gameObject.activeSelf;
            p.LeftPos = _rig.LeftHand.localPosition;
            p.LeftRot = _rig.LeftHand.localRotation.eulerAngles;
        }
        return p;
    }

    void CaptureWeaponTransform()
    {
        if (_rig == null || _weapon == null) return;

        Transform hand = _weapon.Grip == GripType.LeftHand ? _rig.LeftHand : _rig.RightHand;
        if (hand == null || hand.childCount == 0)
        {
            Debug.LogWarning("[WeaponSetup] 손에 무기가 없습니다.");
            return;
        }

        Transform t = hand.GetChild(0);

        Undo.RecordObject(_weapon, "Capture Weapon Transform");

        if (_firstPerson)
        {
            _weapon.FpPositionOffset = t.localPosition;
            _weapon.FpRotationOffset = t.localRotation.eulerAngles;
        }
        else
        {
            _weapon.PositionOffset = t.localPosition;
            _weapon.RotationOffset = t.localRotation.eulerAngles;
        }
        _weapon.ScaleOverride = t.localScale;

        EditorUtility.SetDirty(_weapon);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WeaponSetup] {_weapon.DisplayName} 배치 저장됨");
    }

    void ResetWeaponTransform()
    {
        Undo.RecordObject(_weapon, "Reset Weapon Transform");

        if (_firstPerson)
        {
            _weapon.FpPositionOffset = Vector3.zero;
            _weapon.FpRotationOffset = Vector3.zero;
        }
        else
        {
            _weapon.PositionOffset = Vector3.zero;
            _weapon.RotationOffset = Vector3.zero;
        }
        _weapon.ScaleOverride = Vector3.one;

        EditorUtility.SetDirty(_weapon);
        SpawnWeapon();
    }

    void FrameWeapon()
    {
        if (_rig == null || _weapon == null) return;

        Transform hand = _weapon.Grip == GripType.LeftHand ? _rig.LeftHand : _rig.RightHand;
        if (hand == null) return;

        Selection.activeGameObject = hand.childCount > 0
            ? hand.GetChild(0).gameObject : hand.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    void LookThroughEye()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null || _rig == null) return;

        Transform arm = _rig.ArmRoot != null ? _rig.ArmRoot : _rig.transform;
        Transform eye = arm.parent != null ? arm.parent : arm;

        sv.pivot = eye.position + eye.forward * 0.6f;
        sv.rotation = eye.rotation;
        sv.size = 0.6f;
        sv.Repaint();
    }

    // ══════════════════ 재생 루프 ══════════════════

    void OnEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTime);
        _lastTime = now;

        if (!_playing || _weapon == null || _rig == null) return;

        var step = _weapon.GetStep(_comboIndex);
        if (step == null) { _playing = false; return; }

        _playT += dt * _playSpeed;

        if (_playT >= step.TotalTime)
        {
            _playT = 0f;

            if (_weapon.ComboLength > 1)
            {
                _comboIndex++;
                if (_comboIndex >= _weapon.ComboLength)
                {
                    _comboIndex = 0;
                    if (!_loop)
                    {
                        _playing = false;
                        _rig.ApplyPose(_rig.IdlePose);
                        SceneView.RepaintAll();
                        Repaint();
                        return;
                    }
                }
                step = _weapon.GetStep(_comboIndex);
            }
            else if (!_loop)
            {
                _playing = false;
                _rig.ApplyPose(_rig.IdlePose);
                SceneView.RepaintAll();
                Repaint();
                return;
            }
        }

        _rig.ApplySwing(step.Motion, _playT / Mathf.Max(0.01f, step.TotalTime));
        SceneView.RepaintAll();
        Repaint();
    }

    // ══════════════════ 씬 뷰 시각화 ══════════════════

    void OnSceneGUI(SceneView sv)
    {
        if (_weapon == null || _rig == null) return;
        if (_weapon.Slot != EquipSlot.Weapon) return;

        var step = _weapon.GetStep(_comboIndex);
        if (step == null) return;

        Transform eye = GetEyeTransform();
        if (eye == null) return;

        float total = Mathf.Max(0.01f, step.TotalTime);
        float p = Mathf.Clamp01(_playT / total);
        bool inActive = _playT >= step.WindUp &&
                        _playT <= step.WindUp + step.ActiveWindow;

        if (_showHitbox) DrawHitArc(eye, step, inActive);
        if (_showTrail) DrawTipTrail(inActive);

        DrawInfoLabel(sv, step, p, inActive);
    }

    Transform GetEyeTransform()
    {
        if (_rig == null) return null;
        Transform arm = _rig.ArmRoot != null ? _rig.ArmRoot : _rig.transform;
        return arm.parent != null ? arm.parent : arm;
    }

    void DrawHitArc(Transform eye, ComboStep step, bool inActive)
    {
        if (_showOnlyDuringActive && !inActive) return;

        step.ResolveHitShape(_weapon, out var mode, out float range,
                             out float angle, out float height, out float minRange);

        Vector3 o = eye.position - Vector3.up * 0.6f;
        Vector3 f = new Vector3(eye.forward.x, 0f, eye.forward.z).normalized;
        if (f.sqrMagnitude < 0.001f) f = Vector3.forward;

        Color c = inActive
            ? new Color(1f, 0.25f, 0.15f, 1f)
            : new Color(0.5f, 0.55f, 0.6f, 0.6f);

        // 오버라이드면 색을 다르게
        if (step.OverrideHitShape)
            c = inActive ? new Color(1f, 0.5f, 0.1f, 1f) : new Color(0.6f, 0.5f, 0.3f, 0.6f);

        if (mode == WeaponHitMode.ForwardSphere)
        {
            Handles.color = new Color(c.r, c.g, c.b, inActive ? 0.14f : 0.05f);
            Handles.SphereHandleCap(0, o, Quaternion.identity, range * 2f, EventType.Repaint);
            Handles.color = c;
            Handles.DrawWireDisc(o, Vector3.up, range);
        }
        else
        {
            float half = angle * 0.5f;
            Vector3 from = Quaternion.Euler(0f, -half, 0f) * f;

            Handles.color = new Color(c.r, c.g, c.b, inActive ? 0.20f : 0.07f);
            Handles.DrawSolidArc(o, Vector3.up, from, angle, range);

            Handles.color = c;
            Handles.DrawWireArc(o, Vector3.up, from, angle, range);
            Handles.DrawLine(o, o + from * range, inActive ? 3f : 1.5f);
            Handles.DrawLine(o, o + Quaternion.Euler(0f, half, 0f) * f * range, inActive ? 3f : 1.5f);

            // 수직 경계
            if (height < 50f)
            {
                Handles.color = new Color(c.r, c.g, c.b, 0.35f);
                Handles.DrawWireArc(o + Vector3.up * height, Vector3.up, from, angle, range);
                Handles.DrawWireArc(o - Vector3.up * height, Vector3.up, from, angle, range);
            }
        }

        if (minRange > 0.01f)
        {
            Handles.color = new Color(0.3f, 0.6f, 1f, 0.7f);
            Handles.DrawWireDisc(o, Vector3.up, minRange);
        }

        Handles.color = Color.yellow;
        Handles.Label(o + f * range + Vector3.up * 0.12f,
            $"{range:F2}m{(step.OverrideHitShape ? " (개별)" : "")}",
            EditorStyles.whiteMiniLabel);
    }

    void DrawTipTrail(bool inActive)
    {
        if (_rig == null) return;

        Transform tip = _rig.WeaponTip;
        if (tip == null) return;

        if (_playing)
        {
            _tipTrail.Add(tip.position);
            if (_tipTrail.Count > 90) _tipTrail.RemoveAt(0);
        }

        if (_tipTrail.Count < 2) return;

        for (int i = 1; i < _tipTrail.Count; i++)
        {
            float a = (float)i / _tipTrail.Count;
            Handles.color = new Color(0.3f, 0.9f, 1f, a * 0.9f);
            Handles.DrawLine(_tipTrail[i - 1], _tipTrail[i], 2f + a * 2f);
        }

        Handles.color = inActive ? Color.red : Color.white;
        Handles.SphereHandleCap(0, tip.position, Quaternion.identity, 0.035f, EventType.Repaint);
    }

    void DrawInfoLabel(SceneView sv, ComboStep step, float p, bool inActive)
    {
        Handles.BeginGUI();

        var rect = new Rect(10, 10, 250, 92);
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.65f));

        GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

        GUILayout.Label($"{_weapon.DisplayName}  —  {step.Name}", EditorStyles.whiteBoldLabel);
        GUILayout.Label($"진행 {p * 100f:F0}%   {(inActive ? "◆ 판정 중" : "－")}",
            inActive ? EditorStyles.whiteBoldLabel : EditorStyles.whiteMiniLabel);

        float range = _weapon.Range * step.RangeMultiplier;

        if (_weapon.HitMode == WeaponHitMode.ForwardArc)
            GUILayout.Label($"부채꼴  {range:F2}m  /  {_weapon.HitAngle:F0}°",
                EditorStyles.whiteMiniLabel);
        else
            GUILayout.Label($"정면 구체  {range:F2}m", EditorStyles.whiteMiniLabel);

        GUILayout.Label($"피해 ×{step.DamageMultiplier:F2}   대상 {step.MaxTargets}",
            EditorStyles.whiteMiniLabel);

        GUILayout.EndArea();
        Handles.EndGUI();
    }
}