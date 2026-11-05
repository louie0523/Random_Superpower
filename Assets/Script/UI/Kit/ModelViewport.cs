using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// UI 칸 안의 3D 모델 (무기 보기 · 스킨) — 먼 곳에 메시만 복사해서 띄우고 전용 카메라로 찍어 RawImage에.
/// 저절로 천천히 돌고, 끌면 돌리기 · 휠 = 확대. 스크립트는 복사 안 함 (안 돌게)
/// </summary>
public class ModelViewport : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler
{
    public RawImage Target;
    public int Size = 640;
    [Tooltip("저절로 도는 속도 (도/초)")]
    public float AutoSpin = 18f;
    public float Fov = 24f;
    public Color Background = new(0f, 0f, 0f, 0f);

    const int Layer = 31;
    static int _count;

    GameObject _root;
    Camera _cam;
    RenderTexture _rt;
    Bounds _b;
    float _yaw = -35f, _pitch = 12f, _zoom = 1f;
    bool _dragging, _has;
    Vector3 _origin;

    void Awake()
    {
        _count++;
        _origin = new Vector3(-9000f - _count * 60f, -9000f, 0f);
        if (Target == null) Target = GetComponent<RawImage>();
    }

    void OnDisable() { if (_cam != null) _cam.enabled = false; }

    void OnDestroy()
    {
        if (_root != null) Destroy(_root);
        if (_cam != null) Destroy(_cam.gameObject);
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    /// <summary>모델 띄우기 (null = 비움). skin = 무기 스킨 재질</summary>
    public void Show(GameObject prefab, WeaponSkinData skin = null)
    {
        if (_root != null) Destroy(_root);
        _root = null;
        _has = false;
        if (_cam != null) _cam.enabled = false;
        if (prefab == null) { if (Target != null) Target.enabled = false; return; }

        _root = Copy(prefab, _origin, out _b, out bool any);
        if (!any) { Destroy(_root); _root = null; if (Target != null) Target.enabled = false; return; }
        if (skin != null) WeaponSkins.ApplyMaterials(_root, skin);
        _has = true;
        _zoom = 1f;
        Ensure();
        if (Target != null) Target.enabled = true;
    }

    void LateUpdate()
    {
        if (!_has || _cam == null) return;
        if (!_dragging) _yaw += AutoSpin * Time.unscaledDeltaTime;
        _cam.enabled = true;

        float r = Mathf.Max(0.05f, _b.extents.magnitude);
        float dist = r / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad) / _zoom;
        var rot = Quaternion.Euler(_pitch, _yaw, 0f);
        _cam.transform.SetPositionAndRotation(_b.center - rot * Vector3.forward * dist, rot);
        _cam.nearClipPlane = Mathf.Max(0.01f, dist - r * 2f);
        _cam.farClipPlane = dist + r * 2f;
    }

    void Ensure()
    {
        if (_rt == null)
        {
            var rr = Target != null ? Target.rectTransform.rect : new Rect(0, 0, 1, 1);
            float aspect = rr.height > 1f ? rr.width / rr.height : 1f;
            int w = Mathf.Max(16, Mathf.RoundToInt(Size * Mathf.Clamp(aspect, 0.25f, 4f)));
            _rt = new RenderTexture(w, Size, 24, RenderTextureFormat.ARGB32) { name = "ModelViewport", antiAliasing = 4 };
            _rt.Create();
        }
        if (_cam == null)
        {
            var go = new GameObject("ModelViewportCam") { hideFlags = HideFlags.DontSave };
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Background;
            _cam.cullingMask = 1 << Layer;
            _cam.fieldOfView = Fov;
            _cam.targetTexture = _rt;
            _cam.depth = -60f;
            var data = _cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
            }
        }
        if (Target != null) Target.texture = _rt;
    }

    public void OnBeginDrag(PointerEventData e) => _dragging = true;
    public void OnEndDrag(PointerEventData e) => _dragging = false;

    public void OnDrag(PointerEventData e)
    {
        _yaw += e.delta.x * 0.4f;
        _pitch = Mathf.Clamp(_pitch - e.delta.y * 0.3f, -60f, 70f);
    }

    public void OnScroll(PointerEventData e) => _zoom = Mathf.Clamp(_zoom * (e.scrollDelta.y > 0 ? 1.1f : 0.91f), 0.5f, 3f);

    /// <summary>프리팹의 메시 · 재질만 복사 (스크립트 없이) — 범위도 돌려줌</summary>
    public static GameObject Copy(GameObject prefab, Vector3 at, out Bounds bounds, out bool any)
    {
        var root = new GameObject("__Viewport") { hideFlags = HideFlags.DontSave, layer = Layer };
        root.transform.position = at;
        var toRoot = prefab.transform.worldToLocalMatrix;
        any = false;
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh == null || !r.enabled) continue;
            var go = new GameObject(r.name) { hideFlags = HideFlags.DontSave, layer = Layer };
            go.transform.SetParent(root.transform, false);
            var m = toRoot * r.transform.localToWorldMatrix;
            go.transform.localPosition = m.GetColumn(3);
            go.transform.localRotation = m.rotation;
            go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = r.sharedMaterials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            any = true;
        }
        bounds = new Bounds(at, Vector3.one * 0.1f);
        bool first = true;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
        return root;
    }
}
