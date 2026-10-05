using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>순서가 저장값 — 뒤에만 추가</summary>
public enum SpecialEffectKind
{
    ApplyStatus,        // 상태이상 부여 (중독은 MaxStacks까지 중첩)
    MaxHpDamage,        // 대상 최대 체력 N% 추가 피해
    LifestealToDamage,  // 이 무기 흡혈 불가, 흡혈량의 N%를 피해로 (혈격)
    Lifesteal,          // 이 무기로 준 피해의 N%만큼 추가 흡혈 (탐식)
    Execute,            // 대상 체력 Value% 이하면 이 타격 피해 +Value2% (처형)
    DefensePen,         // 이 타격은 대상 방어력 N% 무시 (관통)
}

[System.Serializable]
public class SpecialEffect
{
    public SpecialEffectKind Kind = SpecialEffectKind.ApplyStatus;

    [Tooltip("ApplyStatus일 때 부여할 상태이상")]
    public StatusType Status;

    [Tooltip("ApplyStatus: 둔화 15 = 15%, 화상 2 = 초당 최대체력 2%, 중독 0.25 = 중첩당 초당 0.25%\n" +
             "MaxHpDamage: 7 = 대상 최대 체력의 7%\n" +
             "LifestealToDamage: 100 = 흡혈량의 100%를 피해로\n" +
             "Lifesteal: 10 = 준 피해의 10% 회복\n" +
             "Execute: 30 = 대상 체력 30% 이하일 때\n" +
             "DefensePen: 20 = 방어력 20% 무시")]
    public float Value = 15f;

    [Tooltip("Execute: 피해 증가 % (30 = +30%)")]
    public float Value2 = 30f;

    [Tooltip("ApplyStatus 지속 (초)")]
    public float Duration = 2f;

    [Tooltip("발동 확률. 1 = 항상, 0.15 = 15%. 0이면 1로 취급")]
    [Range(0f, 1f)] public float Chance = 1f;

    [Tooltip("ApplyStatus 중독: 최대 중첩. 다른 상태이상은 무시")]
    [Min(1)] public int MaxStacks = 1;

    [Tooltip("MaxHpDamage · LifestealToDamage: 방어력을 무시하는 고정 피해로")]
    public bool IgnoreDefense = true;

    public float GetChance() => Chance <= 0f ? 1f : Chance;
    public int GetMaxStacks() => Mathf.Max(1, MaxStacks);
}

[System.Serializable]
public class SpecialTier
{
    [Tooltip("인스펙터 표시용 이름표 (I, II …). 게임에는 안 나옴")]
    public string Label;

    [Tooltip("이 등급만 다른 설명이 필요할 때. 비우면 에셋 공통 Description 사용")]
    [TextArea(1, 3)] public string Description;

    public SpecialEffect[] Effects;
}

/// <summary>
/// 특수 인챈트 하나 (둔화, 화상, 중독, 감전, 처형, 관통, 탐식, 혈격 …).
/// 등급 I, II, … 마다 효과 목록. 무기에는 종류별로 하나만 붙고, 같은 종류가 다시 나오면 등급이 오름.
/// </summary>
[CreateAssetMenu(menuName = "Game/Special Enchant")]
public class SpecialEnchantData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("일반 인챈트와 별개 번호. 9500번대 권장")]
    public int Id;
    public string DisplayName;
    public Color Color = new(1f, 0.6f, 0.2f);
    public Sprite Icon;

    [Tooltip("모든 등급 공통 설명. 숫자 자리 (N = 효과 번호):\n" +
             "{vN} 수치  {dN} 지속(초)  {cN} 확률(%)  {sN} 최대 중첩  {xN} 두 번째 수치\n" +
             "예) {d0}초간 대상의 이동속도를 {v0}% 둔화시킵니다.")]
    [TextArea(2, 4)] public string Description;

    [Header("등급 — [0] = I, [1] = II …")]
    public SpecialTier[] Tiers;

    public int MaxTier => Mathf.Min(Tiers?.Length ?? 0, EnchantCode.MaxTier);

    public SpecialTier GetTier(int tier) =>
        Tiers != null && tier >= 1 && tier <= Tiers.Length ? Tiers[tier - 1] : null;

    // ══════════════════ 표시 ══════════════════

    public string BaseName => LocUtil.GetOr($"special.{Id}.name", LocUtil.Resolve(DisplayName));

    /// <summary>"둔화 II"</summary>
    public string Name(int tier) => $"{BaseName} {Roman(tier)}";

    public string ColoredName(int tier) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(Color)}>{Name(tier)}</color>";

    static readonly Regex Token = new(@"\{([vdcsx])(\d+)\}", RegexOptions.Compiled);

    /// <summary>등급 설명 — 등급 전용 → 공통 → 자동 순서. 숫자 자리를 그 등급 값으로 채움</summary>
    public string Describe(int tier)
    {
        var t = GetTier(tier);
        if (t == null) return "";

        string text = LocUtil.GetOr($"special.{Id}.tier{tier}", LocUtil.Resolve(t.Description));
        if (string.IsNullOrWhiteSpace(text))
            text = LocUtil.GetOr($"special.{Id}.desc", LocUtil.Resolve(Description));

        if (!string.IsNullOrWhiteSpace(text))
            return FillTokens(text, t);

        var sb = new StringBuilder();
        if (t.Effects != null)
            foreach (var e in t.Effects)
            {
                if (e == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(DescribeEffect(e));
            }
        return sb.ToString();
    }

    static string FillTokens(string text, SpecialTier t) =>
        Token.Replace(text, m =>
        {
            int i = int.Parse(m.Groups[2].Value);
            if (t.Effects == null || i >= t.Effects.Length || t.Effects[i] == null) return m.Value;

            var e = t.Effects[i];
            return m.Groups[1].Value switch
            {
                "v" => e.Value.ToString("0.##"),
                "d" => e.Duration.ToString("0.#"),
                "c" => (e.GetChance() * 100f).ToString("0.#"),
                "s" => e.GetMaxStacks().ToString(),
                "x" => e.Value2.ToString("0.##"),
                _ => m.Value,
            };
        });

    static string DescribeEffect(SpecialEffect e)
    {
        string body = e.Kind switch
        {
            SpecialEffectKind.ApplyStatus => LocUtil.Get("ui.special.status",
                LocUtil.Get($"status.{e.Status}"), StatusValue(e), e.Duration.ToString("0.#")),
            SpecialEffectKind.MaxHpDamage => LocUtil.Get("ui.special.maxhp", e.Value.ToString("0.#")),
            SpecialEffectKind.LifestealToDamage => LocUtil.Get("ui.special.lifesteal_to_damage", e.Value.ToString("0")),
            SpecialEffectKind.Lifesteal => LocUtil.Get("ui.special.lifesteal", e.Value.ToString("0.#")),
            SpecialEffectKind.Execute => LocUtil.Get("ui.special.execute", e.Value.ToString("0"), e.Value2.ToString("0")),
            SpecialEffectKind.DefensePen => LocUtil.Get("ui.special.defpen", e.Value.ToString("0")),
            _ => "",
        };

        return e.GetChance() < 1f
            ? LocUtil.Get("ui.special.chance", (e.GetChance() * 100f).ToString("0.#")) + " " + body
            : body;
    }

    static string StatusValue(SpecialEffect e) => e.Status switch
    {
        StatusType.Seal or StatusType.Stun => "",
        StatusType.Burn => $"{e.Value:0.##}%/s",
        StatusType.Poison => $"{e.Value:0.##}%/s ×{e.GetMaxStacks()}",
        _ => $"{e.Value:0.#}%",
    };

    static readonly string[] RomanTable = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    public static string Roman(int n) => n >= 1 && n < RomanTable.Length ? RomanTable[n] : n.ToString();
}