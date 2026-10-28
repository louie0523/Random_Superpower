using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 모션 편집 창 (Tools → Motion Setup).
///   탭: 무기·손 / 원거리 / 모션 / 도구
///   모션 탭: 타임라인 하나에 판정 구간 · 키프레임(◆) · 이벤트를 같이 표시하고 드래그로 편집.
///            클릭 = 선택, 오른쪽 클릭 = 추가·삭제 메뉴, Delete = 삭제, Space = 재생
///   키프레임에 몸(Body) · 머리(Head) 채널 — FPArmRig의 Body/Head에 적용되어 다른 사람에게 보임
/// </summary>
public class WeaponSetupWindow : EditorWindow
{
    enum Tab { Weapon, Ranged, Motion, Tools }
    enum Sel { None, Key, Event }
    enum Drag { None, HitStart, HitEnd, Cancel, Key, Event, Scrub }

    // ── 대상 ──
    private Object _target;
    private IMotionSource _source;
    private EquipmentData _weapon;
    private EquipmentData _previewWeaponOverride;
    private FPArmRig _rig;

    // ── 상태 ──
    private Tab _tab = Tab.Motion;
    private bool _firstPerson = true;
    private int _comboIndex;
    private Sel _sel;
    private int _selIndex = -1;
    private SwingPresets.Kind _presetKind = SwingPresets.Kind.DiagonalSlash;

    // ── 재생 ──
    private bool _playing;
    private bool _playAll;
    private float _playT;
    private float _playSpeed = 1f;
    private bool _loop;
    private double _lastTime;

    // ── 타임라인 드래그 ──
    private Drag _drag;
    private PoseKey _dragKey;
    private MotionEvent _dragEvent;

    // ── UI ──
    private Vector2 _scroll;
    private readonly Dictionary<string, bool> _folds = new();

    // ── 씬 표시 ──
    private bool _showHitbox = true;
    private bool _showTrail = true;
    private bool _showOnlyDuringActive;
    private readonly List<Vector3> _tipTrail = new();
    private float _previewDraw;
    private float _previewRecoil;
    private bool _forceProjectile;
    private float _autoBodyStrength = 0.3f;

    private static readonly string[] FingerNames = { "엄지", "검지", "중지", "약지" };
    private static readonly float[] JointWeights = { 1f, 0.95f, 0.85f };

    // ── 색 ──
    static readonly Color CBg = new(0.13f, 0.13f, 0.14f);
    static readonly Color CLane = new(0.18f, 0.18f, 0.19f);
    static readonly Color CWindUp = new(0.26f, 0.31f, 0.45f);
    static readonly Color CActive = new(0.80f, 0.26f, 0.18f);
    static readonly Color CRecovery = new(0.22f, 0.22f, 0.24f);
    static readonly Color CHandle = new(1f, 0.9f, 0.3f);
    static readonly Color CCancel = new(0.4f, 0.95f, 0.5f);
    static readonly Color CKey = new(0.92f, 0.92f, 0.92f);
    static readonly Color CSelected = new(0.3f, 0.85f, 1f);
    static readonly Color CPlayhead = Color.cyan;

    // ── 이벤트 종류별 표시 — 새 종류를 추가하면 여기와 DrawEventFields에 한 줄씩 ──
    static readonly Dictionary<MotionEventType, (string icon, string label, Color color)> EventMeta = new()
    {
        { MotionEventType.Sound,        ("♪", "사운드",       new Color(0.45f, 0.80f, 1.00f)) },
        { MotionEventType.RandomSound,  ("♫", "랜덤 사운드",  new Color(0.35f, 0.65f, 1.00f)) },
        { MotionEventType.Effect,       ("✦", "이펙트",       new Color(1.00f, 0.75f, 0.30f)) },
        { MotionEventType.GroundEffect, ("▼", "지면 이펙트",  new Color(0.85f, 0.60f, 0.30f)) },
        { MotionEventType.TrailStart,   ("▶", "트레일 켜기",  new Color(0.60f, 1.00f, 0.75f)) },
        { MotionEventType.TrailEnd,     ("■", "트레일 끄기",  new Color(0.45f, 0.75f, 0.55f)) },
        { MotionEventType.CameraShake,  ("≈", "화면 흔들림",  new Color(1.00f, 0.50f, 0.50f)) },
        { MotionEventType.ScreenFlash,  ("□", "화면 번쩍",    new Color(1.00f, 1.00f, 0.80f)) },
        { MotionEventType.FovKick,      ("○", "시야각",       new Color(0.80f, 0.60f, 1.00f)) },
        { MotionEventType.LightFlash,   ("☆", "순간 조명",    new Color(1.00f, 0.95f, 0.50f)) },
        { MotionEventType.Custom,       ("◆", "커스텀",       new Color(0.75f, 0.75f, 0.75f)) },
        { MotionEventType.Vfx,          ("✸", "VFX",          new Color(1.00f, 0.55f, 0.90f)) },
    };

    static (string icon, string label, Color color) Meta(MotionEventType t) =>
        EventMeta.TryGetValue(t, out var m) ? m : ("◆", t.ToString(), Color.white);

    /// <summary>본인 화면에서만 의미 있는 이벤트 (시점 토글을 숨김)</summary>
    static bool LocalOnly(MotionEventType t) =>
        t == MotionEventType.CameraShake || t == MotionEventType.ScreenFlash || t == MotionEventType.FovKick;

    [MenuItem("Tools/Motion Setup")]
    static void Open()
    {
        var w = GetWindow<WeaponSetupWindow>("Motion Setup");
        w.minSize = new Vector2(400, 560);
    }

    void OnEnable()
    {
        FindRig();
        _rig?.CaptureBodyRest();

        EditorApplication.update += OnEditorUpdate;
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndo;
        _lastTime = EditorApplication.timeSinceStartup;
    }

    void OnDisable()
    {
        ClearEffectPreviews();
        EditorApplication.update -= OnEditorUpdate;
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndo;

        HandRigEditor.EditTarget = null;
        HandRigEditor.OnPoseChanged = null;

        if (_rig != null)
        {
            _rig.RestoreBodyRest();
            _rig.CleanWeaponOnly();
        }
    }

    void OnUndo() { RefreshPreview(); Repaint(); }

    void FindRig()
    {
        if (_rig == null) _rig = FindFirstObjectByType<FPArmRig>();
    }

    EquipmentData ResolvePreviewWeapon() =>
        _previewWeaponOverride != null ? _previewWeaponOverride : _source?.PreviewWeapon;

    ComboStep CurrentStep => _source != null && _source.ComboLength > 0
        ? _source.GetStep(Mathf.Clamp(_comboIndex, 0, _source.ComboLength - 1))
        : null;

    // ══════════════════════ 공통 도우미 ══════════════════════

    /// <summary>접히는 구역 제목. 상태는 창이 열려 있는 동안 기억</summary>
    bool Fold(string title, bool defaultOpen = true)
    {
        if (!_folds.TryGetValue(title, out bool open)) open = defaultOpen;
        open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
        EditorGUILayout.EndFoldoutHeaderGroup();
        _folds[title] = open;
        return open;
    }

    static IDisposable Box() => new EditorGUILayout.VerticalScope(EditorStyles.helpBox);

    static void Hint(string s) => EditorGUILayout.LabelField(s, EditorStyles.wordWrappedMiniLabel);

    /// <summary>메뉴 콜백 등 OnGUI 밖에서 에셋을 바꿀 때</summary>
    void Modify(string undoName, Action action)
    {
        if (_target == null) return;
        Undo.RecordObject(_target, undoName);
        action();
        EditorUtility.SetDirty(_target);
        RefreshPreview();
        Repaint();
    }

    static Vector3 NormalizeEuler(Vector3 e) => new(N(e.x), N(e.y), N(e.z));
    static float N(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    // ══════════════════════ GUI ══════════════════════

    void OnGUI()
    {
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 편집할 수 없습니다.\nPlay를 중지한 뒤 사용하세요.", MessageType.Warning);
            return;
        }

        EditorGUI.indentLevel = 0;
        EditorGUIUtility.wideMode = position.width > 360f;
        EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.32f, 90f, 150f);

        // 입력이 있는 이벤트마다 기록 — 바뀐 게 있을 때만 실제 Undo 항목이 생김
        var ev = Event.current;
        if (_target != null && ev.type != EventType.Layout && ev.type != EventType.Repaint)
            Undo.RecordObject(_target, "Motion Setup");

        DrawHeader();

        if (_source == null || _rig == null) return;

        DrawTabs();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(4);

        switch (_tab)
        {
            case Tab.Weapon: DrawWeaponTab(); break;
            case Tab.Ranged: DrawRangedTab(); break;
            case Tab.Motion: DrawMotionTab(); break;
            case Tab.Tools: DrawToolsTab(); break;
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.EndScrollView();

        if (GUI.changed && _target != null) EditorUtility.SetDirty(_target);
    }

    // ─────────── 머리말 (대상) ───────────

    void DrawHeader()
    {
        using (Box())
        {
            EditorGUI.BeginChangeCheck();
            _target = EditorGUILayout.ObjectField("무기 / 능력 모션", _target, typeof(Object), false);
            if (EditorGUI.EndChangeCheck()) OnTargetChanged();

            EditorGUI.BeginChangeCheck();
            _rig = (FPArmRig)EditorGUILayout.ObjectField("팔 리그", _rig, typeof(FPArmRig), true);
            if (EditorGUI.EndChangeCheck() && _rig != null) _rig.CaptureBodyRest();

            if (_rig == null)
            {
                EditorGUILayout.HelpBox(
                    "씬에 FPArmRig가 없습니다.\n\n" +
                    "PreviewCamera\n └ CamPivot\n    └ ArmRoot (FPArmRig)\n       ├ RightHand\n       └ LeftHand",
                    MessageType.Warning);
                if (GUILayout.Button("씬에서 찾기")) { FindRig(); _rig?.CaptureBodyRest(); }
                return;
            }

            if (_target == null)
            {
                EditorGUILayout.HelpBox("EquipmentData(무기) 또는 AbilityMotionData(능력 모션)를 넣으세요.", MessageType.Info);
                return;
            }

            if (_source == null)
            {
                EditorGUILayout.HelpBox($"{_target.name}은(는) 모션 소스가 아닙니다.", MessageType.Error);
                return;
            }

            if (_weapon == null)
            {
                EditorGUI.BeginChangeCheck();
                _previewWeaponOverride = (EquipmentData)EditorGUILayout.ObjectField(
                    new GUIContent("테스트 무기", "미리보기 전용. 에셋에 저장되지 않음"),
                    _previewWeaponOverride, typeof(EquipmentData), false);
                if (EditorGUI.EndChangeCheck()) SpawnWeapon();
            }

            string kind = _weapon != null ? "무기" : "능력 모션";
            string body = _rig.Body != null ? _rig.Body.name : "없음";
            string head = _rig.Head != null ? _rig.Head.name : "없음";
            EditorGUILayout.LabelField($"{kind}  ·  몸 {body}  ·  머리 {head}", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("다시 로드", EditorStyles.miniButtonLeft)) SpawnWeapon();
                if (GUILayout.Button("무기 치우기", EditorStyles.miniButtonMid)) ClearPreview();
                if (GUILayout.Button("손 재생성", EditorStyles.miniButtonMid)) { _rig.RebuildHands(); SpawnWeapon(); }
                if (GUILayout.Button("전부 정리", EditorStyles.miniButtonRight)) { _rig.CleanAll(); SceneView.RepaintAll(); }
            }
        }
    }

    void OnTargetChanged()
    {
        _source = _target as IMotionSource;
        _weapon = _target as EquipmentData;

        _comboIndex = 0;
        ClearSelection();
        _playT = 0f;
        _playing = false;

        if (_weapon == null && (_tab == Tab.Weapon || _tab == Tab.Ranged)) _tab = Tab.Motion;

        SpawnWeapon();
    }

    void DrawTabs()
    {
        var tabs = new List<Tab>();
        var names = new List<string>();

        if (_weapon != null) { tabs.Add(Tab.Weapon); names.Add("무기 · 손"); }
        if (_weapon != null && _weapon.Class == WeaponClass.Ranged) { tabs.Add(Tab.Ranged); names.Add("원거리"); }
        tabs.Add(Tab.Motion); names.Add("모션");
        tabs.Add(Tab.Tools); names.Add("도구");

        int cur = Mathf.Max(0, tabs.IndexOf(_tab));
        if (!tabs.Contains(_tab)) _tab = tabs[cur];

        int next = GUILayout.Toolbar(cur, names.ToArray(), GUILayout.Height(24));
        if (next != cur) _tab = tabs[next];
    }

    // ══════════════════════ 무기 · 손 탭 ══════════════════════

    void DrawWeaponTab()
    {
        // ── 한 번에 저장 ──
        using (Box())
        {
            if (GUILayout.Button(new GUIContent("씬 그립 한 번에 저장",
                "씬에서 맞춰 둔 걸 전부 무기 설정으로: 무기 위치 · 회전 · 크기(0번) + 추가 파츠 위치 + 손 기본 자세 + 손가락 그립(오른손 · 왼손)\n" +
                "둥둥 흔들림은 빼고 저장. Ctrl+Z로 되돌릴 수 있음"), GUILayout.Height(26)))
                CaptureAllGrip();
        }

        // ── 배치 ──
        if (Fold("무기 배치"))
            using (Box())
            {
                EditorGUI.BeginChangeCheck();
                var grip = (GripType)EditorGUILayout.EnumPopup("잡는 손", _weapon.Grip);
                var cls = (WeaponClass)EditorGUILayout.EnumPopup("무기 계열", _weapon.Class);
                if (EditorGUI.EndChangeCheck())
                {
                    bool gripChanged = grip != _weapon.Grip;
                    _weapon.Grip = grip;
                    _weapon.Class = cls;
                    if (grip == GripType.TwoHand) _weapon.UseLeftHand = true;
                    if (gripChanged) SpawnWeapon();
                }

                EditorGUI.BeginChangeCheck();
                _firstPerson = EditorGUILayout.Toggle("1인칭 오프셋 편집", _firstPerson);

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
                if (EditorGUI.EndChangeCheck()) RefreshPreview();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("크기 균일", EditorStyles.miniButtonLeft))
                    {
                        float s = _weapon.ScaleOverride.x;
                        _weapon.ScaleOverride = new Vector3(s, s, s);
                        RefreshPreview();
                    }
                    if (GUILayout.Button("크기 1", EditorStyles.miniButtonMid))
                    {
                        _weapon.ScaleOverride = Vector3.one;
                        RefreshPreview();
                    }
                    if (GUILayout.Button("씬 위치 저장", EditorStyles.miniButtonMid)) CaptureWeaponTransform();
                    if (GUILayout.Button("초기화", EditorStyles.miniButtonRight)) ResetWeaponTransform();
                }
                Hint("씬에서 무기를 직접 옮긴 뒤 '씬 위치 저장'을 누르면 편합니다.");
            }

        // ── 둥둥 (떠 있는 무기) ──
        if (Fold("둥둥 (떠 있는 무기)", _weapon.FloatFree || _weapon.UseFloat))
            using (Box()) DrawWeaponFloat();

        // ── 파츠 (모델 여러 개) ──
        if (Fold("파츠 (모델 여러 개)", _weapon.PartCount > 1 || _weapon.MainParent != WeaponParent.Default))
            using (Box()) DrawWeaponParts();

        // ── 손 자세 ──
        if (Fold("손 기본 자세"))
            using (Box())
            {
                EditorGUI.BeginChangeCheck();
                _weapon.OverrideIdlePose = EditorGUILayout.Toggle(
                    new GUIContent("무기 전용 자세", "끄면 FPArmRig의 IdlePose 사용"), _weapon.OverrideIdlePose);

                if (_weapon.OverrideIdlePose)
                {
                    _weapon.RightHandPos = EditorGUILayout.Vector3Field("오른손 위치", _weapon.RightHandPos);
                    _weapon.RightHandRot = EditorGUILayout.Vector3Field("오른손 회전", _weapon.RightHandRot);

                    _weapon.UseLeftHand = EditorGUILayout.Toggle("왼손 표시", _weapon.UseLeftHand);
                    if (_weapon.UseLeftHand)
                    {
                        _weapon.LeftHandPos = EditorGUILayout.Vector3Field("왼손 위치", _weapon.LeftHandPos);
                        _weapon.LeftHandRot = EditorGUILayout.Vector3Field("왼손 회전", _weapon.LeftHandRot);
                    }
                }
                if (EditorGUI.EndChangeCheck()) RefreshPreview();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("씬 손 자세 저장", EditorStyles.miniButtonLeft)) CaptureWeaponIdlePose();
                    if (GUILayout.Button("리그 Idle 복사", EditorStyles.miniButtonMid)) CopyRigIdle();
                    if (GUILayout.Button("리그 기본값 전부", EditorStyles.miniButtonRight)) PullFromRigDefaults();
                }
            }

        // ── 손가락 ──
        if (Fold("손가락 그립"))
            using (Box())
            {
                Vector3 axis = GetCurlAxis();

                _weapon.RightGrip ??= HandPose.Grip(axis);
                DrawGripEditor("오른손", ref _weapon.RightGrip, axis, _rig.RightHandRig, _weapon);

                if (_weapon.Grip == GripType.TwoHand || _weapon.UseLeftHand)
                {
                    EditorGUILayout.Space(4);
                    _weapon.LeftGrip ??= HandPose.Relaxed(axis);
                    DrawGripEditor("왼손", ref _weapon.LeftGrip, axis, _rig.LeftHandRig, _weapon);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("오른손 → 왼손", EditorStyles.miniButtonLeft)
                            && _rig.RightHandRig != null && _rig.LeftHandRig != null)
                        {
                            _rig.LeftHandRig.CopyBoneRotationsFrom(_rig.RightHandRig);
                            _weapon.LeftGrip ??= new HandPose();
                            _rig.LeftHandRig.BakeAll(_weapon.LeftGrip);
                            RefreshPreview();
                        }
                        if (GUILayout.Button("왼손 → 오른손", EditorStyles.miniButtonRight)
                            && _rig.RightHandRig != null && _rig.LeftHandRig != null)
                        {
                            _rig.RightHandRig.CopyBoneRotationsFrom(_rig.LeftHandRig);
                            _weapon.RightGrip ??= new HandPose();
                            _rig.RightHandRig.BakeAll(_weapon.RightGrip);
                            RefreshPreview();
                        }
                    }
                }
            }

        // ── 판정 ──
        if (Fold("기본 판정 범위"))
            using (Box())
            {
                EditorGUI.BeginChangeCheck();
                _weapon.HitMode = (WeaponHitMode)EditorGUILayout.EnumPopup("판정 방식", _weapon.HitMode);
                _weapon.Range = EditorGUILayout.FloatField("사거리 (m)", _weapon.Range);
                if (_weapon.HitMode == WeaponHitMode.ForwardArc)
                    _weapon.HitAngle = EditorGUILayout.Slider("각도 (°)", _weapon.HitAngle, 20f, 360f);
                _weapon.HitHeight = EditorGUILayout.FloatField("수직 허용 (m)", _weapon.HitHeight);
                _weapon.MinRange = EditorGUILayout.FloatField("최소 사거리", _weapon.MinRange);
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
                Hint("씬 뷰에 부채꼴로 표시됩니다. 타격마다 다르게 하려면 모션 탭 → 타격 설정 → 판정 범위 개별 설정.");
            }
    }

    // ══════════════════════ 원거리 탭 ══════════════════════

    void DrawRangedTab()
    {
        if (Fold("발사"))
            using (Box())
            {
                _weapon.RangedKind = (RangedType)EditorGUILayout.EnumPopup("발사 방식", _weapon.RangedKind);
                _weapon.ProjectilePrefab = (GameObject)EditorGUILayout.ObjectField(
                    "투사체 프리팹", _weapon.ProjectilePrefab, typeof(GameObject), false);
                _weapon.ProjectileLifetime = EditorGUILayout.FloatField("투사체 수명 (초)", _weapon.ProjectileLifetime);

                var wr = _rig.CurrentWeaponRig;
                if (wr == null)
                {
                    EditorGUILayout.HelpBox(
                        "무기 모델 프리팹에 WeaponRig가 없습니다.\n프리팹 루트에 추가한 뒤 Muzzle · StringBone을 지정하세요.",
                        MessageType.Warning);
                    if (_weapon.ModelPrefab != null && GUILayout.Button("모델 프리팹 열기"))
                        AssetDatabase.OpenAsset(_weapon.ModelPrefab);
                }
                else
                {
                    EditorGUILayout.LabelField("발사 지점", wr.Muzzle != null ? wr.Muzzle.name : "없음 (Tip 사용)", EditorStyles.miniLabel);
                    if (_weapon.RangedKind == RangedType.Bow)
                        EditorGUILayout.LabelField("시위 본", wr.StringBone != null ? wr.StringBone.name : "없음", EditorStyles.miniLabel);
                }
            }

        bool charge = _weapon.RangedKind == RangedType.Bow || _weapon.RangedKind == RangedType.Staff;

        if (charge && Fold("차징"))
            using (Box()) DrawChargeSection();

        if (_weapon.RangedKind == RangedType.Bow && Fold("시위"))
            using (Box()) DrawStringSection();

        if (_weapon.RangedKind == RangedType.Gun && Fold("총"))
            using (Box()) DrawGunSection();

        if (Fold("투사체 미리보기"))
            using (Box()) DrawProjectilePreviewSection();
    }

    void DrawChargeSection()
    {
        _weapon.MinDrawTime = EditorGUILayout.FloatField("최소 차징 (초)", _weapon.MinDrawTime);
        _weapon.MaxDrawTime = EditorGUILayout.FloatField("최대 차징 (초)", _weapon.MaxDrawTime);
        _weapon.AttackSpeedScale = EditorGUILayout.Slider(new GUIContent("공속 반영 비율",
            "차징 속도에 공속을 얼마나 반영할지. 1 = 그대로"), _weapon.AttackSpeedScale, 0f, 1f);

        using (new EditorGUILayout.HorizontalScope())
        {
            _weapon.MinDrawDamageMul = EditorGUILayout.FloatField("피해× 최소", _weapon.MinDrawDamageMul);
            _weapon.MaxDrawDamageMul = EditorGUILayout.FloatField("최대", _weapon.MaxDrawDamageMul);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _weapon.MinDrawSpeed = EditorGUILayout.FloatField("속도 최소", _weapon.MinDrawSpeed);
            _weapon.MaxDrawSpeed = EditorGUILayout.FloatField("최대", _weapon.MaxDrawSpeed);
        }

        _weapon.ProjectileGravity = EditorGUILayout.FloatField("투사체 중력", _weapon.ProjectileGravity);

        using (new EditorGUILayout.HorizontalScope())
        {
            _weapon.PierceCount = EditorGUILayout.IntSlider("관통", _weapon.PierceCount, 0, 1);
            using (new EditorGUI.DisabledScope(_weapon.PierceCount == 0))
                _weapon.PierceDamageFalloff = EditorGUILayout.Slider("감쇠", _weapon.PierceDamageFalloff, 0f, 1f);
        }

        EditorGUILayout.Space(4);
        DrawDrawPreview();

        EditorGUILayout.Space(4);
        _weapon.UseChargePose = EditorGUILayout.Toggle("차징 자세 사용", _weapon.UseChargePose);
        if (!_weapon.UseChargePose) return;

        _weapon.ChargeCurve = EditorGUILayout.CurveField("보간 곡선", _weapon.ChargeCurve);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_previewDraw < 0.999f))
                if (GUILayout.Button(new GUIContent("씬 자세 → 차징 자세", "차징 미리보기를 1로 둔 상태에서 저장"), GUILayout.Height(22)))
                {
                    _weapon.ChargePose = CaptureCurrentPose();
                    ApplyPreviewDraw();
                }
            if (GUILayout.Button("기본 자세 복사", GUILayout.Width(100), GUILayout.Height(22)))
                _weapon.ChargePose = _rig.GetEffectiveIdle().Clone();
        }

        if (_weapon.ChargePose != null)
        {
            EditorGUI.BeginChangeCheck();
            DrawPoseValues(_weapon.ChargePose, false);
            if (EditorGUI.EndChangeCheck()) ApplyPreviewDraw();

            if (_rig.RightHandRig != null)
            {
                _weapon.ChargePose.UseWeaponGrip = EditorGUILayout.Toggle(
                    new GUIContent("무기 그립 사용", "끄면 차징 중 손 모양을 따로 지정"), _weapon.ChargePose.UseWeaponGrip);

                if (!_weapon.ChargePose.UseWeaponGrip)
                {
                    Vector3 ax = GetCurlAxis();
                    _weapon.ChargePose.RightFingers ??= HandPose.Grip(ax);
                    DrawSceneEditToggle(_weapon.ChargePose.RightFingers, _rig.RightHandRig, _weapon);
                    DrawFingerSliders(_weapon.ChargePose.RightFingers, ax, _rig.RightHandRig, _weapon);
                }
            }
        }
    }

    void DrawDrawPreview()
    {
        EditorGUI.BeginChangeCheck();
        _previewDraw = EditorGUILayout.Slider("차징 미리보기", _previewDraw, 0f, 1f);
        if (EditorGUI.EndChangeCheck()) { _rangedPlay = RangedPlay.None; ApplyPreviewDraw(); }

        using (new EditorGUILayout.HorizontalScope())
        {
            bool on = _rangedPlay == RangedPlay.Charge;
            if (GUILayout.Button(on ? "■ 정지" : "▶ 차징 재생 (당김 → 놓기 반복)", GUILayout.Height(22)))
                StartRangedPlay(on ? RangedPlay.None : RangedPlay.Charge);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (_weapon.RangedKind == RangedType.Bow && GUILayout.Button("씬 시위를 방향·거리로 읽기"))
                CaptureStringPull();
            if (GUILayout.Button("놓기", GUILayout.Width(50)))
            {
                _previewDraw = 0f;
                ApplyPreviewDraw();
            }
        }

        if (_weapon.RangedKind == RangedType.Bow && !_rig.HasBowString)
            EditorGUILayout.HelpBox("시위 본이 없습니다. 무기 모델 프리팹의 WeaponRig에서 StringBone을 지정하세요.", MessageType.Warning);
    }

    void DrawStringSection()
    {
        _weapon.StringPullDir = EditorGUILayout.Vector3Field("당기는 방향", _weapon.StringPullDir);
        _weapon.StringPullDistance = EditorGUILayout.FloatField("당기는 거리 (m)", _weapon.StringPullDistance);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("-Z (뒤)", EditorStyles.miniButtonLeft)) _weapon.StringPullDir = Vector3.back;
            if (GUILayout.Button("-X", EditorStyles.miniButtonMid)) _weapon.StringPullDir = Vector3.left;
            if (GUILayout.Button("-Y", EditorStyles.miniButtonRight)) _weapon.StringPullDir = Vector3.down;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("시위 잡는 손 (왼손)", EditorStyles.miniBoldLabel);
        _weapon.StringHandOffset = EditorGUILayout.Vector3Field("손 오프셋", _weapon.StringHandOffset);
        _weapon.StringHandRot = EditorGUILayout.Vector3Field("손 회전", _weapon.StringHandRot);
        _weapon.StringHandBlendTime = EditorGUILayout.FloatField("블렌드 시간 (초)", _weapon.StringHandBlendTime);

        if (GUILayout.Button("씬 왼손 위치를 시위 손으로 저장")) CaptureStringHand();
    }

    void DrawGunSection()
    {
        _weapon.FireMode = (GunFireMode)EditorGUILayout.EnumPopup("판정 방식", _weapon.FireMode);
        _weapon.MaxAmmo = EditorGUILayout.IntField("탄약", _weapon.MaxAmmo);
        _weapon.RoundsPerMinute = EditorGUILayout.FloatField("분당 발사", _weapon.RoundsPerMinute);
        _weapon.FullAuto = EditorGUILayout.Toggle("연사", _weapon.FullAuto);
        _weapon.SpreadAngle = EditorGUILayout.Slider(new GUIContent("탄퍼짐 (°)", "0 = 조준점 정확히"), _weapon.SpreadAngle, 0f, 15f);
        _weapon.AttackSpeedScale = EditorGUILayout.Slider(new GUIContent("공속 반영 비율",
            "1 = 공속 증가분 그대로, 0.5 = 절반만, 0 = 공속 무시 (RPM 고정)"), _weapon.AttackSpeedScale, 0f, 1f);

        if (_weapon.FireMode == GunFireMode.Hitscan)
        {
            _weapon.HitscanRange = EditorGUILayout.FloatField("사거리 (m)", _weapon.HitscanRange);
            _weapon.TracerPrefab = (GameObject)EditorGUILayout.ObjectField("예광탄", _weapon.TracerPrefab, typeof(GameObject), false);
        }
        else
        {
            _weapon.ProjectileSpeed = EditorGUILayout.FloatField("투사체 속도", _weapon.ProjectileSpeed);
            _weapon.ProjectileGravity = EditorGUILayout.FloatField("투사체 중력", _weapon.ProjectileGravity);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("카메라 반동", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _weapon.RecoilVertical = EditorGUILayout.FloatField("수직", _weapon.RecoilVertical);
            _weapon.RecoilHorizontal = EditorGUILayout.FloatField("수평", _weapon.RecoilHorizontal);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _weapon.RecoilRamp = EditorGUILayout.FloatField("누적 배율", _weapon.RecoilRamp);
            _weapon.RecoilMaxMul = EditorGUILayout.FloatField("누적 상한", _weapon.RecoilMaxMul);
        }

        EditorGUILayout.Space(4);
        _weapon.UseRecoilPose = EditorGUILayout.Toggle("팔 반동 자세 사용", _weapon.UseRecoilPose);
        if (!_weapon.UseRecoilPose) return;

        _weapon.RecoilRiseTime = EditorGUILayout.FloatField("올라가는 시간", _weapon.RecoilRiseTime);
        _weapon.RecoilFallTime = EditorGUILayout.FloatField("돌아오는 시간", _weapon.RecoilFallTime);
        _weapon.RecoilPoseMaxStack = EditorGUILayout.Slider("연사 누적 상한", _weapon.RecoilPoseMaxStack, 1f, 3f);
        _weapon.RecoilSwayAmount = EditorGUILayout.FloatField("흔들림 (수평)", _weapon.RecoilSwayAmount);
        _weapon.RecoilSwayVertical = EditorGUILayout.FloatField("흔들림 (수직)", _weapon.RecoilSwayVertical);
        _weapon.RecoilSwaySpeed = EditorGUILayout.FloatField("흔들림 속도", _weapon.RecoilSwaySpeed);

        EditorGUI.BeginChangeCheck();
        _previewRecoil = EditorGUILayout.Slider("반동 미리보기", _previewRecoil, 0f, 1f);
        if (EditorGUI.EndChangeCheck()) { _rangedPlay = RangedPlay.None; ApplyPreviewRecoil(); }

        using (new EditorGUILayout.HorizontalScope())
        {
            bool on = _rangedPlay == RangedPlay.Recoil;
            if (GUILayout.Button(on ? "■ 정지" : "▶ 반동 재생 (발사)", GUILayout.Height(22)))
                StartRangedPlay(on ? RangedPlay.None : RangedPlay.Recoil);
            _recoilPlayShots = EditorGUILayout.IntSlider(new GUIContent("발 수", "연사 미리보기 — 분당 발사 · 누적 배율대로"), _recoilPlayShots, 1, 20);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_previewRecoil < 0.999f))
                if (GUILayout.Button(new GUIContent("씬 자세 → 반동 자세", "반동 미리보기를 1로 둔 상태에서 저장"), GUILayout.Height(22)))
                {
                    _weapon.RecoilPose = CaptureCurrentPose();
                    ApplyPreviewRecoil();
                }
            if (GUILayout.Button("기본 자세 복사", GUILayout.Width(100), GUILayout.Height(22)))
                _weapon.RecoilPose = _rig.GetEffectiveIdle().Clone();
        }

        if (_weapon.RecoilPose != null)
        {
            EditorGUI.BeginChangeCheck();
            DrawPoseValues(_weapon.RecoilPose, false);
            if (EditorGUI.EndChangeCheck()) ApplyPreviewRecoil();
        }
    }

    void DrawProjectilePreviewSection()
    {
        var wr = _rig.CurrentWeaponRig;
        if (wr == null)
        {
            Hint("무기 모델에 WeaponRig가 없습니다.");
            return;
        }

        EditorGUILayout.LabelField("모델", wr.ProjectileVisual != null ? wr.ProjectileVisual.name : "없음");
        EditorGUILayout.LabelField("표시 방식", wr.PreviewMode switch
        {
            ProjectilePreviewMode.WhileDrawing => "당기는 동안 (활)",
            ProjectilePreviewMode.WhileLoaded => "탄약이 있는 동안 (RPG 등)",
            _ => "사용 안 함",
        });

        if (wr.ProjectileVisual == null)
            EditorGUILayout.HelpBox(
                "모델 프리팹 안에 화살·탄두를 자식으로 만들고 WeaponRig의 Projectile Visual에 연결하세요.\n" +
                "활은 StringBone의 자식으로 두면 시위를 따라 움직입니다.", MessageType.Info);
        else if (wr.PreviewMode == ProjectilePreviewMode.WhileDrawing && wr.StringBone != null
                 && !wr.ProjectileVisual.transform.IsChildOf(wr.StringBone))
            EditorGUILayout.HelpBox("화살이 시위 본의 자식이 아니라서 시위를 당겨도 따라오지 않습니다.", MessageType.Warning);

        EditorGUI.BeginChangeCheck();
        _forceProjectile = EditorGUILayout.Toggle("항상 보이기 (위치 확인용)", _forceProjectile);
        if (EditorGUI.EndChangeCheck())
        {
            if (_forceProjectile) wr.ForcePreview(true);
            else { wr.SetLoaded(true); wr.OnDraw(_previewDraw); }
            SceneView.RepaintAll();
        }

        if (_weapon.ModelPrefab != null && GUILayout.Button("모델 프리팹 열기 (위치는 프리팹에서 직접)"))
            AssetDatabase.OpenAsset(_weapon.ModelPrefab);

        Hint("모드와 화살 연결은 프리팹의 WeaponRig에서 정합니다. 차징 미리보기 슬라이더로 따라오는지 확인하세요.");
    }

    // ══════════════════ 원거리 재생 미리보기 (차징 · 반동) ══════════════════

    enum RangedPlay { None, Charge, Recoil }
    private RangedPlay _rangedPlay;
    private float _rangedT;
    private int _recoilPlayShots = 6, _recoilFired;
    private float _recoilNextShot;

    void StartRangedPlay(RangedPlay mode)
    {
        _rangedPlay = mode;
        _rangedT = 0f;
        _recoilFired = 0;
        _recoilNextShot = 0f;
        _playing = false;
        if (_rig == null) return;
        _rig.SetRecoilPreview(0f);
        _previewRecoil = 0f;
        _previewDraw = 0f;
        _rig.SetDrawAmount(0f);
        _rig.ApplyPose(_rig.GetEffectiveIdle());
        SceneView.RepaintAll();
    }

    /// <summary>OnEditorUpdate에서 — 재생 중이면 true</summary>
    bool TickRangedPlay(float dt)
    {
        if (_rangedPlay == RangedPlay.None || _rig == null || _weapon == null) return false;
        _rangedT += dt;

        if (_rangedPlay == RangedPlay.Charge)
        {
            float pull = Mathf.Max(0.05f, _weapon.MaxDrawTime);
            const float hold = 0.35f, rest = 0.5f;
            float t = _rangedT;
            if (t < pull) _previewDraw = t / pull;
            else if (t < pull + hold) _previewDraw = 1f;
            else if (t < pull + hold + rest) _previewDraw = 0f;           // 놓음
            else { _rangedT = 0f; _previewDraw = 0f; }                   // 반복
            _rig.SetDrawAmount(_previewDraw, dt);
            if (_previewDraw <= 0f) _rig.CurrentWeaponRig?.OnFired();
            _rig.ApplyPose(_rig.GetEffectiveIdle());
        }
        else
        {
            float interval = 60f / Mathf.Max(1f, _weapon.RoundsPerMinute);
            int shots = _weapon.FullAuto ? _recoilPlayShots : 1;
            if (_recoilFired < shots && _rangedT >= _recoilNextShot)
            {
                float mul = Mathf.Min(Mathf.Pow(_weapon.RecoilRamp, _recoilFired), _weapon.RecoilMaxMul);
                _rig.AddRecoil(mul);
                _rig.MuzzleFlash?.Play();
                _recoilFired++;
                _recoilNextShot = _rangedT + interval;
            }
            _rig.SetDrawAmount(0f, dt);                                  // 반동 오르내림이 여기서 돎
            _rig.ApplyPose(_rig.GetEffectiveIdle());

            // 다 쏘고 돌아오면 잠깐 쉬고 반복
            float done = _recoilNextShot + Mathf.Max(0.1f, _weapon.RecoilFallTime) * 2f + 0.6f;
            if (_recoilFired >= shots && _rangedT >= done)
            {
                _rangedT = 0f;
                _recoilFired = 0;
                _recoilNextShot = 0f;
            }
        }
        SceneView.RepaintAll();
        return true;
    }

    void ApplyPreviewRecoil()
    {
        _rig.SetRecoilPreview(_previewRecoil);
        _rig.ApplyPose(_rig.GetEffectiveIdle());
        SceneView.RepaintAll();
    }

    void ApplyPreviewDraw()
    {
        _rig.SetDrawAmount(_previewDraw);
        _rig.ApplyPose(_rig.GetEffectiveIdle());
        if (_forceProjectile) _rig.CurrentWeaponRig?.ForcePreview(true);
        SceneView.RepaintAll();
    }

    // ══════════════════════ 모션 탭 ══════════════════════

    void DrawMotionTab()
    {
        int len = _source.ComboLength;

        if (len == 0)
        {
            EditorGUILayout.HelpBox("모션이 비어 있습니다.", MessageType.Warning);
            if (_source.SupportsMultiStep && GUILayout.Button("1타 추가"))
                Modify("Add Combo", () => _source.SetSteps(new[] { new ComboStep { Name = "1타" } }));
            return;
        }

        _comboIndex = Mathf.Clamp(_comboIndex, 0, len - 1);
        DrawComboBar(len);

        var step = CurrentStep;
        if (step == null) return;

        step.Motion ??= new SwingMotion();
        step.Motion.Keys ??= new List<PoseKey>();
        step.Motion.Shakes ??= new List<MotionShake>();
        step.Motion.Floats ??= new List<MotionFloat>();
        step.Events ??= new MotionEvent[0];

        // 떨림 속도(초당)를 맞추려고 모션 길이를 모션에 적어 둠
        if (!Mathf.Approximately(step.Motion.LengthHint, step.TotalTime))
        {
            step.Motion.LengthHint = step.TotalTime;
            if (_target != null) EditorUtility.SetDirty(_target);
        }

        HandleShortcuts(step);

        // ── 이름 · 길이 · 프리셋 ──
        using (Box())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                step.Name = EditorGUILayout.TextField(step.Name);
                EditorGUILayout.LabelField("길이", GUILayout.Width(28));
                step.Duration = Mathf.Max(0.05f, EditorGUILayout.FloatField(step.Duration, GUILayout.Width(48)));
                EditorGUILayout.LabelField("초", GUILayout.Width(16));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var m = step.Motion;
                EditorGUILayout.LabelField(new GUIContent("보간", "직선 = 키마다 곡선(Ease)으로 가감속, 키에서 멈추기 쉬움\n" +
                    "부드럽게 = 모든 키를 지나는 곡선. 중간 키를 멈춤 없이 통과하고, 회전도 입력한 방향 그대로 돎"), GUILayout.Width(28));
                int mode = GUILayout.Toolbar((int)m.Interp, new[] { "직선", "부드럽게" }, EditorStyles.miniButton, GUILayout.Width(120));
                if (mode != (int)m.Interp) { m.Interp = (MotionInterp)mode; Scrub(step, NowT(step)); }

                using (new EditorGUI.DisabledScope(m.Interp != MotionInterp.Smooth))
                {
                    EditorGUILayout.LabelField(new GUIContent("탄력", "0 = 키마다 멈춤, 1 = 자연스럽게 통과, 1 초과 = 더 크게 휘어짐"), GUILayout.Width(28));
                    float sm = EditorGUILayout.Slider(m.Smoothness, 0f, 1.5f);
                    if (!Mathf.Approximately(sm, m.Smoothness)) { m.Smoothness = sm; Scrub(step, NowT(step)); }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _presetKind = (SwingPresets.Kind)EditorGUILayout.EnumPopup(_presetKind);
                if (GUILayout.Button("프리셋 적용", GUILayout.Width(80))) ApplyPreset(step, _rig.GetEffectiveIdle());
                if (GUILayout.Button(new GUIContent("현재 포즈 기준", "지금 씬 포즈를 기준으로 프리셋 적용"), GUILayout.Width(96)))
                    ApplyPreset(step, CaptureCurrentPose());
            }
        }

        // ── 타임라인 ──
        EditorGUILayout.Space(4);
        DrawTimeline(step);
        DrawAddBar(step);
        DrawPlayback(step);

        // ── 떨림 ──
        EditorGUILayout.Space(4);
        if (Fold("떨림 구간 (힘주기)", step.Motion.Shakes.Count > 0))
            using (Box()) DrawShakes(step);

        // ── 둥둥 ──
        if (Fold("둥둥 구간 (무기가 떠다님)", step.Motion.Floats.Count > 0))
            using (Box()) DrawFloats(step);

        // ── 파츠 부모 ──
        if (Fold("파츠 부모 (이 모션 동안)", step.PartParents != null && step.PartParents.Length > 0))
            using (Box()) DrawStepParents(step);

        // ── 선택한 것 ──
        EditorGUILayout.Space(6);
        using (Box())
        {
            if (_sel == Sel.Key && _selIndex >= 0 && _selIndex < step.Motion.Keys.Count)
                DrawKeyInspector(step, _selIndex);
            else if (_sel == Sel.Event && _selIndex >= 0 && _selIndex < step.Events.Length)
                DrawEventInspector(step, _selIndex);
            else
            {
                ClearSelection();
                EditorGUILayout.LabelField("선택한 항목 없음", EditorStyles.boldLabel);
                Hint("타임라인에서 ◆ 키나 이벤트를 클릭하세요.\n" + ShortcutHelp);
            }
        }

        // ── 타격 설정 ──
        EditorGUILayout.Space(4);
        if (Fold("타격 설정 (판정 · 피해 · 연출)", false))
            using (Box()) DrawStepSettings(step);

        // ── 목록 ──
        if (Fold("키 순서 · 이벤트 목록", true))
            using (Box()) DrawLists(step);
    }

    float NowT(ComboStep step) => Mathf.Clamp01(_playT / Mathf.Max(0.01f, step.TotalTime));

    void DrawComboBar(int len)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (len > 1)
            {
                for (int i = 0; i < len; i++)
                {
                    var s = _source.GetStep(i);
                    var style = i == 0 ? EditorStyles.miniButtonLeft
                        : i == len - 1 ? EditorStyles.miniButtonRight
                        : EditorStyles.miniButtonMid;

                    bool on = _comboIndex == i;
                    if (GUILayout.Toggle(on, s?.Name ?? $"{i + 1}타", style) && !on)
                    {
                        _comboIndex = i;
                        ClearSelection();
                        _playT = 0f;
                        _playing = false;
                        RefreshPreview();
                    }
                }
            }
            else GUILayout.FlexibleSpace();

            GUILayout.Space(6);
            if (GUILayout.Button(new GUIContent("⇋ 좌우 반전 ▾", "오른손 ↔ 왼손을 바꾼 모션 만들기"), EditorStyles.miniButton, GUILayout.Width(92)))
                ShowMirrorMenu();

            if (!_source.SupportsMultiStep) return;

            GUILayout.Space(4);
            if (GUILayout.Button(new GUIContent("+", "타 추가"), EditorStyles.miniButtonLeft, GUILayout.Width(24))) AddComboStep();
            if (GUILayout.Button(new GUIContent("⧉", "이 타 복제"), EditorStyles.miniButtonMid, GUILayout.Width(24))) DuplicateComboStep();
            using (new EditorGUI.DisabledScope(len <= 1))
                if (GUILayout.Button(new GUIContent("−", "이 타 삭제"), EditorStyles.miniButtonRight, GUILayout.Width(24))) DeleteComboStep();
        }
    }

    // ─────────── 좌우 반전 ───────────

    /// <summary>손가락 관절 회전을 반전할 축. 왼손 모델이 오른손의 거울상이면 보통 '그대로'가 맞음</summary>
    enum FingerMirror { AsIs, MirrorX, MirrorY, MirrorZ }
    private FingerMirror _fingerMirror = FingerMirror.AsIs;

    void ShowMirrorMenu()
    {
        var menu = new GenericMenu();

        if (_source.SupportsMultiStep)
        {
            menu.AddItem(new GUIContent("반전한 타를 바로 다음에 추가"), false, () => AddMirroredStep(false));
            menu.AddItem(new GUIContent("반전한 타를 맨 끝에 추가"), false, () => AddMirroredStep(true));
        }
        menu.AddItem(new GUIContent("이 타를 그대로 반전"), false, MirrorCurrentInPlace);
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("손가락 회전/그대로 (왼손이 거울 모델)"), _fingerMirror == FingerMirror.AsIs, () => _fingerMirror = FingerMirror.AsIs);
        menu.AddItem(new GUIContent("손가락 회전/X축 반전"), _fingerMirror == FingerMirror.MirrorX, () => _fingerMirror = FingerMirror.MirrorX);
        menu.AddItem(new GUIContent("손가락 회전/Y축 반전"), _fingerMirror == FingerMirror.MirrorY, () => _fingerMirror = FingerMirror.MirrorY);
        menu.AddItem(new GUIContent("손가락 회전/Z축 반전"), _fingerMirror == FingerMirror.MirrorZ, () => _fingerMirror = FingerMirror.MirrorZ);
        menu.ShowAsContext();
    }

    void AddMirroredStep(bool atEnd)
    {
        var list = new List<ComboStep>(_source.GetSteps());
        var src = list[_comboIndex];
        var m = MirrorStep(src);
        m.Name = $"{list.Count + 1}타";

        int at = atEnd ? list.Count : _comboIndex + 1;
        list.Insert(at, m);

        Modify("Add Mirrored Step", () => _source.SetSteps(list.ToArray()));
        _comboIndex = at;
        ClearSelection();
        _playT = 0f;
        RefreshPreview();
    }

    void MirrorCurrentInPlace()
    {
        var step = CurrentStep;
        if (step == null) return;

        var m = MirrorStep(step);
        Modify("Mirror Step", () =>
        {
            step.Motion = m.Motion;
            step.Events = m.Events;
            step.HandMode = m.HandMode;
        });
        RefreshPreview();
    }

    /// <summary>오른손 ↔ 왼손 바꾸고 좌우(X) 대칭. 타이밍·판정·피해 설정은 그대로</summary>
    ComboStep MirrorStep(ComboStep src)
    {
        var c = CloneStep(src);

        c.HandMode = src.HandMode switch
        {
            HandVisibility.RightOnly => HandVisibility.LeftOnly,
            HandVisibility.LeftOnly => HandVisibility.RightOnly,
            _ => src.HandMode,
        };

        if (c.Motion?.Keys != null)
            foreach (var k in c.Motion.Keys)
                k.Pose = MirrorPose(k.Pose, _fingerMirror);

        if (c.Events != null)
            foreach (var e in c.Events)
            {
                if (e == null) continue;

                e.Space = e.Space switch
                {
                    EventSpace.AttachToRightHand => EventSpace.AttachToLeftHand,
                    EventSpace.AttachToLeftHand => EventSpace.AttachToRightHand,
                    _ => e.Space,
                };

                // 무기·총구 기준은 무기 쪽 좌표라 그대로, 나머지는 좌우 대칭
                if (e.Space != EventSpace.AttachToWeapon && e.Space != EventSpace.AttachToMuzzle)
                {
                    e.PositionOffset = MirrorPos(e.PositionOffset);
                    e.RotationOffset = MirrorRot(e.RotationOffset);
                }
            }

        return c;
    }

    static Vector3 MirrorPos(Vector3 v) => new(-v.x, v.y, v.z);
    static Vector3 MirrorRot(Vector3 e) => new(e.x, -e.y, -e.z);

    static ArmPose MirrorPose(ArmPose p, FingerMirror fm)
    {
        if (p == null) return null;
        var r = p.Clone();

        r.RightPos = MirrorPos(p.LeftPos);
        r.RightRot = MirrorRot(p.LeftRot);
        r.LeftPos = MirrorPos(p.RightPos);
        r.LeftRot = MirrorRot(p.RightRot);
        r.UseLeftHand = true;                       // 왼손이 주인공이 되므로 항상 보이게

        r.RightFingers = MirrorHand(p.LeftFingers, fm);
        r.LeftFingers = MirrorHand(p.RightFingers, fm);

        r.CamPos = MirrorPos(p.CamPos);
        r.CamRot = MirrorRot(p.CamRot);
        r.BodyPos = MirrorPos(p.BodyPos);
        r.BodyRot = MirrorRot(p.BodyRot);
        r.HeadPos = MirrorPos(p.HeadPos);
        r.HeadRot = MirrorRot(p.HeadRot);

        // 무기 오프셋은 무기를 든 손 기준이라 그대로
        return r;
    }

    static HandPose MirrorHand(HandPose h, FingerMirror fm)
    {
        if (h == null) return null;
        var c = h.Clone();
        if (fm == FingerMirror.AsIs) return c;

        c.EnsureValid();
        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var fp = c.Get(f);
            if (fp?.Joints == null) continue;

            for (int j = 0; j < fp.Joints.Length; j++)
            {
                var q = fp.Joints[j];
                fp.Joints[j] = fm switch
                {
                    FingerMirror.MirrorX => new Quaternion(q.x, -q.y, -q.z, q.w),
                    FingerMirror.MirrorY => new Quaternion(-q.x, q.y, -q.z, q.w),
                    _ => new Quaternion(-q.x, -q.y, q.z, q.w),
                };
            }
        }
        return c;
    }

    static ComboStep CloneStep(ComboStep src)
    {
        var c = new ComboStep();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(src), c);
        return c;
    }

    void AddComboStep()
    {
        var list = new List<ComboStep>(_source.GetSteps() ?? new ComboStep[0]);
        var src = list.Count > 0 ? list[^1] : null;

        var next = src != null ? CloneStep(src) : new ComboStep();
        next.Name = $"{list.Count + 1}타";
        next.DamageMultiplier = (src?.DamageMultiplier ?? 1f) + 0.15f;
        next.Events = new MotionEvent[0];

        var kind = list.Count % 2 == 0 ? SwingPresets.Kind.DiagonalSlash : SwingPresets.Kind.BackhandReturn;
        next.Motion = SwingPresets.Create(kind, _rig.GetEffectiveIdle(), IsTwoHand());

        list.Add(next);
        Modify("Add Combo Step", () => _source.SetSteps(list.ToArray()));

        _comboIndex = list.Count - 1;
        ClearSelection();
        _playT = 0f;
        RefreshPreview();
    }

    void DuplicateComboStep()
    {
        var list = new List<ComboStep>(_source.GetSteps());
        var copy = CloneStep(list[_comboIndex]);
        copy.Name = $"{list.Count + 1}타";
        list.Insert(_comboIndex + 1, copy);

        Modify("Duplicate Step", () => _source.SetSteps(list.ToArray()));
        _comboIndex++;
        ClearSelection();
    }

    void DeleteComboStep()
    {
        if (!EditorUtility.DisplayDialog("확인", $"{CurrentStep?.Name}를 삭제할까요?", "삭제", "취소")) return;

        var list = new List<ComboStep>(_source.GetSteps());
        list.RemoveAt(_comboIndex);
        Modify("Delete Combo Step", () => _source.SetSteps(list.ToArray()));

        _comboIndex = Mathf.Clamp(_comboIndex, 0, Mathf.Max(0, list.Count - 1));
        ClearSelection();
    }

    void ApplyPreset(ComboStep step, ArmPose baseIdle)
    {
        Modify("Apply Preset", () => step.Motion = SwingPresets.Create(_presetKind, baseIdle, IsTwoHand()));
        ClearSelection();
        _playT = 0f;
        RefreshPreview();
    }

    bool IsTwoHand()
    {
        var w = DesiredPreviewWeapon();         // 이 모션만 쥐는 무기가 있으면 그 무기 기준
        return w != null && w.Grip == GripType.TwoHand;
    }

    // ─────────── 타임라인 ───────────

    // ══════════════════ 파츠 (모델 여러 개) ══════════════════

    static readonly string[] ParentNames = { "기본 (무기 설정)", "오른손", "왼손", "팔 (손을 안 따라감)" };
    static readonly string[] PartParentNames = { "오른손", "왼손", "팔 (손을 안 따라감)" };     // 추가 파츠 — 기본 없음

    /// <summary>지금 손에 든 무기 (파츠 이름 · 수)</summary>
    EquipmentData PartsWeapon => _rig != null && _rig.CurrentWeapon != null ? _rig.CurrentWeapon : DesiredPreviewWeapon();

    /// <summary>파츠 고르기 — allowAll이면 맨 앞에 '전부'(-1)</summary>
    int PartPopup(GUIContent label, int value, bool allowAll)
    {
        var w = PartsWeapon;
        int n = Mathf.Max(1, w != null ? w.PartCount : 1);
        var names = new List<string>();
        if (allowAll) names.Add("전부");
        for (int i = 0; i < n; i++) names.Add($"{i}번 · {(w != null ? w.PartName(i) : "메인")}");
        int idx = allowAll ? value + 1 : value;
        idx = Mathf.Clamp(idx, 0, names.Count - 1);
        idx = EditorGUILayout.Popup(label, idx, names.ToArray());
        return allowAll ? idx - 1 : idx;
    }

    /// <summary>무기 설정 — 0번 부모 + 추가 파츠 목록</summary>
    void DrawWeaponParts()
    {
        Hint("한 무기에 모델 여러 개 — 0번 = 위 '무기 배치'의 모델 (판정 · 총구 · 끝점 · 왼손 그립 기준), 1번부터 추가.\n" +
             "파츠마다 붙는 곳(오른손 · 왼손 · 팔)과 위치 · 회전 · 크기를 따로. 모션 탭의 '파츠 부모'로 모션 동안만 바꿀 수 있음.\n" +
             "씬에서 파츠를 옮기고 '씬 위치 저장' → 그 파츠의 기본값. 모션 키의 '씬 포즈 저장'은 파츠마다 따로 계산됨");

        Undo.RecordObject(_weapon, "Weapon Parts");

        EditorGUI.BeginChangeCheck();
        var mp = (WeaponParent)EditorGUILayout.Popup(new GUIContent("0번 (메인) 붙는 곳",
            "기본 = 잡는 손 (둥둥의 '손을 따라가지 않음'을 켰으면 팔)"), (int)_weapon.MainParent, ParentNames);
        if (EditorGUI.EndChangeCheck())
        {
            _weapon.MainParent = mp;
            EditorUtility.SetDirty(_weapon);
            SpawnWeapon();
            return;
        }

        var list = _weapon.ExtraParts ?? new WeaponPart[0];
        int remove = -1;
        bool respawn = false, refresh = false;

        for (int i = 0; i < list.Length; i++)
        {
            var d = list[i] ??= new WeaponPart();
            int idx = i + 1;
            using (Box())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{idx}번", EditorStyles.boldLabel, GUILayout.Width(30));
                    d.Name = EditorGUILayout.TextField(d.Name);
                    if (GUILayout.Button(new GUIContent("선택", "씬에서 이 파츠 고르기"), EditorStyles.miniButton, GUILayout.Width(36)))
                    {
                        var t = _rig != null ? _rig.GetPartTransform(idx) : null;
                        if (t != null) Selection.activeGameObject = t.gameObject;
                    }
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) remove = i;
                }

                EditorGUI.BeginChangeCheck();
                d.ModelPrefab = (GameObject)EditorGUILayout.ObjectField("모델", d.ModelPrefab, typeof(GameObject), false);
                int pi = Mathf.Clamp((int)d.Parent - 1, 0, 2);
                pi = EditorGUILayout.Popup("붙는 곳", pi, PartParentNames);
                d.Parent = (WeaponParent)(pi + 1);
                if (EditorGUI.EndChangeCheck()) respawn = true;

                EditorGUI.BeginChangeCheck();
                d.Position = EditorGUILayout.Vector3Field("위치", d.Position);
                d.Rotation = EditorGUILayout.Vector3Field("회전", d.Rotation);
                d.Scale = EditorGUILayout.Vector3Field("크기", d.Scale);
                d.UseFloat = EditorGUILayout.ToggleLeft(new GUIContent("이 파츠 항상 둥둥"), d.UseFloat);
                d.Float ??= new FloatWave();
                if (d.UseFloat)
                    using (new EditorGUI.IndentLevelScope()) DrawFloatWave(d.Float);
                if (EditorGUI.EndChangeCheck()) refresh = true;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("씬 위치 저장", "씬에서 옮긴 이 파츠의 위치 · 회전 · 크기를 기본값으로 (둥둥 흔들림은 빼고)"), EditorStyles.miniButtonLeft))
                    {
                        var t = _rig != null ? _rig.GetPartTransform(idx) : null;
                        if (t != null)
                        {
                            _rig.GetPartLastFloat(idx, out var fP, out var fR);
                            d.Position = t.localPosition - fP;
                            d.Rotation = NormalizeEuler(t.localRotation.eulerAngles - fR);
                            d.Scale = t.localScale;
                            refresh = true;
                        }
                    }
                    if (GUILayout.Button("초기화", EditorStyles.miniButtonRight))
                    {
                        d.Position = d.Rotation = Vector3.zero;
                        d.Scale = Vector3.one;
                        refresh = true;
                    }
                }
            }
        }

        if (remove >= 0)
        {
            var l = new List<WeaponPart>(list);
            l.RemoveAt(remove);
            _weapon.ExtraParts = l.ToArray();
            respawn = true;
            Hint("파츠를 지우면 그 뒤 번호가 한 칸씩 당겨짐 — 모션 키의 파츠 값은 번호 기준이라 확인 필요");
        }

        if (GUILayout.Button("+ 파츠 추가"))
        {
            var l = new List<WeaponPart>(list) { new WeaponPart { Name = $"파츠 {list.Length + 1}" } };
            _weapon.ExtraParts = l.ToArray();
            respawn = true;
        }

        if (respawn || refresh) EditorUtility.SetDirty(_weapon);
        if (respawn) SpawnWeapon();
        else if (refresh && !_playing) RefreshPreview();
    }

    /// <summary>모션 — 이 모션 동안 파츠가 붙는 곳</summary>
    void DrawStepParents(ComboStep step)
    {
        var w = PartsWeapon;
        int n = Mathf.Max(1, w != null ? w.PartCount : 1);
        Hint("이 모션 동안만 파츠가 붙는 곳을 바꿈 (예: 펜은 오른손, 노트는 왼손). 기본 = 무기 설정대로.\n" +
             "키의 무기 · 파츠 위치는 여기서 정한 곳 기준 — 바꾸면 이미 찍은 키는 다시 '씬 포즈 저장' 필요");

        var arr = step.PartParents ?? new WeaponParent[0];
        EditorGUI.BeginChangeCheck();
        var next = new WeaponParent[n];
        for (int i = 0; i < n; i++)
        {
            var cur = i < arr.Length ? arr[i] : WeaponParent.Default;
            next[i] = (WeaponParent)EditorGUILayout.Popup($"{i}번 · {(w != null ? w.PartName(i) : "메인")}", (int)cur, ParentNames);
        }
        if (EditorGUI.EndChangeCheck())
        {
            bool any = false;
            foreach (var v in next) if (v != WeaponParent.Default) any = true;
            Modify("Part Parents", () => step.PartParents = any ? next : null);
            if (!_playing) RefreshPreview();
        }
    }

    /// <summary>키 — 추가 파츠의 위치 · 회전 · 크기</summary>
    void DrawKeyExtraParts(ArmPose p)
    {
        var w = PartsWeapon;
        int n = w != null ? w.PartCount : 1;
        if (n <= 1) return;

        for (int i = 1; i < n; i++)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"{i}번 파츠 · {w.PartName(i)}", EditorStyles.miniBoldLabel);
            Vector3 pos = p.PartPos(i), rot = p.PartRot(i), scl = p.PartScale(i);
            EditorGUI.BeginChangeCheck();
            pos = EditorGUILayout.Vector3Field("추가 위치", pos);
            rot = EditorGUILayout.Vector3Field("추가 회전", rot);
            using (new EditorGUILayout.HorizontalScope())
            {
                scl = EditorGUILayout.Vector3Field("크기 (배율)", scl);
                if (GUILayout.Button(new GUIContent("1", "원래 크기로"), EditorStyles.miniButton, GUILayout.Width(22))) scl = Vector3.one;
            }
            if (EditorGUI.EndChangeCheck()) p.SetPart(i, pos, rot, scl);
        }
    }

    // ══════════════════ 둥둥 ══════════════════

    static float Div(float a, float b) => Mathf.Abs(b) < 1e-5f ? 1f : a / b;

    /// <summary>무기 설정 — 손에서 벗어나 떠 있음 · 항상 둥둥</summary>
    void DrawWeaponFloat()
    {
        Hint("손을 벗어나 공중에 떠 있는 특수 무기용.\n" +
             "'손을 따라가지 않음'을 켜면 무기가 손 대신 팔(화면) 기준 자리에 놓임 → 위 '무기 배치'의 위치 · 회전이 그 자리.\n" +
             "'항상 둥둥'은 가만히 있을 때 · 모션 중 모두 제자리를 중심으로 떠다님 (모션 키 위치가 중심)");

        EditorGUI.BeginChangeCheck();
        bool free = EditorGUILayout.ToggleLeft(new GUIContent("손을 따라가지 않음 (떠 있는 무기)",
            "켜면 손이 움직여도 무기는 제자리 — 손 대신 팔 기준. 켜고 끄면 무기 위치를 다시 맞춰야 함"), _weapon.FloatFree);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_weapon, "Float Free");
            _weapon.FloatFree = free;
            EditorUtility.SetDirty(_weapon);
            SpawnWeapon();
            return;
        }

        Undo.RecordObject(_weapon, "Weapon Float");        // 바뀐 게 있을 때만 되돌리기에 남음
        EditorGUI.BeginChangeCheck();
        bool use = EditorGUILayout.ToggleLeft(new GUIContent("항상 둥둥", "제자리를 중심으로 계속 떠다님"), _weapon.UseFloat);
        _weapon.Float ??= new FloatWave();
        if (use)
        {
            using (new EditorGUI.IndentLevelScope())
                DrawFloatWave(_weapon.Float);
        }
        if (EditorGUI.EndChangeCheck())
        {
            _weapon.UseFloat = use;
            EditorUtility.SetDirty(_weapon);
            if (!_playing) RefreshPreview();
        }

        bool prev = EditorGUILayout.ToggleLeft(new GUIContent("편집 중에도 움직여 보기",
            "끄면 편집 모드에선 제자리에 멈춰 있음 (플레이에선 항상 움직임).\n켜 둔 동안엔 계속 다시 그려서 씬에서 손 · 무기를 옮길 수 없음 — 옮길 땐 끄기"),
            FPArmRig.EditorFloatPreview);
        if (prev != FPArmRig.EditorFloatPreview)
        {
            FPArmRig.EditorFloatPreview = prev;
            RefreshPreview();
        }
    }

    static void DrawFloatWave(FloatWave w)
    {
        w.PosAmount = EditorGUILayout.Vector3Field(new GUIContent("위치 (cm)", "축마다 떠다니는 거리 — 위아래 둥둥이면 Y만 크게"), w.PosAmount);
        w.RotAmount = EditorGUILayout.Vector3Field(new GUIContent("회전 (°)", "축마다 기울어지는 각도"), w.RotAmount);
        w.Speed = EditorGUILayout.Slider(new GUIContent("빠르기 (초당)", "0.3~0.8 = 둥둥 · 2 이상 = 붕붕"), w.Speed, 0.05f, 4f);
        w.Wander = EditorGUILayout.Slider(new GUIContent("불규칙함", "0 = 일정한 물결 · 1 = 제멋대로 떠돎"), w.Wander, 0f, 1f);
        w.Seed = EditorGUILayout.IntField(new GUIContent("무늬", "숫자를 바꾸면 다른 모양"), w.Seed);
    }

    /// <summary>모션의 둥둥 구간</summary>
    void DrawFloats(ComboStep step)
    {
        var list = step.Motion.Floats;
        Hint("구간 동안 무기가 키 위치(제자리)를 중심으로 둥둥 떠다님 — 떨림보다 느리고 크게. 키 자세 위에 더해져서 키를 따로 안 찍어도 됨.\n" +
             "구간이 모션 처음 · 끝에 붙어 있으면 그쪽은 페이드 없이 바로. 손에서 벗어난 무기면 무기 설정의 '손을 따라가지 않음'도 같이");

        EditorGUI.BeginChangeCheck();
        int remove = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var f = list[i];
            if (f == null) { remove = i; continue; }
            f.Wave ??= new FloatWave();

            using (Box())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"둥둥 {i + 1}", EditorStyles.boldLabel);
                    if (GUILayout.Button(new GUIContent("▶", "이 구간 시작으로 이동"), EditorStyles.miniButton, GUILayout.Width(24)))
                        Scrub(step, Mathf.Min(f.Start, f.End));
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) remove = i;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    float a = f.Start, b = f.End;
                    EditorGUILayout.LabelField("구간", GUILayout.Width(52));
                    a = EditorGUILayout.FloatField(a, GUILayout.Width(42));
                    EditorGUILayout.MinMaxSlider(ref a, ref b, 0f, 1f);
                    b = EditorGUILayout.FloatField(b, GUILayout.Width(42));
                    f.Start = Mathf.Clamp01(Mathf.Min(a, b));
                    f.End = Mathf.Clamp01(Mathf.Max(a, b));
                    if (GUILayout.Button(new GUIContent("전체", "모션 처음부터 끝까지"), EditorStyles.miniButton, GUILayout.Width(36)))
                    { f.Start = 0f; f.End = 1f; }
                }
                Hint($"{f.Start * step.TotalTime:0.00}초 ~ {f.End * step.TotalTime:0.00}초");

                f.Fade = EditorGUILayout.Slider(new GUIContent("부드럽게", "들어오고 빠지는 길이 (구간 대비)"), f.Fade, 0f, 0.5f);
                f.Part = PartPopup(new GUIContent("파츠", "떠다닐 파츠 — 전부 / 0번(메인) / 1번 …"), f.Part, true);
                DrawFloatWave(f.Wave);
            }
        }

        if (remove >= 0) Modify("Remove Float", () => list.RemoveAt(remove));

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("+ 둥둥 추가 (모션 전체)", "모션 처음부터 끝까지 — 구간은 아래에서 줄이기")))
                Modify("Add Float", () => list.Add(new MotionFloat { Start = 0f, End = 1f }));
            if (list.Count > 0 && GUILayout.Button(new GUIContent("미리보기", "처음부터 재생"), GUILayout.Width(70)))
            {
                _playAll = false;
                _playT = 0f;
                _playing = true;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            if (_target != null) EditorUtility.SetDirty(_target);
            if (!_playing) RefreshPreview();
        }
    }

    // ══════════════════ 떨림 구간 ══════════════════

    static readonly string[] ShakeHandNames = { "오른손", "왼손", "양손" };

    void DrawShakes(ComboStep step)
    {
        var list = step.Motion.Shakes;
        Hint("구간 동안 손이 바르르 떨림 — 버티거나 힘을 주는 느낌. 키 자세 위에 더해져서 키를 따로 안 찍어도 됨.\n" +
             "타임라인 아래 '떨림' 줄에 세기가 그려짐 (주황 = 오른손 · 파랑 = 왼손 · 보라 = 양손)");

        EditorGUI.BeginChangeCheck();       // 이 칸들만 — 위의 재생 버튼 클릭까지 잡으면 재생이 멈춤
        int remove = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s == null) { remove = i; continue; }

            using (Box())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"떨림 {i + 1}", EditorStyles.boldLabel, GUILayout.Width(52));
                    int h = GUILayout.Toolbar((int)s.Hands, ShakeHandNames, EditorStyles.miniButton);
                    if (h != (int)s.Hands) s.Hands = (ShakeHand)h;
                    if (GUILayout.Button(new GUIContent("▶", "이 구간 시작으로 이동"), EditorStyles.miniButton, GUILayout.Width(24)))
                        Scrub(step, Mathf.Min(s.Start, s.End));
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) remove = i;
                }

                // 구간
                using (new EditorGUILayout.HorizontalScope())
                {
                    float a = s.Start, b = s.End;
                    EditorGUILayout.LabelField("구간", GUILayout.Width(52));
                    a = EditorGUILayout.FloatField(a, GUILayout.Width(42));
                    EditorGUILayout.MinMaxSlider(ref a, ref b, 0f, 1f);
                    b = EditorGUILayout.FloatField(b, GUILayout.Width(42));
                    s.Start = Mathf.Clamp01(Mathf.Min(a, b));
                    s.End = Mathf.Clamp01(Mathf.Max(a, b));
                    if (GUILayout.Button(new GUIContent("지금", "구간 끝을 지금 재생 위치로"), EditorStyles.miniButton, GUILayout.Width(36)))
                        s.End = Mathf.Max(s.Start + 0.01f, NowT(step));
                }
                Hint($"{s.Start * step.TotalTime:0.00}초 ~ {s.End * step.TotalTime:0.00}초");

                s.PosAmount = EditorGUILayout.Slider(new GUIContent("위치 (cm)", "손이 흔들리는 거리 — 1인칭에선 1~2cm면 확실히 보임"), s.PosAmount, 0f, 6f);
                s.RotAmount = EditorGUILayout.Slider(new GUIContent("회전 (°)", "손목이 흔들리는 각도 — 무기 끝이 크게 흔들려서 제일 티가 남"), s.RotAmount, 0f, 25f);
                s.Frequency = EditorGUILayout.Slider(new GUIContent("빠르기 (초당)", "15~30 = 바르르, 5~10 = 휘청"), s.Frequency, 1f, 60f);
                s.Fade = EditorGUILayout.Slider(new GUIContent("부드럽게", "들어오고 빠지는 길이 (구간 대비)"), s.Fade, 0f, 0.5f);
                s.Build = EditorGUILayout.Slider(new GUIContent("점점 세게", "0 = 일정 · 1 = 끝으로 갈수록 세짐"), s.Build, 0f, 1f);
                s.CamAmount = EditorGUILayout.Slider(new GUIContent("화면 (°)", "1인칭 화면도 같이 떨림 — 0이면 손만"), s.CamAmount, 0f, 3f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (s.Hands == ShakeHand.Both)
                        s.Independent = EditorGUILayout.ToggleLeft(new GUIContent("두 손 따로", "끄면 두 손이 같이 떨림 (한 무기를 같이 쥘 때)"), s.Independent, GUILayout.Width(90));
                    s.Seed = EditorGUILayout.IntField(new GUIContent("무늬", "숫자를 바꾸면 다른 떨림 모양"), s.Seed);
                }
            }
        }

        if (remove >= 0) Modify("Remove Shake", () => list.RemoveAt(remove));

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("+ 지금 위치에 떨림 추가", "재생 위치부터 모션 15% 길이")))
            {
                float now = NowT(step);
                Modify("Add Shake", () => list.Add(new MotionShake
                {
                    Start = Mathf.Clamp01(now),
                    End = Mathf.Clamp01(now + 0.15f),
                }));
            }
            if (list.Count > 0 && GUILayout.Button(new GUIContent("미리보기", "처음부터 재생"), GUILayout.Width(70)))
            {
                _playAll = false;
                _playT = 0f;
                _playing = true;
            }
        }

        // 값을 바꾸면 미리보기만 갱신 (재생 중이면 그대로 계속 재생 — 다음 프레임에 반영됨)
        if (EditorGUI.EndChangeCheck() && !_playing) RefreshPreview();
    }

    const float LabelW = 46f;

    void DrawTimeline(ComboStep step)
    {
        bool hasShake = step.Motion.Shakes != null && step.Motion.Shakes.Count > 0;
        Rect full = GUILayoutUtility.GetRect(0, hasShake ? 124 : 104, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(full, CBg);

        Rect area = new(full.x + LabelW, full.y + 2, full.width - LabelW - 8, full.height - 4);
        Rect ruler = new(area.x, area.y, area.width, 14);
        Rect phase = new(area.x, ruler.yMax + 2, area.width, 24);
        Rect keys = new(area.x, phase.yMax + 6, area.width, 22);
        Rect evs = new(area.x, keys.yMax + 6, area.width, 22);
        Rect shk = new(area.x, evs.yMax + 4, area.width, 14);

        float X(float t) => area.x + area.width * Mathf.Clamp01(t);

        // 왼쪽 이름
        var lab = EditorStyles.miniLabel;
        GUI.Label(new Rect(full.x + 4, phase.y + 4, LabelW, 16), "타격", lab);
        GUI.Label(new Rect(full.x + 4, keys.y + 3, LabelW, 16), "키", lab);
        GUI.Label(new Rect(full.x + 4, evs.y + 3, LabelW, 16), "이벤트", lab);

        // 눈금
        for (int i = 0; i <= 20; i++)
        {
            float t = i / 20f;
            bool major = i % 5 == 0;
            EditorGUI.DrawRect(new Rect(X(t), ruler.yMax - (major ? 8 : 4), 1, major ? 8 : 4), new Color(1, 1, 1, 0.25f));
            if (major)
                GUI.Label(new Rect(X(t) + 2, ruler.y - 2, 50, 14), $"{t * step.Duration:0.##}s", EditorStyles.miniLabel);
        }

        // 판정 구간
        float xs = X(step.HitStart), xe = X(step.HitEnd), xc = X(step.CancelPoint);
        EditorGUI.DrawRect(new Rect(phase.x, phase.y, xs - phase.x, phase.height), CWindUp);
        EditorGUI.DrawRect(new Rect(xs, phase.y, xe - xs, phase.height), CActive);
        EditorGUI.DrawRect(new Rect(xe, phase.y, phase.xMax - xe, phase.height), CRecovery);
        DrawHandle(xs, phase, CHandle);
        DrawHandle(xe, phase, CHandle);
        DrawHandle(xc, phase, CCancel);
        GUI.Label(new Rect(xs + 3, phase.y + 4, Mathf.Max(0, xe - xs), 16), "판정", EditorStyles.whiteMiniLabel);

        // 레인
        EditorGUI.DrawRect(keys, CLane);
        EditorGUI.DrawRect(evs, CLane);

        // 키
        var kl = step.Motion.Keys;
        for (int i = 0; i < kl.Count; i++)
        {
            bool sel = _sel == Sel.Key && _selIndex == i;
            float x = X(kl[i].Time);
            DrawDiamond(new Vector2(x, keys.center.y), sel ? 7f : 5.5f, sel ? CSelected : CKey);
        }

        // 이벤트
        for (int i = 0; i < step.Events.Length; i++)
        {
            var e = step.Events[i];
            if (e == null) continue;
            var m = Meta(e.Type);
            bool sel = _sel == Sel.Event && _selIndex == i;
            Rect r = new(X(e.Time) - 8, evs.y + 2, 16, evs.height - 4);
            EditorGUI.DrawRect(r, sel ? CSelected : m.color * 0.85f);
            var st = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            st.normal.textColor = Color.black;
            GUI.Label(r, m.icon, st);
        }

        // 떨림 구간 — 세기(높이)를 그대로 그림
        if (hasShake)
        {
            GUI.Label(new Rect(full.x + 4, shk.y - 1, LabelW, 16), "떨림", lab);
            EditorGUI.DrawRect(shk, CLane);
            foreach (var sh in step.Motion.Shakes)
            {
                if (sh == null) continue;
                Color c = sh.Hands switch
                {
                    ShakeHand.Right => new Color(1f, 0.6f, 0.25f, 0.9f),
                    ShakeHand.Left => new Color(0.4f, 0.75f, 1f, 0.9f),
                    _ => new Color(0.85f, 0.55f, 1f, 0.9f),
                };
                const int N = 40;
                float a0 = Mathf.Min(sh.Start, sh.End), a1 = Mathf.Max(sh.Start, sh.End);
                for (int k = 0; k < N; k++)
                {
                    float t = Mathf.Lerp(a0, a1, (k + 0.5f) / N);
                    float h = SwingMotion.Envelope(sh, t) * shk.height;
                    float x0 = X(Mathf.Lerp(a0, a1, k / (float)N)), x1 = X(Mathf.Lerp(a0, a1, (k + 1f) / N));
                    EditorGUI.DrawRect(new Rect(x0, shk.yMax - h, Mathf.Max(1f, x1 - x0), h), c);
                }
            }
        }

        // 재생 위치
        float px = X(NowT(step));
        EditorGUI.DrawRect(new Rect(px - 1, area.y, 2, area.height), CPlayhead);

        HandleTimelineInput(step, area, ruler, phase, keys, evs);
    }

    static void DrawHandle(float x, Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(x - 1.5f, r.y, 3, r.height), c);
        EditorGUIUtility.AddCursorRect(new Rect(x - 6, r.y, 12, r.height), MouseCursor.ResizeHorizontal);
    }

    static GUIStyle _diamondStyle;

    static void DrawDiamond(Vector2 c, float s, Color col)
    {
        _diamondStyle ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset() };
        _diamondStyle.fontSize = Mathf.RoundToInt(s * 2.2f);
        _diamondStyle.normal.textColor = col;
        GUI.Label(new Rect(c.x - 10, c.y - 10, 20, 20), "◆", _diamondStyle);
    }

    void HandleTimelineInput(ComboStep step, Rect area, Rect ruler, Rect phase, Rect keys, Rect evs)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        float T(float mx) => Mathf.Clamp01((mx - area.x) / area.width);
        float X(float t) => area.x + area.width * t;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown when e.button == 0 && area.Contains(e.mousePosition):
                {
                    Vector2 m = e.mousePosition;
                    _drag = Drag.Scrub;

                    if (evs.Contains(m) && Nearest(step.Events.Length, i => step.Events[i] != null ? X(step.Events[i].Time) : -999, m.x, 9f, out int ei))
                    {
                        Select(Sel.Event, ei, step);
                        _drag = Drag.Event;
                        _dragEvent = step.Events[ei];
                    }
                    else if (keys.Contains(m) && Nearest(step.Motion.Keys.Count, i => X(step.Motion.Keys[i].Time), m.x, 8f, out int ki))
                    {
                        Select(Sel.Key, ki, step);
                        _drag = Drag.Key;
                        _dragKey = step.Motion.Keys[ki];
                    }
                    else if (phase.Contains(m))
                    {
                        float ds = Mathf.Abs(m.x - X(step.HitStart));
                        float de = Mathf.Abs(m.x - X(step.HitEnd));
                        float dc = Mathf.Abs(m.x - X(step.CancelPoint));
                        float min = Mathf.Min(ds, Mathf.Min(de, dc));
                        if (min < 7f) _drag = min == ds ? Drag.HitStart : min == de ? Drag.HitEnd : Drag.Cancel;
                    }
                    else if (keys.Contains(m) || evs.Contains(m))
                    {
                        ClearSelection();
                    }

                    if (_drag == Drag.Scrub) Scrub(step, T(m.x));

                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                }

            case EventType.MouseDrag when GUIUtility.hotControl == id:
                {
                    float t = T(e.mousePosition.x);
                    switch (_drag)
                    {
                        case Drag.Scrub: Scrub(step, t); break;
                        case Drag.HitStart: step.HitStart = Mathf.Min(t, step.HitEnd - 0.02f); break;
                        case Drag.HitEnd: step.HitEnd = Mathf.Max(t, step.HitStart + 0.02f); break;
                        case Drag.Cancel: step.CancelPoint = t; break;
                        case Drag.Key:
                            if (_dragKey != null) { _dragKey.Time = t; Scrub(step, t); }
                            break;
                        case Drag.Event:
                            if (_dragEvent != null) _dragEvent.Time = t;
                            break;
                    }
                    GUI.changed = true;
                    e.Use();
                    Repaint();
                    break;
                }

            case EventType.MouseUp when GUIUtility.hotControl == id:
                {
                    if (_drag == Drag.Key && _dragKey != null)
                    {
                        step.Motion.SortKeys();
                        _selIndex = step.Motion.Keys.IndexOf(_dragKey);
                    }
                    _drag = Drag.None;
                    _dragKey = null;
                    _dragEvent = null;
                    GUIUtility.hotControl = 0;
                    EditorUtility.SetDirty(_target);
                    e.Use();
                    break;
                }

            case EventType.ContextClick when area.Contains(e.mousePosition):
                {
                    ShowContextMenu(step, T(e.mousePosition.x), e.mousePosition, keys, evs, X);
                    e.Use();
                    break;
                }
        }
    }

    static bool Nearest(int count, Func<int, float> xOf, float mx, float within, out int index)
    {
        index = -1;
        float best = within;
        for (int i = 0; i < count; i++)
        {
            float d = Mathf.Abs(xOf(i) - mx);
            if (d <= best) { best = d; index = i; }
        }
        return index >= 0;
    }

    void Scrub(ComboStep step, float t)
    {
        _playing = false;
        _playT = t * step.TotalTime;
        RefreshPreview();
    }

    void ShowContextMenu(ComboStep step, float t, Vector2 mouse, Rect keys, Rect evs, Func<float, float> X)
    {
        var menu = new GenericMenu();

        bool onKey = keys.Contains(mouse) && Nearest(step.Motion.Keys.Count, i => X(step.Motion.Keys[i].Time), mouse.x, 8f, out int ki);
        bool onEvent = evs.Contains(mouse) && Nearest(step.Events.Length, i => step.Events[i] != null ? X(step.Events[i].Time) : -999, mouse.x, 9f, out int ei);

        // 컴파일러가 위 out 값을 '항상 할당됨'으로 보지 않아서 한 번 더 구함 (onKey/onEvent가 true일 때만 사용)
        Nearest(step.Motion.Keys.Count, i => X(step.Motion.Keys[i].Time), mouse.x, 8f, out ki);
        Nearest(step.Events.Length, i => step.Events[i] != null ? X(step.Events[i].Time) : -999, mouse.x, 9f, out ei);

        if (onKey)
        {
            var key = step.Motion.Keys[ki];
            menu.AddItem(new GUIContent("키 복제"), false, () => DuplicateKey(step, key));
            menu.AddItem(new GUIContent("키 삭제"), false, () => DeleteKey(step, key));
            menu.AddSeparator("");
        }
        if (onEvent)
        {
            var ev = step.Events[ei];
            menu.AddItem(new GUIContent("이벤트 복제"), false, () => DuplicateEvent(step, ev));
            menu.AddItem(new GUIContent("이벤트 삭제"), false, () => DeleteEvent(step, ev));
            menu.AddSeparator("");
        }

        menu.AddItem(new GUIContent($"여기({t * step.Duration:0.00}s)에 키 — 현재 씬 포즈"), false,
            () => AddKeyAt(step, t, CaptureCurrentPose()));
        menu.AddItem(new GUIContent("여기에 키 — 이 시점 모션 그대로"), false,
            () => AddKeyAt(step, t, EvaluateAt(step, t)));

        foreach (MotionEventType type in Enum.GetValues(typeof(MotionEventType)))
        {
            var m = Meta(type);
            var tt = type;
            menu.AddItem(new GUIContent($"이벤트 추가/{m.icon}  {m.label}"), false, () => AddEventAt(step, t, tt));
        }

        menu.ShowAsContext();
    }

    void ShowAddEventMenu(ComboStep step, float t)
    {
        var menu = new GenericMenu();
        foreach (MotionEventType type in Enum.GetValues(typeof(MotionEventType)))
        {
            var m = Meta(type);
            var tt = type;
            menu.AddItem(new GUIContent($"{m.icon}  {m.label}"), false, () => AddEventAt(step, t, tt));
        }
        menu.ShowAsContext();
    }

    ArmPose EvaluateAt(ComboStep step, float t)
    {
        var idle = _rig.GetEffectiveIdle();
        return step.Motion != null && step.Motion.IsValid ? step.Motion.Evaluate(t, idle) : idle.Clone();
    }

    const string ShortcutHelp =
        "K 키 추가 · Shift+K 씬 포즈로 키 · E 이벤트 추가 · D 복제 · Delete 삭제\n" +
        "Ctrl+C / Ctrl+V 키 포즈 복사·붙여넣기 · ← → 이전/다음 키 · Esc 선택 해제 · Space 재생";

    /// <summary>타임라인 바로 아래 — 선택 상태와 상관없이 항상 보이는 추가 버튼</summary>
    void DrawAddBar(ComboStep step)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("+ 키", "K — 선택한 키 다음(또는 재생 위치)에 지금 모션 그대로 키를 끼워 넣음"), EditorStyles.miniButtonLeft))
                AddInterpolatedKey(step);
            if (GUILayout.Button(new GUIContent("+ 씬 포즈로 키", "Shift+K — 재생 위치에 지금 씬 포즈로 키"), EditorStyles.miniButtonMid))
                AddKeyAt(step, NowT(step), CaptureCurrentPose());
            if (GUILayout.Button(new GUIContent("+ 이벤트 ▾", "E — 재생 위치(또는 선택한 키 시점)에 이벤트"), EditorStyles.miniButtonMid))
                ShowAddEventMenu(step, EventTime(step));

            using (new EditorGUI.DisabledScope(_sel == Sel.None))
                if (GUILayout.Button(new GUIContent("복제", "D"), EditorStyles.miniButtonRight, GUILayout.Width(44)))
                    DuplicateSelection(step);

            GUILayout.FlexibleSpace();
            GUILayout.Label(new GUIContent("단축키 ?", ShortcutHelp), EditorStyles.miniLabel);
        }
    }

    /// <summary>새 키 시점 — 선택한 키와 다음 키 사이, 없으면 재생 위치 (같은 자리에 키가 있으면 옆으로)</summary>
    float NewKeyTime(ComboStep step)
    {
        var keys = step.Motion.Keys;

        if (_sel == Sel.Key && _selIndex >= 0 && _selIndex < keys.Count)
        {
            float cur = keys[_selIndex].Time;
            float next = _selIndex + 1 < keys.Count ? keys[_selIndex + 1].Time : 1f;
            return next - cur > 0.01f ? (cur + next) * 0.5f : Mathf.Clamp01(cur + 0.05f);
        }

        float t = NowT(step);
        for (int i = 0; i < keys.Count; i++)
            if (Mathf.Abs(keys[i].Time - t) < 0.005f)
            {
                float next = i + 1 < keys.Count ? keys[i + 1].Time : 1f;
                return (t + next) * 0.5f;
            }
        return t;
    }

    float EventTime(ComboStep step) =>
        _sel == Sel.Key && _selIndex >= 0 && _selIndex < step.Motion.Keys.Count
            ? step.Motion.Keys[_selIndex].Time
            : NowT(step);

    /// <summary>모션 모양을 바꾸지 않고 키만 끼워 넣음 — 그 다음 원하는 대로 수정</summary>
    void AddInterpolatedKey(ComboStep step)
    {
        float t = NewKeyTime(step);
        AddKeyAt(step, t, EvaluateAt(step, t));
    }

    void DuplicateSelection(ComboStep step)
    {
        if (_sel == Sel.Key && _selIndex >= 0 && _selIndex < step.Motion.Keys.Count)
            DuplicateKey(step, step.Motion.Keys[_selIndex]);
        else if (_sel == Sel.Event && _selIndex >= 0 && _selIndex < step.Events.Length)
            DuplicateEvent(step, step.Events[_selIndex]);
    }

    void HandleShortcuts(ComboStep step)
    {
        var e = Event.current;

        // Ctrl+C / Ctrl+V / Ctrl+D — 키를 선택했고 글자 입력 중이 아닐 때만 (아니면 Unity 기본 동작)
        if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand)
            && !EditorGUIUtility.editingTextField
            && _sel == Sel.Key && _selIndex >= 0 && _selIndex < step.Motion.Keys.Count)
        {
            string cmd = e.commandName;
            bool ours = cmd == "Copy" || cmd == "Duplicate" || (cmd == "Paste" && TryGetClipPose(out _));
            if (!ours) return;

            if (e.type == EventType.ExecuteCommand)
            {
                var k = step.Motion.Keys[_selIndex];
                if (cmd == "Copy") CopyPose(k.Pose);
                else if (cmd == "Paste" && TryGetClipPose(out var clip)) PasteInto(step, _selIndex, _selIndex, clip, PosePart.All);
                else if (cmd == "Duplicate") DuplicateKey(step, k);
            }
            e.Use();
            return;
        }

        if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
        if (e.control || e.command || e.alt) return;      // Ctrl+Z 등은 Unity에 맡김

        var keys = step.Motion.Keys;

        switch (e.keyCode)
        {
            case KeyCode.Delete:
            case KeyCode.Backspace:
                if (_sel == Sel.Key && _selIndex < keys.Count) DeleteKey(step, keys[_selIndex]);
                else if (_sel == Sel.Event && _selIndex < step.Events.Length) DeleteEvent(step, step.Events[_selIndex]);
                else return;
                break;

            case KeyCode.K:
                if (e.shift) AddKeyAt(step, NowT(step), CaptureCurrentPose());
                else AddInterpolatedKey(step);
                break;

            case KeyCode.E:
                ShowAddEventMenu(step, EventTime(step));
                break;

            case KeyCode.D:
                DuplicateSelection(step);
                break;

            case KeyCode.LeftArrow:
            case KeyCode.RightArrow:
                {
                    if (keys.Count == 0) return;
                    int dir = e.keyCode == KeyCode.RightArrow ? 1 : -1;
                    int next;

                    if (_sel == Sel.Key) next = Mathf.Clamp(_selIndex + dir, 0, keys.Count - 1);
                    else
                    {
                        // 선택이 없으면 재생 위치 기준 앞/뒤 키
                        float t = NowT(step);
                        next = dir > 0 ? keys.FindIndex(k => k.Time > t + 0.001f) : keys.FindLastIndex(k => k.Time < t - 0.001f);
                        if (next < 0) next = dir > 0 ? keys.Count - 1 : 0;
                    }
                    Select(Sel.Key, next, step);
                    break;
                }

            case KeyCode.Escape:
                if (_sel == Sel.None) return;
                ClearSelection();
                break;

            case KeyCode.Space:
                TogglePlay();
                break;

            default:
                return;
        }

        e.Use();
        Repaint();
    }

    // ─────────── 선택 · 추가 · 삭제 ───────────

    void Select(Sel kind, int index, ComboStep step)
    {
        _sel = kind;
        _selIndex = index;

        if (kind == Sel.Key && index >= 0 && index < step.Motion.Keys.Count)
            Scrub(step, step.Motion.Keys[index].Time);
        else if (kind == Sel.Event && index >= 0 && index < step.Events.Length)
            Scrub(step, step.Events[index].Time);

        GUI.FocusControl(null);
    }

    void ClearSelection()
    {
        _sel = Sel.None;
        _selIndex = -1;
    }

    void AddKeyAt(ComboStep step, float t, ArmPose pose)
    {
        var key = new PoseKey { Time = t, Pose = pose };
        Modify("Add Key", () =>
        {
            step.Motion.Keys.Add(key);
            step.Motion.SortKeys();
        });
        Select(Sel.Key, step.Motion.Keys.IndexOf(key), step);
    }

    void DuplicateKey(ComboStep step, PoseKey src)
    {
        var copy = new PoseKey
        {
            Time = Mathf.Clamp01(src.Time + 0.05f),
            Pose = src.Pose.Clone(),
            Ease = src.Ease != null ? new AnimationCurve(src.Ease.keys) : AnimationCurve.EaseInOut(0, 0, 1, 1),
            Hold = src.Hold,
        };
        Modify("Duplicate Key", () =>
        {
            step.Motion.Keys.Add(copy);
            step.Motion.SortKeys();
        });
        Select(Sel.Key, step.Motion.Keys.IndexOf(copy), step);
    }

    void DeleteKey(ComboStep step, PoseKey key)
    {
        Modify("Delete Key", () => step.Motion.Keys.Remove(key));
        ClearSelection();
    }

    void AddEventAt(ComboStep step, float t, MotionEventType type)
    {
        var ev = new MotionEvent { Time = t, Type = type };
        if (type == MotionEventType.Effect || type == MotionEventType.LightFlash) ev.Space = EventSpace.AttachToWeapon;
        if (type == MotionEventType.GroundEffect) ev.PositionOffset = new Vector3(0f, 0f, 1f);

        Modify("Add Event", () =>
        {
            var list = new List<MotionEvent>(step.Events) { ev };
            step.Events = list.ToArray();
        });
        Select(Sel.Event, Array.IndexOf(step.Events, ev), step);
    }

    void DuplicateEvent(ComboStep step, MotionEvent src)
    {
        var copy = src.Clone();
        copy.Time = Mathf.Clamp01(src.Time + 0.03f);
        Modify("Duplicate Event", () =>
        {
            var list = new List<MotionEvent>(step.Events) { copy };
            step.Events = list.ToArray();
        });
        Select(Sel.Event, Array.IndexOf(step.Events, copy), step);
    }

    void DeleteEvent(ComboStep step, MotionEvent ev)
    {
        Modify("Delete Event", () =>
        {
            var list = new List<MotionEvent>(step.Events);
            list.Remove(ev);
            step.Events = list.ToArray();
        });
        ClearSelection();
    }

    // ─────────── 재생 ───────────

    void DrawPlayback(ComboStep step)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(_playing ? "■ 정지" : "▶ 재생", GUILayout.Height(24), GUILayout.Width(70)))
                TogglePlay();

            using (new EditorGUI.DisabledScope(_source.ComboLength <= 1))
                if (GUILayout.Button("▶▶ 전체", GUILayout.Height(24), GUILayout.Width(66)))
                {
                    _playing = true;
                    _playAll = true;
                    _comboIndex = 0;
                    _playT = 0f;
                    _tipTrail.Clear();
                }

            _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(24));

            GUILayout.Label("속도", GUILayout.Width(28));
            _playSpeed = EditorGUILayout.Slider(_playSpeed, 0.05f, 2f);
        }

        EditorGUILayout.LabelField(
            $"선딜 {step.WindUp:F2}s · 판정 {step.ActiveWindow:F2}s · 후딜 {step.Recovery:F2}s · 캔슬 {step.CancelTime:F2}s",
            EditorStyles.miniLabel);

        if (step.ActiveWindow < 0.05f)
            EditorGUILayout.HelpBox($"판정 구간이 {step.ActiveWindow:F3}초로 너무 짧습니다. 0.1초 이상 권장.", MessageType.Warning);
    }

    void TogglePlay()
    {
        _playing = !_playing;
        _playAll = false;
        if (_playing) { _playT = 0f; _tipTrail.Clear(); }
        else RefreshPreview();
    }

    // ─────────── 키 인스펙터 ───────────

    void DrawKeyInspector(ComboStep step, int index)
    {
        var motion = step.Motion;
        var k = motion.Keys[index];
        var p = k.Pose;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"◆ 키 {index}  ·  {k.Time * step.Duration:F2}s", EditorStyles.boldLabel);
            if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.Width(24)) && index > 0) Select(Sel.Key, index - 1, step);
            if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.Width(24)) && index < motion.Keys.Count - 1) Select(Sel.Key, index + 1, step);
        }

        EditorGUI.BeginChangeCheck();
        float nt = EditorGUILayout.Slider("시점", k.Time, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            k.Time = nt;
            motion.SortKeys();
            _selIndex = motion.Keys.IndexOf(k);
            Scrub(step, nt);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("씬 포즈 저장", EditorStyles.miniButtonLeft))
            {
                var cap = CaptureCurrentPose();
                cap.CamPos = p.CamPos; cap.CamRot = p.CamRot; cap.CamFov = p.CamFov;   // 카메라는 유지
                Modify("Capture Pose", () => k.Pose = cap);       // 저장 표시 · 되돌리기
                PreviewPose(cap);
            }
            if (GUILayout.Button("Idle 복사", EditorStyles.miniButtonMid))
            {
                var idle = _rig.GetEffectiveIdle().Clone();
                Modify("Copy Idle", () => k.Pose = idle);
                PreviewPose(k.Pose);
            }
            using (new EditorGUI.DisabledScope(index == 0))
                if (GUILayout.Button("앞 키 복사", EditorStyles.miniButtonMid))
                {
                    var prev = motion.Keys[index - 1].Pose.Clone();
                    Modify("Copy Prev Key", () => k.Pose = prev);
                    PreviewPose(k.Pose);
                }
            if (GUILayout.Button("복제", EditorStyles.miniButtonMid)) { DuplicateKey(step, k); return; }
            if (GUILayout.Button("삭제", EditorStyles.miniButtonRight)) { DeleteKey(step, k); return; }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("포즈 복사", "Ctrl+C — 이 키 포즈 전체를 클립보드로"), EditorStyles.miniButtonLeft))
                CopyPose(k.Pose);
            if (GUILayout.Button(new GUIContent("붙여넣기 ▾", "Ctrl+V = 전체. 메뉴에서 손·손가락·몸 등 일부만"), EditorStyles.miniButtonMid))
                ShowPasteMenu(step, index);
            using (new EditorGUI.DisabledScope(index >= motion.Keys.Count - 1))
                if (GUILayout.Button(new GUIContent("다음 키로 보내기 ▾", "이 키 포즈(또는 일부)를 다음 키·뒤의 모든 키에 그대로"), EditorStyles.miniButtonRight))
                    ShowSendMenu(step, index);
        }

        p = k.Pose;
        EditorGUI.BeginChangeCheck();

        if (Fold("키 · 손", true))
            using (Box())
            {
                p.RightPos = EditorGUILayout.Vector3Field("오른손 위치", p.RightPos);
                p.RightRot = EditorGUILayout.Vector3Field("오른손 회전", p.RightRot);

                p.UseLeftHand = EditorGUILayout.Toggle("왼손 표시", p.UseLeftHand);
                if (p.UseLeftHand)
                {
                    p.LeftPos = EditorGUILayout.Vector3Field("왼손 위치", p.LeftPos);
                    p.LeftRot = EditorGUILayout.Vector3Field("왼손 회전", p.LeftRot);
                    if (GUILayout.Button("오른손 좌우 반전 복사", EditorStyles.miniButton))
                    {
                        p.LeftPos = new Vector3(-p.RightPos.x, p.RightPos.y, p.RightPos.z);
                        p.LeftRot = new Vector3(p.RightRot.x, -p.RightRot.y, -p.RightRot.z);
                    }
                }
            }

        if (Fold("키 · 무기 · 카메라", false))
            using (Box())
            {
                p.WeaponPos = EditorGUILayout.Vector3Field("무기 추가 위치", p.WeaponPos);
                p.WeaponRot = EditorGUILayout.Vector3Field("무기 추가 회전", p.WeaponRot);
                using (new EditorGUILayout.HorizontalScope())
                {
                    p.WeaponScale = EditorGUILayout.Vector3Field(new GUIContent("무기 크기 (배율)",
                        "무기 기본 크기에 곱함. 1 = 그대로, 2 = 두 배. 0인 축은 1로 봄 — 키 사이는 부드럽게 커지고 작아짐"),
                        ArmPose.Scl(p.WeaponScale));
                    if (GUILayout.Button(new GUIContent("1", "원래 크기로"), EditorStyles.miniButton, GUILayout.Width(22)))
                        p.WeaponScale = Vector3.one;
                }
                DrawKeyExtraParts(p);
                EditorGUILayout.Space(2);
                p.CamPos = EditorGUILayout.Vector3Field("카메라 위치", p.CamPos);
                p.CamRot = EditorGUILayout.Vector3Field("카메라 회전", p.CamRot);
                p.CamFov = EditorGUILayout.FloatField("FOV 추가", p.CamFov);
                Hint("카메라는 본인 화면만 움직입니다.");
            }

        if (Fold("키 · 몸 · 머리 (다른 사람에게 보임)", true))
            using (Box()) DrawBodyFields(p);

        if (Fold("키 · 보간", motion.Interp == MotionInterp.Smooth))
            using (Box())
            {
                if (motion.Interp == MotionInterp.Smooth)
                {
                    bool ends = index == 0 || index == motion.Keys.Count - 1;
                    using (new EditorGUI.DisabledScope(ends))
                        k.Hold = EditorGUILayout.Toggle(new GUIContent("여기서 멈춤",
                            "켜면 이 키에서 속도 0 (치켜든 자세에서 잠깐 멈췄다 휘두르기 등).\n끄면 멈추지 않고 그대로 통과"), ends || k.Hold);
                    Hint(ends ? "첫·끝 키는 항상 멈춤" :
                         k.Hold ? "이 키에서 잠깐 멈췄다 출발" : "이 키를 멈추지 않고 통과 — 휘두르는 중간 지점용");
                }
                else
                {
                    k.Ease = EditorGUILayout.CurveField("이 키로 오는 곡선", k.Ease);
                    Hint("중간 키에서 투툭 끊기면 위의 보간을 '부드럽게'로 바꿔보세요.");
                }
            }

        if (EditorGUI.EndChangeCheck()) PreviewPose(p);

        if (Fold("키 · 손가락", false))
            using (Box()) DrawKeyFingers(p);
    }

    void DrawBodyFields(ArmPose p)
    {
        if (_rig.Body == null && _rig.Head == null)
            EditorGUILayout.HelpBox(
                "FPArmRig의 Body · Head가 비어 있어 미리보기에 안 보입니다. 값은 저장됩니다.\n" +
                "플레이어 프리팹: Body = Player/Body. 미리보기 씬에도 같은 Body를 두고 연결하세요.",
                MessageType.Info);

        p.BodyPos = EditorGUILayout.Vector3Field("몸 위치", p.BodyPos);
        p.BodyRot = EditorGUILayout.Vector3Field("몸 회전", p.BodyRot);
        p.HeadPos = EditorGUILayout.Vector3Field("머리 위치", p.HeadPos);
        p.HeadRot = EditorGUILayout.Vector3Field("머리 회전", p.HeadRot);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_rig.Body == null))
                if (GUILayout.Button("몸 선택", EditorStyles.miniButtonLeft)) Selection.activeTransform = _rig.Body;
            using (new EditorGUI.DisabledScope(_rig.Head == null))
                if (GUILayout.Button("머리 선택", EditorStyles.miniButtonMid)) Selection.activeTransform = _rig.Head;
            if (GUILayout.Button(new GUIContent("씬 몸·머리 저장", "씬에서 몸·머리를 직접 움직인 뒤 누르기"), EditorStyles.miniButtonMid))
            {
                _rig.GetBodyOffset(out var bp, out var br, out var hp, out var hr);
                p.BodyPos = bp; p.BodyRot = NormalizeEuler(br);
                p.HeadPos = hp; p.HeadRot = NormalizeEuler(hr);
                GUI.changed = true;
            }
            if (GUILayout.Button("0으로", EditorStyles.miniButtonRight))
            {
                p.BodyPos = p.BodyRot = p.HeadPos = p.HeadRot = Vector3.zero;
                GUI.changed = true;
            }
        }
        Hint("기준 자세 대비 오프셋입니다. 회전 Y = 허리 비틀기, X = 숙이기, Z = 기울이기.");
    }

    void DrawKeyFingers(ArmPose p)
    {
        bool prev = p.UseWeaponGrip;
        p.UseWeaponGrip = EditorGUILayout.Toggle(
            new GUIContent("무기 그립 사용", "끄면 이 키에서 손 모양을 직접 지정"), p.UseWeaponGrip);
        if (prev != p.UseWeaponGrip) PreviewPose(p);
        if (p.UseWeaponGrip) return;

        Vector3 axis = GetCurlAxis();

        EditorGUILayout.LabelField("오른손", EditorStyles.miniBoldLabel);
        p.RightFingers ??= HandPose.Relaxed(axis);
        DrawFingerPresets(p, axis, true);
        DrawFingerClipRow(p, true, _rig.RightHandRig);
        if (_rig.RightHandRig != null)
        {
            DrawSceneEditToggle(p.RightFingers, _rig.RightHandRig, _target);
            DrawFingerSliders(p.RightFingers, axis, _rig.RightHandRig, _target);
        }

        if (!p.UseLeftHand) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("왼손", EditorStyles.miniBoldLabel);
        p.LeftFingers ??= HandPose.Relaxed(axis);
        DrawFingerPresets(p, axis, false);
        DrawFingerClipRow(p, false, _rig.LeftHandRig);
        if (_rig.LeftHandRig != null)
        {
            DrawSceneEditToggle(p.LeftFingers, _rig.LeftHandRig, _target);
            DrawFingerSliders(p.LeftFingers, axis, _rig.LeftHandRig, _target);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("오른손 → 왼손", EditorStyles.miniButtonLeft))
            {
                p.LeftFingers = p.RightFingers?.Clone() ?? HandPose.Relaxed(axis);
                EditorUtility.SetDirty(_target);
                PreviewPose(p);
            }
            if (GUILayout.Button("왼손 → 오른손", EditorStyles.miniButtonRight))
            {
                p.RightFingers = p.LeftFingers?.Clone() ?? HandPose.Relaxed(axis);
                EditorUtility.SetDirty(_target);
                PreviewPose(p);
            }
        }
    }

    /// <summary>손 하나의 손가락 — 씬에서 저장 / 복사 / 붙여넣기</summary>
    void DrawFingerClipRow(ArmPose p, bool right, HandRig rig)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(rig == null))
                if (GUILayout.Button(new GUIContent("씬 손가락 저장", "씬에서 IK·회전으로 만든 손 모양을 이 키에"), EditorStyles.miniButtonLeft))
                {
                    var hp = new HandPose();
                    rig.BakeAll(hp);
                    if (right) p.RightFingers = hp; else p.LeftFingers = hp;
                    EditorUtility.SetDirty(_target);
                }

            if (GUILayout.Button(new GUIContent("손가락 복사", "이 손 모양만 클립보드로 — 다른 키·다른 손에 붙여넣기"), EditorStyles.miniButtonMid))
                CopyHand(right ? p.RightFingers : p.LeftFingers);

            bool has = TryGetClipHand(out var clip);
            using (new EditorGUI.DisabledScope(!has))
                if (GUILayout.Button("붙여넣기", EditorStyles.miniButtonRight))
                {
                    if (right) p.RightFingers = clip.Clone(); else p.LeftFingers = clip.Clone();
                    EditorUtility.SetDirty(_target);
                    PreviewPose(p);
                }
        }
    }

    void DrawFingerPresets(ArmPose p, Vector3 axis, bool right)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            HandPose np = null;
            if (GUILayout.Button("펌", EditorStyles.miniButtonLeft)) np = HandPose.Open(axis);
            if (GUILayout.Button("자연", EditorStyles.miniButtonMid)) np = HandPose.Relaxed(axis);
            if (GUILayout.Button("쥠", EditorStyles.miniButtonMid)) np = HandPose.Grip(axis);
            if (GUILayout.Button("주먹", EditorStyles.miniButtonRight)) np = HandPose.Fist(axis);

            if (np == null) return;
            if (right) p.RightFingers = np; else p.LeftFingers = np;
            EditorUtility.SetDirty(_target);
            PreviewPose(p);
        }
    }

    // ─────────── 이벤트 인스펙터 ───────────

    void DrawEventInspector(ComboStep step, int index)
    {
        var ev = step.Events[index];
        var m = Meta(ev.Type);

        using (new EditorGUILayout.HorizontalScope())
        {
            var st = new GUIStyle(EditorStyles.boldLabel);
            st.normal.textColor = m.color;
            EditorGUILayout.LabelField($"{m.icon}  {m.label}  ·  {ev.Time * step.Duration:F2}s", st);
            if (GUILayout.Button("복제", EditorStyles.miniButtonLeft, GUILayout.Width(40))) { DuplicateEvent(step, ev); return; }
            if (GUILayout.Button("삭제", EditorStyles.miniButtonRight, GUILayout.Width(40))) { DeleteEvent(step, ev); return; }
        }

        ev.Time = EditorGUILayout.Slider("시점", ev.Time, 0f, 1f);
        ev.Type = (MotionEventType)EditorGUILayout.EnumPopup("종류", ev.Type);

        EditorGUILayout.Space(2);
        DrawEventFields(ev);

        EditorGUILayout.Space(2);
        if (LocalOnly(ev.Type))
            Hint("본인 화면에서만 재생됩니다.");
        else
            using (new EditorGUILayout.HorizontalScope())
            {
                ev.ShowFirstPerson = EditorGUILayout.ToggleLeft("본인 화면", ev.ShowFirstPerson, GUILayout.Width(90));
                ev.ShowThirdPerson = EditorGUILayout.ToggleLeft("다른 사람 화면", ev.ShowThirdPerson);
            }
    }

    /// <summary>종류별 입력 칸. 새 종류는 여기에 case 하나 추가</summary>
    void DrawEventFields(MotionEvent ev)
    {
        switch (ev.Type)
        {
            case MotionEventType.Sound:
                ev.Clip = (AudioClip)EditorGUILayout.ObjectField("사운드", ev.Clip, typeof(AudioClip), false);
                ev.Volume = EditorGUILayout.Slider("볼륨", ev.Volume, 0f, 1f);
                ev.Pitch = EditorGUILayout.Slider("피치", ev.Pitch, 0.5f, 1.5f);
                break;

            case MotionEventType.RandomSound:
                DrawClipArray(ev);
                ev.Volume = EditorGUILayout.Slider("볼륨", ev.Volume, 0f, 1f);
                ev.Pitch = EditorGUILayout.Slider("피치", ev.Pitch, 0.5f, 1.5f);
                ev.PitchJitter = EditorGUILayout.Slider("피치 랜덤 ±", ev.PitchJitter, 0f, 0.5f);
                break;

            case MotionEventType.Effect:
                ev.Prefab = (GameObject)EditorGUILayout.ObjectField("프리팹", ev.Prefab, typeof(GameObject), false);
                ev.Space = (EventSpace)EditorGUILayout.EnumPopup("붙을 곳", ev.Space);
                ev.PositionOffset = EditorGUILayout.Vector3Field("위치", ev.PositionOffset);
                ev.RotationOffset = EditorGUILayout.Vector3Field("회전", ev.RotationOffset);
                ev.Scale = EditorGUILayout.FloatField("크기", ev.Scale);
                ev.Lifetime = EditorGUILayout.FloatField("수명 (초, 0 = 프리팹이 정리)", ev.Lifetime);
                DrawRigStateField(ev);
                break;

            case MotionEventType.GroundEffect:
                ev.Prefab = (GameObject)EditorGUILayout.ObjectField("프리팹", ev.Prefab, typeof(GameObject), false);
                ev.Vfx = (ToonVfx.VfxEffect)EditorGUILayout.ObjectField(new GUIContent("VFX 이펙트", "넣으면 프리팹 대신 이걸 씀 (풀링)"),
                                                       ev.Vfx, typeof(ToonVfx.VfxEffect), false);
                ev.PositionOffset = EditorGUILayout.Vector3Field("발밑 기준 위치", ev.PositionOffset);
                ev.RotationOffset = EditorGUILayout.Vector3Field("회전", ev.RotationOffset);
                ev.Scale = EditorGUILayout.FloatField("크기", ev.Scale);
                ev.Lifetime = EditorGUILayout.FloatField("수명 (초)", ev.Lifetime);
                Hint("플레이어 기준 위치(Z = 앞)에서 바닥을 찾아 바닥 기울기에 맞춰 생성합니다. 충격파·바닥 자국용.");
                break;


            case MotionEventType.TrailStart:
            case MotionEventType.TrailEnd:
                Hint("무기 끝(WeaponRig.Tip) 아래의 TrailRenderer를 켜고 끕니다.");
                break;

            case MotionEventType.CameraShake:
                ev.ShakeAmount = EditorGUILayout.FloatField("강도", ev.ShakeAmount);
                break;

            case MotionEventType.ScreenFlash:
                ev.FlashColor = EditorGUILayout.ColorField("색 (알파 = 진하기)", ev.FlashColor);
                ev.FlashDuration = EditorGUILayout.FloatField("지속 (초)", ev.FlashDuration);
                break;

            case MotionEventType.FovKick:
                ev.FovAmount = EditorGUILayout.FloatField("시야각 변화 (− 좁아짐)", ev.FovAmount);
                break;

            case MotionEventType.LightFlash:
                ev.Space = (EventSpace)EditorGUILayout.EnumPopup("붙을 곳", ev.Space);
                ev.PositionOffset = EditorGUILayout.Vector3Field("위치", ev.PositionOffset);
                ev.FlashColor = EditorGUILayout.ColorField("색", ev.FlashColor);
                ev.LightIntensity = EditorGUILayout.FloatField("밝기", ev.LightIntensity);
                ev.LightRange = EditorGUILayout.FloatField("범위 (m)", ev.LightRange);
                ev.FlashDuration = EditorGUILayout.FloatField("지속 (초)", ev.FlashDuration);
                break;

            case MotionEventType.Custom:
                ev.Key = EditorGUILayout.TextField("키", ev.Key);
                Hint("MotionEventPlayer.OnCustomEvent로 이 문자열이 전달됩니다.");
                break;

            case MotionEventType.Vfx:
                ev.Vfx = (ToonVfx.VfxEffect)EditorGUILayout.ObjectField("VFX 이펙트", ev.Vfx, typeof(ToonVfx.VfxEffect), false);
                ev.Space = (EventSpace)EditorGUILayout.EnumPopup("붙을 곳", ev.Space);
                ev.PositionOffset = EditorGUILayout.Vector3Field("위치", ev.PositionOffset);
                ev.RotationOffset = EditorGUILayout.Vector3Field("회전", ev.RotationOffset);
                ev.Scale = EditorGUILayout.FloatField("크기", ev.Scale);
                ev.FitToHit = EditorGUILayout.Toggle(new GUIContent("판정 범위에 맞추기",
                    "호의 반지름 · 각도를 이 타격의 실제 판정 범위에 맞춤 (사거리 버프 반영)"), ev.FitToHit);
                ev.VfxPalette = (ToonVfx.VfxPalette)EditorGUILayout.ObjectField("색 (비우면 기본)", ev.VfxPalette, typeof(ToonVfx.VfxPalette), false);
                break;
        }
    }

    void DrawClipArray(MotionEvent ev)
    {
        ev.Clips ??= new AudioClip[0];

        int n = Mathf.Max(0, EditorGUILayout.IntField("소리 개수", ev.Clips.Length));
        if (n != ev.Clips.Length) Array.Resize(ref ev.Clips, n);

        for (int i = 0; i < ev.Clips.Length; i++)
            ev.Clips[i] = (AudioClip)EditorGUILayout.ObjectField($"  {i + 1}", ev.Clips[i], typeof(AudioClip), false);
    }

    // ─────────── 타격 설정 ───────────

    void DrawStepSettings(ComboStep step)
    {
        EditorGUILayout.LabelField("무기", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        step.WeaponOverride = (EquipmentData)EditorGUILayout.ObjectField(new GUIContent("이 모션 동안 쥘 무기",
            "모션이 재생되는 동안만 이 무기 모델로 바꿔 쥠 (그립·위치는 그 무기 설정을 따름).\n" +
            "모델만 바뀌고 판정·스탯은 실제 무기 그대로. 비우면 들고 있는 무기"), step.WeaponOverride, typeof(EquipmentData), false);
        if (EditorGUI.EndChangeCheck()) SyncPreviewWeapon();
        if (step.WeaponOverride != null) Hint("모션이 끝나면 원래 무기로 돌아갑니다. 판정 범위 미리보기는 원래 무기 기준.");
        EditorGUI.BeginChangeCheck();
        step.HideWeapon = EditorGUILayout.Toggle(new GUIContent("무기 숨기기",
            "이 모션 동안 무기 모델을 숨김 — 맨손 스킬. 판정·스탯은 실제 무기 그대로"), step.HideWeapon);
        if (EditorGUI.EndChangeCheck()) RefreshPreview();

        EditorGUI.BeginChangeCheck();
        step.ContinueFromPrevious = EditorGUILayout.Toggle(new GUIContent("이전 타 위치에서 시작",
            "첫 키 자리에 '이전 타를 누른 순간의 자세'(검 위치 등)를 넣고, 두 번째 키까지 가며 원래 모션으로 이어짐.\n" +
            "미리보기는 앞 타의 캔슬 지점 자세에서 시작하는 걸로 보여 줌"), step.ContinueFromPrevious);
        if (EditorGUI.EndChangeCheck()) RefreshPreview();
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("타이밍 (비율 0~1 — 타임라인에서 드래그해도 됨)", EditorStyles.miniBoldLabel);
        step.HitStart = EditorGUILayout.Slider("판정 시작", step.HitStart, 0f, 1f);
        step.HitEnd = EditorGUILayout.Slider("판정 끝", step.HitEnd, 0f, 1f);
        step.CancelPoint = EditorGUILayout.Slider("캔슬 지점", step.CancelPoint, 0f, 1f);
        step.ComboResetTime = EditorGUILayout.FloatField("콤보 유지 (초)", step.ComboResetTime);
        step.HitEnd = Mathf.Max(step.HitEnd, step.HitStart + 0.02f);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("피해", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            step.DamageMultiplier = EditorGUILayout.FloatField("피해×", step.DamageMultiplier);
            step.RangeMultiplier = EditorGUILayout.FloatField("사거리×", step.RangeMultiplier);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            step.MaxTargets = Mathf.Max(0, EditorGUILayout.IntField(
                new GUIContent("최대 대상", "0 = 제한 없음 (범위 안 전부)"), step.MaxTargets));
            step.LungeDistance = EditorGUILayout.FloatField("전진 (m)", step.LungeDistance);
        }
        step.NoWeaponEnchants = EditorGUILayout.Toggle(
            new GUIContent("특수 인챈트 끄기", "이 타격에는 무기의 특수 인챈트 효과가 붙지 않음"), step.NoWeaponEnchants);

        EditorGUILayout.Space(4);
        step.OverrideHitShape = EditorGUILayout.Toggle(
            new GUIContent("판정 범위 개별 설정", "체크하면 무기 값 대신 이 값 사용"), step.OverrideHitShape);

        if (step.OverrideHitShape)
        {
            EditorGUI.indentLevel++;
            step.HitMode = (WeaponHitMode)EditorGUILayout.EnumPopup("방식", step.HitMode);
            step.Range = EditorGUILayout.FloatField("사거리", step.Range);
            if (step.HitMode == WeaponHitMode.ForwardArc)
                step.HitAngle = EditorGUILayout.Slider("각도", step.HitAngle, 20f, 360f);
            step.HitHeight = EditorGUILayout.FloatField("수직 허용", step.HitHeight);
            step.MinRange = EditorGUILayout.FloatField("최소 사거리", step.MinRange);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("주변 구체", EditorStyles.miniButtonLeft))
                { step.HitMode = WeaponHitMode.ForwardSphere; step.Range = 2f; step.HitHeight = 1.5f; }
                if (GUILayout.Button("전방 넓게", EditorStyles.miniButtonMid))
                { step.HitMode = WeaponHitMode.ForwardArc; step.Range = 3.5f; step.HitAngle = 160f; }
                if (GUILayout.Button("찌르기", EditorStyles.miniButtonRight))
                { step.HitMode = WeaponHitMode.ForwardArc; step.Range = 5f; step.HitAngle = 40f; }
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("손맛", EditorStyles.miniBoldLabel);
        step.HandMode = (HandVisibility)EditorGUILayout.EnumPopup("손 표시", step.HandMode);
        using (new EditorGUILayout.HorizontalScope())
        {
            step.HitStopDuration = EditorGUILayout.FloatField("히트스톱", step.HitStopDuration);
            step.CameraKickFov = EditorGUILayout.FloatField("FOV킥", step.CameraKickFov);
        }
        step.CameraShake = EditorGUILayout.FloatField("셰이크", step.CameraShake);
    }

    // ─────────── 목록 ───────────

    private ReorderableList _keyList;
    private List<PoseKey> _keyListSource;

    void DrawLists(ComboStep step)
    {
        DrawKeyOrderList(step);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("모든 키 왼손 켜기", EditorStyles.miniButtonLeft)) SetAllLeftHand(step.Motion, true);
            if (GUILayout.Button("모든 키 왼손 끄기", EditorStyles.miniButtonRight)) SetAllLeftHand(step.Motion, false);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"이벤트 ({step.Events.Length})", EditorStyles.miniBoldLabel);
        for (int i = 0; i < step.Events.Length; i++)
        {
            var ev = step.Events[i];
            if (ev == null) continue;
            var m = Meta(ev.Type);
            bool sel = _sel == Sel.Event && _selIndex == i;
            if (GUILayout.Toggle(sel, $"{m.icon} {m.label}   {ev.Time * step.Duration:F2}s", EditorStyles.miniButton) && !sel)
                Select(Sel.Event, i, step);
        }
    }

    /// <summary>
    /// 키 목록 — 끌어서 순서 바꾸기. 시점(0~1)은 자리에 그대로 남고 포즈·곡선이 이동.
    /// 예) 키 1을 맨 아래로 끌면 그 포즈가 마지막 시점에서 나옴
    /// </summary>
    void DrawKeyOrderList(ComboStep step)
    {
        var keys = step.Motion.Keys;

        if (_keyList == null || !ReferenceEquals(_keyListSource, keys))
        {
            _keyListSource = keys;
            _keyList = new ReorderableList(keys, typeof(PoseKey), true, true, false, false)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 4,
                drawHeaderCallback = r => EditorGUI.LabelField(r, $"키 {keys.Count}개 — ≡ 를 끌어 순서 변경 (시점은 그대로, 포즈가 이동)"),
            };

            _keyList.drawElementCallback = (r, i, active, focused) =>
            {
                if (i < 0 || i >= keys.Count) return;
                var cur = CurrentStep;
                float dur = cur != null ? cur.Duration : 1f;
                r.y += 2;
                r.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.LabelField(r, $"◆ {i}    {keys[i].Time * dur:F2}s    {Summary(keys[i].Pose)}");
            };

            _keyList.onSelectCallback = l =>
            {
                var cur = CurrentStep;
                if (cur != null && l.index >= 0) Select(Sel.Key, l.index, cur);
            };

            _keyList.onReorderCallback = l =>
            {
                // 시점은 자리 순서대로 다시 배정 → 포즈만 자리를 바꾼 효과
                var times = new List<float>(keys.Count);
                foreach (var k in keys) times.Add(k.Time);
                times.Sort();
                for (int i = 0; i < keys.Count; i++) keys[i].Time = times[i];

                var cur = CurrentStep;
                if (cur != null) Select(Sel.Key, l.index, cur);
                EditorUtility.SetDirty(_target);
                RefreshPreview();
            };
        }

        _keyList.index = _sel == Sel.Key ? _selIndex : -1;
        _keyList.DoLayoutList();
    }

    static string Summary(ArmPose p)
    {
        if (p == null) return "";
        var s = p.UseLeftHand ? "양손" : "오른손";
        if (!p.UseWeaponGrip) s += " · 손가락";
        if (p.BodyRot != Vector3.zero || p.BodyPos != Vector3.zero) s += " · 몸";
        if (p.CamRot != Vector3.zero || p.CamFov != 0f) s += " · 카메라";
        return s;
    }

    // ══════════════════════ 포즈 클립보드 ══════════════════════
    // 시스템 클립보드에 JSON으로 저장 — 다른 무기·능력 모션, 스크립트 재컴파일 뒤에도 붙여넣기 가능

    [Flags]
    enum PosePart
    {
        RightHand = 1, LeftHand = 2, RightFingers = 4, LeftFingers = 8,
        Weapon = 16, Camera = 32, Body = 64,
        Fingers = RightFingers | LeftFingers,
        All = 127,
    }

    static readonly (PosePart part, string label)[] PasteChoices =
    {
        (PosePart.All, "전체"),
        (PosePart.RightHand, "오른손 위치·회전"),
        (PosePart.LeftHand, "왼손 위치·회전"),
        (PosePart.Fingers, "손가락 (양손)"),
        (PosePart.RightFingers, "오른손 손가락"),
        (PosePart.LeftFingers, "왼손 손가락"),
        (PosePart.Weapon, "무기 오프셋"),
        (PosePart.Camera, "카메라"),
        (PosePart.Body, "몸 · 머리"),
    };

    const string PoseClipPrefix = "MOTIONSETUP_POSE:";
    const string HandClipPrefix = "MOTIONSETUP_HAND:";

    [Serializable] class PoseClip { public ArmPose Pose; }
    [Serializable] class HandClip { public HandPose Hand; }

    static void CopyPose(ArmPose p) =>
        EditorGUIUtility.systemCopyBuffer = PoseClipPrefix + EditorJsonUtility.ToJson(new PoseClip { Pose = p });

    static bool TryGetClipPose(out ArmPose pose)
    {
        pose = null;
        var s = EditorGUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(s) || !s.StartsWith(PoseClipPrefix)) return false;

        var clip = new PoseClip();
        EditorJsonUtility.FromJsonOverwrite(s.Substring(PoseClipPrefix.Length), clip);
        pose = clip.Pose;
        return pose != null;
    }

    static void CopyHand(HandPose h) =>
        EditorGUIUtility.systemCopyBuffer = HandClipPrefix + EditorJsonUtility.ToJson(new HandClip { Hand = h });

    static bool TryGetClipHand(out HandPose hand)
    {
        hand = null;
        var s = EditorGUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(s)) return false;

        if (s.StartsWith(HandClipPrefix))
        {
            var clip = new HandClip();
            EditorJsonUtility.FromJsonOverwrite(s.Substring(HandClipPrefix.Length), clip);
            hand = clip.Hand;
        }
        else if (TryGetClipPose(out var pose))
        {
            hand = pose.RightFingers ?? pose.LeftFingers;     // 포즈 전체를 복사해 뒀으면 오른손 손가락을 씀
        }
        return hand != null;
    }

    /// <summary>src의 고른 부분만 dst에 덮어씀 (시점·곡선은 그대로)</summary>
    static void PasteParts(ArmPose src, ArmPose dst, PosePart parts)
    {
        if (src == null || dst == null) return;

        if ((parts & PosePart.RightHand) != 0) { dst.RightPos = src.RightPos; dst.RightRot = src.RightRot; }
        if ((parts & PosePart.LeftHand) != 0) { dst.UseLeftHand = src.UseLeftHand; dst.LeftPos = src.LeftPos; dst.LeftRot = src.LeftRot; }
        if ((parts & PosePart.Weapon) != 0) { dst.WeaponPos = src.WeaponPos; dst.WeaponRot = src.WeaponRot; dst.WeaponScale = src.WeaponScale;
            if (src.Parts != null)
            {
                dst.Parts = new PartPose[src.Parts.Length];
                for (int j = 0; j < src.Parts.Length; j++) dst.Parts[j] = src.Parts[j]?.Clone();
            }
            else dst.Parts = null;
        }
        if ((parts & PosePart.Camera) != 0) { dst.CamPos = src.CamPos; dst.CamRot = src.CamRot; dst.CamFov = src.CamFov; }
        if ((parts & PosePart.Body) != 0)
        {
            dst.BodyPos = src.BodyPos; dst.BodyRot = src.BodyRot;
            dst.HeadPos = src.HeadPos; dst.HeadRot = src.HeadRot;
        }

        bool rf = (parts & PosePart.RightFingers) != 0;
        bool lf = (parts & PosePart.LeftFingers) != 0;
        if (!rf && !lf) return;

        if (src.UseWeaponGrip)
        {
            // 원본이 무기 그립을 쓰면 — 양손 다 고른 경우에만 무기 그립으로
            if (rf && lf) dst.UseWeaponGrip = true;
            return;
        }

        dst.UseWeaponGrip = false;
        if (rf && src.RightFingers != null) dst.RightFingers = src.RightFingers.Clone();
        if (lf && src.LeftFingers != null) dst.LeftFingers = src.LeftFingers.Clone();
    }

    void ShowPasteMenu(ComboStep step, int index)
    {
        var menu = new GenericMenu();
        bool has = TryGetClipPose(out var clip);

        foreach (var (part, label) in PasteChoices)
        {
            var p = part;
            if (has) menu.AddItem(new GUIContent($"이 키에/{label}"), false, () => PasteInto(step, index, index, clip, p));
            else menu.AddDisabledItem(new GUIContent($"이 키에/{label}"));
        }
        foreach (var (part, label) in PasteChoices)
        {
            var p = part;
            if (has) menu.AddItem(new GUIContent($"이 키부터 끝까지/{label}"), false,
                () => PasteInto(step, index, step.Motion.Keys.Count - 1, clip, p));
            else menu.AddDisabledItem(new GUIContent($"이 키부터 끝까지/{label}"));
        }

        menu.AddSeparator("");
        if (has) menu.AddItem(new GUIContent("이 키에 좌우 반전해서 (전체)"), false,
            () => PasteInto(step, index, index, MirrorPose(clip, _fingerMirror), PosePart.All));
        else menu.AddDisabledItem(new GUIContent("이 키에 좌우 반전해서 (전체)"));

        if (!has) { menu.AddSeparator(""); menu.AddDisabledItem(new GUIContent("먼저 '포즈 복사'를 누르세요")); }
        menu.ShowAsContext();
    }

    /// <summary>이 키의 포즈를 다음 키(또는 끝까지)에 그대로 보냄 — 복사·붙여넣기 한 번에</summary>
    void ShowSendMenu(ComboStep step, int index)
    {
        var menu = new GenericMenu();
        var keys = step.Motion.Keys;
        bool hasNext = index + 1 < keys.Count;
        var src = keys[index].Pose;

        foreach (var (part, label) in PasteChoices)
        {
            var p = part;
            if (hasNext)
            {
                menu.AddItem(new GUIContent($"다음 키로/{label}"), false, () => PasteInto(step, index + 1, index + 1, src.Clone(), p));
                menu.AddItem(new GUIContent($"뒤의 모든 키로/{label}"), false, () => PasteInto(step, index + 1, keys.Count - 1, src.Clone(), p));
            }
        }
        if (!hasNext) menu.AddDisabledItem(new GUIContent("마지막 키입니다"));
        menu.ShowAsContext();
    }

    void PasteInto(ComboStep step, int from, int to, ArmPose src, PosePart parts)
    {
        Modify("Paste Pose", () =>
        {
            var keys = step.Motion.Keys;
            for (int i = Mathf.Max(0, from); i <= to && i < keys.Count; i++)
            {
                keys[i].Pose ??= new ArmPose();
                PasteParts(src, keys[i].Pose, parts);
            }
        });

        if (_sel == Sel.Key && _selIndex >= 0 && _selIndex < step.Motion.Keys.Count)
            PreviewPose(step.Motion.Keys[_selIndex].Pose);
    }

    void SetAllLeftHand(SwingMotion motion, bool on)
    {
        Modify("Toggle Left Hand", () => { foreach (var k in motion.Keys) k.Pose.UseLeftHand = on; });
    }

    // ══════════════════════ 도구 탭 ══════════════════════

    void DrawToolsTab()
    {
        if (Fold("보기"))
            using (Box())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("씬 뷰를 무기에 맞추기")) FrameWeapon();
                    if (GUILayout.Button("1인칭 시점으로 보기")) LookThroughEye();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("에셋 선택")) Selection.activeObject = _target;
                    if (GUILayout.Button("FPArmRig 선택")) Selection.activeGameObject = _rig.gameObject;
                }
                if (GUILayout.Button(new GUIContent("변화 배치 · 생성물 애니메이션 (Rig Animator)",
                        "말 같은 변화 파츠를 이 리그에 붙여 마우스로 배치 · 탈것/그림자 손 본 정리 · IK · 애니메이션")))
                    RigAnimatorWindow.Open();

                EditorGUILayout.Space(4);
                _showHitbox = EditorGUILayout.Toggle("판정 범위 표시", _showHitbox);
                _showTrail = EditorGUILayout.Toggle("무기 궤적 표시", _showTrail);
                _showEffects = EditorGUILayout.Toggle("이펙트 미리보기", _showEffects);
                _showOnlyDuringActive = EditorGUILayout.Toggle("판정 중에만", _showOnlyDuringActive);
                if (GUILayout.Button("궤적 지우기")) _tipTrail.Clear();
            }

        if (Fold("리그"))
            using (Box())
            {
                if (GUILayout.Button("Idle 포즈를 씬에서 저장 (FPArmRig)"))
                {
                    Undo.RecordObject(_rig, "Capture Idle");
                    _rig.IdlePose = CaptureCurrentPose();
                    EditorUtility.SetDirty(_rig);
                }

                if (GUILayout.Button(new GUIContent("몸·머리 기준 자세 다시 잡기", "지금 씬의 몸·머리 위치를 0 오프셋으로")))
                {
                    _rig.CaptureBodyRest();
                    RefreshPreview();
                }
            }

        var step = CurrentStep;
        if (step?.Motion?.Keys != null && Fold("몸 동작 자동 생성"))
            using (Box())
            {
                Hint("손 동작을 보고 몸 비틀기·머리 따라가기를 키마다 채웁니다. 시작점으로 쓰고 직접 다듬으세요.");
                _autoBodyStrength = EditorGUILayout.Slider("세기", _autoBodyStrength, 0f, 1f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("이 타에 적용")) AutoBody(step, _autoBodyStrength);
                    if (GUILayout.Button("이 타 몸·머리 전부 0")) AutoBody(step, 0f);
                }
            }
    }

    /// <summary>오른손이 기본 자세에서 벗어난 만큼 몸·머리를 같은 방향으로 따라가게</summary>
    void AutoBody(ComboStep step, float strength)
    {
        var idle = _rig.GetEffectiveIdle();

        Modify("Auto Body", () =>
        {
            foreach (var k in step.Motion.Keys)
            {
                var p = k.Pose;
                Vector3 dr = NormalizeEuler(p.RightRot - idle.RightRot);
                Vector3 dp = p.RightPos - idle.RightPos;

                p.BodyRot = new Vector3(dr.x * 0.25f, dr.y * 0.5f + dp.x * 40f, -dp.x * 15f) * strength;
                p.BodyPos = new Vector3(0f, Mathf.Min(0f, dp.y) * 0.3f, dp.z * 0.3f) * strength;
                p.HeadRot = p.BodyRot * 0.4f;
                p.HeadPos = Vector3.zero;
            }
        });
    }

    // ══════════════════════ 손가락 편집 공통 ══════════════════════

    Vector3 GetCurlAxis() =>
        _rig?.RightHandRig != null ? _rig.RightHandRig.CurlAxis : new Vector3(0f, 0f, -70f);

    void DrawGripEditor(string label, ref HandPose grip, Vector3 axis, HandRig handRig, Object dirtyTarget)
    {
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            HandPose np = null;
            if (GUILayout.Button("펌", EditorStyles.miniButtonLeft)) np = HandPose.Open(axis);
            if (GUILayout.Button("자연", EditorStyles.miniButtonMid)) np = HandPose.Relaxed(axis);
            if (GUILayout.Button("쥠", EditorStyles.miniButtonMid)) np = HandPose.Grip(axis);
            if (GUILayout.Button("주먹", EditorStyles.miniButtonRight)) np = HandPose.Fist(axis);
            if (np != null)
            {
                grip = np;
                EditorUtility.SetDirty(dirtyTarget);
                handRig?.Apply(np);
                RefreshPreview();
            }
        }

        if (handRig == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("씬 손가락 → 저장"))
            {
                grip ??= new HandPose();
                handRig.BakeAll(grip);
                EditorUtility.SetDirty(dirtyTarget);
            }
            if (GUILayout.Button("저장값 → 씬"))
            {
                if (grip != null) handRig.Apply(grip);
                SceneView.RepaintAll();
            }
        }

        DrawSceneEditToggle(grip, handRig, dirtyTarget);
        DrawFingerSliders(grip, axis, handRig, dirtyTarget);
    }

    void DrawSceneEditToggle(HandPose pose, HandRig handRig, Object dirtyTarget)
    {
        if (pose == null) return;

        bool editing = ReferenceEquals(HandRigEditor.EditTarget, pose);
        bool now = GUILayout.Toggle(editing, editing ? "■ 씬 IK 편집 중 (끄기)" : "▶ 씬에서 IK로 편집", "Button", GUILayout.Height(20));
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
            if (fp == null || fp.Joints == null || fp.Joints.Length == 0) continue;

            float cur = fp.EstimateCurl(axis);
            if (cur > 1.2f)
            {
                EditorGUILayout.LabelField(FingerNames[f], $"{cur:F2}  (IK 편집됨)", EditorStyles.miniLabel);
                continue;
            }

            EditorGUI.BeginChangeCheck();
            float nv = EditorGUILayout.Slider(FingerNames[f], cur, 0f, 1.2f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(dirtyTarget, "Finger Curl");
                fp.SetCurl(nv, axis, JointWeights);
                EditorUtility.SetDirty(dirtyTarget);
                handRig?.Apply(pose);
                SceneView.RepaintAll();
            }
        }
    }

    /// <summary>차징·반동 자세 값 편집 (손·무기·카메라)</summary>
    static void DrawPoseValues(ArmPose cp, bool body)
    {
        cp.RightPos = EditorGUILayout.Vector3Field("오른손 위치", cp.RightPos);
        cp.RightRot = EditorGUILayout.Vector3Field("오른손 회전", cp.RightRot);
        cp.UseLeftHand = EditorGUILayout.Toggle("왼손 표시", cp.UseLeftHand);
        if (cp.UseLeftHand)
        {
            cp.LeftPos = EditorGUILayout.Vector3Field("왼손 위치", cp.LeftPos);
            cp.LeftRot = EditorGUILayout.Vector3Field("왼손 회전", cp.LeftRot);
        }
        cp.WeaponPos = EditorGUILayout.Vector3Field("무기 위치", cp.WeaponPos);
        cp.WeaponRot = EditorGUILayout.Vector3Field("무기 회전", cp.WeaponRot);
        cp.WeaponScale = EditorGUILayout.Vector3Field("무기 크기 (배율)", ArmPose.Scl(cp.WeaponScale));
        cp.CamRot = EditorGUILayout.Vector3Field("카메라 회전", cp.CamRot);
        cp.CamFov = EditorGUILayout.FloatField("FOV 추가", cp.CamFov);
        if (!body) return;
        cp.BodyRot = EditorGUILayout.Vector3Field("몸 회전", cp.BodyRot);
        cp.HeadRot = EditorGUILayout.Vector3Field("머리 회전", cp.HeadRot);
    }

    // ══════════════════════ 프리뷰 ══════════════════════

    void SpawnWeapon()
    {
        ClearEffectPreviews();
        FindRig();
        if (_rig == null) return;

        if (_source == null)
        {
            _rig.CleanWeaponOnly();
            SceneView.RepaintAll();
            return;
        }

        var w = DesiredPreviewWeapon();
        _rig.SetPartParents(_tab == Tab.Motion ? CurrentStep?.PartParents : null);
        _rig.Mount(w);
        if (w != null) MarkPreviewWeapon();

        _playing = false;
        _playT = 0f;
        ClearSelection();
        _previewDraw = 0f;
        _forceProjectile = false;
        _rig.ApplyPose(_rig.GetEffectiveIdle());
        SceneView.RepaintAll();
    }

    void MarkPreviewWeapon()
    {
        // 추가 파츠도 씬에 저장되지 않게
        for (int i = 1; _rig != null && i < _rig.PartCount; i++)
        {
            var pt = _rig.GetPartTransform(i);
            if (pt != null) pt.gameObject.hideFlags = HideFlags.DontSave;
        }
        var t = _rig?.GetWeaponTransform();
        if (t != null) t.gameObject.hideFlags = HideFlags.DontSave;
    }

    void ClearPreview()
    {
        ClearEffectPreviews();
        if (_rig == null) return;
        _rig.CleanWeaponOnly();
        SceneView.RepaintAll();
    }

    /// <summary>모션 탭에서 지금 타가 '쥘 무기'를 지정했으면 그걸로, 아니면 미리보기 무기로</summary>
    EquipmentData DesiredPreviewWeapon()
    {
        var step = _tab == Tab.Motion ? CurrentStep : null;
        if (step != null && step.WeaponOverride != null) return step.WeaponOverride;
        return ResolvePreviewWeapon();
    }

    void SyncPreviewWeapon()
    {
        if (_rig == null || _source == null) return;
        var w = DesiredPreviewWeapon();
        if (_rig.CurrentWeapon == w) return;
        _rig.Mount(w);
        if (w != null) MarkPreviewWeapon();
        RefreshPreview();
    }

    void RefreshPreview()
    {
        if (_rig == null || _source == null) return;
        if (_rig.CurrentWeapon != DesiredPreviewWeapon()) { SyncPreviewWeapon(); return; }

        var step = CurrentStep;
        ApplyStepParents();
        if (step?.Motion != null && step.Motion.IsValid)
        {
            _rig.SetWeaponHidden(step.HideWeapon);
            _rig.ApplySwing(step.Motion, _playT / Mathf.Max(0.01f, step.TotalTime), step.HandMode,
                            _tab == Tab.Motion ? PreviewCont(step) : null);
        }
        else
            _rig.ApplyPose(_rig.GetEffectiveIdle());

        SceneView.RepaintAll();
    }

    /// <summary>미리보기용 '이전 타 위치에서 시작' — 앞 타의 캔슬 지점 자세에서 시작 (게임에서 누르는 타이밍)</summary>
    MotionContinuity PreviewCont(ComboStep step)
    {
        if (step == null || !step.ContinueFromPrevious || _source == null || _rig == null) return null;
        int i = Mathf.Clamp(_comboIndex, 0, _source.ComboLength - 1);
        if (i <= 0) return null;
        var prev = _source.GetStep(i - 1);
        if (prev?.Motion == null || !prev.Motion.IsValid) return null;
        var from = prev.Motion.Evaluate(Mathf.Clamp01(prev.CancelPoint), _rig.GetEffectiveIdle());
        return MotionContinuity.From(from, step.Motion);
    }

    /// <summary>모션 탭이면 지금 타의 '파츠 부모', 아니면 무기 설정대로</summary>
    void ApplyStepParents()
    {
        if (_rig == null) return;
        var step = _tab == Tab.Motion ? CurrentStep : null;
        _rig.SetPartParents(step?.PartParents);
        MarkPreviewWeapon();
    }

    void PreviewPose(ArmPose pose)
    {
        if (_rig == null) return;
        ApplyStepParents();
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

        // 무기 추가 오프셋 (기본 오프셋 대비 차이)
        //  - 기준은 '지금 손에 든 무기' — 이 모션만 쥐는 무기(WeaponOverride)면 그 무기의 기본 오프셋
        //    (예전엔 원래 미리보기 무기 기준으로 빼서, 재생 땐 다른 무기 기준으로 더해져 위치 · 회전이 틀어졌음)
        //  - 회전은 적용 쪽(FPArmRig: Euler(기본 + 추가))과 같은 식으로 빼야 그대로 돌아옴
        var weaponT = _rig.GetWeaponTransform();
        var w = _rig.CurrentWeapon != null ? _rig.CurrentWeapon : ResolvePreviewWeapon();
        if (weaponT != null && w != null)
        {
            Vector3 baseP = w.GetPosOffset(true);
            Vector3 baseR = w.GetRotOffset(true);
            // 둥둥 흔들림은 빼고 저장 — 키에는 '제자리'만
            p.WeaponPos = weaponT.localPosition - baseP - _rig.LastWeaponFloatPos;
            p.WeaponRot = NormalizeEuler(weaponT.localRotation.eulerAngles - baseR - _rig.LastWeaponFloatRot);

            // 크기 배율 = 지금 크기 ÷ 무기 기본 크기 (씬에서 크기 도구로 바꿔도 됨)
            Vector3 so = w.ScaleOverride, ls = weaponT.localScale;
            p.WeaponScale = new Vector3(Div(ls.x, so.x), Div(ls.y, so.y), Div(ls.z, so.z));
        }

        // 추가 파츠 (1번~) — 각자 부모 · 기본값 기준으로 같은 식
        for (int i = 1; i < _rig.PartCount; i++)
        {
            var pt = _rig.GetPartTransform(i);
            if (pt == null || !_rig.GetPartBase(i, out var bP, out var bR, out var bS)) continue;
            _rig.GetPartLastFloat(i, out var fP, out var fR);
            Vector3 l = pt.localScale;
            p.SetPart(i, pt.localPosition - bP - fP,
                      NormalizeEuler(pt.localRotation.eulerAngles - bR - fR),
                      new Vector3(Div(l.x, bS.x), Div(l.y, bS.y), Div(l.z, bS.z)));
        }

        // 몸 · 머리
        _rig.GetBodyOffset(out var bp, out var br, out var hp, out var hr);
        p.BodyPos = bp;
        p.BodyRot = NormalizeEuler(br);
        p.HeadPos = hp;
        p.HeadRot = NormalizeEuler(hr);

        // 손가락
        p.UseWeaponGrip = false;
        if (_rig.RightHandRig != null)
        {
            p.RightFingers = new HandPose();
            _rig.RightHandRig.BakeAll(p.RightFingers);
        }
        if (_rig.LeftHandRig != null)
        {
            p.LeftFingers = new HandPose();
            _rig.LeftHandRig.BakeAll(p.LeftFingers);
        }

        return p;
    }

    void CaptureWeaponTransform()
    {
        Transform t = _rig.GetWeaponTransform();
        if (t == null)
        {
            Debug.LogWarning("[MotionSetup] 손에 무기가 없습니다.");
            return;
        }

        // 둥둥 흔들림은 빼고 (기본 자리만)
        Vector3 lp = t.localPosition - _rig.LastWeaponFloatPos;
        Vector3 le = NormalizeEuler(t.localRotation.eulerAngles - _rig.LastWeaponFloatRot);
        if (_firstPerson)
        {
            _weapon.FpPositionOffset = lp;
            _weapon.FpRotationOffset = le;
        }
        else
        {
            _weapon.PositionOffset = lp;
            _weapon.RotationOffset = le;
        }
        _weapon.ScaleOverride = t.localScale;
        EditorUtility.SetDirty(_weapon);
    }

    void ResetWeaponTransform()
    {
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

    /// <summary>무기 · 파츠 · 손 자세 · 손가락 그립을 씬에서 한 번에</summary>
    void CaptureAllGrip()
    {
        if (_rig == null || _weapon == null) return;
        Undo.RecordObject(_weapon, "Capture All Grip");

        CaptureWeaponTransform();

        // 추가 파츠 기본 위치
        if (_weapon.ExtraParts != null)
            for (int i = 1; i < _rig.PartCount; i++)
            {
                var t = _rig.GetPartTransform(i);
                var d = i - 1 < _weapon.ExtraParts.Length ? _weapon.ExtraParts[i - 1] : null;
                if (t == null || d == null) continue;
                _rig.GetPartLastFloat(i, out var fP, out var fR);
                d.Position = t.localPosition - fP;
                d.Rotation = NormalizeEuler(t.localRotation.eulerAngles - fR);
                d.Scale = t.localScale;
            }

        CaptureWeaponIdlePose();

        // 손가락
        if (_rig.RightHandRig != null)
        {
            _weapon.RightGrip ??= new HandPose();
            _rig.RightHandRig.BakeAll(_weapon.RightGrip);
        }
        if (_rig.LeftHandRig != null && (_weapon.Grip == GripType.TwoHand || _weapon.UseLeftHand))
        {
            _weapon.LeftGrip ??= new HandPose();
            _rig.LeftHandRig.BakeAll(_weapon.LeftGrip);
        }

        EditorUtility.SetDirty(_weapon);
        RefreshPreview();
        ShowNotification(new GUIContent("무기 · 파츠 · 손 자세 · 손가락 그립 저장됨"));
    }

    void CaptureWeaponIdlePose()
    {
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
    }

    void CopyRigIdle()
    {
        var idle = _rig.IdlePose;
        _weapon.RightHandPos = idle.RightPos;
        _weapon.RightHandRot = idle.RightRot;
        _weapon.UseLeftHand = idle.UseLeftHand;
        _weapon.LeftHandPos = idle.LeftPos;
        _weapon.LeftHandRot = idle.LeftRot;
        EditorUtility.SetDirty(_weapon);
        RefreshPreview();
    }

    void PullFromRigDefaults()
    {
        var idle = _rig.IdlePose;
        _weapon.RightHandPos = idle.RightPos;
        _weapon.RightHandRot = idle.RightRot;
        _weapon.LeftHandPos = idle.LeftPos;
        _weapon.LeftHandRot = idle.LeftRot;
        _weapon.UseLeftHand = idle.UseLeftHand || _weapon.Grip == GripType.TwoHand;
        _weapon.OverrideIdlePose = true;
        EditorUtility.SetDirty(_weapon);
        SpawnWeapon();
    }

    void CaptureStringPull()
    {
        var bone = _rig.StringBone;
        if (bone == null)
        {
            Debug.LogWarning("[MotionSetup] 시위 본이 없습니다. WeaponRig의 StringBone을 확인하세요.");
            return;
        }

        Vector3 delta = bone.localPosition - _rig.StringRestPos;
        if (delta.sqrMagnitude < 1e-6f)
        {
            Debug.LogWarning("[MotionSetup] 시위를 먼저 씬에서 당겨 주세요.");
            return;
        }

        _weapon.StringPullDir = delta.normalized;
        _weapon.StringPullDistance = delta.magnitude;
        EditorUtility.SetDirty(_weapon);
    }

    void CaptureStringHand()
    {
        var bone = _rig.StringBone;
        if (bone == null || _rig.LeftHand == null)
        {
            Debug.LogWarning("[MotionSetup] 시위 본 또는 왼손이 없습니다.");
            return;
        }

        _weapon.StringHandOffset = bone.InverseTransformPoint(_rig.LeftHand.position);
        _weapon.StringHandRot = NormalizeEuler(_rig.LeftHand.localRotation.eulerAngles);

        if (_rig.LeftHandRig != null)
        {
            _weapon.StringHandPose = new HandPose();
            _rig.LeftHandRig.BakeAll(_weapon.StringHandPose);
        }
        EditorUtility.SetDirty(_weapon);
    }

    void FrameWeapon()
    {
        Transform t = _rig.GetWeaponTransform();
        if (t == null) t = _rig.RightHand;
        if (t == null) return;

        Selection.activeGameObject = t.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    void LookThroughEye()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return;

        Transform eye = GetEyeTransform();
        if (eye == null) return;

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

        // 원거리 재생 미리보기 (차징 · 반동)
        if (_tab == Tab.Ranged && TickRangedPlay(dt)) { Repaint(); return; }
        if (_tab != Tab.Ranged && _rangedPlay != RangedPlay.None) _rangedPlay = RangedPlay.None;

        // 무기 둥둥 미리보기 — 재생 중이 아니어도 계속 움직여 봄
        if (!_playing && FPArmRig.EditorFloatPreview && _rig != null && _rig.CurrentWeapon != null && _rig.CurrentWeapon.UseFloat)
        {
            RefreshPreview();
            return;
        }

        if (!_playing || _source == null || _rig == null) return;

        var step = CurrentStep;
        if (step == null) { _playing = false; return; }

        _playT += dt * _playSpeed;

        // 전체 재생 — 다음 타가 '이전 타 위치에서 시작'이면 게임처럼 캔슬 지점에서 넘어감
        if (_playAll && _comboIndex + 1 < _source.ComboLength)
        {
            var next = _source.GetStep(_comboIndex + 1);
            if (next != null && next.ContinueFromPrevious && _playT >= step.CancelTime)
            {
                _comboIndex++;
                _playT = 0f;
                ClearSelection();
                step = CurrentStep;
            }
        }

        if (_playT >= step.TotalTime)
        {
            _playT = 0f;

            if (_playAll && _source.ComboLength > 1)
            {
                _comboIndex++;
                if (_comboIndex >= _source.ComboLength)
                {
                    _comboIndex = 0;
                    if (!_loop) { StopPlay(); return; }
                }
                ClearSelection();
                step = CurrentStep;
            }
            else if (!_loop) { StopPlay(); return; }
        }

        _rig.SetPartParents(step.PartParents);
        _rig.ApplySwing(step.Motion, _playT / Mathf.Max(0.01f, step.TotalTime), step.HandMode, PreviewCont(step));
        SceneView.RepaintAll();
        Repaint();
    }

    void StopPlay()
    {
        _playing = false;
        _playAll = false;
        _rig.ApplyPose(_rig.GetEffectiveIdle());
        SceneView.RepaintAll();
        Repaint();
    }

    // ══════════════════════ 씬 시각화 ══════════════════════

    void OnSceneGUI(SceneView sv)
    {
        if (Application.isPlaying || _source == null || _rig == null) { ClearEffectPreviews(); return; }

        var step = CurrentStep;
        if (step == null) { ClearEffectPreviews(); return; }

        Transform eye = GetEyeTransform();
        if (eye == null) return;

        float total = Mathf.Max(0.01f, step.TotalTime);
        float p = Mathf.Clamp01(_playT / total);
        bool inActive = _playT >= step.WindUp && _playT <= step.WindUp + step.ActiveWindow;

        // 판정 기준 무기 — 미리보기 무기 → 이 모션의 무기 → 지금 손에 쥔 무기(모션 무기 포함) 순
        var hitSrc = ResolvePreviewWeapon();
        if (hitSrc == null) hitSrc = _rig.CurrentWeapon;

        if (_showHitbox) DrawHitArc(eye, step, hitSrc, inActive);      // 무기가 없으면 이 단계의 판정 값으로
        if (_showTrail) DrawTipTrail(inActive);
        if (Event.current.type == EventType.Layout) UpdateEffectPreviews(step);
        DrawEventMarkers(step, p);

        DrawInfoLabel(step, p, inActive, hitSrc);
    }

    Transform GetEyeTransform()
    {
        if (_rig == null) return null;
        Transform arm = _rig.ArmRoot != null ? _rig.ArmRoot : _rig.transform;
        return arm.parent != null ? arm.parent : arm;
    }

    /// <summary>선택한 이펙트·조명 이벤트가 생길 위치를 씬에 표시</summary>
    void DrawEventMarkers(ComboStep step, float p)
    {
        if (_sel != Sel.Event || _selIndex < 0 || _selIndex >= step.Events.Length) return;

        var ev = step.Events[_selIndex];
        if (ev == null) return;
        if (ev.Type != MotionEventType.Effect && ev.Type != MotionEventType.LightFlash) return;

        Transform basis = ev.Space switch
        {
            EventSpace.AttachToWeapon => _rig.WeaponTip,
            EventSpace.AttachToMuzzle => _rig.Muzzle,
            EventSpace.AttachToBody => _rig.Body,
            EventSpace.AttachToHead => _rig.Head,
            EventSpace.AttachToRightHand => _rig.RightHand,
            EventSpace.AttachToLeftHand => _rig.LeftHand,
            EventSpace.AttachToCamera => GetEyeTransform(),
            _ => _rig.Body != null ? _rig.Body : GetEyeTransform(),
        };
        if (basis == null) return;

        Vector3 pos = basis.TransformPoint(ev.PositionOffset);
        var m = Meta(ev.Type);
        Handles.color = m.color;
        Handles.SphereHandleCap(0, pos, Quaternion.identity, 0.06f, EventType.Repaint);
        Handles.Label(pos + Vector3.up * 0.06f, $"{m.icon} {m.label}", EditorStyles.whiteMiniLabel);
    }

    void DrawHitArc(Transform eye, ComboStep step, EquipmentData w, bool inActive)
    {
        if (_showOnlyDuringActive && !inActive) return;

        step.ResolveHitShape(w, out var mode, out float range, out float angle, out float height, out float minRange);

        Vector3 o = eye.position - Vector3.up * 0.7f;
        Vector3 f = new Vector3(eye.forward.x, 0f, eye.forward.z).normalized;
        if (f.sqrMagnitude < 0.001f) f = Vector3.forward;

        Color c = inActive ? new Color(1f, 0.25f, 0.15f, 1f) : new Color(0.5f, 0.55f, 0.6f, 0.6f);
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
            $"{range:F2}m{(step.OverrideHitShape ? " (개별)" : "")}", EditorStyles.whiteMiniLabel);
    }

    void DrawTipTrail(bool inActive)
    {
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

   
        step.ResolveHitShape(w, out var mode, out float range, out float angle, out _, out _);
        GUILayout.Label(mode == WeaponHitMode.ForwardArc ? $"부채꼴 {range:F2}m / {angle:F0}°" : $"구체 {range:F2}m",
            EditorStyles.whiteMiniLabel);


        GUILayout.Label($"피해 ×{step.DamageMultiplier:F2}   대상 {(step.MaxTargets > 0 ? step.MaxTargets.ToString() : "∞")}", EditorStyles.whiteMiniLabel);

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    // ══════════════════════ 이펙트 미리보기 — Effect 이벤트 프리팹 + Rig 애니메이션 ══════════════════════

    private bool _showEffects = true;
    private readonly Dictionary<MotionEvent, (GameObject prefab, GameObject go)> _fxPreview = new();
    private readonly List<MotionEvent> _fxDrop = new();

    void DrawRigStateField(MotionEvent ev)
    {
        var ra = ev.Prefab != null ? ev.Prefab.GetComponentInChildren<RigAnimator>(true) : null;
        if (ra == null || ra.Data == null) return;

        var names = new List<string> { "(기본)" };
        foreach (var s in ra.Data.States) if (s != null) names.Add(s.Name);

        int cur = string.IsNullOrEmpty(ev.RigState) ? 0 : names.IndexOf(ev.RigState);
        if (cur < 0) Hint($"Rig 상태 '{ev.RigState}'가 {ra.Data.name}에 없음 — 기본 상태로 재생됨");

        EditorGUI.BeginChangeCheck();
        int pick = EditorGUILayout.Popup("Rig 상태", Mathf.Max(0, cur), names.ToArray());
        if (EditorGUI.EndChangeCheck()) ev.RigState = pick <= 0 ? "" : names[pick];
    }

    Transform EffectBasis(EventSpace space) => space switch
    {
        EventSpace.AttachToWeapon => _rig.WeaponTip,
        EventSpace.AttachToMuzzle => _rig.Muzzle,
        EventSpace.AttachToBody => _rig.Body,
        EventSpace.AttachToHead => _rig.Head,
        EventSpace.AttachToRightHand => _rig.RightHand,
        EventSpace.AttachToLeftHand => _rig.LeftHand,
        EventSpace.AttachToCamera => GetEyeTransform(),
        _ => _rig.Body != null ? _rig.Body : GetEyeTransform(),
    };

    /// <summary>지금 재생 위치에서 살아 있는 Effect 프리팹을 씬에 띄우고 Rig 애니메이션을 그 시간에 맞춤 (저장 안 됨)</summary>
    void UpdateEffectPreviews(ComboStep step)
    {
        float total = Mathf.Max(0.01f, step.TotalTime);
        _fxDrop.Clear();
        foreach (var kv in _fxPreview) _fxDrop.Add(kv.Key);

        if (_showEffects && step.Events != null)
        {
            foreach (var ev in step.Events)
            {
                if (ev == null || ev.Type != MotionEventType.Effect || ev.Prefab == null) continue;
                float age = _playT - ev.Time * total;
                if (age < 0f || (ev.Lifetime > 0f && age > ev.Lifetime)) continue;

                if (!_fxPreview.TryGetValue(ev, out var e) || e.go == null || e.prefab != ev.Prefab)
                {
                    if (e.go != null) DestroyImmediate(e.go);
                    var go = Instantiate(ev.Prefab);
                    go.name = "[미리보기] " + ev.Prefab.name;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    e = (ev.Prefab, go);
                    _fxPreview[ev] = e;
                }
                _fxDrop.Remove(ev);
                PlaceEffectPreview(ev, e.go, age);
            }
        }

        foreach (var ev in _fxDrop)
        {
            var go = _fxPreview[ev].go;
            if (go != null) DestroyImmediate(go);
            _fxPreview.Remove(ev);
        }
    }

    void PlaceEffectPreview(MotionEvent ev, GameObject go, float age)
    {
        // World(생성 위치에 고정)도 미리보기에선 기준을 따라 그림 — 위치 확인용
        Transform basis = EffectBasis(ev.Space);
        if (basis == null) basis = GetEyeTransform();
        if (basis == null) return;

        go.transform.SetPositionAndRotation(basis.TransformPoint(ev.PositionOffset),
                                            basis.rotation * Quaternion.Euler(ev.RotationOffset));
        go.transform.localScale = ev.Prefab.transform.localScale * ev.Scale;

        var ra = go.GetComponentInChildren<RigAnimator>(true);
        if (ra == null || ra.Data == null) return;

        string state = !string.IsNullOrEmpty(ev.RigState) && ra.Data.Find(ev.RigState) != null ? ev.RigState
                     : !string.IsNullOrEmpty(ra.StartState) ? ra.StartState
                     : ra.Data.IdleState;
        var st = ra.Data.Find(state);
        if (st == null) return;

        float t = age * st.Speed * ra.SpeedMul;
        // 한 번짜리가 끝나면 게임처럼 기본(Idle)으로
        if (!st.Loop && t > st.Length && state != ra.Data.IdleState && ra.Data.Find(ra.Data.IdleState) is { } idle)
        {
            ra.PreviewState(idle.Name, (t - st.Length) / Mathf.Max(0.01f, st.Speed) * idle.Speed);
            return;
        }
        ra.PreviewState(state, t);
    }

    void ClearEffectPreviews()
    {
        foreach (var kv in _fxPreview) if (kv.Value.go != null) DestroyImmediate(kv.Value.go);
        _fxPreview.Clear();
    }
}