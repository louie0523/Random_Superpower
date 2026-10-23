using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(HandRig))]
public class HandRigEditor : Editor
{
    // ══════════════════ 상수 ══════════════════

    static readonly string[] FingerNames = { "엄지", "검지", "중지", "약지" };
    static readonly Color[] FingerColors =
    {
        new(1f, 0.5f, 0.2f),
        new(0.3f, 0.9f, 1f),
        new(0.4f, 1f, 0.4f),
        new(1f, 0.4f, 0.8f),
    };

    public enum IkMode { TipIK, JointRotate, Both }
    public static IkMode Mode = IkMode.Both;

    // ══════════════════ 상태 ══════════════════

    public static HandPose EditTarget;
    public static System.Action OnPoseChanged;

    Vector3[] _targets = new Vector3[HandPose.FingerCount];
    bool[] _dragging = new bool[HandPose.FingerCount];

    int _selectedFinger = -1;
    int _selectedJoint = -1;

    // ══════════════════ 인스펙터 ══════════════════

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var rig = (HandRig)target;

        EditorGUILayout.Space(6);
        if (GUILayout.Button("본 자동 탐색", GUILayout.Height(24))) rig.Rebuild();
        if (GUILayout.Button("Rest 자세로")) rig.ResetToRest();

        // ── 손가락별 끝 · 펴기 ──
        if (rig.IsBuilt)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("손가락", EditorStyles.miniBoldLabel);
            for (int f = 0; f < HandPose.FingerCount; f++)
            {
                int n = rig.GetJointCount(f);
                if (n == 0) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{FingerNames[f]}  마디 {n}  ·  끝: {rig.TipSourceLabel(f)}", GUILayout.MinWidth(160));
                if (GUILayout.Button("일자로", GUILayout.Width(60))) Straighten(rig, f);
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("전부 일자로")) Straighten(rig, -1);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("프리셋", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("펌")) ApplyPreset(rig, HandPose.Open(rig.CurlAxis));
        if (GUILayout.Button("자연")) ApplyPreset(rig, HandPose.Relaxed(rig.CurlAxis));
        if (GUILayout.Button("쥠")) ApplyPreset(rig, HandPose.Grip(rig.CurlAxis));
        if (GUILayout.Button("주먹")) ApplyPreset(rig, HandPose.Fist(rig.CurlAxis));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("좌우 복사", EditorStyles.miniBoldLabel);

        //EditorGUILayout.BeginHorizontal();
        //if (GUILayout.Button("→ 미러 복사")) MirrorToOther(rig, true);
        //if (GUILayout.Button("← 미러 가져오기")) MirrorToOther(rig, false);
        //EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("→ 그대로 복사")) CopyToOther(rig, true);
        if (GUILayout.Button("← 그대로 가져오기")) CopyToOther(rig, false);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            EditTarget != null
                ? "씬에서 조작한 결과가 포즈에 저장됩니다."
                : "편집 대상이 없습니다. 모션 툴이나 FPArmRig에서 지정하세요.",
            MessageType.None);
    }

    /// <summary>손가락을 일자로 — 각 마디를 바로 앞 마디 방향으로 (f = -1이면 전부)</summary>
    void Straighten(HandRig rig, int f)
    {
        Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, "Straighten Finger");
        for (int i = 0; i < HandPose.FingerCount; i++)
            if (f < 0 || f == i) rig.StraightenFinger(i);

        if (EditTarget != null)
        {
            rig.BakeAll(EditTarget);
            OnPoseChanged?.Invoke();
        }
        SceneView.RepaintAll();
    }

    void ApplyPreset(HandRig rig, HandPose p)
    {
        rig.Apply(p);

        if (EditTarget != null)
        {
            rig.BakeAll(EditTarget);
            OnPoseChanged?.Invoke();
        }

        SceneView.RepaintAll();
    }

    // ══════════════════ 좌우 복사 ══════════════════

    HandRig FindOtherHand(HandRig rig)
    {
        var arm = rig.GetComponentInParent<FPArmRig>();
        if (arm == null) return null;

        if (arm.RightHandRig == rig) return arm.LeftHandRig;
        if (arm.LeftHandRig == rig) return arm.RightHandRig;
        return null;
    }

    //void MirrorToOther(HandRig rig, bool push)
    //{
    //    var other = FindOtherHand(rig);
    //    if (other == null)
    //    {
    //        Debug.LogWarning("[HandRig] 반대 손을 못 찾았습니다. FPArmRig 연결 확인.");
    //        return;
    //    }

    //    if (push)
    //    {
    //        var cur = new HandPose();
    //        rig.BakeAll(cur);
    //        other.Apply(cur.Mirrored());
    //    }
    //    else
    //    {
    //        var src = new HandPose();
    //        other.BakeAll(src);
    //        rig.Apply(src.Mirrored());

    //        if (EditTarget != null)
    //        {
    //            rig.BakeAll(EditTarget);
    //            OnPoseChanged?.Invoke();
    //        }
    //    }

    //    SceneView.RepaintAll();
    //}

    void CopyToOther(HandRig rig, bool push)
    {
        var other = FindOtherHand(rig);
        if (other == null) return;

        if (push)
        {
            var cur = new HandPose();
            rig.BakeAll(cur);
            other.Apply(cur);
        }
        else
        {
            var src = new HandPose();
            other.BakeAll(src);
            rig.Apply(src);

            if (EditTarget != null)
            {
                rig.BakeAll(EditTarget);
                OnPoseChanged?.Invoke();
            }
        }

        SceneView.RepaintAll();
    }

    // ══════════════════ 씬 뷰 ══════════════════

    void OnSceneGUI()
    {
        var rig = (HandRig)target;
        if (!rig.IsBuilt) rig.Build();
        if (rig.Chains == null) return;

        DrawModeBar();

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            if (rig.GetJointCount(f) == 0) continue;

            var chain = rig.Chains[f];

            // 뼈대
            Handles.color = new Color(FingerColors[f].r, FingerColors[f].g,
                                      FingerColors[f].b, 0.6f);
            for (int j = 0; j < chain.Length - 1; j++)
                if (chain[j] != null && chain[j + 1] != null)
                    Handles.DrawLine(chain[j].position, chain[j + 1].position, 2f);

            Vector3 tip = rig.GetTipPosition(f);
            if (chain.Length > 0 && chain[^1] != null)
                Handles.DrawLine(chain[^1].position, tip, 2f);

            if (Mode != IkMode.TipIK) DrawJointHandles(rig, f, chain);
            if (Mode != IkMode.JointRotate) DrawTipHandle(rig, f, tip);
        }
    }

    void DrawJointHandles(HandRig rig, int f, Transform[] chain)
    {
        // 마지막 = 손가락 끝 (끝 마디의 휘어짐도 이걸로 잡음). '둘 다' 모드에선 끝점 IK 손잡이가 대신함
        int count = Mode == IkMode.JointRotate ? chain.Length + 1 : chain.Length;
        for (int j = 0; j < count; j++)
        {
            bool isTip = j == chain.Length;
            var bone = isTip ? null : chain[j];
            if (!isTip && bone == null) continue;
            Vector3 pos = isTip ? rig.GetTipPosition(f) : bone.position;

            float hs = HandleUtility.GetHandleSize(pos);

            // 뒤쪽 마디일수록 작게
            float t = chain.Length > 1 ? (float)j / (chain.Length - 1) : 0f;
            float size = hs * Mathf.Lerp(0.055f, 0.085f, t);

            Handles.color = new Color(FingerColors[f].r, FingerColors[f].g,
                                      FingerColors[f].b, 0.85f);

            EditorGUI.BeginChangeCheck();
            Vector3 np = Handles.FreeMoveHandle(
                pos, size, Vector3.zero, Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(chain, "Finger IK");
                // 이 마디까지의 부분 체인에 IK (끝이면 끝 마디까지 전부)
                rig.SolveIKPartial(f, j, np);

                if (EditTarget != null)
                {
                    rig.BakeToPose(f, EditTarget);
                    OnPoseChanged?.Invoke();
                }

                SceneView.RepaintAll();
            }
        }
    }

    void DrawTipHandle(HandRig rig, int f, Vector3 tip)
    {
        if (!_dragging[f]) _targets[f] = tip;

        Handles.color = FingerColors[f];
        float size = HandleUtility.GetHandleSize(_targets[f]) * 0.11f;

        EditorGUI.BeginChangeCheck();
        Vector3 np = Handles.FreeMoveHandle(
            _targets[f], size, Vector3.zero, Handles.SphereHandleCap);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObjects(rig.Chains[f], "Finger Tip IK");
            _dragging[f] = true;
            _targets[f] = np;

            rig.SolveIK(f, np, EditTarget ?? new HandPose());
            if (EditTarget != null) OnPoseChanged?.Invoke();
        }

        if (Event.current.type == EventType.MouseUp) _dragging[f] = false;

        Handles.Label(_targets[f] + Vector3.up * size * 1.6f,
            FingerNames[f], EditorStyles.whiteMiniLabel);
    }

    void DrawModeBar()
    {
        Handles.BeginGUI();

        var box = new Rect(10, 40, 300, 56);
        EditorGUI.DrawRect(box, new Color(0, 0, 0, 0.75f));

        GUILayout.BeginArea(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12));

        GUILayout.Label(EditTarget != null ? "◆ 편집 중" : "읽기 전용",
            EditorStyles.whiteMiniLabel);

        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(Mode == IkMode.TipIK, "끝점 IK", EditorStyles.miniButtonLeft))
            Mode = IkMode.TipIK;
        if (GUILayout.Toggle(Mode == IkMode.JointRotate, "마디 회전", EditorStyles.miniButtonMid))
            Mode = IkMode.JointRotate;
        if (GUILayout.Toggle(Mode == IkMode.Both, "둘 다", EditorStyles.miniButtonRight))
            Mode = IkMode.Both;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
        Handles.EndGUI();
    }
}