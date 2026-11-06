using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실행 중 UI 그림 위에 바로 덧칠 (개발용) — RawImage · Image(Simple)가 있는 UI에 붙이고 플레이 중 F9 (또는 메뉴 Tools → UI → 선택한 UI 칠하기).
///   그림 칸 위에서 칠함 · 아래 도구 막대 그대로 · '완료' = 저장
///   저장: 에디터에선 그 그림 PNG 파일을 덮어씀 (프로젝트에 바로 반영 · 플레이 끝나도 남음) / 원본이 없으면 Assets/UI/Game/UiArt/Painted 에 새로
///         빌드에선 persistentDataPath/paint/ui_이름.png
/// 9-slice(Sliced) 그림은 화면 모양과 그림 모양이 달라서 안 됨 — 에디터 '그림 그리기 창'으로
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UiPaintCanvas : MonoBehaviour
{
    [Tooltip("칠할 그림 — 비우면 이 UI의 RawImage · Image 그림")]
    public Texture2D Art;
    [Tooltip("그림이 없을 때 새로 만들 크기")]
    public Vector2Int NewSize = new(1024, 256);
    public KeyCode Toggle = KeyCode.F9;
    [Tooltip("에디터 · Development 빌드에서만")]
    public bool DevOnly = true;
    [Header("붓 크기 (그림 짧은 변 대비)")]
    public float MinRadius = 0.01f;
    public float MaxRadius = 0.25f;

    public bool Painting => _canvas != null;

    PaintCanvas _canvas;
    PaintToolbarView _bar;
    GameObject _barHost;
    RawImage _view;
    Graphic _hidden;
    Texture _oldRawTex;
    Texture _base;
    readonly StrokeShaper _shaper = new();
    readonly System.Collections.Generic.List<PaintDab> _dabs = new();
    bool _drawing;
    int _seed = 1;

    bool Allowed => !DevOnly || Application.isEditor || Debug.isDebugBuild;

    void Update()
    {
        if (!Allowed) return;
        if (Input.GetKeyDown(Toggle)) { if (Painting) End(true); else Begin(); }
        if (!Painting) return;
        if (Input.GetKeyDown(KeyCode.Space) && _bar != null) _bar.ToggleHidden();      // 막대 내리기 · 올리기
        StepInput();
    }

    void OnDisable()
    {
        if (Painting) End(false);
    }

    // ══════════════════ 시작 · 끝 ══════════════════

    public void Begin()
    {
        if (Painting) return;
        var raw = GetComponent<RawImage>();
        var img = GetComponent<Image>();
        if (img != null && img.type != Image.Type.Simple && img.type != Image.Type.Filled)
            Debug.LogWarning("[UI 칠하기] Sliced · Tiled 그림은 화면 모양과 달라서 위치가 어긋나 — 에디터 'UI 그림 그리기 창'을 써", this);

        _base = Art != null ? Art : raw != null ? raw.texture : img != null && img.sprite != null ? img.sprite.texture : null;
        int w = _base != null ? _base.width : NewSize.x, h = _base != null ? _base.height : NewSize.y;
        _canvas = new PaintCanvas(w, h, _base, _base != null ? Color.white : Color.clear, 30);

        if (raw != null)
        {
            _view = raw;
            _oldRawTex = raw.texture;
            raw.texture = _canvas.Display;
        }
        else
        {
            var go = new GameObject("__UiPaintView", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            _view = go.AddComponent<RawImage>();
            _view.texture = _canvas.Display;
            _view.raycastTarget = false;
            if (img != null && img.sprite != null)
            {
                var tr = img.sprite.textureRect;
                _view.uvRect = new Rect(tr.x / w, tr.y / h, tr.width / w, tr.height / h);
            }
            if (img != null) { _hidden = img; img.enabled = false; }
        }

        // 도구 막대 — 맨 위 캔버스
        _barHost = new GameObject("__UiPaintBar");
        var barRoot = GameUiKit.MakeScreen(_barHost, 900, null);
        var set = UiPrefabSet.Get();
        _bar = PaintToolbarBuilder.Build(barRoot, set != null ? set.Font : null);
        _bar.UndoClicked += () => _canvas?.Undo();
        _bar.RedoClicked += () => _canvas?.Redo();
        _bar.ClearClicked += () => _canvas?.ClearPaint();
        _bar.DoneClicked += () => End(true);
        GameUiKit.EnsureEventSystem();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log($"[UI 칠하기] '{name}' 칠하기 시작 — {Toggle} 또는 '완료'로 저장", this);
    }

    public void End(bool save)
    {
        if (!Painting) return;
        if (_drawing) { _canvas.Commit(); _drawing = false; }
        if (save) Save();

        if (_view != null)
        {
            if (_hidden != null) { _hidden.enabled = true; Destroy(_view.gameObject); }
            else _view.texture = save ? (Texture)LastSaved ?? _oldRawTex : _oldRawTex;
        }
        _hidden = null;
        _view = null;
        if (_barHost != null) Destroy(_barHost);
        _canvas.Dispose();
        _canvas = null;
    }

    Texture2D LastSaved;

    // ══════════════════ 입력 ══════════════════

    void StepInput()
    {
        var rt = (RectTransform)transform;
        var cv = GetComponentInParent<Canvas>();
        var cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
        Vector2 mouse = Input.mousePosition;
        float t = Time.unscaledTime;

        bool onBar = _bar != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_bar.transform, mouse, null);
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, mouse, cam, out var local);
        var r = rt.rect;
        var uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        if (_view != null)
        {
            var u = _view.uvRect;
            uv = new Vector2(u.x + uv.x * u.width, u.y + uv.y * u.height);
        }
        var px = new Vector2(uv.x * _canvas.Width, uv.y * _canvas.Height);
        bool over = inside && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        if (Input.GetMouseButtonDown(0) && over && !onBar)
        {
            var b = _bar.Brush;
            _shaper.Radius = b.Radius(MinRadius, MaxRadius) * Mathf.Min(_canvas.Width, _canvas.Height);
            _shaper.Stabilize = b.Stabilize;
            _shaper.StabilizeAmount = b.StabilizeAmount;
            _shaper.Spray = b.IsSpray;
            _shaper.Drips = b.Drips;
            _shaper.Snap = b.Snap;
            _shaper.Eraser = b.IsEraser;
            _shaper.Outline = b.Outline;
            _shaper.DripDir = new Vector2(0f, -1f);
            _shaper.Begin(px, t, _seed++);
            _drawing = true;
        }
        if (!_drawing) return;

        if (Input.GetMouseButton(0))
        {
            if (inside) _shaper.Move(px, t);
            else _shaper.Tick(t);
            _shaper.Build(_dabs);
            _canvas.DrawStroke(_bar.Brush, _dabs, PaintBrush.OutlinePx(_shaper.Radius));
        }
        else
        {
            _shaper.Finish(t);
            _shaper.Build(_dabs);
            _canvas.DrawStroke(_bar.Brush, _dabs, PaintBrush.OutlinePx(_shaper.Radius));
            _canvas.Commit();
            _drawing = false;
        }
    }

    // ══════════════════ 저장 ══════════════════

    void Save()
    {
        var tex = _canvas.ReadDisplay();
        byte[] png = tex.EncodeToPNG();
#if UNITY_EDITOR
        string path = _base != null ? UnityEditor.AssetDatabase.GetAssetPath(_base) : null;
        bool asset = !string.IsNullOrEmpty(path) && path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);
        if (!asset)
        {
            Directory.CreateDirectory("Assets/UI/Game/UiArt/Painted");
            path = $"Assets/UI/Game/UiArt/Painted/{name}_{System.DateTime.Now:MMdd_HHmmss}.png";
        }
        File.WriteAllBytes(path, png);
        UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
        if (!asset && UnityEditor.AssetImporter.GetAtPath(path) is UnityEditor.TextureImporter ti)
        {
            ti.textureType = UnityEditor.TextureImporterType.Sprite;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        Debug.Log($"[UI 칠하기] 저장 → {path}", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path));
#else
        string path = Path.Combine(Application.persistentDataPath, "paint", $"ui_{name}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, png);
        Debug.Log($"[UI 칠하기] 저장 → {path}");
#endif
        LastSaved = tex;
    }
}
