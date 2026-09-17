using Fusion;
using UnityEngine;

public class PlayerEquipment : NetworkBehaviour
{
    [SerializeField] private EquipmentDatabase database;
    [SerializeField] private FPArmRig armRig;
    [SerializeField] private Transform tpWeaponSocket;
    [SerializeField] private NetworkObject pickupPrefab;
    private GameObject _tpWeaponObj;
    private int _tpMountedId = -1;

    public const int WeaponSlots = 3;

    [Networked, Capacity(WeaponSlots), OnChangedRender(nameof(OnEquipChanged))]
    public NetworkArray<int> WeaponIds { get; }

    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int ActiveWeaponSlot { get; set; }

    public int WeaponId => WeaponIds[Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1)];


    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int ArmorId { get; set; }
    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int HelmetId { get; set; }
    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int BootsId { get; set; }

    [Networked] public int Ammo { get; set; }

    [Header("Starting Gear")]
    [SerializeField] private int startWeaponId = 1001;

    private PlayerStats _stats;
    private int _mountedWeaponId = -1;

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        if (armRig == null) armRig = GetComponentInChildren<FPArmRig>(true);

        // 손 초기화
        if (armRig != null)
        {
            armRig.RightHandRig?.ResetToRest();
            armRig.LeftHandRig?.ResetToRest();
        }

        if (HasStateAuthority)
        {
            WeaponIds.Set(0, startWeaponId);
            ArmorId = 0;
            HelmetId = 0;
            BootsId = 0;
            RefillAmmo();
        }

        ApplyAll();
    }

    public void RefillAmmo()
    {
        if (!HasStateAuthority) return;
        var w = GetWeapon();
        Ammo = (w != null && w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun)
            ? w.MaxAmmo : 0;
    }

    public bool ConsumeAmmo()
    {
        if (!HasStateAuthority) return false;
        if (Ammo <= 0) return false;
        Ammo--;
        return true;
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

        Debug.Log($"[Equip] Mount 시도 — WeaponId={WeaponId}, data={(data == null ? "NULL" : data.DisplayName)}");

        armRig.Mount(data);
    }

    public void Equip(int id)
    {
        if (!HasStateAuthority) return;

        var data = database.Get(id);
        if (data == null) return;

        switch (data.Slot)
        {
            case EquipSlot.Weapon:
                WeaponIds.Set(ActiveWeaponSlot, id);
                RefillAmmo();
                //_enchant?.OnWeaponChanged(id);
                break;
            case EquipSlot.Armor: ArmorId = id; break;
            case EquipSlot.Helmet: HelmetId = id; break;
            case EquipSlot.Boots: BootsId = id; break;
        }
    }

    public bool PickUpWeapon(int id)
    {
        if (!HasStateAuthority) return false;

        var data = database?.Get(id);
        if (data == null || data.Slot != EquipSlot.Weapon) return false;

        for (int i = 0; i < WeaponSlots; i++)
        {
            if (WeaponIds[i] != 0) continue;

            WeaponIds.Set(i, id);
            ActiveWeaponSlot = i;
            RefillAmmo();
            return true;
        }

        return false;      // 슬롯이 꽉 차면 안 먹음
    }

    public void DropWeapon(int id)
    {
        if (!HasStateAuthority) return;
        if (pickupPrefab == null) return;

        Vector3 pos = transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;

        var obj = Runner.Spawn(pickupPrefab, pos, Quaternion.identity);
        if (obj.TryGetComponent<Pickup>(out var pk))
        {
            pk.Init(PickupKind.Weapon, id);
            pk.SetPickupDelay(1.5f);
        }
    }

    public void DropActiveWeapon()
    {
        if (!HasStateAuthority) return;

        int id = WeaponIds[ActiveWeaponSlot];
        if (id == 0) return;

        WeaponIds.Set(ActiveWeaponSlot, 0);
        DropWeapon(id);
    }

    public void SwapWithPickup(int id)
    {
        if (!HasStateAuthority) return;

        int old = WeaponIds[ActiveWeaponSlot];
        WeaponIds.Set(ActiveWeaponSlot, id);
        RefillAmmo();

        if (old != 0) DropWeapon(old);
    }

    public void SwitchWeapon(int slot)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= WeaponSlots) return;
        if (slot == ActiveWeaponSlot) return;

        ActiveWeaponSlot = slot;
    }

    public void CycleWeapon(int dir)
    {
        if (!HasStateAuthority) return;
        SwitchWeapon((ActiveWeaponSlot + dir + WeaponSlots) % WeaponSlots);
    }

    public EquipmentData GetWeapon() => database != null ? database.Get(WeaponId) : null;
    public EquipmentData GetArmor() => database != null ? database.Get(ArmorId) : null;
}