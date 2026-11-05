using UnityEngine;

/// <summary>타이틀 창들이 같이 쓰는 이름 · 색</summary>
public static class MetaUi
{
    public static Color ItemGradeColor(ItemGrade g) => g switch
    {
        ItemGrade.S => new Color(1f, 0.78f, 0.18f),
        ItemGrade.A => new Color(0.75f, 0.45f, 1f),
        ItemGrade.B => new Color(0.30f, 0.66f, 1f),
        _ => new Color(0.66f, 0.66f, 0.70f),
    };

    public static string ItemGradeName(ItemGrade g) => LocUtil.GetOr($"ui.item.grade.{g}", g + "급");

    static string KeyPrefix(EquipmentData d) => d.Slot switch
    {
        EquipSlot.Armor => "armor",
        EquipSlot.Helmet => "helmet",
        EquipSlot.Boots => "boots",
        _ => "weapon",
    };

    public static string ItemName(EquipmentData d) =>
        d == null ? "" : LocUtil.GetOr($"{KeyPrefix(d)}.{d.Id}.name", LocUtil.Resolve(d.DisplayName));

    public static string ItemDesc(EquipmentData d) =>
        d == null ? "" : LocUtil.GetOr($"{KeyPrefix(d)}.{d.Id}.desc", LocUtil.Resolve(d.Description ?? ""));

    public static string ItemKind(EquipmentData d)
    {
        if (d == null) return "";
        if (d.Slot != EquipSlot.Weapon) return LocUtil.GetOr($"ui.item.slot.{d.Slot}", d.Slot.ToString());
        return d.Class == WeaponClass.Melee
            ? LocUtil.GetOr("ui.item.class.Melee", "근접")
            : LocUtil.GetOr($"ui.item.ranged_kind.{d.RangedKind}", d.RangedKind.ToString());
    }

    public static GradeInfo Grade(CosmeticGrade g)
    {
        var t = GameMeta.Get() != null ? GameMeta.Get().Gacha : null;
        if (t != null)
        {
            var i = t.Info(g);
            if (i != null) return i;
        }
        return g switch
        {
            CosmeticGrade.Legend => new GradeInfo { Grade = g, Name = "전설", Color = new Color(1f, 0.78f, 0.18f) },
            CosmeticGrade.Rare => new GradeInfo { Grade = g, Name = "희귀", Color = new Color(0.30f, 0.66f, 1f) },
            _ => new GradeInfo { Grade = g, Name = "일반", Color = new Color(0.66f, 0.66f, 0.70f) },
        };
    }

    public static string SlotName(string key)
    {
        var db = GameMeta.Get() != null ? GameMeta.Get().Cosmetics : null;
        var s = db != null ? db.Slot(key) : null;
        return s != null && !string.IsNullOrEmpty(s.Name) ? s.Name : key;
    }
}
