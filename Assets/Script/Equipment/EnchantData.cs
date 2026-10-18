using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enchant")]
public class EnchantData : ScriptableObject
{
    [Header("Identity")]
    public int Id;
    public string DisplayName;
    [TextArea(1, 2)] public string Description;

    [Tooltip("툴팁·이펙트 색")]
    public Color TierColor = Color.white;

    [Header("효과")]
    public EquipmentData.StatEntry[] Stats;

    [Header("추첨")]
    [Tooltip("클수록 자주 나옴")]
    public float RollWeight = 1f;

    public string BuildLine()
    {
        if (Stats == null || Stats.Length == 0) return DisplayName;

        var sb = new System.Text.StringBuilder(DisplayName);
        sb.Append("  ");
        foreach (var s in Stats)
            sb.Append($"{s.Stat} {StatText.Value(s.Stat, s.Type, s.Value)} ");
        return sb.ToString();
    }
}