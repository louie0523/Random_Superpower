using System.Text;
using UnityEngine;

public enum SpecialEffectKind
{
    ApplyStatus,        // 상태이상 부여
    MaxHpDamage,        // 대상 최대 체력 N% 추가 피해
    LifestealToDamage,  // 이 무기 흡혈 불가, 흡혈량의 N%를 피해로
}

[System.Serializable]
public class SpecialEffect
{
    public SpecialEffectKind Kind = SpecialEffectKind.ApplyStatus;

    [Tooltip("ApplyStatus일 때 부여할 상태이상")]
    public StatusType Status;

    [Tooltip("ApplyStatus: 둔화 15 = 15%, 화상 2 = 초당 최대체력 2% (봉인·기절은 무시)\n" +
             "MaxHpDamage: 7 = 대상 최대 체력의 7%\n" +
             "LifestealToDamage: 100 = 흡혈량의 100%를 피해로")]
    public float Value = 15f;

    [Tooltip("ApplyStatus 지속 (초)")]
    public float Duration = 2f;

    [Tooltip("MaxHpDamage · LifestealToDamage: 방어력을 무시하는 고정 피해로")]
    public bool IgnoreDefense = true;
}

[System.Serializable]
public class SpecialTier
{
    [Tooltip("비우면 효과 목록으로 자동 설명")]
    [TextArea(1, 3)] public string Description;

    public SpecialEffect[] Effects;
}

/// <summary>
/// 특수 인챈트 하나 (둔화, 화상, 서리화상, 심판, 탐식 …).
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

    /// <summary>등급 설명. 직접 쓴 문장이 없으면 효과로 자동 생성</summary>
    public string Describe(int tier)
    {
        var t = GetTier(tier);
        if (t == null) return "";

        string custom = LocUtil.GetOr($"special.{Id}.tier{tier}", LocUtil.Resolve(t.Description));
        if (!string.IsNullOrWhiteSpace(custom)) return custom;

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

    static string DescribeEffect(SpecialEffect e) => e.Kind switch
    {
        SpecialEffectKind.ApplyStatus => LocUtil.Get("ui.special.status",
            LocUtil.Get($"status.{e.Status}"), StatusValue(e), e.Duration.ToString("0.#")),
        SpecialEffectKind.MaxHpDamage => LocUtil.Get("ui.special.maxhp", e.Value.ToString("0.#")),
        SpecialEffectKind.LifestealToDamage => LocUtil.Get("ui.special.lifesteal_to_damage", e.Value.ToString("0")),
        _ => "",
    };

    static string StatusValue(SpecialEffect e) => e.Status switch
    {
        StatusType.Seal or StatusType.Stun => "",
        StatusType.Burn => $"{e.Value:0.#}%/s",
        _ => $"{e.Value:0.#}%",
    };

    static readonly string[] RomanTable = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    public static string Roman(int n) => n >= 1 && n < RomanTable.Length ? RomanTable[n] : n.ToString();
}
