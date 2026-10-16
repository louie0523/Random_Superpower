using Fusion;
using UnityEngine;

public class PlayerEquipment : NetworkBehaviour
{
    [SerializeField] private EquipmentDatabase database;
    [SerializeField] private FPArmRig armRig;
    [SerializeField] private NetworkObject pickupPrefab;

    /// <summary>무기 칸 최대치 (네트워크 용량). 실제로 쓰는 칸 수는 UsableSlots</summary>
    public const int WeaponSlots = 7;
    /// <summary>기본 무기 칸 수 — 무구의 달인 등 능력이 SetSlotCount로 늘림</summary>
    public const int DefaultSlotCount = 3;

    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int SlotCount { get; set; }

    /// <summary>지금 쓸 수 있는 무기 칸 수</summary>
    public int UsableSlots => Mathf.Clamp(SlotCount > 0 ? SlotCount : DefaultSlotCount, 1, WeaponSlots);

    [Networked, Capacity(WeaponSlots), OnChangedRender(nameof(OnEquipChanged))]
    public NetworkArray<int> WeaponIds { get; }

    [Networked, OnChangedRender(nameof(OnEquipChanged))]
    public int ActiveWeaponSlot { get; set; }

    private int _startOverride;

    public int WeaponId
    {
        get
        {
            int id = WeaponIds[Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1)];
            return id != 0 ? id : fistWeaponId;
        }
    }

    /// <summary>기초 무기 — 능력이 바꿀 수 있음 (건마스터 = 글록)</summary>
    public int StartWeaponId => _startOverride != 0 ? _startOverride : startWeaponId;

    /// <summary>0이면 원래 기초 무기로. 지금 기초 무기를 들고 있으면 바로 교체</summary>
    public void SetStartWeapon(int id)
    {
        if (!HasStateAuthority) return;

        int prev = StartWeaponId;
        _startOverride = id;
        int now = StartWeaponId;
        if (prev == now) return;

        for (int i = 0; i < WeaponSlots; i++)
            if (WeaponIds[i] == prev) { ReplaceWeaponAt(i, now); break; }
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
            if (SlotCount <= 0) SlotCount = DefaultSlotCount;
            WeaponIds.Set(0, StartWeaponId);
            ArmorId = 0;
            HelmetId = 0;
            BootsId = 0;

            RefillAmmo(0);
        }
        ApplyAll();
    }

    // ══════════════════ 탄약 ══════════════════

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

    // ══════════════════ 표시 · 스탯 ══════════════════

    //void MountThirdPersonWeapon()
    //{
    //    if (tpWeaponSocket == null) return;
    //    if (_tpMountedId == WeaponId) return;

    //    _tpMountedId = WeaponId;

    //    if (_tpWeaponObj != null) Destroy(_tpWeaponObj);

    //    var data = database.Get(WeaponId);
    //    if (data == null || data.ModelPrefab == null) return;

    //    _tpWeaponObj = Instantiate(data.ModelPrefab, tpWeaponSocket);
    //    _tpWeaponObj.transform.localPosition = data.PositionOffset;   // 3인칭용 오프셋
    //    _tpWeaponObj.transform.localRotation = Quaternion.Euler(data.RotationOffset);
    //    _tpWeaponObj.transform.localScale = data.ScaleOverride;
    //}

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
        if (data == null || data.BaseStats == null) return;

        foreach (var s in data.BaseStats)
            _stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, sourceId));
    }

    void MountWeaponModel()
    {
        if (armRig == null) return;
        if (_mountedWeaponId == WeaponId) return;

        _mountedWeaponId = WeaponId;
        armRig.Mount(database?.Get(WeaponId));
    }

    // ══════════════════ 장착 · 획득 ══════════════════

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

    public bool HasEmptyWeaponSlot()
    {
        for (int i = 0; i < UsableSlots; i++)
            if (WeaponIds[i] == 0) return true;
        return false;
    }

    public bool PickUpWeapon(int id, out int slot)
    {
        slot = -1;
        if (!HasStateAuthority) return false;

        var data = database?.Get(id);
        if (data == null || data.Slot != EquipSlot.Weapon) return false;

        for (int i = 0; i < UsableSlots; i++)
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

    /// <summary>슬롯의 무기를 다른 무기로 교체 (진화). 인챈트·처치 수는 OnWeaponChanged가 비움</summary>
    public void ReplaceWeaponAt(int slot, int newId)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= WeaponSlots) return;

        WeaponIds.Set(slot, newId);
        RefillAmmo(slot);
        _enchant?.OnWeaponChanged(slot, newId);
        ApplyAll();     // SA는 즉시 반영 (OnChangedRender는 렌더 시점)
    }

    // ══════════════════ 버리기 ══════════════════

    /// <summary>무기를 바닥에. 인챈트·처치 수가 픽업에 실림</summary>
    public void DropWeapon(int id, int[] enchants = null, int kills = 0)
    {
        if (!HasStateAuthority) return;
        if (pickupPrefab == null) return;

        Vector3 pos = transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;

        var obj = Runner.Spawn(pickupPrefab, pos, Quaternion.identity);
        if (obj.TryGetComponent<Pickup>(out var pk))
        {
            pk.InitWeapon(id, enchants, kills);
            pk.SetPickupDelay(1.5f);
        }
    }

    /// <summary>지정 슬롯의 무기를 인챈트·처치 수째로 바닥에</summary>
    public void DropWeaponAt(int slot)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= WeaponSlots) return;

        int id = WeaponIds[slot];
        if (id == 0) return;

        // 비우기 전에 복사
        int[] carried = _enchant != null ? _enchant.ExportSlot(slot) : null;
        int kills = _enchant != null ? _enchant.GetKills(slot) : 0;

        WeaponIds.Set(slot, 0);
        _enchant?.ClearSlot(slot);

        DropWeapon(id, carried, kills);
    }

    public void DropActiveWeapon() => DropWeaponAt(ActiveWeaponSlot);

    /// <summary>슬롯의 무기를 꺼내서 없앰 (탐식자). 인챈트 목록을 돌려줌. 다른 무기가 있으면 그쪽으로 바꿔 듦</summary>
    public bool TakeWeaponAt(int slot, out int id, out int[] enchants)
    {
        id = 0;
        enchants = null;
        if (!HasStateAuthority || slot < 0 || slot >= WeaponSlots) return false;

        id = WeaponIds[slot];
        if (id == 0) return false;

        enchants = _enchant != null ? _enchant.ExportSlot(slot) : null;

        WeaponIds.Set(slot, 0);
        SlotAmmo.Set(slot, 0);
        _enchant?.ClearSlot(slot);

        if (slot == ActiveWeaponSlot)
            for (int i = 0; i < UsableSlots; i++)
                if (WeaponIds[i] != 0) { ActiveWeaponSlot = i; break; }

        ApplyAll();
        _enchant?.ApplyAll();
        return true;
    }

    /// <summary>무기 칸 수 변경 (무구의 달인). 줄어들면 넘치는 칸의 무기는 바닥에</summary>
    public void SetSlotCount(int n)
    {
        if (!HasStateAuthority) return;
        n = Mathf.Clamp(n, 1, WeaponSlots);
        if (n == UsableSlots) return;

        for (int i = n; i < WeaponSlots; i++)
            if (WeaponIds[i] != 0) DropWeaponAt(i);

        SlotCount = n;
        if (ActiveWeaponSlot >= n) { ActiveWeaponSlot = 0; _enchant?.ApplyAll(); }
        ApplyAll();
    }

    public void SwapWithPickup(int id)
    {
        if (!HasStateAuthority) return;

        int slot = Mathf.Clamp(ActiveWeaponSlot, 0, WeaponSlots - 1);
        int old = WeaponIds[slot];

        int[] carried = (old != 0 && _enchant != null) ? _enchant.ExportSlot(slot) : null;
        int kills = (old != 0 && _enchant != null) ? _enchant.GetKills(slot) : 0;

        WeaponIds.Set(slot, id);
        RefillAmmo(slot);
        _enchant?.OnWeaponChanged(slot, id);

        if (old != 0) DropWeapon(old, carried, kills);
    }

    // ══════════════════ 교체 ══════════════════

    public void SwitchWeapon(int slot)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= UsableSlots) return;
        if (slot == ActiveWeaponSlot) return;
        if (IsSwapping) return;
        if (WeaponIds[slot] == 0) return;      // 빈 슬롯은 무시

        ActiveWeaponSlot = slot;
        SwapTimer = TickTimer.CreateFromSeconds(Runner, swapTime);
        _enchant?.ApplyAll();
    }

    /// <summary>다음·이전 무기 — 빈 칸은 건너뜀</summary>
    public void CycleWeapon(int dir)
    {
        if (!HasStateAuthority) return;
        int n = UsableSlots;
        for (int step = 1; step < n; step++)
        {
            int s = ((ActiveWeaponSlot + dir * step) % n + n) % n;
            if (WeaponIds[s] != 0) { SwitchWeapon(s); return; }
        }
    }

    // ══════════════════ 라운드 ══════════════════

    /// <summary>시작 무기만 든 처음 상태로</summary>
    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < WeaponSlots; i++)
        {
            WeaponIds.Set(i, 0);
            SlotAmmo.Set(i, 0);
        }

        SlotCount = DefaultSlotCount;
        WeaponIds.Set(0, StartWeaponId);
        ActiveWeaponSlot = 0;
        ArmorId = 0;
        HelmetId = 0;
        BootsId = 0;
        SwapTimer = default;

        RefillAmmo(0);
        ApplyAll();
    }

    public EquipmentData GetWeapon() => database != null ? database.Get(WeaponId) : null;
    public EquipmentData GetArmor() => database != null ? database.Get(ArmorId) : null;
}