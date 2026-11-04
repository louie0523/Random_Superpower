using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기본 모양 만들기 — 프리팹을 안 넣었을 때 런타임에, 그리고 에디터의 '프리팹 만들기' 메뉴가 같은 걸로 프리팹을 만듦.
/// 모양을 바꾸려면 만들어진 프리팹을 직접 고치면 됨 (이 코드는 안 건드려도 됨).
/// </summary>
public static class ClassPickUiBuilder
{
    public static readonly Vector2 CardSize = new(300f, 430f);
    public const float RerollArea = 70f;
    public static Color CardBack => GameUiKit.Panel;

    // ══════════════════ 카드 (v2 스티커 카툰) ══════════════════

    /// <summary>글꼴이 비었으면 UiPrefabSet 글꼴 (한글)</summary>
    static TMP_FontAsset FontOr(TMP_FontAsset font)
    {
        if (font != null) return font;
        var set = UiPrefabSet.Get();
        return set != null ? set.Font : null;
    }

    /// <summary>
    /// ClassCard (루트, 300×500 — ClassCardView)
    ///  ├ Body (300×430, Button)
    ///  │  ├ Glow (능력 색 빛) · Shadow · Back (판, 누르기 판정)
    ///  │  ├ Stripe (능력 색 기울어진 띠 — AccentTargets)
    ///  │  ├ IconRim ─ IconBack(육각, 능력 색 어둡게) ─ Icon
    ///  │  ├ NamePlate(능력 색) ─ Name
    ///  │  ├ Number · Summary · Line · Details
    ///  └ Reroll (Button) ─ Text
    /// </summary>
    public static ClassCardView BuildCard(Transform parent, TMP_FontAsset font)
    {
        font = FontOr(font);
        var display = GameUiKit.Display(font);
        float w = CardSize.x, h = CardSize.y;

        var root = GameUiKit.Rect("ClassCard", parent);
        root.sizeDelta = new Vector2(w, h + RerollArea);
        var view = root.gameObject.AddComponent<ClassCardView>();

        // 몸통
        var body = GameUiKit.Rect("Body", root);
        body.anchorMin = body.anchorMax = new Vector2(0.5f, 1f);
        body.pivot = new Vector2(0.5f, 1f);
        body.anchoredPosition = Vector2.zero;
        body.sizeDelta = new Vector2(w, h);

        var glow = GameUiKit.Img("Glow", body, GameUiKit.Graffiti ? GameUiKit.Shadow : GameUiKit.Cut, new Color(1f, 1f, 1f, 0f));   // v7: 외곽선 없는 부드러운 빛
        GameUiKit.Fill(glow.rectTransform, -10f);

        var face = GameUiKit.Sticker(body, GameUiKit.Panel, 0f, 12f);
        face.name = "Back";
        face.raycastTarget = true;             // 누르기 판정

        var pick = body.gameObject.AddComponent<Button>();
        pick.targetGraphic = face;
        pick.transition = Selectable.Transition.None;

        var stripe = GameUiKit.Img("Stripe", body, GameUiKit.Slant, new Color(0.82f, 0.16f, 0.18f));
        Top(stripe.rectTransform, 0f, 16f, w - 40f, 10f);

        var rim = GameUiKit.Img("IconRim", body, GameUiKit.Hex, GameUiKit.Graffiti ? GameUiKit.InkLine : GameUiKit.Light);
        Top(rim.rectTransform, 0f, 40f, 150f, 134f);
        rim.rectTransform.localRotation = GameUiKit.Tilt(-6f);
        var iconBack = GameUiKit.Img("IconBack", rim.transform, GameUiKit.Hex, new Color(0.5f, 0.1f, 0.1f));
        GameUiKit.Fill(iconBack.rectTransform, 8f);
        var icon = GameUiKit.Img("Icon", iconBack.transform, null, Color.white);
        icon.preserveAspect = true;
        GameUiKit.Fill(icon.rectTransform, 16f);

        var plate = GameUiKit.Img("NamePlate", body, GameUiKit.Cut, new Color(0.82f, 0.16f, 0.18f));
        Top(plate.rectTransform, 0f, 188f, 160f, 50f);
        var name = GameUiKit.Text("Name", plate.transform, display, 30f, Color.white, TextAlignmentOptions.Center);
        GameUiKit.Fill(name.rectTransform, 4f);
        name.text = "클래스";

        var number = GameUiKit.Text("Number", body, font, 15f, GameUiKit.Gold, TextAlignmentOptions.Center, true);
        Top(number.rectTransform, 0f, 244f, w - 40f, 22f);
        number.text = "NO. 0";

        var summary = GameUiKit.Text("Summary", body, font, 19f, GameUiKit.Ink, TextAlignmentOptions.Top);
        summary.textWrappingMode = TextWrappingModes.Normal;
        summary.overflowMode = TextOverflowModes.Ellipsis;
        Top(summary.rectTransform, 0f, 272f, w - 40f, 62f);
        summary.text = "한 줄 요약";

        var line = GameUiKit.Img("Line", body, null, GameUiKit.LineColor);
        Top(line.rectTransform, 0f, 340f, w - 48f, 2f);

        var details = GameUiKit.Text("Details", body, font, 16f, GameUiKit.Dim, TextAlignmentOptions.TopLeft);
        details.textWrappingMode = TextWrappingModes.Normal;
        details.overflowMode = TextOverflowModes.Ellipsis;
        Top(details.rectTransform, 0f, 350f, w - 48f, h - 362f);
        details.text = "◆ 패시브\n• 스킬";

        // 새로고침
        var rr = GameUiKit.Rect("Reroll", root);
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0f);
        rr.pivot = new Vector2(0.5f, 0f);
        rr.anchoredPosition = new Vector2(0f, 8f);
        rr.sizeDelta = new Vector2(210f, 50f);
        var rrImg = rr.gameObject.AddComponent<Image>();
        rrImg.sprite = GameUiKit.Graffiti ? GameUiKit.Para : GameUiKit.Cut;
        rrImg.type = Image.Type.Sliced;
        rrImg.color = GameUiKit.Raised;
        var rrBtn = rr.gameObject.AddComponent<Button>();
        rrBtn.targetGraphic = rrImg;
        GameUiKit.Tint(rrBtn);
        var cb = rrBtn.colors;
        cb.disabledColor = Color.white;     // 못 쓸 때 색은 RerollOff로
        rrBtn.colors = cb;
        var rrText = GameUiKit.Text("Text", rr, font, 20f, GameUiKit.Ink, TextAlignmentOptions.Center, true);
        GameUiKit.Fill(rrText.rectTransform);
        rrText.text = "↻ 새로고침 (1)";

        // 연결
        view.PickButton = pick;
        view.RerollButton = rrBtn;
        view.RerollText = rrText;
        view.RerollBack = rrImg;
        view.RerollOn = GameUiKit.Raised;
        view.RerollOff = GameUiKit.Field;
        view.Icon = icon;
        view.IconBack = iconBack;
        view.NamePlate = plate;
        view.Name = name;
        view.Number = number;
        view.Summary = summary;
        view.Details = details;
        view.AccentTargets = new Graphic[] { stripe };
        view.Glow = glow;
        view.Visual = body;
        view.PlatePadding = 44f;
        return view;
    }

    // ══════════════════ 선택 화면 ══════════════════

    public struct ScreenRefs
    {
        public Canvas Canvas;
        public GameObject Root;
        public TMP_Text Title, Timer;
        public RectTransform Cards;
    }

    /// <summary>
    /// ClassPickPanel (Canvas — Overlay · 정렬 30500, CanvasScaler 1920×1080, GraphicRaycaster)
    ///  └ Root (전체 화면, CanvasGroup)
    ///     ├ Dim (뒤 어둡게, 뒤 클릭 막음)
    ///     ├ Title (◆ + 큰 제목) · Timer (노랑 칩)
    ///     └ Cards (Horizontal Layout Group — 카드가 여기 들어감)
    /// </summary>
    public static ScreenRefs BuildScreen(GameObject host, TMP_FontAsset font, int sortingOrder, Color dim)
    {
        font = FontOr(font);
        var display = GameUiKit.Display(font);
        var s = new ScreenRefs();

        var root = GameUiKit.MakeScreen(host, sortingOrder, null);
        s.Canvas = host.GetComponent<Canvas>();
        root.gameObject.AddComponent<CanvasGroup>();
        s.Root = root.gameObject;

        var d = GameUiKit.Img("Dim", root, null, new Color(dim.r, dim.g, dim.b, Mathf.Max(dim.a, 0.7f)));
        d.raycastTarget = true;
        GameUiKit.Fill(d.rectTransform);

        var head = GameUiKit.Rect("Head", root);
        Center(head, 0f, 340f, 900f, 70f);
        if (GameUiKit.Graffiti)        // v7 — 제목 밑 노랑 스프레이
            GameUiKit.Deco(head, GameUiKit.Spray, GameUiKit.Gold, new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(520f, 64f), -2f);
        var dia = GameUiKit.Img("Dot", head, null, GameUiKit.Mint);
        dia.enabled = !GameUiKit.Graffiti;
        dia.rectTransform.anchorMin = dia.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        dia.rectTransform.sizeDelta = new Vector2(22f, 22f);
        dia.rectTransform.anchoredPosition = new Vector2(-190f, 2f);
        dia.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var title = GameUiKit.Text("Title", head, display, 56f, GameUiKit.Ink, TextAlignmentOptions.Center);
        GameUiKit.Fill(title.rectTransform);
        title.text = "클래스 선택";
        if (GameUiKit.Graffiti) { title.fontStyle = FontStyles.Italic; GameUiKit.InkOutline(title); }
        s.Title = title;

        var timerBox = GameUiKit.Rect("Timer", root);
        Center(timerBox, 0f, 286f, 360f, 38f);
        var tBg = GameUiKit.Img("Back", timerBox, GameUiKit.Cut, GameUiKit.Gold);
        GameUiKit.Fill(tBg.rectTransform);
        var timer = GameUiKit.Text("Text", timerBox, font, 20f, GameUiKit.TextOn(GameUiKit.Gold), TextAlignmentOptions.Center, true);
        GameUiKit.Fill(timer.rectTransform, 4f);
        timer.text = "20초 뒤 무작위 선택";
        var hide = timerBox.gameObject.AddComponent<UiHideWhenEmpty>();
        hide.Text = timer;
        hide.Targets = new[] { tBg.gameObject };
        s.Timer = timer;

        var cards = GameUiKit.Rect("Cards", root);
        Center(cards, 0f, -30f, 1500f, CardSize.y + RerollArea);
        var hlg = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 40f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        s.Cards = cards;
        return s;
    }

    // ══════════════════ 랜덤 능력 한 줄 ══════════════════

    public struct RowStyle
    {
        public float Width, Height, IconSize, NameSize, NumberSize, SummarySize;
        public Color Background;
        public static RowStyle Default => new()
        {
            Width = 540f, Height = 54f, IconSize = 44f, NameSize = 22f, NumberSize = 15f, SummarySize = 18f,
            Background = new Color(0.05f, 0.06f, 0.09f, 0.72f),
        };
    }

    /// <summary>
    /// RandomAbilityRow (540×54 — RandomAbilityRowView, 위-왼쪽 기준)
    ///  ├ Back (왼쪽 진하고 오른쪽으로 사라지는 띠)
    ///  ├ Icon
    ///  ├ NamePlate ─ Name
    ///  ├ Number (위)
    ///  └ Summary (아래)
    /// </summary>
    public static RandomAbilityRowView BuildRow(Transform parent, TMP_FontAsset font, RowStyle st)
    {
        var root = AbilityUi.Rect("RandomAbilityRow", parent);
        AbilityUi.Place(root, 0f, 0f, st.Width, st.Height);
        var view = root.gameObject.AddComponent<RandomAbilityRowView>();

        var bg = AbilityUi.Img("Back", root, AbilityUi.Gradient, st.Background);
        AbilityUi.Fill(bg.rectTransform);

        var icon = AbilityUi.Img("Icon", root, null, new Color(1f, 1f, 1f, 0.12f));
        icon.preserveAspect = true;
        AbilityUi.Place(icon.rectTransform, 6f, (st.Height - st.IconSize) * 0.5f, st.IconSize, st.IconSize);

        float plateH = st.Height - 14f;
        float px = 6f + st.IconSize + 8f;
        var plate = AbilityUi.Img("NamePlate", root, AbilityUi.Rounded, new Color(0.82f, 0.16f, 0.18f), true);
        plate.pixelsPerUnitMultiplier = 2.2f;
        AbilityUi.Place(plate.rectTransform, px, (st.Height - plateH) * 0.5f, 80f, plateH);
        var name = AbilityUi.Text("Name", plate.transform, font, st.NameSize, Color.white, TextAlignmentOptions.Center, true);
        AbilityUi.Fill(name.rectTransform, 4f);
        name.text = "능력";
        // 긴 이름 — 이름표가 PlateMax까지 늘어나고, 그래도 넘치면 글자가 작아짐 (… 로 잘리지 않게)
        name.enableAutoSizing = true;
        name.fontSizeMin = Mathf.Max(11f, st.NameSize * 0.6f);
        name.fontSizeMax = st.NameSize;
        name.overflowMode = TextOverflowModes.Overflow;

        float tx = px + 90f;
        var number = AbilityUi.Text("Number", root, font, st.NumberSize, new Color(1f, 0.84f, 0.22f), TextAlignmentOptions.BottomLeft, true);
        AbilityUi.Place(number.rectTransform, tx, 3f, st.Width - tx - 8f, st.Height * 0.42f);
        number.text = "NO. 0";

        var summary = AbilityUi.Text("Summary", root, font, st.SummarySize, Color.white, TextAlignmentOptions.TopLeft);
        AbilityUi.Place(summary.rectTransform, tx, st.Height * 0.44f, st.Width - tx - 8f, st.Height * 0.56f);
        summary.text = "효과 요약";
        // 긴 요약 — 줄바꿈해서 줄(배경)이 아래로 늘어남 (RandomAbilityRowView.FitHeight)
        summary.textWrappingMode = TextWrappingModes.Normal;
        summary.overflowMode = TextOverflowModes.Overflow;

        view.Icon = icon;
        view.NamePlate = plate;
        view.Name = name;
        view.Number = number;
        view.Summary = summary;
        view.AutoArrange = true;
        view.PlateMax = Mathf.Max(view.PlateMax, st.Width * 0.5f);
        view.MaxSummaryLines = 0;
        return view;
    }

    // ══════════════════ 배치 도우미 ══════════════════

    static void Top(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Center(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }
}
