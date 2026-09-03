using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability")]
public class AbilityData : ScriptableObject
{
    [Header("Identity")]
    public int Id;
    public string DisplayName;
    public AbilityKind Kind = AbilityKind.Main;
    public SubTier Tier = SubTier.Silver;   // Sub만 사용
    public AbilityTag[] Tags;

    [Header("Presentation")]
    public Sprite Icon;
    [TextArea(2, 3)]
    [Tooltip("능력 자체의 플레이버 텍스트")]
    public string Flavor;

    [Header("Content")]
    public PassiveDef[] Passives;
    public SkillDef[] Skills;

    // ─────────────────────────────

    [System.Serializable]
    public class PassiveDef
    {
        public string Name;
        public Sprite Icon;
        [TextArea(2, 4)]
        [Tooltip("{0} {1} 로 EffectDef의 Value를 삽입 가능")]
        public string Description;
        public EffectDef[] Effects;
    }

    [System.Serializable]
    public class SkillDef
    {
        public string Name;
        public Sprite Icon;
        [TextArea(2, 4)] public string Description;

        [Header("Cost / Timing")]
        public float Cooldown = 8f;
        public int SoulCost = 0;
        public float CastTime = 0f;
        public KeyCode Hotkey = KeyCode.Q;

        public EffectDef[] Effects;
    }

    [System.Serializable]
    public class EffectDef
    {
        public TriggerType Trigger = TriggerType.Passive;
        public EffectType Effect;
        public TargetType Target = TargetType.Self;

        [Header("Values")]
        [Tooltip("주 수치. 레벨별로 여러 개 넣으면 랭크 구현")]
        public float[] Values = new float[] { 0f };
        public float Duration = 0f;
        [Range(0f, 1f)] public float Chance = 1f;

        [Header("StatMod 전용")]
        public StatType Stat;
        public ModType ModType;

        [Header("Trigger 옵션")]
        [Tooltip("대상별 재발동 쿨타임 (0이면 없음)")]
        public float PerTargetCooldown = 0f;
        [Tooltip("Periodic일 때 주기(초)")]
        public float Interval = 1f;

        public float GetValue(int rank = 0) =>
            Values == null || Values.Length == 0
                ? 0f
                : Values[Mathf.Clamp(rank, 0, Values.Length - 1)];
    }

    /// <summary>설명 문자열의 {0}, {1}을 실제 수치로 치환</summary>
    public static string Format(string desc, EffectDef[] effects, int rank = 0)
    {
        if (string.IsNullOrEmpty(desc) || effects == null) return desc;
        object[] args = new object[effects.Length];
        for (int i = 0; i < effects.Length; i++)
            args[i] = effects[i].GetValue(rank);
        try { return string.Format(desc, args); }
        catch { return desc; }
    }
}