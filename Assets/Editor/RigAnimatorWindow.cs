#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools → Rig Animator (리그 · 변화)
///   [변화 배치]  AlterData 파츠(말 등)를 씬의 팔 리그에 붙여 보며 마우스로 위치·회전·크기·탄 높이 조절
///   [본 정리]    본 이름 바꾸기 · 접두사 지우기 · 부모 바꾸기 (씬에서 클릭) — 스키닝은 그대로 유지됨
///   [IK]         자동 IK (다리 · 꼬리 · 손가락) · 직접 만들기 — 씬의 주황 상자를 끌면 본들이 따라옴
///   [애니메이션] 상태(Idle · Walk · Grab …)별 키 애니메이션 → RigAnimData 에셋 (RigAnimator가 재생)
/// 씬 뷰: 본이 블렌더처럼 그려짐. 본 클릭 = 선택, W 이동 · E 회전 · R 끝 끌기, K 키 저장
/// </summary>
public class RigAnimatorWindow : EditorWindow
{
    enum Tab { Alter, Bones, IK, Anim }
    enum BoneTool { Move, Rotate, Aim }
    enum PartTool { Move, Rotate, Scale }

    static readonly string[] TabNames = { "변화 배치", "본 정리", "IK", "애니메이션" };
    static readonly string[] BoneToolNames = { "이동 (W)", "회전 (E)", "끝 끌기 (R)" };
    static readonly string[] PartToolNames = { "이동", "회전", "크기" };
    static readonly string[] EaseNames = { "부드럽게 (휙 이어짐)", "멈췄다 출발", "일정한 속도", "그대로 유지 (뚝)" };

    static readonly (string name, float len, bool loop)[] StatePresets =
    {
        ("Idle", 1.2f, true), ("Walk", 0.8f, true), ("Appear", 0.5f, false), ("Grab", 0.4f, false),
        ("Charge", 0.5f, true), ("Leap", 0.6f, false), ("Attack", 0.5f, false),
    };

    // ── 색 ──
    static readonly Color CBone = new(0.85f, 0.85f, 0.9f, 0.9f);
    static readonly Color CBoneSel = new(1f, 0.85f, 0.2f, 1f);
    static readonly Color CBoneChain = new(0.4f, 0.8f, 1f, 0.95f);
    static readonly Color CBoneChainSel = new(1f, 0.55f, 0.15f, 1f);
    static readonly Color CTarget = new(1f, 0.55f, 0.1f, 1f);
    static readonly Color CBg = new(0.13f, 0.13f, 0.14f);
    static readonly Color CTick = new(1f, 1f, 1f, 0.18f);

    [SerializeField] private Tab _tab = Tab.Anim;
    private Vector2 _scroll;

    // ── 모델 ──
    [SerializeField] private GameObject _model;
    [SerializeField] private RigAnimData _data;
    private readonly List<Transform> _bones = new();
    private readonly Dictionary<Transform, Transform> _parentBone = new();
    private readonly Dictionary<Transform, List<Transform>> _childBones = new();
    private readonly Dictionary<Transform, int> _depth = new();
    private readonly Dictionary<string, Transform> _byName = new();
    private Transform _sel;
    private BoneTool _tool = BoneTool.Rotate;
    private bool _sceneEdit = true;
    private bool _xray = true;
    private float _boneScale = 1f;
    private bool _pickParent;
    private bool _toolsHidden;

    // ── IK ──
    private readonly Dictionary<RigIKChain, Vector3> _ikTarget = new();
    private RigIKChain _selChain;
    private int _autoLen = 4;
    private int _manualLen = 3;

    // ── 애니메이션 ──
    private int _stateIdx;
    private int _keyIdx = -1;
    private float _time;
    private bool _playing;
    private bool _autoKey = true;
    private double _lastTick;
    private RigPose _clipboard;
    private bool _dragKey, _scrub;
    private readonly RigPose _tmp = new();

    // ── 변화 배치 ──
    [SerializeField] private FPArmRig _rig;
    [SerializeField] private AlterData _alter;
    private GameObject _part;
    private AlterData _partFor;
    private Transform _partParent;
    private PartTool _partTool = PartTool.Move;
    private int _partStateIdx;
    private bool _partPlay;
    private float _partT;

    private GUIStyle _sceneLabel, _diamond, _hint, _rowSel;

    // ══════════════════ 열기 ══════════════════

    [MenuItem("Tools/Rig Animator (리그 · 변화)")]
    public static void Open()
    {
        var w = GetWindow<RigAnimatorWindow>("Rig Animator");
        w.minSize = new Vector2(380, 520);
    }

    public static void OpenAlter(AlterData a)
    {
        Open();
        var w = GetWindow<RigAnimatorWindow>();
        w._alter = a;
        w._tab = Tab.Alter;
        w.Repaint();
    }

    [MenuItem("CONTEXT/AlterData/모션 에디터에서 배치 (Rig Animator)")]
    static void FromAlterContext(MenuCommand cmd) => OpenAlter(cmd.context as AlterData);

    [MenuItem("CONTEXT/RigAnimator/Rig Animator에서 편집")]
    static void FromRigContext(MenuCommand cmd)
    {
        Open();
        var w = GetWindow<RigAnimatorWindow>();
        var ra = cmd.context as RigAnimator;
        if (ra == null) return;
        w._data = ra.Data;
        w.SetModel(ra.gameObject);
        w._tab = Tab.Anim;
    }

    void OnEnable()
    {
        SceneView.duringSceneGui += OnScene;
        EditorApplication.update += Tick;
        Undo.undoRedoPerformed += OnUndo;
        _lastTick = EditorApplication.timeSinceStartup;
        if (_model != null) CollectBones();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnScene;
        EditorApplication.update -= Tick;
        Undo.undoRedoPerformed -= OnUndo;
        ShowTools();
        ClearPart();
        if (_model != null && _data != null) ApplyPose(_data.Rest);   // 편집하던 자세가 프리팹에 저장되지 않게
    }

    void OnUndo()
    {
        CollectBones();
        _ikTarget.Clear();
        Repaint();
        SceneView.RepaintAll();
    }

    // ══════════════════ GUI ══════════════════

    void OnGUI()
    {
        EnsureStyles();

        int t = GUILayout.Toolbar((int)_tab, TabNames, GUILayout.Height(24));
        if (t != (int)_tab)
        {
            _tab = (Tab)t;
            _pickParent = false;
            if (_tab != Tab.Alter) StopPartPlay();
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_tab != Tab.Alter) DrawModelHeader();

        switch (_tab)
        {
            case Tab.Alter: DrawAlterTab(); break;
            case Tab.Bones: DrawBonesTab(); break;
            case Tab.IK: DrawIKTab(); break;
            case Tab.Anim: DrawAnimTab(); break;
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawModelHeader()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var m = (GameObject)EditorGUILayout.ObjectField("모델 (씬)", _model, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) SetModel(m);
                if (GUILayout.Button("선택한 것", EditorStyles.miniButton, GUILayout.Width(60)))
                    SetModel(Selection.activeGameObject);
            }

            EditorGUI.BeginChangeCheck();
            var d = (RigAnimData)EditorGUILayout.ObjectField("애니메이션 데이터", _data, typeof(RigAnimData), false);
            if (EditorGUI.EndChangeCheck()) { _data = d; _ikTarget.Clear(); SyncData(); }

            if (_model == null)
            {
                EditorGUILayout.LabelField("씬이나 프리팹 모드(프리팹 더블클릭)에 있는 모델을 넣으세요.", _hint);
                return;
            }

            if (EditorUtility.IsPersistent(_model))
            {
                EditorGUILayout.HelpBox("프로젝트 창의 프리팹 에셋입니다. 씬에 놓거나 프리팹 모드(더블클릭)에서 연 오브젝트를 넣어주세요.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _sceneEdit = GUILayout.Toggle(_sceneEdit, "씬에서 본 편집", EditorStyles.miniButtonLeft);
                _xray = GUILayout.Toggle(_xray, "본 앞으로 (X-ray)", EditorStyles.miniButtonRight);
                GUILayout.Space(8);
                GUILayout.Label("본 굵기", EditorStyles.miniLabel, GUILayout.Width(40));
                _boneScale = GUILayout.HorizontalSlider(_boneScale, 0.3f, 3f, GUILayout.MinWidth(60));
            }

            if (_sceneEdit)
            {
                int tool = GUILayout.Toolbar((int)_tool, BoneToolNames, EditorStyles.miniButton);
                if (tool != (int)_tool) { _tool = (BoneTool)tool; SceneView.RepaintAll(); }
            }

            EditorGUILayout.LabelField(
                $"본 {_bones.Count}개 · 선택: {(_sel != null ? _sel.name : "없음")}" +
                (_model.hideFlags.HasFlag(HideFlags.DontSave) ? "   (미리보기 복사본 — 본 정리는 저장 안 됨)" : ""),
                EditorStyles.miniLabel);
        }
    }

    // ══════════════════ 모델 · 본 ══════════════════

    void SetModel(GameObject go)
    {
        if (_model != null && _model != go && _data != null) ApplyPose(_data.Rest);
        _model = go;
        _sel = null;
        _selChain = null;
        _pickParent = false;
        _ikTarget.Clear();

        if (_model != null)
        {
            var ra = _model.GetComponent<RigAnimator>();
            if (ra != null && ra.Data != null) _data = ra.Data;
        }
        CollectBones();
        SyncData();
        Repaint();
        SceneView.RepaintAll();
    }

    void CollectBones()
    {
        _bones.Clear();
        _parentBone.Clear();
        _childBones.Clear();
        _depth.Clear();
        _byName.Clear();
        if (_model == null) return;

        var root = _model.transform;
        var all = _model.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
            if (t != root && !_byName.ContainsKey(t.name)) _byName[t.name] = t;

        var set = new HashSet<Transform>();
        foreach (var smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.bones != null)
                foreach (var b in smr.bones)
                    if (b != null && b != root && b.IsChildOf(root)) set.Add(b);
            if (smr.rootBone != null && smr.rootBone != root && smr.rootBone.IsChildOf(root)) set.Add(smr.rootBone);
        }

        // 데이터에 이미 있는 본 (스키닝 안 된 보조 본 포함)
        if (_data != null)
            foreach (var t in all)
                if (t != root && _data.Bones.Contains(t.name)) set.Add(t);

        // 리깅 안 된 모델 (조각 메쉬로 된 손 등) — 렌더러 없는 오브젝트를 본으로
        if (set.Count == 0)
            foreach (var t in all)
                if (t != root && t.GetComponent<Renderer>() == null) set.Add(t);

        foreach (var t in all) if (set.Contains(t)) _bones.Add(t);

        foreach (var b in _bones)
        {
            Transform p = b.parent;
            while (p != null && !set.Contains(p))
            {
                if (p == root) { p = null; break; }
                p = p.parent;
            }
            _parentBone[b] = p;
            if (p != null)
            {
                if (!_childBones.TryGetValue(p, out var list)) _childBones[p] = list = new List<Transform>();
                list.Add(b);
            }
        }

        foreach (var b in _bones)
        {
            int d = 0;
            var p = _parentBone[b];
            while (p != null && d < 64) { d++; p = _parentBone.TryGetValue(p, out var q) ? q : null; }
            _depth[b] = d;
        }

        if (_sel != null && !set.Contains(_sel)) _sel = null;
    }

    Transform ParentBone(Transform b) => b != null && _parentBone.TryGetValue(b, out var p) ? p : null;
    bool IsLeaf(Transform b) => !_childBones.TryGetValue(b, out var k) || k.Count == 0;

    Transform Bone(string name) => !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var t) && t != null ? t : null;

    /// <summary>모델에 생긴 새 본을 데이터에 추가 (지금 자세를 기본값으로)</summary>
    void SyncData()
    {
        if (_data == null || _model == null) return;
        bool changed = false;
        foreach (var b in _bones)
        {
            if (_data.Bones.Contains(b.name)) continue;
            if (!changed) Undo.RecordObject(_data, "본 추가");
            _data.EnsureBone(b.name, b.localRotation, b.localPosition);
            changed = true;
        }
        if (_data.Rest.Count != _data.Bones.Count) { _data.Rest.Resize(_data.Bones.Count, null); changed = true; }
        if (changed) EditorUtility.SetDirty(_data);
    }

    void Capture(RigPose p)
    {
        if (_data == null) return;
        p.Resize(_data.Bones.Count, _data.Rest);
        for (int i = 0; i < _data.Bones.Count; i++)
        {
            var t = Bone(_data.Bones[i]);
            if (t == null) continue;
            p.Rot[i] = t.localRotation;
            p.Pos[i] = t.localPosition;
        }
    }

    void ApplyPose(RigPose p)
    {
        if (_data == null || p == null || _model == null) return;
        int n = Mathf.Min(_data.Bones.Count, p.Count);
        for (int i = 0; i < n; i++)
        {
            var t = Bone(_data.Bones[i]);
            if (t != null) t.SetLocalPositionAndRotation(p.Pos[i], p.Rot[i]);
        }
        SceneView.RepaintAll();
    }

    (Vector3 head, Vector3 tail) Seg(Transform b)
    {
        Vector3 h = b.position;
        if (_childBones.TryGetValue(b, out var kids) && kids.Count > 0)
        {
            Vector3 tail = kids[0].position;
            if (kids.Count > 1)
            {
                Vector3 sum = Vector3.zero;
                foreach (var k in kids) sum += k.position;
                tail = sum / kids.Count;
            }
            if ((tail - h).sqrMagnitude > 1e-8f) return (h, tail);
            foreach (var k in kids) if ((k.position - h).sqrMagnitude > 1e-8f) return (h, k.position);
        }

        var p = ParentBone(b);
        Vector3 dir = p != null && (h - p.position).sqrMagnitude > 1e-8f ? (h - p.position).normalized : b.up;
        float len = p != null ? Mathf.Max(Vector3.Distance(p.position, h) * 0.5f, 0.01f) : DefaultLen();
        return (h, h + dir * len);
    }

    float DefaultLen()
    {
        if (_model == null) return 0.1f;
        var rs = _model.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 0.1f;
        var bb = rs[0].bounds;
        foreach (var r in rs) bb.Encapsulate(r.bounds);
        return Mathf.Max(0.02f, bb.size.magnitude * 0.05f);
    }

    // ══════════════════ 씬 ══════════════════

    void OnScene(SceneView sv)
    {
        if (_tab == Tab.Alter) { SceneAlter(); return; }

        if (_model == null || !_sceneEdit || EditorUtility.IsPersistent(_model)) { ShowTools(); return; }
        if (_bones.Count == 0 || _bones[0] == null) CollectBones();

        var e = Event.current;
        HandleKeys(e);

        var prevZ = Handles.zTest;
        Handles.zTest = _xray ? CompareFunction.Always : CompareFunction.LessEqual;

        // 본 클릭 (본 몸통 어디든)
        int n = _bones.Count;
        var ids = new int[n];
        for (int i = 0; i < n; i++) ids[i] = GUIUtility.GetControlID(FocusType.Passive);

        if (e.type == EventType.Layout)
            for (int i = 0; i < n; i++)
            {
                if (_bones[i] == null) continue;
                var (h, t) = Seg(_bones[i]);
                HandleUtility.AddControl(ids[i], HandleUtility.DistanceToLine(h, t));
            }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            for (int i = 0; i < n; i++)
                if (HandleUtility.nearestControl == ids[i])
                {
                    OnBoneClicked(_bones[i]);
                    e.Use();
                    break;
                }

        if (e.type == EventType.Repaint) DrawBones();

        if (_sel != null && !_pickParent) DrawBoneTool();
        if (_data != null && (_tab == Tab.IK || _tab == Tab.Anim)) DrawIKTargets();

        if (_pickParent && _sel != null)
            Handles.Label(_sel.position, $"  새 부모가 될 본을 클릭 (Esc 취소)\n  옮길 본: {_sel.name}", _sceneLabel);

        // 빈 곳 클릭 시 다른 오브젝트가 선택되지 않게
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        Handles.zTest = prevZ;

        if (_sel != null || _selChain != null) HideTools(); else ShowTools();
    }

    void HandleKeys(Event e)
    {
        if (e.type != EventType.KeyDown) return;
        switch (e.keyCode)
        {
            case KeyCode.W when _sel != null: _tool = BoneTool.Move; e.Use(); Repaint(); break;
            case KeyCode.E when _sel != null: _tool = BoneTool.Rotate; e.Use(); Repaint(); break;
            case KeyCode.R when _sel != null: _tool = BoneTool.Aim; e.Use(); Repaint(); break;
            case KeyCode.K when _tab == Tab.Anim && CurState != null: AddOrSetKey(); e.Use(); break;
            case KeyCode.Escape:
                if (_pickParent) _pickParent = false;
                else { _sel = null; _selChain = null; }
                e.Use();
                Repaint();
                break;
        }
    }

    void OnBoneClicked(Transform b)
    {
        if (_pickParent && _sel != null)
        {
            _pickParent = false;
            Reparent(_sel, b);
            return;
        }
        _sel = b;
        _selChain = null;
        Repaint();
    }

    void DrawBones()
    {
        var chainBones = new HashSet<string>();
        var selChainBones = new HashSet<string>();
        if (_data != null)
            foreach (var c in _data.Chains)
                if (c?.Bones != null)
                    foreach (var name in c.Bones) { chainBones.Add(name); if (c == _selChain) selChainBones.Add(name); }

        foreach (var b in _bones)
        {
            if (b == null) continue;
            var (h, t) = Seg(b);
            bool sel = b == _sel;
            Color c = sel ? CBoneSel
                    : selChainBones.Contains(b.name) ? CBoneChainSel
                    : chainBones.Contains(b.name) ? CBoneChain
                    : CBone;
            DrawOcta(h, t, c, sel ? 3f : 1.5f);

            Handles.color = c;
            float s = Mathf.Min(HandleUtility.GetHandleSize(h) * 0.04f, Vector3.Distance(h, t) * 0.18f) * _boneScale;
            Handles.SphereHandleCap(0, h, Quaternion.identity, s, EventType.Repaint);
        }
    }

    void DrawOcta(Vector3 h, Vector3 t, Color col, float thick)
    {
        Vector3 ax = t - h;
        float len = ax.magnitude;
        if (len < 1e-5f) return;
        ax /= len;

        Vector3 any = Mathf.Abs(Vector3.Dot(ax, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        Vector3 u = Vector3.Cross(ax, any).normalized;
        Vector3 v = Vector3.Cross(ax, u);
        float w = len * 0.1f * _boneScale;
        Vector3 m = h + ax * len * 0.2f;
        Vector3 a = m + u * w, b = m + v * w, c = m - u * w, d = m - v * w;

        Handles.color = col;
        Handles.DrawLine(h, a, thick); Handles.DrawLine(h, b, thick); Handles.DrawLine(h, c, thick); Handles.DrawLine(h, d, thick);
        Handles.DrawLine(a, t, thick); Handles.DrawLine(b, t, thick); Handles.DrawLine(c, t, thick); Handles.DrawLine(d, t, thick);
        Handles.DrawLine(a, b, thick); Handles.DrawLine(b, c, thick); Handles.DrawLine(c, d, thick); Handles.DrawLine(d, a, thick);
    }

    void DrawBoneTool()
    {
        var b = _sel;
        if (b == null) return;
        var (h, t) = Seg(b);
        float size = HandleUtility.GetHandleSize(h);

        switch (_tool)
        {
            case BoneTool.Rotate:
                {
                    EditorGUI.BeginChangeCheck();
                    var r = Handles.RotationHandle(b.rotation, h);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(b, "본 회전");
                        b.rotation = r;
                        OnPoseEdited();
                    }
                    break;
                }
            case BoneTool.Move:
                {
                    EditorGUI.BeginChangeCheck();
                    var p = Handles.PositionHandle(h, Tools.pivotRotation == PivotRotation.Local ? b.rotation : Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(b, "본 이동");
                        b.position = p;
                        OnPoseEdited();
                    }
                    break;
                }
            case BoneTool.Aim:
                {
                    Handles.color = CBoneSel;
                    EditorGUI.BeginChangeCheck();
                    var nt = Handles.FreeMoveHandle(t, size * 0.1f, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Vector3 from = t - h, to = nt - h;
                        if (from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f)
                        {
                            Undo.RecordObject(b, "본 끝 끌기");
                            b.rotation = Quaternion.FromToRotation(from, to) * b.rotation;
                            OnPoseEdited();
                        }
                    }
                    break;
                }
        }

        Handles.Label(h + Vector3.up * size * 0.15f, " " + b.name, _sceneLabel);
    }

    void DrawIKTargets()
    {
        foreach (var ch in _data.Chains)
        {
            var chain = ChainTransforms(ch);
            if (chain == null) continue;
            var tip = chain[chain.Length - 1];

            if (!_ikTarget.TryGetValue(ch, out var tp)) _ikTarget[ch] = tp = tip.position;
            bool sel = ch == _selChain;
            float s = HandleUtility.GetHandleSize(tp) * 0.08f;

            Handles.color = new Color(CTarget.r, CTarget.g, CTarget.b, sel ? 1f : 0.75f);
            Handles.DrawDottedLine(tip.position, tp, 3f);

            EditorGUI.BeginChangeCheck();
            Vector3 np;
            if (sel)
            {
                Handles.CubeHandleCap(0, tp, Quaternion.identity, s * 1.2f, EventType.Repaint);
                np = Handles.PositionHandle(tp, Quaternion.identity);
            }
            else np = Handles.FreeMoveHandle(tp, s, Vector3.zero, Handles.CubeHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                _ikTarget[ch] = np;
                _selChain = ch;
                _sel = null;
                Undo.RecordObjects(chain, "IK");
                RigIK.Solve(chain, np);
                OnPoseEdited();
            }

            if (sel || HandleUtility.DistanceToCircle(tp, s) < 30f)
                Handles.Label(tp, "  " + ch.Name, _sceneLabel);
        }
    }

    Transform[] ChainTransforms(RigIKChain ch)
    {
        if (ch?.Bones == null || ch.Bones.Length < 2) return null;
        var arr = new Transform[ch.Bones.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = Bone(ch.Bones[i]);
            if (arr[i] == null) return null;
        }
        return arr;
    }

    void ResetIKTargets() => _ikTarget.Clear();

    /// <summary>본을 손으로 움직였을 때 — 자동 키면 선택한 키에 바로 저장</summary>
    void OnPoseEdited()
    {
        if (_tab == Tab.Anim && _autoKey && _data != null)
        {
            var s = CurState;
            if (s != null && _keyIdx >= 0 && _keyIdx < s.Keys.Count && Mathf.Abs(s.Keys[_keyIdx].Time - _time) < 0.001f)
            {
                Undo.RecordObject(_data, "키 자동 저장");
                Capture(s.Keys[_keyIdx].Pose);
                EditorUtility.SetDirty(_data);
            }
        }
        Repaint();
    }

    void HideTools()
    {
        if (_toolsHidden) return;
        _toolsHidden = true;
        Tools.hidden = true;
    }

    void ShowTools()
    {
        if (!_toolsHidden) return;
        _toolsHidden = false;
        Tools.hidden = false;
    }

    // ══════════════════ 본 정리 탭 ══════════════════

    void DrawBonesTab()
    {
        if (!RequireModel()) return;

        Hint("본 이름은 애니메이션이 본을 찾는 열쇠입니다. 정리는 애니메이션을 만들기 전에 하는 게 편합니다.\n" +
             "부모를 바꿔도 메쉬 변형(스키닝)은 그대로입니다 — 본의 월드 위치를 유지한 채 옮겨짐.");

        // 프리팹 구조 잠김
        Transform locked = null;
        foreach (var b in _bones)
            if (b != null && PrefabUtility.IsPartOfPrefabInstance(b)) { locked = b; break; }
        if (locked != null)
        {
            EditorGUILayout.HelpBox(
                "모델 프리팹(FBX) 안의 본이라 부모를 바꿀 수 없습니다 (이름 바꾸기는 됨).\n" +
                "아래 버튼으로 연결을 풀면 자유롭게 바꿀 수 있습니다. 메쉬·재질은 그대로 FBX를 씁니다.\n" +
                "씬에 놓인 프리팹이면 프리팹 연결이 끊기니, 프리팹 모드(프리팹 더블클릭)에서 하는 걸 추천합니다.",
                MessageType.Info);
            if (GUILayout.Button("모델 프리팹 연결 풀기 (본 구조 수정 허용)"))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(locked);
                if (root != null)
                {
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);
                    CollectBones();
                }
            }
        }

        // 겹치는 이름
        var dup = _bones.Where(b => b != null).GroupBy(b => b.name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup.Count > 0)
            EditorGUILayout.HelpBox("이름이 겹치는 본: " + string.Join(", ", dup) + "\n애니메이션은 이름으로 찾으니 다르게 바꿔주세요.", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("tripo:: 같은 접두사 지우기")) StripPrefixes();
            if (GUILayout.Button("새로고침", GUILayout.Width(70))) { CollectBones(); SyncData(); }
        }

        // 선택한 본
        if (_sel != null)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("선택한 본", EditorStyles.boldLabel);
                string nn = EditorGUILayout.DelayedTextField("이름", _sel.name);
                if (nn != _sel.name) Rename(_sel, nn);

                var p = ParentBone(_sel);
                EditorGUILayout.LabelField("부모", p != null ? p.name : "(없음 — 맨 위)");

                using (new EditorGUILayout.HorizontalScope())
                {
                    _pickParent = GUILayout.Toggle(_pickParent, _pickParent ? "씬에서 새 부모 클릭…" : "부모 바꾸기 (씬에서 클릭)", "Button");
                    if (GUILayout.Button("목록에서 고르기 ▾", GUILayout.Width(120))) ParentMenu(_sel);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (p != null && GUILayout.Button("한 단계 위로")) Reparent(_sel, ParentBone(p) != null ? ParentBone(p) : p.parent);
                    if (GUILayout.Button("Hierarchy에서 보기")) EditorGUIUtility.PingObject(_sel.gameObject);
                }
            }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("본 목록 (클릭 = 선택 · 이름 칸에서 바로 바꾸기)", EditorStyles.boldLabel);

        foreach (var b in _bones.ToList())
        {
            if (b == null) continue;
            using (new EditorGUILayout.HorizontalScope(b == _sel ? _rowSel : GUIStyle.none))
            {
                GUILayout.Space(4 + _depth[b] * 12);
                string icon = IsLeaf(b) ? "·" : "▸";
                if (GUILayout.Button(icon, EditorStyles.miniLabel, GUILayout.Width(12))) { _sel = b; SceneView.RepaintAll(); }
                string nn = EditorGUILayout.DelayedTextField(b.name);
                if (nn != b.name) Rename(b, nn);
                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(36))) { _sel = b; _selChain = null; SceneView.RepaintAll(); }
            }
        }
    }

    void ParentMenu(Transform bone)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("(모델 바로 아래)"), false, () => Reparent(bone, _model.transform));
        menu.AddSeparator("");
        foreach (var b in _bones)
        {
            if (b == null || b == bone || b.IsChildOf(bone)) continue;
            string path = new string(' ', _depth[b] * 2) + b.name;
            var target = b;
            menu.AddItem(new GUIContent(path.Replace("/", "_")), ParentBone(bone) == b, () => Reparent(bone, target));
        }
        menu.ShowAsContext();
    }

    void Rename(Transform b, string newName)
    {
        newName = newName?.Trim();
        if (b == null || string.IsNullOrEmpty(newName) || newName == b.name) return;
        if (_byName.TryGetValue(newName, out var other) && other != null && other != b)
        {
            ShowNotification(new GUIContent($"'{newName}'은 이미 있는 이름"));
            return;
        }

        string old = b.name;
        Undo.RecordObject(b.gameObject, "본 이름 바꾸기");
        b.name = newName;

        if (_data != null)
        {
            Undo.RecordObject(_data, "본 이름 바꾸기");
            int i = _data.Bones.IndexOf(old);
            if (i >= 0) _data.Bones[i] = newName;
            foreach (var c in _data.Chains)
                if (c?.Bones != null)
                    for (int k = 0; k < c.Bones.Length; k++)
                        if (c.Bones[k] == old) c.Bones[k] = newName;
            EditorUtility.SetDirty(_data);
        }

        CollectBones();
        Repaint();
    }

    void StripPrefixes()
    {
        var rx = new Regex(@"^.*::");
        int n = 0;
        foreach (var b in _bones.ToList())
        {
            if (b == null || !rx.IsMatch(b.name)) continue;
            string nn = rx.Replace(b.name, "");
            if (string.IsNullOrEmpty(nn)) continue;
            string unique = nn;
            int k = 2;
            while (_byName.TryGetValue(unique, out var o) && o != null && o != b) unique = $"{nn}_{k++}";
            Rename(b, unique);
            n++;
        }
        ShowNotification(new GUIContent($"{n}개 이름 정리"));
    }

    /// <summary>부모 바꾸기 — 월드 자세 유지. 데이터가 있으면 모든 키의 그 본 값을 새 부모 기준으로 바꿔줌</summary>
    void Reparent(Transform bone, Transform newParent)
    {
        if (bone == null || newParent == null) return;
        if (newParent == bone || newParent.IsChildOf(bone))
        {
            ShowNotification(new GUIContent("자기 자식 아래로는 옮길 수 없음"));
            return;
        }
        if (PrefabUtility.IsPartOfPrefabInstance(bone))
        {
            ShowNotification(new GUIContent("프리팹 안의 본 — '본 정리' 탭에서 연결을 먼저 풀어주세요"));
            return;
        }

        if (_data != null) ConvertKeysForReparent(bone, newParent);

        Undo.SetTransformParent(bone, newParent, "본 부모 바꾸기");
        CollectBones();
        Repaint();
        SceneView.RepaintAll();
    }

    void ConvertKeysForReparent(Transform bone, Transform np)
    {
        int bi = _data.BoneIndex(bone.name);
        if (bi < 0) return;

        var keep = new RigPose();
        Capture(keep);
        Undo.RecordObject(_data, "본 부모 바꾸기");

        void Conv(RigPose p)
        {
            if (p == null || bi >= p.Count) return;
            ApplyPose(p);
            p.Rot[bi] = Quaternion.Inverse(np.rotation) * bone.rotation;
            p.Pos[bi] = np.InverseTransformPoint(bone.position);
        }

        Conv(_data.Rest);
        foreach (var s in _data.States)
            foreach (var k in s.Keys) Conv(k.Pose);

        ApplyPose(keep);
        EditorUtility.SetDirty(_data);
    }

    // ══════════════════ IK 탭 ══════════════════

    void DrawIKTab()
    {
        if (!RequireModelAndData()) return;

        Hint("IK = 끝(발굽 · 손끝)을 잡아끌면 그 위 본들이 알아서 굽는 것.\n" +
             "씬의 주황 상자를 끌어보세요. 클릭해 선택하면 화살표로 한 방향씩도 움직일 수 있습니다.");

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("자동으로 만들기", EditorStyles.boldLabel);
            _autoLen = EditorGUILayout.IntSlider(new GUIContent("본 개수 (끝 포함)", "다리는 4, 손가락은 3 정도"), _autoLen, 2, 6);
            if (GUILayout.Button("자동 IK 만들기 — 다리 · 꼬리 · 손가락 (끝이 갈라지지 않는 줄기마다)", GUILayout.Height(24)))
                AutoIK(_autoLen);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("직접 만들기", EditorStyles.boldLabel);
            if (_sel == null) EditorGUILayout.LabelField("씬에서 끝이 될 본(발굽 · 손끝)을 클릭하세요.", _hint);
            else
            {
                _manualLen = EditorGUILayout.IntSlider("위로 몇 개까지 (끝 포함)", _manualLen, 2, 8);
                var preview = ChainUp(_sel, _manualLen);
                EditorGUILayout.LabelField(string.Join(" → ", preview.Select(t => t.name)), EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button($"'{_sel.name}'을 끝으로 IK 만들기")) AddChain(preview);
            }
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"IK {_data.Chains.Count}개", EditorStyles.boldLabel);
            if (GUILayout.Button("목표를 모두 끝 위치로", EditorStyles.miniButton, GUILayout.Width(130))) { ResetIKTargets(); SceneView.RepaintAll(); }
        }

        for (int i = 0; i < _data.Chains.Count; i++)
        {
            var ch = _data.Chains[i];
            if (ch == null) continue;
            bool ok = ChainTransforms(ch) != null;

            using (new EditorGUILayout.VerticalScope(ch == _selChain ? _rowSel : EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string nn = EditorGUILayout.DelayedTextField(ch.Name);
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_data, "IK 이름"); ch.Name = nn; EditorUtility.SetDirty(_data); }

                    if (GUILayout.Button("선택", EditorStyles.miniButtonLeft, GUILayout.Width(40))) { _selChain = ch; _sel = null; SceneView.RepaintAll(); }
                    if (GUILayout.Button("목표 리셋", EditorStyles.miniButtonMid, GUILayout.Width(60))) { _ikTarget.Remove(ch); SceneView.RepaintAll(); }
                    if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    {
                        Undo.RecordObject(_data, "IK 삭제");
                        _data.Chains.RemoveAt(i);
                        EditorUtility.SetDirty(_data);
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.LabelField((ok ? "" : "⚠ 본을 못 찾음 — ") + string.Join(" → ", ch.Bones ?? new string[0]),
                                           EditorStyles.wordWrappedMiniLabel);

                EditorGUI.BeginChangeCheck();
                bool follow = EditorGUILayout.ToggleLeft(
                    new GUIContent("게임 중 목표로 뻗기", "RigAnimator.AimAt — 그림자 손이 붙잡은 적 쪽으로 뻗을 때 등"), ch.FollowRuntimeTarget);
                float w = ch.RuntimeWeight;
                if (follow) w = EditorGUILayout.Slider("  뻗는 정도", w, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "IK 설정");
                    ch.FollowRuntimeTarget = follow;
                    ch.RuntimeWeight = w;
                    EditorUtility.SetDirty(_data);
                }
            }
        }
    }

    List<Transform> ChainUp(Transform tip, int len)
    {
        var list = new List<Transform> { tip };
        var p = ParentBone(tip);
        while (p != null && list.Count < len) { list.Insert(0, p); p = ParentBone(p); }
        return list;
    }

    void AutoIK(int len)
    {
        Undo.RecordObject(_data, "자동 IK");
        int added = 0;
        foreach (var b in _bones)
        {
            if (b == null || !IsLeaf(b)) continue;
            if (_data.Chains.Exists(c => c != null && c.Tip == b.name)) continue;

            var list = new List<Transform> { b };
            var p = ParentBone(b);
            while (p != null && list.Count < len)
            {
                // 갈라지는 곳(엉덩이 · 손바닥)은 넣지 않음 — 여러 IK가 같은 본을 돌리며 싸우지 않게
                if (_childBones.TryGetValue(p, out var kids) && kids.Count > 1) break;
                list.Insert(0, p);
                p = ParentBone(p);
            }
            if (list.Count < 2) continue;

            AddChain(list, false);
            added++;
        }
        EditorUtility.SetDirty(_data);
        ShowNotification(new GUIContent(added > 0 ? $"IK {added}개 만듦" : "새로 만들 줄기가 없음"));
        SceneView.RepaintAll();
    }

    void AddChain(List<Transform> list, bool record = true)
    {
        if (list == null || list.Count < 2) return;
        if (record) Undo.RecordObject(_data, "IK 만들기");

        string baseName = Regex.Replace(Regex.Replace(list[list.Count - 1].name, @"^.*::", ""), @"_\d+$", "");
        string name = baseName;
        int k = 2;
        while (_data.FindChain(name) != null) name = $"{baseName}_{k++}";

        var ch = new RigIKChain { Name = name, Bones = list.Select(t => t.name).ToArray() };
        _data.Chains.Add(ch);
        _selChain = ch;
        _sel = null;
        if (record) EditorUtility.SetDirty(_data);
        SceneView.RepaintAll();
    }

    // ══════════════════ 애니메이션 탭 ══════════════════

    RigState CurState => _data != null && _stateIdx >= 0 && _stateIdx < _data.States.Count ? _data.States[_stateIdx] : null;

    void DrawAnimTab()
    {
        if (!RequireModelAndData()) return;

        // 상태 목록
        using (new EditorGUILayout.HorizontalScope())
        {
            for (int i = 0; i < _data.States.Count; i++)
            {
                var s = _data.States[i];
                string label = s.Name;
                if (s.Name == _data.IdleState) label += " ·기본";
                else if (s.Name == _data.MoveState) label += " ·이동";
                bool on = GUILayout.Toggle(i == _stateIdx, label, EditorStyles.miniButton);
                if (on && i != _stateIdx) SelectState(i);
            }
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(24))) AddStateMenu();
        }

        var st = CurState;
        if (st == null)
        {
            EditorGUILayout.HelpBox("+ 로 상태를 추가하세요. (Idle = 가만히, Walk = 움직일 때, Grab = 붙잡기 …)", MessageType.Info);
            return;
        }

        // 상태 설정
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.DelayedTextField("이름", st.Name);
            float len = EditorGUILayout.FloatField("길이 (초)", st.Length);
            bool loop = EditorGUILayout.Toggle(new GUIContent("반복", "Idle · Walk처럼 계속 도는 것. 끄면 한 번 재생 후 Idle · Walk로"), st.Loop);
            float spd = EditorGUILayout.FloatField("재생 속도", st.Speed);
            float blend = EditorGUILayout.FloatField(new GUIContent("섞는 시간 (초)", "이 상태로 넘어올 때 이전 자세에서 부드럽게"), st.BlendIn);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data, "상태 설정");
                name = string.IsNullOrWhiteSpace(name) ? st.Name : name.Trim();
                if (name != st.Name)
                {
                    if (_data.IdleState == st.Name) _data.IdleState = name;
                    if (_data.MoveState == st.Name) _data.MoveState = name;
                    st.Name = name;
                }
                st.Length = Mathf.Max(0.05f, len);
                st.Loop = loop;
                st.Speed = Mathf.Max(0.01f, spd);
                st.BlendIn = Mathf.Max(0f, blend);
                EditorUtility.SetDirty(_data);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("기본(가만히)으로 지정", EditorStyles.miniButtonLeft)) SetDataField(() => _data.IdleState = st.Name);
                if (GUILayout.Button("이동으로 지정", EditorStyles.miniButtonMid)) SetDataField(() => _data.MoveState = st.Name);
                if (GUILayout.Button("복제", EditorStyles.miniButtonMid)) DuplicateState(st);
                if (GUILayout.Button("삭제", EditorStyles.miniButtonRight) &&
                    EditorUtility.DisplayDialog("상태 삭제", $"'{st.Name}'을 지울까요?", "삭제", "취소"))
                {
                    Undo.RecordObject(_data, "상태 삭제");
                    _data.States.RemoveAt(_stateIdx);
                    EditorUtility.SetDirty(_data);
                    SelectState(Mathf.Clamp(_stateIdx - 1, 0, _data.States.Count - 1));
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUI.BeginChangeCheck();
            float refSpd = EditorGUILayout.FloatField(new GUIContent("이동 기준 속도 (m/초)", "이 속도로 움직일 때 이동 애니메이션이 1배속"), _data.WalkRefSpeed);
            if (EditorGUI.EndChangeCheck()) SetDataField(() => _data.WalkRefSpeed = Mathf.Max(0.1f, refSpd));
        }

        // 타임라인
        DrawTimeline(st);

        // 재생 · 키
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(_playing ? "■ 정지" : "▶ 재생", GUILayout.Width(70))) { _playing = !_playing; if (!_playing) ApplySample(); }
            if (GUILayout.Button("⏮", GUILayout.Width(28))) { _playing = false; SetTime(0f); }
            GUILayout.Label($"{_time:0.00} / {st.Length:0.00}초", GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            _autoKey = GUILayout.Toggle(_autoKey, new GUIContent("자동 키", "키를 선택한 상태에서 본을 움직이면 그 키에 바로 저장"), "Button", GUILayout.Width(60));
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("키 저장 (K)", "지금 시간에 지금 자세를 저장 (있으면 덮어씀)"), GUILayout.Height(24))) AddOrSetKey();
            using (new EditorGUI.DisabledScope(_keyIdx < 0))
                if (GUILayout.Button("키 삭제", GUILayout.Height(24), GUILayout.Width(70))) DeleteKey();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("자세 복사", EditorStyles.miniButtonLeft)) { _clipboard = new RigPose(); Capture(_clipboard); }
            using (new EditorGUI.DisabledScope(_clipboard == null))
                if (GUILayout.Button("붙여넣기", EditorStyles.miniButtonMid)) { ApplyPose(_clipboard); ResetIKTargets(); OnPoseEdited(); }
            if (GUILayout.Button("기본 자세로", EditorStyles.miniButtonMid)) { ApplyPose(_data.Rest); ResetIKTargets(); OnPoseEdited(); }
            if (GUILayout.Button(new GUIContent("첫 키 → 끝", "첫 키를 맨 끝 시간에 복사 (한 번짜리를 처음 자세로 끝낼 때)"), EditorStyles.miniButtonRight)) CopyFirstToEnd(st);
        }

        // 선택한 키
        if (_keyIdx >= 0 && _keyIdx < st.Keys.Count)
        {
            var k = st.Keys[_keyIdx];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"키 {_keyIdx + 1} / {st.Keys.Count}", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                float kt = EditorGUILayout.FloatField("시간 (초)", k.Time);
                var ease = (RigEase)EditorGUILayout.Popup("다음 키까지", (int)k.Ease, EaseNames);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "키 설정");
                    k.Time = Mathf.Clamp(kt, 0f, st.Length);
                    k.Ease = ease;
                    st.SortKeys();
                    _keyIdx = st.Keys.IndexOf(k);
                    _time = k.Time;
                    EditorUtility.SetDirty(_data);
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("지금 자세를 기본 자세로", "키가 없는 상태 · 에디터 닫을 때 돌아가는 자세"), EditorStyles.miniButtonLeft))
            {
                Undo.RecordObject(_data, "기본 자세");
                Capture(_data.Rest);
                EditorUtility.SetDirty(_data);
            }
            if (GUILayout.Button("AnimationClip으로 내보내기", EditorStyles.miniButtonRight)) ExportClip(st);
        }

        Hint("씬: 본 클릭 → W 이동 · E 회전 · R 끝 끌기 / 주황 상자 = IK / K = 키 저장 / Esc = 선택 해제\n" +
             "타임라인: 빈 곳 클릭 = 시간 이동 · 더블클릭 = 키 추가 · 키 끌기 = 시간 옮기기");
    }

    void SetDataField(System.Action set)
    {
        Undo.RecordObject(_data, "애니메이션 설정");
        set();
        EditorUtility.SetDirty(_data);
    }

    void DrawTimeline(RigState s)
    {
        Rect full = GUILayoutUtility.GetRect(10, 58, GUILayout.ExpandWidth(true));
        Rect r = new(full.x + 10, full.y + 4, full.width - 20, full.height - 8);
        EditorGUI.DrawRect(r, CBg);

        float len = Mathf.Max(0.05f, s.Length);
        float X(float t) => r.x + t / len * r.width;
        float T(float x) => Mathf.Clamp((x - r.x) / r.width * len, 0f, len);

        float step = len <= 1.2f ? 0.1f : len <= 3f ? 0.25f : 0.5f;
        int major = Mathf.Max(1, Mathf.RoundToInt(0.5f / step));
        int idx = 0;
        for (float t = 0f; t <= len + 1e-4f; t += step, idx++)
        {
            bool big = idx % major == 0;
            EditorGUI.DrawRect(new Rect(X(t), r.y, 1, big ? 10 : 5), CTick);
            if (big) GUI.Label(new Rect(X(t) + 2, r.y, 40, 14), t.ToString("0.#"), EditorStyles.miniLabel);
        }

        float ky = r.y + 32;
        for (int i = 0; i < s.Keys.Count; i++)
        {
            var c = GUI.color;
            GUI.color = i == _keyIdx ? new Color(0.3f, 0.85f, 1f) : new Color(0.92f, 0.92f, 0.92f);
            GUI.Label(new Rect(X(s.Keys[i].Time) - 8, ky - 9, 16, 18), "◆", _diamond);
            GUI.color = c;
        }

        EditorGUI.DrawRect(new Rect(X(Mathf.Clamp(_time, 0f, len)) - 1, r.y, 2, r.height), Color.cyan);

        var e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && r.Contains(e.mousePosition):
                {
                    int hit = -1;
                    for (int i = 0; i < s.Keys.Count; i++)
                        if (Mathf.Abs(X(s.Keys[i].Time) - e.mousePosition.x) < 7f && e.mousePosition.y > r.y + 16) { hit = i; break; }

                    if (hit >= 0) { SelectKey(hit); _dragKey = true; }
                    else if (e.clickCount == 2) { _playing = false; SetTime(Snap(T(e.mousePosition.x))); AddOrSetKey(); }
                    else { _playing = false; _scrub = true; SetTime(T(e.mousePosition.x)); }
                    e.Use();
                    break;
                }
            case EventType.MouseDrag when _dragKey && _keyIdx >= 0 && _keyIdx < s.Keys.Count:
                Undo.RecordObject(_data, "키 옮기기");
                s.Keys[_keyIdx].Time = Snap(T(e.mousePosition.x));
                _time = s.Keys[_keyIdx].Time;
                EditorUtility.SetDirty(_data);
                e.Use();
                Repaint();
                break;
            case EventType.MouseDrag when _scrub:
                SetTime(T(e.mousePosition.x));
                e.Use();
                break;
            case EventType.MouseUp:
                if (_dragKey && _keyIdx >= 0 && _keyIdx < s.Keys.Count)
                {
                    var k = s.Keys[_keyIdx];
                    s.SortKeys();
                    _keyIdx = s.Keys.IndexOf(k);
                    ApplySample();
                }
                _dragKey = _scrub = false;
                break;
        }
    }

    static float Snap(float t) => Mathf.Round(t / 0.05f) * 0.05f;

    void SelectState(int i)
    {
        _stateIdx = i;
        _keyIdx = -1;
        _time = 0f;
        _playing = false;
        var s = CurState;
        if (s != null && s.Keys.Count > 0) SelectKey(0);
        else ApplySample();
        Repaint();
    }

    void SelectKey(int i)
    {
        var s = CurState;
        if (s == null || i < 0 || i >= s.Keys.Count) return;
        _keyIdx = i;
        _time = s.Keys[i].Time;
        _playing = false;
        ApplyPose(s.Keys[i].Pose);
        ResetIKTargets();
        Repaint();
    }

    void SetTime(float t)
    {
        _time = t;
        var s = CurState;
        _keyIdx = -1;
        if (s != null)
            for (int i = 0; i < s.Keys.Count; i++)
                if (Mathf.Abs(s.Keys[i].Time - t) < 0.001f) { _keyIdx = i; break; }
        ApplySample();
        Repaint();
    }

    void ApplySample()
    {
        if (_data == null) return;
        _data.Sample(CurState, _time, _tmp);
        ApplyPose(_tmp);
        ResetIKTargets();
    }

    void AddOrSetKey()
    {
        var s = CurState;
        if (s == null) return;
        Undo.RecordObject(_data, "키 저장");

        float t = Mathf.Clamp(_time, 0f, s.Length);
        int found = s.Keys.FindIndex(k => Mathf.Abs(k.Time - t) < 0.01f);
        RigKey key;
        if (found >= 0) key = s.Keys[found];
        else
        {
            key = new RigKey { Time = t };
            s.Keys.Add(key);
            s.SortKeys();
        }
        Capture(key.Pose);
        _keyIdx = s.Keys.IndexOf(key);
        _time = key.Time;
        EditorUtility.SetDirty(_data);
        ShowNotification(new GUIContent(found >= 0 ? "키 덮어씀" : "키 추가"), 0.6);
        Repaint();
    }

    void DeleteKey()
    {
        var s = CurState;
        if (s == null || _keyIdx < 0 || _keyIdx >= s.Keys.Count) return;
        Undo.RecordObject(_data, "키 삭제");
        s.Keys.RemoveAt(_keyIdx);
        _keyIdx = -1;
        EditorUtility.SetDirty(_data);
        ApplySample();
    }

    void CopyFirstToEnd(RigState s)
    {
        if (s.Keys.Count == 0) return;
        Undo.RecordObject(_data, "첫 키 → 끝");
        var first = s.Keys[0];
        int last = s.Keys.FindIndex(k => Mathf.Abs(k.Time - s.Length) < 0.01f);
        var copy = new RigKey { Time = s.Length, Ease = first.Ease, Pose = first.Pose.Clone() };
        if (last >= 0 && last != 0) s.Keys[last] = copy; else s.Keys.Add(copy);
        s.SortKeys();
        EditorUtility.SetDirty(_data);
        SelectKey(s.Keys.IndexOf(copy));
    }

    void AddStateMenu()
    {
        var menu = new GenericMenu();
        foreach (var (name, len, loop) in StatePresets)
        {
            if (_data.Find(name) != null) { menu.AddDisabledItem(new GUIContent($"{name} (있음)")); continue; }
            string n = name; float l = len; bool lp = loop;
            menu.AddItem(new GUIContent($"{name}  — {Describe(name)}"), false, () => AddState(n, l, lp));
        }
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("새 상태 (이름 직접)"), false, () =>
        {
            string n = "State";
            int k = 2;
            while (_data.Find(n) != null) n = $"State{k++}";
            AddState(n, 1f, false);
        });
        menu.ShowAsContext();
    }

    static string Describe(string name) => name switch
    {
        "Idle" => "가만히 (반복)",
        "Walk" => "움직일 때 (반복, 이동 속도에 맞춰 빨라짐)",
        "Appear" => "생길 때 한 번 (그림자 손 솟아오르기)",
        "Grab" => "붙잡기 (그림자 손 — 속박 시간 동안 유지)",
        "Charge" => "돌진 중 (죽음의 기사)",
        "Leap" => "도약 (롤란드)",
        "Attack" => "탄 사람이 벨 때",
        _ => "",
    };

    void AddState(string name, float len, bool loop)
    {
        Undo.RecordObject(_data, "상태 추가");
        var s = new RigState { Name = name, Length = len, Loop = loop };
        var k = new RigKey { Time = 0f };
        Capture(k.Pose);
        s.Keys.Add(k);
        _data.States.Add(s);
        EditorUtility.SetDirty(_data);
        SelectState(_data.States.Count - 1);
    }

    void DuplicateState(RigState src)
    {
        Undo.RecordObject(_data, "상태 복제");
        string n = src.Name + "_복사";
        int i = 2;
        while (_data.Find(n) != null) n = $"{src.Name}_복사{i++}";
        var s = new RigState { Name = n, Length = src.Length, Loop = src.Loop, Speed = src.Speed, BlendIn = src.BlendIn };
        foreach (var k in src.Keys) s.Keys.Add(new RigKey { Time = k.Time, Ease = k.Ease, Pose = k.Pose.Clone() });
        _data.States.Add(s);
        EditorUtility.SetDirty(_data);
        SelectState(_data.States.Count - 1);
    }

    void ExportClip(RigState s)
    {
        string path = EditorUtility.SaveFilePanelInProject("AnimationClip 저장", $"{_data.name}_{s.Name}", "anim",
                                                           "Animator로 쓰고 싶을 때만. 게임에서는 RigAnimator가 RigAnimData를 바로 재생합니다.");
        if (string.IsNullOrEmpty(path)) return;

        const float fps = 30f;
        var clip = new AnimationClip { frameRate = fps };
        int frames = Mathf.Max(2, Mathf.CeilToInt(s.Length * fps) + 1);
        var pose = new RigPose();

        int nb = _data.Bones.Count;
        var curves = new AnimationCurve[nb, 7];
        for (int b = 0; b < nb; b++) for (int c = 0; c < 7; c++) curves[b, c] = new AnimationCurve();

        var prev = new Quaternion[nb];
        for (int f = 0; f < frames; f++)
        {
            float t = Mathf.Min(f / fps, s.Length);
            // 반복 상태의 마지막 프레임은 처음과 같게 (Repeat 때문에 0이 됨) — 그대로 둠
            _data.Sample(s, s.Loop && f == frames - 1 ? s.Length - 0.0001f : t, pose);
            for (int b = 0; b < nb; b++)
            {
                var q = pose.Rot[b];
                if (f > 0 && Quaternion.Dot(prev[b], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                prev[b] = q;
                var p = pose.Pos[b];
                curves[b, 0].AddKey(t, q.x); curves[b, 1].AddKey(t, q.y); curves[b, 2].AddKey(t, q.z); curves[b, 3].AddKey(t, q.w);
                curves[b, 4].AddKey(t, p.x); curves[b, 5].AddKey(t, p.y); curves[b, 6].AddKey(t, p.z);
            }
        }

        string[] props = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
                           "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
        int written = 0;
        for (int b = 0; b < nb; b++)
        {
            var t = Bone(_data.Bones[b]);
            if (t == null) continue;
            string rel = AnimationUtility.CalculateTransformPath(t, _model.transform);
            for (int c = 0; c < 7; c++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(rel, typeof(Transform), props[c]), curves[b, c]);
            written++;
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = s.Loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        clip.EnsureQuaternionContinuity();

        AssetDatabase.CreateAsset(clip, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(clip);
        ShowNotification(new GUIContent($"본 {written}개 내보냄"));
    }

    // ══════════════════ 공통 ══════════════════

    bool RequireModel()
    {
        if (_model == null || EditorUtility.IsPersistent(_model)) return false;
        if (_bones.Count == 0) CollectBones();
        if (_bones.Count == 0)
        {
            EditorGUILayout.HelpBox("이 모델에서 본을 찾지 못했습니다.", MessageType.Warning);
            return false;
        }
        return true;
    }

    bool RequireModelAndData()
    {
        if (!RequireModel()) return false;
        if (_data == null)
        {
            EditorGUILayout.HelpBox("애니메이션 데이터(RigAnimData)가 없습니다.\n" +
                                    "만들면 모델에 RigAnimator가 붙고, 지금 자세가 기본 자세(Idle 첫 키)가 됩니다.\n" +
                                    "본 이름 정리는 먼저 '본 정리' 탭에서 해두는 게 좋습니다.", MessageType.Info);
            if (GUILayout.Button("새 애니메이션 데이터 만들기", GUILayout.Height(28))) CreateData();
            return false;
        }
        return true;
    }

    void CreateData()
    {
        string path = EditorUtility.SaveFilePanelInProject("애니메이션 데이터 저장", _model.name.Replace("(Clone)", "").Trim() + "_Rig", "asset",
                                                           "모델과 같은 폴더에 두면 찾기 편합니다.");
        if (string.IsNullOrEmpty(path)) return;

        var d = CreateInstance<RigAnimData>();
        foreach (var b in _bones)
            if (b != null && !d.Bones.Contains(b.name)) d.Bones.Add(b.name);

        d.Rest.Resize(d.Bones.Count, null);
        for (int i = 0; i < d.Bones.Count; i++)
        {
            var t = Bone(d.Bones[i]);
            if (t == null) continue;
            d.Rest.Rot[i] = t.localRotation;
            d.Rest.Pos[i] = t.localPosition;
        }

        var idle = new RigState { Name = "Idle", Length = 1.2f, Loop = true };
        idle.Keys.Add(new RigKey { Time = 0f, Pose = d.Rest.Clone() });
        d.States.Add(idle);

        AssetDatabase.CreateAsset(d, path);
        AssetDatabase.SaveAssets();
        _data = d;

        var ra = _model.GetComponent<RigAnimator>();
        if (ra == null) ra = Undo.AddComponent<RigAnimator>(_model);
        Undo.RecordObject(ra, "RigAnimator 데이터");
        ra.Data = d;
        EditorUtility.SetDirty(ra);
        if (PrefabUtility.IsPartOfPrefabInstance(ra)) PrefabUtility.RecordPrefabInstancePropertyModifications(ra);

        SelectState(0);
    }

    void Hint(string text) => EditorGUILayout.LabelField(text, _hint);

    void EnsureStyles()
    {
        if (_hint != null) return;
        _hint = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = true };
        _diamond = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
        _sceneLabel = new GUIStyle(EditorStyles.whiteMiniLabel) { richText = true };
        _sceneLabel.normal.background = Texture2D.grayTexture;
        _sceneLabel.normal.textColor = Color.white;
        _rowSel = new GUIStyle(EditorStyles.helpBox);
        var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixel(0, 0, new Color(0.25f, 0.45f, 0.7f, 0.35f));
        tex.Apply();
        _rowSel.normal.background = tex;
    }

    // ══════════════════ 재생 (에디터 업데이트) ══════════════════

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = Mathf.Min((float)(now - _lastTick), 0.1f);
        _lastTick = now;

        if (_playing && _tab == Tab.Anim && _model != null)
        {
            var s = CurState;
            if (s == null) _playing = false;
            else
            {
                _time += dt * s.Speed;
                if (s.Loop) _time = Mathf.Repeat(_time, s.Length);
                else if (_time >= s.Length) { _time = s.Length; _playing = false; }
                _keyIdx = -1;
                ApplySample();
                Repaint();
            }
        }

        if (_partPlay && _part != null)
        {
            var ra = _part.GetComponent<RigAnimator>();
            var names = PartStates(ra);
            if (ra == null || names.Length == 0) StopPartPlay();
            else
            {
                _partT += dt;
                ra.PreviewState(names[Mathf.Clamp(_partStateIdx, 0, names.Length - 1)], _partT);
                SceneView.RepaintAll();
            }
        }
    }

    // ══════════════════ 변화 배치 탭 ══════════════════

    void DrawAlterTab()
    {
        Hint("변화(AlterData)의 파츠를 씬의 팔 리그에 붙여 보면서 마우스로 맞춥니다. 바꾼 값은 AlterData에 바로 저장됩니다.\n" +
             "Motion Setup과 같은 씬(FPArmRig가 있는 씬)에서 쓰세요.");

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _rig = (FPArmRig)EditorGUILayout.ObjectField("팔 리그 (씬)", _rig, typeof(FPArmRig), true);
                if (GUILayout.Button("찾기", EditorStyles.miniButton, GUILayout.Width(40))) _rig = FindFirstObjectByType<FPArmRig>();
            }
            EditorGUI.BeginChangeCheck();
            _alter = (AlterData)EditorGUILayout.ObjectField("변화", _alter, typeof(AlterData), false);
            if (EditorGUI.EndChangeCheck()) { StopPartPlay(); ClearPart(); }
        }

        if (_rig == null) { EditorGUILayout.HelpBox("씬에 FPArmRig가 필요합니다 (Motion Setup에서 쓰는 그 리그).", MessageType.Warning); return; }
        if (_alter == null) { ClearPart(); return; }

        EnsurePart();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            var attach = (AlterAttach)EditorGUILayout.EnumPopup("붙는 곳", _alter.Attach);
            string bone = _alter.BoneName;
            if (attach == AlterAttach.Bone) bone = EditorGUILayout.TextField("  찾을 이름", bone);
            Vector3 pos = EditorGUILayout.Vector3Field("위치", _alter.Position);
            Vector3 rot = EditorGUILayout.Vector3Field("회전", _alter.Rotation);
            Vector3 scl = EditorGUILayout.Vector3Field("크기", _alter.Scale);
            float lift = EditorGUILayout.FloatField(new GUIContent("탄 높이 (Lift, m)", "0이 아니면 몸 · 카메라 · 피격 판정이 이만큼 올라감 (말 등)"), _alter.Lift);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_alter, "변화 배치");
                bool reparent = attach != _alter.Attach || bone != _alter.BoneName;
                _alter.Attach = attach;
                _alter.BoneName = bone;
                _alter.Position = pos;
                _alter.Rotation = rot;
                _alter.Scale = scl;
                _alter.Lift = Mathf.Max(0f, lift);
                EditorUtility.SetDirty(_alter);
                if (reparent) ClearPart();
                EnsurePart();
                SyncPart();
                SceneView.RepaintAll();
            }

            // 탄 높이가 있는데 몸에 붙어 있으면 말까지 같이 올라감
            if (_alter.Lift > 0f && (_alter.Attach == AlterAttach.Body || _alter.Attach == AlterAttach.Head))
            {
                EditorGUILayout.HelpBox("탄 높이(Lift)는 플레이어 몸 · 머리 · 카메라를 올립니다.\n" +
                                        "이 파츠는 몸에 붙어 있어서 같이 올라갑니다. 말처럼 땅에 서 있어야 하는 건 '발밑(Root)'에 붙여야 합니다.",
                                        MessageType.Warning);
                if (GUILayout.Button("발밑(Root)으로 옮기기 — 지금 땅 위 자리 그대로", GUILayout.Height(24))) MoveAlterToRoot();
            }

            int pt = GUILayout.Toolbar((int)_partTool, PartToolNames);
            if (pt != (int)_partTool) { _partTool = (PartTool)pt; SceneView.RepaintAll(); }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("미리보기 다시", EditorStyles.miniButtonLeft)) { ClearPart(); EnsurePart(); }
                if (GUILayout.Button("치우기", EditorStyles.miniButtonMid)) ClearPart();
                if (GUILayout.Button("씬 뷰 맞추기", EditorStyles.miniButtonMid) && _part != null) { Selection.activeGameObject = _part; SceneView.FrameLastActiveSceneView(); }
                if (GUILayout.Button("AlterData 선택", EditorStyles.miniButtonRight)) Selection.activeObject = _alter;
            }
        }

        if (_alter.Prefab == null)
        {
            EditorGUILayout.HelpBox("이 변화에는 Prefab이 없습니다 (스탯만).", MessageType.Info);
            return;
        }
        if (_part == null)
        {
            EditorGUILayout.HelpBox("붙일 곳을 찾지 못했습니다 — 붙는 곳 설정과 팔 리그의 Body · Head · 무기를 확인하세요.", MessageType.Warning);
            return;
        }

        // 파츠 애니메이션 미리보기
        var ra = _part.GetComponent<RigAnimator>();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("파츠 애니메이션", EditorStyles.boldLabel);
            if (ra == null || ra.Data == null)
            {
                EditorGUILayout.LabelField("프리팹에 RigAnimator + 애니메이션 데이터가 없습니다.\n" +
                                           "아래 버튼으로 이 미리보기를 편집 대상으로 잡고, '애니메이션' 탭에서 데이터를 만든 뒤\n" +
                                           "프리팹을 열어 RigAnimator에 그 데이터를 넣으세요.", _hint);
            }
            else
            {
                var names = PartStates(ra);
                _partStateIdx = EditorGUILayout.Popup("상태", Mathf.Clamp(_partStateIdx, 0, Mathf.Max(0, names.Length - 1)), names);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(_partPlay ? "■ 정지" : "▶ 탄 채로 재생")) { if (_partPlay) StopPartPlay(); else { _partPlay = true; _partT = 0f; } }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("여기서 애니메이션 편집", "탄 사람과 같이 보면서 키를 잡음 (애니메이션 데이터는 에셋이라 저장됨)")))
                {
                    StopPartPlay();
                    if (ra != null && ra.Data != null) _data = ra.Data;
                    SetModel(_part);
                    _tab = Tab.Anim;
                }
                if (GUILayout.Button(new GUIContent("프리팹 열기 (본 정리)", "본 이름 · 부모 정리와 RigAnimator 설정은 프리팹에서")))
                {
                    ClearPart();
                    AssetDatabase.OpenAsset(_alter.Prefab);
                    var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                    if (stage != null) SetModel(stage.prefabContentsRoot);
                    _tab = Tab.Bones;
                }
            }
        }
    }

    /// <summary>몸에 붙은 파츠를 발밑(플레이어 루트)으로 — 몸을 내린 상태(탄 높이 0)의 자리를 그대로 유지</summary>
    void MoveAlterToRoot()
    {
        EnsurePart();
        if (_part == null || _rig == null) return;

        float lift = _alter.Lift;
        ApplyLiftPreview(0f);                                   // 몸을 내려서 '땅 기준' 자리를 읽음

        var t = _part.transform;
        Vector3 wp = t.position;
        Quaternion wr = t.rotation;
        Vector3 ws = t.lossyScale;
        Transform top = _rig.Body != null ? _rig.Body.root : _rig.transform.root;
        Vector3 ts = top.lossyScale;

        Undo.RecordObject(_alter, "발밑으로 옮기기");
        _alter.Attach = AlterAttach.Root;
        _alter.Position = top.InverseTransformPoint(wp);
        _alter.Rotation = (Quaternion.Inverse(top.rotation) * wr).eulerAngles;
        _alter.Scale = new Vector3(ws.x / Nz(ts.x), ws.y / Nz(ts.y), ws.z / Nz(ts.z));
        _alter.Lift = lift;
        EditorUtility.SetDirty(_alter);

        ClearPart();
        EnsurePart();
        ApplyLiftPreview(lift);
        ShowNotification(new GUIContent("발밑(Root)으로 옮김 — 이제 몸만 올라갑니다"));
    }

    static float Nz(float v) => Mathf.Abs(v) < 0.0001f ? 1f : v;

    static string[] PartStates(RigAnimator ra) =>
        ra != null && ra.Data != null ? ra.Data.States.Where(s => s != null).Select(s => s.Name).ToArray() : new string[0];

    void StopPartPlay()
    {
        if (!_partPlay) return;
        _partPlay = false;
        if (_part != null)
        {
            var ra = _part.GetComponent<RigAnimator>();
            if (ra != null) ra.PreviewRest();
        }
    }

    Transform ResolveAlterParent(AlterData a)
    {
        if (_rig == null || a == null) return null;
        Transform top = _rig.Body != null ? _rig.Body.root : _rig.transform.root;
        switch (a.Attach)
        {
            case AlterAttach.Body: return _rig.Body != null ? _rig.Body : top;
            case AlterAttach.Head: return _rig.Head != null ? _rig.Head : (_rig.Body != null ? _rig.Body : top);
            case AlterAttach.RightHand: return _rig.RightHand;
            case AlterAttach.LeftHand: return _rig.LeftHand;
            case AlterAttach.Weapon: return _rig.WeaponTip;
            case AlterAttach.Root: return top;
            case AlterAttach.Bone:
                if (string.IsNullOrEmpty(a.BoneName)) return null;
                foreach (var t in top.GetComponentsInChildren<Transform>(true)) if (t.name == a.BoneName) return t;
                return null;
        }
        return top;
    }

    void EnsurePart()
    {
        if (_alter == null || _alter.Prefab == null || _rig == null) { ClearPart(); return; }
        var parent = ResolveAlterParent(_alter);
        if (_part != null && _partFor == _alter && _partParent == parent) return;

        ClearPart();
        if (parent == null) return;

        _part = Instantiate(_alter.Prefab, parent);
        _part.name = "__AlterPreview";
        foreach (var t in _part.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        foreach (var c in _part.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        _partFor = _alter;
        _partParent = parent;
        SyncPart();
        ApplyLiftPreview(_alter.Lift);
    }

    void SyncPart()
    {
        if (_part == null || _alter == null) return;
        _part.transform.localPosition = _alter.Position;
        _part.transform.localRotation = Quaternion.Euler(_alter.Rotation);
        _part.transform.localScale = _alter.Scale;
        ApplyLiftPreview(_alter.Lift);
    }

    float _shownLift = 0f;
    readonly Dictionary<Transform, Vector3> _liftOrig = new();

    Transform AlterTop() => _rig == null ? null : (_rig.Body != null ? _rig.Body.root : _rig.transform.root);

    /// <summary>탄 높이만큼 카메라 · 팔 · 몸을 올려서 보여줌 — 게임(PlayerAlter)과 같은 대상</summary>
    void ApplyLiftPreview(float lift)
    {
        if (_rig == null || Mathf.Approximately(_shownLift, lift)) return;
        _shownLift = lift;

        foreach (var kv in _liftOrig) if (kv.Key != null) kv.Key.localPosition = kv.Value;
        _liftOrig.Clear();

        if (lift > 0f)
        {
            var top = AlterTop();
            foreach (var t in PlayerAlter.AutoLiftTargets(top, _rig))
                if (t != null) _liftOrig[t] = t.localPosition;
            ReapplyLift();
        }
        SceneView.RepaintAll();
    }

    /// <summary>다른 미리보기(Motion Setup)가 몸을 되돌려도 다시 올림</summary>
    void ReapplyLift()
    {
        foreach (var kv in _liftOrig)
        {
            var t = kv.Key;
            if (t == null) continue;
            Vector3 up = t.parent != null ? t.parent.InverseTransformVector(Vector3.up * _shownLift) : Vector3.up * _shownLift;
            t.localPosition = kv.Value + up;
        }
    }

    void ClearPart()
    {
        if (_part != null) DestroyImmediate(_part);
        _part = null;
        _partFor = null;
        _partParent = null;
        _partPlay = false;
        if (_model != null && _model.hideFlags.HasFlag(HideFlags.DontSave)) _model = null;
        ApplyLiftPreview(0f);
    }

    void SceneAlter()
    {
        if (_part == null || _alter == null) { ShowTools(); return; }
        HideTools();

        var t = _part.transform;
        if (Event.current.type == EventType.Layout) { SyncPart(); ReapplyLift(); }

        EditorGUI.BeginChangeCheck();
        switch (_partTool)
        {
            case PartTool.Move:
                t.position = Handles.PositionHandle(t.position, Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity);
                break;
            case PartTool.Rotate:
                t.rotation = Handles.RotationHandle(t.rotation, t.position);
                break;
            case PartTool.Scale:
                t.localScale = Handles.ScaleHandle(t.localScale, t.position, t.rotation, HandleUtility.GetHandleSize(t.position));
                break;
        }
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_alter, "변화 배치");
            _alter.Position = t.localPosition;
            _alter.Rotation = t.localRotation.eulerAngles;
            _alter.Scale = t.localScale;
            EditorUtility.SetDirty(_alter);
            Repaint();
        }

        // 탄 높이 — 발밑에서 위로 끄는 초록 화살표
        Transform top = AlterTop();
        Vector3 basePos = top.position;
        Vector3 lp = basePos + Vector3.up * _alter.Lift;
        float size = HandleUtility.GetHandleSize(lp);

        Handles.color = new Color(0.3f, 1f, 0.45f, 0.9f);
        Handles.DrawWireDisc(lp, Vector3.up, size * 0.35f);
        Handles.DrawDottedLine(basePos, lp, 3f);
        EditorGUI.BeginChangeCheck();
        Vector3 nl = Handles.Slider(lp, Vector3.up, size * 0.7f, Handles.ArrowHandleCap, 0.05f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_alter, "탄 높이");
            _alter.Lift = Mathf.Max(0f, Mathf.Round((nl.y - basePos.y) * 100f) / 100f);
            EditorUtility.SetDirty(_alter);
            ApplyLiftPreview(_alter.Lift);
            Repaint();
        }
        Handles.Label(lp + Vector3.right * size * 0.4f, $" 탄 높이 {_alter.Lift:0.00}m", _sceneLabel);
    }
}
#endif