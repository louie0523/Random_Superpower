using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class InventoryPanel : UIPanel
{
    [Header("무기 슬롯")]
    [SerializeField] private Transform weaponSlotParent;
    [SerializeField] private InventorySlotView weaponSlotPrefab;

    [Header("장비")]
    [SerializeField] private InventorySlotView armorSlot;
    [SerializeField] private InventorySlotView helmetSlot;
    [SerializeField] private InventorySlotView bootsSlot;

    [Header("라벨")]
    [SerializeField] private LocText titleLabel;
    [SerializeField] private LocText weaponSectionLabel;
    [SerializeField] private LocText gearSectionLabel;
    [SerializeField] private LocText dropHint;

    [Header("진화 선택 — 갈래가 여러 개일 때만 뜸")]
    [Tooltip("선택 창 전체 (평소엔 꺼둠)")]
    [SerializeField] private GameObject upgradeChoiceRoot;
    [Tooltip("선택 버튼들이 놓일 부모 (Vertical Layout Group)")]
    [SerializeField] private Transform upgradeChoiceParent;
    [Tooltip("선택 버튼 하나. 자식에 TMP_Text")]
    [SerializeField] private Button upgradeChoicePrefab;
    [SerializeField] private LocText upgradeChoiceTitle;

    [Header("닫기")]
    [SerializeField] private Button closeButton;

    private readonly List<InventorySlotView> _weaponSlots = new();
    private readonly List<Button> _choiceButtons = new();

    private PlayerEquipment _equip;
    private PlayerEnchant _enchant;
    private int _sig;

    protected override void Awake()
    {
        base.Awake();

        titleLabel?.SetKey("ui.inventory.title");
        weaponSectionLabel?.SetKey("ui.inventory.weapons");
        gearSectionLabel?.SetKey("ui.inventory.gear");
        dropHint?.SetKey("ui.inventory.drop_hint");
        upgradeChoiceTitle?.SetKey("ui.inventory.upgrade_choose");

        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseAll());

        HideUpgradeChoice();
    }

    public void Bind(PlayerEquipment equip, PlayerEnchant enchant)
    {
        _equip = equip;
        _enchant = enchant;
        BuildWeaponSlots();
    }

    void BuildWeaponSlots()
    {
        foreach (var s in _weaponSlots) Destroy(s.gameObject);
        _weaponSlots.Clear();

        int count = _equip != null ? _equip.UsableSlots : PlayerEquipment.DefaultSlotCount;
        for (int i = 0; i < count; i++)
        {
            var view = Instantiate(weaponSlotPrefab, weaponSlotParent);
            int slot = i;

            view.OnClicked = () => _equip?.SwitchWeapon(slot);
            view.OnDropClicked = () => _equip?.DropWeaponAt(slot);
            view.OnUpgradeClicked = () => OnUpgradeClicked(slot);

            _weaponSlots.Add(view);
        }
    }

    /// <summary>선택 창 취소 버튼 · 배경 클릭용</summary>
    public void CancelUpgradeChoice() => HideUpgradeChoice();
    protected override void OnOpened()
    {
        if (_equip != null && _weaponSlots.Count != _equip.UsableSlots) BuildWeaponSlots();
        HideUpgradeChoice();
        _sig = ComputeSig();
        Refresh();
    }

    protected override void OnClosed() => HideUpgradeChoice();

    void Update()
    {
        if (!IsOpen || _equip == null) return;

        // 무기 칸 수가 바뀌면 (무구의 달인) 칸을 다시 만듦
        if (_weaponSlots.Count != _equip.UsableSlots)
        {
            BuildWeaponSlots();
            _sig = ComputeSig();
            Refresh();
            return;
        }

        int sig = ComputeSig();
        if (sig == _sig) return;

        _sig = sig;
        HideUpgradeChoice();    // 상태가 바뀌면 열려 있던 선택지는 낡은 것
        Refresh();
    }

    /// <summary>화면에 영향 주는 값들만 묶은 지문 — 바뀌었을 때만 다시 그림</summary>
    int ComputeSig()
    {
        unchecked
        {
            int h = _equip.ActiveWeaponSlot * 17 + _equip.UsableSlots;
            for (int i = 0; i < PlayerEquipment.WeaponSlots; i++)
                h = h * 31 + _equip.GetWeaponIdAt(i) * 7 + _equip.GetAmmoAt(i);

            h = h * 31 + _equip.ArmorId;
            h = h * 31 + _equip.HelmetId;
            h = h * 31 + _equip.BootsId;

            if (_enchant != null)
            {
                h = h * 31 + _enchant.LastRollTick;
                for (int i = 0; i < PlayerEquipment.WeaponSlots; i++)
                    h = h * 31 + _enchant.GetStage(i) * 1009 + _enchant.GetKills(i);
            }
            return h;
        }
    }

    void Refresh()
    {
        if (_equip == null) return;

        var db = _equip.Database;

        for (int i = 0; i < _weaponSlots.Count; i++)
        {
            int id = _equip.GetWeaponIdAt(i);
            var data = id != 0 ? db?.Get(id) : null;

            _weaponSlots[i].Bind(new InventorySlotView.Model
            {
                Data = data,
                Database = db,
                SlotIndex = i,
                IsActive = _equip.ActiveWeaponSlot == i,
                Enchant = _enchant,
                Ammo = _equip.GetAmmoAt(i),
                UpgradeReady = data != null && _enchant != null && _enchant.AnyUpgradeReady(i),
            });
        }

        BindGear(armorSlot, _equip.ArmorId);
        BindGear(helmetSlot, _equip.HelmetId);
        BindGear(bootsSlot, _equip.BootsId);
    }

    void BindGear(InventorySlotView view, int id)
    {
        if (view == null) return;

        var data = id != 0 ? _equip.Database?.Get(id) : null;

        view.Bind(new InventorySlotView.Model
        {
            Data = data,
            Database = _equip.Database,
            SlotIndex = -1,
            IsActive = false,
            Enchant = null,
            Ammo = -1,
            UpgradeReady = false,
        });
    }

    // ══════════════════ 진화 ══════════════════

    void OnUpgradeClicked(int slot)
    {
        if (_equip == null || _enchant == null) return;

        var w = _equip.Database?.Get(_equip.GetWeaponIdAt(slot));
        if (w?.Upgrades == null) return;

        var ready = new List<int>();
        for (int i = 0; i < w.Upgrades.Length; i++)
            if (_enchant.IsUpgradeReady(slot, i)) ready.Add(i);

        if (ready.Count == 0) return;

        // 갈래가 하나면 바로, 여러 개면 고르게
        if (ready.Count == 1 || upgradeChoiceRoot == null)
        {
            _enchant.RequestUpgrade(slot, ready[0]);
            return;
        }

        ShowUpgradeChoice(slot, w, ready);
    }

    void ShowUpgradeChoice(int slot, EquipmentData w, List<int> paths)
    {
        ClearChoiceButtons();

        foreach (int path in paths)
        {
            var u = w.Upgrades[path];
            var target = _equip.Database?.Get(u.TargetId);
            string tName = target != null
                ? LocUtil.GetOr($"weapon.{target.Id}.name", LocUtil.Resolve(target.DisplayName))
                : $"#{u.TargetId}";

            var btn = Instantiate(upgradeChoicePrefab, upgradeChoiceParent);
            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = u.InheritEnchant
                    ? tName
                    : $"{tName}  <size=80%><color=#8A8A8A>{LocUtil.Get("ui.item.upgrade_reset")}</color></size>";

            int p = path;
            btn.onClick.AddListener(() =>
            {
                _enchant.RequestUpgrade(slot, p);
                HideUpgradeChoice();
            });

            _choiceButtons.Add(btn);
        }

        upgradeChoiceRoot.SetActive(true);
    }

    void HideUpgradeChoice()
    {
        ClearChoiceButtons();
        if (upgradeChoiceRoot != null) upgradeChoiceRoot.SetActive(false);
    }

    void ClearChoiceButtons()
    {
        foreach (var b in _choiceButtons)
            if (b != null) Destroy(b.gameObject);
        _choiceButtons.Clear();
    }
}