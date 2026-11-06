using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 · 방어구 진열대 (암시장 · 상점 한정판이 같이 씀) — 왼쪽 카드 격자 (등급 색 스프레이 · 그림 · 이름 · 가격) · 오른쪽 자세히 + 구매.
/// 무엇을 팔지 · 살 수 있나 · 사기는 쓰는 쪽이 정함 (SetOffers · CanBuy · Buy)
/// GearOfferBuilder가 만듦
/// </summary>
public class GearOfferPage : MonoBehaviour
{
    public class Offer
    {
        public EquipmentData Item;
        public ArmorEnchantData Enchant;
        public int Price;
        public bool Sold;
        public object Tag;
        /// <summary>상점 한정판 · 암시장 상품이면 (강화 · 할인 · 행방불명 표시)</summary>
        public MarketOffer Market;

        public static Offer From(MarketOffer m, bool sold = false) => new()
        {
            Item = m.Item,
            Enchant = m.ArmorEnchant,
            Price = m.Price,
            Sold = sold,
            Tag = m,
            Market = m,
        };
    }

    [Header("위")]
    [Tooltip("한정판 남은 시간 등 (비우면 숨김)")]
    public TMP_Text Header;

    [Header("카드")]
    public RectTransform Grid;
    public UiItemCard CardTemplate;
    public TMP_Text Empty;

    [Header("자세히")]
    public Image DetailPaint;
    public Image DetailIcon;
    public TMP_Text DetailName;
    public TMP_Text DetailSub;
    public TMP_Text DetailBody;
    public ScrollRect DetailScroll;
    public Button BuyButton;
    public TMP_Text BuyLabel;
    public TMP_Text Msg;

    /// <summary>살 수 있나 (이유는 번역된 글)</summary>
    public Func<Offer, (bool ok, string why)> CanBuy;
    /// <summary>사기 — 결과 글</summary>
    public Func<Offer, (bool ok, string msg)> Buy;

    readonly List<Offer> _offers = new();
    readonly List<UiItemCard> _cards = new();
    Offer _sel;
    float _msgUntil;
    bool _awake;
    Color _face = Color.white;

    void Awake()
    {
        if (_awake) return;
        _awake = true;
        if (CardTemplate != null)
        {
            if (CardTemplate.Face != null) _face = CardTemplate.Face.color;
            CardTemplate.gameObject.SetActive(false);
        }
        if (BuyButton != null) BuyButton.onClick.AddListener(DoBuy);
        SetMsg("", true);
    }

    public void SetHeader(string s)
    {
        if (Header == null) return;
        Header.text = s ?? "";
        Header.gameObject.SetActive(!string.IsNullOrEmpty(s));
    }

    /// <summary>진열 (같은 물건 묶음이면 고른 것 유지)</summary>
    public void SetOffers(IEnumerable<Offer> offers, string emptyText = null)
    {
        Awake();
        _offers.Clear();
        if (offers != null) foreach (var o in offers) if (o != null && o.Item != null) _offers.Add(o);

        while (_cards.Count < _offers.Count && CardTemplate != null)
        {
            var c = Instantiate(CardTemplate, Grid);
            c.Button.onClick.AddListener(() => Select(c.Data as Offer));
            _cards.Add(c);
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = i < _offers.Count;
            _cards[i].gameObject.SetActive(on);
            _cards[i].Data = on ? _offers[i] : null;
        }
        if (Empty != null)
        {
            Empty.text = emptyText ?? SetLoc.T("shop.gear.empty", "판매 중인 물건이 없습니다");
            Empty.gameObject.SetActive(_offers.Count == 0);
        }

        var keep = _sel != null ? _offers.Find(o => o.Item == _sel.Item && o.Enchant == _sel.Enchant) : null;
        Select(keep ?? (_offers.Count > 0 ? _offers[0] : null));
        Refresh();
    }

    public void Select(Offer o)
    {
        _sel = o;
        foreach (var c in _cards) c.SetSelected(c.gameObject.activeSelf && c.Data == o && o != null);
        Detail();
    }

    /// <summary>매 프레임 — 가격 · 품절 · 살 수 있나</summary>
    public void Refresh()
    {
        for (int i = 0; i < _offers.Count && i < _cards.Count; i++)
        {
            var o = _offers[i];
            var d = o.Item;
            var m = o.Market;
            string badge = o.Sold ? SetLoc.T("shop.gear.sold", "품절")
                         : m != null && m.Lost ? SetLoc.T("shop.gear.lost", "행방불명")
                         : m != null && m.Discount > 0f ? $"-{Mathf.RoundToInt(m.Discount * 100f)}%"
                         : o.Enchant != null && d.IsArmorPiece ? "◆" : null;
            string title = (m != null && m.Stage > 0 ? $"<color=#FFD447>+{m.Stage}</color> " : "") + d.GetName();
            string sub = m != null && m.Discount > 0f ? $"<s><color=#8A8494>{m.FullPrice}</color></s> ◆ {o.Price}" : $"◆ {o.Price}";
            if (m != null && m.Specials > 0) sub += $"  <color=#C08BFF>★{m.Specials}</color>";
            _cards[i].Set(title, sub, ItemThumbnail.Get(d), null, MetaUi.ItemGradeColor(d.Grade), false, badge);
            if (_cards[i].Face != null) _cards[i].Face.color = o.Sold ? Color.Lerp(_face, Color.black, 0.45f) : _face;
        }

        if (BuyButton != null)
        {
            bool has = _sel != null;
            BuyButton.gameObject.SetActive(has);
            if (has)
            {
                var (ok, _) = CanBuy != null ? CanBuy(_sel) : (true, null);
                BuyButton.interactable = ok && !_sel.Sold;
                if (BuyLabel != null)
                    BuyLabel.text = _sel.Sold ? SetLoc.T("shop.gear.sold", "품절") : SetLoc.F("shop.gear.buy", "◆ {0}  구매", _sel.Price);
            }
        }
        if (_msgUntil > 0f && Time.unscaledTime > _msgUntil) SetMsg("", true);
    }

    void Detail()
    {
        var o = _sel;
        if (DetailIcon != null)
        {
            DetailIcon.sprite = o != null ? ItemThumbnail.Get(o.Item) : null;
            DetailIcon.enabled = DetailIcon.sprite != null;
        }
        if (DetailPaint != null && o != null)
        {
            var g = MetaUi.ItemGradeColor(o.Item.Grade);       // 뒤 스프레이 = 등급 색
            DetailPaint.color = new Color(g.r, g.g, g.b, 0.85f);
        }
        if (DetailName != null) DetailName.text = o == null ? "" : (o.Market != null && o.Market.Stage > 0 ? $"+{o.Market.Stage} " : "") + o.Item.GetName();
        if (DetailSub != null)
            DetailSub.text = o != null
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(MetaUi.ItemGradeColor(o.Item.Grade))}>{MetaUi.ItemGradeName(o.Item.Grade)}</color> · {MetaUi.ItemKind(o.Item)}"
                  + (o.Item.BlackMarketOnly ? " · <color=#C08BFF>" + SetLoc.T("shop.gear.market_only", "암시장 전용") + "</color>" : "")
                  + (o.Market != null && o.Market.Lost ? " · <color=#FF6B6B>" + SetLoc.T("shop.gear.lost", "행방불명") + "</color>" : "")
                : "";
        if (DetailBody != null)
        {
            DetailBody.text = o != null ? BodyOf(o) : "";
            if (DetailScroll != null && DetailScroll.content != null) DetailScroll.content.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>능력치 · 설명 · 특성 · 특수 인챈트 (이름 · 종류 줄은 위에 따로)</summary>
    static string BodyOf(Offer o)
    {
        var sb = new System.Text.StringBuilder();
        var m = o.Market;
        if (m != null)
        {
            // 가격 · 강화 · 인챈트
            if (m.Discount > 0f)
                sb.Append("<color=#8A8494><s>◆ ").Append(m.FullPrice).Append("</s></color>  ")
                  .Append("<b>◆ ").Append(m.Price).Append("</b>  <color=#7CFFB2>-").Append(Mathf.RoundToInt(m.Discount * 100f)).AppendLine("%</color>");
            if (m.Stage > 0)
            {
                sb.Append("<b><color=#FFD447>+").Append(m.Stage).Append(' ').Append(SetLoc.T("shop.gear.stage", "강화")).AppendLine("</color></b>");
                var en = MarketStock.LocalEnchant;
                var db = en != null ? en.Database : null;
                if (db != null)
                {
                    var normal = new Dictionary<int, int>();
                    var special = new Dictionary<int, int>();
                    foreach (var c in m.Codes)
                    {
                        if (EnchantCode.IsSpecial(c))
                        {
                            int id = EnchantCode.SpecialId(c), t = EnchantCode.Tier(c);
                            special[id] = special.TryGetValue(id, out var old) ? Mathf.Max(old, t) : t;
                        }
                        else normal[c] = normal.TryGetValue(c, out var n) ? n + 1 : 1;
                    }
                    foreach (var kv in special)
                    {
                        var s = db.GetSpecial(kv.Key);
                        if (s != null) sb.Append("  <color=#").Append(ColorUtility.ToHtmlStringRGB(s.Color)).Append(">★ ").Append(s.Name(kv.Value)).AppendLine("</color>");
                    }
                    foreach (var kv in normal)
                    {
                        var e = db.Get(kv.Key);
                        if (e != null) sb.Append("  ").Append(EnchantSummary.ColoredName(e)).Append(kv.Value > 1 ? $" ×{kv.Value}" : "").AppendLine();
                    }
                }
            }
            if (sb.Length > 0) sb.AppendLine();
        }

        string all = BlackMarketWindow.Describe(o.Item, o.Item.IsArmorPiece ? o.Enchant : null);
        // Describe의 앞 두 줄(이름 · 등급 줄)과 빈 줄은 빼고
        var lines = all.Split('\n');
        int skip = Mathf.Min(lines.Length, 3);
        sb.Append(string.Join("\n", lines, skip, lines.Length - skip).Trim());
        return sb.ToString().Trim();
    }

    void DoBuy()
    {
        if (_sel == null || Buy == null) return;
        var (ok, msg) = Buy(_sel);
        SetMsg(msg, ok);
        Refresh();
        GameUiKit.DropButtonFocus();
    }

    public void SetMsg(string s, bool good)
    {
        if (Msg == null) return;
        Msg.color = good ? GameUiKit.Ok : GameUiKit.Red;
        Msg.text = s ?? "";
        _msgUntil = string.IsNullOrEmpty(s) ? 0f : Time.unscaledTime + 3f;
    }
}
