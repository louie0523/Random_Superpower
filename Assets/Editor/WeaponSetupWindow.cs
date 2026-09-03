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

    // 디버깅
    private bool _showHitbox = true;
    private bool _showTrail = true;
    private bool _showOnlyDuringActive = false;
    private readonly List<Vector3> _tipTrail = new();

    private bool _editBox = false;

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
                "    └ LeftHand\n\n" +
                "구조를 만들고 FPArmRig에 연결하세요.",
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

        // ══════ 여기부터 히트 판정 ══════

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("히트 판정", EditorStyles.boldLabel);

        var so2 = new SerializedObject(_weapon);
        so2.Update();

        EditorGUILayout.PropertyField(so2.FindProperty("HitMode"), new GUIContent("판정 방식"));

        if (_weapon.HitMode == WeaponHitMode.WeaponBox)
        {
            EditorGUILayout.PropertyField(so2.FindProperty("HitBoxCenter"), new GUIContent("박스 중심"));
            EditorGUILayout.PropertyField(so2.FindProperty("HitBoxSize"), new GUIContent("박스 크기"));

            EditorGUILayout.Space(2);

            _editBox = GUILayout.Toggle(_editBox,
                _editBox ? "■ 씬 편집 중 (끄기)" : "▶ 씬에서 박스 편집",
                "Button", GUILayout.Height(24));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("프리셋", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("검", EditorStyles.miniButtonLeft))
                SetBoxPreset(new Vector3(0, 0, 0.7f), new Vector3(0.2f, 0.2f, 1.3f));
            if (GUILayout.Button("대검", EditorStyles.miniButtonMid))
                SetBoxPreset(new Vector3(0, 0, 0.9f), new Vector3(0.3f, 0.3f, 1.8f));
            if (GUILayout.Button("망치", EditorStyles.miniButtonMid))
                SetBoxPreset(new Vector3(0, 0, 1.0f), new Vector3(0.7f, 0.7f, 0.5f));
            if (GUILayout.Button("창", EditorStyles.miniButtonMid))
                SetBoxPreset(new Vector3(0, 0, 1.2f), new Vector3(0.15f, 0.15f, 2.2f));
            if (GUILayout.Button("단검", EditorStyles.miniButtonRight))
                SetBoxPreset(new Vector3(0, 0, 0.3f), new Vector3(0.15f, 0.15f, 0.5f));
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.PropertyField(so2.FindProperty("Range"), new GUIContent("사거리"));
            EditorGUILayout.PropertyField(so2.FindProperty("HitRadius"), new GUIContent("반경"));
        }

        so2.ApplyModifiedProperties();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "씬 뷰에서 무기를 직접 옮긴 뒤 '씬 위치를 저장'을 누르면 EquipmentData에 기록됩니다.",
            MessageType.None);
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
        _presetKind = (SwingPresets.Kind)EditorGUILayout.EnumPopup("프리셋", _presetKind);

        if (GUILayout.Button("적용", GUILayout.Width(52)))
        {
            Undo.RecordObject(_weapon, "Apply Swing Preset");

            bool twoHand = _weapon.Grip == GripType.TwoHand;
            step.Motion = SwingPresets.Create(_presetKind, _rig.IdlePose, twoHand);

            EditorUtility.SetDirty(_weapon);
            _selectedKey = -1;
            _playT = 0f;
            RefreshPreview();
        }
        EditorGUILayout.EndHorizontal();          // ★ 추가

        if (GUILayout.Button("현재 씬 포즈를 기준으로 프리셋 적용"))
        {
            Undo.RecordObject(_weapon, "Apply Preset From Scene");

            bool twoHand = _weapon.Grip == GripType.TwoHand;
            step.Motion = SwingPresets.Create(_presetKind, CaptureCurrentPose(), twoHand);

            EditorUtility.SetDirty(_weapon);
            _selectedKey = -1;
            _playT = 0f;
            RefreshPreview();
        }

        EditorGUILayout.Space(4);                 // ★ 추가 (구분)

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
                WindUp = src?.WindUp ?? 0.15f,
                ActiveWindow = src?.ActiveWindow ?? 0.10f,
                Recovery = src?.Recovery ?? 0.30f,
                CancelPoint = src?.CancelPoint ?? 0.6f,
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

            // 직전 타와 반대 방향 프리셋을 기본으로
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

                    _comboIndex = Mathf.Clamp(_comboIndex, 0, list.Count - 1);
                    _selectedKey = -1;
                    RefreshPreview();
                }
            }
        }

        if (GUILayout.Button("복제", EditorStyles.miniButton, GUILayout.Width(46)))
        {
            Undo.RecordObject(_weapon, "Duplicate Combo Step");

            var list = new List<ComboStep>(_weapon.Combo);
            var src = list[_comboIndex];

            var copy = JsonUtility.FromJson<ComboStep>(JsonUtility.ToJson(src));
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
        if (_comboIndex >= arr.arraySize) return;
        var sp = arr.GetArrayElementAtIndex(_comboIndex);

        EditorGUILayout.PropertyField(sp.FindPropertyRelative("Name"), new GUIContent("이름"));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("WindUp"), new GUIContent("선딜"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("ActiveWindow"), new GUIContent("판정"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("Recovery"), new GUIContent("후딜"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("CancelPoint"), new GUIContent("캔슬"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("DamageMultiplier"), new GUIContent("피해×"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("RangeMultiplier"), new GUIContent("사거리×"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("MaxTargets"), new GUIContent("최대 대상"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("LungeDistance"), new GUIContent("전진"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("HitStopDuration"), new GUIContent("히트스톱"));
        EditorGUILayout.PropertyField(sp.FindPropertyRelative("CameraKickFov"), new GUIContent("FOV킥"));
        EditorGUILayout.EndHorizontal();

        so.ApplyModifiedProperties();

        EditorGUILayout.LabelField($"총 시간 {step.TotalTime:F2}s   캔슬 가능 {step.CancelTime:F2}s",
            EditorStyles.miniLabel);
    }

    void DrawTimelineBar(ComboStep step)
    {
        Rect r = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
        float total = Mathf.Max(0.01f, step.TotalTime);

        EditorGUI.DrawRect(r, new Color(0.16f, 0.16f, 0.16f));

        float w1 = step.WindUp / total;
        float w2 = step.ActiveWindow / total;

        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * w1, r.height),
            new Color(0.28f, 0.33f, 0.48f));
        EditorGUI.DrawRect(new Rect(r.x + r.width * w1, r.y, r.width * w2, r.height),
            new Color(0.78f, 0.28f, 0.20f));

        // 캔슬 지점
        float cp = step.CancelTime / total;
        EditorGUI.DrawRect(new Rect(r.x + r.width * cp - 1, r.y, 1, r.height),
            new Color(0.4f, 0.9f, 0.4f, 0.8f));

        // 키 마커
        if (step.Motion?.Keys != null)
        {
            for (int i = 0; i < step.Motion.Keys.Count; i++)
            {
                float kt = Mathf.Clamp01(step.Motion.Keys[i].Time);
                var m = new Rect(r.x + r.width * kt - 2, r.y, 4, r.height);
                EditorGUI.DrawRect(m, i == _selectedKey
                    ? Color.yellow : new Color(1f, 1f, 1f, 0.65f));
            }
        }

        // 재생 커서
        float p = Mathf.Clamp01(_playT / total);
        EditorGUI.DrawRect(new Rect(r.x + r.width * p - 1, r.y, 2, r.height), Color.cyan);

        // 클릭 스크럽
        Event e = Event.current;
        if (e.type == EventType.MouseDown || (e.type == EventType.MouseDrag && e.button == 0))
        {
            if (r.Contains(e.mousePosition))
            {
                _playing = false;
                _playT = Mathf.Clamp01((e.mousePosition.x - r.x) / r.width) * total;
                RefreshPreview();
                e.Use();
                Repaint();
            }
        }

        GUI.Label(new Rect(r.x + 4, r.y + 8, r.width, 16),
            "  선딜 · 판정 · 후딜   |  녹색=캔슬  흰색=키  청록=커서", EditorStyles.miniLabel);
    }

    void DrawPlayback(ComboStep step)
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(_playing ? "■ 정지" : "▶ 재생", GUILayout.Height(26)))
        {
            _playing = !_playing;
            if (_playing) { _playT = 0f; _tipTrail.Clear(); _sweepGhost.Clear(); }
            else RefreshPreview();
        }

        if (GUILayout.Button("▶▶ 전체", GUILayout.Height(26)))
        {
            _playing = true;
            _comboIndex = 0;
            _playT = 0f;
            _tipTrail.Clear();
            _sweepGhost.Clear();
        }

        _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.miniButton, GUILayout.Width(46), GUILayout.Height(26));

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

        if (GUILayout.Button("Idle 포즈를 씬에서 저장"))
        {
            if (_rig != null)
            {
                Undo.RecordObject(_rig, "Capture Idle Pose");
                _rig.IdlePose = CaptureCurrentPose();
                EditorUtility.SetDirty(_rig);
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("씬 표시", EditorStyles.miniBoldLabel);
        _showHitbox = EditorGUILayout.Toggle("히트박스", _showHitbox);
        _showTrail = EditorGUILayout.Toggle("무기 궤적", _showTrail);
        _showOnlyDuringActive = EditorGUILayout.Toggle("판정 중에만", _showOnlyDuringActive);

        if (GUILayout.Button("궤적 지우기")) { _tipTrail.Clear(); _sweepGhost.Clear(); }
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
            Debug.LogWarning($"[WeaponSetup] {_weapon.name}: ModelPrefab이 비어 있습니다. 손만 표시됩니다.");

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

        if (_showHitbox) DrawHitbox(eye, step, inActive);
        if (_showTrail) DrawTipTrail(inActive);

        DrawInfoLabel(sv, step, p, inActive);
    }

    Transform GetEyeTransform()
    {
        if (_rig == null) return null;
        Transform arm = _rig.ArmRoot != null ? _rig.ArmRoot : _rig.transform;
        return arm.parent != null ? arm.parent : arm;
    }

    void DrawHitbox(Transform eye, ComboStep step, bool inActive)
    {
        if (_showOnlyDuringActive && !inActive) return;

        if (_weapon.HitMode == WeaponHitMode.ForwardSphere)
        {
            DrawSphereHitbox(eye, step, inActive);
            return;
        }

        if (!_rig.GetHitBox(_weapon, out Vector3 c, out Vector3 half, out Quaternion rot))
            return;

        half *= step.RadiusMultiplier;

        Color col = inActive
            ? new Color(1f, 0.25f, 0.15f, 1f)
            : new Color(0.5f, 0.5f, 0.55f, 0.6f);

        Handles.color = col;
        Handles.matrix = Matrix4x4.TRS(c, rot, Vector3.one);
        Handles.DrawWireCube(Vector3.zero, half * 2f);
        Handles.matrix = Matrix4x4.identity;

        Handles.Label(c + Vector3.up * (half.y + 0.08f),
            $"{_weapon.HitBoxSize.x:F2} × {_weapon.HitBoxSize.y:F2} × {_weapon.HitBoxSize.z:F2}",
            EditorStyles.whiteMiniLabel);

        if (inActive && _playing) RecordSweep(c, rot, half);
        DrawSweepGhost();
    }

    void RecordSweep(Vector3 c, Quaternion r, Vector3 h)
    {
        _sweepGhost.Add((c, r, h));
        if (_sweepGhost.Count > 30) _sweepGhost.RemoveAt(0);
    }

    void DrawSphereHitbox(Transform eye, ComboStep step, bool inActive)
    {
        float range = _weapon.Range * step.RangeMultiplier;
        float radius = _weapon.HitRadius * step.RadiusMultiplier;
        float sphereR = radius + range * 0.3f;

        Vector3 origin = eye.position;
        Vector3 dir = eye.forward;
        Vector3 center = origin + dir * (range * 0.5f);

        Color c = inActive
            ? new Color(1f, 0.25f, 0.15f, 1f)
            : new Color(0.5f, 0.5f, 0.55f, 0.5f);

        // 반투명 구
        Handles.color = new Color(c.r, c.g, c.b, inActive ? 0.14f : 0.05f);
        Handles.SphereHandleCap(0, center, Quaternion.identity, sphereR * 2f, EventType.Repaint);

        // 와이어 3축
        Handles.color = c;
        Handles.DrawWireDisc(center, Vector3.up, sphereR);
        Handles.DrawWireDisc(center, eye.right, sphereR);
        Handles.DrawWireDisc(center, eye.forward, sphereR);

        // 중심축
        Handles.color = new Color(c.r, c.g, c.b, 0.8f);
        Handles.DrawLine(origin, origin + dir * range, inActive ? 3f : 1.5f);

        // 사거리 끝
        Handles.color = Color.yellow;
        Handles.DrawWireDisc(origin + dir * range, dir, 0.06f);
        Handles.Label(origin + dir * range + Vector3.up * 0.12f,
            $"{range:F2}m", EditorStyles.whiteMiniLabel);

        // 눈 위치
        Handles.color = Color.cyan;
        Handles.DrawWireDisc(origin, dir, 0.04f);

        // 다중 타격 표시
        if (step.MaxTargets > 1)
        {
            Handles.color = new Color(1f, 0.8f, 0.2f, 0.35f);
            Handles.DrawWireDisc(center, Vector3.up, sphereR * 1.02f);
        }
    }

    private readonly List<(Vector3 c, Quaternion r, Vector3 h)> _sweepGhost = new();




    void DrawSweepGhost()
    {
        for (int i = 0; i < _sweepGhost.Count; i++)
        {
            float a = (float)(i + 1) / _sweepGhost.Count;
            Handles.color = new Color(1f, 0.4f, 0.2f, a * 0.3f);
            var g = _sweepGhost[i];
            Handles.matrix = Matrix4x4.TRS(g.c, g.r, Vector3.one);
            Handles.DrawWireCube(Vector3.zero, g.h * 2f);
        }
        Handles.matrix = Matrix4x4.identity;
    }

    void DrawTipTrail(bool inActive)
    {
        if (_rig == null) return;

        Transform tip = _rig.WeaponTip;
        if (tip == null) return;

        // 재생 중일 때만 궤적 기록
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

        // 현재 팁
        Handles.color = inActive ? Color.red : Color.white;
        Handles.SphereHandleCap(0, tip.position, Quaternion.identity, 0.035f, EventType.Repaint);
    }

    void DrawInfoLabel(SceneView sv, ComboStep step, float p, bool inActive)
    {
        Handles.BeginGUI();

        var rect = new Rect(10, 10, 240, 92);
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.65f));

        GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

        GUILayout.Label($"{_weapon.DisplayName}  —  {step.Name}", EditorStyles.whiteBoldLabel);
        GUILayout.Label($"진행 {p * 100f:F0}%   {(inActive ? "◆ 판정 중" : "－")}",
            inActive ? EditorStyles.whiteBoldLabel : EditorStyles.whiteMiniLabel);

        if (_weapon.HitMode == WeaponHitMode.ForwardSphere)
        {
            float range = _weapon.Range * step.RangeMultiplier;
            float radius = _weapon.HitRadius * step.RadiusMultiplier;
            GUILayout.Label($"정면 구체  사거리 {range:F2}m", EditorStyles.whiteMiniLabel);
            GUILayout.Label($"반경 {radius + range * 0.3f:F2}m   대상 {step.MaxTargets}",
                EditorStyles.whiteMiniLabel);
        }
        else
        {
            //float bladeLen = Mathf.Abs(_weapon.BladeEnd - _weapon.BladeStart);
            //GUILayout.Label($"무기 궤적  날 {bladeLen:F2}m", EditorStyles.whiteMiniLabel);
            //GUILayout.Label($"두께 {_weapon.BladeRadius * step.RadiusMultiplier:F2}m   대상 {step.MaxTargets}",
            //    EditorStyles.whiteMiniLabel);
        }

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    void DrawBoxHandles()
    {
        if (_weapon.HitMode != WeaponHitMode.WeaponBox) return;

        Transform w = _rig.GetWeaponTransform();
        if (w == null) return;

        EditorGUI.BeginChangeCheck();

        // 중심 이동
        Handles.matrix = w.localToWorldMatrix;
        Vector3 newCenter = Handles.PositionHandle(_weapon.HitBoxCenter, Quaternion.identity);

        // 크기 조절 (각 축 화살표)
        Handles.color = Color.green;
        Vector3 newSize = _weapon.HitBoxSize;
        float hs = HandleUtility.GetHandleSize(w.TransformPoint(newCenter)) * 0.6f;

        newSize.x = 2f * Handles.ScaleValueHandle(newSize.x * 0.5f,
            newCenter + Vector3.right * newSize.x * 0.5f,
            Quaternion.LookRotation(Vector3.right), hs, Handles.CubeHandleCap, 0f);

        newSize.y = 2f * Handles.ScaleValueHandle(newSize.y * 0.5f,
            newCenter + Vector3.up * newSize.y * 0.5f,
            Quaternion.LookRotation(Vector3.up), hs, Handles.CubeHandleCap, 0f);

        newSize.z = 2f * Handles.ScaleValueHandle(newSize.z * 0.5f,
            newCenter + Vector3.forward * newSize.z * 0.5f,
            Quaternion.LookRotation(Vector3.forward), hs, Handles.CubeHandleCap, 0f);

        Handles.matrix = Matrix4x4.identity;

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_weapon, "Edit Hit Box");
            _weapon.HitBoxCenter = newCenter;
            _weapon.HitBoxSize = new Vector3(
                Mathf.Max(0.02f, newSize.x),
                Mathf.Max(0.02f, newSize.y),
                Mathf.Max(0.02f, newSize.z));
            EditorUtility.SetDirty(_weapon);
            Repaint();
        }
    }

    void SetBoxPreset(Vector3 center, Vector3 size)
    {
        Undo.RecordObject(_weapon, "Box Preset");
        _weapon.HitBoxCenter = center;
        _weapon.HitBoxSize = size;
        EditorUtility.SetDirty(_weapon);
    }
}