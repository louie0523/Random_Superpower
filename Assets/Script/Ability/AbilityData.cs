using System.Collections.Generic;
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

    [Header("Custom Logic")]
    [Tooltip("전용 로직이 필요한 능력만. 단순 능력은 비워두세요")]
    public AbilityBehaviour Behaviour;

    [Header("배정 규칙")]
    [Tooltip("한 게임에 동시에 존재 가능한 인원. 0이면 무제한")]
    public int MaxConcurrent = 0;

    [Tooltip("체크하면 랜덤 배정에서 제외 (진화 전용 등)")]
    public bool ExcludeFromRoll = false;

    [Tooltip("추첨 가중치. 클수록 자주 나옴")]
    public float RollWeight = 1f;

    [Header("진화")]
    [Tooltip("이 능력으로 진화할 대상")]
    public int EvolveToId = 0;

    [Tooltip("진화 조건: 막타 횟수")]
    public int EvolveKillCount = 0;

    [System.Serializable]
    public struct ChargeInfo
    {
        [Range(0, 3)] public int Slot;
        public string DisplayName;
        public Color BarColor;
        public float Max;
    }

    [Header("게이지")]
    public ChargeInfo[] Charges;

    // ─────────────────────────────

    [System.Serializable]
    public class PassiveDef
    {
        public string Name;
        public Sprite Icon;
        [TextArea(2, 4)] public string Description;
        [TextArea(2, 5)] public string DetailedInfo;
        [TextArea(1, 3)] public string Tip;
        [TextArea(1, 2)] public string DevNote;

        public EffectDef[] Effects;
    }

    public enum SkillEffectTiming
    {
        OnHitFrame,     // 판정 시점 (공격형)
        OnCastEnd,      // 모션 종료 후 (버프형)
        Immediate,      // 즉시 (모션과 무관)
    }

    [System.Serializable]
    public class SkillDef   
    {
        public string Name;
        public Sprite Icon;


        [TextArea(2, 4)]
        [Tooltip("인게임 표시용 짧은 설명. {0} {1}로 Effects의 Value 삽입")]
        public string Description;

        [TextArea(2, 5)]
        [Tooltip("상세 설명 — 판정 범위, 예외 상황, 상호작용 등")]
        public string DetailedInfo;

        [TextArea(1, 3)]
        [Tooltip("플레이 팁 — 어떻게 써야 좋은지")]
        public string Tip;

        [TextArea(1, 2)]
        [Tooltip("개발 메모 — 인게임에 노출 안 됨. 밸런스 의도 등")]
        public string DevNote;

        [Header("Slot")]
        public SkillSlot Slot = SkillSlot.Skill1;

        [Header("Cost / Timing")]
        public float Cooldown = 8f;
        public int SoulCost = 0;

        [Tooltip("궁극기는 소울을 모아야 쓸 수 있게 할 때")]
        public int UltimateChargeCost = 0;

        [Header("Motion")]
        [Tooltip("시전 모션. 비우면 즉시 발동")]
        public AbilityMotionData MotionData;

        public ComboStep Motion => MotionData?.Step;

        [Header("Timing")]
        public SkillEffectTiming EffectTiming = SkillEffectTiming.OnHitFrame;



        [Header("Hit")]
        [Tooltip("적중 판정이 필요한 스킬인가 (버프류는 false)")]
        public bool NeedsTarget = false;
        public WeaponHitMode HitMode = WeaponHitMode.ForwardArc;
        public float Range = 6f;
        [Range(20f, 360f)] public float HitAngle = 60f;
        public float HitHeight = 1.5f;
        public int MaxTargets = 1;

        public EffectDef[] Effects;
    }

    [System.Serializable]
    public class EffectDef
    {
        public TriggerType Trigger = TriggerType.Passive;
        public EffectType Effect;
        public TargetType Target = TargetType.Self;

        [Header("조건")]
        [Tooltip("이 상태일 때만 발동 (0 = 조건 없음)")]
        public int RequireStateId = 0;

        [Header("Charge 전용")]
        [Tooltip("게이지 슬롯 번호 (0~3)")]
        [Range(0, 3)] public int ChargeSlot = 0;

        [Tooltip("소모할 게이지 슬롯")]
        [Range(0, 3)] public int CostChargeSlot = 0;
        public float ChargeCost = 0f;

        [Header("Values")]
        [Tooltip("배열에 추가시 랭크에 따라 수치 변경\n" +
            "일반 스탯: Flat = 실수치(+25), Percent = 퍼센트(25 = +25%)\n" +
         "배율 스탯(피해 계열): Flat = 합연산(25 = +25%p), Percent = 곱연산(25 = ×1.25)\n" +
         "비율 스탯(치확/쿨감/흡혈): Flat만, 0~1 단위(0.1 = 10%)")]
        public float[] Values = new float[] { 0f };
        public float Duration = 0f;
        [Range(0f, 1f)] 
        public float Chance = 1f;

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

    /// <summary>스킬 툴팁 전체 텍스트 (수치 치환 포함)</summary>
    public static string BuildTooltip(SkillDef s, bool includeTip = true)
    {
        if (s == null) return "";

        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"<b>{s.Name}</b>  <color=#888>[{s.Slot.Label()}]</color>");

        // 코스트/쿨타임
        var cost = new List<string>();
        if (s.Cooldown > 0f) cost.Add($"쿨타임 {s.Cooldown:0.#}초");
        if (s.SoulCost > 0) cost.Add($"소울 {s.SoulCost}");
        if (cost.Count > 0)
            sb.AppendLine($"<color=#7ac>{string.Join("  ·  ", cost)}</color>");

        sb.AppendLine();
        sb.AppendLine(Format(s.Description, s.Effects));

        if (!string.IsNullOrWhiteSpace(s.DetailedInfo))
        {
            sb.AppendLine();
            sb.AppendLine($"<color=#aaa>{Format(s.DetailedInfo, s.Effects)}</color>");
        }

        if (includeTip && !string.IsNullOrWhiteSpace(s.Tip))
        {
            sb.AppendLine();
            sb.AppendLine($"<color=#dc8><i>팁: {s.Tip}</i></color>");
        }

        return sb.ToString();
    }

    /// <summary>판정 범위를 자동으로 문장화</summary>
    public static string DescribeRange(SkillDef s)
    {
        if (!s.NeedsTarget) return "대상 지정 없음 (자신에게 적용)";

        string shape = s.HitMode == WeaponHitMode.ForwardArc
            ? $"전방 {s.HitAngle:0}° 부채꼴"
            : "주변 전체";

        return $"{shape}, 사거리 {s.Range:0.#}m, 최대 {s.MaxTargets}명";
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        FixEffects(Passives);
        FixEffects(Skills);
    }

    void FixEffects(PassiveDef[] arr)
    {
        if (arr == null) return;
        foreach (var p in arr) FixChance(p?.Effects);
    }

    void FixEffects(SkillDef[] arr)
    {
        if (arr == null) return;
        foreach (var s in arr) FixChance(s?.Effects);
    }

    static void FixChance(EffectDef[] effects)
    {
        if (effects == null) return;
        foreach (var e in effects)
            if (e != null && e.Chance <= 0f) e.Chance = 1f;
    }
#endif
}