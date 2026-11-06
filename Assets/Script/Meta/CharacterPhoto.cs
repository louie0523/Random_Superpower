using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 캐릭터 사진 — 기본 캐릭터 모델(GameMeta) + 꾸미기를 한 장 찍어 둠 (대기실 카드 등).
/// 같은 꾸미기면 다시 안 찍음 (캐시). 먼 곳 · 전용 레이어(31)에서 찍고 바로 지움 — 스크립트는 떼고 찍음
/// </summary>
public static class CharacterPhoto
{
    public const int Layer = 31;
    public enum Frame { Bust, Full }

    static readonly Dictionary<string, Texture2D> _cache = new();
    static readonly Vector3 Far = new(5000f, -5000f, 0f);

    /// <summary>사진 (없으면 찍음) — partsCode = Appearance.PartsCode · 모델이 없으면 null</summary>
    public static Texture2D Get(string partsCode, Frame frame = Frame.Bust, int w = 256, int h = 360)
    {
        string key = $"{partsCode}|{frame}|{w}x{h}";
        if (_cache.TryGetValue(key, out var t) && t != null) return t;
        try { t = Render(Appearance.Parse(partsCode), frame, w, h); }
        catch (System.Exception e)
        {
            Debug.LogWarning("[캐릭터 사진] 찍기 실패\n" + e.Message);
            t = null;
        }
        _cache[key] = t;
        return t;
    }

    public static void ClearCache()
    {
        foreach (var kv in _cache) if (kv.Value != null) Object.Destroy(kv.Value);
        _cache.Clear();
    }

    /// <summary>
    /// 기본 캐릭터 모델을 띄움 (스크립트는 떼고 · Animator와 꾸미기 적용기만 남김) — 타이틀 · 꾸미기 · 사진이 같이 씀
    /// </summary>
    public static GameObject SpawnModel(Transform parent, Appearance look, int layer)
    {
        var meta = GameMeta.Get();
        if (meta == null || meta.CharacterModel == null) return null;

        var holder = new GameObject("__CharHold");
        holder.SetActive(false);
        var go = Object.Instantiate(meta.CharacterModel, holder.transform);
        go.name = "Character";
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(mb is CharacterAppearanceApplier)) Object.DestroyImmediate(mb);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (var c in go.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c);
        foreach (var l in go.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(l);
        foreach (var cv in go.GetComponentsInChildren<Canvas>(true)) if (cv != null) Object.DestroyImmediate(cv.gameObject);

        // 플레이어 모델을 그대로 넣었으면 1인칭용으로 '그림자만'인 몸이 있음 → 전부 보이게
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        var an = go.GetComponentInChildren<Animator>(true);
        if (an != null)
        {
            if (meta.IdleAnimator != null) an.runtimeAnimatorController = meta.IdleAnimator;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.applyRootMotion = false;
        }

        go.transform.SetParent(parent, false);
        go.transform.localScale = meta.CharacterModel.transform.localScale * Mathf.Max(0.01f, meta.CharacterScale);
        Object.Destroy(holder);

        var ap = go.GetComponent<CharacterAppearanceApplier>();
        if (ap == null) ap = go.AddComponent<CharacterAppearanceApplier>();
        ap.Apply(look);
        CharacterAppearanceApplier.SetLayer(go.transform, layer);
        return go;
    }

    static Texture2D Render(Appearance look, Frame frame, int w, int h)
    {
        var root = new GameObject("__CharPhoto") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = Far;
        var go = SpawnModel(root.transform, look, Layer);
        if (go == null) { Object.Destroy(root); return null; }

        var an = go.GetComponentInChildren<Animator>();
        if (an != null) { an.Update(0f); an.Update(0.1f); }       // 첫 자세 (T자세 안 나오게)

        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { Object.Destroy(root); return null; }
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);

        // ── 카메라 (살짝 3/4) ──
        float fov = 22f, aspect = w / (float)h;
        Vector3 center;
        float height;
        if (frame == Frame.Bust)
        {
            height = b.size.y * 0.52f;
            center = new Vector3(b.center.x, b.max.y - height * 0.45f, b.center.z);
        }
        else
        {
            height = b.size.y * 1.08f;
            center = b.center;
        }
        height = Mathf.Max(height, b.size.x * 1.05f / aspect);
        float dist = height * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) + b.extents.z;
        Vector3 fwd = Quaternion.AngleAxis(-18f, Vector3.up) * go.transform.forward;

        var camGo = new GameObject("__CharPhotoCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = fov;
        cam.aspect = aspect;
        cam.transform.SetPositionAndRotation(center + fwd * dist, Quaternion.LookRotation(-fwd, Vector3.up));
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = dist + b.size.magnitude + 1f;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
        }

        // 잠깐 켜는 조명 (어두운 씬에서도 밝게)
        var lightGo = new GameObject("__CharPhotoLight") { hideFlags = HideFlags.HideAndDontSave };
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.cullingMask = 1 << Layer;
        light.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.LookRotation(-fwd + Vector3.down * 0.6f + Vector3.left * 0.3f);

        var rt = RenderTexture.GetTemporary(w * 2, h * 2, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else cam.Render();

        // 반으로 줄이기 (부드럽게)
        var small = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(rt, small);
        var prev = RenderTexture.active;
        RenderTexture.active = small;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CharPhoto", wrapMode = TextureWrapMode.Clamp };
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false, false);
        RenderTexture.active = prev;

        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        RenderTexture.ReleaseTemporary(small);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(lightGo);
        Object.DestroyImmediate(root);
        return tex;
    }
}
