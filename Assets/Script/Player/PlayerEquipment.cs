using Fusion;
using UnityEngine;

public class PlayerEquipment : NetworkBehaviour
{
    [SerializeField] private EquipmentDatabase database;
    [SerializeField] private FPArmRig armRig;
    [SerializeField] private Transform tpWeaponSocket;   // B-handProp.R
    private GameObject _tpWeaponObj;
    private int _tpMountedId = -1;

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
    private int _mountedWeaponId = -1;

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        if (armRig == null) armRig = GetComponentInChildren<FPArmRig>(true);

        if (HasStateAuthority)
        {
            WeaponId = startWeaponId;
            ArmorId = 0;
            HelmetId = 0;
            BootsId = 0;
        }

        ApplyAll();
    }

    void MountThirdPersonWeapon()
    {
        if (tpWeaponSocket == null) return;
        if (_tpMountedId == WeaponId) return;

        _tpMountedId = WeaponId;

        if (_tpWeaponObj != null) Destroy(_tpWeaponObj);

        var data = database.Get(WeaponId);
        if (data == null || data.ModelPrefab == null) return;

        _tpWeaponObj = Instantiate(data.ModelPrefab, tpWeaponSocket);
        _tpWeaponObj.transform.localPosition = data.PositionOffset;   // 3인칭용 오프셋
        _tpWeaponObj.transform.localRotation = Quaternion.Euler(data.RotationOffset);
        _tpWeaponObj.transform.localScale = data.ScaleOverride;
    }

    void OnEquipChanged() => ApplyAll();

    void ApplyAll()
    {
        if (_stats == null || database == null) return;

        Apply(WeaponId, SourceId.Weapon);
        Apply(ArmorId, SourceId.Armor);
        Apply(HelmetId, SourceId.Helmet);
        Apply(BootsId, SourceId.Boots);

        MountWeaponModel();
    }

    void Apply(int id, int sourceId)
    {
        _stats.RemoveBySource(sourceId);

        var data = database.Get(id);
        if (data == null) return;

        foreach (var s in data.BaseStats)
            _stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, sourceId));
    }

    void MountWeaponModel()
    {
        if (armRig == null) return;
        if (_mountedWeaponId == WeaponId) return;

        _mountedWeaponId = WeaponId;

        var data = database?.Get(WeaponId);
        armRig.Mount(data);   // null이면 Mount가 알아서 초기화
    }

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

    public EquipmentData GetWeapon() => database != null ? database.Get(WeaponId) : null;
    public EquipmentData GetArmor() => database != null ? database.Get(ArmorId) : null;
}