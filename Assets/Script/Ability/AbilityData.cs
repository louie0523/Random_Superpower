using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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

    [Header("전용 상점")]
    [Tooltip("이 능력을 가졌을 때만 상점에 나타나는 항목들")]
    public ShopItemData[] ExclusiveShopItems;

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

    /// <summary>레벨별 수치. Behaviour가 코드에서 읽고, 설명에서 {v0} {v1}로 참조</summary>
    [System.Serializable]
    public class RankVar
    {
        [Tooltip("메모용 이름 (인게임에 표시 안 됨)")]
        public string Name;

        [Tooltip("레벨별 값. [0] = Lv1(기본). 배열이 짧으면 마지막 값 반복")]
        public float[] Values = { 0f };

        public float Get(int rank) =>
            Values == null || Values.Length == 0
                ? 0f
                : Values[Mathf.Clamp(rank, 0, Values.Length - 1)];
    }

    const string TextHelp =
        "{0} {1} … = Effects 값 (현재 레벨)\n" +
        "{0:all} = 모든 레벨 값 (예: 400 / 425 / 450, 현재 레벨 굵게)\n" +
        "{v0} {v1} … = Vars 값,  {v0:all} = Vars 모든 레벨\n" +
        "숫자 서식: {0:0.#}  /  {0:all|0.#}";

    [System.Serializable]
    public class PassiveDef
    {
        [System.NonSerialized] public AbilityData Owner;
        public string Name;
        public Sprite Icon;
        [TextArea(2, 4), Tooltip(TextHelp)] public string Description;
        [TextArea(2, 5), Tooltip(TextHelp)] public string DetailedInfo;
        [TextArea(1, 3)] public string Tip;
        [TextArea(1, 2)] public string DevNote;

        [Header("레벨")]
        [Tooltip("최대 레벨. 1이면 강화 불가.\nEffects의 Values와 Vars의 칸 수를 이 값에 맞추세요")]
        [Min(1)] public int MaxLevel = 3;

        [Tooltip("Behaviour가 읽는 레벨별 수치. 설명에서 {v0} {v1}로 참조")]
        public RankVar[] Vars;

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
        [System.NonSerialized] public AbilityData Owner;
        public string Name;
        public Sprite Icon;

        [TextArea(2, 4)]
        [Tooltip("인게임 표시용 짧은 설명.\n" + TextHelp)]
        public string Description;

        [TextArea(2, 5)]
        [Tooltip("상세 설명 — 판정 범위, 예외 상황, 상호작용 등.\n" + TextHelp)]
        public string DetailedInfo;

        [TextArea(1, 3)]
        [Tooltip("플레이 팁 — 어떻게 써야 좋은지")]
        public string Tip;

        [TextArea(1, 2)]
        [Tooltip("개발 메모 — 인게임에 노출 안 됨. 밸런스 의도 등")]
        public string DevNote;

        [Header("Slot")]
        public SkillSlot Slot = SkillSlot.Skill1;

        [Header("레벨")]
        [Tooltip("최대 레벨. 1이면 강화 불가. 궁극기는 보통 3.\nEffects의 Values와 Vars의 칸 수를 이 값에 맞추세요")]
        [Min(1)] public int MaxLevel = 5;

        [Tooltip("Behaviour가 읽는 레벨별 수치. 설명에서 {v0} {v1}로 참조")]
        public RankVar[] Vars;

        [Header("무기 조건")]
        [Tooltip("이 무기를 들었을 때만 사용 가능. None이면 제한 없음")]
        public WeaponRequirement RequiredWeapon = WeaponRequirement.None;


        [Tooltip("레벨별 쿨타임 (초). [0] = Lv1. 비우면 아래 Cooldown 고정")]
        public float[] CooldownByLevel;

        public float GetCooldown(int rank) =>
            CooldownByLevel == null || CooldownByLevel.Length == 0
                ? Cooldown
                : CooldownByLevel[Mathf.Clamp(rank, 0, CooldownByLevel.Length - 1)];

        [Header("Cost / Timing")]
        public float Cooldown = 8f;
        public int SoulCost = 0;

        [Tooltip("켜면 사용 시 쿨타임이 멈춘 채 대기 → Behaviour가 효과를 끝낼 때(ReleaseCooldown) 쿨타임 시작. 승마·변신 같은 지속형")]
        public bool CooldownAfterEffect;

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

        [Tooltip("무기 판정 스킬 — 맞힌 대상에게 들고 있는 무기의 특수 인챈트 효과 적용")]
        public bool AppliesWeaponEnchants = false;

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

        [Tooltip("이 무기를 들고 있을 때만 발동 (None = 항상). 패시브 StatMod에도 적용")]
        public WeaponRequirement RequireWeapon = WeaponRequirement.None;

        [Header("Charge 전용")]
        [Tooltip("게이지 슬롯 번호 (0~3)")]
        [Range(0, 3)] public int ChargeSlot = 0;

        [Tooltip("소모할 게이지 슬롯")]
        [Range(0, 3)] public int CostChargeSlot = 0;
        public float ChargeCost = 0f;

        [Header("Values")]
        [Tooltip("레벨별 수치. [0] = Lv1(기본), [1] = Lv2 …  짧으면 마지막 값 반복\n" +
    "Flat = 스탯 실제 값에 그대로 더함 / Percent = 퍼센트 숫자\n" +
    "  일반 스탯: Flat 25 = +25, Percent 25 = +25%\n" +
    "  배율 스탯(피해 계열, 기본 1.0): Flat 0.25 = +25%p, Percent 25 = ×1.25\n" +
    "  비율 스탯(치확/치피/쿨감/흡혈): Flat만, 0.1 = 10%\n" +
    "EnterState: 값이 상태 ID라 레벨과 무관하게 [0]만 사용")]
        public float[] Values = new float[] { 0f };
        public float Duration = 0f;
        [Range(0f, 1f)]
        public float Chance = 1f;

        [Header("StatMod 전용")]
        public StatType Stat;
        public ModType ModType;

        [Header("상태이상 전용")]
        [Tooltip("ApplyStatus일 때 부여할 상태이상. Values = 수치(둔화 25 = 25%), Duration = 지속(초)")]
        public StatusType Status;

        [Header("변화 전용")]
        [Tooltip("AddAlter / RemoveAlter 대상 변화")]
        public AlterData Alter;

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

    // ══════════════════ 설명 서식 ══════════════════

    /// <summary>
    /// 레벨별 값을 가진 서식 인자.
    ///   {0}          → 현재 레벨 값
    ///   {0:0.#}      → 현재 레벨 값, 숫자 서식 지정
    ///   {0:all}      → 400 / 425 / 450 (현재 레벨 굵게, 나머지 흐리게)
    ///   {0:all|0.#}  → 위와 같고 숫자 서식 지정
    /// </summary>
    sealed class RankedArg : System.IFormattable
    {
        const string DefaultNum = "0.##";
        const string DimColor = "#8A8A8A";

        readonly float[] _values;
        readonly int _rank;
        readonly int _levels;
        readonly float _scale;

        public RankedArg(float[] values, int rank, int levels, float scale = 1f)
        {
            _values = values;
            _rank = rank;
            _levels = Mathf.Max(1, levels);
            _scale = scale;
        }

        float At(int r) =>
            _values == null || _values.Length == 0
                ? 0f
                : _values[Mathf.Clamp(r, 0, _values.Length - 1)] * _scale;

        public override string ToString() => ToString(null, null);

        public string ToString(string format, System.IFormatProvider provider)
        {
            if (!string.IsNullOrEmpty(format) && format.StartsWith("all"))
            {
                string num = DefaultNum;
                int bar = format.IndexOf('|');
                if (bar >= 0 && bar < format.Length - 1) num = format.Substring(bar + 1);

                // 레벨이 하나뿐이거나 모든 레벨 값이 같으면 하나만
                bool same = true;
                for (int i = 1; i < _levels; i++)
                    if (!Mathf.Approximately(At(i), At(0))) { same = false; break; }

                if (same) return At(0).ToString(num, provider);

                var sb = new StringBuilder();
                for (int i = 0; i < _levels; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    string s = At(i).ToString(num, provider);

                    if (i == _rank) sb.Append("<b>").Append(s).Append("</b>");
                    else sb.Append("<color=").Append(DimColor).Append('>').Append(s).Append("</color>");
                }
                return sb.ToString();
            }

            return At(_rank).ToString(string.IsNullOrEmpty(format) ? DefaultNum : format, provider);
        }
    }

    static readonly Regex VarToken = new(@"\{v(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// 설명 문자열의 자리표시자를 레벨 기준 수치로 치환.
    /// {0}~ = Effects 순서, {v0}~ = Vars 순서.
    /// </summary>
    public static string FormatRanked(string text, EffectDef[] effects, RankVar[] vars,
                                      int rank, int maxLevel)
    {
        if (string.IsNullOrEmpty(text)) return text;

        int e = effects?.Length ?? 0;
        int v = vars?.Length ?? 0;
        if (e + v == 0) return text;

        var args = new object[e + v];
        for (int i = 0; i < e; i++) args[i] = new RankedArg(effects[i]?.Values, rank, maxLevel, PercentScale(effects[i]));
        for (int i = 0; i < v; i++) args[e + i] = new RankedArg(vars[i]?.Values, rank, maxLevel);

        // {v0} → {e+0} 로 번호 변환
        string s = v > 0
            ? VarToken.Replace(text, m => "{" + (e + int.Parse(m.Groups[1].Value)))
            : text;

        try { return string.Format(s, args); }
        catch (System.FormatException ex)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[AbilityData] 서식 오류 (Effects {e}개, Vars {v}개): \"{text}\" — {ex.Message}");
#endif
            return text;
        }
    }

    /// <summary>비율 스탯(치확·치피·쿨감·흡혈)의 Flat 값은 0~1 단위라 설명에선 ×100 (0.2 → 20)</summary>
    static float PercentScale(EffectDef e) =>
        e != null && e.Effect == EffectType.StatMod && e.ModType == ModType.Flat &&
        (e.Stat == StatType.CritChance || e.Stat == StatType.CritDamage ||
         e.Stat == StatType.CooldownReduction || e.Stat == StatType.LifeSteal)
            ? 100f : 1f;

    /// <summary>레벨별 값을 "8 / 7.5 / 7" 형태로 (현재 레벨 굵게)</summary>
    public static string FormatLevels(float[] values, int rank, int levels, string num = "0.#") =>
        new RankedArg(values, rank, levels).ToString("all|" + num, null);

    public static string FormatPassive(PassiveDef p, string text, int rank) =>
      p == null ? text : FormatRanked(AbilityDescTokens.Apply(text, p.Owner, p.Vars, rank, p.MaxLevel),
                                      p.Effects, p.Vars, rank, p.MaxLevel);

    public static string FormatSkill(SkillDef s, string text, int rank) =>
        s == null ? text : FormatRanked(AbilityDescTokens.Apply(text, s.Owner, s.Vars, rank, s.MaxLevel),
                                        s.Effects, s.Vars, rank, s.MaxLevel);

    static readonly Regex BehaviourToken = new(@"\{b:(\w+)(?::([^}]*))?\}", RegexOptions.Compiled);

    /// <summary>
    /// {b:필드} = 현재 레벨 값 · {b:필드:all} = 전 레벨 · {b:필드:0.#} = 서식 지정.
    /// Behaviour의 LevelValue(또는 float·int) 필드를 읽음
    /// </summary>
    public static string ApplyBehaviourTokens(string text, AbilityBehaviour b, int rank, int maxLevel)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("{b:", System.StringComparison.Ordinal) < 0) return text;

        return BehaviourToken.Replace(text, m =>
        {
            var f = b != null ? b.GetType().GetField(m.Groups[1].Value, LevelBinding.Flags) : null;
            object raw = f != null ? f.GetValue(b) : null;

            float[] values = raw switch
            {
                LevelValue lv => lv.Values,
                float x => new[] { x },
                int i => new[] { (float)i },
                _ => null,
            };
            if (values == null) return $"<color=#E05050>[b:{m.Groups[1].Value}?]</color>";

            string fmt = m.Groups[2].Success ? m.Groups[2].Value : null;
            return new RankedArg(values, rank, maxLevel).ToString(fmt, null);
        });
    }

    /// <summary>기존 호출부 호환 — Vars 없이 Effects만</summary>
    public static string Format(string desc, EffectDef[] effects, int rank = 0) =>
        FormatRanked(desc, effects, null, rank, 1);

    /// <summary>스킬 툴팁 전체 텍스트 (수치 치환 포함)</summary>
    public static string BuildTooltip(SkillDef s, int rank = 0, bool includeTip = true)
    {
        if (s == null) return "";

        var sb = new StringBuilder();

        sb.AppendLine($"<b>{s.Name}</b>  <color=#888>[{s.Slot.Label()}]</color>");

        var cost = new List<string>();
        if (s.Cooldown > 0f) cost.Add($"쿨타임 {s.Cooldown:0.#}초");
        if (s.SoulCost > 0) cost.Add($"소울 {s.SoulCost}");
        if (cost.Count > 0)
            sb.AppendLine($"<color=#7ac>{string.Join("  ·  ", cost)}</color>");

        sb.AppendLine();
        sb.AppendLine(FormatSkill(s, s.Description, rank));

        if (!string.IsNullOrWhiteSpace(s.DetailedInfo))
        {
            sb.AppendLine();
            sb.AppendLine($"<color=#aaa>{FormatSkill(s, s.DetailedInfo, rank)}</color>");
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

    void OnEnable() => LinkOwner();

    /// <summary>패시브·스킬이 자기 능력(Behaviour)을 알게 — 설명의 {b:} 토큰용</summary>
    public void LinkOwner()
    {
        if (Passives != null) foreach (var p in Passives) if (p != null) p.Owner = this;
        if (Skills != null) foreach (var s in Skills) if (s != null) s.Owner = this;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        LinkOwner();
        FixEffects(Passives);
        FixEffects(Skills);
    }

    void FixEffects(PassiveDef[] arr)
    {
        if (arr == null) return;
        foreach (var p in arr)
        {
            if (p == null) continue;
            if (p.MaxLevel < 1) p.MaxLevel = 1;
            FixChance(p.Effects);
        }
    }

    void FixEffects(SkillDef[] arr)
    {
        if (arr == null) return;
        foreach (var s in arr)
        {
            if (s == null) continue;
            if (s.MaxLevel < 1) s.MaxLevel = 1;
            FixChance(s.Effects);
        }
    }

    static void FixChance(EffectDef[] effects)
    {
        if (effects == null) return;
        foreach (var e in effects)
            if (e != null && e.Chance <= 0f) e.Chance = 1f;
    }
#endif
}