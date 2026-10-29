using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// 변화 설정 창 — 변신 모델(AlterData)을 씬에 띄워 놓고:
///   ① 동작 · 타이밍 : AlterClipAnimator 상태(Idle · Walk · Run · States)를 프레임 단위로 넘겨 보기 · 재생,
///                     Hit Frame · 발걸음 프레임 · 능력 판정 구간을 타임라인에서 바로 찍기,
///                     자동 찾기 (발걸음 · 이륙 · 특정 뼈가 가장 빠른/낮은/높은/앞 프레임)
///   ② 기본 공격     : AlterData의 Override Attack — 길이 · 판정 시간 · 범위 · 배율, 클립 타격 순간과 판정 구간 맞춤 표시,
///                     씬에서 범위 · 각도 · 공격 충격파(Attack Wave)를 마우스로
///   ③ 능력 범위     : 능력에 [AlterGizmo]가 붙은 범위 (용: 활공 · 푸스로다 · 브레스) — 그 동작 · 프레임일 때 강조, 마우스로 수정
///   ④ 피격 범위     : AlterData.Hitboxes 보기 · 추가 · 이동 · 회전 · 크기 (마우스), 뼈 위치로 · 모델 감싸기
/// 미리보기 모델은 씬에 임시로만 생김 (저장 안 됨, 창을 닫으면 사라짐). 플레이 중엔 값만 고칠 수 있음.
/// </summary>
public class AlterSetupWindow : EditorWindow
{
    [MenuItem("Tools/Game/변화 설정 (Alter Setup)")]
    public static void Open() => GetWindow<AlterSetupWindow>("변화 설정");

    public static void Open(AlterData alter)
    {
        var w = GetWindow<AlterSetupWindow>("변화 설정");
        if (alter != null && w._alter != alter) { w._alter = alter; w.OnTargetsChanged(true); }
    }

    [MenuItem("CONTEXT/AlterData/변화 설정 창 열기")]
    static void OpenFromContext(MenuCommand cmd) => Open(cmd.context as AlterData);

    // ══════════════════ 대상 ══════════════════

    [SerializeField] AlterData _alter;
    [SerializeField] ScriptableObject _ability;
    [SerializeField] AnimationClip _looseClip;

    SerializedObject _alterSO, _animSO, _abilSO;
    AlterClipAnimator _animAsset;
    AlterGizmoAttribute[] _gizmos = Array.Empty<AlterGizmoAttribute>();

    // ══════════════════ 미리보기 ══════════════════

    Transform _root;                    // 플레이어 자리 (발밑, 앞 = +Z)
    GameObject _model;
    Animator _anim;
    Vector3 _animBasePos;
    Quaternion _animBaseRot;
    [SerializeField] Vector3 _anchor;
    [SerializeField] float _yaw;
    [SerializeField] float _eyeHeight = 1.6f;
    [SerializeField] float _pitch;
    [SerializeField] bool _lockRoot = true;

    // 보기
    [SerializeField] bool _showHitboxes = true, _showAttack = true, _showAbility = true, _onlyTiming;
    [SerializeField] int _tab;
    Vector2 _scroll;

    // ══════════════════ 타임라인 ══════════════════

    class Track
    {
        public string Name;
        public AnimationClip Clip;
        public SerializedProperty State;       // States[i] (없으면 Idle · Walk · Run · 임시)
        public int Kind;                        // 0 Idle · 1 Walk · 2 Run · 3 State · 4 임시 클립
    }

    readonly List<Track> _tracks = new();
    [SerializeField] int _sel;
    [SerializeField] float _time;
    bool _playing;
    double _lastTick;
    [SerializeField] float _playRate = 1f;

    struct Mark { public float Frame; public Color Color; public string Label; }
    struct Band { public float From, To; public Color Color; public string Label; }
    readonly List<Mark> _marks = new();
    readonly List<Band> _bands = new();

    // 찾기
    readonly List<Transform> _feetCand = new();
    readonly List<bool> _feetOn = new();
    Transform _probe;
    int _probeMode;
    static readonly string[] ProbeModes = { "가장 빠른 프레임", "가장 낮은 프레임", "가장 높은 프레임", "가장 앞 프레임", "가장 뒤 프레임" };
    [SerializeField] float _liftTol = 0.08f;
    string _findMsg = "";

    // 편집
    [SerializeField] int _gizmoSel;
    [SerializeField] int _hbSel;
    [SerializeField] int _hbTool;           // 0 이동 · 1 회전 · 2 크기
    readonly BoxBoundsHandle _boxH = new();
    readonly CapsuleBoundsHandle _capH = new() { heightAxis = CapsuleBoundsHandle.HeightAxis.Z };
    readonly SphereBoundsHandle _sphH = new();

    static readonly string[] Tabs = { "동작 · 타이밍", "기본 공격", "능력 범위", "피격 범위" };
    static readonly string[] HbTools = { "이동", "회전", "크기" };

    // ══════════════════ 생명주기 ══════════════════

    void OnEnable()
    {
        SceneView.duringSceneGui += OnScene;
        EditorApplication.update += OnUpdate;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Undo.undoRedoPerformed += OnUndo;
        CleanupOrphans();
        OnTargetsChanged(false);
        EditorApplication.delayCall += () => { if (this != null && _alter != null && _root == null) OnTargetsChanged(true); };
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnScene;
        EditorApplication.update -= OnUpdate;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        Undo.undoRedoPerformed -= OnUndo;
        ClearPreview();
    }

    void OnPlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingEditMode) ClearPreview();
    }

    void OnUndo()
    {
        _alterSO?.Update();
        _animSO?.Update();
        _abilSO?.Update();
        Sample();
        Repaint();
    }

    void OnTargetsChanged(bool rebuild)
    {
        _alterSO = _alter != null ? new SerializedObject(_alter) : null;
        _abilSO = _ability != null ? new SerializedObject(_ability) : null;
        _gizmos = _ability != null
            ? _ability.GetType().GetCustomAttributes(typeof(AlterGizmoAttribute), true).Cast<AlterGizmoAttribute>().ToArray()
            : Array.Empty<AlterGizmoAttribute>();

        var prefab = Prefab;
        _animAsset = prefab != null ? prefab.GetComponentInChildren<AlterClipAnimator>(true) : null;
        _animSO = _animAsset != null ? new SerializedObject(_animAsset) : null;

        if (rebuild && !EditorApplication.isPlayingOrWillChangePlaymode) BuildPreview();
        BuildTracks();
    }

    GameObject Prefab => _alterSO != null ? Prop(_alterSO, "Prefab")?.objectReferenceValue as GameObject : null;

    void OnUpdate()
    {
        if (!_playing) { _lastTick = EditorApplication.timeSinceStartup; return; }
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTick);
        _lastTick = now;
        var t = Cur;
        if (t == null || t.Clip == null) { _playing = false; return; }
        _time += dt * Mathf.Max(0.01f, PlaySpeed(t)) * _playRate;
        float len = t.Clip.length;
        if (_time > len) _time = len > 0.0001f ? _time % len : 0f;
        Sample();
        Repaint();
        SceneView.RepaintAll();
    }

    // ══════════════════ 미리보기 모델 ══════════════════

    void BuildPreview()
    {
        ClearPreview();
        if (_alterSO == null || EditorApplication.isPlayingOrWillChangePlaymode) return;

        CleanupOrphans();
        var rootGo = new GameObject(PreviewName);
        rootGo.hideFlags = HideFlags.DontSave;
        _root = rootGo.transform;
        _root.SetPositionAndRotation(_anchor, Quaternion.Euler(0f, _yaw, 0f));

        var prefab = Prefab;
        if (prefab != null)
        {
            _model = Instantiate(prefab, _root);
            _model.name = prefab.name;
            foreach (var tr in _model.GetComponentsInChildren<Transform>(true)) tr.gameObject.hideFlags = HideFlags.DontSave;
            foreach (var mb in _model.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;     // DragonWave 등 에디터 동작 끄기
            ApplyModelTransform();
            _anim = _model.GetComponentInChildren<Animator>(true);
            if (_anim != null)
            {
                _anim.applyRootMotion = false;
                _animBasePos = _anim.transform.localPosition;
                _animBaseRot = _anim.transform.localRotation;
            }
        }
        CollectFeet();
        Sample();
        SceneView.RepaintAll();
    }

    const string PreviewName = "AlterPreview (변화 설정 창 — 저장 안 됨)";

    /// <summary>스크립트 다시 불러오기 등으로 남은 예전 미리보기 지우기</summary>
    static void CleanupOrphans()
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == PreviewName && go.transform.parent == null && !EditorUtility.IsPersistent(go))
                DestroyImmediate(go);
    }

    void ClearPreview()
    {
        _playing = false;
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        if (_root != null) DestroyImmediate(_root.gameObject);
        _root = null;
        _model = null;
        _anim = null;
        _feetCand.Clear();
        _feetOn.Clear();
    }

    /// <summary>AlterData의 위치 · 회전 · 크기 → 미리보기 모델 (매번 — 칸을 바꾸면 바로 보임)</summary>
    void ApplyModelTransform()
    {
        if (_root != null) _root.SetPositionAndRotation(_anchor, Quaternion.Euler(0f, _yaw, 0f));
        if (_model == null || _alterSO == null) return;
        var pos = Prop(_alterSO, "Position");
        var rot = Prop(_alterSO, "Rotation");
        var scl = Prop(_alterSO, "Scale");
        var t = _model.transform;
        Vector3 lp = pos != null && pos.propertyType == SerializedPropertyType.Vector3 ? pos.vector3Value : Vector3.zero;
        Vector3 lr = rot != null && rot.propertyType == SerializedPropertyType.Vector3 ? rot.vector3Value : Vector3.zero;
        Vector3 ls = Vector3.one;
        if (scl != null)
        {
            if (scl.propertyType == SerializedPropertyType.Vector3) ls = scl.vector3Value;
            else if (scl.propertyType == SerializedPropertyType.Float) ls = Vector3.one * scl.floatValue;
        }
        if (t.localPosition != lp) t.localPosition = lp;
        if (t.localEulerAngles != lr) t.localRotation = Quaternion.Euler(lr);
        if (t.localScale != ls) t.localScale = ls;
    }

    void CollectFeet()
    {
        _feetCand.Clear();
        _feetOn.Clear();
        if (_model == null) return;
        foreach (var t in _model.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("ik") || n.Contains("end") || n.Contains("nub")) continue;
            bool foot = n.Contains("foot") || n.Contains("발");
            bool toe = n.Contains("toe") || n.Contains("paw") || n.Contains("claw");
            if (!foot && !toe) continue;
            _feetCand.Add(t);
            _feetOn.Add(foot);
        }
    }

    // ══════════════════ 샘플링 ══════════════════

    void SampleAt(AnimationClip clip, float time)
    {
        if (_anim == null || clip == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(_anim.gameObject, clip, Mathf.Clamp(time, 0f, clip.length));
        AnimationMode.EndSampling();
        if (_lockRoot)
        {
            _anim.transform.localPosition = _animBasePos;          // 게임처럼 루트 모션은 버림
            _anim.transform.localRotation = _animBaseRot;
        }
    }

    void Sample()
    {
        ApplyModelTransform();
        var t = Cur;
        if (t != null) SampleAt(t.Clip, _time);
    }

    // ══════════════════ 트랙 ══════════════════

    Track Cur => _sel >= 0 && _sel < _tracks.Count ? _tracks[_sel] : null;

    void BuildTracks()
    {
        _tracks.Clear();
        if (_animSO != null)
        {
            _animSO.Update();
            AddTrack("Idle", Prop(_animSO, "Idle")?.objectReferenceValue as AnimationClip, null, 0);
            AddTrack("Walk", Prop(_animSO, "Walk")?.objectReferenceValue as AnimationClip, null, 1);
            AddTrack("Run", Prop(_animSO, "Run")?.objectReferenceValue as AnimationClip, null, 2);
            var states = Prop(_animSO, "States");
            if (states != null && states.isArray)
                for (int i = 0; i < states.arraySize; i++)
                {
                    var s = states.GetArrayElementAtIndex(i);
                    string name = s.FindPropertyRelative("Name").stringValue;
                    AddTrack(string.IsNullOrEmpty(name) ? $"(이름 없음 {i})" : name,
                        s.FindPropertyRelative("Clip").objectReferenceValue as AnimationClip, s, 3);
                }
        }
        if (_looseClip != null) AddTrack($"임시: {_looseClip.name}", _looseClip, null, 4);
        _sel = Mathf.Clamp(_sel, 0, Mathf.Max(0, _tracks.Count - 1));
    }

    void AddTrack(string name, AnimationClip clip, SerializedProperty state, int kind)
    {
        if (clip == null && kind != 3) return;
        _tracks.Add(new Track { Name = name, Clip = clip, State = state, Kind = kind });
    }

    Track TrackByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var t in _tracks) if (t.Kind == 3 && t.Name == name) return t;
        return null;
    }

    static float Fps(AnimationClip c) => c != null ? Mathf.Max(1f, c.frameRate) : 30f;
    static int Frames(AnimationClip c) => c != null ? Mathf.Max(1, Mathf.RoundToInt(c.length * Fps(c))) : 1;
    float CurFrame => Cur != null && Cur.Clip != null ? _time * Fps(Cur.Clip) : 0f;

    /// <summary>상태의 Hit Frame 시각 (클립 초) — 없으면 -1</summary>
    static float HitClipTime(Track t)
    {
        if (t == null || t.Clip == null || t.State == null) return -1f;
        int hf = t.State.FindPropertyRelative("HitFrame").intValue;
        float ha = t.State.FindPropertyRelative("HitAt").floatValue;
        if (hf >= 0) return Mathf.Min(hf / Fps(t.Clip), t.Clip.length);
        if (ha >= 0f) return ha * t.Clip.length;
        return -1f;
    }

    string AttackAnimName => _alterSO != null ? Prop(_alterSO, "AttackAnim")?.stringValue : null;
    bool AttackOn => _alterSO != null && (Prop(_alterSO, "OverrideAttack")?.boolValue ?? false);

    /// <summary>재생 속도 — 공격 상태면 판정 시간에 맞춘 속도 (게임과 같게), 아니면 상태의 Speed</summary>
    float PlaySpeed(Track t)
    {
        if (t == null) return 1f;
        if (t.Kind == 3 && AttackOn && t.Name == AttackAnimName && AttackMap(t, out float spd, out _, out _, out _, out _)) return spd;
        if (t.State != null) return Mathf.Max(0.01f, t.State.FindPropertyRelative("Speed").floatValue);
        return 1f;
    }

    /// <summary>
    /// 변화 기본 공격 ↔ 클립 (AlterClipAnimator.PlayAttack과 같은 계산, 공격 속도 1 기준).
    /// 판정 시작 · 끝 · 캔슬 · 공격 끝을 클립 초로
    /// </summary>
    bool AttackMap(Track t, out float speed, out float c0, out float c1, out float cCancel, out float cEnd)
    {
        speed = 1f; c0 = c1 = cCancel = cEnd = 0f;
        var step = Prop(_alterSO, "Attack");
        if (step == null || t == null || t.Clip == null) return false;
        float dur = Mathf.Max(0.01f, step.FindPropertyRelative("Duration").floatValue);
        float hs = step.FindPropertyRelative("HitStart").floatValue;
        float he = step.FindPropertyRelative("HitEnd").floatValue;
        float cp = step.FindPropertyRelative("CancelPoint").floatValue;
        float len = t.Clip.length;
        float hitAt = dur * hs;
        float hit01 = -1f;
        if (t.State != null)
        {
            int hf = t.State.FindPropertyRelative("HitFrame").intValue;
            float ha = t.State.FindPropertyRelative("HitAt").floatValue;
            hit01 = hf >= 0 ? Mathf.Clamp01(hf / Mathf.Max(1f, len * t.Clip.frameRate)) : ha;
        }
        if (hit01 >= 0f && hitAt > 0.01f) speed = hit01 * len / hitAt;
        else speed = len / dur;
        speed = Mathf.Clamp(speed, 0.1f, 5f);
        c0 = dur * hs * speed;
        c1 = dur * he * speed;
        cCancel = dur * cp * speed;
        cEnd = dur * speed;
        return true;
    }

    // ══════════════════ 능력 범위 도우미 ══════════════════

    string StateOf(AlterGizmoAttribute g)
    {
        if (string.IsNullOrEmpty(g.State)) return null;
        if (g.State.StartsWith("=")) return g.State.Substring(1);
        return Prop(_abilSO, g.State)?.stringValue;
    }

    /// <summary>지금 타임라인이 이 범위의 판정 순간인가</summary>
    bool GizmoTiming(AlterGizmoAttribute g, out string info)
    {
        string st = StateOf(g);
        if (string.IsNullOrEmpty(st)) { info = "항상"; return true; }
        var cur = Cur;
        bool viewing = cur != null && cur.Kind == 3 && cur.Name == st;
        var sf = Prop(_abilSO, g.StartFrame);
        var ef = Prop(_abilSO, g.EndFrame);
        if (sf != null && ef != null)
        {
            info = $"'{st}' {sf.intValue} ~ {ef.intValue} 프레임";
            float f = CurFrame;
            return viewing && f >= sf.intValue && f <= ef.intValue;
        }
        float hit = HitClipTime(TrackByName(st));
        if (hit < 0f) { info = $"'{st}' 재생 중 내내"; return viewing; }
        var tr = TrackByName(st);
        info = $"'{st}' Hit Frame {Mathf.RoundToInt(hit * Fps(tr.Clip))}";
        return viewing && Mathf.Abs(_time - hit) <= 0.08f;
    }

    bool GizmoEnabled(AlterGizmoAttribute g)
    {
        var p = Prop(_abilSO, g.Enabled);
        return p == null || p.propertyType != SerializedPropertyType.Boolean || p.boolValue;
    }

    Transform FindBone(string name)
    {
        if (_model == null || string.IsNullOrEmpty(name)) return null;
        foreach (var t in _model.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    Vector3 Flat => _root != null ? _root.forward : Vector3.forward;
    Vector3 Aim => _root != null ? Quaternion.AngleAxis(-_pitch, _root.right) * _root.forward : Vector3.forward;

    Vector3 GizmoOrigin(AlterGizmoAttribute g, out bool fromBone)
    {
        fromBone = false;
        if (_root == null) return Vector3.zero;
        if (g.Origin == AlterGizmoOrigin.Bone)
        {
            var bone = FindBone(Prop(_abilSO, g.Bone)?.stringValue);
            if (bone != null) { fromBone = true; return bone.position; }
            var off = Prop(_abilSO, g.Offset);
            if (off != null && off.propertyType == SerializedPropertyType.Vector3) return _root.TransformPoint(off.vector3Value);
        }
        float fwd = !string.IsNullOrEmpty(g.ForwardField) ? Num(Prop(_abilSO, g.ForwardField)) : g.Forward;
        Vector3 dir = g.Pitch ? Aim : Flat;
        return _root.position + Vector3.up * g.Up + dir * fwd;
    }

    // ══════════════════ GUI ══════════════════

    void OnGUI()
    {
        _alterSO?.Update();
        _animSO?.Update();
        _abilSO?.Update();
        BuildTracks();

        TargetsGUI();
        if (_alterSO == null)
        {
            EditorGUILayout.HelpBox("변화(AlterData)를 넣으세요. AlterData 인스펙터 오른쪽 위 ⋮ → '변화 설정 창 열기'로도 열림.", MessageType.Info);
            return;
        }

        PreviewGUI();
        TimelineGUI();

        EditorGUILayout.Space(4);
        _tab = GUILayout.Toolbar(_tab, Tabs);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        switch (_tab)
        {
            case 0: MotionTabGUI(); break;
            case 1: AttackTabGUI(); break;
            case 2: AbilityTabGUI(); break;
            case 3: HitboxTabGUI(); break;
        }
        EditorGUILayout.EndScrollView();

        bool changed = false;
        if (_alterSO != null && _alterSO.ApplyModifiedProperties()) changed = true;
        if (_animSO != null && _animSO.ApplyModifiedProperties()) { changed = true; EditorUtility.SetDirty(_animAsset); }
        if (_abilSO != null && _abilSO.ApplyModifiedProperties()) changed = true;
        if (changed) { Sample(); SceneView.RepaintAll(); }
    }

    void TargetsGUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            _alter = (AlterData)EditorGUILayout.ObjectField("변화 (AlterData)", _alter, typeof(AlterData), false);
            using (new EditorGUILayout.HorizontalScope())
            {
                _ability = (ScriptableObject)EditorGUILayout.ObjectField(
                    new GUIContent("능력 (선택)", "범위를 같이 볼 능력 — 전설의 용언 · 체스 등 [AlterGizmo]가 붙은 것"),
                    _ability, typeof(ScriptableObject), false);
                if (GUILayout.Button(new GUIContent("자동 찾기", "이 변화를 쓰는 능력 (또는 능력이 쓰는 변화)"), GUILayout.Width(70)))
                    AutoFind();
            }
            _looseClip = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent("임시 클립 (선택)", "AlterClipAnimator에 없는 클립을 잠깐 넘겨 보기"), _looseClip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck()) OnTargetsChanged(true);

            if (_alterSO != null && _animSO == null)
                EditorGUILayout.HelpBox("모델 프리팹에 AlterClipAnimator가 없음 — 타이밍 편집은 안 되고 '임시 클립'으로 보기만 됨.", MessageType.None);
        }
    }

    void AutoFind()
    {
        if (_alter != null && _ability == null)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AbilityBehaviour"))
            {
                var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null && References(a, _alter)) { _ability = a; break; }
            }
            if (_ability == null) ShowNotification(new GUIContent("이 변화를 쓰는 능력이 없음"));
        }
        else if (_ability != null && _alter == null)
        {
            var it = new SerializedObject(_ability).GetIterator();
            while (it.Next(true))
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is AlterData ad) { _alter = ad; break; }
            if (_alter == null) ShowNotification(new GUIContent("이 능력이 쓰는 변화가 없음"));
        }
        else if (_alter != null)
        {
            _ability = null;
            AutoFind();
            return;
        }
        OnTargetsChanged(true);
    }

    static bool References(UnityEngine.Object owner, UnityEngine.Object target)
    {
        var it = new SerializedObject(owner).GetIterator();
        while (it.Next(true))
            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == target) return true;
        return false;
    }

    void PreviewGUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(playing))
                {
                    if (GUILayout.Button(_root == null ? "미리보기 놓기" : "다시 놓기")) BuildPreview();
                    using (new EditorGUI.DisabledScope(_root == null))
                    {
                        if (GUILayout.Button("지우기")) ClearPreview();
                        if (GUILayout.Button("씬 뷰 맞추기")) FrameScene();
                    }
                    if (GUILayout.Button(new GUIContent("씬 시점 중앙으로", "씬 뷰가 보고 있는 곳에 놓기")))
                    {
                        var sv = SceneView.lastActiveSceneView;
                        if (sv != null) { _anchor = sv.pivot; _anchor.y = Mathf.Round(_anchor.y * 10f) / 10f; }
                        if (_root == null) BuildPreview(); else Sample();
                    }
                }
            }
            if (playing) EditorGUILayout.HelpBox("플레이 중 — 미리보기는 꺼짐. 값은 고칠 수 있고 바로 게임에 반영됨.", MessageType.None);

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 60;
                _anchor = EditorGUILayout.Vector3Field("위치", _anchor);
                EditorGUIUtility.labelWidth = 30;
                _yaw = EditorGUILayout.FloatField("방향", _yaw, GUILayout.Width(80));
                EditorGUIUtility.labelWidth = 0;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _pitch = EditorGUILayout.Slider(new GUIContent("시선 위아래 (°)", "활공 · 브레스처럼 시선을 따라가는 범위 확인용 (+ = 위)"), _pitch, -80f, 80f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _eyeHeight = EditorGUILayout.FloatField(new GUIContent("눈 높이 (m)", "기본 공격 판정 기준 = 눈 − 0.7m (무기 설정 창과 같게)"), _eyeHeight);
                _lockRoot = EditorGUILayout.ToggleLeft(new GUIContent("제자리 (게임처럼)", "루트 모션을 버림 — 게임에서 보이는 모습. 끄면 클립이 움직이는 그대로"), _lockRoot, GUILayout.Width(130));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _showHitboxes = GUILayout.Toggle(_showHitboxes, "피격 범위", EditorStyles.miniButtonLeft);
                _showAttack = GUILayout.Toggle(_showAttack, "기본 공격", EditorStyles.miniButtonMid);
                _showAbility = GUILayout.Toggle(_showAbility, "능력 범위", EditorStyles.miniButtonMid);
                _onlyTiming = GUILayout.Toggle(_onlyTiming, new GUIContent("판정 순간에만", "공격 · 능력 범위를 판정 프레임일 때만"), EditorStyles.miniButtonRight);
            }
            if (EditorGUI.EndChangeCheck()) { Sample(); SceneView.RepaintAll(); }

            if (Fold("model", "모델 자리 (AlterData)"))
            {
                Field(_alterSO, "Prefab", "모델 프리팹");
                Field(_alterSO, "Attach", "붙는 자리");
                Field(_alterSO, "Position", "위치");
                Field(_alterSO, "Rotation", "회전");
                Field(_alterSO, "Scale", "크기");
                if (GUILayout.Button("모델 다시 불러오기 (프리팹 바꿨을 때)")) OnTargetsChanged(true);
            }
        }
    }

    void FrameScene()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null || _root == null) return;
        var b = new Bounds(_root.position + Vector3.up, Vector3.one * 2f);
        if (_model != null)
            foreach (var r in _model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        sv.Frame(b, false);
    }

    // ── 타임라인 ──

    void TimelineGUI()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (_tracks.Count == 0) { EditorGUILayout.LabelField("동작 없음 — 모델의 AlterClipAnimator에 클립을 넣거나 '임시 클립'"); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                var names = _tracks.Select(t => t.Clip == null ? $"{t.Name} (클립 없음)" : t.Name).ToArray();
                EditorGUI.BeginChangeCheck();
                _sel = EditorGUILayout.Popup("동작", _sel, names);
                if (EditorGUI.EndChangeCheck()) { _time = 0f; Sample(); SceneView.RepaintAll(); }
                if (GUILayout.Button(_playing ? "■" : "▶", GUILayout.Width(28))) { _playing = !_playing; _lastTick = EditorApplication.timeSinceStartup; }
                _playRate = EditorGUILayout.Slider(_playRate, 0.05f, 2f, GUILayout.Width(130));
            }

            var t = Cur;
            if (t == null || t.Clip == null) { EditorGUILayout.LabelField("이 상태에 클립이 없음"); return; }

            int frames = Frames(t.Clip);
            float fps = Fps(t.Clip);
            CollectMarks(t);
            DrawTimeline(t, frames, fps);

            using (new EditorGUILayout.HorizontalScope())
            {
                int f0 = Mathf.RoundToInt(_time * fps);
                int f = EditorGUILayout.IntField("프레임", f0, GUILayout.Width(200));
                if (GUILayout.Button("◀", GUILayout.Width(24))) f--;
                if (GUILayout.Button("▶", GUILayout.Width(24))) f++;
                if (f != f0) { _playing = false; _time = Mathf.Clamp(f, 0, frames) / fps; Sample(); SceneView.RepaintAll(); }
                float spd = PlaySpeed(t);
                GUILayout.Label($"/ {frames}  ·  {_time:F2}s / {t.Clip.length:F2}s  ·  게임 속도 ×{spd:F2} → {(_time / spd):F2}s", EditorStyles.miniLabel);
            }
        }
    }

    void CollectMarks(Track t)
    {
        _marks.Clear();
        _bands.Clear();
        float fps = Fps(t.Clip);

        // Hit Frame
        float hit = HitClipTime(t);
        if (hit >= 0f) _marks.Add(new Mark { Frame = hit * fps, Color = new Color(1f, 0.85f, 0.2f), Label = "Hit" });

        // 발걸음
        if (_animSO != null && (t.Kind == 1 || t.Kind == 2))
        {
            var arr = Prop(_animSO, t.Kind == 1 ? "WalkStepFrames" : "RunStepFrames");
            bool any = arr != null && arr.isArray && arr.arraySize > 0;
            if (any)
                for (int i = 0; i < arr.arraySize; i++)
                    _marks.Add(new Mark { Frame = arr.GetArrayElementAtIndex(i).intValue, Color = new Color(0.3f, 0.9f, 1f), Label = "발" });
            else
            {
                var st = Prop(_animSO, "StepTimes");
                if (st != null && st.isArray)
                    for (int i = 0; i < st.arraySize; i++)
                        _marks.Add(new Mark { Frame = st.GetArrayElementAtIndex(i).floatValue * Frames(t.Clip), Color = new Color(0.3f, 0.6f, 0.7f), Label = "발(비율)" });
            }
        }

        // 기본 공격 판정
        if (t.Kind == 3 && AttackOn && t.Name == AttackAnimName && AttackMap(t, out _, out float c0, out float c1, out float cc, out float ce))
        {
            _bands.Add(new Band { From = c0 * fps, To = c1 * fps, Color = new Color(1f, 0.25f, 0.15f, 0.55f), Label = "판정" });
            _marks.Add(new Mark { Frame = cc * fps, Color = new Color(0.5f, 1f, 0.5f), Label = "캔슬" });
            if (ce < t.Clip.length - 0.01f) _marks.Add(new Mark { Frame = ce * fps, Color = Color.white, Label = "공격 끝" });
        }

        // 능력 판정 구간
        foreach (var g in _gizmos)
        {
            if (StateOf(g) != t.Name || t.Kind != 3) continue;
            var sf = Prop(_abilSO, g.StartFrame);
            var ef = Prop(_abilSO, g.EndFrame);
            var col = GizmoColor(g);
            if (sf != null && ef != null)
                _bands.Add(new Band { From = sf.intValue, To = ef.intValue, Color = new Color(col.r, col.g, col.b, 0.45f), Label = g.Label });
            else if (hit >= 0f)
                _marks.Add(new Mark { Frame = hit * fps, Color = col, Label = g.Label });
        }
    }

    void DrawTimeline(Track t, int frames, float fps)
    {
        Rect r = GUILayoutUtility.GetRect(10f, 58f, GUILayout.ExpandWidth(true));
        Rect bar = new(r.x + 6f, r.y + 14f, r.width - 12f, r.height - 18f);
        float X(float frame) => bar.x + bar.width * Mathf.Clamp(frame, 0f, frames) / Mathf.Max(1, frames);

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, new Color(0.13f, 0.13f, 0.13f));
            EditorGUI.DrawRect(bar, new Color(0.2f, 0.2f, 0.22f));

            foreach (var b in _bands)
            {
                float x0 = X(Mathf.Min(b.From, b.To)), x1 = X(Mathf.Max(b.From, b.To));
                EditorGUI.DrawRect(new Rect(x0, bar.y, Mathf.Max(2f, x1 - x0), bar.height), b.Color);
                GUI.Label(new Rect(x0 + 2f, bar.yMax - 14f, 160f, 14f), b.Label, EditorStyles.whiteMiniLabel);
            }

            int step = frames <= 40 ? 5 : frames <= 120 ? 10 : frames <= 300 ? 30 : 60;
            for (int f = 0; f <= frames; f += step)
            {
                float x = X(f);
                EditorGUI.DrawRect(new Rect(x, bar.y, 1f, 5f), new Color(1f, 1f, 1f, 0.35f));
                GUI.Label(new Rect(x - 12f, r.y, 30f, 14f), f.ToString(), EditorStyles.miniLabel);
            }

            int row = 0;
            foreach (var m in _marks)
            {
                float x = X(m.Frame);
                EditorGUI.DrawRect(new Rect(x - 1f, bar.y, 2f, bar.height), m.Color);
                var old = GUI.color;
                GUI.color = m.Color;
                GUI.Label(new Rect(x + 2f, bar.y + 1f + (row % 2) * 12f, 90f, 14f), m.Label, EditorStyles.miniBoldLabel);
                GUI.color = old;
                row++;
            }

            float px = X(_time * fps);
            EditorGUI.DrawRect(new Rect(px - 1f, r.y + 10f, 2f, r.height - 10f), Color.white);
        }

        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (r.Contains(e.mousePosition) && e.button == 0)
                {
                    GUIUtility.hotControl = id;
                    Scrub(e.mousePosition.x, bar, frames, fps);
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) { Scrub(e.mousePosition.x, bar, frames, fps); e.Use(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
        }
    }

    void Scrub(float mx, Rect bar, int frames, float fps)
    {
        float f = Mathf.Round(Mathf.Clamp01((mx - bar.x) / Mathf.Max(1f, bar.width)) * frames);
        _time = f / fps;
        _playing = false;
        Sample();
        Repaint();
        SceneView.RepaintAll();
    }

    void JumpTo(string state, float clipTime)
    {
        for (int i = 0; i < _tracks.Count; i++)
            if (_tracks[i].Kind == 3 && _tracks[i].Name == state) { _sel = i; _time = Mathf.Max(0f, clipTime); _playing = false; Sample(); SceneView.RepaintAll(); return; }
        ShowNotification(new GUIContent($"'{state}' 상태가 없음"));
    }

    // ══════════════════ ① 동작 · 타이밍 ══════════════════

    void MotionTabGUI()
    {
        var t = Cur;
        if (t == null) return;
        int frame = Mathf.RoundToInt(CurFrame);

        if (t.Kind == 3 && t.State != null)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"상태 '{t.Name}'", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(t.State.FindPropertyRelative("Name"));
                EditorGUILayout.PropertyField(t.State.FindPropertyRelative("Clip"));
                EditorGUILayout.PropertyField(t.State.FindPropertyRelative("Speed"));
                EditorGUILayout.PropertyField(t.State.FindPropertyRelative("FadeIn"));
                EditorGUILayout.PropertyField(t.State.FindPropertyRelative("HoldEnd"));
                var hf = t.State.FindPropertyRelative("HitFrame");
                var ha = t.State.FindPropertyRelative("HitAt");
                EditorGUILayout.PropertyField(hf, new GUIContent("Hit Frame", "타격 · 이륙 · 외침 순간 (클립 프레임). -1 = 없음"));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"지금({frame}) = Hit Frame")) { hf.intValue = frame; ha.floatValue = -1f; }
                    if (GUILayout.Button("Hit Frame 지우기")) { hf.intValue = -1; ha.floatValue = -1f; }
                    using (new EditorGUI.DisabledScope(HitClipTime(t) < 0f))
                        if (GUILayout.Button("Hit Frame으로 가기")) { _time = HitClipTime(t); Sample(); SceneView.RepaintAll(); }
                }
                if (hf.intValue < 0) EditorGUILayout.PropertyField(ha, new GUIContent("Hit At (0~1)", "Hit Frame이 없을 때 — 비율"));
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("이륙 찾기 (도약 · 이륙 상태)", EditorStyles.boldLabel);
                _liftTol = EditorGUILayout.FloatField(new GUIContent("떠난 높이 (m)", "발이 이만큼 올라가면 '떠남'"), _liftTol);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("루트 높이로 찾기", "클립의 몸통(루트) 높이 곡선이 오르기 시작하는 프레임 — Bake와 상관없이 됨")))
                        ShowFound(FindTakeoffByCurve(t.Clip), t, true);
                    if (GUILayout.Button(new GUIContent("발로 찾기", "선택한 발 뼈가 모두 땅을 떠나는 첫 프레임 ('제자리' 끄고 쓰기)")))
                        ShowFound(FindTakeoffByFeet(t.Clip), t, true);
                }
            }
        }

        if ((t.Kind == 1 || t.Kind == 2) && _animSO != null)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string field = t.Kind == 1 ? "WalkStepFrames" : "RunStepFrames";
                var arr = Prop(_animSO, field);
                EditorGUILayout.LabelField($"발걸음 ({t.Name}) — 쿵쿵 · 흔들림 타이밍", EditorStyles.boldLabel);
                Field(_animSO, "Footsteps", "발걸음 켜기");
                if (t.Kind == 1) Field(_animSO, "WalkRefSpeed", "Walk 1배속 이동 속도");
                else { Field(_animSO, "RunThreshold", "Run 시작 속도"); Field(_animSO, "RunRefSpeed", "Run 1배속 이동 속도"); }
                if (arr == null) { EditorGUILayout.HelpBox($"AlterClipAnimator에 {field} 칸이 없음 — 이 패키지의 AlterClipAnimator로 교체", MessageType.Warning); return; }
                EditorGUILayout.PropertyField(arr, new GUIContent("발 닿는 프레임"), true);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"지금({frame}) 추가")) SetSteps(arr, Steps(arr).Append(frame));
                    if (GUILayout.Button("가장 가까운 것 지우기"))
                    {
                        var list = Steps(arr).ToList();
                        if (list.Count > 0) { list.RemoveAt(list.Select((v, i) => (d: Mathf.Abs(v - frame), i)).OrderBy(p => p.d).First().i); SetSteps(arr, list); }
                    }
                    if (GUILayout.Button("모두 지우기")) SetSteps(arr, Array.Empty<int>());
                }
                if (GUILayout.Button(new GUIContent("발걸음 자동 찾기 (아래 발 뼈)", "발 뼈가 가장 낮은 높이에 닿는 순간마다")))
                {
                    var found = FindSteps(t.Clip);
                    if (found.Count > 0) { SetSteps(arr, found); _findMsg = $"발걸음 {found.Count}개: {string.Join(", ", found)}"; }
                    else _findMsg = "못 찾음 — 발 뼈를 골랐는지 확인";
                    Sample();
                }
                FootsGUI("발 뼈 (발걸음 · 이륙 찾기에 씀)");
                Field(_animSO, "Footstep", "쿵 느낌 (Feel)", true);
            }
        }
        else if (t.Kind == 3) FootsGUI("발 뼈 (이륙 '발로 찾기'에 씀)");

        // 뼈로 찾기
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("뼈로 순간 찾기 (깨무는 순간 · 외치는 순간 등)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _probe = (Transform)EditorGUILayout.ObjectField("뼈", _probe, typeof(Transform), true);
                if (GUILayout.Button("고르기", GUILayout.Width(52))) BoneMenu(tr => { _probe = tr; Repaint(); });
                if (GUILayout.Button(new GUIContent("선택한 것", "하이어라키에서 미리보기 모델의 뼈를 선택하고 누르기"), GUILayout.Width(66)) && Selection.activeTransform != null)
                    _probe = Selection.activeTransform;
            }
            _probeMode = EditorGUILayout.Popup("찾을 것", _probeMode, ProbeModes);
            using (new EditorGUI.DisabledScope(_probe == null || t.Clip == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("찾아서 가기")) ShowFound(FindByBone(t.Clip), t, false);
                if (t.Kind == 3 && GUILayout.Button("찾아서 Hit Frame으로")) ShowFound(FindByBone(t.Clip), t, true);
            }
        }

        if (!string.IsNullOrEmpty(_findMsg)) EditorGUILayout.HelpBox(_findMsg, MessageType.None);

        if (_animSO != null && Fold("animmore", "AlterClipAnimator 나머지 칸"))
        {
            var it = _animSO.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script" || it.name == "States") continue;
                EditorGUILayout.PropertyField(it, true);
            }
        }
    }

    void ShowFound(int frame, Track t, bool setHit)
    {
        if (frame < 0) { _findMsg = "못 찾음"; Sample(); return; }
        _time = frame / Fps(t.Clip);
        if (setHit && t.State != null)
        {
            t.State.FindPropertyRelative("HitFrame").intValue = frame;
            t.State.FindPropertyRelative("HitAt").floatValue = -1f;
            _findMsg = $"{frame} 프레임 → '{t.Name}' Hit Frame으로 넣음";
        }
        else _findMsg = $"{frame} 프레임";
        Sample();
        SceneView.RepaintAll();
    }

    void FootsGUI(string title)
    {
        if (!Fold("feet", title)) return;
        if (_model == null) { EditorGUILayout.LabelField("미리보기를 놓으면 나옴"); return; }
        for (int i = 0; i < _feetCand.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _feetOn[i] = EditorGUILayout.ToggleLeft(_feetCand[i] != null ? _feetCand[i].name : "(없음)", _feetOn[i]);
                if (GUILayout.Button("x", GUILayout.Width(20))) { _feetCand.RemoveAt(i); _feetOn.RemoveAt(i); break; }
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("뼈 추가")) BoneMenu(tr => { if (!_feetCand.Contains(tr)) { _feetCand.Add(tr); _feetOn.Add(true); } Repaint(); });
            if (GUILayout.Button("선택한 것 추가") && Selection.activeTransform != null && !_feetCand.Contains(Selection.activeTransform))
            { _feetCand.Add(Selection.activeTransform); _feetOn.Add(true); }
        }
    }

    static IEnumerable<int> Steps(SerializedProperty arr)
    {
        if (arr == null || !arr.isArray) yield break;
        for (int i = 0; i < arr.arraySize; i++) yield return arr.GetArrayElementAtIndex(i).intValue;
    }

    static void SetSteps(SerializedProperty arr, IEnumerable<int> values)
    {
        if (arr == null) return;
        var v = values.Distinct().OrderBy(x => x).ToList();
        arr.arraySize = v.Count;
        for (int i = 0; i < v.Count; i++) arr.GetArrayElementAtIndex(i).intValue = v[i];
    }

    // ── 찾기 계산 ──

    List<Transform> Feet()
    {
        var list = new List<Transform>();
        for (int i = 0; i < _feetCand.Count; i++) if (_feetOn[i] && _feetCand[i] != null) list.Add(_feetCand[i]);
        return list;
    }

    /// <summary>프레임마다 뼈 위치 (미리보기 자리 기준, m)</summary>
    Vector3[][] SampleBones(AnimationClip clip, IList<Transform> bones)
    {
        int n = Frames(clip);
        float fps = Fps(clip);
        var res = new Vector3[bones.Count][];
        for (int b = 0; b < bones.Count; b++) res[b] = new Vector3[n + 1];
        for (int f = 0; f <= n; f++)
        {
            SampleAt(clip, f / fps);
            for (int b = 0; b < bones.Count; b++) res[b][f] = _root.InverseTransformPoint(bones[b].position);
        }
        return res;
    }

    List<int> FindSteps(AnimationClip clip)
    {
        var feet = Feet();
        var found = new List<int>();
        if (clip == null || _root == null || feet.Count == 0) return found;
        var pos = SampleBones(clip, feet);
        int n = Frames(clip);
        foreach (var p in pos)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int f = 0; f < n; f++) { min = Mathf.Min(min, p[f].y); max = Mathf.Max(max, p[f].y); }
            if (max - min < 0.005f) continue;
            float tol = Mathf.Max(0.01f, (max - min) * 0.15f);
            for (int f = 0; f < n; f++)
            {
                bool low = p[f].y <= min + tol;
                bool prevLow = p[(f - 1 + n) % n].y <= min + tol;
                if (low && !prevLow) found.Add(f);              // 막 닿은 순간
            }
        }
        found.Sort();
        var merged = new List<int>();
        foreach (int f in found) if (merged.Count == 0 || f - merged[merged.Count - 1] > 2) merged.Add(f);
        return merged;
    }

    int FindTakeoffByFeet(AnimationClip clip)
    {
        var feet = Feet();
        if (clip == null || _root == null || feet.Count == 0) return -1;
        var pos = SampleBones(clip, feet);
        int n = Frames(clip);
        var ground = new float[feet.Count];
        for (int b = 0; b < feet.Count; b++)
        {
            ground[b] = float.MaxValue;
            for (int f = 0; f <= Mathf.Max(1, n / 3); f++) ground[b] = Mathf.Min(ground[b], pos[b][f].y);
        }
        for (int f = 0; f <= n; f++)
        {
            bool all = true;
            for (int b = 0; b < feet.Count && all; b++) all = pos[b][f].y > ground[b] + _liftTol;
            if (all) return f;
        }
        return -1;
    }

    /// <summary>클립의 루트 높이 곡선 (Humanoid RootT.y · Generic 가장 위 움직이는 뼈 y)이 오르기 시작하는 프레임</summary>
    int FindTakeoffByCurve(AnimationClip clip)
    {
        if (clip == null) return -1;
        var binds = AnimationUtility.GetCurveBindings(clip);
        AnimationCurve curve = null;
        foreach (var b in binds)
            if (b.propertyName == "RootT.y") { curve = AnimationUtility.GetEditorCurve(clip, b); break; }

        if (curve == null)
        {
            var cands = new List<(int depth, float range, AnimationCurve c)>();
            float maxRange = 0f;
            foreach (var b in binds)
            {
                if (b.propertyName != "m_LocalPosition.y" || b.type != typeof(Transform)) continue;
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null || c.length < 2) continue;
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var k in c.keys) { mn = Mathf.Min(mn, k.value); mx = Mathf.Max(mx, k.value); }
                int depth = string.IsNullOrEmpty(b.path) ? 0 : b.path.Count(ch => ch == '/') + 1;
                cands.Add((depth, mx - mn, c));
                maxRange = Mathf.Max(maxRange, mx - mn);
            }
            if (maxRange <= 1e-5f) return -1;
            curve = cands.Where(c => c.range >= maxRange * 0.2f).OrderBy(c => c.depth).ThenByDescending(c => c.range).First().c;
        }

        int n = Frames(clip);
        float fps = Fps(clip);
        float v0 = curve.Evaluate(0f), vmax = v0;
        for (int f = 0; f <= n; f++) vmax = Mathf.Max(vmax, curve.Evaluate(f / fps));
        if (vmax - v0 <= 1e-4f) return -1;
        float thr = v0 + (vmax - v0) * 0.1f;
        for (int f = 0; f <= n; f++) if (curve.Evaluate(f / fps) > thr) return f;
        return -1;
    }

    int FindByBone(AnimationClip clip)
    {
        if (clip == null || _probe == null || _root == null) return -1;
        var p = SampleBones(clip, new[] { _probe })[0];
        int n = Frames(clip), best = -1;
        float bestV = float.MinValue;
        for (int f = 0; f <= n; f++)
        {
            float v = _probeMode switch
            {
                0 => f > 0 ? (p[f] - p[f - 1]).magnitude : float.MinValue,
                1 => -p[f].y,
                2 => p[f].y,
                3 => p[f].z,
                _ => -p[f].z,
            };
            if (v > bestV) { bestV = v; best = f; }
        }
        return best;
    }

    void BoneMenu(Action<Transform> pick)
    {
        if (_model == null) { ShowNotification(new GUIContent("미리보기를 먼저 놓기")); return; }
        var menu = new GenericMenu();
        foreach (var t in _model.GetComponentsInChildren<Transform>(true))
        {
            if (t == _model.transform) continue;
            string path = AnimationUtility.CalculateTransformPath(t, _model.transform);
            var tr = t;
            menu.AddItem(new GUIContent(path), _probe == t, () => pick(tr));
        }
        menu.ShowAsContext();
    }

    // ══════════════════ ② 기본 공격 ══════════════════

    static readonly (string name, string label)[] StepFields =
    {
        ("Duration", "길이 (초)"), ("HitStart", "판정 시작 (0~1)"), ("HitEnd", "판정 끝 (0~1)"), ("CancelPoint", "캔슬 (0~1)"),
        ("OverrideHitShape", "범위 직접 (개별 판정)"), ("HitMode", "판정 모양"), ("Range", "거리 (m)"), ("HitAngle", "각도 (°)"),
        ("HitHeight", "높이 (m)"), ("MinRange", "최소 거리 (m)"), ("DamageMultiplier", "피해 배율"), ("MaxTargets", "최대 대상 (0 = 무제한)"),
        ("HitStopDuration", "히트 스톱 (초)"), ("CameraShake", "화면 흔들림"), ("LungeDistance", "앞으로 내딛기 (m)"),
    };

    void AttackTabGUI()
    {
        var anim = Prop(_alterSO, "AttackAnim");
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            Field(_alterSO, "OverrideAttack", "기본 공격 바꾸기 (Override Attack)");
            if (anim != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(anim, new GUIContent("공격 동작 (상태 이름)"));
                    var names = _tracks.Where(x => x.Kind == 3).Select(x => x.Name).ToArray();
                    if (names.Length > 0)
                    {
                        int cur = Array.IndexOf(names, anim.stringValue);
                        int pick = EditorGUILayout.Popup(cur, names, GUILayout.Width(110));
                        if (pick != cur && pick >= 0) anim.stringValue = names[pick];
                    }
                    if (GUILayout.Button("보기", GUILayout.Width(40))) JumpTo(anim.stringValue, 0f);
                }
            }
            var motion = Prop(_alterSO, "AttackMotion");
            if (motion != null)
            {
                EditorGUILayout.PropertyField(motion, new GUIContent("모션 에셋 (선택)"));
                if (motion.objectReferenceValue != null)
                    EditorGUILayout.HelpBox("모션 에셋이 들어 있어서 게임은 그 모션의 공격을 씀 — 아래 '공격'은 안 씀. 비우면 아래 값을 씀.", MessageType.Warning);
            }
        }

        var step = Prop(_alterSO, "Attack");
        if (step == null) { EditorGUILayout.HelpBox("AlterData에 'Attack'(공격 스텝) 칸이 없음 — 용 변신 패치가 들어갔는지 확인", MessageType.Warning); return; }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("공격 (판정)", EditorStyles.boldLabel);
            foreach (var (name, label) in StepFields)
            {
                var p = step.FindPropertyRelative(name);
                if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip));
            }
            if (!step.FindPropertyRelative("OverrideHitShape").boolValue)
                EditorGUILayout.HelpBox("'범위 직접'이 꺼져 있으면 게임은 쥔 무기의 범위를 씀 — 여기 그림은 위 값 기준(흐리게).", MessageType.None);

            var t = TrackByName(anim?.stringValue);
            if (t != null && t.Clip != null && AttackMap(t, out float spd, out float c0, out float c1, out _, out _))
            {
                float fps = Fps(t.Clip);
                float hit = HitClipTime(t);
                EditorGUILayout.HelpBox(
                    (hit >= 0f ? $"클립 타격 순간 {Mathf.RoundToInt(hit * fps)}프레임({hit:F2}s) = 판정 시작 → " : "클립 Hit Frame 없음 → 공격 길이에 맞춰 ") +
                    $"재생 ×{spd:F2}\n판정 구간 = 클립 {Mathf.RoundToInt(c0 * fps)} ~ {Mathf.RoundToInt(c1 * fps)} 프레임 (타임라인 빨간 칸)", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("판정 시작으로 가기")) JumpTo(t.Name, c0);
                    if (GUILayout.Button(new GUIContent("판정 끝 = 지금 프레임", "타임라인에서 판정이 끝나야 할 곳에 두고 누르기")) && Cur == t)
                    {
                        float dur = Mathf.Max(0.01f, step.FindPropertyRelative("Duration").floatValue);
                        step.FindPropertyRelative("HitEnd").floatValue = Mathf.Clamp01(_time / spd / dur);
                    }
                }
            }
            else if (anim != null) EditorGUILayout.HelpBox($"'{anim.stringValue}' 상태가 모델에 없음 — 동작 없이 판정만 됨", MessageType.None);
        }

        var wave = Prop(_alterSO, "AttackWave");
        if (wave != null)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("공격 충격파 (Attack Wave — 판정 순간 기절 · 밀치기 · 흔들림)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(wave, new GUIContent("Attack Wave"), true);
            }
    }

    // ══════════════════ ③ 능력 범위 ══════════════════

    void AbilityTabGUI()
    {
        if (_abilSO == null) { EditorGUILayout.HelpBox("위 '능력'을 넣거나 '자동 찾기'.", MessageType.Info); return; }
        if (_gizmos.Length == 0)
            EditorGUILayout.HelpBox($"{_ability.GetType().Name}에는 [AlterGizmo]가 없음 — 붙이는 법은 가이드 참고. 아래 '능력 전체 칸'은 고칠 수 있음.", MessageType.None);

        for (int i = 0; i < _gizmos.Length; i++)
        {
            var g = _gizmos[i];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var old = GUI.color;
                    GUI.color = GizmoColor(g);
                    if (GUILayout.Toggle(_gizmoSel == i, $"  {g.Label}", EditorStyles.radioButton)) _gizmoSel = i;
                    GUI.color = old;
                    bool now = GizmoTiming(g, out string info);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(info + (now ? "  ● 지금" : ""), EditorStyles.miniLabel);
                    string st = StateOf(g);
                    if (!string.IsNullOrEmpty(st) && GUILayout.Button("이 동작 보기", GUILayout.Width(80)))
                    {
                        var sf = Prop(_abilSO, g.StartFrame);
                        var tr = TrackByName(st);
                        float hit = HitClipTime(tr);
                        JumpTo(st, sf != null && tr != null ? sf.intValue / Fps(tr.Clip) : Mathf.Max(0f, hit));
                    }
                }
                if (_gizmoSel != i) continue;

                AField(g.Enabled, "켜짐");
                AField(g.Range, "거리 (m)");
                AField(g.Angle, "각도 (°)");
                AField(g.Height, "위아래 허용 (m)");
                AField(g.Radius, "반지름 (m)");
                AField(g.ForwardField, "앞으로 (m)");
                AField(g.State, "동작 (상태 이름)");
                if (g.Origin == AlterGizmoOrigin.Bone)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        AField(g.Bone, "뼈");
                        var bp = Prop(_abilSO, g.Bone);
                        if (bp != null)
                        {
                            if (GUILayout.Button("고르기", GUILayout.Width(52))) BoneMenu(tr => { _abilSO.Update(); bp.stringValue = tr.name; _abilSO.ApplyModifiedProperties(); SceneView.RepaintAll(); Repaint(); });
                            if (GUILayout.Button("선택한 것", GUILayout.Width(66)) && Selection.activeTransform != null) bp.stringValue = Selection.activeTransform.name;
                        }
                    }
                    if (FindBone(Prop(_abilSO, g.Bone)?.stringValue) == null)
                        EditorGUILayout.LabelField(_model == null ? "  (미리보기를 놓으면 뼈 확인)" : "  뼈 없음 → 아래 위치를 씀", EditorStyles.miniLabel);
                    AField(g.Offset, "위치 (뼈 없을 때, 플레이어 기준)");
                }
                int frame = Mathf.RoundToInt(CurFrame);
                FrameField(g.StartFrame, "판정 시작 프레임", frame);
                FrameField(g.EndFrame, "판정 끝 프레임", frame);
                EditorGUILayout.LabelField("씬에서 점을 끌어 거리 · 각도 · 반지름 · 위치 조절", EditorStyles.miniLabel);
            }
        }

        if (Fold("abilall", "능력 전체 칸"))
        {
            var it = _abilSO.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                EditorGUILayout.PropertyField(it, true);
            }
        }
    }

    void AField(string path, string label)
    {
        var p = Prop(_abilSO, path);
        if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip), true);
    }

    void FrameField(string path, string label, int frame)
    {
        var p = Prop(_abilSO, path);
        if (p == null) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(p, new GUIContent(label));
            if (GUILayout.Button($"지금({frame})", GUILayout.Width(70))) p.intValue = frame;
        }
    }

    static Color GizmoColor(AlterGizmoAttribute g) =>
        ColorUtility.TryParseHtmlString(g.Color, out var c) ? c : new Color(1f, 0.5f, 0.25f);

    // ══════════════════ ④ 피격 범위 ══════════════════

    void HitboxTabGUI()
    {
        var arr = Prop(_alterSO, "Hitboxes");
        if (arr == null) { EditorGUILayout.HelpBox("AlterData에 'Hitboxes' 칸이 없음", MessageType.Warning); return; }

        EditorGUILayout.HelpBox("기준 = 플레이어 발밑, x 오른쪽 · y 위 · z 앞 (m). 모델 위치 · 크기를 바꿔도 범위는 그대로.", MessageType.None);
        _hbTool = GUILayout.Toolbar(_hbTool, HbTools);

        for (int i = 0; i < arr.arraySize; i++)
        {
            var h = arr.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Toggle(_hbSel == i, $"  {i}", EditorStyles.radioButton, GUILayout.Width(40))) _hbSel = i;
                    EditorGUILayout.PropertyField(h.FindPropertyRelative("Shape"), GUIContent.none);
                    if (GUILayout.Button("복제", GUILayout.Width(40))) { arr.InsertArrayElementAtIndex(i); _hbSel = i + 1; break; }
                    if (GUILayout.Button("x", GUILayout.Width(22))) { arr.DeleteArrayElementAtIndex(i); _hbSel = Mathf.Min(_hbSel, arr.arraySize - 1); break; }
                }
                if (_hbSel != i) continue;
                int idx = i;
                EditorGUILayout.PropertyField(h.FindPropertyRelative("Center"), new GUIContent("중심"));
                EditorGUILayout.PropertyField(h.FindPropertyRelative("Size"), new GUIContent("크기", "캡슐: x = 지름 · z = 길이 / 박스: 크기 / 구: x = 지름"));
                EditorGUILayout.PropertyField(h.FindPropertyRelative("Rotation"), new GUIContent("회전"));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("뼈 위치로 중심", "고른 뼈의 지금 자세 위치로 (타임라인 프레임 기준)")))
                        BoneMenu(tr => { _alterSO.Update(); var c = Prop(_alterSO, "Hitboxes").GetArrayElementAtIndex(idx).FindPropertyRelative("Center"); c.vector3Value = Round(_root.InverseTransformPoint(tr.position)); _alterSO.ApplyModifiedProperties(); SceneView.RepaintAll(); Repaint(); });
                    if (GUILayout.Button("선택한 것 위치로") && Selection.activeTransform != null && _root != null)
                        h.FindPropertyRelative("Center").vector3Value = Round(_root.InverseTransformPoint(Selection.activeTransform.position));
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 캡슐")) AddHitbox(arr, AlterHitboxShape.Capsule, new Vector3(0, 1, 0), new Vector3(0.8f, 0.8f, 3f));
            if (GUILayout.Button("+ 박스")) AddHitbox(arr, AlterHitboxShape.Box, new Vector3(0, 1, 0), Vector3.one);
            if (GUILayout.Button("+ 구")) AddHitbox(arr, AlterHitboxShape.Sphere, new Vector3(0, 1, 0), Vector3.one);
        }
        using (new EditorGUI.DisabledScope(_model == null))
            if (GUILayout.Button(new GUIContent("+ 모델 전체 감싸는 캡슐", "지금 자세의 모델 크기로 (가장 긴 방향으로 눕힘)"))) WrapModel(arr);
    }

    void AddHitbox(SerializedProperty arr, AlterHitboxShape shape, Vector3 center, Vector3 size)
    {
        arr.arraySize++;
        var h = arr.GetArrayElementAtIndex(arr.arraySize - 1);
        h.FindPropertyRelative("Shape").enumValueIndex = (int)shape;
        h.FindPropertyRelative("Center").vector3Value = center;
        h.FindPropertyRelative("Size").vector3Value = size;
        h.FindPropertyRelative("Rotation").vector3Value = Vector3.zero;
        _hbSel = arr.arraySize - 1;
    }

    void WrapModel(SerializedProperty arr)
    {
        if (_model == null || _root == null) return;
        bool any = false;
        Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue;
        foreach (var r in _model.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = _root.InverseTransformPoint(c);
                mn = Vector3.Min(mn, l);
                mx = Vector3.Max(mx, l);
                any = true;
            }
        }
        if (!any) return;
        Vector3 size = mx - mn, center = (mn + mx) * 0.5f;
        Vector3 rot = Vector3.zero, s;
        if (size.z >= size.x && size.z >= size.y) s = new Vector3(Mathf.Max(size.x, size.y), 0f, size.z);
        else if (size.y >= size.x) { rot = new Vector3(90f, 0f, 0f); s = new Vector3(Mathf.Max(size.x, size.z), 0f, size.y); }
        else { rot = new Vector3(0f, 90f, 0f); s = new Vector3(Mathf.Max(size.y, size.z), 0f, size.x); }
        s.y = s.x;
        AddHitbox(arr, AlterHitboxShape.Capsule, Round(center), Round(s));
        arr.GetArrayElementAtIndex(arr.arraySize - 1).FindPropertyRelative("Rotation").vector3Value = rot;
    }

    static Vector3 Round(Vector3 v) => new(Mathf.Round(v.x * 100f) / 100f, Mathf.Round(v.y * 100f) / 100f, Mathf.Round(v.z * 100f) / 100f);

    // ══════════════════ 씬 ══════════════════

    void OnScene(SceneView sv)
    {
        if (_root == null || _alterSO == null) return;
        _alterSO.Update();
        _abilSO?.Update();

        if (_showHitboxes) SceneHitboxes();
        if (_showAttack) SceneAttack();
        if (_showAbility && _abilSO != null) SceneAbility();

        // 플레이어 자리 표시
        Handles.color = new Color(1f, 1f, 1f, 0.5f);
        Handles.DrawWireDisc(_root.position, Vector3.up, 0.4f);
        Handles.DrawLine(_root.position, _root.position + Flat * 0.8f);
        Handles.Label(_root.position + Vector3.up * 0.1f, Cur != null ? $"{Cur.Name}  {Mathf.RoundToInt(CurFrame)}f" : "", EditorStyles.whiteMiniLabel);

        bool changed = _alterSO.ApplyModifiedProperties();
        if (_abilSO != null && _abilSO.ApplyModifiedProperties()) changed = true;
        if (changed) Repaint();
    }

    // ── 피격 범위 ──

    void SceneHitboxes()
    {
        var arr = Prop(_alterSO, "Hitboxes");
        if (arr == null) return;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var h = arr.GetArrayElementAtIndex(i);
            var shape = (AlterHitboxShape)h.FindPropertyRelative("Shape").enumValueIndex;
            var cp = h.FindPropertyRelative("Center");
            var sp = h.FindPropertyRelative("Size");
            var rp = h.FindPropertyRelative("Rotation");
            Quaternion lr = Quaternion.Euler(rp.vector3Value);
            Matrix4x4 m = _root.localToWorldMatrix * Matrix4x4.TRS(cp.vector3Value, lr, Vector3.one);
            bool sel = _tab == 3 && _hbSel == i;
            var col = sel ? new Color(0.2f, 1f, 0.4f, 1f) : new Color(0.2f, 0.9f, 0.4f, 0.55f);

            using (new Handles.DrawingScope(col, m))
                DrawShape(shape, sp.vector3Value);

            if (!sel) continue;
            Vector3 wc = _root.TransformPoint(cp.vector3Value);
            Quaternion wr = _root.rotation * lr;
            switch (_hbTool)
            {
                case 0:
                    EditorGUI.BeginChangeCheck();
                    var np = Handles.PositionHandle(wc, wr);
                    if (EditorGUI.EndChangeCheck()) cp.vector3Value = Round(_root.InverseTransformPoint(np));
                    break;
                case 1:
                    EditorGUI.BeginChangeCheck();
                    var nr = Handles.RotationHandle(wr, wc);
                    if (EditorGUI.EndChangeCheck()) rp.vector3Value = Round((Quaternion.Inverse(_root.rotation) * nr).eulerAngles);
                    break;
                case 2:
                    using (new Handles.DrawingScope(Color.yellow, m))
                        SizeHandle(shape, cp, sp, lr);
                    break;
            }
        }
    }

    void SizeHandle(AlterHitboxShape shape, SerializedProperty cp, SerializedProperty sp, Quaternion lr)
    {
        Vector3 size = sp.vector3Value;
        Vector3 shift = Vector3.zero;
        EditorGUI.BeginChangeCheck();
        switch (shape)
        {
            case AlterHitboxShape.Box:
                _boxH.center = Vector3.zero;
                _boxH.size = size;
                _boxH.DrawHandle();
                if (EditorGUI.EndChangeCheck()) { size = _boxH.size; shift = _boxH.center; break; }
                return;
            case AlterHitboxShape.Sphere:
                _sphH.center = Vector3.zero;
                _sphH.radius = size.x * 0.5f;
                _sphH.DrawHandle();
                if (EditorGUI.EndChangeCheck()) { size.x = size.y = size.z = _sphH.radius * 2f; shift = _sphH.center; break; }
                return;
            default:
                _capH.center = Vector3.zero;
                _capH.radius = size.x * 0.5f;
                _capH.height = Mathf.Max(size.z, size.x);
                _capH.DrawHandle();
                if (EditorGUI.EndChangeCheck()) { size.x = size.y = _capH.radius * 2f; size.z = _capH.height; shift = _capH.center; break; }
                return;
        }
        sp.vector3Value = Round(size);
        cp.vector3Value = Round(cp.vector3Value + lr * shift);
    }

    /// <summary>현재 Handles.matrix 안에서 (원점 중심)</summary>
    static void DrawShape(AlterHitboxShape shape, Vector3 size)
    {
        switch (shape)
        {
            case AlterHitboxShape.Box:
                Handles.DrawWireCube(Vector3.zero, size);
                break;
            case AlterHitboxShape.Sphere:
                {
                    float r = size.x * 0.5f;
                    Handles.DrawWireDisc(Vector3.zero, Vector3.up, r);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.right, r);
                    Handles.DrawWireDisc(Vector3.zero, Vector3.forward, r);
                    break;
                }
            default:
                DrawCapsuleZ(size.x * 0.5f, Mathf.Max(size.z, size.x));
                break;
        }
    }

    static void DrawCapsuleZ(float r, float height)
    {
        float half = Mathf.Max(0f, height * 0.5f - r);
        Vector3 a = new(0f, 0f, half), b = new(0f, 0f, -half);
        Handles.DrawWireDisc(a, Vector3.forward, r);
        Handles.DrawWireDisc(b, Vector3.forward, r);
        Handles.DrawLine(a + Vector3.right * r, b + Vector3.right * r);
        Handles.DrawLine(a - Vector3.right * r, b - Vector3.right * r);
        Handles.DrawLine(a + Vector3.up * r, b + Vector3.up * r);
        Handles.DrawLine(a - Vector3.up * r, b - Vector3.up * r);
        HalfCircle(a, Vector3.right, Vector3.forward, r);
        HalfCircle(a, Vector3.up, Vector3.forward, r);
        HalfCircle(b, Vector3.right, Vector3.back, r);
        HalfCircle(b, Vector3.up, Vector3.back, r);
    }

    /// <summary>c에서 side 방향 두 끝을 잇는 반원 (dir 쪽으로 볼록)</summary>
    static void HalfCircle(Vector3 c, Vector3 side, Vector3 dir, float r)
    {
        const int N = 12;
        var pts = new Vector3[N + 1];
        for (int i = 0; i <= N; i++)
        {
            float a = Mathf.PI * i / N;
            pts[i] = c + (Mathf.Cos(a) * side + Mathf.Sin(a) * dir) * r;
        }
        Handles.DrawPolyLine(pts);
    }

    // ── 기본 공격 ──

    void SceneAttack()
    {
        if (!AttackOn) return;
        var step = Prop(_alterSO, "Attack");
        if (step == null) return;

        string anim = AttackAnimName;
        var cur = Cur;
        bool viewing = cur != null && cur.Kind == 3 && cur.Name == anim;
        bool active = false, waveNow = false;
        var tr = TrackByName(anim);
        if (viewing && AttackMap(cur, out _, out float c0, out float c1, out _, out _))
        {
            active = _time >= c0 && _time <= c1;
            waveNow = Mathf.Abs(_time - c0) <= 0.06f;
        }
        else if (tr == null) active = true;          // 동작이 없으면 항상 보여 줌
        if (_onlyTiming && !active) return;

        bool own = step.FindPropertyRelative("OverrideHitShape").boolValue;
        var modeP = step.FindPropertyRelative("HitMode");
        string mode = modeP != null && modeP.enumValueIndex >= 0 && modeP.enumValueIndex < modeP.enumNames.Length ? modeP.enumNames[modeP.enumValueIndex] : "ForwardArc";
        var rangeP = step.FindPropertyRelative("Range");
        var angleP = step.FindPropertyRelative("HitAngle");
        var heightP = step.FindPropertyRelative("HitHeight");
        var minP = step.FindPropertyRelative("MinRange");

        Vector3 o = _root.position + Vector3.up * (_eyeHeight - 0.7f);
        Vector3 f = Flat;
        Color c = active ? new Color(1f, 0.3f, 0.15f, 1f) : new Color(0.75f, 0.45f, 0.35f, 0.55f);
        if (!own) c.a *= 0.5f;

        if (mode == "ForwardSphere")
        {
            Handles.color = new Color(c.r, c.g, c.b, active ? 0.12f : 0.04f);
            Handles.SphereHandleCap(0, o, Quaternion.identity, rangeP.floatValue * 2f, EventType.Repaint);
            Handles.color = c;
            Handles.DrawWireDisc(o, Vector3.up, rangeP.floatValue);
        }
        else FlatCone(o, f, rangeP.floatValue, angleP.floatValue, heightP.floatValue, o.y, c, active, true);

        if (minP != null && minP.floatValue > 0.01f)
        {
            Handles.color = new Color(0.3f, 0.6f, 1f, 0.7f);
            Handles.DrawWireDisc(o, Vector3.up, minP.floatValue);
        }
        Handles.color = Color.yellow;
        Handles.Label(o + f * rangeP.floatValue + Vector3.up * 0.15f, $"기본 공격 {rangeP.floatValue:F1}m{(own ? "" : " (무기 범위 사용 중)")}", EditorStyles.whiteMiniLabel);

        if (_tab == 1)
        {
            RangeHandle(o, f, rangeP);
            if (mode != "ForwardSphere") AngleHandle(o, f, Vector3.up, rangeP.floatValue, angleP);
        }

        // 공격 충격파
        var wave = Prop(_alterSO, "AttackWave");
        if (wave != null && wave.FindPropertyRelative("Enabled") is { } en && en.boolValue)
        {
            var wr = wave.FindPropertyRelative("Radius");
            var wf = wave.FindPropertyRelative("Forward");
            Vector3 wc = _root.position + f * (wf != null ? wf.floatValue : 0f);
            Handles.color = waveNow ? new Color(0.4f, 0.8f, 1f, 1f) : new Color(0.4f, 0.7f, 1f, 0.5f);
            Handles.DrawWireDisc(wc, Vector3.up, wr.floatValue);
            Handles.Label(wc + Vector3.up * 0.1f, "공격 충격파", EditorStyles.whiteMiniLabel);
            if (_tab == 1)
            {
                RadiusHandle(wc, _root.right, wr);
                if (wf != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var np = Handles.Slider(wc, f, HandleUtility.GetHandleSize(wc) * 0.5f, Handles.ArrowHandleCap, 0.05f);
                    if (EditorGUI.EndChangeCheck()) wf.floatValue = Mathf.Round(Vector3.Dot(np - _root.position, f) * 100f) / 100f;
                }
            }
        }
    }

    // ── 능력 범위 ──

    void SceneAbility()
    {
        for (int i = 0; i < _gizmos.Length; i++)
        {
            var g = _gizmos[i];
            bool now = GizmoTiming(g, out _);
            if (_onlyTiming && !now) continue;
            bool on = GizmoEnabled(g);
            bool edit = _tab == 2 && _gizmoSel == i;

            Color c = GizmoColor(g);
            if (!now) c.a = 0.4f;
            if (!on) c.a *= 0.4f;

            Vector3 o = GizmoOrigin(g, out bool fromBone);
            Vector3 dir = g.Pitch ? Aim : Flat;

            switch (g.Shape)
            {
                case AlterGizmoShape.Cone:
                    {
                        var rp = Prop(_abilSO, g.Range);
                        var ap = Prop(_abilSO, g.Angle);
                        var hp = Prop(_abilSO, g.Height);
                        float range = Num(rp, 3f), angle = Num(ap, 60f);
                        if (g.Pitch) Cone3D(o, dir, range, angle, c, now);
                        else FlatCone(o, dir, range, angle, hp != null ? Num(hp) : 999f, _root.position.y, c, now, false);
                        Handles.color = c;
                        Handles.Label(o + dir * range + Vector3.up * 0.2f, $"{g.Label} {range:F1}m · {angle:F0}°", EditorStyles.whiteMiniLabel);
                        if (edit)
                        {
                            if (rp != null) RangeHandle(o, dir, rp);
                            if (ap != null) AngleHandle(o, dir, Vector3.Cross(dir, _root.right).normalized, range, ap);
                        }
                        break;
                    }
                case AlterGizmoShape.Sphere:
                case AlterGizmoShape.Circle:
                    {
                        var rp = Prop(_abilSO, g.Radius);
                        float r = Num(rp, 1f);
                        Handles.color = c;
                        Handles.DrawWireDisc(o, Vector3.up, r);
                        if (g.Shape == AlterGizmoShape.Sphere)
                        {
                            Handles.DrawWireDisc(o, _root.right, r);
                            Handles.DrawWireDisc(o, dir, r);
                            if (now)
                            {
                                Handles.color = new Color(c.r, c.g, c.b, 0.08f);
                                Handles.SphereHandleCap(0, o, Quaternion.identity, r * 2f, EventType.Repaint);
                            }
                        }
                        else if (now)
                        {
                            Handles.color = new Color(c.r, c.g, c.b, 0.1f);
                            Handles.DrawSolidDisc(o, Vector3.up, r);
                        }
                        Handles.color = c;
                        Handles.Label(o + _root.right * r + Vector3.up * 0.1f, $"{g.Label} {r:F1}m", EditorStyles.whiteMiniLabel);
                        if (edit && rp != null) RadiusHandle(o, _root.right, rp);
                        break;
                    }
            }

            // 위치 (뼈가 없을 때 Offset)
            if (edit && g.Origin == AlterGizmoOrigin.Bone && !fromBone)
            {
                var off = Prop(_abilSO, g.Offset);
                if (off != null && off.propertyType == SerializedPropertyType.Vector3)
                {
                    EditorGUI.BeginChangeCheck();
                    var np = Handles.PositionHandle(o, _root.rotation);
                    if (EditorGUI.EndChangeCheck()) off.vector3Value = Round(_root.InverseTransformPoint(np));
                }
            }
            if (fromBone)
            {
                Handles.color = c;
                Handles.SphereHandleCap(0, o, Quaternion.identity, HandleUtility.GetHandleSize(o) * 0.12f, EventType.Repaint);
            }
        }
    }

    // ── 그리기 · 손잡이 도우미 ──

    /// <summary>수평 부채꼴 (+ 위아래 허용 높이 — 기준 baseY ± height)</summary>
    void FlatCone(Vector3 o, Vector3 f, float range, float angle, float height, float baseY, Color c, bool active, bool aroundOrigin)
    {
        f.y = 0f;
        f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        angle = Mathf.Clamp(angle, 1f, 360f);
        float half = angle * 0.5f;
        Vector3 from = Quaternion.Euler(0f, -half, 0f) * f;

        Handles.color = new Color(c.r, c.g, c.b, active ? 0.18f : 0.05f);
        Handles.DrawSolidArc(o, Vector3.up, from, angle, range);
        Handles.color = c;
        Handles.DrawWireArc(o, Vector3.up, from, angle, range);
        if (angle < 359f)
        {
            Handles.DrawLine(o, o + from * range, active ? 3f : 1.5f);
            Handles.DrawLine(o, o + Quaternion.Euler(0f, half, 0f) * f * range, active ? 3f : 1.5f);
        }
        if (height < 50f)
        {
            float center = aroundOrigin ? o.y : baseY;
            Handles.color = new Color(c.r, c.g, c.b, c.a * 0.5f);
            foreach (float y in new[] { center + height, center - height })
            {
                var oy = new Vector3(o.x, y, o.z);
                Handles.DrawWireArc(oy, Vector3.up, from, angle, range);
                Handles.DrawLine(o + from * range, oy + from * range);
            }
        }
    }

    /// <summary>3D 원뿔 (브레스 · 시선 방향)</summary>
    void Cone3D(Vector3 o, Vector3 dir, float range, float angle, Color c, bool active)
    {
        dir = dir.normalized;
        float half = Mathf.Clamp(angle * 0.5f, 0.5f, 180f);
        Vector3 side = _root.right;
        Vector3 up = Vector3.Cross(dir, side).normalized;
        if (up.sqrMagnitude < 0.01f) up = Vector3.up;

        Handles.color = new Color(c.r, c.g, c.b, active ? 0.16f : 0.04f);
        Handles.DrawSolidArc(o, up, Quaternion.AngleAxis(-half, up) * dir, half * 2f, range);
        Handles.DrawSolidArc(o, side, Quaternion.AngleAxis(-half, side) * dir, half * 2f, range);

        Handles.color = c;
        Handles.DrawWireArc(o, up, Quaternion.AngleAxis(-half, up) * dir, half * 2f, range);
        Handles.DrawWireArc(o, side, Quaternion.AngleAxis(-half, side) * dir, half * 2f, range);
        if (half < 89f)
        {
            Vector3 endC = o + dir * (range * Mathf.Cos(half * Mathf.Deg2Rad));
            float endR = range * Mathf.Sin(half * Mathf.Deg2Rad);
            Handles.DrawWireDisc(endC, dir, endR);
            foreach (var ax in new[] { up, -up, side, -side })
                Handles.DrawLine(o, o + Quaternion.AngleAxis(half, Vector3.Cross(dir, ax)) * dir * range, active ? 2.5f : 1f);
        }
    }

    static void RangeHandle(Vector3 o, Vector3 dir, SerializedProperty rp)
    {
        if (rp == null) return;
        Vector3 p = o + dir.normalized * Num(rp);
        EditorGUI.BeginChangeCheck();
        Handles.color = Color.white;
        var np = Handles.FreeMoveHandle(p, HandleUtility.GetHandleSize(p) * 0.09f, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck()) SetNum(rp, Mathf.Max(0.1f, Mathf.Round(Vector3.Dot(np - o, dir.normalized) * 20f) / 20f));
    }

    /// <summary>axis 둘레로 벌어지는 각도</summary>
    static void AngleHandle(Vector3 o, Vector3 dir, Vector3 axis, float range, SerializedProperty ap)
    {
        if (ap == null) return;
        float half = Num(ap) * 0.5f;
        Vector3 d = Quaternion.AngleAxis(half, axis) * dir.normalized;
        Vector3 p = o + d * range * 0.75f;
        EditorGUI.BeginChangeCheck();
        Handles.color = new Color(1f, 0.9f, 0.3f);
        var np = Handles.FreeMoveHandle(p, HandleUtility.GetHandleSize(p) * 0.08f, Vector3.zero, Handles.CubeHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 v = Vector3.ProjectOnPlane(np - o, axis);
            float a = Vector3.Angle(dir, v) * 2f;
            SetNum(ap, Mathf.Clamp(Mathf.Round(a), 1f, 360f));
        }
    }

    static void RadiusHandle(Vector3 c, Vector3 side, SerializedProperty rp)
    {
        if (rp == null) return;
        Vector3 p = c + side.normalized * Num(rp);
        EditorGUI.BeginChangeCheck();
        Handles.color = Color.white;
        var np = Handles.FreeMoveHandle(p, HandleUtility.GetHandleSize(p) * 0.09f, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 v = np - c; v.y = 0f;
            SetNum(rp, Mathf.Max(0.1f, Mathf.Round(v.magnitude * 20f) / 20f));
        }
    }

    // ══════════════════ 작은 도우미 ══════════════════

    static SerializedProperty Prop(SerializedObject so, string path) =>
        so == null || string.IsNullOrEmpty(path) ? null : so.FindProperty(path);

    static float Num(SerializedProperty p, float def = 0f)
    {
        if (p == null) return def;
        return p.propertyType switch
        {
            SerializedPropertyType.Float => p.floatValue,
            SerializedPropertyType.Integer => p.intValue,
            _ => def,
        };
    }

    static void SetNum(SerializedProperty p, float v)
    {
        if (p == null) return;
        if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(v);
        else if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
    }

    static void Field(SerializedObject so, string path, string label, bool children = false)
    {
        var p = Prop(so, path);
        if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip), children);
    }

    static readonly HashSet<string> _open = new();
    static bool Fold(string key, string label)
    {
        bool o = _open.Contains(key);
        bool n = EditorGUILayout.Foldout(o, label, true);
        if (n != o) { if (n) _open.Add(key); else _open.Remove(key); }
        return n;
    }
}