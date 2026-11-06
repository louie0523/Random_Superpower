using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 칠하기 도구 막대 (v7 그래피티) — 화면 아래 가운데. 타이틀 · UI 위 칠하기가 같이 씀.
///   1줄: 붓 3개 · 색 10개 · 크기
///   2줄: 보정 4개 (손떨림 보정 · 흘러내림 · 외곽선 · 반듯하게) · 되돌리기 · 다시 · 지우기 · 완료
/// </summary>
public static class PaintToolbarBuilder
{
    /// <param name="views">보는 곳 버튼 (벽 · 바닥) — 타이틀만</param>
    static bool _narrow;

    public static PaintToolbarView Build(Transform parent, TMP_FontAsset font, bool finish = true, bool views = false)
    {
        _narrow = views;
        var rt = K.Rect("PaintBar", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 28f);
        rt.sizeDelta = new Vector2(views ? 1860f : 1600f, 176f);
        var v = rt.gameObject.AddComponent<PaintToolbarView>();

        var face = K.Sticker(rt, K.Panel, 0f, 0f);
        face.raycastTarget = true;          // 막대 위에서는 안 칠해지게
        K.StripesOn(face, 0.02f);
        var col = face.gameObject.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(30, 30, 20, 18);
        col.spacing = 12f;
        col.childControlWidth = col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;

        // ── 1줄 ──
        var r1 = K.Row("Tools", face.transform, 60f, 12f);
        v.Spray = Tool(r1.transform, font, "Spray", "스프레이");
        v.Marker = Tool(r1.transform, font, "Marker", "마커");
        v.Eraser = Tool(r1.transform, font, "Eraser", "지우개");
        Gap(r1.transform, 18f);

        var colors = PaintBrush.Palette;
        v.Colors = colors;
        v.Swatches = new Button[colors.Length];
        v.SwatchRings = new GameObject[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var s = K.Rect("Color" + i, r1.transform);
            K.Size(s, 46f, 46f);
            var ring = K.Img("Ring", s, K.Plain, K.Gold);
            K.Fill(ring.rectTransform, -6f);
            ring.gameObject.SetActive(false);
            var f = K.Img("Face", s, K.Plain, colors[i]);
            K.Fill(f.rectTransform);
            f.raycastTarget = true;
            var b = s.gameObject.AddComponent<Button>();
            b.targetGraphic = f;
            K.Tint(b);
            v.Swatches[i] = b;
            v.SwatchRings[i] = ring.gameObject;
        }
        Gap(r1.transform, 18f);

        var sizeLabel = K.Text("SizeLabel", r1.transform, font, 22f, K.Ink, TextAlignmentOptions.MidlineRight, true);
        sizeLabel.text = "크기";
        K.Size(sizeLabel, 56f, 50f);
        v.Size = K.SliderBar("Size", r1.transform, 40f);
        K.Size(v.Size, K.PipBarWidth(8), 44f);

        // ── 2줄 ──
        var r2 = K.Row("Options", face.transform, 58f, 10f);
        v.Stabilize = Check(r2.transform, font, "Stabilize", "손떨림 보정");
        v.Drips = Check(r2.transform, font, "Drips", "흘러내림");
        v.Outline = Check(r2.transform, font, "Outline", "외곽선");
        v.Snap = Check(r2.transform, font, "Snap", "반듯하게");
        if (views)
        {
            Gap(r2.transform, 10f);
            v.Views = new[] { Tool(r2.transform, font, "ViewWall", "벽"), Tool(r2.transform, font, "ViewFloor", "바닥") };
            foreach (var o in v.Views) K.Size(o, 96f, 52f);

            // 시야 (카메라 FOV) — 넓히면 벽 위아래까지
            var zl = K.Text("ZoomLabel", r2.transform, font, 21f, K.Ink, TextAlignmentOptions.MidlineRight, true);
            zl.text = "시야";
            K.Size(zl, 52f, 50f);
            v.Zoom = K.SliderBar("Zoom", r2.transform, 40f);
            K.Size(v.Zoom, K.PipBarWidth(6), 44f);
        }
        var flex = K.Rect("Flex", r2.transform);
        K.Size(flex, -1f, -1f, 1f);
        v.Undo = Btn(r2.transform, font, "Undo", "되돌리기", K.Raised, 124f, out _);
        v.Redo = Btn(r2.transform, font, "Redo", "다시", K.Raised, 88f, out _);
        v.Clear = Btn(r2.transform, font, "Clear", "지우기", K.Danger, 150f, out var clearLabel);
        v.ClearLabel = clearLabel;
        v.Done = Btn(r2.transform, font, "Done", "완료", K.Yellow, 128f, out _);

        if (finish) K.Finish(rt);
        return v;
    }

    static UiOption Tool(Transform parent, TMP_FontAsset font, string name, string label)
    {
        var o = K.Option(name, parent, font, label, false, 56f, 24f);
        K.Size(o, 136f, 56f);
        return o;
    }

    static UiOption Check(Transform parent, TMP_FontAsset font, string name, string label)
    {
        var o = K.Option(name, parent, font, label, true, 52f, 21f);
        K.Size(o, _narrow ? 172f : 200f, 52f);
        return o;
    }

    static Button Btn(Transform parent, TMP_FontAsset font, string name, string label, Color face, float w, out TextMeshProUGUI text)
    {
        var b = K.Btn(name, parent, font, label, face, 22f, out text);
        K.Size(b, w, 52f);
        return b;
    }

    static void Gap(Transform parent, float w)
    {
        var g = K.Rect("Gap", parent);
        K.Size(g, w, 10f);
    }
}
