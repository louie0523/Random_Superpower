using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(FPArmRig))]
public class FPArmRigEditor : Editor
{
    static bool _foldBones = true;
    static bool _foldIdle;
    static bool _foldFingers = true;
    static bool _foldMisc;

    static readonly string[] FingerNames = { "엄지", "검지", "중지", "약지" };
    static readonly float[] JointWeights = { 1f, 0.95f, 0.85f };
    static HandPose.MirrorAxis _mirrorAxis = HandPose.MirrorAxis.X;

    public override void OnInspectorGUI()
    {
        var rig = (FPArmRig)target;
        serializedObject.Update();

        EditorGUILayout.Space(4);
        if (GUILayout.Button("손 재생성", GUILayout.Height(26))) rig.RebuildHands();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("기본 자세 적용")) Apply(rig);
        if (GUILayout.Button("씬 손을 기본값으로")) BakeCurrent(rig);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUI.BeginChangeCheck();
        rig.ForceShowLeftHand = EditorGUILayout.Toggle(
            new GUIContent("왼손 항상 표시", "편집용. 인게임에는 영향 없음"),
            rig.ForceShowLeftHand);
        if (EditorGUI.EndChangeCheck()) Apply(rig);

        EditorGUILayout.Space(6);
        _foldBones = EditorGUILayout.Foldout(_foldBones, "본 연결", true, EditorStyles.foldoutHeader);
        if (_foldBones)
        {
            EditorGUI.indentLevel++;
            Prop("ArmRoot"); Prop("RightHand"); Prop("LeftHand");
            Prop("CamPivot"); Prop("Camera");
            Prop("RightHandRig"); Prop("LeftHandRig");
            Prop("RightHandPrefab"); Prop("LeftHandPrefab");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(4);
        _foldIdle = EditorGUILayout.Foldout(_foldIdle, "기본 자세", true, EditorStyles.foldoutHeader);
        if (_foldIdle)
        {
            EditorGUI.indentLevel++;
            Prop("IdlePose");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(4);
        _foldFingers = EditorGUILayout.Foldout(_foldFingers, "기본 손가락", true, EditorStyles.foldoutHeader);

        serializedObject.ApplyModifiedProperties();

        // 손가락 섹션은 SerializedObject 밖에서 (직접 필드 조작)
        if (_foldFingers) DrawFingerSection(rig);

        serializedObject.Update();
        EditorGUILayout.Space(4);
        _foldMisc = EditorGUILayout.Foldout(_foldMisc, "기타", true, EditorStyles.foldoutHeader);
        if (_foldMisc)
        {
            EditorGUI.indentLevel++;
            Prop("LeftGripNodeName");
            Prop("EnableFloat"); Prop("FloatAmount"); Prop("FloatSpeed"); Prop("RotDrift");
            EditorGUI.indentLevel--;
        }
        serializedObject.ApplyModifiedProperties();
    }

    void Prop(string name)
    {
        var p = serializedObject.FindProperty(name);
        if (p != null) EditorGUILayout.PropertyField(p, true);
    }

    // ══════════════════ 손가락 ══════════════════

    void DrawFingerSection(FPArmRig rig)
    {
        Vector3 axis = rig.RightHandRig != null
            ? rig.RightHandRig.CurlAxis : new Vector3(0f, 0f, -70f);

        // null 방어
        rig.IdleRightFingers ??= HandPose.Relaxed(axis);
        rig.IdleLeftFingers ??= HandPose.Relaxed(axis);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("양손 '자연'으로"))
        {
            Undo.RecordObject(rig, "Set Idle Fingers");
            rig.IdleRightFingers = HandPose.Relaxed(axis);
            rig.IdleLeftFingers = HandPose.Relaxed(axis);
            Apply(rig);
        }
        if (GUILayout.Button("양손 '쥠'으로"))
        {
            Undo.RecordObject(rig, "Set Idle Fingers");
            rig.IdleRightFingers = HandPose.Grip(axis);
            rig.IdleLeftFingers = HandPose.Grip(axis);
            Apply(rig);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("오른손 → 왼손"))
        {
            if (rig.RightHandRig == null || rig.LeftHandRig == null)
            {
                Debug.LogWarning("[Copy] HandRig 연결 확인 필요");
            }
            else
            {
                Undo.RecordObject(rig, "Copy Hand");

                rig.LeftHandRig.CopyBoneRotationsFrom(rig.RightHandRig);

                rig.IdleLeftFingers ??= new HandPose();
                rig.LeftHandRig.BakeAll(rig.IdleLeftFingers);

                EditorUtility.SetDirty(rig);
                SceneView.RepaintAll();
                Debug.Log("[Copy] 오른손 → 왼손 완료");
            }
        }

        if (GUILayout.Button("왼손 → 오른손"))
        {
            if (rig.RightHandRig == null || rig.LeftHandRig == null)
            {
                Debug.LogWarning("[Copy] HandRig 연결 확인 필요");
            }
            else
            {
                Undo.RecordObject(rig, "Copy Hand");

                rig.RightHandRig.CopyBoneRotationsFrom(rig.LeftHandRig);

                rig.IdleRightFingers ??= new HandPose();
                rig.RightHandRig.BakeAll(rig.IdleRightFingers);

                EditorUtility.SetDirty(rig);
                SceneView.RepaintAll();
                Debug.Log("[Copy] 왼손 → 오른손 완료");
            }
        }

        EditorGUILayout.EndHorizontal();


        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("오른손", EditorStyles.miniBoldLabel);
        DrawHand(rig, true, axis, rig.RightHandRig);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("왼손", EditorStyles.miniBoldLabel);
        DrawHand(rig, false, axis, rig.LeftHandRig);
    }

    void DrawHand(FPArmRig rig, bool isRight, Vector3 axis, HandRig handRig)
    {
        HandPose pose = isRight ? rig.IdleRightFingers : rig.IdleLeftFingers;
        if (pose == null) return;

        // 프리셋
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("펌", EditorStyles.miniButtonLeft))
            SetPose(rig, isRight, HandPose.Open(axis));
        if (GUILayout.Button("자연", EditorStyles.miniButtonMid))
            SetPose(rig, isRight, HandPose.Relaxed(axis));
        if (GUILayout.Button("쥠", EditorStyles.miniButtonMid))
            SetPose(rig, isRight, HandPose.Grip(axis));
        if (GUILayout.Button("주먹", EditorStyles.miniButtonRight))
            SetPose(rig, isRight, HandPose.Fist(axis));
        EditorGUILayout.EndHorizontal();

        if (handRig == null)
        {
            EditorGUILayout.LabelField("HandRig 미연결", EditorStyles.miniLabel);
            return;
        }

        // 씬 편집 토글
        bool editing = ReferenceEquals(HandRigEditor.EditTarget, pose);
        bool now = GUILayout.Toggle(editing,
            editing ? "■ 씬 IK 편집 중" : "▶ 씬에서 IK로 편집", "Button");

        if (now != editing)
        {
            if (now)
            {
                HandRigEditor.EditTarget = pose;
                var t = rig;
                HandRigEditor.OnPoseChanged = () => { EditorUtility.SetDirty(t); Repaint(); };
                Selection.activeGameObject = handRig.gameObject;
            }
            else
            {
                HandRigEditor.EditTarget = null;
                HandRigEditor.OnPoseChanged = null;
            }
            SceneView.RepaintAll();
        }

        // 슬라이더
        pose.EnsureValid();
        EditorGUI.indentLevel++;

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var fp = pose.Get(f);
            if (fp == null || fp.Joints == null || fp.Joints.Length == 0) continue;

            float cur = fp.EstimateCurl(axis);

            if (cur > 1.2f)
            {
                EditorGUILayout.LabelField(FingerNames[f],
                    $"{cur:F2}  (IK 편집됨)", EditorStyles.miniLabel);
                continue;
            }

            EditorGUI.BeginChangeCheck();
            float nv = EditorGUILayout.Slider(FingerNames[f], cur, 0f, 1.2f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(rig, "Finger Curl");
                fp.SetCurl(nv, axis, JointWeights);
                EditorUtility.SetDirty(rig);
                handRig.Apply(pose);
                SceneView.RepaintAll();
            }
        }

        EditorGUI.indentLevel--;
    }

    void SetPose(FPArmRig rig, bool isRight, HandPose np)
    {
        Undo.RecordObject(rig, "Set Fingers");

        if (isRight) rig.IdleRightFingers = np;
        else rig.IdleLeftFingers = np;

        EditorUtility.SetDirty(rig);
        Apply(rig);
    }

    void BakeCurrent(FPArmRig rig)
    {
        if (rig.RightHandRig != null)
        {
            rig.IdleRightFingers ??= new HandPose();
            rig.RightHandRig.BakeAll(rig.IdleRightFingers);
        }
        if (rig.LeftHandRig != null)
        {
            rig.IdleLeftFingers ??= new HandPose();
            rig.LeftHandRig.BakeAll(rig.IdleLeftFingers);
        }

        EditorUtility.SetDirty(rig);
        Repaint();

        Debug.Log($"[Bake] 저장 후 실제 값 = {rig.IdleRightFingers.Get(0).Joints[0]}");
    }

    void Apply(FPArmRig rig)
    {
        EditorUtility.SetDirty(rig);
        rig.ApplyPose(rig.IdlePose);
        SceneView.RepaintAll();
    }
}