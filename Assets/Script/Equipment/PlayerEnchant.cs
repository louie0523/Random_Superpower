using Fusion;
using UnityEngine;

public class PlayerEnchant : NetworkBehaviour
{
    public const int MaxEnchants = 8;

    [SerializeField] private EnchantDatabase database;

    //[Tooltip("스톤 하나로 거는 인챈트 수")]
    //[SerializeField] private int enchantsPerStone = 1;

    // 슬롯 s의 인챈트 i → [s * MaxEnchants + i]
    [Networked, Capacity(PlayerEquipment.WeaponSlots * MaxEnchants)]
    public NetworkArray<int> SlotEnchants { get; }

    // 각 슬롯에 인챈트가 걸린 무기 ID
    [Networked, Capacity(PlayerEquipment.WeaponSlots)]
    public NetworkArray<int> EnchantedWeaponIds { get; }

    [Networked] public int Stones { get; set; }

    /// <summary>최근 인챈트 결과. HUD 연출용</summary>
    [Networked] public int LastRolled { get; set; }
    [Networked] public int LastRollTick { get; set; }

    [Networked] public NetworkBool IsGunsmith { get; set; }

    public void SetGunsmith(bool on)
    {
        if (!HasStateAuthority) return;
        IsGunsmith = on;
    }

    private PlayerEquipment _equip;
    private PlayerStats _stats;

    public EnchantDatabase Database => database;

    public override void Spawned()
    {
        _equip = GetComponent<PlayerEquipment>();
        _stats = GetComponent<PlayerStats>();

        ApplyAll();
    }

    // ══════════════════ 조회 ══════════════════

    public int GetEnchantId(int slot, int index)
    {
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return 0;
        if (index < 0 || index >= MaxEnchants) return 0;
        return SlotEnchants[slot * MaxEnchants + index];
    }

    public int GetStage(int slot)
    {
        int n = 0;
        for (int i = 0; i < MaxEnchants; i++)
            if (GetEnchantId(slot, i) != 0) n++;
        return n;
    }

    /// <summary>슬롯의 인챈트를 배열로 복사</summary>
    public int[] ExportSlot(int slot)
    {
        var arr = new int[MaxEnchants];
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return arr;

        for (int i = 0; i < MaxEnchants; i++)
            arr[i] = GetEnchantId(slot, i);

        return arr;
    }


    /// <summary>주운 무기의 인챈트를 슬롯에 복원</summary>
    public void ImportSlot(int slot, int weaponId, int[] src)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return;

        bool any = false;

        for (int i = 0; i < MaxEnchants; i++)
        {
            int id = (src != null && i < src.Length) ? src[i] : 0;
            SlotEnchants.Set(slot * MaxEnchants + i, id);
            if (id != 0) any = true;
        }

        EnchantedWeaponIds.Set(slot, any ? weaponId : 0);
        ApplyAll();
    }

    public int ActiveStage => _equip != null ? GetStage(_equip.ActiveWeaponSlot) : 0;

    public bool CanEnchant(out string reason, out int cost)
    {
        reason = "";
        cost = 0;

        var w = _equip?.GetWeapon();
        if (w == null) { reason = "ui.enchant.reason.no_weapon"; return false; }

        // 총은 특정 능력이 있어야 인챈트 가능
        if (w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun
            && !IsGunsmith)
        {
            reason = "ui.enchant.reason.gun_locked";
            return false;
        }

        if (!w.CanEnchant) { reason = "ui.enchant.reason.cannot"; return false; }

        int slot = _equip.ActiveWeaponSlot;
        int stage = GetStage(slot);

        if (stage >= Mathf.Min(w.MaxEnchantStage, MaxEnchants))
        {
            reason = "ui.enchant.reason.max_stage";
            return false;
        }

        cost = w.GetEnchantCost(stage);
        if (Stones < cost) { reason = "ui.enchant.reason.no_stone"; return false; }

        return true;
    }

    public void SetGunEnchant(bool on)
    {
        if (!HasStateAuthority) return;
        IsGunsmith = on;
    }

    // ══════════════════ 인챈트 ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Enchant()
    {
        if (!CanEnchant(out _, out int cost)) return;

        var w = _equip.GetWeapon();
        int slot = _equip.ActiveWeaponSlot;
        int stage = GetStage(slot);

        var rolled = w.EnchantTable.Roll(stage);
        if (rolled == null) return;
            
        Stones -= cost;

        SlotEnchants.Set(slot * MaxEnchants + stage, rolled.Id);
        EnchantedWeaponIds.Set(slot, w.Id);
        LastRolled = rolled.Id;
        LastRollTick = Runner.Tick;

        ApplyAll();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddStones(int amount)
    {
        Stones = Mathf.Max(0, Stones + amount);
    }

    /// <summary>해당 슬롯의 인챈트 전부 제거</summary>
    public void ClearSlot(int slot)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return;

        for (int i = 0; i < MaxEnchants; i++)
            SlotEnchants.Set(slot * MaxEnchants + i, 0);

        EnchantedWeaponIds.Set(slot, 0);
        ApplyAll();
    }

    /// <summary>무기가 바뀌면 그 슬롯 인챈트 초기화</summary>
    public void OnWeaponChanged(int slot, int newWeaponId)
    {
        if (!HasStateAuthority) return;
        if (EnchantedWeaponIds[slot] == newWeaponId) return;

        //ClearSlot(slot);
    }

    // ══════════════════ 적용 ══════════════════

    public void ApplyAll()
    {
        if (!HasStateAuthority) return;
        if (_stats == null || _equip == null || database == null) return;



        for (int i = 0; i < MaxEnchants; i++)
            _stats.RemoveBySource(SourceId.EnchantBase + i);

        int slot = _equip.ActiveWeaponSlot;

        int wid = _equip.GetWeaponIdAt(slot);
        if (wid == 0 || EnchantedWeaponIds[slot] != wid) return;

        for (int i = 0; i < MaxEnchants; i++)
        {
            int id = GetEnchantId(slot, i);
            if (id == 0) continue;

            var e = database.Get(id);
            if (e == null || e.Stats == null) continue;

            int src = SourceId.EnchantBase + i;

            foreach (var s in e.Stats)
                _stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, src));
        }
    }

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        for (int s = 0; s < PlayerEquipment.WeaponSlots; s++) ClearSlot(s);
        Stones = 0;
        LastRolled = 0;
        LastRollTick = 0;
        IsGunsmith = false;      // 능력 재배정 때 건스미스 OnEquip이 다시 켬
    }
}