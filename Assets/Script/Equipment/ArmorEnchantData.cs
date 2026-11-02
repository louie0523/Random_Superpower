using System.Text;
using UnityEngine;

/// <summary>어느 방어구 칸에 붙을 수 있나</summary>
[System.Flags]
public enum ArmorSlotMask
{
    None = 0,
    Armor = 1 << 0,
    Helmet = 1 << 1,
    Boots = 1 << 2,
    All = Armor | Helmet | Boots,
}

/// <summary>
/// 방어구 특수 인챈트 (방어구 하나에 1칸). 상자 · 암시장에서 나온 방어구에 확률로 붙어 있음.
/// 효과는 아이템 고유 특성과 같은 방식 (ItemTrait — 언제 + 무엇을)이라 이상한 조합도 데이터로 만들 수 있음.
/// 예) 가시 갑옷: 피해를 받으면 때린 사람에게 공격력 40% 추가 피해 (재사용 1초)
///     유령 발걸음: 체력 30% 아래로 떨어지면 자신에게 고스트 1.5초 (재사용 40초)
/// </summary>
[CreateAssetMenu(menuName = "Game/Armor Enchant")]
public class ArmorEnchantData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("9800번대 권장 — 일반 · 무기 특수 인챈트와 겹치지 않게")]
    public int Id;
    public string DisplayName;
    [Tooltip("번역 키 접두 — 비우면 armorench.{Id} (.name · .desc)")]
    public string KeyPrefix;
    public Color Color = new(0.55f, 0.85f, 1f);
    public Sprite Icon;
    [TextArea(2, 4)] public string Description;

    [Header("나오는 곳")]
    public ArmorSlotMask Slots = ArmorSlotMask.All;
    [Tooltip("뽑힐 가중치 — 0이면 안 나옴 (암시장 · 개발 명령으로만)")]
    [Min(0f)] public float Weight = 1f;

    [Header("효과")]
    public ItemTrait[] Traits;

    public bool Fits(EquipSlot s) => s switch
    {
        EquipSlot.Armor => (Slots & ArmorSlotMask.Armor) != 0,
        EquipSlot.Helmet => (Slots & ArmorSlotMask.Helmet) != 0,
        EquipSlot.Boots => (Slots & ArmorSlotMask.Boots) != 0,
        _ => false,
    };

    string Prefix => string.IsNullOrEmpty(KeyPrefix) ? $"armorench.{Id}" : KeyPrefix;

    public string GetName() => LocUtil.GetOr(Prefix + ".name", string.IsNullOrEmpty(DisplayName) ? name : DisplayName);

    /// <summary>툴팁 — 이름 줄 + 설명(있으면) + 효과 줄</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(Color)).Append(">◆ ").Append(GetName()).AppendLine("</color>");
        string desc = LocUtil.GetOr(Prefix + ".desc", Description);
        if (!string.IsNullOrWhiteSpace(desc)) sb.AppendLine(desc);
        else if (Traits != null)
            foreach (var t in Traits) if (t != null) sb.Append("  ").AppendLine(t.Describe());
        return sb.ToString().TrimEnd();
    }
}
