using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enchant")]
public class EnchantData : ScriptableObject
{
    [Header("Identity")]
    public int Id;
    public string DisplayName;
    [TextArea(1, 2)] public string Description;

    [Tooltip("ÅøÆÁ¡¤ÀÌÆåÆ® »ö")]
    public Color TierColor = Color.white;

    [Header("È¿°ú")]
    public EquipmentData.StatEntry[] Stats;

    [Header("ÃßÃ·")]
    [Tooltip("Å¬¼ö·Ï ÀÚÁÖ ³ª¿È")]
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