using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 칠할 수 있는 면 (타이틀 벽 · 바닥) — MeshCollider가 있는 메시에 붙임 (UV대로 칠해짐).
///   바탕 = 재질 그대로의 빈 면 (Base Color · 필요하면 Base Texture) — 지우개로 안 지워짐
///   그 위 = 칠 그림 — 처음엔 Default Paint (코드로 그린 기본 그래피티 · 투명 PNG), 플레이어가 칠하면 그 그림 (내 PC 저장)
///   → 기본 그래피티도 플레이어가 지우개로 지울 수 있음
/// 에디터에서도 Scene 뷰에 그대로 보임 (재질은 안 건드리고 MaterialPropertyBlock으로)
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
[RequireComponent(typeof(MeshCollider))]
public class PaintSurface : MonoBehaviour
{
    [Tooltip("저장 파일 이름 · 씬 안에서 겹치지 않게")]
    public string Id = "wall";
    [Tooltip("그림 크기 (픽셀) — 면의 가로세로 비율에 맞추기 (벽 18×9 → 2048×1024)")]
    public Vector2Int Size = new(2048, 1024);

    [Header("바탕 (안 지워짐)")]
    [Tooltip("빈 면 색")]
    public Color BaseColor = new(0.20f, 0.17f, 0.26f, 1f);
    [Tooltip("빈 면 무늬 (비우면 색만)")]
    public Texture2D BaseTexture;

    [Header("기본 그래피티 (지워짐)")]
    [Tooltip("처음 칠해져 있는 그림 (투명 PNG) — 인스펙터 '씬에서 칠하기'로 고쳐서 저장")]
    public Texture2D DefaultPaint;

    [Header("칠하기")]
    [Tooltip("물감이 흘러내리는 방향 (그림 기준) — 벽 (0,-1) · 바닥 (0,0) = 안 흘러내림")]
    public Vector2 DripDirection = new(0f, -1f);
    [Tooltip("재질의 그림 칸 이름 (URP Lit = _BaseMap)")]
    public string TextureProperty = "_BaseMap";
    [Tooltip("플레이어가 칠한 걸 저장 · 불러오기")]
    public bool SavePlayerPaint = true;
    [Tooltip("되돌리기 칸 수 — 한 칸 = 그림 한 장 메모리 (2048×1024 = 8MB)")]
    [Range(0, 40)] public int MaxUndo = 12;

    public static readonly List<PaintSurface> All = new();

    public PaintCanvas Canvas { get; private set; }
    public MeshCollider Collider { get; private set; }
    /// <summary>이번에 칠했나 (안 칠했으면 저장 안 함 → 기본 그래피티가 바뀌어도 따라감)</summary>
    public bool Touched { get; set; }

    bool _playCanvas;
    MaterialPropertyBlock _mpb;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        Collider = GetComponent<MeshCollider>();
        if (!Application.isPlaying) RebuildPreview();
    }

    void OnDisable()
    {
        All.Remove(this);
        Dispose();
    }

    void Start()
    {
        if (Application.isPlaying) Init();
    }

    void Dispose()
    {
        Canvas?.Dispose();
        Canvas = null;
        _playCanvas = false;
        var r = GetComponent<Renderer>();
        if (r != null) r.SetPropertyBlock(null);
    }

    /// <summary>게임용 엔진 준비 (Start에서 저절로) — 저장한 그림이 있으면 그것, 없으면 기본 그래피티</summary>
    public void Init()
    {
        if (Canvas != null && _playCanvas) return;
        Dispose();
        Canvas = new PaintCanvas(Size.x, Size.y, BaseTexture, BaseColor, MaxUndo, true);
        _playCanvas = true;
        if (!(SavePlayerPaint && LoadPlayer())) LoadDefault();
        Bind();
    }

    /// <summary>에디터 미리보기 (기본 그래피티) — 값이 바뀌면 다시</summary>
    public void RebuildPreview()
    {
        Dispose();
        if (!isActiveAndEnabled) return;
        Canvas = new PaintCanvas(Size.x, Size.y, BaseTexture, BaseColor, MaxUndo, true);
        LoadDefault();
        Bind();
    }

    /// <summary>칠 그림을 기본 그래피티로 (없으면 빈 면)</summary>
    public void LoadDefault()
    {
        if (Canvas == null) return;
        if (DefaultPaint != null) Canvas.LoadPaint(DefaultPaint);
        else Canvas.LoadPaint(ClearTex);
    }

    /// <summary>보이는 그림을 이 면에 (재질은 그대로 · 블록으로만)</summary>
    void Bind()
    {
        var r = GetComponent<Renderer>();
        if (r == null || Canvas == null) return;
        var mat = r.sharedMaterial;
        string prop = mat != null && mat.HasProperty(TextureProperty) ? TextureProperty : "_MainTex";
        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetTexture(prop, Canvas.Display);
        if (mat != null && mat.HasProperty(prop + "_ST")) _mpb.SetVector(prop + "_ST", new Vector4(1f, 1f, 0f, 0f));
        if (mat != null && mat.HasProperty("_BaseColor")) _mpb.SetColor("_BaseColor", Color.white);
        if (mat != null && mat.HasProperty("_Color")) _mpb.SetColor("_Color", Color.white);
        r.SetPropertyBlock(_mpb);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || Application.isPlaying || EditorPainting) return;
            RebuildPreview();
        };
    }

    /// <summary>에디터 '씬에서 칠하기' 중 (값 바뀌어도 미리보기 다시 안 만듦)</summary>
    [System.NonSerialized] public bool EditorPainting;
#endif

    /// <summary>1미터가 그림 몇 픽셀 (붓 크기를 벽 · 바닥에서 같게)</summary>
    public float PixelsPerMeter
    {
        get
        {
            var mf = GetComponent<MeshFilter>();
            float w = 1f;
            if (mf != null && mf.sharedMesh != null) w = Mathf.Abs(mf.sharedMesh.bounds.size.x * transform.lossyScale.x);
            return Size.x / Mathf.Max(0.01f, w);
        }
    }

    /// <summary>광선이 이 면에 닿은 곳 → 그림 픽셀</summary>
    public bool Hit(Ray ray, out Vector2 px, float maxDist = 500f)
    {
        px = default;
        if (Collider == null) Collider = GetComponent<MeshCollider>();
        if (Collider == null || !Collider.enabled || !Collider.Raycast(ray, out var hit, maxDist)) return false;
        px = new Vector2(hit.textureCoord.x * Size.x, hit.textureCoord.y * Size.y);
        return true;
    }

    // ══════════════════ 플레이어 그림 저장 ══════════════════

    string SavePath => Path.Combine(Application.persistentDataPath, "paint", $"{SceneManager.GetActiveScene().name}_{Id}.png");

    /// <summary>칠한 게 있으면 저장 (다 지웠어도 저장 — 다음에 기본 그래피티가 다시 안 나오게)</summary>
    public void SavePlayer()
    {
        if (Canvas == null || !SavePlayerPaint || !Touched) return;
        try
        {
            var t = Canvas.ReadPaint();
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            File.WriteAllBytes(SavePath, t.EncodeToPNG());
            Destroy(t);
            Touched = false;
        }
        catch (System.Exception e) { Debug.LogWarning("[칠하기] 저장 실패 — " + e.Message); }
    }

    /// <summary>내가 칠한 걸 버리고 기본 그래피티로 (저장 파일도 지움)</summary>
    public void ResetToDefault()
    {
        try { if (File.Exists(SavePath)) File.Delete(SavePath); }
        catch { /* 못 지워도 됨 */ }
        LoadDefault();
        Touched = false;
    }

    bool LoadPlayer()
    {
        try
        {
            if (!File.Exists(SavePath)) return false;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool ok = t.LoadImage(File.ReadAllBytes(SavePath));
            if (ok) Canvas.LoadPaint(t);
            Destroy(t);
            return ok;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[칠하기] 불러오기 실패 — " + e.Message);
            return false;
        }
    }

    static Texture2D _clear;
    static Texture2D ClearTex
    {
        get
        {
            if (_clear != null) return _clear;
            _clear = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _clear.SetPixels(new[] { Color.clear, Color.clear, Color.clear, Color.clear });
            _clear.Apply();
            return _clear;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.36f, 0.62f, 0.6f);
        var r = GetComponent<Renderer>();
        if (r != null) Gizmos.DrawWireCube(r.bounds.center, r.bounds.size);
    }
}
