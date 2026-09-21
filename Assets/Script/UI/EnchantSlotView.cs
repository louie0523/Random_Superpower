using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EnchantSlotView : MonoBehaviour
{
    [SerializeField] private TMP_Text stageLabel;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image background;
    [SerializeField] private Color emptyColor = new(1f, 1f, 1f, 0.1f);
    [SerializeField] private TooltipTrigger tooltip;

    public void Bind(int stage, EnchantData data)
    {
        if (stageLabel != null) stageLabel.text = stage.ToString();

        if (data == null)
        {
            if (nameText != null) nameText.text = LocUtil.Get("ui.enchant.empty_slot");
            if (background != null) background.color = emptyColor;
            if (tooltip != null) tooltip.Provider = null;
            return;
        }

        if (nameText != null)
        {
            nameText.text = LocUtil.GetOr($"enchant.{data.Id}.name", data.DisplayName);
            nameText.color = data.TierColor;
        }

        if (background != null)
        {
            var c = data.TierColor;
            c.a = 0.2f;
            background.color = c;
        }

        if (tooltip != null)
            tooltip.Provider = () => (
                LocUtil.GetOr($"enchant.{data.Id}.name", data.DisplayName),
                BuildStatLines(data),
                LocUtil.GetOr($"enchant.{data.Id}.desc", data.Description)
            );
    }

    static string BuildStatLines(EnchantData data)
    {
        if (data.Stats == null || data.Stats.Length == 0) return "";

        var sb = new System.Text.StringBuilder();

        foreach (var s in data.Stats)
        {
            string statName = LocUtil.Get($"stat.{s.Stat}");
            string val = s.Type == ModType.Percent ? $"+{s.Value:0.#}%" : $"+{s.Value:0.##}";
            sb.AppendLine($"{statName}  {val}");
        }

        return sb.ToString().TrimEnd();
    }
}