using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 무기 · 방어구 진열대 화면 (v7 그래피티) — parent를 꽉 채움.
///   위: 한 줄 (한정판 남은 시간 등) · 왼쪽: 카드 격자 (뒤 스프레이 = 등급 색) · 오른쪽: 자세히 (큰 그림 + 등급 색 스프레이 · 이름 · 능력치 · 구매)
/// 카드 · 판 안은 망점 없음
/// </summary>
public static class GearOfferBuilder
{
    public static GearOfferPage Build(RectTransform parent, TMP_FontAsset font)
    {
        var display = K.Display(font);
        var rt = K.Rect("GearOffers", parent);
        K.Fill(rt);
        var p = rt.gameObject.AddComponent<GearOfferPage>();
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 10f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        p.Header = K.Text("Header", rt, display, 24f, K.Gold, TextAlignmentOptions.MidlineLeft, true);
        p.Header.fontStyle = FontStyles.Italic;
        K.Size(p.Header, -1f, 34f);
        p.Header.gameObject.SetActive(false);

        var body = K.Rect("Body", rt);
        K.Size(body, -1f, -1f, -1f, 1f);
        var h = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 20f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        // ── 카드 격자 ──
        var list = K.Rect("List", body);
        K.Size(list, -1f, -1f, 1f, 1f);
        var hit = list.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var sr = list.gameObject.AddComponent<ScrollRect>();
        var vp = K.Rect("Viewport", list);
        K.Fill(vp);
        vp.gameObject.AddComponent<RectMask2D>();
        var content = K.Rect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var g = content.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(186f, 232f);
        g.spacing = new Vector2(14f, 16f);
        g.padding = new RectOffset(8, 8, 10, 16);
        g.constraint = GridLayoutGroup.Constraint.Flexible;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.viewport = vp;
        sr.content = content;
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        p.Grid = content;

        var empty = K.Text("Empty", list, font, 22f, K.Dim, TextAlignmentOptions.Center);
        K.Fill(empty.rectTransform, 30f);
        empty.gameObject.SetActive(false);
        p.Empty = empty;

        p.CardTemplate = Card(content, font);

        // ── 자세히 ──
        var det = K.Rect("Detail", body);
        K.Size(det, 440f, -1f, 0f);
        var face = K.Sticker(det, K.Panel2, 0f, 0f);
        face.raycastTarget = true;
        var dv = face.gameObject.AddComponent<VerticalLayoutGroup>();
        dv.padding = new RectOffset(22, 22, 22, 18);
        dv.spacing = 8f;
        dv.childControlWidth = dv.childControlHeight = true;
        dv.childForceExpandWidth = true;
        dv.childForceExpandHeight = false;

        var box = K.Rect("PreviewBox", face.transform);
        K.Size(box, -1f, 220f);
        var back = K.Img("Back", box, K.Plain, K.Field);
        K.Fill(back.rectTransform);
        var paint = K.Img("SprayFill", back.transform, K.SprayFill, K.Purple);
        K.Fill(paint.rectTransform, 6f);
        p.DetailPaint = paint;
        var icon = K.Img("Icon", box, null, Color.white);
        K.Fill(icon.rectTransform, 22f);
        icon.preserveAspect = true;
        p.DetailIcon = icon;

        p.DetailName = K.Text("Name", face.transform, display, 34f, K.Ink, TextAlignmentOptions.MidlineLeft);
        p.DetailName.fontStyle = FontStyles.Italic;
        K.InkOutline(p.DetailName, 0.22f);
        K.Size(p.DetailName, -1f, 46f);
        p.DetailSub = K.Text("Sub", face.transform, font, 19f, K.Dim, TextAlignmentOptions.MidlineLeft);
        K.Size(p.DetailSub, -1f, 28f);

        var scroll = K.Scroll("BodyScroll", face.transform, out var bsr, 4f);
        K.Size(scroll.parent.parent, -1f, -1f, -1f, 1f);
        p.DetailScroll = bsr;
        var text = K.Text("Body", scroll, font, 19f, K.Ink, TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = true;
        text.lineSpacing = 6f;
        p.DetailBody = text;

        p.Msg = K.Text("Msg", face.transform, font, 18f, K.Ok, TextAlignmentOptions.MidlineLeft);
        K.Size(p.Msg, -1f, 26f);
        p.BuyButton = K.Btn("Buy", face.transform, font, "◆ 0  구매", K.Yellow, 26f, out var bl);
        K.Size(p.BuyButton, -1f, 64f);
        p.BuyLabel = bl;

        K.Finish(rt);
        return p;
    }

    /// <summary>카드 — 뒤 스프레이(등급 색) · 그림 · 이름 · 가격 · 오른쪽 위 표시(품절 · ◆) · 고르면 노란 테두리</summary>
    static UiItemCard Card(RectTransform grid, TMP_FontAsset font)
    {
        var rt = K.Rect("Card", grid);
        var c = rt.gameObject.AddComponent<UiItemCard>();

        var sel = K.Img("Selected", rt, K.Plain, K.Gold);
        K.Fill(sel.rectTransform, -7f);
        sel.gameObject.SetActive(false);
        c.Selected = sel.gameObject;

        var face = K.Img("Face", rt, K.Slab, K.Panel2);
        K.Fill(face.rectTransform);
        face.raycastTarget = true;
        c.Face = face;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = face;
        K.Tint(b);
        c.Button = b;

        // 그림 칸 — 뒤 스프레이가 등급 색 (UiItemCard.Grade로 물듦)
        var art = K.Rect("Art", face.transform);
        art.anchorMin = new Vector2(0f, 0f);
        art.anchorMax = new Vector2(1f, 1f);
        art.offsetMin = new Vector2(10f, 66f);
        art.offsetMax = new Vector2(-10f, -10f);
        var spray = K.Img("Spray", art, K.SprayFill, K.Purple);
        K.Fill(spray.rectTransform, -4f);
        c.Grade = spray;
        var icon = K.Img("Icon", art, null, Color.white);
        K.Fill(icon.rectTransform, 14f);
        icon.preserveAspect = true;
        c.Icon = icon;

        var t = K.Text("Title", face.transform, K.Display(font), 20f, K.Ink, TextAlignmentOptions.Center);
        var tr = t.rectTransform;
        tr.anchorMin = new Vector2(0f, 0f);
        tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0.5f, 0f);
        tr.offsetMin = new Vector2(8f, 30f);
        tr.offsetMax = new Vector2(-8f, 64f);
        t.fontStyle = FontStyles.Italic;
        t.text = "이름";
        c.Title = t;

        var s = K.Text("Sub", face.transform, font, 18f, K.Gold, TextAlignmentOptions.Center, true);
        var sr = s.rectTransform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 0f);
        sr.pivot = new Vector2(0.5f, 0f);
        sr.offsetMin = new Vector2(8f, 6f);
        sr.offsetMax = new Vector2(-8f, 32f);
        c.Sub = s;

        var badge = K.Tag(face.transform, font, "품절", K.Danger, 16f, out var bt, "Badge");
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 1f);
        badge.anchoredPosition = new Vector2(8f, 10f);
        var le = badge.GetComponent<LayoutElement>();
        if (le == null) le = badge.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var fit = badge.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        badge.localRotation = K.Tilt(6f);
        badge.gameObject.SetActive(false);
        c.Badge = badge.gameObject;
        c.BadgeText = bt;
        return c;
    }
}
