using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class WeaponSetupWindow : EditorWindow
{
    // ── 대상 ──
    private Object _target;
    private IMotionSource _source;
    private EquipmentData _weapon;
    private EquipmentData _previewWeaponOverride;
    private FPArmRig _rig;

    // ── 상태 ──
    private bool _firstPerson = true;
    private int _comboIndex;
    private int _selectedKey = -1;
    private int _selectedEvent = -1;
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
    private int _dragHandle = -1;
    private int _draggingKey = -1;
    private bool _needsSort;

    // ── 씬 표시 ──
    private bool _showHitbox = true;
    private bool _showTrail = true;
    private bool _showOnlyDuringActive;
    private readonly List<Vector3> _tipTrail = new();

    private static readonly string[] FingerNames = { "엄지", "검지", "중지", "약지" };
    private static readonly float[] JointWeights = { 1f, 0.95f, 0.85f };

    [MenuItem("Tools/Motion Setup")]
    static void Open()
    {
        var w = GetWindow<WeaponSetupWindow>("Motion Setup");
        w.minSize = new Vector2(380, 560);
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

        HandRigEditor.EditTarget = null;
        HandRigEditor.OnPoseChanged = null;

        if (_rig != null) _rig.CleanWeaponOnly();
    }

    void OnUndo() { RefreshPreview(); Repaint(); }

    void FindRig()
    {
        if (_rig == null) _rig = FindFirstObjectByType<FPArmRig>();
    }

    Object TargetAsset => _target;

    EquipmentData ResolvePreviewWeapon()
    {
        if (_previewWeaponOverride != null) return _previewWeaponOverride;
        return _source?.PreviewWeapon;
    }

    // ══════════════════════ GUI ══════════════════════

    void OnGUI()
    {
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "플레이 모드에서는 편집할 수 없습니다.\nPlay를 중지한 뒤 사용하세요.",
                MessageType.Warning);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawTarget();

        if (_source != null && _rig != null)
        {
            if (_weapon != null)
            {
                EditorGUILayout.Space(6);
                _foldTransform = EditorGUILayout.Foldout(_foldTransform,
                    "무기 · 손 · 판정", true, EditorStyles.foldoutHeader);
                if (_foldTransform) DrawWeaponSection();
            }

            EditorGUILayout.Space(6);
            _foldMotion = EditorGUILayout.Foldout(_foldMotion,
                "모션 편집", true, EditorStyles.foldoutHeader);
            if (_foldMotion) DrawMotion();

            EditorGUILayout.Space(6);
            _foldTools = EditorGUILayout.Foldout(_foldTools,
                "도구", true, EditorStyles.foldoutHeader);
            if (_foldTools) DrawTools();
        }

        EditorGUILayout.EndScrollView();
    }

    // ─────────── 대상 ───────────

    void DrawTarget()
    {
        EditorGUILayout.LabelField("대상", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _target = EditorGUILayout.ObjectField("무기 / 능력 모션", _target, typeof(Object), false);
        if (EditorGUI.EndChangeCheck()) OnTargetChanged();

        _rig = (FPArmRig)EditorGUILayout.ObjectField("팔 리그", _rig, typeof(FPArmRig), true);

        if (_rig == null)
        {
            EditorGUILayout.HelpBox(
                "씬에 FPArmRig가 없습니다.\n\n" +
                "PreviewCamera\n └ CamPivot\n    └ ArmRoot (FPArmRig)\n" +
                "       ├ RightHand\n       └ LeftHand",
                MessageType.Warning);

            if (GUILayout.Button("씬에서 찾기")) FindRig();
            return;
        }

        if (_target == null)
        {
            EditorGUILayout.HelpBox(
                "EquipmentData(무기) 또는 AbilityMotionData(능력 모션)를 넣으세요.",
                MessageType.Info);
            return;
        }

        if (_source == null)
        {
            EditorGUILayout.HelpBox($"{_target.name}은(는) 모션 소스가 아닙니다.", MessageType.Error);
            return;
        }

        // 능력 모션이면 테스트 무기 선택 가능
        if (_weapon == null)
        {
            EditorGUI.BeginChangeCheck();
            _previewWeaponOverride = (EquipmentData)EditorGUILayout.ObjectField(
                new GUIContent("테스트 무기", "미리보기 전용. 에셋에 저장되지 않음"),
                _previewWeaponOverride, typeof(EquipmentData), false);
            if (EditorGUI.EndChangeCheck()) SpawnWeapon();
        }

        EditorGUILayout.LabelField("종류", _weapon != null ? "무기" : "능력 모션",
            EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("다시 로드")) SpawnWeapon();
        if (GUILayout.Button("무기 치우기")) ClearPreview();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("손 재생성")) { _rig.RebuildHands(); SpawnWeapon(); }
        if (GUILayout.Button("전부 정리")) { _rig.CleanAll(); SceneView.RepaintAll(); }
        EditorGUILayout.EndHorizontal();
    }

    void OnTargetChanged()
    {
        _source = _target as IMotionSource;
        _weapon = _target as EquipmentData;

        _comboIndex = 0;
        _selectedKey = -1;
        _selectedEvent = -1;
        _playT = 0f;
        _playing = false;

        SpawnWeapon();
    }

    // ─────────── 무기 / 손 / 판정 ───────────

    void DrawWeaponSection()
    {
        EditorGUI.BeginChangeCheck();
        _firstPerson = EditorGUILayout.Toggle("1인칭 오프셋 편집", _firstPerson);
        if (EditorGUI.EndChangeCheck()) RefreshPreview();

        // ══ 무기 배치 ══
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("무기 배치", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        EditorGUI.BeginChangeCheck();
        var newGrip = (GripType)EditorGUILayout.EnumPopup("잡는 손", _weapon.Grip);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_weapon, "Change Grip");
            _weapon.Grip = newGrip;

            if (newGrip == GripType.TwoHand) _weapon.UseLeftHand = true;

            EditorUtility.SetDirty(_weapon);
            SpawnWeapon();          // ★ 무기 재생성
        }

        if (_firstPerson)
        {
            _weapon.FpPositionOffset = EditorGUILayout.Vector3Field("위치", _weapon.FpPositionOffset);
            _weapon.FpRotationOffset = EditorGUILayout.Vector3Field("회전", _weapon.FpRotationOffset);
        }
        else
        {
            _weapon.PositionOffset = EditorGUILayout.Vector3Field("위치", _weapon.PositionOffset);
            _weapon.RotationOffset = EditorGUILayout.Vector3Field("회전", _weapon.RotationOffset);
        }

        _weapon.ScaleOverride = EditorGUILayout.Vector3Field("크기", _weapon.ScaleOverride);

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_weapon);
            RefreshPreview();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("크기 균일 (X기준)"))
        {
            Undo.RecordObject(_weapon, "Uniform Scale");
            float s = _weapon.ScaleOverride.x;
            _weapon.ScaleOverride = new Vector3(s, s, s);
            EditorUtility.SetDirty(_weapon);
            RefreshPreview();
        }
        if (GUILayout.Button("크기 1로"))
        {
            Undo.RecordObject(_weapon, "Reset Scale");
            _weapon.ScaleOverride = Vector3.one;
            EditorUtility.SetDirty(_weapon);
            RefreshPreview();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("씬 무기 위치 저장")) CaptureWeaponTransform();
        if (GUILayout.Button("초기화")) ResetWeaponTransform();
        EditorGUILayout.EndHorizontal();

        // ══ 손 자세 ══
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("손 자세", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        _weapon.OverrideIdlePose = EditorGUILayout.Toggle(
            new GUIContent("무기 전용 기본 자세", "끄면 FPArmRig의 IdlePose 사용"),
            _weapon.OverrideIdlePose);

        if (_weapon.OverrideIdlePose)
        {
            EditorGUI.indentLevel++;

            _weapon.RightHandPos = EditorGUILayout.Vector3Field("오른손 위치", _weapon.RightHandPos);
            _weapon.RightHandRot = EditorGUILayout.Vector3Field("오른손 회전", _weapon.RightHandRot);

            _weapon.UseLeftHand = EditorGUILayout.Toggle("왼손 표시", _weapon.UseLeftHand);
            if (_weapon.UseLeftHand)
            {
                _weapon.LeftHandPos = EditorGUILayout.Vector3Field("왼손 위치", _weapon.LeftHandPos);
                _weapon.LeftHandRot = EditorGUILayout.Vector3Field("왼손 회전", _weapon.LeftHandRot);
            }

            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_weapon);
            RefreshPreview();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("씬 손 자세 저장")) CaptureWeaponIdlePose();
        if (GUILayout.Button("리그 Idle 복사")) CopyRigIdle();
        EditorGUILayout.EndHorizontal();

        // ★ 추가
        if (GUILayout.Button("ArmRoot 기본값 전부 가져오기 (자세 + 손가락)"))
            PullFromRigDefaults();

        // ══ 손가락 그립 ══
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("손가락 그립", EditorStyles.boldLabel);

        Vector3 axis = GetCurlAxis();

        _weapon.RightGrip ??= HandPose.Grip(axis);
        DrawGripEditor("오른손", ref _weapon.RightGrip, axis, _rig.RightHandRig, _weapon);

        if (_weapon.Grip == GripType.TwoHand || _weapon.UseLeftHand)
        {
            _weapon.LeftGrip ??= HandPose.Relaxed(axis);
            DrawGripEditor("왼손", ref _weapon.LeftGrip, axis, _rig.LeftHandRig, _weapon);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("오른손 → 왼손 복사", EditorStyles.miniButton))
            {
                Undo.RecordObject(_weapon, "Copy Grip");
                if (_rig.RightHandRig != null && _rig.LeftHandRig != null)
                {
                    _rig.LeftHandRig.CopyBoneRotationsFrom(_rig.RightHandRig);
                    _weapon.LeftGrip ??= new HandPose();
                    _rig.LeftHandRig.BakeAll(_weapon.LeftGrip);
                }
                EditorUtility.SetDirty(_weapon);
                RefreshPreview();
            }
            if (GUILayout.Button("왼손 → 오른손 복사", EditorStyles.miniButton))
            {
                Undo.RecordObject(_weapon, "Copy Grip");
                if (_rig.RightHandRig != null && _rig.LeftHandRig != null)
                {
                    _rig.RightHandRig.CopyBoneRotationsFrom(_rig.LeftHandRig);
                    _weapon.RightGrip ??= new HandPose();
                    _rig.RightHandRig.BakeAll(_weapon.RightGrip);
                }
                EditorUtility.SetDirty(_weapon);
                RefreshPreview();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ★ 리그 기본값 가져오기
        if (GUILayout.Button("리그 기본 손가락 가져오기"))
        {
            Undo.RecordObject(_weapon, "Pull Fingers");

            if (_rig.IdleRightFingers != null)
                _weapon.RightGrip = _rig.IdleRightFingers.Clone();
            if (_rig.IdleLeftFingers != null)
                _weapon.LeftGrip = _rig.IdleLeftFingers.Clone();

            EditorUtility.SetDirty(_weapon);
            RefreshPreview();
            Debug.Log("[MotionSetup] 리그 기본 손가락 적용됨");
        }

        // ══ 히트 판정 ══
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("히트 판정", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        _weapon.Class = (WeaponClass)EditorGUILayout.EnumPopup("무기 계열", _weapon.Class);
        _weapon.HitMode = (WeaponHitMode)EditorGUILayout.EnumPopup("판정 방식", _weapon.HitMode);
        _weapon.Range = EditorGUILayout.FloatField("사거리 (m)", _weapon.Range);

        if (_weapon.HitMode == WeaponHitMode.ForwardArc)
            _weapon.HitAngle = EditorGUILayout.Slider("각도 (°)", _weapon.HitAngle, 20f, 360f);

        _weapon.HitHeight = EditorGUILayout.FloatField("수직 허용 (m)", _weapon.HitHeight);
        _weapon.MinRange = EditorGUILayout.FloatField("최소 사거리", _weapon.MinRange);

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_weapon);
            SceneView.RepaintAll();
        }
    }

    void PullFromRigDefaults()
    {
        if (_rig == null || _weapon == null) return;

        Undo.RecordObject(_weapon, "Pull Rig Defaults");

        // 손 위치/회전
        var idle = _rig.IdlePose;
        _weapon.RightHandPos = idle.RightPos;
        _weapon.RightHandRot = idle.RightRot;
        _weapon.LeftHandPos = idle.LeftPos;
        _weapon.LeftHandRot = idle.LeftRot;
        _weapon.UseLeftHand = idle.UseLeftHand || _weapon.Grip == GripType.TwoHand;
        _weapon.OverrideIdlePose = true;

        // 손가락
        if (_rig.IdleRightFingers != null)
            _weapon.RightGrip = _rig.IdleRightFingers.Clone();
        if (_rig.IdleLeftFingers != null)
            _weapon.LeftGrip = _rig.IdleLeftFingers.Clone();

        EditorUtility.SetDirty(_weapon);
        SpawnWeapon();

        Debug.Log($"[MotionSetup] {_weapon.DisplayName}에 리그 기본값 적용됨");
    }

    Vector3 GetCurlAxis() =>
        _rig?.RightHandRig != null ? _rig.RightHandRig.CurlAxis : new Vector3(0f, 0f, -70f);

    void DrawGripEditor(string label, ref HandPose grip, Vector3 axis,
                     HandRig handRig, Object dirtyTarget)
    {
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;

        // 프리셋
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("펌", EditorStyles.miniButtonLeft))
            SetGripPose(ref grip, HandPose.Open(axis), dirtyTarget, handRig);
        if (GUILayout.Button("자연", EditorStyles.miniButtonMid))
            SetGripPose(ref grip, HandPose.Relaxed(axis), dirtyTarget, handRig);
        if (GUILayout.Button("쥠", EditorStyles.miniButtonMid))
            SetGripPose(ref grip, HandPose.Grip(axis), dirtyTarget, handRig);
        if (GUILayout.Button("주먹", EditorStyles.miniButtonRight))
            SetGripPose(ref grip, HandPose.Fist(axis), dirtyTarget, handRig);
        EditorGUILayout.EndHorizontal();

        if (handRig != null)
        {
            // ★ 씬 → 데이터
            if (GUILayout.Button("씬 손가락을 이 무기에 저장", GUILayout.Height(22)))
            {
                Undo.RegisterCompleteObjectUndo(dirtyTarget, "Bake Grip");
                grip ??= new HandPose();
                handRig.BakeAll(grip);
                EditorUtility.SetDirty(dirtyTarget);

                Debug.Log($"[MotionSetup] {label} 그립 저장됨 — " +
                          $"엄지1 {grip.Get(0).Joints[0]}");
                Repaint();
            }

            // ★ 데이터 → 씬
            if (GUILayout.Button("저장된 값을 씬에 적용", EditorStyles.miniButton))
            {
                if (grip != null) handRig.Apply(grip);
                SceneView.RepaintAll();
            }

            DrawSceneEditToggle(grip, handRig, dirtyTarget);
            DrawFingerSliders(grip, axis, handRig, dirtyTarget);
        }

        EditorGUI.indentLevel--;
    }

    void DrawSceneEditToggle(HandPose pose, HandRig handRig, Object dirtyTarget)
    {
        if (pose == null) return;

        bool editing = ReferenceEquals(HandRigEditor.EditTarget, pose);

        bool now = GUILayout.Toggle(editing,
            editing ? "■ 씬 IK 편집 중 (끄기)" : "▶ 씬에서 IK로 편집",
            "Button", GUILayout.Height(20));

        if (now == editing) return;

        if (now)
        {
            HandRigEditor.EditTarget = pose;
            HandRigEditor.OnPoseChanged = () =>
            {
                EditorUtility.SetDirty(dirtyTarget);
                Repaint();
            };
            Selection.activeGameObject = handRig.gameObject;
        }
        else
        {
            HandRigEditor.EditTarget = null;
            HandRigEditor.OnPoseChanged = null;
        }

        SceneView.RepaintAll();
    }

    void DrawFingerSliders(HandPose pose, Vector3 axis, HandRig handRig, Object dirtyTarget)
    {
        if (pose == null) return;
        pose.EnsureValid();

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var fp = pose.Get(f);
            if (fp == null || fp.Joints.Length == 0) continue;

            float cur = fp.Joints[0].magnitude / Mathf.Max(0.01f, axis.magnitude);

            EditorGUI.BeginChangeCheck();
            float nv = EditorGUILayout.Slider(FingerNames[f], cur, 0f, 1.2f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(dirtyTarget, "Finger Curl");
                fp.SetCurl(nv, axis, JointWeights);
                EditorUtility.SetDirty(dirtyTarget);
                handRig?.Apply(pose);
                SceneView.RepaintAll();
            }
        }
    }

    void SetGripPose(ref HandPose grip, HandPose np, Object dirty, HandRig handRig)
    {
        Undo.RecordObject(dirty, "Set Grip");
        grip = np;
        EditorUtility.SetDirty(dirty);
        handRig?.Apply(np);
        RefreshPreview();
        SceneView.RepaintAll();
    }

    // ─────────── 모션 ───────────

    void DrawMotion()
    {
        int len = _source.ComboLength;

        if (len == 0)
        {
            EditorGUILayout.HelpBox("모션이 비어 있습니다.", MessageType.Warning);
            if (_source.SupportsMultiStep && GUILayout.Button("1타 추가"))
            {
                Undo.RecordObject(TargetAsset, "Add Combo");
                _source.SetSteps(new[] { new ComboStep { Name = "1타" } });
                EditorUtility.SetDirty(TargetAsset);
            }
            return;
        }

        // 콤보 탭
        if (len > 1)
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < len; i++)
            {
                var s = _source.GetStep(i);
                bool on = _comboIndex == i;
                var style = i == 0 ? EditorStyles.miniButtonLeft
                    : i == len - 1 ? EditorStyles.miniButtonRight
                    : EditorStyles.miniButtonMid;

                if (GUILayout.Toggle(on, s?.Name ?? $"{i + 1}타", style) && !on)
                {
                    _comboIndex = i;
                    _selectedKey = -1;
                    _selectedEvent = -1;
                    _playT = 0f;
                    _playing = false;
                    RefreshPreview();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        _comboIndex = Mathf.Clamp(_comboIndex, 0, len - 1);
        var step = _source.GetStep(_comboIndex);
        if (step == null) return;

        EditorGUILayout.Space(4);

        // 프리셋
        EditorGUILayout.BeginHorizontal();
        _presetKind = (SwingPresets.Kind)EditorGUILayout.EnumPopup("모션 프리셋", _presetKind);
        if (GUILayout.Button("적용", GUILayout.Width(52)))
            ApplyPreset(step, _rig.GetEffectiveIdle());
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("현재 씬 포즈 기준으로 적용"))
            ApplyPreset(step, CaptureCurrentPose());

        if (_source.SupportsMultiStep)
        {
            EditorGUILayout.Space(4);
            DrawComboButtons(len);
        }

        EditorGUILayout.Space(4);
        DrawStepTiming(step);

        EditorGUILayout.Space(4);
        DrawTimelineBar(step);

        EditorGUILayout.Space(6);
        DrawPlayback(step);

        EditorGUILayout.Space(6);
        DrawKeyList(step);

        EditorGUILayout.Space(6);
        DrawEventList(step);
    }

    void ApplyPreset(ComboStep step, ArmPose baseIdle)
    {
        Undo.RecordObject(TargetAsset, "Apply Preset");
        step.Motion = SwingPresets.Create(_presetKind, baseIdle, IsTwoHand());
        EditorUtility.SetDirty(TargetAsset);
        _selectedKey = -1;
        _playT = 0f;
        RefreshPreview();
    }

    bool IsTwoHand()
    {
        var w = ResolvePreviewWeapon();
        return w != null && w.Grip == GripType.TwoHand;
    }

    void DrawComboButtons(int len)
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ 타 추가", EditorStyles.miniButton))
        {
            Undo.RecordObject(TargetAsset, "Add Combo Step");

            var list = new List<ComboStep>(_source.GetSteps() ?? new ComboStep[0]);
            var src = list.Count > 0 ? list[^1] : null;

            var next = new ComboStep
            {
                Name = $"{list.Count + 1}타",
                Duration = src?.Duration ?? 0.55f,
                HitStart = src?.HitStart ?? 0.30f,
                HitEnd = src?.HitEnd ?? 0.55f,
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
            next.Motion = SwingPresets.Create(kind, _rig.GetEffectiveIdle(), IsTwoHand());

            list.Add(next);
            _source.SetSteps(list.ToArray());
            EditorUtility.SetDirty(TargetAsset);

            _comboIndex = list.Count - 1;
            _selectedKey = -1;
            _playT = 0f;
            RefreshPreview();
        }

        using (new EditorGUI.DisabledScope(len <= 1))
        {
            if (GUILayout.Button("− 삭제", EditorStyles.miniButton))
            {
                if (EditorUtility.DisplayDialog("확인",
                    $"{_source.GetStep(_comboIndex)?.Name}를 삭제할까요?", "삭제", "취소"))
                {
                    Undo.RecordObject(TargetAsset, "Delete Combo Step");

                    var list = new List<ComboStep>(_source.GetSteps());
                    list.RemoveAt(_comboIndex);
                    _source.SetSteps(list.ToArray());
                    EditorUtility.SetDirty(TargetAsset);

                    _comboIndex = Mathf.Clamp(_comboIndex, 0, Mathf.Max(0, list.Count - 1));
                    _selectedKey = -1;
                    RefreshPreview();
                }
            }
        }

        if (GUILayout.Button("복제", EditorStyles.miniButton, GUILayout.Width(46)))
        {
            Undo.RecordObject(TargetAsset, "Duplicate Step");

            var list = new List<ComboStep>(_source.GetSteps());
            var copy = JsonUtility.FromJson<ComboStep>(JsonUtility.ToJson(list[_comboIndex]));
            copy.Name = $"{list.Count + 1}타";
            list.Insert(_comboIndex + 1, copy);

            _source.SetSteps(list.ToArray());
            EditorUtility.SetDirty(TargetAsset);
            _comboIndex++;
            RefreshPreview();
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawStepTiming(ComboStep step)
    {
        EditorGUI.BeginChangeCheck();

        step.Name = EditorGUILayout.TextField("이름", step.Name);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("타이밍", EditorStyles.miniBoldLabel);

        step.Duration = EditorGUILayout.FloatField("전체 길이 (초)", step.Duration);
        step.HitStart = EditorGUILayout.Slider("판정 시작", step.HitStart, 0f, 1f);
        step.HitEnd = EditorGUILayout.Slider("판정 끝", step.HitEnd, 0f, 1f);
        step.CancelPoint = EditorGUILayout.Slider("캔슬 지점", step.CancelPoint, 0f, 1f);
        step.ComboResetTime = EditorGUILayout.FloatField("콤보 유지 (초)", step.ComboResetTime);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("피해", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        step.DamageMultiplier = EditorGUILayout.FloatField("피해×", step.DamageMultiplier);
        step.RangeMultiplier = EditorGUILayout.FloatField("사거리×", step.RangeMultiplier);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        step.MaxTargets = EditorGUILayout.IntField("최대 대상", step.MaxTargets);
        step.LungeDistance = EditorGUILayout.FloatField("전진 (m)", step.LungeDistance);
        EditorGUILayout.EndHorizontal();

        // 판정 오버라이드
        EditorGUILayout.Space(2);
        step.OverrideHitShape = EditorGUILayout.Toggle(
            new GUIContent("판정 범위 개별 설정", "체크하면 무기 값 대신 이 값 사용"),
            step.OverrideHitShape);

        if (step.OverrideHitShape)
        {
            EditorGUI.indentLevel++;

            step.HitMode = (WeaponHitMode)EditorGUILayout.EnumPopup("방식", step.HitMode);
            step.Range = EditorGUILayout.FloatField("사거리", step.Range);

            if (step.HitMode == WeaponHitMode.ForwardArc)
                step.HitAngle = EditorGUILayout.Slider("각도", step.HitAngle, 20f, 360f);

            step.HitHeight = EditorGUILayout.FloatField("수직 허용", step.HitHeight);
            step.MinRange = EditorGUILayout.FloatField("최소 사거리", step.MinRange);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("주변 구체", EditorStyles.miniButton))
            {
                step.HitMode = WeaponHitMode.ForwardSphere;
                step.Range = 2f; step.HitHeight = 1.5f;
            }
            if (GUILayout.Button("전방 넓게", EditorStyles.miniButton))
            {
                step.HitMode = WeaponHitMode.ForwardArc;
                step.Range = 3.5f; step.HitAngle = 160f;
            }
            if (GUILayout.Button("찌르기", EditorStyles.miniButton))
            {
                step.HitMode = WeaponHitMode.ForwardArc;
                step.Range = 5f; step.HitAngle = 40f;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("연출", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        step.HitStopDuration = EditorGUILayout.FloatField("히트스톱", step.HitStopDuration);
        step.CameraKickFov = EditorGUILayout.FloatField("FOV킥", step.CameraKickFov);
        EditorGUILayout.EndHorizontal();

        step.CameraShake = EditorGUILayout.FloatField("셰이크", step.CameraShake);

        if (EditorGUI.EndChangeCheck())
        {
            step.HitEnd = Mathf.Max(step.HitEnd, step.HitStart + 0.02f);
            EditorUtility.SetDirty(TargetAsset);
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(
            $"선딜 {step.WindUp:F2}s · 판정 {step.ActiveWindow:F2}s · 후딜 {step.Recovery:F2}s",
            EditorStyles.miniLabel);

        if (step.ActiveWindow < 0.05f)
            EditorGUILayout.HelpBox(
                $"판정 구간이 {step.ActiveWindow:F3}초로 너무 짧습니다. 0.08초 이상 권장.",
                MessageType.Warning);
    }

    void DrawTimelineBar(ComboStep step)
    {
        Rect r = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
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

        // 키 마커 (아래)
        if (step.Motion?.Keys != null)
            foreach (var k in step.Motion.Keys)
            {
                float kx = r.x + r.width * Mathf.Clamp01(k.Time);
                EditorGUI.DrawRect(new Rect(kx - 1, r.y + r.height - 7, 2, 7),
                    new Color(1f, 1f, 1f, 0.75f));
            }

        // 이벤트 마커 (위)
        if (step.Events != null)
            foreach (var ev in step.Events)
            {
                float ex = r.x + r.width * Mathf.Clamp01(ev.Time);
                EditorGUI.DrawRect(new Rect(ex - 1, r.y, 2, 7),
                    new Color(0.3f, 1f, 0.9f, 0.95f));
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
                Undo.RecordObject(TargetAsset, "Edit Timing");
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

            EditorUtility.SetDirty(TargetAsset);
            e.Use(); Repaint();
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
            e.Use(); Repaint();
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

        if (_source.ComboLength > 1 && GUILayout.Button("▶▶ 전체", GUILayout.Height(26)))
        {
            _playing = true; _comboIndex = 0; _playT = 0f; _tipTrail.Clear();
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

    // ─────────── 키프레임 ───────────

    void DrawKeyList(ComboStep step)
    {
        var motion = step.Motion;
        if (motion == null)
        {
            step.Motion = motion = new SwingMotion();
            EditorUtility.SetDirty(TargetAsset);
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
            float nt = EditorGUILayout.Slider(k.Time, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                k.Time = nt;
                _draggingKey = i;
                _needsSort = true;
                EditorUtility.SetDirty(TargetAsset);
                PreviewPose(k.Pose);
            }

            GUILayout.Label($"{k.Time * step.Duration:F2}s",
                EditorStyles.miniLabel, GUILayout.Width(42));

            using (new EditorGUI.DisabledScope(i == 0))
                if (GUILayout.Button("▲", EditorStyles.miniButtonMid, GUILayout.Width(22)))
                {
                    SwapKeyPose(motion, i, i - 1);
                    _selectedKey = i - 1;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

            using (new EditorGUI.DisabledScope(i == motion.Keys.Count - 1))
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22)))
                {
                    SwapKeyPose(motion, i, i + 1);
                    _selectedKey = i + 1;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

            if (GUILayout.Button("보기", EditorStyles.miniButtonMid, GUILayout.Width(34)))
                PreviewPose(k.Pose);

            if (GUILayout.Button("−", EditorStyles.miniButtonRight, GUILayout.Width(22)))
            {
                Undo.RecordObject(TargetAsset, "Delete Key");
                motion.Keys.RemoveAt(i);
                EditorUtility.SetDirty(TargetAsset);
                _selectedKey = -1;
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();

            if (_selectedKey == i) DrawKeyDetail(k);
        }

        // 드래그 끝났을 때만 정렬
        if (_needsSort && Event.current.type == EventType.MouseUp)
        {
            var keep = (_draggingKey >= 0 && _draggingKey < motion.Keys.Count)
                ? motion.Keys[_draggingKey] : null;

            motion.SortKeys();

            if (keep != null) _selectedKey = motion.Keys.IndexOf(keep);

            _needsSort = false;
            _draggingKey = -1;
            EditorUtility.SetDirty(TargetAsset);
            Repaint();
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ 현재 포즈로 키 추가"))
        {
            Undo.RecordObject(TargetAsset, "Add Key");
            float t = Mathf.Clamp01(_playT / Mathf.Max(0.01f, step.TotalTime));
            var nk = new PoseKey { Time = t, Pose = CaptureCurrentPose() };
            motion.Keys.Add(nk);
            motion.SortKeys();
            EditorUtility.SetDirty(TargetAsset);
            _selectedKey = motion.Keys.IndexOf(nk);
        }

        if (GUILayout.Button("정렬", GUILayout.Width(50)))
        {
            motion.SortKeys();
            EditorUtility.SetDirty(TargetAsset);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("모든 키 왼손 켜기", EditorStyles.miniButton))
            SetAllLeftHand(motion, true);
        if (GUILayout.Button("모든 키 왼손 끄기", EditorStyles.miniButton))
            SetAllLeftHand(motion, false);
        EditorGUILayout.EndHorizontal();
    }

    void SetAllLeftHand(SwingMotion motion, bool on)
    {
        Undo.RecordObject(TargetAsset, "Toggle Left Hand");
        foreach (var k in motion.Keys) k.Pose.UseLeftHand = on;
        EditorUtility.SetDirty(TargetAsset);
        RefreshPreview();
    }

    void SwapKeyPose(SwingMotion motion, int a, int b)
    {
        Undo.RecordObject(TargetAsset, "Reorder Keys");

        var poseA = motion.Keys[a].Pose;
        var easeA = motion.Keys[a].Ease;

        motion.Keys[a].Pose = motion.Keys[b].Pose;
        motion.Keys[a].Ease = motion.Keys[b].Ease;
        motion.Keys[b].Pose = poseA;
        motion.Keys[b].Ease = easeA;

        EditorUtility.SetDirty(TargetAsset);
        RefreshPreview();
    }

    void DrawKeyDetail(PoseKey k)
    {
        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();

        var p = k.Pose;

        // 오른손
        EditorGUILayout.LabelField("오른손", EditorStyles.miniBoldLabel);
        p.RightPos = EditorGUILayout.Vector3Field("위치", p.RightPos);
        p.RightRot = EditorGUILayout.Vector3Field("회전", p.RightRot);

        // 왼손
        EditorGUILayout.Space(2);
        p.UseLeftHand = EditorGUILayout.Toggle("왼손 표시", p.UseLeftHand);

        if (p.UseLeftHand)
        {
            EditorGUI.indentLevel++;
            p.LeftPos = EditorGUILayout.Vector3Field("위치", p.LeftPos);
            p.LeftRot = EditorGUILayout.Vector3Field("회전", p.LeftRot);

            if (GUILayout.Button("오른손 좌우 반전 복사", EditorStyles.miniButton))
            {
                p.LeftPos = new Vector3(-p.RightPos.x, p.RightPos.y, p.RightPos.z);
                p.LeftRot = new Vector3(p.RightRot.x, -p.RightRot.y, -p.RightRot.z);
            }
            EditorGUI.indentLevel--;
        }

        // 카메라
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("카메라", EditorStyles.miniBoldLabel);
        p.CamPos = EditorGUILayout.Vector3Field("위치 오프셋", p.CamPos);
        p.CamRot = EditorGUILayout.Vector3Field("회전 오프셋", p.CamRot);
        p.CamFov = EditorGUILayout.FloatField("FOV 추가", p.CamFov);

        // 무기
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("무기 추가 오프셋", EditorStyles.miniBoldLabel);
        p.WeaponPos = EditorGUILayout.Vector3Field("위치", p.WeaponPos);
        p.WeaponRot = EditorGUILayout.Vector3Field("회전", p.WeaponRot);

        EditorGUILayout.Space(2);
        k.Ease = EditorGUILayout.CurveField("보간 곡선", k.Ease);

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(TargetAsset);
            PreviewPose(p);
        }

        // 손가락
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("손가락", EditorStyles.miniBoldLabel);

        bool prevGrip = p.UseWeaponGrip;
        p.UseWeaponGrip = EditorGUILayout.Toggle(
            new GUIContent("무기 그립 사용", "끄면 이 키에서 손 모양을 직접 지정"),
            p.UseWeaponGrip);

        if (prevGrip != p.UseWeaponGrip)
        {
            EditorUtility.SetDirty(TargetAsset);
            PreviewPose(p);
        }

        if (!p.UseWeaponGrip)
        {
            EditorGUI.indentLevel++;

            Vector3 axis = GetCurlAxis();
            p.RightFingers ??= HandPose.Relaxed(axis);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("펌", EditorStyles.miniButtonLeft))
                SetKeyFingers(p, HandPose.Open(axis));
            if (GUILayout.Button("자연", EditorStyles.miniButtonMid))
                SetKeyFingers(p, HandPose.Relaxed(axis));
            if (GUILayout.Button("쥠", EditorStyles.miniButtonMid))
                SetKeyFingers(p, HandPose.Grip(axis));
            if (GUILayout.Button("주먹", EditorStyles.miniButtonRight))
                SetKeyFingers(p, HandPose.Fist(axis));
            EditorGUILayout.EndHorizontal();

            if (_rig.RightHandRig != null)
            {
                DrawSceneEditToggle(p.RightFingers, _rig.RightHandRig, TargetAsset);
                DrawFingerSliders(p.RightFingers, axis, _rig.RightHandRig, TargetAsset);
            }

            EditorGUI.indentLevel--;
        }

        // 버튼
        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("씬 포즈 저장"))
        {
            Undo.RecordObject(TargetAsset, "Capture Pose");
            k.Pose = CaptureCurrentPose();
            EditorUtility.SetDirty(TargetAsset);
        }

        if (GUILayout.Button("Idle 복사"))
        {
            Undo.RecordObject(TargetAsset, "Copy Idle");
            k.Pose = _rig.GetEffectiveIdle().Clone();
            EditorUtility.SetDirty(TargetAsset);
            PreviewPose(k.Pose);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    void SetKeyFingers(ArmPose p, HandPose hp)
    {
        Undo.RecordObject(TargetAsset, "Set Key Fingers");
        p.RightFingers = hp;
        EditorUtility.SetDirty(TargetAsset);
        PreviewPose(p);
        SceneView.RepaintAll();
    }

    // ─────────── 이벤트 ───────────

    void DrawEventList(ComboStep step)
    {
        step.Events ??= new MotionEvent[0];

        EditorGUILayout.LabelField($"이벤트 ({step.Events.Length})", EditorStyles.boldLabel);

        for (int i = 0; i < step.Events.Length; i++)
        {
            var ev = step.Events[i];
            bool sel = _selectedEvent == i;

            EditorGUILayout.BeginHorizontal();

            bool ns = GUILayout.Toggle(sel, EventIcon(ev.Type),
                EditorStyles.miniButtonLeft, GUILayout.Width(30));
            if (ns != sel) _selectedEvent = ns ? i : -1;

            EditorGUI.BeginChangeCheck();
            ev.Time = EditorGUILayout.Slider(ev.Time, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(TargetAsset);

            GUILayout.Label($"{ev.Time * step.Duration:F2}s",
                EditorStyles.miniLabel, GUILayout.Width(42));

            if (GUILayout.Button("−", EditorStyles.miniButtonRight, GUILayout.Width(22)))
            {
                Undo.RecordObject(TargetAsset, "Delete Event");
                var list = new List<MotionEvent>(step.Events);
                list.RemoveAt(i);
                step.Events = list.ToArray();
                EditorUtility.SetDirty(TargetAsset);
                _selectedEvent = -1;
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();

            if (_selectedEvent == i) DrawEventDetail(ev);
        }

        if (GUILayout.Button("+ 이벤트 추가"))
        {
            Undo.RecordObject(TargetAsset, "Add Event");
            var list = new List<MotionEvent>(step.Events);
            list.Add(new MotionEvent
            {
                Time = Mathf.Clamp01(_playT / Mathf.Max(0.01f, step.Duration))
            });
            step.Events = list.ToArray();
            EditorUtility.SetDirty(TargetAsset);
            _selectedEvent = list.Count - 1;
        }
    }

    string EventIcon(MotionEventType t) => t switch
    {
        MotionEventType.Sound => "♪",
        MotionEventType.Effect => "✦",
        MotionEventType.TrailStart => "▶",
        MotionEventType.TrailEnd => "■",
        MotionEventType.CameraShake => "≈",
        _ => "◆",
    };

    void DrawEventDetail(MotionEvent ev)
    {
        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();

        ev.Type = (MotionEventType)EditorGUILayout.EnumPopup("종류", ev.Type);

        switch (ev.Type)
        {
            case MotionEventType.Sound:
                ev.Clip = (AudioClip)EditorGUILayout.ObjectField("사운드", ev.Clip, typeof(AudioClip), false);
                ev.Volume = EditorGUILayout.Slider("볼륨", ev.Volume, 0f, 1f);
                ev.Pitch = EditorGUILayout.Slider("피치", ev.Pitch, 0.5f, 1.5f);
                break;

            case MotionEventType.Effect:
                ev.Prefab = (GameObject)EditorGUILayout.ObjectField("프리팹", ev.Prefab, typeof(GameObject), false);
                ev.Space = (EventSpace)EditorGUILayout.EnumPopup("기준", ev.Space);
                ev.PositionOffset = EditorGUILayout.Vector3Field("위치", ev.PositionOffset);
                ev.RotationOffset = EditorGUILayout.Vector3Field("회전", ev.RotationOffset);
                ev.Scale = EditorGUILayout.FloatField("크기", ev.Scale);
                ev.Lifetime = EditorGUILayout.FloatField("수명 (초)", ev.Lifetime);
                break;

            case MotionEventType.CameraShake:
                ev.ShakeAmount = EditorGUILayout.FloatField("강도", ev.ShakeAmount);
                break;

            case MotionEventType.Custom:
                ev.Key = EditorGUILayout.TextField("키", ev.Key);
                break;
        }

        EditorGUILayout.BeginHorizontal();
        ev.ShowFirstPerson = EditorGUILayout.Toggle("1인칭", ev.ShowFirstPerson);
        ev.ShowThirdPerson = EditorGUILayout.Toggle("3인칭", ev.ShowThirdPerson);
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(TargetAsset);

        EditorGUI.indentLevel--;
    }

    // ─────────── 도구 ───────────

    void DrawTools()
    {
        if (GUILayout.Button("씬 뷰를 무기에 맞추기")) FrameWeapon();
        if (GUILayout.Button("1인칭 시점으로 보기")) LookThroughEye();

        EditorGUILayout.Space(2);
        if (GUILayout.Button("에셋 선택")) Selection.activeObject = TargetAsset;
        if (_rig != null && GUILayout.Button("FPArmRig 선택"))
            Selection.activeGameObject = _rig.gameObject;

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Idle 포즈를 씬에서 저장") && _rig != null)
        {
            Undo.RecordObject(_rig, "Capture Idle");
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

    // ══════════════════════ 프리뷰 ══════════════════════

    void SpawnWeapon()
    {
        FindRig();
        if (_rig == null) return;

        if (_source == null)
        {
            _rig.CleanWeaponOnly();
            SceneView.RepaintAll();
            return;
        }

        var w = ResolvePreviewWeapon();

        if (w == null) _rig.Mount(null);
        else { _rig.Mount(w); MarkPreviewWeapon(); }

        _playing = false;
        _playT = 0f;
        _selectedKey = -1;

        _rig.ApplyPose(_rig.GetEffectiveIdle());
        SceneView.RepaintAll();
    }

    void MarkPreviewWeapon()
    {
        var t = _rig?.GetWeaponTransform();
        if (t != null) t.gameObject.hideFlags = HideFlags.DontSave;
    }

    void ClearPreview()
    {
        if (_rig == null) return;
        _rig.CleanWeaponOnly();
        SceneView.RepaintAll();
    }

    void RefreshPreview()
    {
        if (_rig == null || _source == null) return;

        var step = _source.GetStep(_comboIndex);
        if (step?.Motion != null && step.Motion.IsValid)
            _rig.ApplySwing(step.Motion, _playT / Mathf.Max(0.01f, step.TotalTime));
        else
            _rig.ApplyPose(_rig.GetEffectiveIdle());

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
            p.RightRot = NormalizeEuler(_rig.RightHand.localRotation.eulerAngles);
        }

        if (_rig.LeftHand != null)
        {
            p.UseLeftHand = _rig.LeftHand.gameObject.activeSelf;
            p.LeftPos = _rig.LeftHand.localPosition;
            p.LeftRot = NormalizeEuler(_rig.LeftHand.localRotation.eulerAngles);
        }

        // 손가락도 현재 상태로
        if (_rig.RightHandRig != null)
        {
            p.UseWeaponGrip = false;
            p.RightFingers = new HandPose();
            _rig.RightHandRig.BakeAll(p.RightFingers);
        }

        return p;
    }

    static Vector3 NormalizeEuler(Vector3 e) => new(N(e.x), N(e.y), N(e.z));
    static float N(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    void CaptureWeaponTransform()
    {
        if (_rig == null || _weapon == null) return;

        Transform t = _rig.GetWeaponTransform();
        if (t == null)
        {
            Debug.LogWarning("[MotionSetup] 손에 무기가 없습니다.");
            return;
        }

        Undo.RecordObject(_weapon, "Capture Weapon Transform");

        if (_firstPerson)
        {
            _weapon.FpPositionOffset = t.localPosition;
            _weapon.FpRotationOffset = NormalizeEuler(t.localRotation.eulerAngles);
        }
        else
        {
            _weapon.PositionOffset = t.localPosition;
            _weapon.RotationOffset = NormalizeEuler(t.localRotation.eulerAngles);
        }
        _weapon.ScaleOverride = t.localScale;

        EditorUtility.SetDirty(_weapon);
        Debug.Log($"[MotionSetup] {_weapon.DisplayName} 무기 배치 저장됨");
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

    void CaptureWeaponIdlePose()
    {
        if (_rig == null || _weapon == null) return;

        Undo.RecordObject(_weapon, "Capture Hand Pose");

        if (_rig.RightHand != null)
        {
            _weapon.RightHandPos = _rig.RightHand.localPosition;
            _weapon.RightHandRot = NormalizeEuler(_rig.RightHand.localRotation.eulerAngles);
        }

        if (_rig.LeftHand != null)
        {
            _weapon.UseLeftHand = _rig.LeftHand.gameObject.activeSelf;
            _weapon.LeftHandPos = _rig.LeftHand.localPosition;
            _weapon.LeftHandRot = NormalizeEuler(_rig.LeftHand.localRotation.eulerAngles);
        }

        _weapon.OverrideIdlePose = true;

        EditorUtility.SetDirty(_weapon);
        Debug.Log($"[MotionSetup] {_weapon.DisplayName} 손 자세 저장됨");
    }

    void CopyRigIdle()
    {
        if (_rig == null || _weapon == null) return;

        Undo.RecordObject(_weapon, "Copy Rig Idle");

        var idle = _rig.IdlePose;
        _weapon.RightHandPos = idle.RightPos;
        _weapon.RightHandRot = idle.RightRot;
        _weapon.UseLeftHand = idle.UseLeftHand;
        _weapon.LeftHandPos = idle.LeftPos;
        _weapon.LeftHandRot = idle.LeftRot;

        EditorUtility.SetDirty(_weapon);
        RefreshPreview();
    }

    void FrameWeapon()
    {
        if (_rig == null) return;

        Transform t = _rig.GetWeaponTransform();
        if (t == null) t = _rig.RightHand;
        if (t == null) return;

        Selection.activeGameObject = t.gameObject;
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

    // ══════════════════════ 재생 루프 ══════════════════════

    void OnEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTime);
        _lastTime = now;

        if (Application.isPlaying) return;
        if (!_playing || _source == null || _rig == null) return;

        var step = _source.GetStep(_comboIndex);
        if (step == null) { _playing = false; return; }

        _playT += dt * _playSpeed;

        if (_playT >= step.TotalTime)
        {
            _playT = 0f;

            if (_source.ComboLength > 1)
            {
                _comboIndex++;
                if (_comboIndex >= _source.ComboLength)
                {
                    _comboIndex = 0;
                    if (!_loop) { StopPlay(); return; }
                }
                step = _source.GetStep(_comboIndex);
            }
            else if (!_loop) { StopPlay(); return; }
        }

        _rig.ApplySwing(step.Motion, _playT / Mathf.Max(0.01f, step.TotalTime));
        SceneView.RepaintAll();
        Repaint();
    }

    void StopPlay()
    {
        _playing = false;
        _rig.ApplyPose(_rig.GetEffectiveIdle());
        SceneView.RepaintAll();
        Repaint();
    }

    // ══════════════════════ 씬 시각화 ══════════════════════

    void OnSceneGUI(SceneView sv)
    {
        if (Application.isPlaying) return;
        if (_source == null || _rig == null) return;

        var step = _source.GetStep(_comboIndex);
        if (step == null) return;

        Transform eye = GetEyeTransform();
        if (eye == null) return;

        float total = Mathf.Max(0.01f, step.TotalTime);
        float p = Mathf.Clamp01(_playT / total);
        bool inActive = _playT >= step.WindUp &&
                        _playT <= step.WindUp + step.ActiveWindow;

        var hitSrc = ResolvePreviewWeapon();

        if (_showHitbox && hitSrc != null) DrawHitArc(eye, step, hitSrc, inActive);
        if (_showTrail) DrawTipTrail(inActive);

        DrawInfoLabel(step, p, inActive, hitSrc);
    }

    Transform GetEyeTransform()
    {
        if (_rig == null) return null;
        Transform arm = _rig.ArmRoot != null ? _rig.ArmRoot : _rig.transform;
        return arm.parent != null ? arm.parent : arm;
    }

    void DrawHitArc(Transform eye, ComboStep step, EquipmentData w, bool inActive)
    {
        if (_showOnlyDuringActive && !inActive) return;

        step.ResolveHitShape(w, out var mode, out float range,
                             out float angle, out float height, out float minRange);

        Vector3 o = eye.position - Vector3.up * 0.7f;
        Vector3 f = new Vector3(eye.forward.x, 0f, eye.forward.z).normalized;
        if (f.sqrMagnitude < 0.001f) f = Vector3.forward;

        Color c = inActive
            ? new Color(1f, 0.25f, 0.15f, 1f)
            : new Color(0.5f, 0.55f, 0.6f, 0.6f);

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

            if (height < 50f)
            {
                Handles.color = new Color(c.r, c.g, c.b, 0.3f);
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

    void DrawInfoLabel(ComboStep step, float p, bool inActive, EquipmentData w)
    {
        Handles.BeginGUI();

        var rect = new Rect(10, 110, 250, 92);
        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.65f));

        GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

        GUILayout.Label($"{_source.SourceName}  —  {step.Name}", EditorStyles.whiteBoldLabel);
        GUILayout.Label($"진행 {p * 100f:F0}%   {(inActive ? "◆ 판정 중" : "－")}",
            inActive ? EditorStyles.whiteBoldLabel : EditorStyles.whiteMiniLabel);

        if (w != null)
        {
            step.ResolveHitShape(w, out var mode, out float range,
                                 out float angle, out _, out _);

            GUILayout.Label(mode == WeaponHitMode.ForwardArc
                ? $"부채꼴 {range:F2}m / {angle:F0}°"
                : $"구체 {range:F2}m", EditorStyles.whiteMiniLabel);
        }

        GUILayout.Label($"피해 ×{step.DamageMultiplier:F2}   대상 {step.MaxTargets}",
            EditorStyles.whiteMiniLabel);

        GUILayout.EndArea();
        Handles.EndGUI();
    }
}