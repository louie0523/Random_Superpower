using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조준점 그리기 — 이 RectTransform 가운데에 그림 (게임 화면 · 설정 미리보기 공용).
/// Draw(모양, 움직임 벌어짐 px, 쏘기 벌어짐 px)를 매 프레임 부르면 됨.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CrosshairGraphic : MonoBehaviour
{
    class Bar
    {
        public Image Out, Fill;
    }

    readonly Bar[] _inner = new Bar[4];
    readonly Bar[] _outer = new Bar[4];
    Bar _dot;
    Image _ringOut, _ring;
    string _ringKey;
    bool _built;

    void Build()
    {
        if (_built) return;
        _built = true;

        _ringOut = MakeImage("RingOutline");
        _ring = MakeImage("Ring");
        for (int i = 0; i < 4; i++) _outer[i] = MakeBar("Outer" + i);
        for (int i = 0; i < 4; i++) _inner[i] = MakeBar("Inner" + i);
        _dot = MakeBar("Dot");
    }

    Bar MakeBar(string name) => new Bar { Out = MakeImage(name + "_Outline"), Fill = MakeImage(name) };

    Image MakeImage(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    /// <summary>move · fire = 벌어지는 거리 (픽셀, 1080p 기준)</summary>
    public void Draw(CrosshairStyle s, float move, float fire)
    {
        Build();
        if (s == null) { Hide(); return; }

        float k = Mathf.Max(0f, s.ErrorScale);
        float inSp = ((s.InnerMove ? move : 0f) + (s.InnerFire ? fire : 0f)) * k;
        float outSp = ((s.OuterMove ? move : 0f) + (s.OuterFire ? fire : 0f)) * k;
        float ringSp = ((s.CircleMove ? move : 0f) + (s.CircleFire ? fire : 0f)) * k;

        Lines(_inner, s, s.Inner, s.InnerLength, s.InnerThickness, s.InnerOffset + inSp, s.InnerOpacity);
        Lines(_outer, s, s.Outer, s.OuterLength, s.OuterThickness, s.OuterOffset + outSp, s.OuterOpacity);

        // 가운데 점
        bool dot = s.Dot && s.DotSize > 0;
        Place(_dot, s, dot, Vector2.zero, new Vector2(s.DotSize, s.DotSize), s.DotOpacity);

        // 원
        DrawRing(s, s.Circle, s.CircleRadius + ringSp);
    }

    public void Hide()
    {
        Build();
        foreach (var b in _inner) Show(b, false);
        foreach (var b in _outer) Show(b, false);
        Show(_dot, false);
        _ring.enabled = _ringOut.enabled = false;
    }

    void Lines(Bar[] bars, CrosshairStyle s, bool on, int len, int thick, float offset, float alpha)
    {
        on &= len > 0 && thick > 0;
        float o = Mathf.Round(offset);
        float c = o + len * 0.5f;
        Place(bars[0], s, on, new Vector2(c, 0f), new Vector2(len, thick), alpha);                 // 오른쪽
        Place(bars[1], s, on, new Vector2(-c, 0f), new Vector2(len, thick), alpha);                // 왼쪽
        Place(bars[2], s, on && !s.TStyle, new Vector2(0f, c), new Vector2(thick, len), alpha);    // 위
        Place(bars[3], s, on, new Vector2(0f, -c), new Vector2(thick, len), alpha);                // 아래
    }

    void Place(Bar b, CrosshairStyle s, bool on, Vector2 pos, Vector2 size, float alpha)
    {
        if (!on) { Show(b, false); return; }

        var f = b.Fill.rectTransform;
        f.anchoredPosition = pos;
        f.sizeDelta = size;
        var col = s.Color;
        col.a = Mathf.Clamp01(alpha);
        b.Fill.color = col;
        b.Fill.enabled = true;

        bool outline = s.Outline && s.OutlineThickness > 0 && s.OutlineOpacity > 0f;
        b.Out.enabled = outline;
        if (outline)
        {
            var r = b.Out.rectTransform;
            r.anchoredPosition = pos;
            r.sizeDelta = size + Vector2.one * (s.OutlineThickness * 2);
            b.Out.color = new Color(0f, 0f, 0f, Mathf.Clamp01(s.OutlineOpacity) * Mathf.Clamp01(alpha > 0f ? 1f : 0f));
        }
    }

    static void Show(Bar b, bool on)
    {
        b.Fill.enabled = on;
        b.Out.enabled = on;
    }

    void DrawRing(CrosshairStyle s, bool on, float radius)
    {
        on &= s.CircleThickness > 0 && radius > 1f;
        if (!on)
        {
            _ring.enabled = _ringOut.enabled = false;
            return;
        }

        int r = Mathf.RoundToInt(radius);
        int t = s.CircleThickness;
        int ot = s.Outline ? s.OutlineThickness : 0;
        string key = $"{r}/{t}/{ot}";
        if (key != _ringKey)
        {
            _ringKey = key;
            SetSprite(_ring, RingSprite(r, t, 0));
            SetSprite(_ringOut, ot > 0 ? RingSprite(r, t, ot) : null);
        }

        var col = s.Color;
        col.a = Mathf.Clamp01(s.CircleOpacity);
        _ring.color = col;
        _ring.enabled = true;
        _ring.rectTransform.anchoredPosition = Vector2.zero;
        _ring.SetNativeSize();

        bool outline = ot > 0 && s.OutlineOpacity > 0f;
        _ringOut.enabled = outline;
        if (outline)
        {
            _ringOut.color = new Color(0f, 0f, 0f, Mathf.Clamp01(s.OutlineOpacity));
            _ringOut.rectTransform.anchoredPosition = Vector2.zero;
            _ringOut.SetNativeSize();
        }
    }

    static void SetSprite(Image img, Sprite sp)
    {
        if (img.sprite != null && img.sprite != sp)
        {
            var old = img.sprite;
            img.sprite = null;
            if (old.texture != null) Destroy(old.texture);
            Destroy(old);
        }
        img.sprite = sp;
    }

    /// <summary>고리 그림 — 반지름 r · 두께 t (+ 바깥쪽 · 안쪽으로 extra만큼 두꺼움 = 테두리용)</summary>
    static Sprite RingSprite(int r, int t, int extra)
    {
        float inner = r - t * 0.5f - extra;
        float outer = r + t * 0.5f + extra;
        int size = Mathf.CeilToInt(outer * 2f) + 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);   // 가장자리 부드럽게
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    void OnDestroy()
    {
        if (_ring != null) SetSprite(_ring, null);
        if (_ringOut != null) SetSprite(_ringOut, null);
    }
}
