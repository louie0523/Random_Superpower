using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 레벨별 수치. [0] = Lv1, [1] = Lv2 … 짧으면 마지막 값 반복. 값이 하나면 모든 레벨 같음.
/// Behaviour 필드로 쓰고 [ForSkill] / [ForPassive]를 붙이면
///   - 능력 데이터 인스펙터의 그 스킬·패시브 탭에 같이 표시됨
///   - Behaviour 코드에서 Lv(필드)로 그 항목의 현재 레벨 값을 읽음
///   - 설명에서 {b:필드이름} / {b:필드이름:all} 로 표시
/// </summary>
[Serializable]
public class LevelValue
{
    public float[] Values = { 0f };

    public LevelValue() { }
    public LevelValue(float v) { Values = new[] { v }; }
    public LevelValue(params float[] v) { Values = v != null && v.Length > 0 ? v : new[] { 0f }; }

    /// <summary>필드 초기값을 숫자로 쓸 수 있게 — public LevelValue Range = 3f;</summary>
    public static implicit operator LevelValue(float v) => new(v);

    public float At(int rank) =>
        Values == null || Values.Length == 0 ? 0f : Values[Mathf.Clamp(rank, 0, Values.Length - 1)];

    public float Base => At(0);
}

/// <summary>이 수치는 indexField(int 필드 이름)가 가리키는 스킬의 레벨을 따름</summary>
[AttributeUsage(AttributeTargets.Field)]
public class ForSkillAttribute : Attribute
{
    public readonly string IndexField;
    public readonly int FixedIndex = -1;
    public readonly string Label;

    public ForSkillAttribute(string indexField, string label = null) { IndexField = indexField; Label = label; }
    public ForSkillAttribute(int index, string label = null) { FixedIndex = index; Label = label; }
}

/// <summary>이 수치는 indexField(int 필드 이름)가 가리키는 패시브의 레벨을 따름</summary>
[AttributeUsage(AttributeTargets.Field)]
public class ForPassiveAttribute : Attribute
{
    public readonly string IndexField;
    public readonly int FixedIndex = -1;
    public readonly string Label;

    public ForPassiveAttribute(string indexField, string label = null) { IndexField = indexField; Label = label; }
    public ForPassiveAttribute(int index, string label = null) { FixedIndex = index; Label = label; }
}

/// <summary>LevelValue 필드가 어느 항목에 묶였는지 읽기 (런타임·에디터 공용)</summary>
public static class LevelBinding
{
    public const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool TryGet(FieldInfo f, object owner, out bool isSkill, out int index, out string label)
    {
        isSkill = false;
        index = -1;
        label = null;
        if (f == null || f.FieldType != typeof(LevelValue)) return false;

        var s = f.GetCustomAttribute<ForSkillAttribute>();
        if (s != null)
        {
            isSkill = true;
            index = s.FixedIndex >= 0 ? s.FixedIndex : ReadInt(owner, s.IndexField);
            label = s.Label;
            return index >= 0;
        }

        var p = f.GetCustomAttribute<ForPassiveAttribute>();
        if (p != null)
        {
            index = p.FixedIndex >= 0 ? p.FixedIndex : ReadInt(owner, p.IndexField);
            label = p.Label;
            return index >= 0;
        }

        return false;
    }

    static int ReadInt(object owner, string field)
    {
        if (owner == null || string.IsNullOrEmpty(field)) return -1;
        var f = owner.GetType().GetField(field, Flags);
        return f != null && f.FieldType == typeof(int) ? (int)f.GetValue(owner) : -1;
    }

    /// <summary>필드 이름 → 사람이 읽는 이름 (라벨이 없을 때)</summary>
    public static string Nice(FieldInfo f, string label) =>
        !string.IsNullOrEmpty(label) ? label :
#if UNITY_EDITOR
        UnityEditor.ObjectNames.NicifyVariableName(f.Name);
#else
        f.Name;
#endif
}

#if UNITY_EDITOR
/// <summary>Behaviour 인스펙터에서 LevelValue를 한 줄로 — [전 레벨] 또는 Lv1 Lv2 … + / −</summary>
[UnityEditor.CustomPropertyDrawer(typeof(LevelValue))]
public class LevelValueDrawer : UnityEditor.PropertyDrawer
{
    public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label) =>
        UnityEditor.EditorGUIUtility.singleLineHeight;

    public override void OnGUI(Rect pos, UnityEditor.SerializedProperty property, GUIContent label)
    {
        var arr = property.FindPropertyRelative("Values");
        if (arr.arraySize == 0) arr.arraySize = 1;

        UnityEditor.EditorGUI.BeginProperty(pos, label, property);
        Rect r = UnityEditor.EditorGUI.PrefixLabel(pos, label);

        int n = arr.arraySize;
        const float btn = 18f;
        float w = (r.width - btn * 2 - 4) / n;

        int old = UnityEditor.EditorGUI.indentLevel;
        UnityEditor.EditorGUI.indentLevel = 0;
        for (int i = 0; i < n; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            e.floatValue = UnityEditor.EditorGUI.FloatField(new Rect(r.x + i * w, r.y, w - 2, r.height), e.floatValue);
        }
        UnityEditor.EditorGUI.indentLevel = old;

        float bx = r.xMax - btn * 2;
        if (GUI.Button(new Rect(bx, r.y, btn, r.height), new GUIContent("+", "레벨 칸 추가 (마지막 값 복사)"), UnityEditor.EditorStyles.miniButtonLeft))
            arr.arraySize++;
        using (new UnityEditor.EditorGUI.DisabledScope(n <= 1))
            if (GUI.Button(new Rect(bx + btn, r.y, btn, r.height), new GUIContent("−", "마지막 칸 삭제"), UnityEditor.EditorStyles.miniButtonRight))
                arr.arraySize--;

        UnityEditor.EditorGUI.EndProperty();
    }
}
#endif
