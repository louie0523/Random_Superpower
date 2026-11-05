using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 암시장 창 — 씬에 없어도 저절로 생김 (설정 창과 같은 카툰 모양, 코드로 만듦).
/// 왼쪽: 물건 목록 (이름 · 종류 · [◆ 가격]) / 오른쪽: 고른 물건 자세히.
/// 닫기: Esc · 상호작용 키 · 멀어지거나 전투가 시작되면 저절로
/// </summary>
public class BlackMarketWindow : MonoBehaviour
{
    static BlackMarketWindow _inst;
    public static bool IsOpen { get; private set; }
    /// <summary>닫은 프레임 (같은 Esc로 ESC 메뉴가 또 열리지 않게)</summary>
    public static int ClosedFrame { get; private set; } = -10;

    BlackMarket _market;
    RectTransform _root, _list;
    TMP_Text _title, _soul, _detail, _msg;
    float _msgUntil;
    int _openFrame;
    BlackMarket.Entry _selected;

    class Row
    {
        public BlackMarket.Entry E;
        public UiListRow V;
    }
    readonly List<Row> _rows = new();

    static readonly string[] GradeHex = { "#A9B0BA", "#5BC8FF", "#C08BFF", "#FFD447" };   // 어두운 판 위에서 잘 보이게

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
        _selected = null;
        _openFrame = Time.frameCount;
        FillRows();
        K.Set(_title, m.Name);
        K.Set(_msg, "");
        _root.gameObject.SetActive(true);
        IsOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RefreshDetail();
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

        var win = K.Window("Window", screen, 1180f, 740f, out var content);
        _title = K.Header(content, font, "암시장");

        _soul = K.Text("Soul", content, font, 24f, K.Gold, TextAlignmentOptions.MidlineLeft, true);
        K.Size(_soul, -1f, 32f);

        // 본문 — 왼쪽 목록 · 오른쪽 자세히
        var body = K.Rect("Body", content);
        K.Size(body, -1f, 470f, 1f, 1f);
        var h = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 18f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var listHost = K.Rect("ListHost", body);
        K.Size(listHost, 640f, -1f, 0f, 1f);
        _list = K.Scroll("List", listHost, out _, 8f);
        K.Fill((RectTransform)_list.parent.parent);

        var detailHost = K.Rect("DetailHost", body);
        K.Size(detailHost, -1f, -1f, 1f, 1f);
        var face = K.Sticker(detailHost, K.Field, 0f, 0f);
        _detail = K.Text("Detail", face.transform, font, 20f, K.Ink, TextAlignmentOptions.TopLeft);
        _detail.textWrappingMode = TextWrappingModes.Normal;
        _detail.overflowMode = TextOverflowModes.Overflow;
        _detail.richText = true;
        K.Fill(_detail.rectTransform, 16f);

        // 아래 — 알림 · 닫기
        var bottom = K.Row("Bottom", content, 56f, 12f);
        _msg = K.Text("Msg", bottom.transform, font, 22f, K.Muted, TextAlignmentOptions.MidlineLeft);
        K.Size(_msg, -1f, -1f, 1f);
        var close = K.Btn("Close", bottom.transform, font, SetLoc.T("market.close", "닫기"), K.Raised, 22f, out var ct);
        K.Size(close, 160f);
        SetLocText.Ensure(ct, "market.close");
        close.onClick.AddListener(Hide);

        K.Finish(_root);                       // v7 외곽선 굵기
        _root.gameObject.SetActive(false);
    }

    void FillRows()
    {
        // 전부 지우고 다시 (섹션 제목 포함)
        for (int i = _list.childCount - 1; i >= 0; i--)
        {
            var c = _list.GetChild(i);
            c.SetParent(null);
            Destroy(c.gameObject);
        }
        _rows.Clear();
        if (_market == null) return;

        var font = UiFont.Get();
        bool? lastWeapon = null;
        foreach (var e in _market.Items())
        {
            if (e == null || e.Item == null) continue;
            bool w = e.Item.IsWeapon;
            if (lastWeapon != w)
            {
                K.Section(_list, font, w ? SetLoc.T("market.section.weapons", "무기") : SetLoc.T("market.section.armor", "방어구"));
                lastWeapon = w;
            }

            var v = K.ListRow("Item", _list, font, true, "", 62f, true);
            var row = new Row { E = e, V = v };
            v.Button.onClick.AddListener(() => { _selected = row.E; RefreshDetail(); });
            v.Action.onClick.AddListener(() => BuyRow(row));
            _rows.Add(row);
        }
        UiTextFit.Apply(_list);     // 긴 이름 · 다른 언어 — 줄이기
    }

    void BuyRow(Row r)
    {
        if (_market == null) return;
        _selected = r.E;
        bool ok = _market.Buy(r.E, out string why);
        ShowMsg(why, ok);
        RefreshDetail();
    }

    void ShowMsg(string s, bool good)
    {
        if (_msg == null) return;
        _msg.color = good ? K.Ok : K.Red;
        K.Set(_msg, s ?? "");
        _msgUntil = Time.time + 3f;
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
        if (!_market.CanUse(out string why))
        {
            Hide();
            if (!string.IsNullOrEmpty(why)) AltarHud.Prompt(why);
            return;
        }

        var me = PlayerAttack.Local;
        int soul = me != null && me.TryGetComponent<PlayerHealth>(out var hp) ? hp.Soul : 0;
        K.Set(_soul, SetLoc.F("market.soul", "◆ 보유 소울 {0}", soul));

        foreach (var r in _rows)
        {
            var d = r.E.Item;
            string name = $"<color={GradeHex[Mathf.Clamp((int)d.Grade, 0, 3)]}>[{d.Grade}]</color> {d.GetName()}";
            if (r.E.Enchant != null && d.IsArmorPiece)
                name += $" <color=#{ColorUtility.ToHtmlStringRGB(r.E.Enchant.Color)}>◆</color>";
            K.Set(r.V.Label, name);
            K.Set(r.V.Sub, KindText(d));
            K.Set(r.V.ActionLabel, $"◆ {_market.PriceOf(r.E)}");
            K.Interact(r.V.Action, _market.CanBuy(r.E, out _));
            r.V.Face.color = r.E == _selected ? K.CheckOn : K.Raised;
        }

        if (_msgUntil > 0f && Time.time > _msgUntil) { _msgUntil = 0f; K.Set(_msg, ""); }
    }

    static string KindText(EquipmentData d)
    {
        if (d.IsArmorPiece) return LocUtil.Get($"ui.item.slot.{d.Slot}");
        return d.Class == WeaponClass.Melee ? LocUtil.Get("ui.item.class.Melee") : LocUtil.Get($"ui.item.ranged_kind.{d.RangedKind}");
    }

    void RefreshDetail()
    {
        if (_detail == null) return;
        if (_selected == null || _selected.Item == null)
        {
            K.Set(_detail, $"<color=#A9B0BA>{SetLoc.T("market.detail.empty", "물건을 고르면 자세히 보여 줌")}</color>");
            return;
        }
        K.Set(_detail, Describe(_selected.Item, _selected.Item.IsArmorPiece ? _selected.Enchant : null));
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
