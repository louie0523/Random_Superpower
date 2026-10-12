using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>3인칭 카메라 설정 — 스킬·Behaviour마다 따로</summary>
[Serializable]
public class ThirdPersonSettings
{
    [Tooltip("카메라 위치 (시선 기준): x 오른쪽 · y 위 · z 앞(뒤는 음수)")]
    public Vector3 Offset = new(0.4f, 0.35f, -3.6f);
    [Tooltip("기준점(머리) 높이 추가 (m)")]
    public float PivotHeight = 0.1f;
    [Tooltip("내려다보는 각도 추가 (°)")]
    public float PitchOffset = 4f;
    [Tooltip("FOV 추가")]
    public float Fov = 4f;
    [Tooltip("1인칭 → 3인칭 전환 시간 (초)")]
    public float BlendIn = 0.35f;
    [Tooltip("3인칭 → 1인칭 전환 시간 (초)")]
    public float BlendOut = 0.3f;
}

/// <summary>
/// 3인칭 시점 — 플레이어 프리팹(루트)에 붙임. 본인 화면에서만 동작.
///   Enter(주인, 설정) / Exit(주인). 여러 곳에서 동시에 요청하면 마지막 요청 설정을 씀.
///
/// 원리: 1인칭 카메라는 팔이 붙어 있어서 움직이지 않고, 별도 3인칭 카메라를 만들어
///       1인칭 카메라 자리에서 출발 → 뒤쪽으로 부드럽게 빠짐. 돌아올 땐 반대로 들어간 뒤 원래 카메라로 교체.
///       벽은 스피어캐스트로 막고, 벽에서 벗어날 땐 천천히 펴져서 덜컥거리지 않음.
/// </summary>
[DefaultExecutionOrder(10000)]   // 이동·카메라·Fusion 보간이 끝난 뒤
public class ThirdPersonView : MonoBehaviour
{
    [Header("참조 (비우면 자동)")]
    [Tooltip("1인칭 카메라 (FPSCamera가 쓰는 Camera)")]
    [SerializeField] private Camera fpCamera;
    [Tooltip("시선 회전 기준 — CameraRoot (피치가 도는 오브젝트)")]
    [SerializeField] private Transform pivot;

    [Header("벽 충돌")]
    [Tooltip("카메라를 막는 레이어 — 맵(벽·바닥)만. 플레이어 레이어는 빼기")]
    [SerializeField] private LayerMask obstacleMask = 1;
    [SerializeField] private float probeRadius = 0.25f;
    [Tooltip("벽에서 띄울 거리")]
    [SerializeField] private float wallPadding = 0.12f;
    [Tooltip("벽에서 벗어날 때 다시 펴지는 속도 (m/초). 벽에 가까워질 땐 즉시 당김")]
    [SerializeField] private float extendSpeed = 5f;
    [Tooltip("머리 높이 흔들림(헤드밥) 거르기 — 클수록 빨리 따라감")]
    [SerializeField] private float heightFollow = 10f;

    [Header("흔들림")]
    [Tooltip("1인칭 피격 반동·흔들림을 3인칭 카메라에도 이 배율로 (0 = 끔)")]
    [Range(0f, 2f)][SerializeField] private float shakeScale = 0.8f;

    [Header("3인칭에서 보이게")]
    [Tooltip("1인칭 카메라에서 빼둔 내 몸·팔 레이어 (3인칭 카메라는 이 레이어도 그림)")]
    [SerializeField] private LayerMask extraVisibleLayers;
    [Tooltip("본인에게 그림자만 보이던(숨겨둔) 렌더러 — 3인칭 동안 보이게")]
    [SerializeField] private Renderer[] showInThirdPerson;
    [Tooltip("1인칭 전용 오브젝트 — 3인칭 동안 끔")]
    [SerializeField] private GameObject[] hideInThirdPerson;

    /// <summary>3인칭 카메라로 보는 중 (전환 중 포함)</summary>
    public bool IsThirdPerson => _camOn;

    /// <summary>true = 3인칭 시작 (빠지기 시작), false = 1인칭으로 완전히 돌아옴</summary>
    public event Action<bool> Changed;

    private readonly List<(object owner, ThirdPersonSettings s)> _requests = new();
    private ThirdPersonSettings _last = new();

    private Camera _tp;
    private FPSCamera _fps;
    private bool _camOn;
    private float _w;           // 0 = 1인칭, 1 = 3인칭
    private float _dist;
    private float _originY;

    private readonly List<Camera> _disabled = new();
    private readonly List<(Canvas canvas, Camera cam)> _canvases = new();
    private int _uiLayer = -1;
    private readonly List<(Renderer r, UnityEngine.Rendering.ShadowCastingMode mode, bool enabled)> _shown = new();
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    void Awake()
    {
        if (fpCamera == null)
        {
            var fps = GetComponentInChildren<FPSCamera>(true);
            fpCamera = fps != null ? fps.GetComponentInChildren<Camera>(true) : GetComponentInChildren<Camera>(true);
        }
        _fps = GetComponentInChildren<FPSCamera>(true);
        if (pivot == null) pivot = FindDeep(transform, "CameraRoot");
        if (pivot == null && fpCamera != null) pivot = fpCamera.transform.parent;
    }

    void OnDisable() => ForceFirstPerson();

    void OnDestroy()
    {
        ForceFirstPerson();
        if (_tp != null) Destroy(_tp.gameObject);
    }

    // ══════════════════ 요청 ══════════════════

    public void Enter(object owner, ThirdPersonSettings s)
    {
        if (owner == null || s == null) return;
        _requests.RemoveAll(r => r.owner == owner);
        _requests.Add((owner, s));
    }

    public void Exit(object owner) => _requests.RemoveAll(r => r.owner == owner);

    /// <summary>즉시 1인칭 (사망·라운드 리셋 등)</summary>
    public void ForceFirstPerson()
    {
        _requests.Clear();
        _w = 0f;
        if (_camOn) TurnOff();
    }

    // ══════════════════ 매 프레임 ══════════════════

    void LateUpdate()
    {
        if (fpCamera == null || pivot == null) return;

        bool want = _requests.Count > 0;
        if (want) _last = _requests[^1].s;
        var s = _last;

        float time = want ? s.BlendIn : s.BlendOut;
        _w = Mathf.MoveTowards(_w, want ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, time));

        if (_w <= 0f)
        {
            if (_camOn) TurnOff();
            return;
        }

        var fpT = fpCamera.transform;
        Vector3 fpPos = fpT.position;
        Quaternion fpRot = fpT.rotation;

        // 기준점 — 수평은 CameraRoot, 높이는 1인칭 카메라 높이를 부드럽게 (헤드밥 제거, 탈것 높이는 따라감)
        _originY = _camOn ? Mathf.Lerp(_originY, fpPos.y, 1f - Mathf.Exp(-heightFollow * Time.deltaTime)) : fpPos.y;
        Vector3 origin = new(pivot.position.x, _originY + s.PivotHeight, pivot.position.z);

        Quaternion look = pivot.rotation * Quaternion.Euler(s.PitchOffset, 0f, 0f);
        Quaternion shake = _fps != null && shakeScale > 0f ? Quaternion.Euler(_fps.ShakeEuler * shakeScale) : Quaternion.identity;
        Vector3 wantPos = origin + look * s.Offset;
        Vector3 delta = wantPos - origin;
        float full = delta.magnitude;
        Vector3 dir = full > 0.0001f ? delta / full : -(look * Vector3.forward);

        float allowed = Cast(origin, dir, full);

        if (!_camOn)
        {
            TurnOn();
            _dist = allowed;
        }
        else
        {
            // 벽에 가까워지면 즉시, 멀어지면 천천히
            _dist = allowed < _dist ? allowed : Mathf.MoveTowards(_dist, allowed, extendSpeed * Time.deltaTime);
        }

        Vector3 tpPos = origin + dir * _dist;

        float e = Mathf.SmoothStep(0f, 1f, _w);
        var t = _tp.transform;
        // 위치는 흔들림 없이 (벽 판정 그대로), 시선만 흔들림 — 1인칭 카메라는 이미 흔들리고 있음
        t.SetPositionAndRotation(Vector3.Lerp(fpPos, tpPos, e), Quaternion.Slerp(fpRot, look * shake, e));

        _tp.fieldOfView = fpCamera.fieldOfView + s.Fov * e;
        _tp.nearClipPlane = fpCamera.nearClipPlane;
    }

    float Cast(Vector3 origin, Vector3 dir, float dist)
    {
        if (dist <= 0.0001f) return 0f;

        int n = Physics.SphereCastNonAlloc(origin, probeRadius, dir, _hits, dist + wallPadding,
                                           obstacleMask, QueryTriggerInteraction.Ignore);
        float best = dist;
        Transform root = transform.root;

        for (int i = 0; i < n; i++)
        {
            var h = _hits[i];
            if (h.collider == null || h.collider.transform.root == root) continue;

            // 시작부터 겹쳐 있으면 거리 0 — 머리 바로 옆까지
            float d = h.distance <= 0f && h.point == Vector3.zero ? 0f : h.distance - wallPadding;
            if (d < best) best = d;
        }
        return Mathf.Max(0f, best);
    }

    // ══════════════════ 카메라 교체 ══════════════════

    void TurnOn()
    {
        if (_tp == null) CreateCamera();

        // 1인칭 카메라(팔 오버레이 포함) 끄기 — UI 오버레이 카메라는 남김
        if (_uiLayer < 0) _uiLayer = LayerMask.NameToLayer("UI");
        _disabled.Clear();
        foreach (var c in GetComponentsInChildren<Camera>(true))
            if (c != _tp && c.enabled && !IsUiCamera(c)) { c.enabled = false; _disabled.Add(c); }

        _tp.cullingMask = fpCamera.cullingMask | extraVisibleLayers;

        // URP 카메라 스택 (UI 오버레이 등)을 3인칭 카메라로 옮김 — 끈 카메라(팔)는 빼고
        var src = fpCamera.GetUniversalAdditionalCameraData();
        var dst = _tp.GetUniversalAdditionalCameraData();
        if (dst != null && dst.cameraStack != null)
        {
            dst.cameraStack.Clear();
            if (src != null && src.cameraStack != null)
                foreach (var c in src.cameraStack)
                    if (c != null && !_disabled.Contains(c)) dst.cameraStack.Add(c);
        }

        // 'Screen Space - Camera' 캔버스가 1인칭 카메라를 보고 있으면 3인칭 카메라로
        _canvases.Clear();
        foreach (var cv in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cv.isRootCanvas && cv.renderMode == RenderMode.ScreenSpaceCamera &&
                cv.worldCamera != null && (cv.worldCamera == fpCamera || _disabled.Contains(cv.worldCamera)))
            {
                _canvases.Add((cv, cv.worldCamera));
                cv.worldCamera = _tp;
            }
        _tp.gameObject.SetActive(true);
        _tp.enabled = true;

        _shown.Clear();
        if (showInThirdPerson != null)
            foreach (var r in showInThirdPerson)
                if (r != null)
                {
                    _shown.Add((r, r.shadowCastingMode, r.enabled));
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.enabled = true;
                }

        if (hideInThirdPerson != null)
            foreach (var go in hideInThirdPerson) if (go != null) go.SetActive(false);

        _camOn = true;
        Changed?.Invoke(true);
    }

    void TurnOff()
    {
        _camOn = false;

        foreach (var c in _disabled) if (c != null) c.enabled = true;
        _disabled.Clear();

        foreach (var (cv, cam) in _canvases) if (cv != null) cv.worldCamera = cam;
        _canvases.Clear();

        if (_tp != null) _tp.gameObject.SetActive(false);

        // 사망 중 숨김 등 그 사이 바뀐 상태는 덮지 않게 — 우리가 켠 것만 되돌림
        foreach (var (r, mode, enabled) in _shown)
            if (r != null) { r.shadowCastingMode = mode; r.enabled = enabled && r.enabled; }
        _shown.Clear();

        if (hideInThirdPerson != null)
            foreach (var go in hideInThirdPerson) if (go != null) go.SetActive(true);

        Changed?.Invoke(false);
    }

    void CreateCamera()
    {
        var go = new GameObject("ThirdPersonCamera");
        go.tag = fpCamera.tag;                      // Camera.main이 3인칭 카메라를 잡도록
        _tp = go.AddComponent<Camera>();
        _tp.CopyFrom(fpCamera);
        _tp.depth = fpCamera.depth;

        // URP 설정 (후처리 등)
        var src = fpCamera.GetUniversalAdditionalCameraData();
        var dst = _tp.GetUniversalAdditionalCameraData();
        if (src != null && dst != null)
        {
            dst.renderType = CameraRenderType.Base;
            dst.renderPostProcessing = src.renderPostProcessing;
            dst.antialiasing = src.antialiasing;
            dst.antialiasingQuality = src.antialiasingQuality;
            dst.renderShadows = src.renderShadows;
            dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = _tp.transform;
            dst.dithering = src.dithering;
        }

        go.SetActive(false);
    }

    /// <summary>UI만 그리는 오버레이 카메라 — 3인칭에서도 그대로 둠</summary>
    bool IsUiCamera(Camera c)
    {
        if (c == fpCamera || _uiLayer < 0) return false;
        bool drawsUi = (c.cullingMask & (1 << _uiLayer)) != 0;
        bool onlyUi = c.cullingMask == (1 << _uiLayer);
        var data = c.GetUniversalAdditionalCameraData();
        bool overlay = data != null && data.renderType == CameraRenderType.Overlay;
        return onlyUi || (overlay && drawsUi && (c.cullingMask & ~(1 << _uiLayer)) == 0);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}