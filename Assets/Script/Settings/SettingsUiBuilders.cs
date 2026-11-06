using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 설정 창 · ESC 메뉴 기본 모양 — 프리팹을 안 넣었을 때 실행 중에 만들고, 프리팹 만들기 메뉴도 같은 걸 씀.
/// 설정 줄은 SettingsRegistry 목록대로 저절로 만들어짐.
/// </summary>
public static class SettingsUiBuilders
{
    // ══════════════════ 설정 창 ══════════════════

    /// <summary>
    /// SettingsView (Canvas 31000)
    ///  └ Root ─ Backdrop · Window
    ///     ├ Top: 제목 · 닫기
    ///     └ Body: Tabs(왼쪽 세로) · Pages(탭마다 스크롤 — SettingRow들)
    /// </summary>
    public static SettingsView Settings(GameObject host, TMP_FontAsset font)
    {
        if (K.Graffiti) return SettingsV7(host, font);
        var root = K.MakeScreen(host, 31000, K.DimColor);
        var v = host.AddComponent<SettingsView>();
        v.Root = root.gameObject;

        K.Window("Window", root, 1500f, 940f, out var c, 26, 16f);

        // 위: 제목 · 닫기
        var top = K.Row("Top", c, 60f, 12f);
        var accHold = K.Rect("AccentHolder", top.transform);        // 제목 앞 민트 마름모
        K.Size(accHold, 22f);
        var acc = K.Img("Accent", accHold, null, K.Mint);
        K.Center(acc.rectTransform, 0f, 0f, 18f, 18f);
        acc.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        v.Title = K.Text("Title", top.transform, K.Display(font), 42f, K.Ink, TextAlignmentOptions.MidlineLeft, false);
        K.Size(v.Title, -1f, -1f, 1f);
        v.Title.text = "설정";
        SetLocText.Add(v.Title, "set.ui.title");
        v.CloseButton = K.Btn("Close", top.transform, font, "닫기 (Esc)", K.Yellow, 22f, out var closeLabel);
        SetLocText.Add(closeLabel, "set.ui.close");
        K.Size(v.CloseButton, 180f);

        // 몸통: 탭 · 페이지
        var body = K.Rect("Body", c);
        K.Size(body, -1f, -1f, -1f, 1f);
        var bh = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bh.spacing = 22f;
        bh.childControlWidth = bh.childControlHeight = true;
        bh.childForceExpandWidth = false;
        bh.childForceExpandHeight = true;

        var tabs = K.Group("Tabs", body, 10f);
        // flexible 0 — 안 그러면 세로 그룹(가로 꽉 채우기)이 남는 폭을 페이지와 반씩 나눠 가져서 설정 칸이 잘림
        K.Size(tabs, 250f, -1f, 0f);
        tabs.padding = new RectOffset(0, 6, 4, 10);

        var pages = K.Rect("Pages", body);
        K.Size(pages, -1f, -1f, 1f);

        foreach (var (id, label) in SettingsRegistry.Tabs)
        {
            var opt = K.Option("Tab_" + id, tabs.transform, font, label, false, 56f, 22f);

            var page = K.Rect("Page_" + id, pages);
            K.Fill(page);
            var content = K.Scroll("Scroll", page, out var sr, 10f);
            K.Fill((RectTransform)sr.transform);

            if (id == "crosshair") v.Crosshair = CrosshairEditor(content, font);

            foreach (var d in SettingsRegistry.All)
                if (d.Tab == id) Row(content, font, d);

            if (id == "controls")
            {
                var n = K.Note(content, font, "보이스 키는 '채팅 · 보이스' 탭", 18f);
                SetLocText.Add(n, "set.ui.voice_keys_note");
                K.Size(n, -1f, 30f);
            }

            v.Tabs.Add(new SettingsTab { Id = id, Button = opt, Page = page.gameObject });
            page.gameObject.SetActive(v.Tabs.Count == 1);
        }
        return v;
    }

    /// <summary>
    /// v7 설정 창 (시안 SetV7) — 위: 기울어진 큰 제목 + 스프레이 탭 줄 + 닫기 / 아래: 탭마다 스크롤 (줄 = 기울어진 띠)
    /// SettingsView 칸은 v2와 같음 (Title · Tabs · CloseButton · Crosshair)
    /// </summary>
    static SettingsView SettingsV7(GameObject host, TMP_FontAsset font)
    {
        var root = K.MakeScreen(host, 31000, K.DimColor);
        var v = host.AddComponent<SettingsView>();
        v.Root = root.gameObject;

        K.Window("Window", root, 1740f, 980f, out var c, 26, 18f);

        // 위: 제목 · 탭 · 닫기
        var top = K.Row("Top", c, 84f, 26f);
        top.childAlignment = TextAnchor.MiddleLeft;
        v.Title = K.Header(top.transform, font, "설정", 46f);
        SetLocText.Add(v.Title, "set.ui.title");

        var tabs = K.Rect("Tabs", top.transform);
        K.Size(tabs, -1f, -1f, 1f);
        var th = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 18f;
        th.childAlignment = TextAnchor.MiddleLeft;
        th.childControlWidth = th.childControlHeight = true;
        th.childForceExpandWidth = false;
        th.childForceExpandHeight = false;

        v.CloseButton = K.Btn("Close", top.transform, font, "닫기 (Esc)", K.Raised, 22f, out var closeLabel);
        SetLocText.Add(closeLabel, "set.ui.close");
        K.Size(v.CloseButton, 190f, 56f);

        // 몸통: 페이지 (탭마다)
        var pages = K.Rect("Pages", c);
        K.Size(pages, -1f, -1f, -1f, 1f);

        foreach (var (id, label) in SettingsRegistry.Tabs)
        {
            var opt = SprayTab(tabs, font, label);
            opt.name = "Tab_" + id;

            var page = K.Rect("Page_" + id, pages);
            K.Fill(page);
            var content = K.Scroll("Scroll", page, out var sr, 8f);
            K.Fill((RectTransform)sr.transform);

            if (id == "crosshair") v.Crosshair = CrosshairEditor(content, font);

            foreach (var d in SettingsRegistry.All)
                if (d.Tab == id) Row(content, font, d);

            if (id == "controls")
            {
                var n = K.Note(content, font, "보이스 키는 '채팅 · 보이스' 탭", 18f);
                SetLocText.Add(n, "set.ui.voice_keys_note");
                K.Size(n, -1f, 30f);
            }

            v.Tabs.Add(new SettingsTab { Id = id, Button = opt, Page = page.gameObject });
            page.gameObject.SetActive(v.Tabs.Count == 1);
        }
        Thinner(root);
        return v;
    }

    /// <summary>
    /// v7 외곽선 굵기 맞춤 (Pixels Per Unit Multiplier — 클수록 선이 가늘어짐)
    ///   체크칸 · 체크 = 6 / 닫기 버튼 = 1.5 / 나머지 판 · 띠 (Back · Face …) = 2
    /// </summary>
    static void Thinner(Transform root)
    {
        K.Finish(root);
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            var sp = img.sprite;
            if (sp == null || img.type != Image.Type.Sliced) continue;
            if (sp != K.Para && sp != K.Slab && sp != K.Plain) continue;
            if (img.name == "CheckBox" || img.name == "Check") img.pixelsPerUnitMultiplier = 6f;
            else if (img.name == "Face" && img.transform.parent != null && img.transform.parent.name == "Close") img.pixelsPerUnitMultiplier = 1.5f;
            else img.pixelsPerUnitMultiplier = 2f;
        }
    }

    /// <summary>스프레이 탭 — 고르면 핑크 스프레이가 글자 뒤에 깔림 (글자 폭만큼)</summary>
    static UiOption SprayTab(Transform parent, TMP_FontAsset font, string label)
    {
        var rt = K.Rect("Tab", parent);
        var hl = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(14, 14, 8, 8);
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;

        var face = K.SprayUnder(rt, K.Pink, "Face");
        var fr = face.rectTransform;
        fr.anchorMin = new Vector2(0f, 0.05f);
        fr.anchorMax = new Vector2(1f, 0.95f);
        fr.offsetMin = new Vector2(-10f, 0f);
        fr.offsetMax = new Vector2(10f, 0f);
        face.raycastTarget = true;

        var t = K.Text("Label", rt, K.Display(font), 26f, K.Ink, TextAlignmentOptions.Center);
        t.fontStyle = FontStyles.Italic;
        t.overflowMode = TextOverflowModes.Overflow;
        t.text = label;
        K.InkOutline(t, 0.22f);

        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = face;
        K.Tint(b);

        var opt = rt.gameObject.AddComponent<UiOption>();
        opt.Button = b;
        opt.Face = face;
        opt.Label = t;
        opt.OnFace = K.Pink;
        opt.OffFace = new Color(K.Pink.r, K.Pink.g, K.Pink.b, 0f);
        opt.AutoTextColor = false;
        opt.FixedTextColors = true;
        opt.OnText = K.Ink;
        opt.OffText = K.Dim;
        opt.SetOn(false);
        return opt;
    }

    /// <summary>v7 고르기 칸 — 종이 띠 안에 ◀ 값 ▶</summary>
    static void ChoiceV7(SettingRow row, RectTransform right, TMP_FontAsset font)
    {
        var bar = K.Rect("Choice", right);
        K.Size(bar, -1f, -1f, 1f);
        var face = K.ParaFace(bar, K.PaperFace);
        var h = face.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(22, 22, 6, 8);
        h.spacing = 6f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        row.Prev = Arrow(face.transform, true);
        row.Value = K.Text("Text", face.transform, font, 20f, K.InkLine, TextAlignmentOptions.Center, true);
        K.Size(row.Value, -1f, -1f, 1f);
        row.Next = Arrow(face.transform, false);
    }

    static Button Arrow(Transform parent, bool left)
    {
        var rt = K.Rect(left ? "Prev" : "Next", parent);
        K.Size(rt, 36f);
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var tri = K.Img("Arrow", rt, K.Tri, K.InkLine);
        K.Center(tri.rectTransform, 0f, 0f, 18f, 18f);
        if (left) tri.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = tri;
        K.Tint(b);
        return b;
    }

    /// <summary>설정 한 줄 — 왼쪽 이름(+설명) · 오른쪽 조작</summary>
    public static SettingRow Row(Transform parent, TMP_FontAsset font, SettingDef d)
    {
        bool note = !string.IsNullOrEmpty(d.Note);
        var rt = K.Rect("Row_" + d.Key, parent);
        K.Size(rt, -1f, note ? 80f : 62f, -1f, 0f);

        bool v7 = K.Graffiti;
        var bg = v7 ? K.Img("Back", rt, K.Para, K.Soft) : K.Img("Back", rt, K.Cut, K.Soft);
        K.Fill(bg.rectTransform);
        var hl = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = v7 ? new RectOffset(34, 30, 8, 8) : new RectOffset(18, 12, 7, 7);
        hl.spacing = 14f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;

        var row = rt.gameObject.AddComponent<SettingRow>();
        row.Key = d.Key;

        var left = K.Group("Left", bg.transform, 0f);
        K.Size(left, 200f, -1f, 1f);
        left.childAlignment = TextAnchor.MiddleLeft;
        row.Label = K.Text("Label", left.transform, font, 22f, K.Ink, TextAlignmentOptions.MidlineLeft, true);
        K.Size(row.Label, -1f, 32f);
        row.Label.text = d.Label;
        if (note)
        {
            row.Note = K.Text("Note", left.transform, font, 17f, K.Muted);
            K.Size(row.Note, -1f, 24f);
            row.Note.text = d.Note;
        }

        var right = K.Rect("Control", bg.transform);
        K.Size(right, v7 ? 480f : 440f, -1f, 0f);     // 조작 칸 폭 고정 — 남는 폭은 이름 쪽이 가져감
        var rh = right.gameObject.AddComponent<HorizontalLayoutGroup>();
        rh.spacing = 8f;
        rh.childAlignment = TextAnchor.MiddleLeft;
        rh.childControlWidth = rh.childControlHeight = true;
        rh.childForceExpandWidth = false;
        rh.childForceExpandHeight = true;
        rh.padding = new RectOffset(0, 0, 0, 4);

        switch (d.Kind)
        {
            case SettingKind.Toggle:
                row.Toggle = K.Option("Toggle", right, font, "꺼짐", true, 46f, 20f);
                K.Size(row.Toggle, -1f, -1f, 1f);
                break;

            case SettingKind.Slider:
                row.Slider = K.SliderBar("Slider", right, 30f);
                if (v7) K.Size(row.Slider, K.PipBarWidth(11), -1f, 0f);    // 칸 11개 딱 (끝 칸 안 잘리게)
                else K.Size(row.Slider, -1f, -1f, 1f);
                row.Value = K.Text("Value", right, font, 20f, K.Ink, TextAlignmentOptions.MidlineRight, true);
                K.Size(row.Value, 90f);
                break;

            case SettingKind.Choice when v7:
                ChoiceV7(row, right, font);
                break;

            case SettingKind.Choice:
                row.Prev = K.Btn("Prev", right, font, "<", Color.white, 22f, out _);
                K.Size(row.Prev, 52f);
                var box = K.Rect("Value", right);
                K.Size(box, -1f, -1f, 1f);
                var face = K.Sticker(box, Color.white, 3f, 0f);
                row.Value = K.Text("Text", face.transform, font, 20f, K.Ink, TextAlignmentOptions.Center, true);
                K.Fill(row.Value.rectTransform, 4f);
                row.Next = K.Btn("Next", right, font, ">", Color.white, 22f, out _);
                K.Size(row.Next, 52f);
                break;

            case SettingKind.Key:
                row.KeyButton = K.Btn("KeyButton", right, font, "T", v7 ? K.PaperFace : Color.white, 22f, out var kl);   // v7 = 종이 키 캡
                K.Size(row.KeyButton, -1f, -1f, 1f);
                row.KeyLabel = kl;
                break;

            case SettingKind.Action:
                row.KeyButton = K.Btn("Button", right, font, d.ButtonText ?? "실행", K.Yellow, 20f, out var al);
                K.Size(row.KeyButton, -1f, -1f, 1f);
                row.KeyLabel = al;
                break;
        }
        return row;
    }

    /// <summary>조준점 탭 위쪽 — 무기 종류 · 미리보기 · 코드 버튼</summary>
    static CrosshairEditorView CrosshairEditor(Transform parent, TMP_FontAsset font)
    {
        var g = K.Group("CrosshairEditor", parent, 10f);
        g.padding = new RectOffset(0, 0, 0, 10);
        var v = g.gameObject.AddComponent<CrosshairEditorView>();

        var kinds = K.Row("Kinds", g.transform, 58f, 10f);
        kinds.childForceExpandWidth = true;
        v.GunOption = K.Option("Gun", kinds.transform, font, "총", false, 52f);
        SetLocText.Add(v.GunOption.Label, "set.xh.kind.gun");
        K.Size(v.GunOption, -1f, -1f, 1f);
        v.BowOption = K.Option("Bow", kinds.transform, font, "활 · 지팡이", false, 52f);
        SetLocText.Add(v.BowOption.Label, "set.xh.kind.bow");
        K.Size(v.BowOption, -1f, -1f, 1f);
        v.MeleeOption = K.Option("Melee", kinds.transform, font, "근접", false, 52f);
        SetLocText.Add(v.MeleeOption.Label, "set.xh.kind.melee");
        K.Size(v.MeleeOption, -1f, -1f, 1f);

        v.SharedNote = K.Note(g.transform, font, "[모든 무기에 같은 조준점]이 켜져 있어서 총 조준점을 모두 씀", 18f);
        SetLocText.Add(v.SharedNote, "set.xh.shared_note");
        K.Size(v.SharedNote, -1f, 26f);

        var pv = K.Row("PreviewRow", g.transform, 310f, 16f);

        var box = K.Rect("Preview", pv.transform);
        K.Size(box, 300f);
        v.PreviewBack = K.Sticker(box, new Color(0.36f, 0.42f, 0.36f), 0f, 8f);
        v.PreviewBack.gameObject.AddComponent<RectMask2D>();
        var cg = K.Rect("Crosshair", v.PreviewBack.transform);
        cg.anchorMin = cg.anchorMax = cg.pivot = new Vector2(0.5f, 0.5f);
        cg.sizeDelta = Vector2.zero;
        v.Preview = cg.gameObject.AddComponent<CrosshairGraphic>();

        var btns = K.Group("Buttons", pv.transform, 8f);
        K.Size(btns, -1f, -1f, 1f);
        v.BackButton = K.Btn("Back", btns.transform, font, "미리보기 배경 바꾸기", Color.white, 20f, out var lBack);
        SetLocText.Add(lBack, "set.xh.btn.back");
        K.Size(v.BackButton, -1f, 42f);
        v.AnimateOption = K.Option("Animate", btns.transform, font, "벌어짐 움직임 보기", true, 42f, 20f);
        SetLocText.Add(v.AnimateOption.Label, "set.xh.btn.animate");
        v.CopyButton = K.Btn("Copy", btns.transform, font, "코드 복사", K.Yellow, 20f, out var lCopy);
        SetLocText.Add(lCopy, "set.xh.btn.copy");
        K.Size(v.CopyButton, -1f, 42f);
        v.PasteButton = K.Btn("Paste", btns.transform, font, "코드 붙여넣기", Color.white, 20f, out var lPaste);
        SetLocText.Add(lPaste, "set.xh.btn.paste");
        K.Size(v.PasteButton, -1f, 42f);
        v.ResetButton = K.Btn("Reset", btns.transform, font, "이 무기 기본값으로", Color.white, 20f, out var lReset);
        SetLocText.Add(lReset, "set.xh.btn.reset");
        K.Size(v.ResetButton, -1f, 42f);
        v.CopyToAllButton = K.Btn("CopyAll", btns.transform, font, "다른 무기에도 똑같이", Color.white, 20f, out var lAll);
        SetLocText.Add(lAll, "set.xh.btn.copy_all");
        K.Size(v.CopyToAllButton, -1f, 42f);

        v.Status = K.Text("Status", g.transform, font, 19f, K.Muted, TextAlignmentOptions.MidlineLeft);
        K.Size(v.Status, -1f, 26f);
        v.Status.text = "";
        return v;
    }

    // ══════════════════ ESC 메뉴 ══════════════════

    /// <summary>EscMenuView (Canvas 30900) └ Root ─ Backdrop · Window: 제목 · 계속하기 · 설정 · 방 나가기 · 게임 종료</summary>
    public static EscMenuView EscMenu(GameObject host, TMP_FontAsset font)
    {
        var root = K.MakeScreen(host, 30900, K.DimColor);
        var v = host.AddComponent<EscMenuView>();
        v.Root = root.gameObject;

        K.Window("Window", root, 480f, 626f, out var c, 30, 16f);
        v.Title = K.Header(c, font, "메뉴");
        SetLocText.Add(v.Title, "set.esc.title");

        v.ContinueButton = Big(c, font, "Continue", "계속하기", K.Yellow, K.Ink, "set.esc.continue");
        v.SettingsButton = Big(c, font, "Settings", "설정", Color.white, K.Ink, "set.esc.settings");
        v.LeaveButton = Big(c, font, "Leave", "방 나가기", K.Red, Color.white, "set.esc.leave");
        v.TitleButton = Big(c, font, "ToTitle", "타이틀로", K.Mint, K.Ink, "set.esc.to_title");
        v.QuitButton = Big(c, font, "Quit", "게임 종료", Color.white, K.Ink, "set.esc.quit");
        K.Finish(host.transform);
        return v;
    }

    static Button Big(Transform parent, TMP_FontAsset font, string name, string label, Color face, Color text, string locKey)
    {
        var b = K.Btn(name, parent, font, label, face, 26f, out var t, text);
        SetLocText.Add(t, locKey);
        K.Size(b, -1f, 70f);
        return b;
    }
}
