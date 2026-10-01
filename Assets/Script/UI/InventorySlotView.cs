using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class InventorySlotView : MonoBehaviour
{
    public struct Model
    {
        public EquipmentData Data;
        public int SlotIndex;
        public bool IsActive;
        public PlayerEnchant Enchant;
        public int Ammo;
    }

    [SerializeField] private Image icon;
    [SerializeField] private Image frame;
    [SerializeField] private TMP_Text slotNumber;
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private GameObject enchantMark;
    [SerializeField] private TMP_Text enchantCount;
    [SerializeField] private Button clickArea;
    [SerializeField] private Button dropButton;
    [SerializeField] private LocText emptyLabel;
    [SerializeField] private TooltipTrigger tooltip;

    [Header("색")]
    [SerializeField] private Color activeColor = new(1f, 0.85f, 0.3f);
    [SerializeField] private Color normalColor = new(1f, 1f, 1f, 0.4f);

    public System.Action OnClicked;
    public System.Action OnDropClicked;

    private Model _model;

    void Awake()
    {
        emptyLabel?.SetKey("ui.inventory.empty");

        clickArea?.onClick.AddListener(() => OnClicked?.Invoke());
        dropButton?.onClick.AddListener(() => OnDropClicked?.Invoke());
    }

    public void Bind(Model m)
    {
        _model = m;

        bool hasItem = m.Data != null;

        if (icon != null)
        {
            icon.enabled = hasItem && m.Data.Icon != null;
            if (hasItem) icon.sprite = m.Data.Icon;
        }

        if (emptyLabel != null) emptyLabel.gameObject.SetActive(!hasItem);

        if (slotNumber != null)
        {
            slotNumber.gameObject.SetActive(m.SlotIndex >= 0);
            if (m.SlotIndex >= 0) slotNumber.text = (m.SlotIndex + 1).ToString();
        }

        if (frame != null)
            frame.color = m.IsActive ? activeColor : normalColor;

        // 탄약
        if (ammoText != null)
        {
            bool showAmmo = hasItem && m.Ammo >= 0
                && m.Data.Class == WeaponClass.Ranged
                && m.Data.RangedKind == RangedType.Gun;

            ammoText.gameObject.SetActive(showAmmo);
            if (showAmmo) ammoText.text = $"{m.Ammo} / {m.Data.MaxAmmo}";
        }

        // 인챈트 개수
        int stage = 0;
        if (hasItem && m.Enchant != null && m.SlotIndex >= 0)
            stage = m.Enchant.GetStage(m.SlotIndex);

        if (enchantMark != null) enchantMark.SetActive(stage > 0);
        if (enchantCount != null) enchantCount.text = $"+{stage}";

        if (dropButton != null)
            dropButton.gameObject.SetActive(hasItem && m.IsActive);

        // 툴팁
        if (tooltip != null)
            tooltip.Provider = hasItem ? BuildTooltip : (System.Func<TooltipView.Content>)null;
    }

    TooltipView.Content BuildTooltip()
    {
        var d = _model.Data;

        return new TooltipView.Content
        {
            Title = LocUtil.GetOr($"weapon.{d.Id}.name", d.DisplayName),
            Body = BuildBody(d),
            Detail = BuildDetail(d),
            Tags = BuildTags(d),
        };
    }

    string[] BuildTags(EquipmentData d)
    {
        var tags = new List<string>();

        // 종류
        if (d.Slot == EquipSlot.Weapon)
        {
            tags.Add(d.Class == WeaponClass.Melee
                ? LocUtil.Get("ui.item.class.Melee")
                : LocUtil.Get($"ui.item.ranged_kind.{d.RangedKind}"));
        }
        else
        {
            tags.Add(LocUtil.Get($"ui.item.slot.{d.Slot}"));
        }

        // 인챈트 가능 여부
        tags.Add(LocUtil.Get(d.CanEnchant
            ? "ui.item.tag.enchantable"
            : "ui.item.tag.no_enchant"));

        // 에셋에 적어둔 커스텀 태그
        if (d.Tags != null)
            foreach (var key in d.Tags)
                if (!string.IsNullOrWhiteSpace(key))
                    tags.Add(LocUtil.Get("tag." + key));

        return tags.ToArray();
    }

    string BuildBody(EquipmentData d)
    {
        var sb = new System.Text.StringBuilder();

        // 기본 스탯
        if (d.BaseStats != null)
            foreach (var s in d.BaseStats)
            {
                string statName = LocUtil.Get($"stat.{s.Stat}");
                string val = s.Type == ModType.Percent ? $"+{s.Value:0.#}%" : $"+{s.Value:0.##}";
                sb.AppendLine($"{statName}  {val}");
            }

        // 무기 정보
        if (d.Slot == EquipSlot.Weapon)
        {
            sb.AppendLine();

            if (d.Class == WeaponClass.Melee)
            {
                sb.AppendLine(LocUtil.Get("ui.item.melee_range", d.Range.ToString("0.#")));
                //sb.AppendLine(LocUtil.Get("ui.item.hit_angle", d.HitAngle.ToString("0")));
            }
            else
            {

                if (d.RangedKind == RangedType.Gun)
                {
                    sb.AppendLine(LocUtil.Get("ui.item.ammo", d.MaxAmmo));
                    sb.AppendLine(LocUtil.Get("ui.item.rpm", d.RoundsPerMinute.ToString("0")));
                }
                else
                {
                    sb.AppendLine(LocUtil.Get("ui.item.charge_time",
                        d.MinDrawTime.ToString("0.##"), d.MaxDrawTime.ToString("0.##")));
                    sb.AppendLine(LocUtil.Get("ui.item.charge_damage",
                        (d.MinDrawDamageMul * 100f).ToString("0"),
                        (d.MaxDrawDamageMul * 100f).ToString("0")));
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    string BuildDetail(EquipmentData d)
    {
        var sb = new System.Text.StringBuilder();

        // 설명
        string desc = LocUtil.GetOr($"item.{d.Id}.desc", d.Description);
        if (!string.IsNullOrWhiteSpace(desc) && !desc.StartsWith("#"))
        {
            sb.AppendLine(desc);
            sb.AppendLine();
        }

        // 인챈트 목록
        if (_model.Enchant != null && _model.SlotIndex >= 0)
        {
            int stage = _model.Enchant.GetStage(_model.SlotIndex);

            if (stage > 0)
            {
                sb.AppendLine(LocUtil.Get("ui.item.enchants"));

                var db = _model.Enchant.Database;
                for (int i = 0; i < stage; i++)
                {
                    int id = _model.Enchant.GetEnchantId(_model.SlotIndex, i);
                    var e = db?.Get(id);
                    if (e == null) continue;

                    string ename = LocUtil.GetOr($"enchant.{e.Id}.name", e.DisplayName);
                    sb.Append($"  {i + 1}. {ename}");

                    if (e.Stats != null)
                        foreach (var s in e.Stats)
                        {
                            string sign = s.Value >= 0 ? "+" : "";
                            string val = s.Type == ModType.Percent
                                ? $"{sign}{s.Value:0.#}%"
                                : $"{sign}{s.Value:0.##}";
                            sb.Append($"  {LocUtil.Get($"stat.{s.Stat}")} {val}");
                        }

                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().TrimEnd();
    }
}