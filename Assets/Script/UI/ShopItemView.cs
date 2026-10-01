using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopItemView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private Button buyButton;
    [SerializeField] private LocText buyLabel;
    [SerializeField] private TMP_Text reasonText;
    [SerializeField] private TooltipTrigger tooltip;

    private PlayerShop _shop;
    private int _index;
    private bool _exclusive;
    private ShopItemData _data;

    public void Bind(PlayerShop shop, int index, bool exclusive)
    {
        _shop = shop;
        _index = index;
        _exclusive = exclusive;

        _data = exclusive
            ? shop.GetExclusiveItem(index)
            : shop.Database.GetByIndex(index);

        if (_data == null) return;

        if (icon != null)
        {
            icon.enabled = _data.Icon != null;
            icon.sprite = _data.Icon;
        }

        if (nameText != null)
            nameText.text = LocUtil.GetOr($"shop.{_data.Id}.name", _data.DisplayName);

        buyLabel?.SetKey("ui.shop.buy");

        buyButton?.onClick.RemoveAllListeners();
        buyButton?.onClick.AddListener(OnBuy);

        if (tooltip != null)
            tooltip.Provider = () => (
                LocUtil.GetOr($"shop.{_data.Id}.name", _data.DisplayName),
                LocUtil.GetOr($"shop.{_data.Id}.desc", _data.Description),
                BuildDetail()
            );

        RefreshState();
    }

    public void RefreshState()
    {
        if (_data == null || _shop == null) return;

        bool ok = _exclusive
            ? _shop.CanBuyExclusive(_index, out int price, out string reason)
            : _shop.CanBuy(_index, out price, out reason);

        int lv = _exclusive ? _shop.GetExclusiveLevel(_index) : _shop.GetLevel(_index);

        if (levelText != null)
        {
            bool showLevel = !_data.IsRepeatable;
            levelText.gameObject.SetActive(showLevel);
            if (showLevel) levelText.text = $"{lv} / {_data.MaxLevel}";
        }

        if (priceText != null)
        {
            priceText.gameObject.SetActive(ok);
            if (ok) priceText.text = price.ToString();
        }

        if (reasonText != null)
        {
            reasonText.gameObject.SetActive(!ok);
            if (!ok) reasonText.text = LocUtil.Get(reason);   // reason이 키
        }

        if (buyButton != null) buyButton.interactable = ok;
    }

    void OnBuy()
    {
        if (_exclusive) _shop?.RPC_BuyExclusive(_index);
        else _shop?.RPC_Buy(_index);
    }

    string BuildDetail()
    {
        if (_data == null) return null;

        var sb = new System.Text.StringBuilder();

        switch (_data.Kind)
        {
            case ShopItemKind.Stat:
                int lv = _exclusive ? _shop.GetExclusiveLevel(_index) : _shop.GetLevel(_index);

                for (int i = 0; i < _data.MaxLevel; i++)
                {
                    string mark = (i + 1 <= lv) ? "▣" : "□";
                    sb.AppendLine($"{mark} Lv.{i + 1}  {FormatStat(_data, i + 1)}");
                }
                break;

            case ShopItemKind.InstantHeal:
                sb.AppendLine(LocUtil.Get("ui.shop.heal_amount", _data.HealAmount));
                break;

            case ShopItemKind.RefillAmmo:
                sb.AppendLine(LocUtil.Get("ui.shop.ammo_ratio",
                    Mathf.RoundToInt(_data.AmmoRatio * 100f)));
                break;
        }

        return sb.ToString().TrimEnd();
    }

    static string FormatStat(ShopItemData d, int level)
    {
        float v = d.GetValue(level);
        string statName = LocUtil.Get($"stat.{d.Stat}");

        return d.ModType == ModType.Percent
            ? $"{statName} +{v:0.#}%"
            : $"{statName} +{v:0.##}";
    }
}