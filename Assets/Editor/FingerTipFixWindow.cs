using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 손가락 끝 보정 — 고른 모션들의 손 모양에서, 끝 마디를 바로 앞 마디가 굽은 만큼 따라 굽혀 줌.
/// (사람 손가락은 가운데 마디를 굽히면 끝 마디도 약 2/3 같이 굽음)
///
/// 예전엔 끝 마디가 IK로 안 돌아서 기존 모션의 끝 마디는 '기본 자세 그대로'로 저장돼 있음
/// → 손끝이 풀려(펴져) 보이면 이 창으로 한 번에 맞춤. 기본 자세(Rest)를 다시 잡았을 때도 같은 증상.
///
/// 쓰는 법: Project 창에서 능력 모션(AbilityMotionData) · 능력(AbilityData) · 무기(EquipmentData)를 고르고
///          Tools → 손가락 끝 보정 (또는 우클릭 → 손가락 끝 보정) → '보정'. Ctrl+Z로 되돌림.
/// </summary>
public class FingerTipFixWindow : EditorWindow
{
    static readonly string[] FingerNames = { "엄지", "검지", "중지", "약지" };

    [SerializeField] float _ratio = 0.7f;
    [SerializeField] bool _onlyUntouched = true;
    [SerializeField] float _untouchedDeg = 3f;
    [SerializeField] bool[] _fingers = { false, true, true, true };
    [SerializeField] bool _right = true, _left = true, _grips = true;
    Vector2 _scroll;
    string _last;

    [MenuItem("Tools/손가락 끝 보정")]
    static void Open() => GetWindow<FingerTipFixWindow>("손가락 끝 보정");

    [MenuItem("Assets/손가락 끝 보정", false, 2000)]
    static void OpenFromAssets() => Open();

    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "고른 모션의 손 모양에서 끝 마디를 바로 앞 마디가 굽은 만큼 따라 굽힘.\n" +
            "Project 창에서 능력 모션 · 능력 · 무기를 골라(여러 개 가능) '보정'. Ctrl+Z로 되돌림.",
            MessageType.None);

        var targets = Collect(out int stepCount);
        EditorGUILayout.LabelField($"고른 것: {targets.Count}개  ·  모션 스텝 {stepCount}개", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(110));
        foreach (var t in targets) EditorGUILayout.LabelField("  • " + t.name, EditorStyles.miniLabel);
        if (targets.Count == 0) EditorGUILayout.LabelField("  (Project 창에서 모션 · 능력 · 무기를 고르세요)", EditorStyles.miniLabel);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        _ratio = EditorGUILayout.Slider(new GUIContent("따라 굽는 비율", "끝 마디 = 앞 마디 굽힘 × 이 값. 사람 손은 약 0.67"), _ratio, 0f, 1.2f);

        _onlyUntouched = EditorGUILayout.Toggle(new GUIContent("손 안 댄 끝 마디만", "이미 직접 굽혀 둔 끝 마디는 그대로 둠"), _onlyUntouched);
        using (new EditorGUI.DisabledScope(!_onlyUntouched))
            _untouchedDeg = EditorGUILayout.Slider(new GUIContent("  '안 댐' 기준 (°)", "끝 마디가 기본 자세에서 이 각도 이하로만 돌아가 있으면 '안 댄 것'"), _untouchedDeg, 0.5f, 20f);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("손가락", GUILayout.Width(60));
        for (int f = 0; f < FingerNames.Length; f++)
            _fingers[f] = GUILayout.Toggle(_fingers[f], FingerNames[f], EditorStyles.miniButton);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("손", GUILayout.Width(60));
        _right = GUILayout.Toggle(_right, "오른손", EditorStyles.miniButton);
        _left = GUILayout.Toggle(_left, "왼손", EditorStyles.miniButton);
        _grips = GUILayout.Toggle(_grips, "무기 쥐는 손(Grip)", EditorStyles.miniButton);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(targets.Count == 0))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("세어 보기", GUILayout.Height(26))) _last = Run(targets, dryRun: true);
            if (GUILayout.Button("보정", GUILayout.Height(26))) _last = Run(targets, dryRun: false);
            EditorGUILayout.EndHorizontal();
        }

        if (!string.IsNullOrEmpty(_last)) EditorGUILayout.HelpBox(_last, MessageType.Info);
    }

    // ══════════════════ 대상 ══════════════════

    /// <summary>고른 에셋 → 손 모양을 가진 것들 (능력이면 스킬 모션들)</summary>
    static List<Object> Collect(out int stepCount)
    {
        var list = new List<Object>();
        stepCount = 0;
        foreach (var o in Selection.objects)
        {
            if (o is AbilityData ad)
            {
                if (ad.Skills == null) continue;
                foreach (var s in ad.Skills)
                    if (s != null && s.MotionData != null && !list.Contains(s.MotionData)) list.Add(s.MotionData);
            }
            else if (o is IMotionSource && !list.Contains(o)) list.Add(o);
        }
        foreach (var o in list)
        {
            var steps = ((IMotionSource)o).GetSteps();
            if (steps != null) stepCount += steps.Length;
        }
        return list;
    }

    // ══════════════════ 보정 ══════════════════

    string Run(List<Object> targets, bool dryRun)
    {
        int changed = 0, hands = 0, skipped = 0;

        if (!dryRun) Undo.RecordObjects(targets.ToArray(), "Finger Tip Fix");

        foreach (var o in targets)
        {
            var steps = ((IMotionSource)o).GetSteps();
            if (steps != null)
                foreach (var step in steps)
                {
                    if (step?.Motion?.Keys == null) continue;
                    foreach (var key in step.Motion.Keys)
                    {
                        if (key?.Pose == null) continue;
                        if (_right) Fix(key.Pose.RightFingers, dryRun, ref changed, ref hands, ref skipped);
                        if (_left) Fix(key.Pose.LeftFingers, dryRun, ref changed, ref hands, ref skipped);
                    }
                }

            if (_grips && o is EquipmentData eq)
            {
                Fix(eq.RightGrip, dryRun, ref changed, ref hands, ref skipped);
                Fix(eq.LeftGrip, dryRun, ref changed, ref hands, ref skipped);
                Fix(eq.StringHandPose, dryRun, ref changed, ref hands, ref skipped);
            }

            if (!dryRun) EditorUtility.SetDirty(o);
        }

        if (!dryRun) AssetDatabase.SaveAssets();
        return dryRun
            ? $"손 모양 {hands}개 · 바꿀 끝 마디 {changed}개 · 건너뜀 {skipped}개 (이미 굽혀 둔 것 · 앞 마디가 안 굽은 것)"
            : $"보정함 — 손 모양 {hands}개 · 끝 마디 {changed}개 (건너뜀 {skipped}개). 되돌리기 Ctrl+Z";
    }

    void Fix(HandPose hp, bool dryRun, ref int changed, ref int hands, ref int skipped)
    {
        if (hp == null) return;
        hp.EnsureValid();
        hands++;

        for (int f = 0; f < HandPose.FingerCount && f < _fingers.Length; f++)
        {
            if (!_fingers[f]) continue;
            var fp = hp.Get(f);
            if (fp?.Joints == null || fp.Joints.Length < 2) continue;

            int tip = fp.Joints.Length - 1;
            Quaternion cur = Norm(fp.Joints[tip]);
            Quaternion mid = Norm(fp.Joints[tip - 1]);

            if (_onlyUntouched && Quaternion.Angle(cur, Quaternion.identity) > _untouchedDeg) { skipped++; continue; }
            if (Quaternion.Angle(mid, Quaternion.identity) < 1f) { skipped++; continue; }    // 앞 마디가 안 굽음 → 펴진 손

            if (!dryRun) fp.Joints[tip] = Quaternion.SlerpUnclamped(Quaternion.identity, mid, _ratio);
            changed++;
        }
    }

    /// <summary>저장된 값이 (0,0,0,0) 같은 잘못된 회전이면 기본으로</summary>
    static Quaternion Norm(Quaternion q)
    {
        float m = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        if (m < 1e-6f) return Quaternion.identity;
        return Quaternion.Normalize(q);
    }
}
