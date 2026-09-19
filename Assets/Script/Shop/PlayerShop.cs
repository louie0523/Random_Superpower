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

    [SerializeField] private ShopDatabase database;


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

        // ── 인챈트 리셋 ──
        if (item.Kind == ShopItemKind.ResetEnchant)
        {
            var en = GetComponent<PlayerEnchant>();
            int stage = en != null ? en.ActiveStage : 0;

            if (stage == 0) { reason = "인챈트 없음"; return false; }

            price = item.BasePrice + item.PricePerStage * stage;

            if (_health == null || _health.Soul < price) { reason = "소울 부족"; return false; }
            return true;
        }

        // ── 탄약 충전 ──
        if (item.Kind == ShopItemKind.RefillAmmo)
        {   
            var en = GetComponent<PlayerEnchant>();
            if (en == null || !en.IsGunsmith) { reason = "능력 필요"; return false; }

            var eq = GetComponent<PlayerEquipment>();
            var w = eq?.GetWeapon();

            if (w == null || w.Class != WeaponClass.Ranged || w.RangedKind != RangedType.Gun)
            {
                reason = "총 아님";
                return false;
            }

            if (eq.Ammo >= w.MaxAmmo) { reason = "가득참"; return false; }

            price = item.BasePrice;
            if (_health == null || _health.Soul < price) { reason = "소울 부족"; return false; }
            return true;
        }

        // ── 일반 ──
        int lv = GetLevel(index);

        if (!item.IsRepeatable && lv >= item.MaxLevel) { reason = "최대"; return false; }

        price = item.GetPrice(item.IsRepeatable ? 0 : lv);

        if (_health == null || _health.Soul < price) { reason = "소울 부족"; return false; }
        return true;
    }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Buy(int index)
    {
        var item = database?.GetByIndex(index);
        if (item == null) return;

        // ── 특수 항목 (레벨 없음) ──

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

        if (item.Kind == ShopItemKind.RefillAmmo)
        {
            var eq = GetComponent<PlayerEquipment>();
            if (eq == null) return;

            var en = GetComponent<PlayerEnchant>();
            if (en == null || !en.IsGunsmith) return;

            var w = eq.GetWeapon();
            if (w == null || w.Class != WeaponClass.Ranged || w.RangedKind != RangedType.Gun)
                return;

            if (eq.Ammo >= w.MaxAmmo) return;
            if (_health.Soul < item.BasePrice) return;

            _health.SpendSoul(item.BasePrice);
            eq.AddAmmo(Mathf.CeilToInt(w.MaxAmmo * item.AmmoRatio));
            return;
        }

        // ── 일반 항목 ──

        int lv = Levels[index];
        if (!item.IsRepeatable && lv >= item.MaxLevel) return;

        int price = item.GetPrice(item.IsRepeatable ? 0 : lv);
        if (_health.Soul < price) return;

        _health.SpendSoul(price);

        if (item.Kind == ShopItemKind.InstantHeal)
        {
            _health.RPC_Heal(item.HealAmount);
            return;                          // 레벨 안 올림
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