using UnityEngine;

/// <summary>
/// 무기 · 방어구를 소울로 사기 — 암시장 · 상점 한정판이 같이 씀 (내 캐릭터 권한자에서 바로).
/// 무기 칸이 꽉 차 있으면 든 무기를 바닥에 내려놓고 바꿈 · 방어구는 예전 것을 바닥에
/// </summary>
public static class GearPurchase
{
    /// <summary>살 수 있나 (거리 · 전투 같은 장소 조건은 부르는 쪽에서) — reason은 번역된 이유</summary>
    public static bool CanBuy(EquipmentData d, int price, out string reason)
    {
        reason = "";
        var me = PlayerAttack.Local;
        if (me == null || d == null) return false;
        var hp = me.GetComponent<PlayerHealth>();
        var eq = me.GetComponent<PlayerEquipment>();
        if (hp == null || eq == null) return false;
        if (hp.IsDead) { reason = SetLoc.T("market.reason.dead", "죽은 상태에서는 살 수 없습니다"); return false; }
        if (eq.Locked) { reason = SetLoc.T("market.reason.locked", "변신 중에는 장비를 바꿀 수 없습니다"); return false; }
        if (hp.Soul < price) { reason = SetLoc.T("market.reason.soul", "소울이 부족합니다"); return false; }
        return true;
    }

    /// <summary>상점 한정판 · 암시장 상품 — 무기는 강화 · 인챈트째, 방어구는 특수 인챈트째</summary>
    public static bool Buy(MarketOffer o, out string reason)
    {
        reason = "";
        if (o == null || o.Item == null) return false;
        if (!o.Item.IsWeapon) return Buy(o.Item, o.ArmorEnchant, o.Price, out reason);
        if (!CanBuy(o.Item, o.Price, out reason)) return false;

        var me = PlayerAttack.Local;
        var hp = me.GetComponent<PlayerHealth>();
        var eq = me.GetComponent<PlayerEquipment>();
        var en = me.GetComponent<PlayerEnchant>();
        int slot;
        if (eq.HasEmptyWeaponSlot())
        {
            if (!eq.PickUpWeapon(o.Item.Id, out slot)) { reason = SetLoc.T("market.reason.fail", "지금은 살 수 없습니다"); return false; }
        }
        else
        {
            slot = eq.ActiveWeaponSlot;
            eq.SwapWithPickup(o.Item.Id);       // 든 무기는 바닥에
        }
        if (en != null && o.Codes != null && o.Codes.Length > 0) en.ImportSlot(slot, o.Item.Id, o.Codes);
        hp.SpendSoul(Mathf.Max(0, o.Price));
        reason = SetLoc.F("market.bought", "{0}을(를) 구매했습니다", o.Item.GetName());
        return true;
    }

    public static bool Buy(EquipmentData d, ArmorEnchantData ench, int price, out string reason)
    {
        if (!CanBuy(d, price, out reason)) return false;
        var me = PlayerAttack.Local;
        var hp = me.GetComponent<PlayerHealth>();
        var eq = me.GetComponent<PlayerEquipment>();

        bool ok;
        if (d.IsWeapon)
        {
            if (eq.HasEmptyWeaponSlot()) ok = eq.PickUpWeapon(d.Id, out _);
            else { eq.SwapWithPickup(d.Id); ok = true; }
        }
        else ok = eq.EquipArmor(d.Id, ench != null && d.IsArmorPiece ? ench.Id : 0, true);

        if (!ok) { reason = SetLoc.T("market.reason.fail", "지금은 살 수 없습니다"); return false; }
        hp.SpendSoul(Mathf.Max(0, price));
        reason = SetLoc.F("market.bought", "{0}을(를) 구매했습니다", d.GetName());
        return true;
    }
}
