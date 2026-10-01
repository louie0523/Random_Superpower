using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EnchantRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text indexText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private GameObject newMark;

    public void Bind(int index, EnchantData data, bool isNew)
    {
        if (data == null) return;

        if (indexText != null) indexText.text = (index + 1).ToString();

        if (nameText != null)
            nameText.text = LocUtil.GetOr($"enchant.{data.Id}.name", data.DisplayName);

        if (statsText != null) statsText.text = BuildStats(data);
        if (newMark != null) newMark.SetActive(isNew);
    }

    static string BuildStats(EnchantData d)
    {
        if (d.Stats == null) return "";

        var sb = new System.Text.StringBuilder();
        foreach (var s in d.Stats)
        {
            string sign = s.Value >= 0 ? "+" : "";
            string val = s.Type == ModType.Percent
                ? $"{sign}{s.Value:0.#}%"
                : $"{sign}{s.Value:0.##}";

            if (sb.Length > 0) sb.Append("   ");
            sb.Append(LocUtil.Get($"stat.{s.Stat}")).Append(' ').Append(val);
        }
        return sb.ToString();
    }
}