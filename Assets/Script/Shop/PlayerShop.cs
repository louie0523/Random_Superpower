using Fusion;
using UnityEngine;

public class PlayerShop : NetworkBehaviour
{
    public const int MaxItems = 16;
    public const int MaxExclusive = 8;

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

    // ══════════════════ 구매 ══════════════════

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

        // 상점 열린 중 전투 돌입 → 닫기 (기존 코드)
        if (IsShopping && _health != null && !_health.IsDead && _health.InCombat)
            IsShopping = false;
    }

    // ══════════════════ 적용 ══════════════════

    void ApplyAll()
    {
        if (!HasStateAuthority) return;
        if (_stats == null) return;

        int rank = 0;

        // ── 공용 ──
        for (int i = 0; i < MaxItems; i++)
        {
            int src = SourceId.ShopBase + i;
            _stats.RemoveBySource(src);

            var item = database != null ? database.GetByIndex(i) : null;
            rank += ApplyItem(item, Levels[i], src);
        }

        // ── 전용 ──
        for (int i = 0; i < MaxExclusive; i++)
        {
            int src = SourceId.ShopExclusiveBase + i;
            _stats.RemoveBySource(src);

            rank += ApplyItem(GetExclusiveItem(i), ExclusiveLevels[i], src);
        }

        _ability?.SetSkillRank(rank);
    }

    /// <summary>스탯 적용 후, 스킬 랭크 기여분 반환</summary>
    int ApplyItem(ShopItemData item, int lv, int src)
    {
        if (item == null || lv <= 0) return 0;

        switch (item.Kind)
        {
            case ShopItemKind.Stat:
                _stats.AddModifier(new StatModifier(item.Stat, item.ModType, item.GetValue(lv), src));
                return 0;

            case ShopItemKind.SkillRank:
                return lv;
        }
        return 0;
    }

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < MaxExclusive; i++) ExclusiveLevels.Set(i, 0);
        IsShopping = false;
        ApplyAll();
    }

    public bool CanOpenShop
    {
        get
        {
            if (_health == null) return true;
            return _health.IsDead || !_health.InCombat;
        }
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
                    var en = _enchant;
                    int stage = en != null ? en.ActiveStage : 0;
                    if (stage == 0) { reason = "ui.shop.reason.no_enchant"; return false; }
                    price = item.BasePrice + item.PricePerStage * stage;
                    break;
                }

            case ShopItemKind.RefillAmmo:
                {
                    var en = _enchant;
                    if (en == null || !en.IsGunsmith) { reason = "ui.shop.reason.need_ability"; return false; }

                    var eq = _equip;
                    var w = eq != null ? eq.GetWeapon() : null;
                    if (w == null || w.Class != WeaponClass.Ranged || w.RangedKind != RangedType.Gun)
                    { reason = "ui.shop.reason.not_gun"; return false; }

                    if (eq.Ammo >= w.MaxAmmo) { reason = "ui.shop.reason.ammo_full"; return false; }
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
        var item = database?.GetByIndex(index);
        if (Purchase(item, GetLevel(index), out bool levelUp) && levelUp)
        {
            Levels.Set(index, Levels[index] + 1);
            ApplyAll();
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_BuyExclusive(int index)
    {
        if (index < 0 || index >= MaxExclusive) return;

        var item = GetExclusiveItem(index);
        if (Purchase(item, GetExclusiveLevel(index), out bool levelUp) && levelUp)
        {
            ExclusiveLevels.Set(index, ExclusiveLevels[index] + 1);
            ApplyAll();
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
                {
                    var en = GetComponent<PlayerEnchant>();
                    var eq = GetComponent<PlayerEquipment>();
                    en?.ClearSlot(eq.ActiveWeaponSlot);
                    break;
                }

            case ShopItemKind.RefillAmmo:
                {
                    var eq = GetComponent<PlayerEquipment>();
                    var w = eq.GetWeapon();
                    eq.AddAmmo(Mathf.CeilToInt(w.MaxAmmo * item.AmmoRatio));
                    break;
                }

            default:
                levelUp = true;      // Stat, SkillRank
                break;
        }

        return true;
    }
}