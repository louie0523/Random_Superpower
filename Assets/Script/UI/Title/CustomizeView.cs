using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 꾸미기 — 칸 탭(머리 · 몸 · 안경 · 모자 · 손 치장 … CosmeticDatabase의 칸 목록 그대로) · 칸마다 하나.
/// 카드를 누르면 가운데 캐릭터가 바로 입어 봄 (저장 안 함). 가진 건 '장착', 없는 건 SC로 사기 (값 0 = 뽑기에서만).
/// 창을 닫으면 저장된 모습으로 돌아감
/// </summary>
public class CustomizeView : TitlePanel
{
    int _tab;
    PartData _sel;
    bool _none;     // '없음' · '기본' 카드 고름
    readonly List<PartData> _list = new();
    readonly List<SlotDef> _slots = new();

    static CosmeticDatabase Db => Meta != null ? Meta.Cosmetics : null;
    SlotDef Slot => _tab >= 0 && _tab < _slots.Count ? _slots[_tab] : null;

    public override void Show()
    {
        Frame.Open();
        _slots.Clear();
        var src = Db != null && Db.Slots != null && Db.Slots.Length > 0 ? Db.Slots : CosmeticDatabase.DefaultSlots();
        _slots.AddRange(src.Where(s => s != null));
        _tab = Mathf.Clamp(_tab, 0, Mathf.Max(0, _slots.Count - 1));
        Frame.Tabs(_slots.Select(s => s.Name).ToArray(), _tab, i => { _tab = i; _sel = null; Refresh(); });
        Refresh();
    }

    protected override void OnClosed()
    {
        if (TitleStage.Instance != null) TitleStage.Instance.EndPreview();
    }

    protected override void OnSave() => Paint();

    /// <summary>비운 칸 카드 — 비울 수 있는 칸 = '없음', 기본 모델 그대로인 칸 = '기본', 기본 부위가 정해진 칸 = 없음</summary>
    bool HasEmptyCard => Slot != null && (!Slot.Required || Slot.DefaultPart <= 0);
    string EmptyName => Slot != null && Slot.Required ? "기본" : "없음";

    int Current => Slot == null ? 0 : PlayerSave.EquippedPart(Slot.Key) is var id && id > 0 && PlayerSave.OwnsPart(id) ? id : Slot.DefaultPart;

    protected override void Refresh()
    {
        _list.Clear();
        if (Slot == null) { Frame.Layout(0, null); Frame.Clear(); return; }
        if (HasEmptyCard) _list.Add(null);
        if (Db != null) _list.AddRange(Db.PartsIn(Slot.Key).Where(p => p != null).OrderBy(p => p.Grade).ThenBy(p => p.Id));
        Frame.SetEmpty("");

        Frame.Layout(_list.Count, c => Pick(c.Data as PartData, c.Data == null));
        Paint();

        int cur = Current;
        _sel = _list.FirstOrDefault(p => p != null && p.Id == cur);
        Pick(_sel, _sel == null);
        if (Db == null) Frame.SetNote("GameMeta에 꾸미기 목록(Cosmetics)이 비어 있어");
    }

    void Paint()
    {
        var cards = Frame.Cards;
        int cur = Current;
        for (int i = 0; i < _list.Count && i < cards.Count; i++)
        {
            var p = _list[i];
            cards[i].Data = p;
            if (p == null)
            {
                cards[i].Set(EmptyName, "", null, null, null, false, cur <= 0 ? "장착" : null);
                cards[i].SetSelected(_none);
                continue;
            }
            bool own = PlayerSave.OwnsPart(p.Id);
            string sub = own ? MetaUi.Grade(p.Grade).Name : p.PriceSC > 0 ? $"{p.PriceSC:N0} SC" : "뽑기 전용";
            cards[i].Set(p.Name, sub, p.Icon, null, MetaUi.Grade(p.Grade).Color, false, cur == p.Id ? "장착" : own ? "보유" : null);
            cards[i].SetSelected(!_none && p == _sel);
        }
    }

    void Pick(PartData p, bool none)
    {
        _sel = p;
        _none = none || p == null;
        Frame.Clear();
        Paint();
        var slot = Slot;
        if (slot == null) return;

        // 가운데 캐릭터가 입어 봄
        var look = PlayerSave.Look;
        look.Set(slot.Key, _none ? 0 : p.Id);
        if (TitleStage.Instance != null) TitleStage.Instance.Preview(look);

        int cur = Current;
        if (_none)
        {
            TitlePanelFrame.Set(Frame.Name, EmptyName);
            TitlePanelFrame.Set(Frame.Sub, slot.Name);
            if (cur > 0) Frame.Action1(slot.Required ? "기본으로" : "벗기", () => Equip(0));
            return;
        }

        Frame.ShowIcon(p.Icon, false);
        var g = MetaUi.Grade(p.Grade);
        TitlePanelFrame.Set(Frame.Name, p.Name);
        TitlePanelFrame.Set(Frame.Sub, $"<color={Hex(g.Color)}>{g.Name}</color> · {slot.Name}");

        bool own = PlayerSave.OwnsPart(p.Id);
        if (!own)
        {
            bool can = p.PriceSC > 0 && PlayerSave.SC >= p.PriceSC;
            Frame.Action1(p.PriceSC > 0 ? $"사기 · {p.PriceSC:N0} SC" : "뽑기 전용", () => Buy(p), can);
            if (p.PriceSC > 0 && !can) Frame.SetNote($"SC가 {p.PriceSC - PlayerSave.SC:N0} 모자라요");
        }
        else if (cur != p.Id) Frame.Action1("장착", () => Equip(p.Id));
        else if (HasEmptyCard) Frame.Action1(slot.Required ? "기본으로" : "벗기", () => Equip(0));
        if (cur != p.Id) Frame.Action2("되돌리기", () => { int c = Current; var e = _list.FirstOrDefault(x => x != null && x.Id == c); Pick(e, e == null); });
    }

    void Buy(PartData p)
    {
        if (!PlayerSave.TrySpendSC(p.PriceSC)) { Frame.SetNote("SC가 모자라요"); return; }
        PlayerSave.GrantPart(p.Id);
        Equip(p.Id);
    }

    void Equip(int id)
    {
        var slot = Slot;
        if (slot == null) return;
        PlayerSave.EquipPart(slot.Key, id);
        PlayerSave.Commit();
        CharacterPhoto.ClearCache();
        int cur = Current;
        var e = _list.FirstOrDefault(x => x != null && x.Id == cur);
        Pick(e, e == null);
    }
}
