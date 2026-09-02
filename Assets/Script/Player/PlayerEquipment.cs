using Fusion;
using UnityEngine;

public class PlayerEquipment : NetworkBehaviour
{
    [SerializeField] private EquipmentDatabase database;

    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int WeaponId { get; set; }
    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int ArmorId { get; set; }
    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int HelmetId { get; set; }
    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int BootsId { get; set; }

    [Header("Starting Gear")]
    [SerializeField] private int startWeaponId = 1001;

    private PlayerStats _stats;

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();

        if (HasStateAuthority)
        {
            WeaponId = startWeaponId;
            ArmorId = 0;
            HelmetId = 0;
            BootsId = 0;
        }

        ApplyAll();
    }

    void OnEquipChanged() => ApplyAll();

    void ApplyAll()
    {
        if (_stats == null || database == null) return;

        Apply(WeaponId, SourceId.Weapon);
        Apply(ArmorId, SourceId.Armor);
        Apply(HelmetId, SourceId.Helmet);
        Apply(BootsId, SourceId.Boots);
    }

    void Apply(int id, int sourceId)
    {
        _stats.RemoveBySource(sourceId);

        var data = database.Get(id);
        if (data == null) return;

        foreach (var s in data.BaseStats)
            _stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, sourceId));
    }

    /// <summary>장비 교체 (주인만 호출)</summary>
    public void Equip(int id)
    {
        if (!HasStateAuthority) return;

        var data = database.Get(id);
        if (data == null) return;

        switch (data.Slot)
        {
            case EquipSlot.Weapon: WeaponId = id; break;
            case EquipSlot.Armor: ArmorId = id; break;
            case EquipSlot.Helmet: HelmetId = id; break;
            case EquipSlot.Boots: BootsId = id; break;
        }
    }

    public EquipmentData GetWeapon() => database.Get(WeaponId);
    public EquipmentData GetArmor() => database.Get(ArmorId);
}