using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 스킨 · 뽑기 — 탭 '무기 스킨': 사기(SC) · 장착 · 해제 / 탭 '뽑기': 1번 · 10번 (티켓이 있으면 티켓 먼저, 없으면 SC).
/// 겹치면 등급별 SC를 돌려받음. 확률 · 값은 GameMeta의 Gacha 에셋에서
/// </summary>
public class SkinShopView : TitlePanel
{
    static readonly string[] TabNames = { "무기 스킨", "뽑기" };

    int _tab;
    int _focusWeapon;           // 0 = 전체
    object _sel;
    readonly List<object> _list = new();
    readonly List<string> _results = new();
    readonly HashSet<object> _new = new();
    readonly System.Random _rng = new();

    static CosmeticDatabase Db => Meta != null ? Meta.Cosmetics : null;
    static GachaTable Table => Meta != null ? Meta.Gacha : null;

    /// <summary>한 무기의 스킨만 보며 열기 (무기 보기 창에서)</summary>
    public void ShowWeapon(int weaponId)
    {
        _focusWeapon = weaponId;
        _tab = 0;
        _sel = null;
        Show();
    }

    public override void Show()
    {
        Frame.Open();
        Frame.Tabs(TabNames, _tab, i => { _tab = i; _focusWeapon = 0; _sel = null; Refresh(); });
        Refresh();
    }

    protected override void OnClosed()
    {
        _focusWeapon = 0;
        if (Frame.Viewport != null) Frame.Viewport.Show(null);
    }

    protected override void OnSave() => Paint();

    // ══════════════════ 목록 ══════════════════

    protected override void Refresh()
    {
        _list.Clear();
        var db = Db;
        if (db != null)
        {
            if (_tab == 0)
                _list.AddRange(db.WeaponSkins.Where(s => s != null && (_focusWeapon == 0 || s.WeaponId == _focusWeapon))
                                             .OrderBy(s => s.WeaponId).ThenBy(s => s.Grade).ThenBy(s => s.Id));
            else
            {
                _list.AddRange(db.Parts.Where(p => p != null && p.InGacha && !db.IsFree(p.Id)).OrderByDescending(p => p.Grade).ThenBy(p => p.Id));
                _list.AddRange(db.WeaponSkins.Where(s => s != null && s.InGacha).OrderByDescending(s => s.Grade).ThenBy(s => s.Id));
            }
        }
        Frame.SetEmpty(db == null ? "꾸미기 목록이 비어 있습니다" :
                       _tab == 0 ? "아직 스킨이 없습니다" : "뽑기에서 나올 항목이 없습니다");

        Frame.Layout(_list.Count, c => Pick(c.Data));
        Paint();
        if (_sel == null || !_list.Contains(_sel)) _sel = _list.Count > 0 && _tab == 0 ? _list[0] : null;
        Pick(_sel);
    }

    /// <summary>카드 표시만 다시 (가진 것 · 장착 · 새로 얻음)</summary>
    void Paint()
    {
        var cards = Frame.Cards;
        for (int i = 0; i < _list.Count && i < cards.Count; i++)
        {
            var o = _list[i];
            cards[i].Data = o;
            if (o is WeaponSkinData s)
            {
                var w = Meta.Equipment != null ? Meta.Equipment.Get(s.WeaponId) : null;
                bool own = PlayerSave.OwnsSkin(s.Id);
                bool on = PlayerSave.EquippedSkin(s.WeaponId) == s.Id;
                string sub = _tab == 1 ? MetaUi.ItemName(w) : own ? MetaUi.ItemName(w) : PriceText(s.PriceSC);
                cards[i].Set(s.Name, sub, s.Icon != null ? s.Icon : (w != null ? ItemThumbnail.Get(w) : null), null,
                             MetaUi.Grade(s.Grade).Color, false, Badge(o, own, on));
            }
            else if (o is PartData p)
            {
                bool own = PlayerSave.OwnsPart(p.Id);
                bool on = PlayerSave.EquippedPart(p.Slot) == p.Id;
                cards[i].Set(p.Name, MetaUi.SlotName(p.Slot), p.Icon, null, MetaUi.Grade(p.Grade).Color, false, Badge(o, own, on));
            }
            cards[i].SetSelected(o == _sel && o != null);
        }
    }

    string Badge(object o, bool own, bool on) => _new.Contains(o) ? "NEW" : on ? "장착" : own ? "보유" : null;

    static string PriceText(int sc) => sc > 0 ? $"{sc:N0} SC" : "뽑기 전용";

    // ══════════════════ 자세히 ══════════════════

    void Pick(object o)
    {
        _sel = o;
        Frame.Clear();
        foreach (var c in Frame.Cards) c.SetSelected(c.gameObject.activeSelf && o != null && c.Data == o);

        if (o is WeaponSkinData s) ShowSkin(s);
        else if (o is PartData p) ShowPart(p);

        if (_tab == 1) ShowGacha(o == null);
    }

    void ShowSkin(WeaponSkinData s)
    {
        var w = Meta.Equipment != null ? Meta.Equipment.Get(s.WeaponId) : null;
        var model = WeaponSkins.ModelFor(w, s);
        if (model != null) Frame.ShowModel(model, s);
        else Frame.ShowIcon(s.Icon, false);

        var g = MetaUi.Grade(s.Grade);
        Frame.SetPaint(g.Color);
        TitlePanelFrame.Set(Frame.Name, s.Name);
        TitlePanelFrame.Set(Frame.Sub, $"<color={Hex(g.Color)}>{g.Name}</color> · {MetaUi.ItemName(w)} 스킨");
        if (_tab == 1) return;

        Frame.SetBody(s.InGacha ? "뽑기에서도 나옵니다" : "");
        bool own = PlayerSave.OwnsSkin(s.Id);
        bool on = PlayerSave.EquippedSkin(s.WeaponId) == s.Id;
        if (!own)
        {
            bool canBuy = s.PriceSC > 0 && PlayerSave.SC >= s.PriceSC;
            Frame.Action1(s.PriceSC > 0 ? $"사기 · {s.PriceSC:N0} SC" : "뽑기 전용", () => Buy(s), canBuy);
            if (s.PriceSC > 0 && !canBuy) Frame.SetNote($"SC가 {s.PriceSC - PlayerSave.SC:N0} 부족합니다");
        }
        else if (!on) Frame.Action1("장착", () => { PlayerSave.EquipSkin(s.WeaponId, s.Id); PlayerSave.Commit(); Pick(s); });
        else Frame.Action1("장착 해제", () => { PlayerSave.EquipSkin(s.WeaponId, 0); PlayerSave.Commit(); Pick(s); });
        if (_focusWeapon != 0) Frame.Action2("모든 스킨", () => { _focusWeapon = 0; Refresh(); });
    }

    void ShowPart(PartData p)
    {
        Frame.ShowIcon(p.Icon, false);
        var g = MetaUi.Grade(p.Grade);
        TitlePanelFrame.Set(Frame.Name, p.Name);
        TitlePanelFrame.Set(Frame.Sub, $"<color={Hex(g.Color)}>{g.Name}</color> · {MetaUi.SlotName(p.Slot)}");
    }

    void Buy(WeaponSkinData s)
    {
        if (!PlayerSave.TrySpendSC(s.PriceSC)) { Frame.SetNote("SC가 부족합니다"); return; }
        PlayerSave.GrantSkin(s.Id);
        PlayerSave.EquipSkin(s.WeaponId, s.Id);
        PlayerSave.Commit();
        Pick(s);
        Frame.SetNote("구매했습니다 — 바로 장착합니다");
    }

    // ══════════════════ 뽑기 ══════════════════

    void ShowGacha(bool title)
    {
        var t = Table;
        if (title)
        {
            TitlePanelFrame.Set(Frame.Name, "뽑기");
            TitlePanelFrame.Set(Frame.Sub, $"티켓 {PlayerSave.Tickets} · {PlayerSave.SC:N0} SC");
        }

        var sb = new StringBuilder();
        if (t == null) sb.AppendLine("뽑기 표가 비어 있습니다");
        else
        {
            float sum = t.Grades.Sum(x => Mathf.Max(0f, x.Weight));
            foreach (var g in t.Grades)
                sb.AppendLine($"<color={Hex(g.Color)}>{g.Name}</color>  {(sum > 0 ? g.Weight / sum : 0f) * 100f:0.#}%" +
                              $"   <color=#9A93A8>겹치면 {g.RefundSC} SC</color>");
        }
        if (_results.Count > 0)
        {
            sb.AppendLine().AppendLine("<b>방금 뽑은 것</b>");
            foreach (var r in _results) sb.AppendLine(r);
        }
        Frame.SetBody(sb.ToString().TrimEnd());

        if (t == null) return;
        Frame.Action1(CostText(1, t.SingleTickets, t.SingleSC), () => Pull(1), CanPay(t.SingleTickets, t.SingleSC));
        Frame.Action2(CostText(10, t.TenTickets, t.TenSC), () => Pull(10), CanPay(t.TenTickets, t.TenSC));
    }

    static bool CanPay(int tickets, int sc) =>
        (tickets > 0 && PlayerSave.Tickets >= tickets) || (sc > 0 && PlayerSave.SC >= sc);

    static string CostText(int n, int tickets, int sc) =>
        tickets > 0 && PlayerSave.Tickets >= tickets ? $"{n}번 뽑기 · 티켓 {tickets}" : $"{n}번 뽑기 · {sc:N0} SC";

    static bool Pay(int tickets, int sc) =>
        (tickets > 0 && PlayerSave.TrySpendTickets(tickets)) || (sc > 0 && PlayerSave.TrySpendSC(sc));

    void Pull(int n)
    {
        var t = Table;
        var db = Db;
        if (t == null || db == null) return;
        var pool = new List<object>();
        pool.AddRange(db.Parts.Where(p => p != null && p.InGacha && !db.IsFree(p.Id)));
        pool.AddRange(db.WeaponSkins.Where(s => s != null && s.InGacha));
        if (pool.Count == 0) { Frame.SetNote("뽑기에서 나올 항목이 없습니다"); return; }

        if (!Pay(n == 1 ? t.SingleTickets : t.TenTickets, n == 1 ? t.SingleSC : t.TenSC))
        {
            Frame.SetNote("티켓 · SC가 부족합니다");
            return;
        }

        _results.Clear();
        _new.Clear();
        int refund = 0;
        for (int i = 0; i < n; i++)
        {
            var grade = t.RollGrade(_rng);
            var same = pool.Where(o => GradeOf(o) == grade).ToList();
            if (same.Count == 0) same = pool;
            var got = same[_rng.Next(same.Count)];
            var gi = MetaUi.Grade(GradeOf(got));
            bool dup = Owns(got);
            if (dup)
            {
                refund += gi.RefundSC;
                PlayerSave.AddSC(gi.RefundSC);
            }
            else
            {
                if (got is PartData p) PlayerSave.GrantPart(p.Id);
                else if (got is WeaponSkinData s) PlayerSave.GrantSkin(s.Id);
                _new.Add(got);
            }
            _results.Add($"<color={Hex(gi.Color)}>{gi.Name}</color> {NameOf(got)}" +
                         (dup ? $" <color=#9A93A8>(겹침 +{gi.RefundSC} SC)</color>" : " <color=#7CFFB2>NEW</color>"));
        }
        PlayerSave.Commit();
        Pick(_sel);
        if (refund > 0) Frame.SetNote($"중복된 항목으로 {refund:N0} SC를 돌려받았습니다");
    }

    static CosmeticGrade GradeOf(object o) => o is PartData p ? p.Grade : o is WeaponSkinData s ? s.Grade : CosmeticGrade.Common;
    static bool Owns(object o) => o is PartData p ? PlayerSave.OwnsPart(p.Id) : o is WeaponSkinData s && PlayerSave.OwnsSkin(s.Id);
    static string NameOf(object o) => o is PartData p ? p.Name : o is WeaponSkinData s ? s.Name : "";
}
