using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class InventorySlotView : MonoBehaviour
{
    public struct Model
    {
        public EquipmentData Data;
        public EquipmentDatabase Database;   // 진화 대상 이름 조회용
        public int SlotIndex;                // 장비 칸은 -1
        public bool IsActive;
        public PlayerEnchant Enchant;
        public int Ammo;
        public bool UpgradeReady;            // 진화 버튼 표시
    }

    [SerializeField] private Image icon;
    [SerializeField] private Image frame;
    [SerializeField] private TMP_Text slotNumber;
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private GameObject enchantMark;
    [SerializeField] private TMP_Text enchantCount;
    [SerializeField] private Button clickArea;
    [SerializeField] private Button dropButton;
    [Tooltip("진화 조건을 만족했을 때만 보임")]
    [SerializeField] private Button upgradeButton;
    [SerializeField] private LocText emptyLabel;
    [SerializeField] private TooltipTrigger tooltip;

    [Header("색")]
    [SerializeField] private Color activeColor = new(1f, 0.85f, 0.3f);
    [SerializeField] private Color normalColor = new(1f, 1f, 1f, 0.4f);

    public System.Action OnClicked;
    public System.Action OnDropClicked;
    public System.Action OnUpgradeClicked;

    private Model _model;
    private readonly List<EnchantSummary.Group> _groups = new();

    const string MetColor = "#7FD07F";
    const string DimColor = "#8A8A8A";

    void Awake()
    {
        emptyLabel?.SetKey("ui.inventory.empty");

        clickArea?.onClick.AddListener(() => OnClicked?.Invoke());
        dropButton?.onClick.AddListener(() => OnDropClicked?.Invoke());
        upgradeButton?.onClick.AddListener(() => OnUpgradeClicked?.Invoke());
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

        // 강화 단계
        int stage = 0;
        if (hasItem && m.Enchant != null && m.SlotIndex >= 0)
            stage = m.Enchant.GetStage(m.SlotIndex);

        if (enchantMark != null) enchantMark.SetActive(stage > 0);
        if (enchantCount != null) enchantCount.text = $"+{stage}";

        // 버튼 — 버리기는 무기 칸 전부, 진화는 조건 만족 시
        if (dropButton != null)
            dropButton.gameObject.SetActive(hasItem && m.SlotIndex >= 0);

        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(hasItem && m.SlotIndex >= 0 && m.UpgradeReady);

        if (tooltip != null)
            tooltip.Provider = hasItem ? BuildTooltip : (System.Func<TooltipView.Content>)null;
    }

    // ══════════════════ 툴팁 ══════════════════

    static string KeyPrefix(EquipmentData d) => d.Slot switch
    {
        EquipSlot.Armor => "armor",
        EquipSlot.Helmet => "helmet",
        EquipSlot.Boots => "boots",
        _ => "weapon",
    };

    static string NameOf(EquipmentData d) =>
        LocUtil.GetOr($"{KeyPrefix(d)}.{d.Id}.name", LocUtil.Resolve(d.DisplayName));

    TooltipView.Content BuildTooltip()
    {
        var d = _model.Data;

        return new TooltipView.Content
        {
            Title = NameOf(d),
            Body = BuildBody(d),
            Detail = BuildDetail(d),
            Tags = BuildTags(d),
        };
    }

    string[] BuildTags(EquipmentData d)
    {
        var tags = new List<string>();

        tags.Add(LocUtil.Get($"ui.item.grade.{d.Grade}"));

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

        tags.Add(LocUtil.Get(d.CanEnchant ? "ui.item.tag.enchantable" : "ui.item.tag.no_enchant"));

        if (d.IsSpecial) tags.Add(LocUtil.Get("ui.item.tag.special"));

        if (d.Tags != null)
            foreach (var key in d.Tags)
                if (!string.IsNullOrWhiteSpace(key))
                    tags.Add(LocUtil.Get("tag." + key));

        return tags.ToArray();
    }

    string BuildBody(EquipmentData d)
    {
        var sb = new System.Text.StringBuilder();

        if (d.BaseStats != null)
            foreach (var s in d.BaseStats)
                sb.AppendLine($"{LocUtil.Get($"stat.{s.Stat}")}  {StatText.Value(s.Stat, s.Type, s.Value)}");

        if (d.Slot == EquipSlot.Weapon)
        {
            sb.AppendLine();

            if (d.Class == WeaponClass.Melee)
            {
                sb.AppendLine(LocUtil.Get("ui.item.melee_range", d.Range.ToString("0.#")));
            }
            else if (d.RangedKind == RangedType.Gun)
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

        return sb.ToString().TrimEnd();
    }

    string BuildDetail(EquipmentData d)
    {
        var sb = new System.Text.StringBuilder();

        // 설명
        string desc = LocUtil.GetOr($"{KeyPrefix(d)}.{d.Id}.desc", LocUtil.Resolve(d.Description));
        if (!string.IsNullOrWhiteSpace(desc) && !desc.StartsWith("#"))
        {
            sb.AppendLine(desc);
            sb.AppendLine();
        }

        var en = _model.Enchant;
        int slot = _model.SlotIndex;
        bool weaponSlot = en != null && slot >= 0;

        // 인챈트 — 같은 것끼리 묶어서, TierColor로
        if (weaponSlot)
        {
            int stage = en.GetStage(slot);
            if (stage > 0)
            {
                sb.Append(LocUtil.Get("ui.item.enchants")).Append("  +").Append(stage).AppendLine();

                EnchantSummary.Build(en, slot, _groups);
                foreach (var g in _groups)
                    sb.AppendLine(EnchantSummary.Line(g));

                sb.AppendLine();
            }
        }

        // 진화 — 갈래마다 조건 진행도
        if (weaponSlot && d.Upgrades != null && d.Upgrades.Length > 0)
        {
            int kills = en.GetKills(slot);
            int stage = en.GetStage(slot);

            sb.AppendLine(LocUtil.Get("ui.item.upgrades"));

            for (int i = 0; i < d.Upgrades.Length; i++)
            {
                var u = d.Upgrades[i];
                if (u == null || u.TargetId == 0) continue;

                var target = _model.Database != null ? _model.Database.Get(u.TargetId) : null;
                string tName = target != null ? NameOf(target) : $"#{u.TargetId}";
                bool ready = en.IsUpgradeReady(slot, i);

                sb.Append(ready ? $"  <color={MetColor}>→ {tName}</color>" : $"  → {tName}");

                if (u.RequiredKills > 0)
                    sb.Append("   ").Append(Progress(LocUtil.Get("ui.item.upgrade_kills", kills, u.RequiredKills),
                                                     kills >= u.RequiredKills));

                if (u.RequiredStage > 0)
                    sb.Append("   ").Append(Progress(LocUtil.Get("ui.item.upgrade_stage", stage, u.RequiredStage),
                                                     stage >= u.RequiredStage));

                if (!u.InheritEnchant)
                    sb.Append("   <color=").Append(DimColor).Append('>')
                      .Append(LocUtil.Get("ui.item.upgrade_reset")).Append("</color>");

                sb.AppendLine();
            }
        }
        else if (weaponSlot && d.Slot == EquipSlot.Weapon)
        {
            // 진화가 없어도 처치 수는 보여줌
            sb.AppendLine(LocUtil.Get("ui.item.kills", en.GetKills(slot)));
        }

        return sb.ToString().TrimEnd();
    }

    static string Progress(string text, bool met) =>
        met ? $"<color={MetColor}>✓ {text}</color>" : $"<color={DimColor}>{text}</color>";
}