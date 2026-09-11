using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class HandIkGizmo
{
    static readonly string[] FingerNames = { "엄지", "검지", "중지", "약지" };
    static readonly Color[] FingerColors =
    {
        new(1f, 0.5f, 0.2f),
        new(0.3f, 0.9f, 1f),
        new(0.4f, 1f, 0.4f),
        new(1f, 0.4f, 0.8f),
    };

    /// <summary>현재 편집 중인 포즈</summary>
    public static HandPose EditTarget;
    public static HandRig ActiveRig;
    public static System.Action OnPoseChanged;

    public static bool Enabled = true;

    static readonly Vector3[] _targets = new Vector3[HandPose.FingerCount];
    static readonly bool[] _dragging = new bool[HandPose.FingerCount];

    static HandIkGizmo()
    {
        SceneView.duringSceneGui -= OnScene;
        SceneView.duringSceneGui += OnScene;
    }

    static void OnScene(SceneView sv)
    {
        if (Application.isPlaying) return;

        var rig = ResolveRig();

        if (!Enabled) return;
        if (rig == null) return;

        Handles.BeginGUI();
        GUI.color = Color.cyan;
        GUI.Label(new Rect(10, 60, 500, 20),
            $"[IK] built={rig.IsBuilt} chains={(rig.Chains == null ? "NULL" : rig.Chains.Length.ToString())} " +
            $"joints={rig.GetJointCount(0)}/{rig.GetJointCount(1)}/{rig.GetJointCount(2)}/{rig.GetJointCount(3)}");
        GUI.color = Color.white;
        Handles.EndGUI();

        if (!rig.IsBuilt) rig.Build();
        DrawToolbar(sv, rig);
        DrawHandles(rig);
    }

    /// <summary>편집 대상 리그 결정 — 지정된 것 우선, 없으면 씬에서 탐색</summary>
    static HandRig ResolveRig()
    {
        if (ActiveRig != null) return ActiveRig;

        // 선택한 오브젝트나 그 부모/자식에서 찾기
        var go = Selection.activeGameObject;
        if (go != null)
        {
            var r = go.GetComponent<HandRig>();
            if (r != null) return r;

            r = go.GetComponentInChildren<HandRig>(true);
            if (r != null) return r;

            r = go.GetComponentInParent<HandRig>();
            if (r != null) return r;
        }

        return null;
    }

    static void DrawToolbar(SceneView sv, HandRig rig)
    {
        Handles.BeginGUI();

        var box = new Rect(10, 40, 260, 84);
        EditorGUI.DrawRect(box, new Color(0, 0, 0, 0.75f));

        GUILayout.BeginArea(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12));

        GUILayout.Label($"손 IK — {rig.name}", EditorStyles.whiteBoldLabel);
        GUILayout.Label(EditTarget != null ? "◆ 편집 중" : "읽기 전용",
            EditorStyles.whiteMiniLabel);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Rest")) { rig.ResetToRest(); Bake(rig); }
        if (GUILayout.Button("자연")) ApplyPreset(rig, HandPose.Relaxed(rig.CurlAxis));
        if (GUILayout.Button("쥠")) ApplyPreset(rig, HandPose.Grip(rig.CurlAxis));
        if (GUILayout.Button("끄기")) Enabled = false;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    static void Bake(HandRig rig)
    {
        if (EditTarget == null) return;
        rig.BakeAll(EditTarget);
        OnPoseChanged?.Invoke();
    }

    static void ApplyPreset(HandRig rig, HandPose p)
    {
        rig.Apply(p);
        if (EditTarget != null) { CopyInto(EditTarget, p); OnPoseChanged?.Invoke(); }
        SceneView.RepaintAll();
    }

    static void CopyInto(HandPose dst, HandPose src)
    {
        dst.EnsureValid();
        src.EnsureValid();
        for (int i = 0; i < HandPose.FingerCount; i++)
            dst.Fingers[i] = src.Fingers[i].Clone();
    }

    static void DrawHandles(HandRig rig)
    {
        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            if (rig.GetJointCount(f) == 0) continue;

            var chain = rig.Chains[f];

            // 뼈대
            Handles.color = new Color(FingerColors[f].r, FingerColors[f].g,
                                      FingerColors[f].b, 0.5f);
            for (int j = 0; j < chain.Length - 1; j++)
                if (chain[j] != null && chain[j + 1] != null)
                    Handles.DrawLine(chain[j].position, chain[j + 1].position, 2f);

            Vector3 tip = rig.GetTipPosition(f);
            if (chain.Length > 0 && chain[^1] != null)
                Handles.DrawLine(chain[^1].position, tip, 2f);

            // 관절점
            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            foreach (var b in chain)
                if (b != null)
                    Handles.SphereHandleCap(0, b.position, Quaternion.identity,
                        HandleUtility.GetHandleSize(b.position) * 0.04f, EventType.Repaint);

            // IK 핸들
            if (!_dragging[f]) _targets[f] = tip;

            Handles.color = FingerColors[f];
            float size = HandleUtility.GetHandleSize(_targets[f]) * 0.12f;

            EditorGUI.BeginChangeCheck();
            Vector3 np = Handles.FreeMoveHandle(
                _targets[f], size, Vector3.zero, Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                _dragging[f] = true;
                _targets[f] = np;

                var pose = EditTarget ?? new HandPose();

                Undo.RecordObject(rig.transform, "Finger IK");
                rig.SolveIK(f, np, pose);

                if (EditTarget != null) OnPoseChanged?.Invoke();
                SceneView.RepaintAll();
            }

            if (Event.current.type == EventType.MouseUp) _dragging[f] = false;

            Handles.Label(_targets[f] + Vector3.up * size * 1.6f,
                FingerNames[f], EditorStyles.whiteMiniLabel);
        }
    }
}