using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 암시장 창 — 씬에 없어도 저절로 생김 (v7 그래피티, 코드로 만듦).
/// 진열대(GearOfferPage — 상점 한정판과 같은 모양): 왼쪽 카드 격자 (뒤 스프레이 = 등급 색) / 오른쪽 자세히 + 구매.
/// 닫기: Esc · 상호작용 키 · 멀어지거나 전투가 시작되면 저절로
/// </summary>
public class BlackMarketWindow : MonoBehaviour
{
    static BlackMarketWindow _inst;
    public static bool IsOpen { get; private set; }
    /// <summary>닫은 프레임 (같은 Esc로 ESC 메뉴가 또 열리지 않게)</summary>
    public static int ClosedFrame { get; private set; } = -10;

    BlackMarket _market;
    RectTransform _root;
    TMP_Text _title, _soul;
    GearOfferPage _page;
    int _openFrame;
    int _serial = -1;              // 전역 암시장 — 연 때의 회차 (바뀌면 다시 채움)
    bool _closingAfterBuy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_inst != null) return;
        var go = new GameObject("BlackMarketWindow");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<BlackMarketWindow>();
    }

    void Awake()
    {
        if (_inst != null && _inst != this) { Destroy(gameObject); return; }
        _inst = this;
    }

    void OnDestroy()
    {
        if (_inst == this) { _inst = null; IsOpen = false; }
    }

    // ══════════════════ 열기 · 닫기 ══════════════════

    public static void Open(BlackMarket m)
    {
        if (m == null) return;
        if (_inst == null) AutoCreate();
        _inst.Show(m);
    }

    public static void Close()
    {
        if (_inst != null) _inst.Hide();
    }

    void Show(BlackMarket m)
    {
        Build();
        _market = m;
        _openFrame = Time.frameCount;
        _closingAfterBuy = false;
        K.Set(_title, m.Name);
        _root.gameObject.SetActive(true);
        IsOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Fill();
    }

    void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ClosedFrame = Time.frameCount;
        if (_root != null) _root.gameObject.SetActive(false);
        _market = null;
        K.DropButtonFocus();
        EscMenu.RelockCursor();
    }

    // ══════════════════ 만들기 ══════════════════

    void Build()
    {
        if (_root != null) return;
        var font = UiFont.Get();
        K.EnsureEventSystem();

        var screen = K.MakeScreen(gameObject, 465, K.DimColor);
        _root = screen;

        var win = K.Window("Window", screen, 1400f, 860f, out var content);
        K.StripesOn(content.GetComponent<Image>(), 0.02f);       // 창 바탕만 옅은 망점

        // 위 — 제목 · 소울 · 닫기
        var top = K.Row("Top", content, 72f, 16f);
        _title = K.Header(top.transform, font, "암시장", 44f);
        var header = _title.transform;
        while (header.parent != null && header.name != "Header") header = header.parent;
        K.Size(header, -1f, -1f, 1f);
        K.Tag(top.transform, font, "BLACK MARKET", K.Purple, 18f, out _, "Tag").localRotation = K.Tilt(-4f);
        _soul = K.Text("Soul", top.transform, K.Display(font), 30f, K.Gold, TextAlignmentOptions.MidlineRight, true);
        K.Size(_soul, 260f, 60f);
        var close = K.Btn("Close", top.transform, font, SetLoc.T("market.close", "닫기"), K.Raised, 22f, out var ct);
        K.Size(close, 150f, 58f);
        SetLocText.Ensure(ct, "market.close");
        close.onClick.AddListener(Hide);

        // 진열대 (상점 한정판과 같은 모양)
        var host = K.Rect("Offers", content);
        K.Size(host, -1f, -1f, -1f, 1f);
        _page = GearOfferBuilder.Build(host, font);
        _page.CanBuy = o =>
        {
            if (_market == null) return (false, "");
            if (o.Market != null)                                     // 전역 암시장 — 상품은 MarketDirector가 정함
            {
                if (!_market.CanUse(out var r)) return (false, r);
                bool can = GearPurchase.CanBuy(o.Item, o.Price, out var r2);
                return (can, r2);
            }
            bool ok = _market.CanBuy((BlackMarket.Entry)o.Tag, out var why);
            return (ok, why);
        };
        _page.Buy = o =>
        {
            if (_market == null) return (false, "");
            if (o.Market != null)
            {
                if (!_market.CanUse(out var r)) return (false, r);
                if (!GearPurchase.Buy(o.Market, out var msg)) return (false, msg);
                // 하나라도 사면 이 암시장은 닫힘 — 다음 암시장은 7~11분 뒤 다른 곳에서
                MarketDirector.Instance?.MarkBought();
                AltarHud.Toast(SetLoc.F("market.bought", "{0}을(를) 구매했습니다", o.Item.GetName()));
                _closingAfterBuy = true;
                return (true, msg);
            }
            bool ok = _market.Buy((BlackMarket.Entry)o.Tag, out var msg2);
            return (ok, msg2);
        };

        K.Finish(_root);                       // v7 외곽선 굵기
        _root.gameObject.SetActive(false);
    }

    void Fill()
    {
        if (_market == null || _page == null) return;
        var offers = new List<GearOfferPage.Offer>();
        _serial = -1;
        if (BlackMarket.Global)
        {
            var dir = MarketDirector.Instance;
            _serial = dir.Serial;
            foreach (var o in dir.Stock()) offers.Add(GearOfferPage.Offer.From(o));
            _page.SetOffers(offers);
            _page.SetHeader(SetLoc.T("market.header", "단 한 번의 거래"));
            return;
        }
        foreach (var e in _market.Items())
            if (e != null && e.Item != null)
                offers.Add(new GearOfferPage.Offer { Item = e.Item, Enchant = e.Enchant, Price = _market.PriceOf(e), Tag = e });
        _page.SetOffers(offers);
    }

    // ══════════════════ 매 프레임 ══════════════════

    void Update()
    {
        if (!IsOpen) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_market == null) { Hide(); return; }

        // 닫기 — Esc · 상호작용 키(연 프레임 제외) · 멀어짐 · 전투
        if (Input.GetKeyDown(KeyCode.Escape) || (Time.frameCount > _openFrame && KeyBinds.Down(GameAction.Interact)))
        { Hide(); return; }
        if (_closingAfterBuy) { Hide(); return; }
        if (!_market.CanUse(out string why))
        {
            Hide();
            if (!string.IsNullOrEmpty(why)) AltarHud.Prompt(why);
            return;
        }
        if (BlackMarket.Global && MarketDirector.Instance.Serial != _serial) Fill();

        var me = PlayerAttack.Local;
        int soul = me != null && me.TryGetComponent<PlayerHealth>(out var hp) ? hp.Soul : 0;
        K.Set(_soul, SetLoc.F("market.soul", "◆ {0}", soul));
        _page?.Refresh();
    }

    static string KindText(EquipmentData d)
    {
        if (d.IsArmorPiece) return LocUtil.Get($"ui.item.slot.{d.Slot}");
        return d.Class == WeaponClass.Melee ? LocUtil.Get("ui.item.class.Melee") : LocUtil.Get($"ui.item.ranged_kind.{d.RangedKind}");
    }

    /// <summary>아이템 설명 — 이름 · 종류 · 주요 능력치 · 기초 스탯 · 설명 · 고유 특성 · 특수 인챈트</summary>
    public static string Describe(EquipmentData d, ArmorEnchantData ench)
    {
        var sb = new StringBuilder();
        sb.Append("<size=120%><b>").Append(d.GetName()).AppendLine("</b></size>");
        sb.Append("<color=#A9B0BA>").Append(LocUtil.Get($"ui.item.grade.{d.Grade}")).Append(" · ").Append(KindText(d)).AppendLine("</color>");
        sb.AppendLine();

        if (!Mathf.Approximately(d.MainStatValue, 0f) && EquipmentData.MainStatOf(d.Slot, out var ms, out var mm))
            sb.AppendLine("<b>" + StatText.Line(ms, mm, d.MainStatValue, "  ") + "</b>");
        if (d.BaseStats != null)
            foreach (var s in d.BaseStats)
                sb.AppendLine(StatText.Line(s.Stat, s.Type, s.Value, "  "));

        string desc = LocUtil.GetOr($"{Prefix(d)}.{d.Id}.desc", LocUtil.Resolve(d.Description));
        if (!string.IsNullOrWhiteSpace(desc) && !desc.StartsWith("#")) { sb.AppendLine(); sb.AppendLine(desc); }

        if (d.Traits != null && d.Traits.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(SetLoc.T("ui.item.traits", "고유 특성"));
            foreach (var t in d.Traits) if (t != null) sb.Append("  ").AppendLine(t.Describe());
        }

        if (d.IsArmorPiece)
        {
            sb.AppendLine();
            if (ench != null) sb.AppendLine(ench.Describe());
            else sb.Append("<color=#A9B0BA>").Append(SetLoc.T("ui.item.armor_enchant_empty", "◇ 특수 인챈트 없음")).AppendLine("</color>");
        }
        return sb.ToString().TrimEnd();
    }

    static string Prefix(EquipmentData d) => d.Slot switch
    {
        EquipSlot.Armor => "armor",
        EquipSlot.Helmet => "helmet",
        EquipSlot.Boots => "boots",
        _ => "weapon",
    };
}
