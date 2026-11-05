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
    static readonly string[] TabNames = { "근접", "활", "지팡이", "총", "던지기", "방어구" };

    [Tooltip("'스킨 보기'를 누르면 — 무기 Id")]
    public System.Action<int> OpenSkins;

    int _tab;
    EquipmentData _sel;
    readonly List<EquipmentData> _list = new();

    public override void Show()
    {
        Frame.Open();
        Frame.Tabs(TabNames, _tab, i => { _tab = i; _sel = null; Refresh(); });
        Refresh();
    }

    bool InTab(EquipmentData d, int tab)
    {
        if (tab == 5) return d.IsArmorPiece;
        if (d.Slot != EquipSlot.Weapon) return false;
        if (tab == 0) return d.Class == WeaponClass.Melee;
        if (d.Class != WeaponClass.Ranged) return false;
        return tab switch
        {
            1 => d.RangedKind == RangedType.Bow,
            2 => d.RangedKind == RangedType.Staff,
            3 => d.RangedKind == RangedType.Gun,
            _ => d.RangedKind == RangedType.Thrown,
        };
    }

    protected override void Refresh()
    {
        _list.Clear();
        var db = Meta != null ? Meta.Equipment : null;
        if (db != null && db.All != null)
            _list.AddRange(db.All.Where(d => d != null && InTab(d, _tab)).OrderBy(d => d.Grade).ThenBy(d => d.Id));

        Frame.SetEmpty(db == null ? "GameMeta에 장비 목록(Equipment)이 비어 있어" : "이 종류는 아직 없어");
        var cards = Frame.Layout(_list.Count, c => Pick(c.Data as EquipmentData));
        for (int i = 0; i < _list.Count; i++)
        {
            var d = _list[i];
            cards[i].Data = d;
            cards[i].Id = d.Id;
            cards[i].Set(MetaUi.ItemName(d), MetaUi.ItemGradeName(d.Grade), ItemThumbnail.Get(d), null,
                         MetaUi.ItemGradeColor(d.Grade), false, Skins(d) > 0 ? "스킨 " + Skins(d) : null);
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

        var skin = WeaponSkins.Get(PlayerSave.EquippedSkin(d.Id));
        var model = WeaponSkins.ModelFor(d, skin);
        if (model != null) Frame.ShowModel(model, skin);
        else Frame.ShowIcon(ItemThumbnail.Get(d), false);

        TitlePanelFrame.Set(Frame.Name, MetaUi.ItemName(d));
        string kind = MetaUi.ItemKind(d);
        TitlePanelFrame.Set(Frame.Sub, $"<color={Hex(MetaUi.ItemGradeColor(d.Grade))}>{MetaUi.ItemGradeName(d.Grade)}</color> · {kind}" +
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
