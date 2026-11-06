using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 칠하기 엔진 (GPU) — 바탕(기본 그림) 위에 칠 그림 한 장. 타이틀 벽 · 에디터 그림 창 · UI 위 칠하기가 같이 씀.
///   Display = 화면에 보이는 그림 (바탕 + 칠 + 그리는 중인 획) → 재질 · RawImage에 그대로 넣으면 됨
///   한 획: DrawStroke(매 프레임, 붓 자국 전부) → Commit(손 뗄 때)
///   되돌리기 · 다시 · 지우기 · PNG 읽고 쓰기
/// 좌표 = 그림 픽셀 (왼쪽 아래 0,0). 위아래가 뒤집혀 나오면 FlipY = true
/// </summary>
public sealed class PaintCanvas : IDisposable
{
    public static bool FlipY = false;

    public int Width { get; }
    public int Height { get; }
    public RenderTexture Display => _display;
    public bool Drawing => _stroking;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    Texture _base;
    Color _baseColor;
    readonly RenderTexture _paint, _preview, _mask, _display;
    readonly List<RenderTexture> _undo = new(), _redo = new();
    readonly int _maxUndo;
    bool _stroking;

    static Material _dabMat, _compMat, _showMat;
    static Texture2D _noise;
    static readonly int IdMask = Shader.PropertyToID("_Mask"), IdNoise = Shader.PropertyToID("_Noise"),
        IdColor = Shader.PropertyToID("_Color"), IdInk = Shader.PropertyToID("_Ink"), IdNoiseScale = Shader.PropertyToID("_NoiseScale"),
        IdSpray = Shader.PropertyToID("_Spray"), IdErase = Shader.PropertyToID("_Erase"), IdOutline = Shader.PropertyToID("_OutlinePx"),
        IdBaseTex = Shader.PropertyToID("_BaseTex"), IdBaseColor = Shader.PropertyToID("_BaseColor");

    /// <param name="baseTex">바탕 그림 (null = baseColor 한 색)</param>
    /// <param name="baseColor">바탕 색 (그림에 곱함 · 투명한 그림판이면 Color.clear)</param>
    /// <param name="maxUndo">되돌리기 칸 수 (한 칸 = 그림 한 장 메모리)</param>
    public PaintCanvas(int width, int height, Texture baseTex, Color baseColor, int maxUndo = 16, bool mips = false)
    {
        Width = Mathf.Clamp(width, 8, 8192);
        Height = Mathf.Clamp(height, 8, 8192);
        _base = baseTex;
        _baseColor = baseColor;
        _maxUndo = Mathf.Max(0, maxUndo);
        _paint = NewRT(false, false, "Paint");
        _preview = NewRT(false, false, "PaintPreview");
        _mask = NewRT(true, false, "PaintMask");
        _display = NewRT(false, mips, "PaintDisplay");
        Clear(_paint);
        Refresh();
    }

    RenderTexture NewRT(bool linear, bool mips, string name)
    {
        var rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32,
                                   linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = mips,
            autoGenerateMips = mips,
            hideFlags = HideFlags.DontSave,
        };
        rt.Create();
        return rt;
    }

    // ══════════════════ 바탕 ══════════════════

    public void SetBase(Texture baseTex, Color baseColor)
    {
        _base = baseTex;
        _baseColor = baseColor;
        Refresh();
    }

    /// <summary>보이는 그림 다시 (바탕 + 칠)</summary>
    public void Refresh() => Show(_stroking ? _preview : _paint);

    // ══════════════════ 획 ══════════════════

    /// <summary>
    /// 그리는 중인 획 전체를 다시 그림 (매 프레임). outlinePx = 외곽선 두께(픽셀, 0 = 없음)
    /// </summary>
    public void DrawStroke(PaintBrush brush, List<PaintDab> dabs, float outlinePx)
    {
        if (!Ready()) return;
        if (!_stroking)
        {
            PushUndo();
            _stroking = true;
        }

        Clear(_mask);
        DrawDabs(_mask, dabs);

        var m = _compMat;
        m.SetTexture(IdMask, _mask);
        m.SetTexture(IdNoise, Noise);
        var c = brush.Color;
        c.a = brush.Opacity;
        m.SetColor(IdColor, c);
        m.SetColor(IdInk, PaintBrush.Ink);
        m.SetVector(IdNoiseScale, new Vector4(Width / 256f, Height / 256f, 0f, 0f));
        m.SetFloat(IdSpray, brush.IsSpray && !brush.Outline ? 1f : 0f);     // 외곽선이면 알갱이 없이 (테두리가 깔끔하게 · 카툰 스티커)
        m.SetFloat(IdErase, brush.IsEraser ? 1f : 0f);
        m.SetFloat(IdOutline, brush.Outline && !brush.IsEraser ? Mathf.Max(0f, outlinePx) : 0f);
        Graphics.Blit(_paint, _preview, m);
        Show(_preview);
    }

    /// <summary>손 뗌 — 획을 칠 그림에 굳힘</summary>
    public void Commit()
    {
        if (!_stroking) return;
        Graphics.Blit(_preview, _paint);
        _stroking = false;
        ClearList(_redo);
        Show(_paint);
    }

    /// <summary>그리던 획 버림</summary>
    public void Cancel()
    {
        if (!_stroking) return;
        _stroking = false;
        if (_undo.Count > 0)
        {
            Release(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
        }
        Show(_paint);
    }

    // ══════════════════ 되돌리기 ══════════════════

    void PushUndo()
    {
        if (_maxUndo <= 0) return;
        _undo.Add(Copy(_paint));
        while (_undo.Count > _maxUndo)
        {
            Release(_undo[0]);
            _undo.RemoveAt(0);
        }
    }

    public bool Undo()
    {
        if (_stroking || _undo.Count == 0) return false;
        _redo.Add(Copy(_paint));
        var last = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        Graphics.Blit(last, _paint);
        Release(last);
        Show(_paint);
        return true;
    }

    public bool Redo()
    {
        if (_stroking || _redo.Count == 0) return false;
        _undo.Add(Copy(_paint));
        var last = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        Graphics.Blit(last, _paint);
        Release(last);
        Show(_paint);
        return true;
    }

    /// <summary>칠 그림 전부 지움 (되돌리기 됨) — 바탕은 그대로</summary>
    public void ClearPaint()
    {
        if (_stroking) Cancel();
        PushUndo();
        ClearList(_redo);
        Clear(_paint);
        Show(_paint);
    }

    // ══════════════════ 읽기 · 쓰기 ══════════════════

    /// <summary>칠 그림을 이 그림으로 (저장해 둔 PNG 등 · 크기가 달라도 늘려 맞춤)</summary>
    public void LoadPaint(Texture tex)
    {
        if (tex == null) return;
        Graphics.Blit(tex, _paint);
        ClearList(_undo);
        ClearList(_redo);
        Show(_paint);
    }

    /// <summary>칠 그림만 (투명 바탕)</summary>
    public Texture2D ReadPaint() => Read(_paint);

    /// <summary>보이는 그림 (바탕 + 칠) — 기본 그림에 굽거나 UI 그림으로 저장할 때</summary>
    public Texture2D ReadDisplay()
    {
        Show(_paint);
        return Read(_display);
    }

    /// <summary>칠한 게 있나 (조금이라도 안 투명한 픽셀) — 저장 전에 빈 그림 거르기</summary>
    public bool HasPaint()
    {
        var t = Read(_paint);
        var px = t.GetPixels32();
        UnityEngine.Object.DestroyImmediate(t);
        foreach (var p in px) if (p.a > 2) return true;
        return false;
    }

    Texture2D Read(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
        t.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        t.Apply();
        RenderTexture.active = prev;
        return t;
    }

    // ══════════════════ 그리기 ══════════════════

    void Show(RenderTexture src)
    {
        if (!Ready()) return;
        _showMat.SetTexture(IdBaseTex, _base != null ? _base : Texture2D.whiteTexture);
        _showMat.SetColor(IdBaseColor, _baseColor);
        Graphics.Blit(src, _display, _showMat);
    }

    void DrawDabs(RenderTexture rt, List<PaintDab> dabs)
    {
        if (dabs == null || dabs.Count == 0) return;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, Width, 0, Height);
        _dabMat.SetPass(0);
        GL.Begin(GL.QUADS);
        foreach (var d in dabs)
        {
            float x = d.P.x, y = FlipY ? Height - d.P.y : d.P.y, r = Mathf.Max(0.5f, d.R);
            GL.MultiTexCoord2(1, d.A, d.Hard);
            GL.MultiTexCoord2(0, 0f, 0f); GL.Vertex3(x - r, y - r, 0f);
            GL.MultiTexCoord2(1, d.A, d.Hard);
            GL.MultiTexCoord2(0, 0f, 1f); GL.Vertex3(x - r, y + r, 0f);
            GL.MultiTexCoord2(1, d.A, d.Hard);
            GL.MultiTexCoord2(0, 1f, 1f); GL.Vertex3(x + r, y + r, 0f);
            GL.MultiTexCoord2(1, d.A, d.Hard);
            GL.MultiTexCoord2(0, 1f, 0f); GL.Vertex3(x + r, y - r, 0f);
        }
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = prev;
    }

    static void Clear(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    RenderTexture Copy(RenderTexture src)
    {
        var rt = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        return rt;
    }

    static void Release(RenderTexture rt)
    {
        if (rt != null) RenderTexture.ReleaseTemporary(rt);
    }

    static void ClearList(List<RenderTexture> l)
    {
        foreach (var rt in l) Release(rt);
        l.Clear();
    }

    // ══════════════════ 셰이더 ══════════════════

    static bool Ready()
    {
        if (_dabMat != null && _compMat != null && _showMat != null) return true;
        _dabMat ??= Mat("PaintDab", "Hidden/Paint/Dab");
        _compMat ??= Mat("PaintComposite", "Hidden/Paint/Composite");
        _showMat ??= Mat("PaintShow", "Hidden/Paint/Show");
        return _dabMat != null && _compMat != null && _showMat != null;
    }

    static Material Mat(string file, string name)
    {
        var sh = Resources.Load<Shader>("PaintShaders/" + file);
        if (sh == null) sh = Shader.Find(name);
        if (sh == null)
        {
            Debug.LogError($"[칠하기] 셰이더 '{name}'를 못 찾음 — Paint/Resources/PaintShaders 폴더 확인");
            return null;
        }
        return new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
    }

    /// <summary>스프레이 알갱이용 잡음 (256 반복)</summary>
    static Texture2D Noise
    {
        get
        {
            if (_noise != null) return _noise;
            const int N = 256;
            _noise = new Texture2D(N, N, TextureFormat.R8, false, true)
            {
                name = "PaintNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var r = new System.Random(17);
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)r.Next(256);
                px[i] = new Color32(v, v, v, 255);
            }
            _noise.SetPixels32(px);
            _noise.Apply();
            return _noise;
        }
    }

    public void Dispose()
    {
        ClearList(_undo);
        ClearList(_redo);
        foreach (var rt in new[] { _paint, _preview, _mask, _display })
            if (rt != null)
            {
                rt.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(rt);
                else UnityEngine.Object.DestroyImmediate(rt);
            }
    }
}
