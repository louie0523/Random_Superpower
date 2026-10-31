using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 카툰 · 깔끔 UI 도구 — 흰 바탕 + 굵은 검은 테두리 + 아래로 딱 떨어지는 그림자.
/// 프리팹 만들기(GameUiBuilders)와 뷰들이 같이 씀. 색을 바꾸려면 만들어진 프리팹을 직접 고치면 됨.
/// </summary>
public static class GameUiKit
{
    // ══════════════════ 색 ══════════════════

    public static readonly Color Ink = new(0.11f, 0.11f, 0.14f, 1f);       // 테두리 · 글자
    public static readonly Color Paper = new(1f, 0.99f, 0.96f, 1f);        // 창 바탕
    public static readonly Color Soft = new(0.92f, 0.92f, 0.94f, 1f);      // 칸 바탕
    public static readonly Color Muted = new(0.46f, 0.46f, 0.52f, 1f);     // 흐린 글자
    public static readonly Color Yellow = new(1f, 0.81f, 0.24f, 1f);       // 강조 · 주요 버튼
    public static readonly Color Red = new(0.95f, 0.36f, 0.36f, 1f);       // 위험 · 밴
    public static readonly Color Blue = new(0.36f, 0.67f, 1f, 1f);
    public static readonly Color Green = new(0.4f, 0.84f, 0.6f, 1f);       // 준비
    public static readonly Color Sky = new(0.55f, 0.78f, 0.96f, 1f);       // 로비 배경
    public static readonly Color DimColor = new(0.04f, 0.04f, 0.07f, 0.62f);
    public static readonly Color CheckOn = new(1f, 0.95f, 0.78f, 1f);      // 체크 켜짐 바탕

    public const float Line = 4f;      // 테두리 두께
    public const float Drop = 6f;      // 그림자 거리

    public static Sprite Round => AbilityUi.Rounded;
    public static Sprite Circle => AbilityUi.Circle;

    // ══════════════════ 기본 ══════════════════

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;   // UI
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Fill(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    public static void Center(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>레이아웃 크기 (-1 = 안 건드림)</summary>
    public static LayoutElement Size(Component c, float w = -1f, float h = -1f, float flexW = -1f, float flexH = -1f)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
        if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
        if (flexW >= 0f) le.flexibleWidth = flexW;
        if (flexH >= 0f) le.flexibleHeight = flexH;
        return le;
    }

    public static Image Img(string name, Transform parent, Sprite sprite, Color color)
    {
        var rt = Rect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null && sprite == Round) img.type = Image.Type.Sliced;
        return img;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color,
                                       TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
    {
        var rt = Rect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        if (bold) t.fontStyle = FontStyles.Bold;
        return t;
    }

    /// <summary>스티커 모양: root 안에 그림자 · 테두리 · 면을 깔고 면(Face)을 돌려줌 — 내용은 Face 안에</summary>
    public static Image Sticker(RectTransform root, Color face, float line = Line, float drop = Drop)
    {
        if (drop > 0f)
        {
            var sh = Img("Shadow", root, Round, Ink);
            Fill(sh.rectTransform);
            sh.rectTransform.offsetMin += new Vector2(0f, -drop);
            sh.rectTransform.offsetMax += new Vector2(0f, -drop);
        }
        var ol = Img("Outline", root, Round, Ink);
        Fill(ol.rectTransform);
        var f = Img("Face", root, Round, face);
        Fill(f.rectTransform, line);
        return f;
    }

    // ══════════════════ 화면 · 창 ══════════════════

    /// <summary>오버레이 캔버스 + 전체 화면 Root (+ 어둡게)</summary>
    public static RectTransform MakeScreen(GameObject host, int sortingOrder, Color? backdrop)
    {
        if (host.GetComponent<RectTransform>() == null) host.AddComponent<RectTransform>();
        var c = host.GetComponent<Canvas>();
        if (c == null) c = host.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortingOrder;
        var sc = host.GetComponent<CanvasScaler>();
        if (sc == null) sc = host.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 1f;
        if (host.GetComponent<GraphicRaycaster>() == null) host.AddComponent<GraphicRaycaster>();

        var root = Rect("Root", host.transform);
        Fill(root);
        if (backdrop.HasValue)
        {
            var d = Img("Backdrop", root, null, backdrop.Value);
            d.raycastTarget = true;     // 뒤 클릭 막음
            Fill(d.rectTransform);
        }
        return root;
    }

    /// <summary>창 — 돌려주는 건 루트(위치 · 크기), content = 내용 부모 (세로 쌓기)</summary>
    public static RectTransform Window(string name, Transform parent, float w, float h, out RectTransform content,
                                       int pad = 28, float spacing = 14f)
    {
        var rt = Rect(name, parent);
        Center(rt, 0f, 0f, w, h);
        var face = Sticker(rt, Paper, Line + 1f, Drop + 4f);
        face.raycastTarget = true;
        var v = face.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(pad, pad, pad - 6, pad);
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        content = (RectTransform)face.transform;
        return rt;
    }

    /// <summary>제목 + 노란 밑줄</summary>
    public static TextMeshProUGUI Header(Transform parent, TMP_FontAsset font, string title, float size = 40f)
    {
        var g = Group("Header", parent, 4f);
        g.childForceExpandWidth = false;
        var t = Text("Title", g.transform, font, size, Ink, TextAlignmentOptions.MidlineLeft, true);
        Size(t, -1f, size * 1.25f, 1f);
        t.text = title;
        var bar = Img("Accent", g.transform, Round, Yellow);
        Size(bar, 96f, 9f, 0f);
        return t;
    }

    public static TextMeshProUGUI Section(Transform parent, TMP_FontAsset font, string title)
    {
        var t = Text("Section", parent, font, 24f, Ink, TextAlignmentOptions.BottomLeft, true);
        Size(t, -1f, 40f);
        t.text = title;
        return t;
    }

    public static TextMeshProUGUI Note(Transform parent, TMP_FontAsset font, string text, float size = 19f)
    {
        var t = Text("Note", parent, font, size, Muted, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.text = text;
        return t;
    }

    /// <summary>세로 묶음</summary>
    public static VerticalLayoutGroup Group(string name, Transform parent, float spacing = 8f)
    {
        var rt = Rect(name, parent);
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    /// <summary>가로 줄 (높이 고정)</summary>
    public static HorizontalLayoutGroup Row(string name, Transform parent, float height, float spacing = 10f)
    {
        var rt = Rect(name, parent);
        Size(rt, -1f, height, -1f, 0f);     // 세로로 안 늘어나게 (남는 공간은 스크롤이 가짐)
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.padding = new RectOffset(0, 0, 0, 6);   // 그림자 자리
        return h;
    }

    // ══════════════════ 조작 ══════════════════

    public static Button Btn(string name, Transform parent, TMP_FontAsset font, string label, Color face, float fontSize,
                             out TextMeshProUGUI text, Color? textColor = null)
    {
        var rt = Rect(name, parent);
        var f = Sticker(rt, face, 3f, 4f);
        f.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = f;
        Tint(b);
        text = Text("Label", f.transform, font, fontSize, textColor ?? Ink, TextAlignmentOptions.Center, true);
        Fill(text.rectTransform, 4f);
        text.text = label;
        return b;
    }

    public static void Tint(Selectable s)
    {
        var cb = s.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.6f);
        cb.fadeDuration = 0.06f;
        s.colors = cb;
        s.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    /// <summary>켜고 끄는 버튼. check = 왼쪽 체크칸 (켜면 점), 아니면 면 색이 바뀜</summary>
    public static UiOption Option(string name, Transform parent, TMP_FontAsset font, string label, bool check,
                                  float h = 48f, float fontSize = 22f)
    {
        var b = Btn(name, parent, font, label, Color.white, fontSize, out var t);
        Size(b, -1f, h);
        var opt = b.gameObject.AddComponent<UiOption>();
        opt.Button = b;
        opt.Face = (Image)b.targetGraphic;
        opt.Label = t;

        if (check)
        {
            var box = Img("CheckBox", opt.Face.transform, Round, Ink);
            var r = box.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(12f, 0f);
            r.sizeDelta = new Vector2(26f, 26f);
            var inner = Img("Inner", box.transform, Round, Color.white);
            Fill(inner.rectTransform, 3f);
            var dot = Img("Check", inner.transform, Round, Ink);
            Fill(dot.rectTransform, 4f);
            opt.Check = dot;
            opt.OnFace = CheckOn;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.rectTransform.offsetMin = new Vector2(50f, 4f);
        }
        else opt.OnFace = Yellow;

        opt.OffFace = Color.white;
        opt.SetOn(false);
        return opt;
    }

    public static TMP_InputField Input(string name, Transform parent, TMP_FontAsset font, float fontSize, string placeholder, int limit)
    {
        var rt = Rect(name, parent);
        var face = Sticker(rt, Color.white, 3f, 0f);
        face.raycastTarget = true;

        var area = Rect("Text Area", face.transform);
        Fill(area, 10f);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = Text("Placeholder", area, font, fontSize, Muted);
        Fill(ph.rectTransform);
        ph.fontStyle = FontStyles.Italic;
        ph.text = placeholder;

        var tx = Text("Text", area, font, fontSize, Ink);
        Fill(tx.rectTransform);
        tx.overflowMode = TextOverflowModes.Overflow;

        var inp = rt.gameObject.AddComponent<TMP_InputField>();
        inp.textViewport = area;
        inp.textComponent = tx;
        inp.placeholder = ph;
        inp.targetGraphic = face;
        inp.characterLimit = limit;
        inp.pointSize = fontSize;
        if (font != null) inp.fontAsset = font;
        inp.customCaretColor = true;
        inp.caretColor = Ink;
        inp.caretWidth = 2;
        inp.selectionColor = new Color(1f, 0.81f, 0.24f, 0.45f);
        Tint(inp);
        return inp;
    }

    public static Slider SliderBar(string name, Transform parent, float height = 34f)
    {
        var rt = Rect(name, parent);
        Size(rt, -1f, height);

        var bg = Img("Back", rt, Round, Ink);
        Band(bg.rectTransform, 16f);
        bg.raycastTarget = true;
        var bgIn = Img("Inner", bg.transform, Round, Soft);
        Fill(bgIn.rectTransform, 3f);

        var fillArea = Rect("Fill Area", rt);
        Band(fillArea, 10f);
        fillArea.offsetMin += new Vector2(3f, 0f);
        fillArea.offsetMax -= new Vector2(3f, 0f);
        var fill = Img("Fill", fillArea, Round, Yellow);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = Vector2.zero;

        var handleArea = Rect("Handle Slide Area", rt);
        Fill(handleArea);
        handleArea.offsetMin = new Vector2(height * 0.5f, 0f);
        handleArea.offsetMax = new Vector2(-height * 0.5f, 0f);
        var handle = Img("Handle", handleArea, Circle, Ink);
        handle.rectTransform.anchorMin = Vector2.zero;
        handle.rectTransform.anchorMax = new Vector2(0f, 1f);
        handle.rectTransform.sizeDelta = new Vector2(height, 0f);
        var hf = Img("Face", handle.transform, Circle, Color.white);
        Fill(hf.rectTransform, 4f);

        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = hf;
        s.direction = Slider.Direction.LeftToRight;
        Tint(s);
        return s;
    }

    static void Band(RectTransform r, float h)
    {
        r.anchorMin = new Vector2(0f, 0.5f);
        r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(0f, -h * 0.5f);
        r.offsetMax = new Vector2(0f, h * 0.5f);
    }

    /// <summary>세로 스크롤 목록 — 돌려주는 건 Content (줄이 여기 쌓임)</summary>
    public static RectTransform Scroll(string name, Transform parent, out ScrollRect sr, float spacing = 10f)
    {
        var rt = Rect(name, parent);
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);          // 휠 · 끌기 받기
        sr = rt.gameObject.AddComponent<ScrollRect>();

        var vp = Rect("Viewport", rt);
        Fill(vp);
        vp.gameObject.AddComponent<RectMask2D>();

        var content = Rect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = new RectOffset(4, 10, 4, 14);       // 오른쪽 · 아래 = 그림자 자리
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vp;
        sr.content = content;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        return content;
    }

    // ══════════════════ 줄 ══════════════════

    /// <summary>목록 한 줄 — wholeButton = 줄 전체가 버튼, actionLabel = 오른쪽 버튼 (null이면 없음)</summary>
    public static UiListRow ListRow(string name, Transform parent, TMP_FontAsset font, bool wholeButton, string actionLabel,
                                    float h = 60f, bool withSub = true)
    {
        var rt = Rect(name, parent);
        Size(rt, -1f, h);
        var face = Sticker(rt, Color.white, 3f, 4f);
        var row = rt.gameObject.AddComponent<UiListRow>();
        row.Face = face;

        if (wholeButton)
        {
            face.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = face;
            Tint(b);
            row.Button = b;
        }

        var hl = face.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(18, 8, 6, 6);
        hl.spacing = 10f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;

        var label = Text("Label", face.transform, font, 24f, Ink, TextAlignmentOptions.MidlineLeft, true);
        Size(label, 100f, -1f, 1f);
        label.text = "이름";
        row.Label = label;
        if (withSub)
        {
            var sub = Text("Sub", face.transform, font, 20f, Muted, TextAlignmentOptions.MidlineRight);
            Size(sub, 150f);
            sub.text = "";
            row.Sub = sub;
        }

        if (actionLabel != null)
        {
            var a = Btn("Action", face.transform, font, actionLabel, Yellow, 20f, out var at);
            Size(a, 150f);
            row.Action = a;
            row.ActionLabel = at;
        }
        return row;
    }

    /// <summary>값 줄: 이름 · [--] · [-] · 값 · [+] · [++]</summary>
    public static StepperRowView Stepper(string name, Transform parent, TMP_FontAsset font, string label, bool big,
                                         string minus = "-", string plus = "+")
    {
        var row = Row(name, parent, 54f, 8f);
        var view = row.gameObject.AddComponent<StepperRowView>();

        var lb = Text("Label", row.transform, font, 22f, Ink, TextAlignmentOptions.MidlineLeft, true);
        Size(lb, 220f, -1f, 1f);
        lb.text = label;
        view.Label = lb;

        if (big) view.BigMinus = Small("BigMinus", row.transform, font, "--");
        view.Minus = Small("Minus", row.transform, font, minus);

        var box = Rect("Value", row.transform);
        Size(box, 190f);
        var face = Sticker(box, Soft, 3f, 0f);
        var val = Text("Text", face.transform, font, 22f, Ink, TextAlignmentOptions.Center, true);
        Fill(val.rectTransform, 4f);
        val.text = "0";
        view.Value = val;

        view.Plus = Small("Plus", row.transform, font, plus);
        if (big) view.BigPlus = Small("BigPlus", row.transform, font, "++");
        return view;
    }

    static Button Small(string name, Transform parent, TMP_FontAsset font, string label)
    {
        var b = Btn(name, parent, font, label, Color.white, 22f, out _);
        Size(b, 54f);
        return b;
    }

    // ══════════════════ 실행 중 도우미 ══════════════════

    /// <summary>uGUI 클릭에 EventSystem이 필요 — 없으면 만듦</summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    /// <summary>누른 버튼이 '선택됨'으로 남으면 Enter · Space가 또 누름 → 버튼 선택 풀기 (입력 칸은 그대로)</summary>
    public static void DropButtonFocus()
    {
        var es = EventSystem.current;
        if (es == null) return;
        var sel = es.currentSelectedGameObject;
        if (sel == null || sel.GetComponent<TMP_InputField>() != null) return;
        es.SetSelectedGameObject(null);
    }

    /// <summary>글이 바뀌었을 때만 넣기</summary>
    public static void Set(TMP_Text t, string s)
    {
        if (t != null && t.text != s) t.text = s;
    }

    public static void Active(Component c, bool on)
    {
        if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
    }

    public static void Active(GameObject g, bool on)
    {
        if (g != null && g.activeSelf != on) g.SetActive(on);
    }

    public static void Interact(Selectable s, bool on)
    {
        if (s != null && s.interactable != on) s.interactable = on;
    }
}
