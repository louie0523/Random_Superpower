using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 본인 화면 암흑 시야 — 내 몸 둘레 반경 N m만 보이고 그 밖은 어둠 속으로 흐려짐.
///   암전(Blind 상태이상): 값 = 보이는 반경 (m, 최소 1). 여러 개면 작은 값
///   그림자 영역 등: ScreenDarkness.PushVision(반경)을 매 프레임 부르면 그동안 적용 (안 부르면 풀림)
/// 둘 다 있으면 작은 쪽. 처음 쓸 때 스스로 생김 — 씬에 둘 것 없음.
/// 셰이더: Vision/Resources/VisionDarkness.shader (Resources 폴더 안에 있어야 빌드에 포함). 게임 카메라의 Depth Texture는 자동으로 켬.
/// </summary>
public class ScreenDarkness : MonoBehaviour
{
    static ScreenDarkness _inst;

    [Tooltip("어둠 색 (a = 최대 불투명도 — 1이면 반경 밖이 완전히 안 보임)")]
    public Color DarkColor = new(0f, 0f, 0f, 1f);
    [Tooltip("또렷한 반경 밖에서 완전한 어둠까지 넘어가는 폭 = 반경 × 이 값 (최소 FadeMin, 최대 FadeMax)")]
    public float FadeRatio = 0.25f;
    public float FadeMin = 0.3f;
    public float FadeMax = 1f;
    [Tooltip("켜지고 꺼지는 속도 — 클수록 빨리 조여 옴")]
    public float FadeSpeed = 7f;
    [Tooltip("시작할 때 어둠이 이 반경에서 조여 들어옴 (m)")]
    public float OpenRadius = 30f;
    [Tooltip("몸 선분 — 발 위 / 머리 높이 (m)")]
    public float BodyLow = 0.15f, BodyHigh = 1.7f;

    static readonly int IdA = Shader.PropertyToID("_VisionA");
    static readonly int IdB = Shader.PropertyToID("_VisionB");
    static readonly int IdRadius = Shader.PropertyToID("_VisionRadius");
    static readonly int IdFade = Shader.PropertyToID("_VisionFade");
    static readonly int IdStrength = Shader.PropertyToID("_VisionStrength");
    static readonly int IdColor = Shader.PropertyToID("_VisionColor");

    private Material _mat;
    private Mesh _tri;
    private float _push = float.PositiveInfinity;
    private float _radius, _strength;
    private Camera _cam;
    private float _camTimer;
    private bool _warned;

    // ══════════════════ 외부에서 ══════════════════

    /// <summary>이번 프레임 시야를 radius(m)로 — 매 프레임 불러야 유지 (그림자 영역 등)</summary>
    public static void PushVision(float radius)
    {
        if (!Application.isPlaying) return;
        Ensure();
        _inst._push = Mathf.Min(_inst._push, Mathf.Max(PlayerStatus.MinBlindRadius, radius));
    }

    /// <summary>예전 이름 — 세기 대신 기본 3m 시야</summary>
    public static void PushZone(float strength)
    {
        if (strength > 0.01f) PushVision(3f);
    }

    /// <summary>지금 시야가 좁아져 있는지</summary>
    public static bool Limited => _inst != null && _inst._strength > 0.5f;

    /// <summary>이 위치가 지금 보이는지 (머리 위 이름표 등 — 어둠 속이면 숨기기)</summary>
    public static bool CanSee(Vector3 worldPos)
    {
        if (!Limited) return true;
        var local = AbilityDescTokens.Local;
        if (local == null) return true;
        _inst.Segment(local, out var a, out var b);
        float d = Vector3.Distance(worldPos, ClosestOnSegment(a, b, worldPos));
        return d <= _inst._radius + _inst.Fade(_inst._radius) * 0.5f;
    }

    // ══════════════════ 생성 ══════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Ensure()
    {
        if (_inst != null) return;
        var go = new GameObject("ScreenDarkness", typeof(ScreenDarkness));
        DontDestroyOnLoad(go);
        _inst = go.GetComponent<ScreenDarkness>();
    }

    void Awake()
    {
        _radius = OpenRadius;

        var sh = Resources.Load<Shader>("VisionDarkness");
        if (sh == null) sh = Shader.Find("Hidden/Game/VisionDarkness");
        if (sh != null) _mat = new Material(sh) { name = "VisionDarkness (instance)", hideFlags = HideFlags.HideAndDontSave };

        // 화면 전체를 덮는 삼각형 (꼭짓점 = 화면 좌표)
        _tri = new Mesh { name = "VisionFullscreenTri", hideFlags = HideFlags.HideAndDontSave };
        _tri.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(3f, -1f, 0f), new Vector3(-1f, 3f, 0f) };
        _tri.triangles = new[] { 0, 1, 2 };
        _tri.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_tri != null) Destroy(_tri);
    }

    private Component _alterOf;
    private PlayerAlter _alter;

    /// <summary>내 몸 선분 — 탈것(변화 Lift)에 올라 있으면 그만큼 위로 늘림</summary>
    void Segment(Component local, out Vector3 a, out Vector3 b)
    {
        if (_alterOf != local) { _alterOf = local; _alter = local.GetComponent<PlayerAlter>(); }
        float lift = _alter != null ? _alter.Lift : 0f;
        Vector3 p = local.transform.position;
        a = p + Vector3.up * BodyLow;
        b = p + Vector3.up * (BodyHigh + lift);
    }

    float Fade(float r) => Mathf.Clamp(r * FadeRatio, FadeMin, FadeMax);

    // ══════════════════ 매 프레임 ══════════════════

    void LateUpdate()
    {
        var local = AbilityDescTokens.Local;
        var st = local != null ? local.Status : null;

        // 목표 반경 — 암전 값 · 영역 중 작은 것
        float want = _push;
        _push = float.PositiveInfinity;
        if (st != null && st.Has(StatusType.Blind)) want = Mathf.Min(want, Mathf.Max(PlayerStatus.MinBlindRadius, st.Get(StatusType.Blind)));
        if (local == null) want = float.PositiveInfinity;

        bool on = !float.IsInfinity(want);
        float k = 1f - Mathf.Exp(-FadeSpeed * Time.unscaledDeltaTime);
        _strength = Mathf.Lerp(_strength, on ? 1f : 0f, k);
        _radius = Mathf.Lerp(_radius, on ? want : OpenRadius, k);       // 켜질 땐 멀리서 조여 오고, 꺼질 땐 넓어지며 사라짐
        if (!on && _strength < 0.003f) { _strength = 0f; _radius = OpenRadius; }

        if (_strength <= 0f || _mat == null)
        {
            if (on && _mat == null && !_warned)
            {
                _warned = true;
                Debug.LogWarning("[ScreenDarkness] VisionDarkness 셰이더를 못 찾음 — VisionDarkness.shader를 Resources 폴더에 두세요");
            }
            return;
        }

        var cam = GameCamera();
        if (cam == null) return;

        Segment(local, out var segA, out var segB);
        Shader.SetGlobalVector(IdA, segA);
        Shader.SetGlobalVector(IdB, segB);
        Shader.SetGlobalFloat(IdRadius, _radius);
        Shader.SetGlobalFloat(IdFade, Fade(_radius));
        Shader.SetGlobalFloat(IdStrength, _strength);
        Shader.SetGlobalColor(IdColor, DarkColor);

        var rp = new RenderParams(_mat)
        {
            camera = cam,                                                       // 게임 카메라에만 (씬 뷰 · 미니맵 X)
            worldBounds = new Bounds(cam.transform.position, Vector3.one * 1e5f),
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            layer = FirstLayer(cam.cullingMask),
        };
        Graphics.RenderMesh(rp, _tri, 0, Matrix4x4.identity);
    }

    /// <summary>화면을 그리는 기본(Base) 카메라 — 깊이 텍스처를 켜 둠</summary>
    Camera GameCamera()
    {
        _camTimer -= Time.unscaledDeltaTime;
        if (_cam != null && _cam.isActiveAndEnabled && _camTimer > 0f) return _cam;
        _camTimer = 0.5f;

        Camera best = null;
        foreach (var c in Camera.allCameras)
        {
            if (c == null || !c.isActiveAndEnabled || c.targetTexture != null) continue;
            var data = c.GetUniversalAdditionalCameraData();
            if (data != null && data.renderType != CameraRenderType.Base) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        _cam = best != null ? best : Camera.main;

        if (_cam != null)
        {
            var d = _cam.GetUniversalAdditionalCameraData();
            if (d != null && !d.requiresDepthTexture) d.requiresDepthTexture = true;
        }
        return _cam;
    }

    static int FirstLayer(int mask)
    {
        if ((mask & 1) != 0) return 0;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }

    static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
        return a + ab * t;
    }
}