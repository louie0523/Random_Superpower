using Fusion;
using UnityEngine;

public class PlayerShop : NetworkBehaviour
{
    public const int MaxItems = 16;

    [SerializeField] private ShopDatabase database;

    [Networked, Capacity(MaxItems)]
    public NetworkArray<int> Levels { get; }

    [Networked] public NetworkBool IsShopping { get; set; }

    private PlayerHealth _health;
    private PlayerStats _stats;
    private AbilityRuntime _ability;

    public ShopDatabase Database => database;

    public override void Spawned()
    {
        _health = GetComponent<PlayerHealth>();
        _stats = GetComponent<PlayerStats>();
        _ability = GetComponent<AbilityRuntime>();

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

    /// <summary>구매 가능 여부와 가격. HUD 표시용</summary>
    public bool CanBuy(int index, out int price, out string reason)
    {
        price = 0;
        reason = "";

        var item = database?.GetByIndex(index);
        if (item == null) { reason = "없음"; return false; }

        int lv = GetLevel(index);

        if (!item.IsRepeatable && lv >= item.MaxLevel)
        {
            reason = "최대";
            return false;
        }

        price = item.GetPrice(item.IsRepeatable ? 0 : lv);

        if (_health == null || _health.Soul < price)
        {
            reason = "소울 부족";
            return false;
        }

        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Buy(int index)
    {
        var item = database?.GetByIndex(index);
        if (item == null) return;

        int lv = Levels[index];

        if (!item.IsRepeatable && lv >= item.MaxLevel) return;

        int price = item.GetPrice(item.IsRepeatable ? 0 : lv);
        if (_health.Soul < price) return;

        _health.SpendSoul(price);

        if (item.Kind == ShopItemKind.ResetEnchant)
        {
            var en = GetComponent<PlayerEnchant>();
            var eq = GetComponent<PlayerEquipment>();
            if (en == null || eq == null) return;

            int stage = en.ActiveStage;
            if (stage == 0) return;

            int cost = item.BasePrice + item.PricePerStage * stage;
            if (_health.Soul < cost) return;

            _health.SpendSoul(cost);
            en.ClearSlot(eq.ActiveWeaponSlot);
            return;
        }

        Levels.Set(index, lv + 1);
        ApplyAll();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (!IsShopping) return;

        if (_health != null && !_health.IsDead && _health.InCombat)
            IsShopping = false;
    }

    // ══════════════════ 적용 ══════════════════

    void ApplyAll()
    {
        if (!HasStateAuthority) return;
        if (database == null || _stats == null) return;

        int rank = 0;

        for (int i = 0; i < MaxItems; i++)
        {
            int src = SourceId.ShopPurchase + i;
            _stats.RemoveBySource(src);

            int lv = Levels[i];
            if (lv <= 0) continue;

            var item = database.GetByIndex(i);
            if (item == null) continue;

            switch (item.Kind)
            {
                case ShopItemKind.Stat:
                    _stats.AddModifier(new StatModifier(
                        item.Stat, item.ModType, item.GetValue(lv), src));
                    break;

                case ShopItemKind.SkillRank:
                    rank += lv;
                    break;
            }
        }

        _ability?.SetSkillRank(rank);
    }

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < MaxItems; i++) Levels.Set(i, 0);
        IsShopping = false;
        ApplyAll();
    }
}