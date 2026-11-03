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
        public ArmorEnchantData ArmorEnchant; // 방어구 특수 인챈트 (없으면 null)
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
            // 3D 모델을 찍은 그림 (없으면 아이콘)
            var sp = hasItem ? ItemThumbnail.Get(m.Data) : null;
            if (sp == null && hasItem) sp = m.Data.Icon;
            icon.enabled = sp != null;
            icon.sprite = sp;
            icon.preserveAspect = true;
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
            Spec = BuildSpec(d),
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

        // 방어구 주요 능력치 — 맨 위
        if (!Mathf.Approximately(d.MainStatValue, 0f) && EquipmentData.MainStatOf(d.Slot, out var ms, out var mm))
            sb.AppendLine("<b>" + StatText.Line(ms, mm, d.MainStatValue, "  ") + "</b>");

        if (d.BaseStats != null)
            foreach (var s in d.BaseStats)
                sb.AppendLine(StatText.Line(s.Stat, s.Type, s.Value, "  "));

        return sb.ToString().TrimEnd();
    }

    /// <summary>무기 스펙 — 근접: 사거리 / 총: 탄약 · 분당 발사 / 활 · 지팡이: 차징 (툴팁에서 능력치 아래 줄 긋고)</summary>
    string BuildSpec(EquipmentData d)
    {
        var sb = new System.Text.StringBuilder();

        if (d.Slot == EquipSlot.Weapon)
        {
            if (d.Class == WeaponClass.Melee)
            {
                sb.AppendLine(LocUtil.Get("ui.item.melee_range", d.Range.ToString("0.#")));
            }
            else if (d.RangedKind == RangedType.Gun)
            {
                if (d.FireMode == GunFireMode.Hitscan)
                    sb.AppendLine(LocUtil.Get("ui.item.melee_range", d.HitscanRange.ToString("0.#")));     // "사거리 {0}m"
                sb.AppendLine(LocUtil.Get("ui.item.ammo", d.MaxAmmo));
                sb.AppendLine(SetLoc.F("ui.item.rps", "초당 {0}발", (d.RoundsPerMinute / 60f).ToString("0.#")));
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

        // 고유 특성
        if (d.Traits != null && d.Traits.Length > 0)
        {
            sb.AppendLine(SetLoc.T("ui.item.traits", "고유 특성"));
            foreach (var t in d.Traits)
                if (t != null) sb.Append("  ").AppendLine(t.Describe());
            sb.AppendLine();
        }

        // 방어구 특수 인챈트 (1칸)
        if (d.IsArmorPiece)
        {
            if (_model.ArmorEnchant != null) sb.AppendLine(_model.ArmorEnchant.Describe());
            else sb.Append("<color=").Append(DimColor).Append('>').Append(SetLoc.T("ui.item.armor_enchant_empty", "◇ 특수 인챈트 없음")).AppendLine("</color>");
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

        return sb.ToString().TrimEnd();
    }

    static string Progress(string text, bool met) =>
        met ? $"<color={MetColor}>✓ {text}</color>" : $"<color={DimColor}>{text}</color>";
}
