using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 무기 · 장비의 3D 모델을 찍은 그림 (인벤토리 칸 등).
///   1) 에디터에서 찍어 둔 그림 — Resources/ItemShots/{Id} (Tools → UI → 아이템 모델 사진 · 장비 에셋 ⋮ 메뉴 · 모션 셋업)
///   2) 없으면 게임 중 처음 볼 때 한 번 찍고 기억해 둠
///   - 모델 = EquipmentData.ModelPrefab (메인 모델만). 없으면 null → 쓰는 쪽이 아이콘으로
///   - 프리팹을 통째로 띄우지 않고 메시 · 재질만 복사해서 찍음 (스크립트가 안 돌게)
///   - 아무도 안 쓰는 먼 곳 · 전용 레이어(31)에서 찍음. 조명은 씬 조명 + 주변광
///   - 가장 긴 쪽이 화면 대각선(↗)이 되게 눕혀서, 살짝 돌려 입체감
/// </summary>
public static class ItemThumbnail
{
    public static int Layer = 31;
    public static int Size = 256;

    static readonly Dictionary<int, Sprite> _cache = new();
    static readonly Vector3 Far = new(0f, -5000f, 0f);

    /// <summary>모델 그림 — 모델이 없거나 못 찍으면 null</summary>
    public static Sprite Get(EquipmentData d)
    {
        if (d == null || d.ModelPrefab == null) return null;
        if (_cache.TryGetValue(d.Id, out var s)) return s;

        s = Resources.Load<Sprite>($"ItemShots/{d.Id}");
        if (s != null) { _cache[d.Id] = s; return s; }

        try { s = Render(d.ModelPrefab); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ItemThumbnail] {d.name} 찍기 실패 — 아이콘 사용\n{e.Message}");
            s = null;
        }
        _cache[d.Id] = s;
        return s;
    }

    /// <summary>모두 지우기 (모델을 바꿨을 때)</summary>
    public static void Clear()
    {
        foreach (var kv in _cache)
            if (kv.Value != null) { Object.Destroy(kv.Value.texture); Object.Destroy(kv.Value); }
        _cache.Clear();
    }

    static Sprite Render(GameObject prefab)
    {
        var tex = RenderTexture2D(prefab, Size, null);
        return tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
    }

    /// <summary>
    /// 모델을 찍은 그림 (투명 배경). view = 모델 회전 (null이면 가장 긴 쪽이 ↗ 대각선) — 에디터 도구도 씀
    /// </summary>
    public static Texture2D RenderTexture2D(GameObject prefab, int size, Quaternion? view)
    {
        // ── 메시만 복사 ──
        var root = new GameObject("__Thumb") { hideFlags = HideFlags.HideAndDontSave };
        var pivot = new GameObject("Pivot") { hideFlags = HideFlags.HideAndDontSave };
        pivot.transform.SetParent(root.transform, false);

        var src = prefab.transform;
        var toRoot = src.worldToLocalMatrix;
        int count = 0;
        Bounds local = default;

        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh == null || !r.enabled) continue;

            var go = new GameObject(r.name) { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
            go.transform.SetParent(pivot.transform, false);
            var m = toRoot * r.transform.localToWorldMatrix;
            go.transform.localPosition = m.GetColumn(3);
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = r.sharedMaterials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 모델 기준 크기
            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(c);
                if (count == 0 && i == 0) local = new Bounds(p, Vector3.zero);
                else local.Encapsulate(p);
            }
            count++;
        }

        if (count == 0) { Object.DestroyImmediate(root); return null; }

        // ── 눕히기: 가장 긴 축 → 오른쪽, 그다음 ↗ 대각선 + 살짝 돌림 ──
        Vector3 ext = local.extents;
        Vector3 axis = ext.x >= ext.y && ext.x >= ext.z ? Vector3.right : ext.y >= ext.z ? Vector3.up : Vector3.forward;
        Quaternion lay = Quaternion.FromToRotation(axis, Vector3.right);
        Quaternion rot = view ?? Quaternion.AngleAxis(35f, Vector3.forward) * Quaternion.AngleAxis(-20f, Vector3.up) * Quaternion.AngleAxis(15f, Vector3.right) * lay;

        pivot.transform.localPosition = -(rot * local.center);
        pivot.transform.localRotation = rot;
        root.transform.position = Far;

        // 화면에 맞추기 — 돌린 뒤 실제 범위
        var wb = new Bounds(Far, Vector3.zero);
        bool first = true;
        foreach (var r in pivot.GetComponentsInChildren<Renderer>())
        {
            if (first) { wb = r.bounds; first = false; }
            else wb.Encapsulate(r.bounds);
        }

        // ── 카메라 ──
        var camGo = new GameObject("__ThumbCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(wb.extents.x, wb.extents.y) * 1.12f + 0.001f;
        cam.transform.position = wb.center - Vector3.forward * (wb.extents.z + 2f);
        cam.transform.rotation = Quaternion.identity;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = wb.extents.z * 2f + 4f;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;
        cam.allowMSAA = true;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
        }

        var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;

        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Thumb_" + prefab.name, wrapMode = TextureWrapMode.Clamp };
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply(false, false);
        RenderTexture.active = prev;

        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(root);

        return tex;
    }
}
