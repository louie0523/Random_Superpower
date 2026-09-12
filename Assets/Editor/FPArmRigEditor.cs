using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(FPArmRig))]
public class FPArmRigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var rig = (FPArmRig)target;

        EditorGUILayout.Space(6);

        if (GUILayout.Button("손 재생성", GUILayout.Height(26))) rig.RebuildHands();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("손 자세 초기화"))
        {
            rig.RightHandRig?.ResetToRest();
            rig.LeftHandRig?.ResetToRest();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("기본 자세 적용"))
        {
            rig.ApplyPose(rig.GetEffectiveIdle());
            SceneView.RepaintAll();
        }

        EditorGUILayout.EndHorizontal();
    }
}