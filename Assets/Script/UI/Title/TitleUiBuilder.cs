using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 타이틀 화면 (v7 그래피티) — 가운데는 비워 둠 (3D 씬의 내 캐릭터가 보이게).
///   왼쪽 위: 로고 + 스프레이 밑줄 · 오른쪽 위: SC · 티켓 · 닉네임 · 오른쪽: 엇갈린 메뉴 버튼
///   창 5개: 무기 보기 · 스킨/뽑기 · 도감 · 프로필 (가운데 큰 창) / 꾸미기 (왼쪽 창 — 캐릭터가 보이게)
/// 빗금 대신 망점(Dots) · 그림자 없음 · 외곽선은 그림에
/// </summary>
public static class TitleUiBuilder
{
    public static TitleScreenView Build(GameObject host, TMP_FontAsset font)
    {
        var display = K.Display(font);
        var root = K.MakeScreen(host, 10, null);
        var v = host.AddComponent<TitleScreenView>();

        // ── 로고 (왼쪽 위) ──
        var logo = K.Rect("Logo", root);
        Corner(logo, new Vector2(0f, 1f), new Vector2(70f, -46f), new Vector2(900f, 270f));
        K.Deco(logo, K.Splat, K.Gold, new Vector2(0f, 1f), new Vector2(90f, -110f), new Vector2(250f, 250f), -12f, 0.95f);
        var spray = K.Img("Spray", logo, K.SprayLong, K.Pink);      // 긴 그림 그대로 (늘려서 번지지 않게 4:1)
        Corner(spray.rectTransform, new Vector2(0f, 1f), new Vector2(-10f, -80f), new Vector2(840f, 210f));
        spray.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -3f);
        var title = K.Text("Title", logo, display, 128f, K.Ink, TextAlignmentOptions.MidlineLeft);
        title.fontStyle = FontStyles.Italic;
        title.overflowMode = TextOverflowModes.Overflow;
        Corner(title.rectTransform, new Vector2(0f, 1f), new Vector2(10f, 0f), new Vector2(900f, 180f));
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 4f);
        title.text = "정루이 멀티";
        K.InkOutline(title, 0.3f);
        v.Logo = title;
        var sub = K.Tag(logo, font, "배틀로얄", K.Mint, 28f, out _, "SubTag");
        Corner(sub, new Vector2(0f, 1f), new Vector2(26f, -196f), Vector2.zero);
        Fit(sub);
        sub.localRotation = K.Tilt(-4f);

        // ── 지갑 · 닉네임 (오른쪽 위) ──
        var wallet = K.Rect("Wallet", root);
        Corner(wallet, new Vector2(1f, 1f), new Vector2(-60f, -48f), new Vector2(900f, 66f));
        var wh = wallet.gameObject.AddComponent<HorizontalLayoutGroup>();
        wh.spacing = 14f;
        wh.childAlignment = TextAnchor.MiddleRight;
        wh.childControlWidth = wh.childControlHeight = true;
        wh.childForceExpandWidth = wh.childForceExpandHeight = false;
        K.Tag(wallet, font, "0 SC", K.Gold, 26f, out var sc, "SC");
        v.SC = sc;
        var tk = K.Tag(wallet, font, "티켓 0", K.Pink, 26f, out var tickets, "Tickets");
        tk.localRotation = K.Tilt(2f);
        v.Tickets = tickets;
        var nick = K.Input("Nick", wallet, font, 24f, "닉네임", 16);
        K.Size(nick, 280f, 58f);
        v.Nick = nick;

        // ── 메뉴 (오른쪽) ──
        var menu = K.Rect("Menu", root);
        Corner(menu, new Vector2(1f, 0.5f), new Vector2(-70f, -30f), new Vector2(540f, 800f));
        v.Menu = menu.gameObject;
        K.Deco(menu, K.SprayDrip, K.Mint, new Vector2(0.5f, 0.5f), new Vector2(-30f, -40f), new Vector2(720f, 360f), 9f, 0.45f);

        v.Play = MenuButton(menu, font, "Play", "게임 시작", K.Yellow, 0f, 0f, 500f, 128f, 60f, true);
        var tag = K.Tag(v.Play.transform.GetChild(0), font, "GO!", K.Danger, 22f, out _, "Go");
        Corner(tag, new Vector2(0f, 1f), new Vector2(-18f, 18f), Vector2.zero);
        Fit(tag);
        tag.localRotation = K.Tilt(-10f);

        (string name, string label, Color face)[] items =
        {
            ("Weapons", "무기 보기", K.Raised),
            ("Skins", "스킨 · 뽑기", K.Pink),
            ("Codex", "도감", K.Raised),
            ("Profile", "프로필", K.Raised),
            ("Customize", "꾸미기", K.Mint),
            ("Settings", "설정", K.Raised),
            ("Quit", "종료", K.Panel2),
        };
        var buttons = new Button[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            float x = (i % 2 == 0) ? 0f : -30f;         // 엇갈리게 (손으로 붙인 느낌)
            buttons[i] = MenuButton(menu, font, items[i].name, items[i].label, items[i].face, x, -150f - i * 82f, 390f, 68f, 30f, false);
        }
        v.Weapons = buttons[0];
        v.Skins = buttons[1];
        v.Codex = buttons[2];
        v.Profile = buttons[3];
        v.Customize = buttons[4];
        v.Settings = buttons[5];
        v.Quit = buttons[6];

        // ── 지난 판 보상 ──
        var toast = K.Tag(root, font, "지난 판 1등 · +100 SC", K.Gold, 28f, out var toastText, "Toast");
        Corner(toast, new Vector2(0.5f, 0f), new Vector2(0f, 100f), Vector2.zero);
        toast.pivot = new Vector2(0.5f, 0f);
        Fit(toast);
        toast.localRotation = K.Tilt(-2f);
        v.Toast = toast.gameObject;
        v.ToastText = toastText;

        // ── 창 ──
        v.WeaponPanel = Add<WeaponGalleryView>(Frame(root, font, "WeaponPanel", "무기 보기", false, true));
        v.SkinPanel = Add<SkinShopView>(Frame(root, font, "SkinPanel", "스킨 · 뽑기", false, true));
        v.CodexPanel = Add<CodexView>(Frame(root, font, "CodexPanel", "도감", false, false));
        v.ProfilePanel = Add<ProfileView>(Frame(root, font, "ProfilePanel", "프로필", false, false));
        v.CustomizePanel = Add<CustomizeView>(Frame(root, font, "CustomizePanel", "꾸미기", true, false));

        K.Finish(root);
        return v;
    }

    static T Add<T>(TitlePanelFrame f) where T : TitlePanel
    {
        var p = f.gameObject.AddComponent<T>();
        p.Frame = f;
        return p;
    }

    // ══════════════════ 메뉴 버튼 ══════════════════

    static Button MenuButton(RectTransform menu, TMP_FontAsset font, string name, string label, Color face,
                             float x, float y, float w, float h, float size, bool big)
    {
        var b = K.Btn(name, menu, font, label, face, size, out var t);
        var rt = (RectTransform)b.transform;
        Corner(rt, new Vector2(1f, 1f), new Vector2(x, y), new Vector2(w, h));
        t.font = K.Display(font);
        t.fontStyle = FontStyles.Italic;
        if (big) rt.localRotation = K.Tilt(-2f);
        return b;
    }

    // ══════════════════ 창 ══════════════════

    /// <summary>
    /// 창 틀 — side = 왼쪽 좁은 창 (꾸미기: 가운데 캐릭터가 보이게 · 바탕 안 어둡게), 아니면 가운데 큰 창 + 오른쪽 자세히.
    /// model = 자세히 칸에 3D 모델 (끌어 돌리기)
    /// </summary>
    static TitlePanelFrame Frame(RectTransform root, TMP_FontAsset font, string name, string title, bool side, bool model)
    {
        var rt = K.Rect(name, root);
        K.Fill(rt);
        var f = rt.gameObject.AddComponent<TitlePanelFrame>();

        if (!side)
        {
            var dim = K.Img("Dim", rt, null, K.DimColor);
            K.Fill(dim.rectTransform);
            dim.raycastTarget = true;
        }

        var win = K.Window("Window", rt, side ? 780f : 1680f, side ? 960f : 970f, out var content, 28, 14f);
        if (side)
        {
            win.anchorMin = win.anchorMax = new Vector2(0f, 0.5f);
            win.pivot = new Vector2(0f, 0.5f);
            win.anchoredPosition = new Vector2(56f, -12f);
        }
        K.StripesOn(content.GetComponent<Image>(), 0.02f);

        // 위: 제목 + 뒤로
        var top = K.Row("Top", content, 76f, 16f);
        f.Title = K.Header(top.transform, font, title, 44f);
        var header = f.Title.transform;
        while (header.parent != null && header.name != "Header") header = header.parent;
        K.Size(header, -1f, -1f, 1f);
        f.Back = K.Btn("Back", top.transform, font, "뒤로  Esc", K.Raised, 24f, out _);
        K.Size(f.Back, 190f, 60f);

        // 탭
        var tabs = K.Row("Tabs", content, 58f, 10f);
        f.TabRow = (RectTransform)tabs.transform;
        f.TabTemplate = K.Option("Tab", tabs.transform, font, "탭", false, 54f, 24f);
        K.Size(f.TabTemplate, side ? 150f : 190f, 54f);

        // 몸통
        var body = K.Rect("Body", content);
        K.Size(body, -1f, -1f, -1f, 1f);
        if (side)
        {
            var bv = body.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.spacing = 16f;
            bv.childControlWidth = bv.childControlHeight = true;
            bv.childForceExpandWidth = true;
            bv.childForceExpandHeight = false;
        }
        else
        {
            var bh = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.spacing = 24f;
            bh.childControlWidth = bh.childControlHeight = true;
            bh.childForceExpandWidth = false;
            bh.childForceExpandHeight = true;
        }

        var list = GridList(body, font, side ? new Vector2(156f, 196f) : new Vector2(196f, 246f), f);
        K.Size(list, -1f, -1f, 1f, 1f);
        CardTemplate(f, font, side);

        if (side) SideDetail(body, font, f);
        else WideDetail(body, font, f, model);
        return f;
    }

    static RectTransform GridList(Transform parent, TMP_FontAsset font, Vector2 cell, TitlePanelFrame f)
    {
        var rt = K.Rect("List", parent);
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var sr = rt.gameObject.AddComponent<ScrollRect>();

        var vp = K.Rect("Viewport", rt);
        K.Fill(vp);
        vp.gameObject.AddComponent<RectMask2D>();

        var content = K.Rect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var g = content.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = cell;
        g.spacing = new Vector2(16f, 18f);
        g.padding = new RectOffset(10, 10, 12, 18);
        g.startCorner = GridLayoutGroup.Corner.UpperLeft;
        g.startAxis = GridLayoutGroup.Axis.Horizontal;
        g.childAlignment = TextAnchor.UpperLeft;
        g.constraint = GridLayoutGroup.Constraint.Flexible;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vp;
        sr.content = content;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;

        var empty = K.Text("Empty", rt, font, 24f, K.Dim, TextAlignmentOptions.Center);
        K.Fill(empty.rectTransform, 40f);
        empty.textWrappingMode = TextWrappingModes.Normal;
        empty.text = "";
        empty.gameObject.SetActive(false);

        f.Grid = content;
        f.GridScroll = sr;
        f.Empty = empty;
        return rt;
    }

    /// <summary>카드 견본 — 등급 띠 · 그림(망점 바탕) · 이름 · 아래 글 · 잠김 '?' · 오른쪽 위 표시 · 고르면 노란 테두리</summary>
    static void CardTemplate(TitlePanelFrame f, TMP_FontAsset font, bool small)
    {
        var rt = K.Rect("Card", f.Grid);
        var c = rt.gameObject.AddComponent<UiItemCard>();

        var sel = K.Img("Selected", rt, K.Plain, K.Gold);
        K.Fill(sel.rectTransform, -7f);
        c.Selected = sel.gameObject;

        var face = K.Img("Face", rt, K.Slab, K.Panel2);
        K.Fill(face.rectTransform);
        face.raycastTarget = true;
        c.Face = face;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = face;
        K.Tint(b);
        c.Button = b;

        float textH = small ? 50f : 62f;
        var iconBack = K.Img("IconBack", face.transform, K.Plain, K.Raised);
        var ib = iconBack.rectTransform;
        ib.anchorMin = new Vector2(0f, 0f);
        ib.anchorMax = new Vector2(1f, 1f);
        ib.offsetMin = new Vector2(12f, textH + 6f);
        ib.offsetMax = new Vector2(-12f, -26f);

        var icon = K.Img("Icon", iconBack.transform, null, Color.white);
        K.Fill(icon.rectTransform, 8f);
        icon.preserveAspect = true;
        c.Icon = icon;
        var raw = K.Rect("Raw", iconBack.transform).gameObject.AddComponent<RawImage>();
        K.Fill(raw.rectTransform, 8f);
        raw.raycastTarget = false;
        raw.enabled = false;
        c.Raw = raw;

        var grade = K.Img("Grade", face.transform, K.Plain, K.Gold);
        var gr = grade.rectTransform;
        gr.anchorMin = new Vector2(0f, 1f);
        gr.anchorMax = new Vector2(1f, 1f);
        gr.pivot = new Vector2(0.5f, 1f);
        gr.offsetMin = new Vector2(12f, -22f);
        gr.offsetMax = new Vector2(-40f, -9f);
        c.Grade = grade;

        var lockT = K.Text("Lock", iconBack.transform, K.Display(font), small ? 64f : 84f, K.Light, TextAlignmentOptions.Center);
        K.Fill(lockT.rectTransform);
        lockT.text = "?";
        lockT.fontStyle = FontStyles.Italic;
        K.InkOutline(lockT, 0.32f);
        lockT.gameObject.SetActive(false);
        c.Lock = lockT.gameObject;

        var t = K.Text("Title", face.transform, K.Display(font), small ? 18f : 21f, K.Ink, TextAlignmentOptions.Center);
        var tr = t.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f);
        tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0.5f, 0f);
        tr.offsetMin = new Vector2(10f, textH * 0.42f);
        tr.offsetMax = new Vector2(-10f, textH + 4f);
        t.fontStyle = FontStyles.Italic;
        t.text = "이름";
        c.Title = t;

        var s = K.Text("Sub", face.transform, font, small ? 14f : 16f, K.Dim, TextAlignmentOptions.Center);
        var sr = s.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 0f);
        sr.pivot = new Vector2(0.5f, 0f);
        sr.offsetMin = new Vector2(10f, 8f);
        sr.offsetMax = new Vector2(-10f, textH * 0.42f + 2f);
        s.text = "";
        c.Sub = s;

        var badge = K.Tag(face.transform, font, "장착", K.Mint, small ? 14f : 16f, out var bt, "Badge");
        Corner(badge, new Vector2(1f, 1f), new Vector2(10f, 12f), Vector2.zero);
        Fit(badge);
        badge.localRotation = K.Tilt(6f);
        badge.gameObject.SetActive(false);
        c.Badge = badge.gameObject;
        c.BadgeText = bt;

        sel.gameObject.SetActive(false);
        f.CardTemplate = c;
    }

    static void WideDetail(Transform body, TMP_FontAsset font, TitlePanelFrame f, bool model)
    {
        var rt = K.Rect("Detail", body);
        K.Size(rt, 560f, -1f, 0f);
        var face = K.Sticker(rt, K.Panel2, 0f, 0f);
        face.raycastTarget = true;
        K.StripesOn(face, 0.02f);
        var v = face.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(26, 26, 26, 22);
        v.spacing = 10f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        // 그림 칸 (3D 모델 · 아이콘 · 사진)
        var box = K.Rect("PreviewBox", face.transform);
        K.Size(box, -1f, 290f);
        var back = K.Img("Back", box, K.Plain, K.Field);
        K.Fill(back.rectTransform);
        K.Deco(back.transform, K.Spray, K.Purple, new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(420f, 160f), -6f, 0.35f);

        var raw = K.Rect("Preview", box).gameObject.AddComponent<RawImage>();
        K.Fill(raw.rectTransform, 8f);
        raw.raycastTarget = model;          // 끌어 돌리기
        raw.enabled = false;
        f.Preview = raw;
        if (model)
        {
            var vp = raw.gameObject.AddComponent<ModelViewport>();
            vp.Target = raw;
            f.Viewport = vp;
        }

        var icon = K.Img("PreviewIcon", box, null, Color.white);
        K.Center(icon.rectTransform, 0f, 0f, 240f, 240f);
        icon.preserveAspect = true;
        icon.enabled = false;
        f.PreviewIcon = icon;

        var name = K.Text("Name", face.transform, K.Display(font), 38f, K.Ink, TextAlignmentOptions.MidlineLeft);
        K.Size(name, -1f, 54f);
        name.fontStyle = FontStyles.Italic;
        K.InkOutline(name, 0.22f);
        f.Name = name;

        var sub = K.Text("Sub", face.transform, font, 21f, K.Dim, TextAlignmentOptions.MidlineLeft);
        K.Size(sub, -1f, 30f);
        f.Sub = sub;

        var scroll = K.Scroll("BodyScroll", face.transform, out var bodySr, 4f);
        f.BodyScroll = bodySr;
        K.Size(scroll.parent.parent, -1f, -1f, -1f, 1f);
        var text = K.Text("Body", scroll, font, 21f, K.Ink, TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = true;
        text.lineSpacing = 8f;
        f.Body = text;

        Actions(face.transform, font, f, 26f);
    }

    static void SideDetail(Transform body, TMP_FontAsset font, TitlePanelFrame f)
    {
        var rt = K.Rect("Detail", body);
        K.Size(rt, -1f, 250f, -1f, 0f);
        var face = K.Sticker(rt, K.Panel2, 0f, 0f);
        face.raycastTarget = true;
        K.StripesOn(face, 0.02f);
        var v = face.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(24, 24, 20, 18);
        v.spacing = 8f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var info = K.Row("Info", face.transform, 110f, 18f);
        var box = K.Rect("IconBox", info.transform);
        K.Size(box, 104f, 104f);
        var back = K.Img("Back", box, K.Plain, K.Field);
        K.Fill(back.rectTransform);
        var icon = K.Img("PreviewIcon", box, null, Color.white);
        K.Fill(icon.rectTransform, 10f);
        icon.preserveAspect = true;
        icon.enabled = false;
        f.PreviewIcon = icon;

        var col = K.Group("Text", info.transform, 2f);
        K.Size(col, -1f, -1f, 1f);
        col.childAlignment = TextAnchor.MiddleLeft;
        var name = K.Text("Name", col.transform, K.Display(font), 34f, K.Ink, TextAlignmentOptions.MidlineLeft);
        K.Size(name, -1f, 48f);
        name.fontStyle = FontStyles.Italic;
        K.InkOutline(name, 0.22f);
        f.Name = name;
        var sub = K.Text("Sub", col.transform, font, 20f, K.Dim, TextAlignmentOptions.MidlineLeft);
        K.Size(sub, -1f, 30f);
        f.Sub = sub;

        Actions(face.transform, font, f, 24f);
    }

    static void Actions(Transform parent, TMP_FontAsset font, TitlePanelFrame f, float size)
    {
        var row = K.Row("Actions", parent, 70f, 12f);
        row.childForceExpandWidth = true;
        f.ActionA = K.Btn("ActionA", row.transform, font, "사기", K.Yellow, size, out var a);
        K.Size(f.ActionA, -1f, 64f, 1f);
        f.ActionALabel = a;
        f.ActionB = K.Btn("ActionB", row.transform, font, "장착", K.Raised, size, out var b);
        K.Size(f.ActionB, -1f, 64f, 1f);
        f.ActionBLabel = b;
        f.ActionRow = row.gameObject;

        var note = K.Text("Note", parent, font, 18f, K.Dim, TextAlignmentOptions.MidlineLeft);
        K.Size(note, -1f, 26f);
        note.text = "";
        note.gameObject.SetActive(false);
        f.Note = note;
    }

    // ══════════════════ 도우미 ══════════════════

    /// <summary>앵커 한 점에 놓기 — pivot도 그 점 (size 0 = 안 건드림)</summary>
    static void Corner(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        if (size != Vector2.zero) rt.sizeDelta = size;
    }

    /// <summary>글자 길이만큼 (이름표)</summary>
    static void Fit(RectTransform rt)
    {
        var le = rt.GetComponent<LayoutElement>();
        if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }
}
