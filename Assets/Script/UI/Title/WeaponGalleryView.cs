using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 무기 보기 — 종류 탭 · 무기 카드 · 오른쪽에 3D 모델(끌어서 돌리기 · 휠 확대) + 능력치 · 설명.
/// 스킨이 있는 무기는 '스킨 보기' (스킨/뽑기 창으로)
/// </summary>
public class WeaponGalleryView : TitlePanel
{
    [Tooltip("'스킨 보기'를 누르면 — 무기 Id")]
    public System.Action<int> OpenSkins;

    // 위 탭 = 무구 / 방어구 · 아래 탭 = 전체 + 종류
    int _group;          // 0 무구 · 1 방어구
    int _sub;            // 0 전체
    EquipmentData _sel;
    readonly List<EquipmentData> _list = new();

    static readonly EquipSlot[] ArmorSlots = { EquipSlot.Armor, EquipSlot.Helmet, EquipSlot.Boots };

    public override void Show()
    {
        Frame.Open();
        Frame.Tabs(new[] { SetLoc.T("ui.codex.tab.weapons", "무구"), SetLoc.T("ui.codex.tab.armor", "방어구") }, _group,
                   i => { _group = i; _sub = 0; _sel = null; RefreshTabs(); Refresh(); });
        RefreshTabs();
        Refresh();
    }

    void OnDisable()
    {
        if (Frame != null) Frame.SubTabs(null, 0, null);      // 다른 창은 아래 탭 줄 없음
    }

    static string All => SetLoc.T("ui.codex.tab.all", "전체");

    string[] SubNames(int group) => group == 0
        ? new[]
        {
            All,
            LocUtil.GetOr("ui.item.class.Melee", "근접"),
            LocUtil.GetOr("ui.item.ranged_kind.Bow", "활"),
            LocUtil.GetOr("ui.item.ranged_kind.Staff", "지팡이"),
            LocUtil.GetOr("ui.item.ranged_kind.Gun", "총"),
            LocUtil.GetOr("ui.item.ranged_kind.Thrown", "투척류"),
        }
        : new[]
        {
            All,
            LocUtil.GetOr("ui.item.slot.Armor", "갑옷"),
            LocUtil.GetOr("ui.item.slot.Helmet", "투구"),
            LocUtil.GetOr("ui.item.slot.Boots", "신발"),
        };

    void RefreshTabs()
    {
        var names = SubNames(_group);
        for (int i = 0; i < names.Length; i++) names[i] = $"{names[i]} {Listed().Count(d => InTab(d, _group, i))}";
        Frame.SubTabs(names, _sub, i => { _sub = i; _sel = null; Refresh(); });
        Frame.SetTabText(0, $"{SetLoc.T("ui.codex.tab.weapons", "무구")} {Listed().Count(d => d.Slot == EquipSlot.Weapon)}");
        Frame.SetTabText(1, $"{SetLoc.T("ui.codex.tab.armor", "방어구")} {Listed().Count(d => d.IsArmorPiece)}");
    }

    static IEnumerable<EquipmentData> Listed()
    {
        var db = Meta != null ? Meta.Equipment : null;
        if (db == null || db.All == null) return Enumerable.Empty<EquipmentData>();
        return db.All.Where(d => d != null && d.IsListed);
    }

    static bool InTab(EquipmentData d, int group, int sub)
    {
        if (group == 1)
        {
            if (!d.IsArmorPiece) return false;
            return sub == 0 || (sub - 1 < ArmorSlots.Length && d.Slot == ArmorSlots[sub - 1]);
        }
        if (d.Slot != EquipSlot.Weapon) return false;
        if (sub == 0) return true;
        if (sub == 1) return d.Class == WeaponClass.Melee;
        if (d.Class != WeaponClass.Ranged) return false;
        return sub switch
        {
            2 => d.RangedKind == RangedType.Bow,
            3 => d.RangedKind == RangedType.Staff,
            4 => d.RangedKind == RangedType.Gun,
            _ => d.RangedKind == RangedType.Thrown,
        };
    }

    protected override void Refresh()
    {
        _list.Clear();
        var db = Meta != null ? Meta.Equipment : null;
        if (db != null && db.All != null)
            _list.AddRange(db.All.Where(d => d != null && d.IsListed && InTab(d, _group, _sub)).OrderBy(d => d.Grade).ThenBy(d => d.Id));

        Frame.SetEmpty(db == null ? "장비 목록이 비어 있습니다" : "이 종류는 아직 없습니다");
        var cards = Frame.Layout(_list.Count, c => Pick(c.Data as EquipmentData));
        for (int i = 0; i < _list.Count; i++)
        {
            var d = _list[i];
            cards[i].Data = d;
            cards[i].Id = d.Id;
            string badge = d.BlackMarketOnly ? SetLoc.T("shop.gear.market_tag", "암시장") : Skins(d) > 0 ? "스킨 " + Skins(d) : null;
            cards[i].Set(MetaUi.ItemName(d), MetaUi.ItemGradeName(d.Grade), ItemThumbnail.Get(d), null,
                         MetaUi.ItemGradeColor(d.Grade), false, badge);
        }

        if (_sel == null || !_list.Contains(_sel)) _sel = _list.Count > 0 ? _list[0] : null;
        Pick(_sel);
    }

    static int Skins(EquipmentData d)
    {
        var c = Meta != null ? Meta.Cosmetics : null;
        return c != null ? c.SkinsFor(d.Id).Count() : 0;
    }

    void Pick(EquipmentData d)
    {
        _sel = d;
        Frame.Clear();
        foreach (var c in Frame.Cards) if (c.gameObject.activeSelf && c.Data == (object)d) Frame.Select(c);
        if (d == null) return;

        Frame.SetPaint(MetaUi.ItemGradeColor(d.Grade));      // 뒤 스프레이 = 등급 색
        var skin = WeaponSkins.Get(PlayerSave.EquippedSkin(d.Id));
        var model = WeaponSkins.ModelFor(d, skin);
        if (model != null) Frame.ShowModel(model, skin);
        else Frame.ShowIcon(ItemThumbnail.Get(d), false);

        TitlePanelFrame.Set(Frame.Name, MetaUi.ItemName(d));
        string kind = MetaUi.ItemKind(d);
        TitlePanelFrame.Set(Frame.Sub, $"<color={Hex(MetaUi.ItemGradeColor(d.Grade))}>{MetaUi.ItemGradeName(d.Grade)}</color> · {kind}" +
                                       (d.BlackMarketOnly ? " · <color=#C08BFF>" + SetLoc.T("shop.gear.market_only", "암시장 전용") + "</color>" : "") +
                                       (skin != null ? " · 스킨: " + skin.Name : ""));

        var sb = new StringBuilder();
        if (!Mathf.Approximately(d.MainStatValue, 0f) && EquipmentData.MainStatOf(d.Slot, out var ms, out var mm))
            sb.AppendLine("<b>" + StatText.Line(ms, mm, d.MainStatValue, "  ") + "</b>");
        if (d.BaseStats != null)
            foreach (var s in d.BaseStats) sb.AppendLine(StatText.Line(s.Stat, s.Type, s.Value, "  "));
        string desc = MetaUi.ItemDesc(d);
        if (!string.IsNullOrWhiteSpace(desc)) sb.AppendLine().Append("<i>").Append(desc).Append("</i>");
        Frame.SetBody(sb.ToString().TrimEnd());

        int n = Skins(d);
        if (n > 0 && OpenSkins != null) Frame.Action1($"스킨 {n}개 보기", () => OpenSkins(d.Id));
    }

    protected override void OnClosed()
    {
        if (Frame.Viewport != null) Frame.Viewport.Show(null);
    }
}
