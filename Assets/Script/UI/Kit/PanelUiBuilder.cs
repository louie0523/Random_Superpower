using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 게임 창 — 테마 'Graffiti'가 켜져 있으면 v7 카툰 그래피티 (외곽선 · 스프레이 · 빨간 이름표), 꺼져 있으면 v2.
///   v7 인벤토리 = 왼쪽 내 캐릭터(HudPortrait 온몸 · 못 찍으면 카툰 인형) + 장비 3칸 / 오른쪽 파란 판 무기 칸
/// ── 아래는 v2 설명 ──
/// 게임 창 v2 (스티커 카툰) — 'UI 프리팹 만들기 창'의 '게임 창 (v2)'가 이걸로 프리팹 하나를 만듦.
///   능력 창 F1 (AbilityPanel + 카드 AbilityEntryView) — 시안 PanelV2: 머리 · 패시브 열 · 스킬 2열
///   스킬 상세 (DetailWindow) — 카드 누르면 가운데 창
///   상점 (ShopPanel) — 탭 3개: 메인(칸 격자) · 인챈트(EnchantPage) · 특수
///   순위표 Tab (ScoreboardView + ScoreboardRow)
///   툴팁 (TooltipView) — 상점 물품에 올리면
/// 기존 컴포넌트를 그대로 쓰고 모양만 새로 — 연결은 UIManager가 UiPrefabSet의 'Use Panels'를 보고 알아서.
/// 한 줄 · 카드 틀은 꺼진 '_Templates' 아래 (켜진 채로) — 복제하면 바로 보임.
///   인벤토리 (InventoryPanel + InventorySlotView) — 무기 격자 · 장비 3칸 · 진화 갈래 고르기
/// </summary>
public static class PanelUiBuilder
{
    const int Order = 400;      // HUD(100) 위 · 암시장(465) · 설정(30900~) 아래

    public static GameObject Build(GameObject host, TMP_FontAsset font, Sprite soul)
    {
        var display = K.Display(font);
        var root = K.MakeScreen(host, Order, null);

        var tpl = K.Rect("_Templates", root);
        K.Fill(tpl);

        var entry = EntryTemplate(tpl, font);
        var item = ShopItemTemplate(tpl, font, display);
        var row = EnchantRowTemplate(tpl, font);
        var choice = ChoiceTemplate(tpl, font);
        var pick = PickTemplate(tpl, font, display);
        var pickHeader = PickHeaderTemplate(tpl, display);
        var score = ScoreRowTemplate(tpl, font, display);
        var invSlot = InvSlotTemplate(tpl, font, display, null);
        var choiceBtn = K.Btn("UpgradeChoice", tpl, font, "무기 이름", K.Raised, 21f, out _);
        K.Size(choiceBtn, -1f, 58f);

        BuildAbility(root, font, display, entry);
        BuildShop(root, font, display, soul, item, row, choice, pick, pickHeader);
        BuildInventory(root, font, display, invSlot, choiceBtn);
        BuildScoreboard(root, font, display, score);
        BuildDetail(root, font, display);
        BuildTooltip(root, font);

        tpl.SetAsLastSibling();
        tpl.gameObject.SetActive(false);       // 틀 묶음만 끔 — 틀 자체는 켜진 채 (복제본이 바로 보이게)
        return host;
    }

    // ══════════════════ 능력 창 (F1) ══════════════════

    static void BuildAbility(RectTransform root, TMP_FontAsset font, TMP_FontAsset display, AbilityEntryView entry)
    {
        var screen = FullScreen(root, "AbilityPanel");
        var p = screen.gameObject.AddComponent<AbilityPanel>();
        var win = Window(screen, "Window", 1640f, 920f);

        // ── 머리 ──
        var icon = HexIcon(win, "ClassIcon", 112f, K.Mint, out var iconImg);
        Abs(icon, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -150f), new Vector2(152f, -38f));
        icon.localRotation = K.Tilt(-7f);

        var name = K.Text("Name", win, display, 56f, K.Ink);
        Abs(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(178f, -104f), new Vector2(-260f, -34f));
        name.text = "건마스터";
        Loud(name);

        var flavor = K.Text("Flavor", win, font, 20f, K.Dim, TextAlignmentOptions.TopLeft);
        Wrap(flavor);
        flavor.overflowMode = TextOverflowModes.Ellipsis;
        Abs(flavor.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(180f, -162f), new Vector2(-260f, -108f));
        flavor.text = "당신은 모든 총기류의 마스터입니다.";

        var hint = K.Text("CloseHint", win, font, 17f, K.Dim, TextAlignmentOptions.MidlineRight, true);
        Abs(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-250f, -84f), new Vector2(-96f, -40f));
        hint.text = "[F1] 닫기";
        var hintLoc = hint.gameObject.AddComponent<LocText>();

        var close = CloseButton(win, font);

        // ── 패시브 열 (왼쪽) ──
        var left = K.Rect("Passives", win);
        Abs(left, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(40f, 36f), new Vector2(560f, -186f));
        var pLabel = SectionTitle(left, display, "패시브", K.Light);
        var pScroll = K.Scroll("List", left, out _, 12f);
        Abs((RectTransform)pScroll.parent.parent, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -52f));
        var pEmpty = EmptyNote(left, font);

        // ── 스킬 열 (오른쪽, 2칸 격자) ──
        var right = K.Rect("Skills", win);
        Abs(right, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(592f, 36f), new Vector2(-40f, -186f));
        var sLabel = SectionTitle(right, display, "스킬", K.Mint);

        var pointsBox = TextChip(right, "Points", font, 18f, K.Gold, out var points);
        Abs(pointsBox, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-220f, -40f), new Vector2(-8f, -6f));
        points.text = "강화 포인트 1";

        var sGrid = GridScroll(right, new Vector2(486f, 176f), 2, 14f);
        Abs((RectTransform)sGrid.parent.parent, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -52f));
        var sEmpty = EmptyNote(right, font);

        Wire(p, "abilityIcon", iconImg);
        Wire(p, "abilityName", name);
        Wire(p, "abilityFlavor", flavor);
        Wire(p, "pointsText", points);
        Wire(p, "passiveParent", pScroll);
        Wire(p, "skillParent", sGrid);
        Wire(p, "passiveLabel", pLabel.gameObject.AddComponent<LocText>());
        Wire(p, "skillLabel", sLabel.gameObject.AddComponent<LocText>());
        Wire(p, "passiveEmpty", pEmpty.gameObject);
        Wire(p, "skillEmpty", sEmpty.gameObject);
        Wire(p, "entryPrefab", entry);
        Wire(p, "mainAccent", new Color(0f, 0f, 0f, 0f));
        Wire(p, "subAccent", K.Info);
        Wire(p, "randomAccent", K.Gold);
        Wire(p, "closeButton", close);
        Wire(p, "closeHint", hintLoc);
    }

    /// <summary>능력 카드 — 육각 아이콘(+키 이름표) · 이름 · 레벨 ◆ · + 버튼 · 설명. 왼쪽 띠 = 출처 색 (메인은 없음)</summary>
    static AbilityEntryView EntryTemplate(Transform tpl, TMP_FontAsset font)
    {
        var rt = K.Rect("AbilityEntry", tpl);
        rt.sizeDelta = new Vector2(486f, 150f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = K.Cut;
        bg.type = Image.Type.Sliced;
        bg.color = K.Panel2;
        bg.raycastTarget = true;

        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(22, 18, 16, 16);
        h.spacing = 18f;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var bar = K.Img("Accent", rt, null, K.Gold);
        Under(bar);
        bar.rectTransform.anchorMin = new Vector2(0f, 0f);
        bar.rectTransform.anchorMax = new Vector2(0f, 1f);
        bar.rectTransform.pivot = new Vector2(0f, 0.5f);
        bar.rectTransform.offsetMin = new Vector2(K.Graffiti ? 9f : 0f, K.Graffiti ? 14f : 10f);
        bar.rectTransform.offsetMax = new Vector2(K.Graffiti ? 17f : 6f, K.Graffiti ? -14f : -10f);

        // 아이콘 칸 (64) + 아래 키 이름표
        var box = K.Rect("IconBox", rt);
        K.Size(box, 64f, 84f, 0f, 0f);
        var hex = HexIcon(box, "Hex", 64f, K.Field, out var icon);
        hex.anchorMin = hex.anchorMax = hex.pivot = new Vector2(0.5f, 1f);
        hex.anchoredPosition = Vector2.zero;
        var slotBox = TextChip(box, "Slot", font, 14f, K.Field, out var slot);
        Abs(slotBox, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(-6f, 0f), new Vector2(6f, 24f));
        slot.text = "E";
        slot.enableAutoSizing = true;
        slot.fontSizeMin = 10f;
        slot.fontSizeMax = 14f;

        // 글자 열
        var col = K.Group("Text", rt, 6f);
        K.Size(col, -1f, -1f, 1f, 1f);
        var head = K.Row("Head", col.transform, 32f, 10f);
        head.padding = new RectOffset(0, 0, 0, 0);
        head.childForceExpandHeight = false;

        var title = K.Text("Title", head.transform, font, 23f, K.Ink, TextAlignmentOptions.MidlineLeft, true);
        K.Size(title, -1f, 32f, 1f);
        title.text = "정밀사격";

        var pips = K.Rect("Pips", head.transform);
        var ph = pips.gameObject.AddComponent<HorizontalLayoutGroup>();
        ph.spacing = 5f;
        ph.childAlignment = TextAnchor.MiddleLeft;
        ph.childControlWidth = ph.childControlHeight = false;
        ph.childForceExpandWidth = ph.childForceExpandHeight = false;
        K.Size(pips, -1f, 32f);
        for (int i = 0; i < 3; i++)
        {
            var d = K.Img("Pip", pips, null, i < 2 ? K.Mint : K.LineColor);
            d.rectTransform.sizeDelta = new Vector2(11f, 11f);
            d.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        var up = K.Btn("Upgrade", head.transform, font, "+", K.Mint, 24f, out _);
        K.Size(up, 40f, 32f);

        var body = K.Text("Body", col.transform, font, 18f, K.Dim, TextAlignmentOptions.TopLeft);
        Wrap(body);
        body.overflowMode = TextOverflowModes.Ellipsis;
        body.lineSpacing = 6f;
        K.Size(body, -1f, -1f, 1f, 1f);
        body.text = "4초간 줌 상태가 되며 이동 속도가 느려집니다. 다음 탄환은 적중 시 추가 피해를 가합니다.";

        var v = rt.gameObject.AddComponent<AbilityEntryView>();
        Wire(v, "background", bg);
        Wire(v, "accentBar", bar);
        Wire(v, "icon", icon);
        Wire(v, "slotLabel", slot);
        Wire(v, "title", title);
        Wire(v, "body", body);
        Wire(v, "pipParent", pips);
        Wire(v, "pipOn", K.Mint);
        Wire(v, "pipOff", K.LineColor);
        Wire(v, "upgradeButton", up);
        Wire(v, "normalColor", K.Panel2);
        Wire(v, "hoverColor", K.Raised);
        Wire(v, "selectedColor", K.MintDark);
        return v;
    }

    // ══════════════════ 스킬 상세 (DetailWindow) ══════════════════

    static void BuildDetail(RectTransform root, TMP_FontAsset font, TMP_FontAsset display)
    {
        var screen = K.Rect("DetailWindow", root);
        K.Fill(screen);
        var group = screen.gameObject.AddComponent<CanvasGroup>();
        var w = screen.gameObject.AddComponent<DetailWindow>();

        var back = K.Img("Backdrop", screen, null, new Color(0f, 0f, 0f, 0.45f));
        back.raycastTarget = true;
        K.Fill(back.rectTransform);
        var backBtn = back.gameObject.AddComponent<Button>();
        backBtn.transition = Selectable.Transition.None;

        var win = AutoWindow(screen, "Window", 780f, 28);

        var head = K.Row("Head", win, 84f, 18f);
        head.padding = new RectOffset(0, 0, 0, 0);
        head.childForceExpandHeight = false;
        head.childAlignment = TextAnchor.MiddleLeft;
        var iconBox = K.Rect("IconBox", head.transform);
        K.Size(iconBox, 76f, 76f);
        var hex = HexIcon(iconBox, "Hex", 76f, K.Field, out var icon);
        K.Fill(hex);

        var titles = K.Group("Titles", head.transform, 2f);
        titles.childAlignment = TextAnchor.MiddleLeft;
        K.Size(titles, -1f, -1f, 1f);
        var sub = K.Text("Subtitle", titles.transform, font, 17f, K.Mint, TextAlignmentOptions.MidlineLeft, true);
        K.Size(sub, -1f, 24f);
        sub.text = "E · 스킬";
        var title = K.Text("Title", titles.transform, display, 36f, K.Ink);
        K.Size(title, -1f, 46f);
        title.text = "정밀사격";
        Loud(title);

        var close = K.Btn("Close", head.transform, font, "×", K.Raised, 24f, out _);
        K.Size(close, 48f, 48f);

        var desc = K.Text("Desc", win, font, 21f, K.Ink, TextAlignmentOptions.TopLeft);
        Wrap(desc);
        desc.lineSpacing = 8f;
        desc.text = "4초간 줌 상태가 되며 이동 속도가 느려집니다.";

        var line = K.Img("Divider", win, null, K.LineColor);
        K.Size(line, -1f, 2f);

        var body = K.Text("Body", win, font, 18f, K.Dim, TextAlignmentOptions.TopLeft);
        Wrap(body);
        body.lineSpacing = 8f;
        body.text = "레벨 2 / 5\n쿨타임 12 / <b>10</b> / 8초";

        Wire(w, "group", group);
        Wire(w, "backdrop", backBtn);
        Wire(w, "closeButton", close);
        Wire(w, "icon", icon);
        Wire(w, "titleText", title);
        Wire(w, "subtitleText", sub);
        Wire(w, "descText", desc);
        Wire(w, "divider", line.gameObject);
        Wire(w, "bodyText", body);

        group.alpha = 0f;
        group.blocksRaycasts = group.interactable = false;
    }

    // ══════════════════ 툴팁 ══════════════════

    static void BuildTooltip(RectTransform root, TMP_FontAsset font)
    {
        var holder = K.Rect("Tooltip", root);
        K.Fill(holder);
        var view = holder.gameObject.AddComponent<TooltipView>();

        var box = K.Rect("Box", holder);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0f, 1f);
        box.sizeDelta = new Vector2(400f, 100f);
        var img = box.gameObject.AddComponent<Image>();
        img.sprite = K.Cut;
        img.type = Image.Type.Sliced;
        img.color = new Color(K.Panel.r, K.Panel.g, K.Panel.b, 0.98f);
        img.raycastTarget = false;
        DropShadow(box.gameObject, 6f);
        var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(20, 20, 16, 18);
        v.spacing = 8f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var fit = box.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = K.Text("Title", box, font, 21f, K.Ink, TextAlignmentOptions.TopLeft, true);
        Wrap(title);
        title.text = "체력 강화";
        // 능력치 / 줄 / 스펙 / 줄 / 설명 / 줄 / 태그 — 비어 있는 구역은 줄째 숨음
        var body = K.Text("Body", box, font, 17f, K.Ink, TextAlignmentOptions.TopLeft);
        Wrap(body);
        body.lineSpacing = 4f;
        body.text = "공격력  +12\n치명타 확률  +5%";
        var specLine = K.Img("SpecDivider", box, null, K.LineColor);
        K.Size(specLine, -1f, 2f);
        var spec = K.Text("Spec", box, font, 16f, K.Info, TextAlignmentOptions.TopLeft);
        Wrap(spec);
        spec.lineSpacing = 4f;
        spec.text = "사거리 30m\n탄약 12발\n초당 8발";
        var detailLine = K.Img("DetailDivider", box, null, K.LineColor);
        K.Size(detailLine, -1f, 2f);
        var detail = K.Text("Detail", box, font, 16f, K.Dim, TextAlignmentOptions.TopLeft);
        Wrap(detail);
        detail.lineSpacing = 6f;
        detail.text = "설명 글";
        var line = K.Img("Divider", box, null, K.LineColor);
        K.Size(line, -1f, 2f);
        var tags = K.Text("Tags", box, font, 15f, K.Light, TextAlignmentOptions.TopLeft, true);
        Wrap(tags);
        tags.lineSpacing = 18f;                        // 칩 두 줄일 때 안 겹치게
        tags.margin = new Vector4(2f, 4f, 2f, 6f);     // 칩 배경이 위아래로 안 잘리게

        Wire(view, "root", box);
        Wire(view, "titleText", title);
        Wire(view, "bodyText", body);
        Wire(view, "detailText", detail);
        Wire(view, "divider", line.gameObject);
        Wire(view, "tagsText", tags);
        Wire(view, "specText", spec);
        Wire(view, "specDivider", specLine.gameObject);
        Wire(view, "detailDivider", detailLine.gameObject);
        box.gameObject.SetActive(false);
    }

    // ══════════════════ 상점 ══════════════════

    static void BuildShop(RectTransform root, TMP_FontAsset font, TMP_FontAsset display, Sprite soul,
                          ShopItemView item, EnchantRowView row, SpecialChoiceView choice, PickEntryView pick, TMP_Text pickHeader)
    {
        var screen = FullScreen(root, "ShopPanel");
        var p = screen.gameObject.AddComponent<ShopPanel>();
        var win = Window(screen, "Window", 1340f, 860f);

        // ── 머리: 제목 · 소울 · 닫기 ──
        var title = K.Text("Title", win, display, 46f, K.Ink);
        Abs(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -96f), new Vector2(560f, -30f));
        title.text = "상점";

        var soulBox = K.Rect("Soul", win);
        Abs(soulBox, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-420f, -96f), new Vector2(-104f, -30f));
        var soulIcon = SoulSticker(soulBox, soul, 54f);
        soulIcon.anchorMin = soulIcon.anchorMax = soulIcon.pivot = new Vector2(0f, 0.5f);
        soulIcon.anchoredPosition = new Vector2(0f, 0f);
        var soulVal = K.Text("Value", soulBox, display, 40f, K.Gold, TextAlignmentOptions.MidlineLeft);
        Abs(soulVal.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(66f, 0f), new Vector2(-70f, 0f));
        soulVal.text = "1250";
        var soulLbl = K.Text("Label", soulBox, font, 17f, K.Dim, TextAlignmentOptions.MidlineRight, true);
        Abs(soulLbl.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-70f, 0f), Vector2.zero);
        soulLbl.text = "소울";

        var close = CloseButton(win, font);

        // ── 탭 ──
        var tabs = K.Rect("Tabs", win);
        Abs(tabs, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -170f), new Vector2(-40f, -114f));
        var th = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 10f;
        th.childAlignment = TextAnchor.MiddleLeft;
        th.childControlWidth = th.childControlHeight = true;
        th.childForceExpandWidth = th.childForceExpandHeight = false;
        var mainTab = Tab(tabs, font, "메인", out var mainLbl);
        var enchTab = Tab(tabs, font, "인챈트", out var enchLbl);
        var specTab = Tab(tabs, font, "특수", out var specLbl);
        mainTab.interactable = false;       // 시안: 메인 고른 모양

        // ── 페이지 ──
        var pages = K.Rect("Pages", win);
        Abs(pages, Vector2.zero, Vector2.one, new Vector2(40f, 34f), new Vector2(-40f, -188f));

        var mainPage = K.Rect("MainPage", pages);
        K.Fill(mainPage);
        var mainGrid = GridScroll(mainPage, new Vector2(300f, 172f), 4, 14f);
        Abs((RectTransform)mainGrid.parent.parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        for (int i = 0; i < 3; i++) Preview(item, mainGrid);

        var enchPage = K.Rect("EnchantPage", pages);
        K.Fill(enchPage);
        var ench = BuildEnchant(enchPage, font, display, soul, row, choice, pick, pickHeader);
        enchPage.gameObject.SetActive(false);

        var specPage = K.Rect("SpecialPage", pages);
        K.Fill(specPage);
        var specGrid = GridScroll(specPage, new Vector2(300f, 172f), 4, 14f);
        Abs((RectTransform)specGrid.parent.parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var specEmpty = K.Text("Empty", specPage, font, 22f, K.Dim, TextAlignmentOptions.Center);
        K.Fill(specEmpty.rectTransform);
        specEmpty.text = "특수 상점 물품이 없습니다";
        specPage.gameObject.SetActive(false);

        Wire(p, "soulValue", soulVal);
        Wire(p, "soulLabel", soulLbl.gameObject.AddComponent<LocText>());
        Wire(p, "titleLabel", title.gameObject.AddComponent<LocText>());
        Wire(p, "closeButton", close);
        Wire(p, "mainTab", mainTab);
        Wire(p, "enchantTab", enchTab);
        Wire(p, "specialTab", specTab);
        Wire(p, "mainTabLabel", mainLbl.gameObject.AddComponent<LocText>());
        Wire(p, "enchantTabLabel", enchLbl.gameObject.AddComponent<LocText>());
        Wire(p, "specialTabLabel", specLbl.gameObject.AddComponent<LocText>());
        Wire(p, "mainPage", mainPage.gameObject);
        Wire(p, "enchantPage", enchPage.gameObject);
        Wire(p, "specialPage", specPage.gameObject);
        Wire(p, "itemParent", mainGrid);
        Wire(p, "itemPrefab", item);
        Wire(p, "enchantView", ench);
        Wire(p, "specialParent", specGrid);
        Wire(p, "specialItemPrefab", item);
        Wire(p, "specialEmptyLabel", specEmpty.gameObject.AddComponent<LocText>());
    }

    /// <summary>인챈트 탭 — 왼쪽: 대상 무기 · 스톤 · 실행 / 오른쪽: 붙은 인챈트 목록 (특수 선택 · 대장장이 지정은 위에 덮음)</summary>
    static EnchantPage BuildEnchant(RectTransform page, TMP_FontAsset font, TMP_FontAsset display, Sprite soul,
                                    EnchantRowView row, SpecialChoiceView choice, PickEntryView pick, TMP_Text pickHeader)
    {
        var e = page.gameObject.AddComponent<EnchantPage>();

        // ── 왼쪽 ──
        var left = K.Rect("Left", page);
        Abs(left, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(400f, 0f));
        var lbg = K.Img("Back", left, K.Cut, K.Panel2);
        K.Fill(lbg.rectTransform);

        var wHex = HexIcon(left, "WeaponIcon", 110f, K.Field, out var wIcon);
        Abs(wHex, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-55f, -138f), new Vector2(55f, -28f));
        var wName = K.Text("WeaponName", left, display, 30f, K.Ink, TextAlignmentOptions.Center);
        Abs(wName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -190f), new Vector2(-20f, -146f));
        wName.text = "글록";
        var stage = K.Text("Stage", left, font, 22f, K.Gold, TextAlignmentOptions.Center, true);
        Abs(stage.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -226f), new Vector2(-20f, -192f));
        stage.text = "3 / 10";

        var stoneLbl = K.Text("StoneLabel", left, font, 18f, K.Dim, TextAlignmentOptions.MidlineLeft, true);
        Abs(stoneLbl.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -290f), new Vector2(-150f, -250f));
        stoneLbl.text = "인챈트 스톤";
        var stoneVal = K.Text("StoneValue", left, display, 32f, K.Info, TextAlignmentOptions.MidlineRight);
        Abs(stoneVal.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(180f, -294f), new Vector2(-28f, -246f));
        stoneVal.text = "4";

        var doBtn = K.Btn("Enchant", left, font, "인챈트", K.Mint, 26f, out var doLbl);
        Abs((RectTransform)doBtn.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 150f), new Vector2(-28f, 218f));
        var cost = K.Text("Cost", left, display, 28f, K.Gold, TextAlignmentOptions.MidlineLeft);
        Abs(cost.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-40f, 104f), new Vector2(120f, 144f));
        cost.text = "120";
        SoulDot(cost.rectTransform, soul);
        var reason = K.Text("Reason", left, font, 17f, K.Danger, TextAlignmentOptions.Center, true);
        Wrap(reason);
        Abs(reason.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 98f), new Vector2(-28f, 148f));
        reason.gameObject.SetActive(false);

        var pickBtn = K.Btn("Pick", left, font, "다음 인챈트 지정", K.Raised, 20f, out var pickLbl);
        Abs((RectTransform)pickBtn.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 28f), new Vector2(-28f, 82f));

        // ── 오른쪽: 목록 ──
        var right = K.Rect("Right", page);
        Abs(right, Vector2.zero, Vector2.one, new Vector2(424f, 0f), Vector2.zero);
        var list = K.Scroll("Rows", right, out _, 8f);
        Abs((RectTransform)list.parent.parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        for (int i = 0; i < 2; i++) Preview(row, list);
        var empty = K.Text("Empty", right, font, 22f, K.Dim, TextAlignmentOptions.Center);
        K.Fill(empty.rectTransform);
        empty.text = "아직 인챈트가 없습니다";
        empty.gameObject.SetActive(false);

        // ── 특수 단계 선택 (오른쪽 덮음) ──
        var sc = K.Rect("SpecialChoice", right);
        K.Fill(sc);
        var scBg = K.Img("Back", sc, K.Cut, K.Panel2);
        scBg.raycastTarget = true;
        K.Fill(scBg.rectTransform);
        var scTitle = SectionTitle(sc, display, "특수 인챈트 — 하나 고르기", K.Gold);
        Abs((RectTransform)scTitle.rectTransform.parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -64f), new Vector2(-24f, -20f));
        var scList = K.Rect("Choices", sc);
        Abs(scList, Vector2.zero, Vector2.one, new Vector2(20f, 90f), new Vector2(-20f, -74f));
        var sh = scList.gameObject.AddComponent<HorizontalLayoutGroup>();
        sh.spacing = 16f;
        sh.childAlignment = TextAnchor.MiddleCenter;
        sh.childControlWidth = sh.childControlHeight = true;
        sh.childForceExpandWidth = sh.childForceExpandHeight = false;
        var normal = K.Btn("NormalInstead", sc, font, "일반 인챈트로", K.Raised, 20f, out var normalLbl);
        Abs((RectTransform)normal.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-150f, 24f), new Vector2(150f, 74f));
        sc.gameObject.SetActive(false);

        // ── 대장장이 지정 창 (페이지 전체 덮음) ──
        var pr = K.Rect("PickWindow", page);
        K.Fill(pr);
        var prBg = K.Img("Back", pr, K.Cut, K.Panel2);
        prBg.raycastTarget = true;
        K.Fill(prBg.rectTransform);
        var prTitle = K.Text("Title", pr, display, 30f, K.Ink);
        Abs(prTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -64f), new Vector2(-100f, -16f));
        prTitle.text = "다음 인챈트 지정";
        var prClose = K.Btn("Close", pr, font, "×", K.Raised, 24f, out _);
        Abs((RectTransform)prClose.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-72f, -66f), new Vector2(-20f, -16f));
        var prList = K.Scroll("List", pr, out _, 8f);
        Abs((RectTransform)prList.parent.parent, Vector2.zero, Vector2.one, new Vector2(20f, 20f), new Vector2(-20f, -78f));
        pr.gameObject.SetActive(false);

        Wire(e, "stoneLabel", stoneLbl.gameObject.AddComponent<LocText>());
        Wire(e, "stoneValue", stoneVal);
        Wire(e, "weaponIcon", wIcon);
        Wire(e, "weaponName", wName);
        Wire(e, "stageText", stage);
        Wire(e, "rowParent", list);
        Wire(e, "rowPrefab", row);
        Wire(e, "emptyLabel", empty.gameObject.AddComponent<LocText>());
        Wire(e, "enchantButton", doBtn);
        Wire(e, "enchantLabel", doLbl.gameObject.AddComponent<LocText>());
        Wire(e, "costText", cost);
        Wire(e, "reasonText", reason);
        Wire(e, "specialChoiceRoot", sc.gameObject);
        Wire(e, "specialChoiceParent", scList);
        Wire(e, "specialChoicePrefab", choice);
        Wire(e, "specialChoiceTitle", scTitle.gameObject.AddComponent<LocText>());
        Wire(e, "normalInsteadButton", normal);
        Wire(e, "normalInsteadLabel", normalLbl.gameObject.AddComponent<LocText>());
        Wire(e, "pickButton", pickBtn);
        Wire(e, "pickLabel", pickLbl.gameObject.AddComponent<LocText>());
        Wire(e, "pickRoot", pr.gameObject);
        Wire(e, "pickTitle", prTitle.gameObject.AddComponent<LocText>());
        Wire(e, "pickParent", prList);
        Wire(e, "pickEntryPrefab", pick);
        Wire(e, "pickHeaderPrefab", pickHeader);
        Wire(e, "pickCloseButton", prClose);
        return e;
    }

    /// <summary>상점 물품 칸 — 아이콘 · 이름 · 레벨 / 아래: 가격(노랑) 또는 못 사는 이유(빨강) + 구매</summary>
    static ShopItemView ShopItemTemplate(Transform tpl, TMP_FontAsset font, TMP_FontAsset display)
    {
        var rt = K.Rect("ShopItem", tpl);
        rt.sizeDelta = new Vector2(300f, 172f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = K.Cut;
        bg.type = Image.Type.Sliced;
        bg.color = K.Panel2;
        bg.raycastTarget = true;        // 툴팁

        var hex = HexIcon(rt, "Icon", 66f, K.Field, out var icon);
        Abs(hex, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -82f), new Vector2(82f, -16f));

        var name = K.Text("Name", rt, font, 22f, K.Ink, TextAlignmentOptions.TopLeft, true);
        Wrap(name);
        name.overflowMode = TextOverflowModes.Ellipsis;
        Abs(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(96f, -74f), new Vector2(-14f, -16f));
        name.text = "체력 강화";

        var level = K.Text("Level", rt, font, 16f, K.Dim, TextAlignmentOptions.MidlineLeft, true);
        Abs(level.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(96f, -100f), new Vector2(-14f, -76f));
        level.text = "Lv 2 / 5";

        var price = K.Text("Price", rt, display, 28f, K.Gold, TextAlignmentOptions.MidlineLeft);
        Abs(price.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(46f, 14f), new Vector2(150f, 60f));
        price.text = "150";
        SoulDot(price.rectTransform, null);

        var reason = K.Text("Reason", rt, font, 15f, K.Danger, TextAlignmentOptions.MidlineLeft, true);
        Wrap(reason);
        reason.overflowMode = TextOverflowModes.Ellipsis;
        Abs(reason.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 12f), new Vector2(160f, 62f));
        reason.text = "소울 부족";
        reason.gameObject.SetActive(false);

        var buy = K.Btn("Buy", rt, font, "구매", K.Mint, 20f, out var buyLbl);
        Abs((RectTransform)buy.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-132f, 14f), new Vector2(-14f, 60f));

        var tip = rt.gameObject.AddComponent<TooltipTrigger>();
        var v = rt.gameObject.AddComponent<ShopItemView>();
        Wire(v, "icon", icon);
        Wire(v, "nameText", name);
        Wire(v, "levelText", level);
        Wire(v, "priceText", price);
        Wire(v, "buyButton", buy);
        Wire(v, "buyLabel", buyLbl.gameObject.AddComponent<LocText>());
        Wire(v, "reasonText", reason);
        Wire(v, "tooltip", tip);
        return v;
    }

    /// <summary>인챈트 한 줄 — ×N / ★ · 이름(인챈트 색) · 수치 · NEW</summary>
    static EnchantRowView EnchantRowTemplate(Transform tpl, TMP_FontAsset font)
    {
        var rt = K.Rect("EnchantRow", tpl);
        K.Size(rt, -1f, 66f);
        rt.sizeDelta = new Vector2(860f, 66f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = K.Cut;
        bg.type = Image.Type.Sliced;
        bg.color = K.Panel2;

        var special = K.Img("Special", rt, K.Cut, new Color(K.Gold.r, K.Gold.g, K.Gold.b, 0.14f));
        K.Fill(special.rectTransform);

        var idx = K.Text("Index", rt, font, 22f, K.Gold, TextAlignmentOptions.Center, true);
        Abs(idx.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(68f, 0f));
        idx.text = "×2";

        var name = K.Text("Name", rt, font, 21f, K.Ink, TextAlignmentOptions.MidlineLeft, true);
        Abs(name.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(76f, 0f), new Vector2(316f, 0f));
        name.text = "날카로움";

        var stats = K.Text("Stats", rt, font, 17f, K.Dim, TextAlignmentOptions.MidlineLeft);
        Wrap(stats);
        stats.overflowMode = TextOverflowModes.Ellipsis;
        Abs(stats.rectTransform, Vector2.zero, Vector2.one, new Vector2(326f, 4f), new Vector2(-90f, -4f));
        stats.text = "공격력 +6   치명타 확률 +3%";

        var nw = Chip(rt, font, "NEW", K.Mint);
        Abs(nw, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-78f, -14f), new Vector2(-14f, 14f));

        var v = rt.gameObject.AddComponent<EnchantRowView>();
        Wire(v, "indexText", idx);
        Wire(v, "nameText", name);
        Wire(v, "statsText", stats);
        Wire(v, "newMark", nw.gameObject);
        Wire(v, "specialMark", special.gameObject);
        return v;
    }

    /// <summary>특수 인챈트 선택지 카드</summary>
    static SpecialChoiceView ChoiceTemplate(Transform tpl, TMP_FontAsset font)
    {
        var rt = K.Rect("SpecialChoice", tpl);
        K.Size(rt, 270f, 260f);
        rt.sizeDelta = new Vector2(270f, 260f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = K.Cut;
        bg.type = Image.Type.Sliced;
        bg.color = K.Raised;
        bg.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        K.Tint(b);

        var hex = HexIcon(rt, "Icon", 76f, K.Field, out var icon);
        Abs(hex, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-38f, -96f), new Vector2(38f, -20f));
        var name = K.Text("Name", rt, font, 23f, K.Gold, TextAlignmentOptions.Center, true);
        Abs(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -140f), new Vector2(-12f, -104f));
        name.text = "둔화 II";
        var desc = K.Text("Desc", rt, font, 16f, K.Dim, TextAlignmentOptions.Top);
        Wrap(desc);
        desc.overflowMode = TextOverflowModes.Ellipsis;
        Abs(desc.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 14f), new Vector2(-16f, -146f));
        desc.text = "적중 시 2초간 이동 속도 20% 감소";
        var upMark = Chip(rt, font, "▲", K.Gold);
        Abs(upMark, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-52f, -42f), new Vector2(-12f, -12f));

        var v = rt.gameObject.AddComponent<SpecialChoiceView>();
        Wire(v, "button", b);
        Wire(v, "icon", icon);
        Wire(v, "nameText", name);
        Wire(v, "descText", desc);
        Wire(v, "upgradeMark", upMark.gameObject);
        return v;
    }

    /// <summary>대장장이 지정 목록 한 줄</summary>
    static PickEntryView PickTemplate(Transform tpl, TMP_FontAsset font, TMP_FontAsset display)
    {
        var rt = K.Rect("PickEntry", tpl);
        K.Size(rt, -1f, 76f);
        rt.sizeDelta = new Vector2(860f, 76f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = K.Cut;
        bg.type = Image.Type.Sliced;
        bg.color = K.Raised;
        bg.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        K.Tint(b);

        var hex = HexIcon(rt, "Icon", 52f, K.Field, out var icon);
        Abs(hex, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, -26f), new Vector2(66f, 26f));
        var name = K.Text("Name", rt, font, 20f, K.Ink, TextAlignmentOptions.MidlineLeft, true);
        Abs(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(80f, 0f), new Vector2(-150f, -6f));
        name.text = "흡혈 I";
        var desc = K.Text("Desc", rt, font, 16f, K.Dim, TextAlignmentOptions.MidlineLeft);
        Abs(desc.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(80f, 6f), new Vector2(-150f, 0f));
        desc.text = "피해의 5%만큼 회복";
        var cost = K.Text("Cost", rt, display, 26f, K.Gold, TextAlignmentOptions.MidlineRight);
        Abs(cost.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-110f, 0f), new Vector2(-18f, 0f));
        cost.text = "300";
        var upMark = Chip(rt, font, "▲", K.Gold);
        Abs(upMark, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, -14f), new Vector2(-116f, 14f));

        var v = rt.gameObject.AddComponent<PickEntryView>();
        Wire(v, "button", b);
        Wire(v, "icon", icon);
        Wire(v, "nameText", name);
        Wire(v, "descText", desc);
        Wire(v, "costText", cost);
        Wire(v, "upgradeMark", upMark.gameObject);
        Wire(v, "costOk", K.Gold);
        Wire(v, "costLack", K.Danger);
        return v;
    }

    static TMP_Text PickHeaderTemplate(Transform tpl, TMP_FontAsset display)
    {
        var t = K.Text("PickHeader", tpl, display, 22f, K.Ink, TextAlignmentOptions.BottomLeft);
        K.Size(t, -1f, 40f);
        t.text = "특수 인챈트";
        return t;
    }

    // ══════════════════ 인벤토리 (I) ══════════════════

    static void BuildInventory(RectTransform root, TMP_FontAsset font, TMP_FontAsset display,
                               InventorySlotView slotTpl, Button choiceTpl)
    {
        if (K.Graffiti) { BuildInventoryV7(root, font, display, slotTpl, choiceTpl); return; }
        var screen = FullScreen(root, "InventoryPanel");
        var p = screen.gameObject.AddComponent<InventoryPanel>();
        var win = Window(screen, "Window", 1180f, 880f);

        var title = K.Text("Title", win, display, 46f, K.Ink);
        Abs(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -96f), new Vector2(600f, -30f));
        title.text = "인벤토리";
        var close = CloseButton(win, font);

        // ── 무기 (격자 — 칸 수는 InventoryPanel이 만듦) ──
        var wSec = K.Rect("Weapons", win);
        Abs(wSec, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -564f), new Vector2(-40f, -116f));
        var wTitle = SectionTitle(wSec, display, "무기", K.Mint);
        var grid = K.Rect("Slots", wSec);
        Abs(grid, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -54f));
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(156f, 190f);
        g.spacing = new Vector2(14f, 14f);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 6;
        g.childAlignment = TextAnchor.UpperLeft;
        for (int i = 0; i < 4; i++) Preview(slotTpl, grid);

        // ── 장비 (갑옷 · 투구 · 신발) ──
        var gSec = K.Rect("Gear", win);
        Abs(gSec, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -838f), new Vector2(-40f, -584f));
        var gTitle = SectionTitle(gSec, display, "장비", K.Light);
        var hint = K.Text("DropHint", gSec, font, 17f, K.Dim, TextAlignmentOptions.MidlineRight, true);
        Abs(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-560f, -44f), Vector2.zero);
        hint.text = "[G] 현재 무기 버리기";
        var gearRow = K.Rect("Slots", gSec);
        Abs(gearRow, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -54f));
        var gh = gearRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        gh.spacing = 14f;
        gh.childAlignment = TextAnchor.UpperLeft;
        gh.childControlWidth = gh.childControlHeight = true;
        gh.childForceExpandWidth = gh.childForceExpandHeight = false;
        var armor = InvSlotTemplate(gearRow, font, display, "ui.item.slot.Armor");
        var helmet = InvSlotTemplate(gearRow, font, display, "ui.item.slot.Helmet");
        var boots = InvSlotTemplate(gearRow, font, display, "ui.item.slot.Boots");

        // ── 진화 갈래 고르기 (창 전체 덮음) ──
        var choose = K.Rect("UpgradeChoice", win);
        K.Fill(choose);
        var cDim = K.Img("Dim", choose, K.Cut, new Color(0f, 0f, 0f, 0.55f));
        cDim.raycastTarget = true;
        K.Fill(cDim.rectTransform);
        var cDimBtn = cDim.gameObject.AddComponent<Button>();
        cDimBtn.transition = Selectable.Transition.None;
        var box = AutoWindow(choose, "Box", 560f, 26);
        var cTitle = K.Text("Title", box, display, 30f, K.Ink);
        K.Size(cTitle, -1f, 44f);
        cTitle.text = "진화할 무기 고르기";
        var cList = K.Group("Choices", box, 10f);
        var cancel = K.Btn("Cancel", box, font, "×", K.Raised, 22f, out _);
        K.Size(cancel, -1f, 48f);
        choose.gameObject.SetActive(false);
#if UNITY_EDITOR
        UnityEditor.Events.UnityEventTools.AddPersistentListener(cDimBtn.onClick, p.CancelUpgradeChoice);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(cancel.onClick, p.CancelUpgradeChoice);
#endif

        Wire(p, "weaponSlotParent", grid);
        Wire(p, "weaponSlotPrefab", slotTpl);
        Wire(p, "armorSlot", armor);
        Wire(p, "helmetSlot", helmet);
        Wire(p, "bootsSlot", boots);
        Wire(p, "titleLabel", title.gameObject.AddComponent<LocText>());
        Wire(p, "weaponSectionLabel", wTitle.gameObject.AddComponent<LocText>());
        Wire(p, "gearSectionLabel", gTitle.gameObject.AddComponent<LocText>());
        Wire(p, "dropHint", hint.gameObject.AddComponent<LocText>());
        Wire(p, "upgradeChoiceRoot", choose.gameObject);
        Wire(p, "upgradeChoiceParent", cList.transform);
        Wire(p, "upgradeChoicePrefab", choiceTpl);
        Wire(p, "upgradeChoiceTitle", cTitle.gameObject.AddComponent<LocText>());
        Wire(p, "closeButton", close);
    }

    /// <summary>
    /// v7 인벤토리 (시안 InvV7) — 창 1720×920
    ///  ├ 제목 (스프레이 밑줄) · 닫기
    ///  ├ 왼쪽 '장비': 핑크 스프레이 위에 내 캐릭터 (HudPortrait 온몸 — 못 찍으면 카툰 인형) + 투구 · 갑옷 · 신발 칸
    ///  └ 오른쪽 파란 판 '무기': 칸 격자 (4열) · [G] 버리기 안내
    /// InventoryPanel 칸 이름은 v2와 같음
    /// </summary>
    static void BuildInventoryV7(RectTransform root, TMP_FontAsset font, TMP_FontAsset display,
                                 InventorySlotView slotTpl, Button choiceTpl)
    {
        var screen = FullScreen(root, "InventoryPanel");
        var p = screen.gameObject.AddComponent<InventoryPanel>();
        var win = Window(screen, "Window", 1720f, 920f);

        // 제목
        var tBox = K.Rect("TitleBox", win);
        Abs(tBox, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -118f), new Vector2(560f, -34f));
        K.Deco(tBox, K.Spray, K.Gold, new Vector2(0f, 0f), new Vector2(170f, 18f), new Vector2(360f, 46f), -2f);
        var title = K.Text("Title", tBox, display, 58f, K.Ink);
        K.Fill(title.rectTransform);
        title.text = "인벤토리";
        Loud(title);
        var close = CloseButton(win, font);

        // ── 왼쪽: 장비 + 내 캐릭터 ──
        var gSec = K.Rect("Gear", win);
        Abs(gSec, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(40f, 40f), new Vector2(700f, -140f));
        K.Deco(gSec, K.SprayDrip, K.Pink, new Vector2(0.5f, 0.5f), new Vector2(-10f, -40f), new Vector2(760f, 380f), -14f);

        var who = K.Rect("Character", gSec);
        who.anchorMin = who.anchorMax = new Vector2(0.5f, 0.5f);
        who.pivot = new Vector2(0.5f, 0.5f);
        who.anchoredPosition = new Vector2(0f, -10f);
        who.sizeDelta = new Vector2(330f, 600f);

        var fallback = K.Rect("Doll", who);
        K.Fill(fallback);
        var dw = 250f;
        var dh = dw * UiGraffitiArt.DollH / UiGraffitiArt.DollW;
        var shirt = K.Img("Shirt", fallback, K.DollShirt, new Color(0.310f, 0.639f, 0.878f));
        var doll = K.Img("Body", fallback, K.Doll, Color.white);
        foreach (var d in new[] { shirt, doll }) K.Center(d.rectTransform, 0f, 0f, dw, dh);

        var live = K.Rect("Live", who);
        K.Fill(live);
        var raw = live.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        var cam = who.gameObject.AddComponent<HudPortrait>();
        cam.Target = raw;
        cam.LiveRoot = live.gameObject;
        cam.FallbackRoot = fallback.gameObject;
        cam.FullBody = true;
        cam.BodyHeight = 0.95f;
        cam.Distance = 3.4f;
        cam.Fov = 30f;
        cam.Far = 6f;
        cam.Size = 640;
        cam.Aspect = 330f / 600f;
        cam.Background = new Color(0f, 0f, 0f, 0f);
        live.gameObject.SetActive(false);

        var gTitle = SectionTitle(gSec, display, "장비", K.Light);
        var helmet = InvSlotTemplate(gSec, font, display, "ui.item.slot.Helmet");
        var armor = InvSlotTemplate(gSec, font, display, "ui.item.slot.Armor");
        var boots = InvSlotTemplate(gSec, font, display, "ui.item.slot.Boots");
        GearAt(helmet, new Vector2(1f, 1f), new Vector2(-90f, -150f), 5f);
        GearAt(armor, new Vector2(0f, 0.5f), new Vector2(90f, 10f), -4f);
        GearAt(boots, new Vector2(1f, 0f), new Vector2(-90f, 130f), -3f);

        // ── 오른쪽: 파란 판 · 무기 ──
        var wSec = K.Rect("Weapons", win);
        Abs(wSec, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(740f, 110f), new Vector2(-40f, -150f));
        var wFace = K.Sticker(wSec, K.BluePanel, 0f, 0f);
        K.StripesOn(wFace, 0.14f);
        var wTitle = SectionTitle(wSec, display, "무기", K.Danger);
        ((RectTransform)wTitle.transform.parent).anchoredPosition += new Vector2(22f, 22f);   // 판 위 가장자리에 걸치게
        var grid = K.Rect("Slots", wFace.transform);
        Abs(grid, Vector2.zero, Vector2.one, new Vector2(34f, 30f), new Vector2(-30f, -48f));
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(196f, 230f);
        g.spacing = new Vector2(20f, 22f);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 4;
        g.childAlignment = TextAnchor.UpperLeft;
        for (int i = 0; i < 4; i++) Preview(slotTpl, grid);

        var hint = K.Text("DropHint", win, font, 19f, K.Dim, TextAlignmentOptions.MidlineRight, true);
        Abs(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-760f, 40f), new Vector2(-44f, 90f));
        hint.text = "[G] 현재 무기 버리기";

        // ── 진화 갈래 고르기 (창 전체 덮음) ──
        var choose = K.Rect("UpgradeChoice", win);
        K.Fill(choose);
        var cDim = K.Img("Dim", choose, null, new Color(0f, 0f, 0f, 0.6f));
        cDim.raycastTarget = true;
        K.Fill(cDim.rectTransform);
        var cDimBtn = cDim.gameObject.AddComponent<Button>();
        cDimBtn.transition = Selectable.Transition.None;
        var box = AutoWindow(choose, "Box", 560f, 30);
        var cTitle = K.Text("Title", box, display, 32f, K.Ink);
        K.Size(cTitle, -1f, 46f);
        cTitle.text = "진화할 무기 고르기";
        Loud(cTitle);
        var cList = K.Group("Choices", box, 10f);
        var cancel = K.Btn("Cancel", box, font, "×", K.Raised, 22f, out _);
        K.Size(cancel, -1f, 48f);
        choose.gameObject.SetActive(false);
#if UNITY_EDITOR
        UnityEditor.Events.UnityEventTools.AddPersistentListener(cDimBtn.onClick, p.CancelUpgradeChoice);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(cancel.onClick, p.CancelUpgradeChoice);
#endif

        Wire(p, "weaponSlotParent", grid);
        Wire(p, "weaponSlotPrefab", slotTpl);
        Wire(p, "armorSlot", armor);
        Wire(p, "helmetSlot", helmet);
        Wire(p, "bootsSlot", boots);
        Wire(p, "titleLabel", title.gameObject.AddComponent<LocText>());
        Wire(p, "weaponSectionLabel", wTitle.gameObject.AddComponent<LocText>());
        Wire(p, "gearSectionLabel", gTitle.gameObject.AddComponent<LocText>());
        Wire(p, "dropHint", hint.gameObject.AddComponent<LocText>());
        Wire(p, "upgradeChoiceRoot", choose.gameObject);
        Wire(p, "upgradeChoiceParent", cList.transform);
        Wire(p, "upgradeChoicePrefab", choiceTpl);
        Wire(p, "upgradeChoiceTitle", cTitle.gameObject.AddComponent<LocText>());
        Wire(p, "closeButton", close);
    }

    /// <summary>장비 칸을 캐릭터 둘레에 스티커처럼 붙이기 (앵커 · 위치 · 살짝 돌림)</summary>
    static void GearAt(InventorySlotView slot, Vector2 anchor, Vector2 pos, float tilt)
    {
        var rt = (RectTransform)slot.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.localRotation = Quaternion.Euler(0f, 0f, tilt);
    }

    /// <summary>
    /// 인벤토리 칸 (156×190) — 번호 · 강화 +N · 아이콘 · 탄약 · 진화 · 버리기(▼). 고른 무기 = 민트 바탕.
    /// caption(키)이 있으면 장비 칸 — 왼쪽 위에 갑옷 · 투구 · 신발
    /// </summary>
    static InventorySlotView InvSlotTemplate(Transform parent, TMP_FontAsset font, TMP_FontAsset display, string caption)
    {
        var rt = K.Rect(caption == null ? "InventorySlot" : "Gear_" + caption.Substring(caption.LastIndexOf('.') + 1), parent);
        rt.sizeDelta = new Vector2(156f, 190f);
        K.Size(rt, 156f, 190f);
        var frame = rt.gameObject.AddComponent<Image>();
        frame.sprite = K.Cut;
        frame.type = Image.Type.Sliced;
        frame.color = K.Panel2;
        frame.raycastTarget = true;
        var click = rt.gameObject.AddComponent<Button>();
        click.targetGraphic = frame;
        K.Tint(click);
        var tip = rt.gameObject.AddComponent<TooltipTrigger>();

        var num = K.Text("Number", rt, display, 24f, K.Dim, TextAlignmentOptions.TopLeft);
        Abs(num.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -40f), new Vector2(60f, -8f));
        num.text = "1";

        if (caption != null)
        {
            var cap = K.Text("Caption", rt, font, 15f, K.Dim, TextAlignmentOptions.TopLeft, true);
            Abs(cap.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -34f), new Vector2(-10f, -8f));
            cap.text = caption.EndsWith("Armor") ? "갑옷" : caption.EndsWith("Helmet") ? "투구" : "신발";
            SetLocText.Add(cap, caption);
        }

        var ench = K.Rect("Enchant", rt);
        Abs(ench, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -36f), new Vector2(-8f, -8f));
        var eBg = K.Img("Back", ench, K.Cut, K.Gold);
        K.Fill(eBg.rectTransform);
        var eCount = K.Text("Count", ench, font, 16f, K.TextOn(K.Gold), TextAlignmentOptions.Center, true);
        K.Fill(eCount.rectTransform);
        eCount.text = "+3";

        var icon = K.Img("Icon", rt, null, Color.white);
        icon.preserveAspect = true;
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(0f, 14f);
        icon.rectTransform.sizeDelta = new Vector2(96f, 96f);
        icon.enabled = false;

        var empty = K.Text("Empty", rt, font, 16f, K.Dim, TextAlignmentOptions.Center);
        Abs(empty.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 40f), new Vector2(-8f, -30f));
        empty.text = "비어 있음";

        var ammo = K.Text("Ammo", rt, font, 15f, K.Ink, TextAlignmentOptions.Center, true);
        Abs(ammo.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8f, 42f), new Vector2(-8f, 62f));
        ammo.text = "12 / 30";

        var up = K.Btn("Upgrade", rt, font, "진화", K.Gold, 16f, out var upLbl);
        Abs((RectTransform)up.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 8f), new Vector2(-50f, 38f));
        SetLocText.Add(upLbl, "ui.item.upgrades");
        up.gameObject.SetActive(false);

        var drop = K.Btn("Drop", rt, font, "▼", K.Raised, 16f, out _);
        Abs((RectTransform)drop.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-44f, 8f), new Vector2(-8f, 38f));

        var v = rt.gameObject.AddComponent<InventorySlotView>();
        Wire(v, "icon", icon);
        Wire(v, "frame", frame);
        Wire(v, "slotNumber", num);
        Wire(v, "ammoText", ammo);
        Wire(v, "enchantMark", ench.gameObject);
        Wire(v, "enchantCount", eCount);
        Wire(v, "clickArea", click);
        Wire(v, "dropButton", drop);
        Wire(v, "upgradeButton", up);
        Wire(v, "emptyLabel", empty.gameObject.AddComponent<LocText>());
        Wire(v, "tooltip", tip);
        Wire(v, "activeColor", K.MintDark);
        Wire(v, "normalColor", K.Panel2);
        return v;
    }

    // ══════════════════ 순위표 (Tab) ══════════════════

    // 칸 너비 — 머리줄 · 한 줄이 같이 씀 (이름이 남는 자리)
    const float CRank = 64f, CIcon = 42f, CClass = 210f, CAltar = 96f, CKda = 64f, CSoul = 110f;

    static void BuildScoreboard(RectTransform root, TMP_FontAsset font, TMP_FontAsset display, ScoreboardRow rowTpl)
    {
        var holder = K.Rect("Scoreboard", root);
        K.Fill(holder);
        var view = holder.gameObject.AddComponent<ScoreboardView>();

        var win = K.Rect("Board", holder);
        win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
        win.pivot = new Vector2(0.5f, 0.5f);
        win.anchoredPosition = new Vector2(0f, 40f);
        win.sizeDelta = new Vector2(1180f, 400f);
        var img = win.gameObject.AddComponent<Image>();
        img.sprite = K.Cut;
        img.type = Image.Type.Sliced;
        img.color = new Color(K.Panel.r, K.Panel.g, K.Panel.b, 0.95f);
        DropShadow(win.gameObject, 10f);
        var v = win.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(28, 28, 22, 26);
        v.spacing = 8f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var fit = win.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = K.Text("Title", win, display, 34f, K.Ink);
        K.Size(title, -1f, 48f);
        title.text = "순위표";
        Loud(title);
        SetLocText.Add(title, "ui.score.title");

        // 머리줄
        var head = ScoreLine(win, "Head", 30f);
        HeadCell(head, font, "순위", "ui.score.rank", CRank, TextAlignmentOptions.Center);
        Spacer(head, CIcon);
        HeadCell(head, font, "이름", "ui.score.name", -1f, TextAlignmentOptions.MidlineLeft);
        HeadCell(head, font, "클래스", "ui.score.class", CClass, TextAlignmentOptions.MidlineLeft);
        HeadCell(head, font, "제단", "ui.score.altar", CAltar, TextAlignmentOptions.Center);
        HeadCell(head, font, "K", null, CKda, TextAlignmentOptions.Center);
        HeadCell(head, font, "A", null, CKda, TextAlignmentOptions.Center);
        HeadCell(head, font, "D", null, CKda, TextAlignmentOptions.Center);
        HeadCell(head, font, "소울", "ui.hud.soul", CSoul, TextAlignmentOptions.Center);

        var rows = K.Group("Rows", win, 6f);
        for (int i = 0; i < 3; i++) Preview(rowTpl, rows.transform);

        Wire(view, "root", win.gameObject);
        Wire(view, "rowParent", rows.transform);
        Wire(view, "rowPrefab", rowTpl);
        win.gameObject.SetActive(false);
    }

    static ScoreboardRow ScoreRowTemplate(Transform tpl, TMP_FontAsset font, TMP_FontAsset display)
    {
        var line = ScoreLine(tpl, "ScoreRow", 54f);
        var rt = (RectTransform)line.transform;
        rt.sizeDelta = new Vector2(1120f, 54f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = K.Cut;
        bg.type = Image.Type.Sliced;
        bg.color = K.Panel2;

        var hl = K.Rect("Local", rt);
        Under(hl);
        K.Fill(hl);
        var hlFace = K.Img("Face", hl, K.Cut, K.MintDark);
        K.Fill(hlFace.rectTransform);
        var hlBar = K.Img("Bar", hl, null, K.Mint);
        hlBar.rectTransform.anchorMin = new Vector2(0f, 0f);
        hlBar.rectTransform.anchorMax = new Vector2(0f, 1f);
        hlBar.rectTransform.pivot = new Vector2(0f, 0.5f);
        hlBar.rectTransform.offsetMin = new Vector2(0f, 8f);
        hlBar.rectTransform.offsetMax = new Vector2(6f, -8f);

        var rank = Cell(line, display, 26f, K.Ink, CRank, TextAlignmentOptions.Center, "Rank", "1");
        var iconBox = K.Rect("ClassIcon", line.transform);
        K.Size(iconBox, CIcon, 40f);
        var hex = HexIcon(iconBox, "Hex", 40f, K.Field, out var icon);
        K.Fill(hex);
        var name = Cell(line, font, 22f, K.Ink, -1f, TextAlignmentOptions.MidlineLeft, "Name", "정루이", true);
        var cls = Cell(line, font, 18f, K.Mint, CClass, TextAlignmentOptions.MidlineLeft, "Class", "건마스터", true);
        var altar = Cell(line, font, 20f, K.Ink, CAltar, TextAlignmentOptions.Center, "Altar", "1/3", true);
        var k = Cell(line, font, 21f, K.Ink, CKda, TextAlignmentOptions.Center, "K", "5");
        var a = Cell(line, font, 21f, K.Dim, CKda, TextAlignmentOptions.Center, "A", "2");
        var d = Cell(line, font, 21f, K.Dim, CKda, TextAlignmentOptions.Center, "D", "1");
        var soul = Cell(line, display, 24f, K.Gold, CSoul, TextAlignmentOptions.Center, "Soul", "840");

        var v = rt.gameObject.AddComponent<ScoreboardRow>();
        Wire(v, "rankText", rank);
        Wire(v, "nameText", name);
        Wire(v, "killsText", k);
        Wire(v, "assistsText", a);
        Wire(v, "deathsText", d);
        Wire(v, "soulText", soul);
        Wire(v, "localHighlight", hl.gameObject);
        Wire(v, "altarText", altar);
        Wire(v, "classText", cls);
        Wire(v, "classIcon", icon);
        hl.gameObject.SetActive(false);
        return v;
    }

    static HorizontalLayoutGroup ScoreLine(Transform parent, string name, float h)
    {
        var rt = K.Rect(name, parent);
        K.Size(rt, -1f, h);
        var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        g.padding = new RectOffset(10, 14, 0, 0);
        g.spacing = 8f;
        g.childAlignment = TextAnchor.MiddleLeft;
        g.childControlWidth = g.childControlHeight = true;
        g.childForceExpandWidth = false;
        g.childForceExpandHeight = false;
        return g;
    }

    static TextMeshProUGUI Cell(HorizontalLayoutGroup line, TMP_FontAsset font, float size, Color c, float w,
                                TextAlignmentOptions align, string name, string sample, bool bold = false)
    {
        var t = K.Text(name, line.transform, font, size, c, align, bold);
        if (w > 0f) K.Size(t, w, 40f, 0f);
        else K.Size(t, 0f, 40f, 1f);
        t.text = sample;
        return t;
    }

    static void HeadCell(HorizontalLayoutGroup line, TMP_FontAsset font, string label, string key, float w, TextAlignmentOptions align)
    {
        var t = Cell(line, font, 16f, K.Dim, w, align, "H_" + label, label, true);
        if (key != null) SetLocText.Add(t, key);
    }

    static void Spacer(HorizontalLayoutGroup line, float w)
    {
        var s = K.Rect("Space", line.transform);
        K.Size(s, w, 10f, 0f);
    }

    // ══════════════════ 도우미 ══════════════════

    /// <summary>미리보기 줄 · 칸 — 프리팹 열면 보이고, 실행하면 사라짐</summary>
    static void Preview(Component tpl, Transform parent)
    {
        var go = Object.Instantiate(tpl.gameObject, parent);
        go.name = "Preview";
        go.AddComponent<UiPreviewOnly>();
    }

    /// <summary>
    /// 배경 칩 + 글자 — 코드가 글자만 켜고 꺼도 배경이 따라감 (UiActiveMirror).
    /// 돌려주는 건 묶음 (위치는 이걸로), 글자는 out
    /// </summary>
    static RectTransform TextChip(Transform parent, string name, TMP_FontAsset font, float size, Color face, out TextMeshProUGUI text)
    {
        var rt = K.Rect(name, parent);
        var bg = K.Img("Back", rt, K.Graffiti ? K.Para : K.Cut, face);
        K.Fill(bg.rectTransform);
        text = K.Text("Text", rt, font, size, K.TextOn(face), TextAlignmentOptions.Center, true);
        K.Fill(text.rectTransform, 2f);
        var m = rt.gameObject.AddComponent<UiActiveMirror>();
        m.Source = text.gameObject;
        m.Targets = new[] { bg.gameObject };
        return rt;
    }

    /// <summary>전체 화면 묶음 + 뒤 어둡게 (UIPanel 루트 = 이 오브젝트)</summary>
    static RectTransform FullScreen(RectTransform root, string name)
    {
        var rt = K.Rect(name, root);
        K.Fill(rt);
        var d = K.Img("Backdrop", rt, null, K.DimColor);
        d.raycastTarget = true;
        K.Fill(d.rectTransform);
        return rt;
    }

    /// <summary>가운데 큰 창 — 모서리 잘린 판 + 그림자. 돌려주는 건 면 (내용은 앵커로 배치)</summary>
    static RectTransform Window(RectTransform parent, string name, float w, float h)
    {
        var rt = K.Rect(name, parent);
        K.Center(rt, 0f, 0f, w, h);
        var face = K.Sticker(rt, K.Paper, 0f, 14f);
        face.raycastTarget = true;
        return (RectTransform)face.transform;
    }

    /// <summary>내용 높이에 맞는 창 (세로 쌓기) — 그림자는 Shadow 효과</summary>
    static RectTransform AutoWindow(RectTransform parent, string name, float w, int pad)
    {
        var rt = K.Rect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, 300f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = K.Cut;
        img.type = Image.Type.Sliced;
        img.color = K.Paper;
        img.raycastTarget = true;
        DropShadow(rt.gameObject, 12f);
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(pad, pad, pad, pad + 4);
        v.spacing = 16f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    static void DropShadow(GameObject go, float d)
    {
        if (K.Graffiti) return;         // v7 — 그림자 없음
        var s = go.AddComponent<UnityEngine.UI.Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.35f);
        s.effectDistance = new Vector2(0f, -d);
    }

    /// <summary>✕ 닫기 — 창 오른쪽 위</summary>
    static Button CloseButton(RectTransform win, TMP_FontAsset font)
    {
        var b = K.Btn("Close", win, font, "×", K.Raised, 26f, out _);
        Abs((RectTransform)b.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-88f, -86f), new Vector2(-34f, -32f));
        return b;
    }

    /// <summary>열 제목 — ◆ + 큰 글자 (부모 맨 위). 돌려주는 건 글자</summary>
    static TextMeshProUGUI SectionTitle(RectTransform parent, TMP_FontAsset display, string text, Color diamond)
    {
        if (K.Graffiti)
        {
            // v7 — 이름표 (흰 마름모 자리는 빨강, 색 마름모는 그 색)
            Color face = diamond == K.Light ? K.Danger : diamond;
            var tag = K.Tag(parent, display, text, face, 24f, out var tt, "Title");
            tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
            tag.pivot = new Vector2(0f, 1f);
            tag.anchoredPosition = new Vector2(0f, -2f);
            var fit = tag.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return tt;
        }
        var rt = K.Rect("Title", parent);
        Abs(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(-240f, 0f));
        var dot = K.Img("Dot", rt, null, diamond);
        dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        dot.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        dot.rectTransform.sizeDelta = new Vector2(18f, 18f);
        dot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var t = K.Text("Text", rt, display, 30f, K.Ink);
        Abs(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(34f, 0f), Vector2.zero);
        t.text = text;
        return t;
    }

    static TextMeshProUGUI EmptyNote(RectTransform parent, TMP_FontAsset font)
    {
        var t = K.Text("Empty", parent, font, 20f, K.Dim, TextAlignmentOptions.Center);
        Abs(t.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -140f), new Vector2(0f, -60f));
        t.text = "—";
        t.gameObject.SetActive(false);
        return t;
    }

    /// <summary>육각 아이콘 — 흰 테두리 · 면 · 그림 (그림 Image를 out)</summary>
    static RectTransform HexIcon(Transform parent, string name, float size, Color face, out Image icon)
    {
        var rt = K.Rect(name, parent);
        rt.sizeDelta = new Vector2(size, size * 0.9f);
        var rim = K.Img("Rim", rt, K.Hex, K.Graffiti ? K.InkLine : K.Light);
        K.Fill(rim.rectTransform);
        var f = K.Img("Face", rim.transform, K.Hex, face);
        K.Fill(f.rectTransform, Mathf.Max(3f, size * 0.06f));
        icon = K.Img("Icon", f.transform, null, Color.white);
        K.Fill(icon.rectTransform, size * 0.14f);
        icon.preserveAspect = true;
        icon.enabled = false;
        return rt;
    }

    /// <summary>소울 스티커 (노랑 해골 그림, 없으면 노랑 육각)</summary>
    static RectTransform SoulSticker(Transform parent, Sprite soul, float size)
    {
        if (soul != null)
        {
            var img = K.Img("SoulIcon", parent, soul, Color.white);
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img.rectTransform;
        }
        var rt = HexIcon(parent, "SoulIcon", size, K.Gold, out _);
        return rt;
    }

    /// <summary>가격 글자 왼쪽 작은 노랑 육각 (글자 자식 — 글자와 같이 켜지고 꺼짐)</summary>
    static void SoulDot(RectTransform text, Sprite soul)
    {
        var dot = soul != null ? K.Img("Soul", text, soul, Color.white) : K.Img("Soul", text, K.Hex, K.Gold);
        dot.preserveAspect = true;
        dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        dot.rectTransform.pivot = new Vector2(1f, 0.5f);
        dot.rectTransform.anchoredPosition = new Vector2(-6f, 0f);
        dot.rectTransform.sizeDelta = new Vector2(26f, 24f);
    }

    /// <summary>작은 칩 (NEW · ▲)</summary>
    static RectTransform Chip(Transform parent, TMP_FontAsset font, string label, Color face)
    {
        var rt = K.Rect("Chip_" + label, parent);
        var bg = K.Img("Back", rt, K.Graffiti ? K.Para : K.Cut, face);
        K.Fill(bg.rectTransform);
        var t = K.Text("Text", rt, font, 14f, K.TextOn(face), TextAlignmentOptions.Center, true);
        K.Fill(t.rectTransform);
        t.text = label;
        return rt;
    }

    /// <summary>탭 버튼 — 고른 탭(누를 수 없음)은 강조색 면 (UiTabLook)</summary>
    static Button Tab(RectTransform parent, TMP_FontAsset font, string label, out TextMeshProUGUI text)
    {
        var b = K.Btn("Tab_" + label, parent, font, label, K.Raised, 21f, out text);
        K.Size(b, 190f, 52f);
        var cb = b.colors;
        cb.disabledColor = Color.white;     // 고른 탭이 흐려지지 않게 — 색은 UiTabLook이
        b.colors = cb;
        var look = b.gameObject.AddComponent<UiTabLook>();
        look.Button = b;
        look.Face = b.targetGraphic as Image;
        look.Label = text;
        return b;
    }

    /// <summary>격자 스크롤 — 돌려주는 건 Content (칸이 여기 쌓임)</summary>
    static RectTransform GridScroll(RectTransform parent, Vector2 cell, int columns, float spacing)
    {
        var content = K.Scroll("Grid", parent, out _, spacing);
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg != null) Object.DestroyImmediate(vlg);
        var g = content.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = cell;
        g.spacing = new Vector2(spacing, spacing);
        g.padding = new RectOffset(4, 12, 4, 14);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = columns;
        g.childAlignment = TextAnchor.UpperLeft;
        return content;
    }

    /// <summary>v7 큰 글자 — 기울임 + 검정 외곽선 (v2면 그대로)</summary>
    static void Loud(TMP_Text t)
    {
        if (!K.Graffiti || t == null) return;
        t.fontStyle = FontStyles.Italic;
        K.InkOutline(t);
    }

    static void Wrap(TMP_Text t)
    {
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
    }

    /// <summary>레이아웃에서 빼기 (배경 · 띠)</summary>
    static void Under(Component c)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    /// <summary>앵커 · 여백으로 놓기</summary>
    static void Abs(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
    }

    /// <summary>컴포넌트의 [SerializeField] private 칸에 값 넣기 (프리팹 만들 때만)</summary>
    static void Wire(Object target, string field, object value)
    {
        var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null)
        {
            Debug.LogWarning($"[창 v2] {target.GetType().Name}에 '{field}' 칸이 없음 — 건너뜀");
            return;
        }
        f.SetValue(target, value);
    }
}
