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

    public int WeaponId
    {
        get
        {
            int id = WeaponIds[Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1)];
            return id != 0 ? id : fistWeaponId;
        }
    }


    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int ArmorId { get; set; }
    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int HelmetId { get; set; }
    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int BootsId { get; set; }

    [Networked, Capacity(WeaponSlots)]
    public NetworkArray<int> SlotAmmo { get; }

    public int Ammo => SlotAmmo[Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1)];

    [Header("Starting Gear")]
    [SerializeField] private int startWeaponId = 1001;
    [SerializeField] private int fistWeaponId = 1000;

    private PlayerStats _stats;
    private int _mountedWeaponId = -1;

    private PlayerEnchant _enchant;

    public EquipmentDatabase Database => database;

    public int GetWeaponIdAt(int slot)
        => (slot >= 0 && slot < WeaponSlots) ? WeaponIds[slot] : 0;

    public int GetAmmoAt(int slot)
        => (slot >= 0 && slot < WeaponSlots) ? SlotAmmo[slot] : 0;

    [SerializeField] private float swapTime = 0.6f;
    [Networked] private TickTimer SwapTimer { get; set; }
    public bool IsSwapping => SwapTimer.IsRunning && !SwapTimer.Expired(Runner);

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _enchant = GetComponent<PlayerEnchant>();
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

            RefillAmmo(0);
        }
        ApplyAll();
    }

    /// <summary>시작 무기만 든 처음 상태로</summary>
    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < WeaponSlots; i++)
        {
            WeaponIds.Set(i, 0);
            SlotAmmo.Set(i, 0);
        }

        WeaponIds.Set(0, startWeaponId);
        ActiveWeaponSlot = 0;
        ArmorId = 0;
        HelmetId = 0;
        BootsId = 0;
        SwapTimer = default;

        RefillAmmo(0);
        ApplyAll();      // 스탯 즉시 재적용 (OnChangedRender는 렌더 때라 늦음)
    }

    public bool HasEmptyWeaponSlot()
    {
        for (int i = 0; i < WeaponSlots; i++)
            if (WeaponIds[i] == 0) return true;
        return false;
    }
    public void AddAmmo(int amount)
    {
        if (!HasStateAuthority) return;

        int slot = Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1);
        var w = database?.Get(WeaponIds[slot]);
        if (w == null) return;

        SlotAmmo.Set(slot, Mathf.Min(SlotAmmo[slot] + amount, w.MaxAmmo));
    }

    public void RefillAmmo(int slot)
    {
        if (!HasStateAuthority) return;

        var w = database?.Get(WeaponIds[slot]);
        int max = (w != null && w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun)
            ? w.MaxAmmo : 0;

        SlotAmmo.Set(slot, max);
    }

    public bool ConsumeAmmo()
    {
        if (!HasStateAuthority) return false;

        int slot = Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1);
        int cur = SlotAmmo[slot];
        if (cur <= 0) return false;

        SlotAmmo.Set(slot, cur - 1);
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
                RefillAmmo(ActiveWeaponSlot);
                _enchant?.OnWeaponChanged(ActiveWeaponSlot, id);
                break;
            case EquipSlot.Armor: ArmorId = id; break;
            case EquipSlot.Helmet: HelmetId = id; break;
            case EquipSlot.Boots: BootsId = id; break;
        }
    }

    public bool PickUpWeapon(int id, out int slot)
    {
        slot = -1;
        if (!HasStateAuthority) return false;

        var data = database?.Get(id);
        if (data == null || data.Slot != EquipSlot.Weapon) return false;

        for (int i = 0; i < WeaponSlots; i++)
        {
            if (WeaponIds[i] != 0) continue;

            WeaponIds.Set(i, id);
            ActiveWeaponSlot = i;
            RefillAmmo(i);
            _enchant?.OnWeaponChanged(i, id);

            slot = i;
            return true;
        }

        return false;
    }

    /// <summary>기존 호출부 호환</summary>
    public bool PickUpWeapon(int id) => PickUpWeapon(id, out _);

    public void DropWeapon(int id, int[] enchants = null)
    {
        if (!HasStateAuthority) return;
        if (pickupPrefab == null) return;

        Vector3 pos = transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;

        var obj = Runner.Spawn(pickupPrefab, pos, Quaternion.identity);
        if (obj.TryGetComponent<Pickup>(out var pk))
        {
            pk.InitWeapon(id, enchants);     // Init 대신 (안에서 Init을 부름)
            pk.SetPickupDelay(1.5f);
        }
    }

    public void DropActiveWeapon()
    {
        if (!HasStateAuthority) return;

        int slot = ActiveWeaponSlot;
        int id = WeaponIds[slot];
        if (id == 0) return;

        int[] carried = _enchant != null ? _enchant.ExportSlot(slot) : null;
        Debug.Log($"[Drop] id={id} slot={slot} carried={(carried == null ? "null" : string.Join(",", carried))}");

        WeaponIds.Set(slot, 0);
        _enchant?.ClearSlot(slot);

        DropWeapon(id, carried);
    }

    public void SwapWithPickup(int id)
    {
        if (!HasStateAuthority) return;

        int slot = Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1);
        int old = WeaponIds[slot];

        int[] carried = (old != 0 && _enchant != null) ? _enchant.ExportSlot(slot) : null;  // ★

        WeaponIds.Set(slot, id);
        RefillAmmo(slot);
        _enchant?.OnWeaponChanged(slot, id);

        if (old != 0) DropWeapon(old, carried);
    }

    public void SwitchWeapon(int slot)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= WeaponSlots) return;
        if (slot == ActiveWeaponSlot) return;
        if (IsSwapping) return;
        if (WeaponIds[slot] == 0) return;      // 빈 슬롯은 무시

        ActiveWeaponSlot = slot;
        SwapTimer = TickTimer.CreateFromSeconds(Runner, swapTime);
        _enchant?.ApplyAll();
    }

    public void CycleWeapon(int dir)
    {
        if (!HasStateAuthority) return;
        SwitchWeapon((ActiveWeaponSlot + dir + WeaponSlots) % WeaponSlots);
    }

    public EquipmentData GetWeapon() => database != null ? database.Get(WeaponId) : null;
    public EquipmentData GetArmor() => database != null ? database.Get(ArmorId) : null;
}