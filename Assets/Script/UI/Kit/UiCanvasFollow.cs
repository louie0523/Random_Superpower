using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코드로 만든 화면(MakeScreen)이 씬 UI 방식에 맞춰 보이게 함.
/// 'Match Camera'가 켜진 캔버스 (HUD):
///   - UIManager의 uiCamera가 있으면 → Screen Space - Camera + 그 카메라.
///     거리는 씬 UI 캔버스와 같게 (카메라 근 · 원 클립 안으로 맞춤), 레이어는 그 카메라가 그리는 레이어로
///   - 없으면 씬의 카메라 모드 캔버스를 따라가고, 그것도 없으면 Overlay 그대로
///   - 'Force Overlay'(UiPrefabSet)가 켜져 있으면 항상 Overlay
/// 메뉴 창(설정 · ESC · 로비 · 암시장 · 능력 · 상점)은 꺼짐 = 늘 Overlay (맨 위에 떠야 하니까)
/// 다른 캔버스 안에 들어간 경우엔 꽉 채우고 레이어를 맞춤 (예전 방식 대비)
/// 처음 맞출 때 콘솔에 '[UI] … 캔버스:' 한 줄 — 안 보이면 이 줄을 보내 줘
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class UiCanvasFollow : MonoBehaviour
{
    /// <summary>씬 UI 캔버스 — UIManager가 넣어 줌</summary>
    public static Canvas Reference;
    /// <summary>UI 카메라 — UIManager의 uiCamera</summary>
    public static Camera UiCamera;

    [Tooltip("씬 UI 방식(오버레이 / 스크린 카메라)을 따라감 — HUD는 켜짐, 메뉴 창은 꺼짐")]
    public bool matchCamera;

    Canvas _c;
    CanvasScaler _sc;
    RectTransform _rt;
    int _order;
    bool _nested;
    bool _logged;
    Vector2 _lastSize;
    float _lastRef;

    void Awake()
    {
        _c = GetComponent<Canvas>();
        _sc = GetComponent<CanvasScaler>();
        _rt = transform as RectTransform;
        _order = _c.sortingOrder;
    }

    void OnEnable() => Apply();
    void OnTransformParentChanged() { if (_c != null) Apply(); }

    /// <summary>지금 자리 · 카메라에 맞게 다시 맞춤 (UIManager가 플레이어 붙을 때도 부름)</summary>
    public void Apply()
    {
        if (_c == null) Awake();
        var outer = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>(true) : null;
        _nested = outer != null;

        if (_nested)
        {
            var root = outer.rootCanvas;
            _c.overrideSorting = true;
            _c.sortingLayerID = root.sortingLayerID;
            _c.sortingOrder = _order;
            SetLayer(gameObject, root.gameObject.layer);
            _lastSize = Vector2.zero;
            FitNested();
            Log("안쪽 캔버스 (" + root.name + " 안)", root.worldCamera);
            return;
        }

        SetLayer(gameObject, 5);
        if (!matchCamera) return;

        var set = UiPrefabSet.Get();
        if (set != null && set.ForceOverlay)
        {
            _c.renderMode = RenderMode.ScreenSpaceOverlay;
            Log("Overlay (Force Overlay 켜짐)", null);
            return;
        }

        var r = FindReference();
        Camera cam = UiCamera;
        if (cam == null && r != null && r.renderMode == RenderMode.ScreenSpaceCamera) cam = r.worldCamera;
        if (cam == null)
        {
            _c.renderMode = RenderMode.ScreenSpaceOverlay;
            Log("Overlay (UI 카메라 없음)", null);
            return;
        }

        float dist = r != null && r.renderMode == RenderMode.ScreenSpaceCamera ? r.planeDistance : 1f;
        float near = cam.nearClipPlane, far = cam.farClipPlane;
        dist = Mathf.Clamp(dist, near + 0.02f, Mathf.Max(near + 0.03f, far - 0.02f));

        _c.renderMode = RenderMode.ScreenSpaceCamera;
        _c.worldCamera = cam;
        _c.planeDistance = dist;
        if (r != null) _c.sortingLayerID = r.sortingLayerID;
        _c.sortingOrder = _order;

        int layer = r != null && InMask(cam, r.gameObject.layer) ? r.gameObject.layer : LayerFor(cam);
        SetLayer(gameObject, layer);
        Log("Screen Space - Camera", cam);
    }

    void Log(string mode, Camera cam)
    {
        if (_logged) return;
        _logged = true;
        string camInfo = cam != null
            ? $" · 카메라 {cam.name} (켜짐 {cam.isActiveAndEnabled}, 클립 {cam.nearClipPlane:0.###}~{cam.farClipPlane:0.#}, 레이어 그림 {InMask(cam, gameObject.layer)})"
            : "";
        Debug.Log($"[UI] {name} 캔버스: {mode}{camInfo} · 거리 {_c.planeDistance:0.###} · 레이어 {LayerMask.LayerToName(gameObject.layer)} · 순서 {_c.sortingOrder}", this);
        if (cam != null && !InMask(cam, gameObject.layer))
            Debug.LogWarning($"[UI] {cam.name}의 Culling Mask에 '{LayerMask.LayerToName(gameObject.layer)}' 레이어가 없어서 안 보임 — 카메라 Culling Mask에 UI를 켜 줘", cam);
    }

    void LateUpdate()
    {
        if (_nested) FitNested();
    }

    /// <summary>안쪽 캔버스 — 부모를 꽉 채우고 크기는 Canvas Scaler 기준 해상도대로 (HUD 크기 설정 포함)</summary>
    void FitNested()
    {
        if (_rt == null) return;
        Vector2 size = Vector2.zero;
        var p = _rt.parent as RectTransform;
        if (p != null) size = p.rect.size;
        if (size.x < 2f || size.y < 2f)
        {
            var root = _c.rootCanvas != null ? _c.rootCanvas.transform as RectTransform : null;
            if (root == null) return;
            size = root.rect.size;
            p = null;
        }
        if (size.x < 2f || size.y < 2f) return;

        float refY = _sc != null ? _sc.referenceResolution.y : 0f;
        if (size == _lastSize && Mathf.Approximately(refY, _lastRef)) return;
        _lastSize = size;
        _lastRef = refY;

        float k = 1f;
        if (_sc != null && _sc.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            var rr = _sc.referenceResolution;
            float lw = Mathf.Log(size.x / Mathf.Max(1f, rr.x), 2f);
            float lh = Mathf.Log(size.y / Mathf.Max(1f, rr.y), 2f);
            k = Mathf.Pow(2f, Mathf.Lerp(lw, lh, _sc.matchWidthOrHeight));
        }
        if (k <= 0.0001f) k = 1f;

        _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(0.5f, 0.5f);
        _rt.localRotation = Quaternion.identity;
        _rt.localScale = new Vector3(k, k, 1f);
        _rt.sizeDelta = size / k;
        _rt.anchoredPosition3D = Vector3.zero;
        if (p == null) _rt.position = _c.rootCanvas.transform.position;
    }

    Canvas FindReference()
    {
        if (Reference != null && Reference != _c) return Reference.rootCanvas;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == _c || !c.isRootCanvas || c.GetComponent<UiCanvasFollow>() != null) continue;
            if (c.renderMode == RenderMode.ScreenSpaceCamera && c.worldCamera != null) return c;
        }
        return null;
    }

    static bool InMask(Camera cam, int layer) => (cam.cullingMask & (1 << layer)) != 0;

    /// <summary>카메라가 그리는 레이어 — UI(5)가 있으면 UI, 아니면 처음 켜진 것</summary>
    static int LayerFor(Camera cam)
    {
        if (InMask(cam, 5)) return 5;
        for (int i = 0; i < 32; i++)
            if (InMask(cam, i)) return i;
        return 5;
    }

    static void SetLayer(GameObject go, int layer)
    {
        if (go.layer != layer) go.layer = layer;
        foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
    }
}
