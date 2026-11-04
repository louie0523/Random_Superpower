#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 아이템 사진 무대 — 에디터 안의 따로 떨어진 작은 씬 (PreviewRenderUtility).
///   지금 열린 씬의 조명 · 물체와 섞이지 않음 · 조명 3개(정면 · 보조 · 뒤 테두리) + 주변광
///   모델은 프리팹을 통째로 띄우지 않고 메시 · 재질만 복사 (스크립트가 안 돌게) — ItemThumbnail과 같은 방식
///   카메라는 직교(원근 없음) — 인벤토리 아이콘처럼
/// 창(ItemPhotoStudio)이 미리보기에 쓰고, 찍을 때도 같은 무대로 찍음 → 보이는 그대로 저장
/// </summary>
public class ItemShotStage : IDisposable
{
    PreviewRenderUtility _pru;
    GameObject _root, _pivot, _rimGo;
    Light _rim;
    Bounds _local;
    GameObject _prefab;
    int _count;

    const int Layer = 31;

    public bool HasModel => _count > 0;
    public GameObject Prefab => _prefab;

    public ItemShotStage()
    {
        _pru = new PreviewRenderUtility();
        var cam = _pru.camera;
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << Layer;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
        }

        _rimGo = new GameObject("RimLight") { hideFlags = HideFlags.HideAndDontSave };
        _rim = _rimGo.AddComponent<Light>();
        _rim.type = LightType.Directional;
        _rim.shadows = LightShadows.None;
        _pru.AddSingleGO(_rimGo);

        foreach (var l in _pru.lights)
        {
            l.type = LightType.Directional;
            l.shadows = LightShadows.None;
        }
    }

    public void Dispose()
    {
        ClearModel();
        if (_rimGo != null) Object.DestroyImmediate(_rimGo);
        if (_pru != null) { _pru.Cleanup(); _pru = null; }
    }

    // ══════════════════ 모델 ══════════════════

    void ClearModel()
    {
        if (_root != null) Object.DestroyImmediate(_root);
        _root = _pivot = null;
        _count = 0;
        _prefab = null;
    }

    /// <summary>모델 바꾸기 (같은 프리팹이면 그대로)</summary>
    public void SetModel(GameObject prefab, bool force = false)
    {
        if (!force && prefab == _prefab && _root != null) return;
        ClearModel();
        _prefab = prefab;
        if (prefab == null) return;

        _root = new GameObject("__Shot") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
        _pivot = new GameObject("Pivot") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
        _pivot.transform.SetParent(_root.transform, false);

        var toRoot = prefab.transform.worldToLocalMatrix;
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh == null || !r.enabled || !r.gameObject.activeSelf) continue;

            var go = new GameObject(r.name) { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
            go.transform.SetParent(_pivot.transform, false);
            var m = toRoot * r.transform.localToWorldMatrix;
            go.transform.localPosition = m.GetColumn(3);
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = r.sharedMaterials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(c);
                if (_count == 0 && i == 0) _local = new Bounds(p, Vector3.zero);
                else _local.Encapsulate(p);
            }
            _count++;
        }

        if (_count == 0)        // 그릴 메시가 없음 — 프리팹은 기억 (매번 다시 만들지 않게)
        {
            Object.DestroyImmediate(_root);
            _root = _pivot = null;
            return;
        }
        _pru.AddSingleGO(_root);
    }

    // ══════════════════ 각도 ══════════════════

    /// <summary>가장 긴 축을 오른쪽(→)으로 눕히는 회전</summary>
    public Quaternion Lay()
    {
        Vector3 e = _local.extents;
        Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
        return Quaternion.FromToRotation(axis, Vector3.right);
    }

    /// <summary>이 모습의 모델 회전 (카메라는 +Z를 봄)</summary>
    public Quaternion Rotation(ShotLook k)
    {
        var lay = Lay();
        var tilt = Quaternion.AngleAxis(-20f, Vector3.up) * Quaternion.AngleAxis(15f, Vector3.right);
        Quaternion b = k.Angle switch
        {
            ShotAngle.Diagonal => Quaternion.AngleAxis(35f, Vector3.forward) * tilt * lay,
            ShotAngle.Horizontal => tilt * lay,
            ShotAngle.Vertical => Quaternion.AngleAxis(90f, Vector3.forward) * tilt * lay,
            ShotAngle.Front => Quaternion.AngleAxis(10f, Vector3.right) * Quaternion.AngleAxis(180f + 25f, Vector3.up),   // 앞이 카메라 쪽 + 살짝 3/4
            _ => Quaternion.Euler(k.Euler),
        };
        return Quaternion.AngleAxis(k.Spin, Vector3.forward) * b;
    }

    // ══════════════════ 놓기 · 조명 ══════════════════

    void Pose(ShotLook k)
    {
        var rot = Rotation(k);
        _pivot.transform.localPosition = -(rot * _local.center);
        _pivot.transform.localRotation = rot;
        _root.transform.position = Vector3.zero;

        var wb = new Bounds(Vector3.zero, Vector3.zero);
        bool first = true;
        foreach (var r in _pivot.GetComponentsInChildren<Renderer>())
        {
            if (first) { wb = r.bounds; first = false; }
            else wb.Encapsulate(r.bounds);
        }

        float zoom = Mathf.Max(0.05f, k.Zoom);
        float half = Mathf.Max(wb.extents.x, wb.extents.y) * 1.12f / zoom + 0.001f;
        var cam = _pru.camera;
        cam.orthographicSize = half;
        var c = wb.center - new Vector3(k.Offset.x, k.Offset.y, 0f) * half;
        cam.transform.SetPositionAndRotation(c - Vector3.forward * (wb.extents.z + 2f), Quaternion.identity);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = wb.extents.z * 2f + 4f;
    }

    void Lights(ShotLight l)
    {
        if (l == null) l = new ShotLight();
        var key = _pru.lights[0];
        key.transform.rotation = Quaternion.Euler(l.KeyPitch, l.KeyYaw, 0f);
        key.intensity = l.KeyIntensity;
        key.color = l.KeyColor;

        var fill = _pru.lights[1];
        fill.transform.rotation = Quaternion.Euler(l.FillPitch, l.FillYaw, 0f);
        fill.intensity = l.FillIntensity;
        fill.color = l.FillColor;

        _rim.transform.rotation = Quaternion.Euler(l.RimPitch, l.RimYaw, 0f);
        _rim.intensity = l.RimIntensity;
        _rim.color = l.RimColor;

        _pru.ambientColor = l.Ambient;
    }

    // ══════════════════ 그리기 ══════════════════

    /// <summary>창 미리보기 — 돌려주는 그림을 GUI에 그리면 됨 (투명 배경)</summary>
    public Texture Draw(Rect r, ShotLook k)
    {
        if (!HasModel || r.width < 4f || r.height < 4f) return null;
        Pose(k);
        Lights(k.Light);
        _pru.BeginPreview(r, GUIStyle.none);
        _pru.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _pru.Render(true, false);
        return _pru.EndPreview();
    }

    /// <summary>
    /// 저장용으로 찍기 — size × supersample 로 찍고 줄인 뒤 (선택) 카툰 외곽선. 투명 배경 RGBA
    /// </summary>
    public Texture2D Capture(ShotLook k, ItemShotSettings s)
    {
        if (!HasModel) return null;
        int size = Mathf.Clamp(s != null ? s.Size : 512, 32, 4096);
        int ss = Mathf.Clamp(s != null ? s.Supersample : 2, 1, 4);
        int n = size * ss;

        Pose(k);
        Lights(k.Light);

        var cam = _pru.camera;
        var rt = RenderTexture.GetTemporary(n, n, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.aspect = 1f;

        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var big = new Texture2D(n, n, TextureFormat.RGBA32, false);
        big.ReadPixels(new Rect(0, 0, n, n), 0, 0);
        big.Apply(false, false);
        RenderTexture.active = prev;
        cam.targetTexture = prevTarget;
        cam.ResetAspect();
        RenderTexture.ReleaseTemporary(rt);

        var px = big.GetPixels32();
        Object.DestroyImmediate(big);
        var small = Downsample(px, n, ss);
        if (s != null && s.Outline && s.OutlineWidth > 0f) small = AddOutline(small, size, s.OutlineWidth, s.OutlineColor);

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(small);
        tex.Apply(false, false);
        return tex;
    }

    /// <summary>ss×ss 칸 평균 (알파 곱해서 — 가장자리 검은 테두리 없게)</summary>
    static Color32[] Downsample(Color32[] src, int n, int ss)
    {
        if (ss <= 1) return src;
        int m = n / ss;
        var dst = new Color32[m * m];
        for (int y = 0; y < m; y++)
            for (int x = 0; x < m; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < ss; j++)
                    for (int i = 0; i < ss; i++)
                    {
                        var c = src[(y * ss + j) * n + x * ss + i];
                        float al = c.a / 255f;
                        r += c.r * al; g += c.g * al; b += c.b * al; a += al;
                    }
                if (a <= 0f) { dst[y * m + x] = new Color32(0, 0, 0, 0); continue; }
                dst[y * m + x] = new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / (ss * ss) * 255f));
            }
        return dst;
    }

    /// <summary>카툰 외곽선 — 모양을 width px 만큼 부풀린 자리를 외곽선 색으로 깔고 그 위에 원래 그림</summary>
    public static Color32[] AddOutline(Color32[] src, int n, float width, Color ink)
    {
        int R = Mathf.CeilToInt(width + 1f);
        var offs = new List<(int dx, int dy, float w)>();
        for (int dy = -R; dy <= R; dy++)
            for (int dx = -R; dx <= R; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float w = Mathf.Clamp01(width + 0.5f - d);      // 가장자리 부드럽게
                if (w > 0f) offs.Add((dx, dy, w));
            }

        var a = new float[n * n];
        for (int i = 0; i < a.Length; i++) a[i] = src[i].a / 255f;

        var dst = new Color32[n * n];
        Color32 inkC = ink;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = src[y * n + x];
                if (c.a == 255) { dst[y * n + x] = c; continue; }      // 꽉 찬 곳은 그대로
                float grow = 0f;
                foreach (var (dx, dy, w) in offs)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                    float v = a[yy * n + xx] * w;
                    if (v > grow) { grow = v; if (grow >= 1f) break; }
                }
                float sa = c.a / 255f;
                float oa = grow * (1f - sa);
                float outA = sa + oa;
                if (outA <= 0f) { dst[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                byte Mix(byte s, byte k) => (byte)Mathf.Clamp((s * sa + k * oa) / outA, 0f, 255f);
                dst[y * n + x] = new Color32(Mix(c.r, inkC.r), Mix(c.g, inkC.g), Mix(c.b, inkC.b), (byte)(outA * 255f));
            }
        return dst;
    }
}
#endif
