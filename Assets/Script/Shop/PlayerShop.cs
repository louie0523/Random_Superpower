using Fusion;
using UnityEngine;

public class PlayerShop : NetworkBehaviour
{
    public const int MaxItems = 16;
    public const int MaxExclusive = 50;

    [Networked, Capacity(MaxItems)]
    public NetworkArray<int> Levels { get; }

    [Networked, Capacity(MaxExclusive)]
    public NetworkArray<int> ExclusiveLevels { get; }

    [Networked] private int ExclusiveOwnerId { get; set; }

    [SerializeField] private ShopDatabase database;

    [Networked] public NetworkBool IsShopping { get; set; }

    private PlayerHealth _health;
    private PlayerStats _stats;
    private AbilityRuntime _ability;
    private PlayerEnchant _enchant;
    private PlayerEquipment _equip;

    public ShopDatabase Database => database;

    public override void Spawned()
    {
        _health = GetComponent<PlayerHealth>();
        _stats = GetComponent<PlayerStats>();
        _ability = GetComponent<AbilityRuntime>();
        _enchant = GetComponent<PlayerEnchant>();
        _equip = GetComponent<PlayerEquipment>();

        ApplyAll();
    }

    // ══════════════════ 상점 열기 ══════════════════

    public void SetShopping(bool on)
    {
        if (!HasStateAuthority) return;
        if (on && _health != null && !_health.IsDead && _health.InCombat) return;
        IsShopping = on;
    }

    public void Toggle() => SetShopping(!IsShopping);

    public bool CanOpenShop
    {
        get
        {
            if (_health == null) return true;
            return _health.IsDead || !_health.InCombat;
        }
    }

    // ══════════════════ 갱신 ══════════════════

    public int GetLevel(int index) =>
        (index >= 0 && index < MaxItems) ? Levels[index] : 0;

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        // 메인 능력이 바뀌면 전용 상점 초기화
        var main = _ability != null ? _ability.GetMainAbility() : null;
        int mainId = main != null ? main.Id : 0;

        if (mainId != ExclusiveOwnerId)
        {
            for (int i = 0; i < MaxExclusive; i++) ExclusiveLevels.Set(i, 0);
            ExclusiveOwnerId = mainId;
            ApplyAll();
        }

        // 상점 열린 중 전투 돌입 → 닫기
        if (IsShopping && _health != null && !_health.IsDead && _health.InCombat)
            IsShopping = false;
    }

    // ══════════════════ 적용 ══════════════════

    /// <summary>상점 스탯을 현재 레벨로 다시 건다 (스킬 포인트는 구매 시점에 지급되므로 여기선 안 다룸)</summary>
    void ApplyAll()
    {
        if (!HasStateAuthority) return;
        if (_stats == null) return;

        for (int i = 0; i < MaxItems; i++)
        {
            int src = SourceId.ShopBase + i;
            _stats.RemoveBySource(src);

            var item = database != null ? database.GetByIndex(i) : null;
            ApplyItem(item, Levels[i], src);
        }

        for (int i = 0; i < MaxExclusive; i++)
        {
            int src = SourceId.ShopExclusiveBase + i;
            _stats.RemoveBySource(src);

            ApplyItem(GetExclusiveItem(i), ExclusiveLevels[i], src);
        }
    }

    void ApplyItem(ShopItemData item, int lv, int src)
    {
        if (item == null || lv <= 0) return;

        if (item.Kind == ShopItemKind.Stat)
            _stats.AddModifier(new StatModifier(item.Stat, item.ModType, item.GetValue(lv), src));
    }

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < MaxItems; i++) Levels.Set(i, 0);
        for (int i = 0; i < MaxExclusive; i++) ExclusiveLevels.Set(i, 0);
        IsShopping = false;

        ApplyAll();
    }

    // ══════════════════ 조회 ══════════════════

    public ShopItemData[] GetExclusiveItems()
    {
        var main = _ability != null ? _ability.GetMainAbility() : null;
        return main != null ? main.ExclusiveShopItems : null;
    }

    public ShopItemData GetExclusiveItem(int i)
    {
        var items = GetExclusiveItems();
        return (items != null && i >= 0 && i < items.Length) ? items[i] : null;
    }

    public int GetExclusiveLevel(int i)
        => (i >= 0 && i < MaxExclusive) ? ExclusiveLevels[i] : 0;

    // ══════════════════ 구매 가능 여부 ══════════════════

    public bool CanBuy(int index, out int price, out string reason)
    {
        var item = database?.GetByIndex(index);
        return Evaluate(item, GetLevel(index), out price, out reason);
    }

    public bool CanBuyExclusive(int index, out int price, out string reason)
    {
        var item = GetExclusiveItem(index);
        return Evaluate(item, GetExclusiveLevel(index), out price, out reason);
    }

    /// <summary>공용·전용 공통 판정. reason은 로컬라이징 키</summary>
    bool Evaluate(ShopItemData item, int lv, out int price, out string reason)
    {
        price = 0;
        reason = "";

        if (item == null) { reason = "ui.shop.reason.none"; return false; }

        switch (item.Kind)
        {
            case ShopItemKind.ResetEnchant:
                {
                    int stage = _enchant != null ? _enchant.ActiveStage : 0;
                    if (stage == 0) { reason = "ui.shop.reason.no_enchant"; return false; }
                    price = item.BasePrice + item.PricePerStage * stage;
                    break;
                }

            case ShopItemKind.RefillAmmo:
                {
                    if (_enchant == null || !_enchant.IsGunsmith) { reason = "ui.shop.reason.need_ability"; return false; }

                    var w = _equip != null ? _equip.GetWeapon() : null;
                    if (w == null || w.Class != WeaponClass.Ranged || w.RangedKind != RangedType.Gun)
                    { reason = "ui.shop.reason.not_gun"; return false; }

                    if (_equip.Ammo >= w.MaxAmmo) { reason = "ui.shop.reason.ammo_full"; return false; }
                    price = item.BasePrice;
                    break;
                }

            default:
                {
                    if (!item.IsRepeatable && lv >= item.MaxLevel)
                    { reason = "ui.shop.reason.max"; return false; }

                    price = item.GetPrice(item.IsRepeatable ? 0 : lv);
                    break;
                }
        }

        if (_health == null || _health.Soul < price)
        { reason = "ui.shop.reason.no_soul"; return false; }

        return true;
    }

    // ══════════════════ 구매 ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Buy(int index)
    {
        if (index < 0 || index >= MaxItems) return;

        var item = database?.GetByIndex(index);
        if (Purchase(item, GetLevel(index), out bool levelUp) && levelUp)
        {
            float before = _stats.Get(StatType.MaxHP);
            _health.ApplyMaxHpGain(before);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_BuyExclusive(int index)
    {
        if (index < 0 || index >= MaxExclusive) return;

        var item = GetExclusiveItem(index);
        if (Purchase(item, GetExclusiveLevel(index), out bool levelUp) && levelUp)
        {
            float before = _stats.Get(StatType.MaxHP);
            _health.ApplyMaxHpGain(before);
        }
    }

    /// <summary>차감과 즉발 효과까지. 레벨을 올려야 하면 levelUp = true</summary>
    bool Purchase(ShopItemData item, int lv, out bool levelUp)
    {
        levelUp = false;

        if (!Evaluate(item, lv, out int price, out _)) return false;

        _health.SpendSoul(price);

        switch (item.Kind)
        {
            case ShopItemKind.InstantHeal:
                _health.RPC_Heal(item.HealAmount);
                break;

            case ShopItemKind.ResetEnchant:
                _enchant?.ClearSlot(_equip.ActiveWeaponSlot);
                break;

            case ShopItemKind.RefillAmmo:
                {
                    var w = _equip.GetWeapon();
                    _equip.AddAmmo(Mathf.CeilToInt(w.MaxAmmo * item.AmmoRatio));
                    break;
                }

            case ShopItemKind.SkillRank:
                // ★ 스킬 포인트 1 지급 — 어디에 쓸지는 능력 패널(F1)에서 고름
                _ability?.AddSkillPoints(1);
                levelUp = true;      // 구매 횟수 = 레벨 (MaxLevel로 총 구매 수 제한)
                break;

            default:
                levelUp = true;      // Stat
                break;
        }

        return true;
    }
}