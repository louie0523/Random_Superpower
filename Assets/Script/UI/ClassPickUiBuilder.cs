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
    public static readonly Color CardBack = new(0.09f, 0.1f, 0.13f, 0.97f);

    // ══════════════════ 카드 ══════════════════

    /// <summary>
    /// ClassCard (루트, 300×500 — ClassCardView)
    ///  ├ Body (300×430, Button · CanvasGroup 없음)
    ///  │  ├ Glow · Frame(능력 색) · Back
    ///  │  ├ IconBack ─ Icon
    ///  │  ├ NamePlate ─ Name
    ///  │  ├ Number · Summary · Details
    ///  └ Reroll (Button) ─ Text
    /// </summary>
    public static ClassCardView BuildCard(Transform parent, TMP_FontAsset font)
    {
        float w = CardSize.x, h = CardSize.y;

        var root = AbilityUi.Rect("ClassCard", parent);
        root.sizeDelta = new Vector2(w, h + RerollArea);
        var view = root.gameObject.AddComponent<ClassCardView>();

        // 몸통
        var body = AbilityUi.Rect("Body", root);
        body.anchorMin = body.anchorMax = new Vector2(0.5f, 1f);
        body.pivot = new Vector2(0.5f, 1f);
        body.anchoredPosition = Vector2.zero;
        body.sizeDelta = new Vector2(w, h);

        var glow = AbilityUi.Img("Glow", body, AbilityUi.Rounded, new Color(1f, 1f, 1f, 0f), true);
        AbilityUi.Fill(glow.rectTransform, -10f);

        var frame = AbilityUi.Img("Frame", body, AbilityUi.Rounded, new Color(0.82f, 0.16f, 0.18f), true);
        AbilityUi.Fill(frame.rectTransform);
        frame.raycastTarget = true;             // 누르기 판정

        var back = AbilityUi.Img("Back", body, AbilityUi.Rounded, CardBack, true);
        AbilityUi.Fill(back.rectTransform, 4f);

        var pick = body.gameObject.AddComponent<Button>();
        pick.targetGraphic = frame;
        pick.transition = Selectable.Transition.None;

        var iconBack = AbilityUi.Img("IconBack", body, AbilityUi.Circle, new Color(0.5f, 0.1f, 0.1f));
        Top(iconBack.rectTransform, 0f, 34f, 132f, 132f);
        var icon = AbilityUi.Img("Icon", iconBack.transform, null, Color.white);
        icon.preserveAspect = true;
        AbilityUi.Fill(icon.rectTransform, 14f);

        var plate = AbilityUi.Img("NamePlate", body, AbilityUi.Rounded, new Color(0.82f, 0.16f, 0.18f), true);
        plate.pixelsPerUnitMultiplier = 2.2f;
        Top(plate.rectTransform, 0f, 182f, 160f, 48f);
        var name = AbilityUi.Text("Name", plate.transform, font, 28f, Color.white, TextAlignmentOptions.Center, true);
        AbilityUi.Fill(name.rectTransform);
        name.text = "클래스";

        var number = AbilityUi.Text("Number", body, font, 15f, new Color(1f, 0.84f, 0.22f), TextAlignmentOptions.Center, true);
        Top(number.rectTransform, 0f, 236f, w - 40f, 22f);
        number.text = "NO. 0";

        var summary = AbilityUi.Text("Summary", body, font, 19f, Color.white, TextAlignmentOptions.Top);
        summary.textWrappingMode = TextWrappingModes.Normal;
        Top(summary.rectTransform, 0f, 266f, w - 40f, 70f);
        summary.text = "한 줄 요약";

        var details = AbilityUi.Text("Details", body, font, 16f, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.TopLeft);
        details.textWrappingMode = TextWrappingModes.Normal;
        Top(details.rectTransform, 0f, 340f, w - 48f, h - 352f);
        details.text = "◆ 패시브\n• 스킬";

        // 새로고침
        var rr = AbilityUi.Rect("Reroll", root);
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0f);
        rr.pivot = new Vector2(0.5f, 0f);
        rr.anchoredPosition = new Vector2(0f, 6f);
        rr.sizeDelta = new Vector2(200f, 50f);
        var rrImg = rr.gameObject.AddComponent<Image>();
        rrImg.sprite = AbilityUi.Rounded;
        rrImg.type = Image.Type.Sliced;
        rrImg.color = new Color(0.22f, 0.26f, 0.34f, 1f);
        var rrBtn = rr.gameObject.AddComponent<Button>();
        rrBtn.targetGraphic = rrImg;
        var cb = rrBtn.colors;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.disabledColor = Color.white;
        rrBtn.colors = cb;
        var rrText = AbilityUi.Text("Text", rr, font, 20f, Color.white, TextAlignmentOptions.Center, true);
        AbilityUi.Fill(rrText.rectTransform);
        rrText.text = "↻ 새로고침 (1)";

        // 연결
        view.PickButton = pick;
        view.RerollButton = rrBtn;
        view.RerollText = rrText;
        view.RerollBack = rrImg;
        view.Icon = icon;
        view.IconBack = iconBack;
        view.NamePlate = plate;
        view.Name = name;
        view.Number = number;
        view.Summary = summary;
        view.Details = details;
        view.AccentTargets = new Graphic[] { frame };
        view.Glow = glow;
        view.Visual = body;
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
    ///     ├ Dim (검은 반투명, 뒤 클릭 막음)
    ///     ├ Title · Timer
    ///     └ Cards (Horizontal Layout Group — 카드가 여기 들어감)
    /// </summary>
    public static ScreenRefs BuildScreen(GameObject host, TMP_FontAsset font, int sortingOrder, Color dim)
    {
        var s = new ScreenRefs();

        var rt = host.GetComponent<RectTransform>();
        if (rt == null) rt = host.AddComponent<RectTransform>();
        s.Canvas = host.GetComponent<Canvas>();
        if (s.Canvas == null) s.Canvas = host.AddComponent<Canvas>();
        s.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        s.Canvas.sortingOrder = sortingOrder;
        var sc = host.GetComponent<CanvasScaler>();
        if (sc == null) sc = host.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 1f;
        if (host.GetComponent<GraphicRaycaster>() == null) host.AddComponent<GraphicRaycaster>();

        var root = AbilityUi.Rect("Root", host.transform);
        AbilityUi.Fill(root);
        root.gameObject.AddComponent<CanvasGroup>();
        s.Root = root.gameObject;

        var d = AbilityUi.Img("Dim", root, null, dim);
        d.raycastTarget = true;
        AbilityUi.Fill(d.rectTransform);

        var title = AbilityUi.Text("Title", root, font, 42f, Color.white, TextAlignmentOptions.Center, true);
        Center(title.rectTransform, 0f, 330f, 900f, 60f);
        title.text = "클래스 선택";
        s.Title = title;

        var timer = AbilityUi.Text("Timer", root, font, 22f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Center);
        Center(timer.rectTransform, 0f, 285f, 600f, 30f);
        timer.text = "20초 뒤 무작위 선택";
        s.Timer = timer;

        var cards = AbilityUi.Rect("Cards", root);
        Center(cards, 0f, -20f, 1500f, CardSize.y + RerollArea);
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
        AbilityUi.Fill(name.rectTransform);
        name.text = "능력";

        float tx = px + 90f;
        var number = AbilityUi.Text("Number", root, font, st.NumberSize, new Color(1f, 0.84f, 0.22f), TextAlignmentOptions.BottomLeft, true);
        AbilityUi.Place(number.rectTransform, tx, 3f, st.Width - tx - 8f, st.Height * 0.42f);
        number.text = "NO. 0";

        var summary = AbilityUi.Text("Summary", root, font, st.SummarySize, Color.white, TextAlignmentOptions.TopLeft);
        AbilityUi.Place(summary.rectTransform, tx, st.Height * 0.44f, st.Width - tx - 8f, st.Height * 0.56f);
        summary.text = "효과 요약";

        view.Icon = icon;
        view.NamePlate = plate;
        view.Name = name;
        view.Number = number;
        view.Summary = summary;
        view.AutoArrange = true;
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
