using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class InventoryPanel : UIPanel
{
    [Header("¹«±â ½½·Ô")]
    [SerializeField] private Transform weaponSlotParent;
    [SerializeField] private InventorySlotView weaponSlotPrefab;

    [Header("Àåºñ")]
    [SerializeField] private InventorySlotView armorSlot;
    [SerializeField] private InventorySlotView helmetSlot;
    [SerializeField] private InventorySlotView bootsSlot;

    [Header("¶óº§")]
    [SerializeField] private LocText titleLabel;
    [SerializeField] private LocText weaponSectionLabel;
    [SerializeField] private LocText gearSectionLabel;
    [SerializeField] private LocText dropHint;

    [Header("´Ý±â")]
    [SerializeField] private Button closeButton;

    private readonly List<InventorySlotView> _weaponSlots = new();

    private PlayerEquipment _equip;
    private PlayerEnchant _enchant;

    protected override void Awake()
    {
        base.Awake();

        titleLabel?.SetKey("ui.inventory.title");
        weaponSectionLabel?.SetKey("ui.inventory.weapons");
        gearSectionLabel?.SetKey("ui.inventory.gear");
        dropHint?.SetKey("ui.inventory.drop_hint");

        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseAll());
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

        for (int i = 0; i < PlayerEquipment.WeaponSlots; i++)
        {
            var view = Instantiate(weaponSlotPrefab, weaponSlotParent);
            int slot = i;

            view.OnClicked = () => _equip?.SwitchWeapon(slot);
            view.OnDropClicked = () =>
            {
                if (_equip != null && _equip.ActiveWeaponSlot == slot)
                    _equip.DropActiveWeapon();
            };

            _weaponSlots.Add(view);
        }
    }

    protected override void OnOpened() => Refresh();

    void Update()
    {
        if (IsOpen) Refresh();
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
                SlotIndex = i,
                IsActive = _equip.ActiveWeaponSlot == i,
                Enchant = _enchant,
                Ammo = _equip.GetAmmoAt(i),
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
            SlotIndex = -1,
            IsActive = false,
            Enchant = null,
            Ammo = -1,
        });
    }
}