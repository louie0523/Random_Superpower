using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// HUD 왼쪽 아래 얼굴 칸 — 내 캐릭터 머리를 작은 카메라로 찍어서 보여 줌 (RenderTexture).
/// UIManager가 플레이어를 붙일 때 SetSubject(플레이어). 머리를 못 찾거나 플레이어가 없으면 스티커 그림 그대로.
/// 머리 찾는 순서: HeadZone 표시 → 휴머노이드 Head 뼈 → 이름에 'head'가 들어간 자식
/// 1인칭에서 내 몸을 숨기는 방식(레이어 · 그림자만)에 따라 안 보일 수 있음 → Culling Mask로 그 레이어를 넣어 주기
/// </summary>
public class HudPortrait : MonoBehaviour
{
    [Tooltip("찍은 그림이 들어갈 칸")]
    public RawImage Target;
    [Tooltip("찍은 그림 묶음 (평소엔 꺼짐 — 머리를 찾으면 켜짐)")]
    public GameObject LiveRoot;

    [Header("카메라")]
    [Tooltip("그릴 레이어 — 내 캐릭터 레이어만 넣으면 뒤 배경 없이 얼굴만")]
    public LayerMask CullingMask = ~0;
    public int Size = 256;
    [Tooltip("머리 앞 거리 (m)")]
    public float Distance = 0.6f;
    [Tooltip("머리 기준 높이 (m)")]
    public float Height = 0.03f;
    public float Fov = 24f;
    [Tooltip("이보다 먼 건 안 그림 (뒤 벽 등)")]
    public float Far = 2.5f;
    public Color Background = new(0.16f, 0.19f, 0.22f, 1f);
    [Tooltip("머리 뼈 이름에 들어가는 글자 (소문자)")]
    public string HeadName = "head";

    static Transform _subject;
    static int _ver;

    /// <summary>얼굴을 찍을 플레이어 (null = 끔)</summary>
    public static void SetSubject(Transform player)
    {
        _subject = player;
        _ver++;
    }

    Camera _cam;
    RenderTexture _rt;
    Transform _head;
    int _seen = -1;

    void OnEnable()
    {
        _seen = -1;
        Show(false);
    }

    void OnDisable()
    {
        if (_cam != null) _cam.enabled = false;
    }

    void OnDestroy()
    {
        if (_cam != null) Destroy(_cam.gameObject);
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    void LateUpdate()
    {
        if (_seen != _ver)
        {
            _seen = _ver;
            _head = FindHead(_subject);
        }

        if (_subject == null || _head == null || !_head.gameObject.activeInHierarchy)
        {
            if (_cam != null) _cam.enabled = false;
            Show(false);
            return;
        }

        Ensure();
        _cam.enabled = true;

        Vector3 fwd = _subject.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 look = _head.position + Vector3.up * Height;
        _cam.transform.SetPositionAndRotation(look + fwd * Distance, Quaternion.LookRotation(-fwd, Vector3.up));
        Show(true);
    }

    void Ensure()
    {
        if (_rt == null)
        {
            _rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32) { name = "HudPortrait" };
            _rt.Create();
        }
        if (_cam == null)
        {
            var go = new GameObject("HudPortraitCamera") { hideFlags = HideFlags.DontSave };
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Background;
            _cam.cullingMask = CullingMask;
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = Far;
            _cam.targetTexture = _rt;
            _cam.depth = -50f;
            var data = _cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
            }
        }
        if (Target != null && Target.texture != _rt) Target.texture = _rt;
    }

    void Show(bool on)
    {
        if (LiveRoot != null && LiveRoot.activeSelf != on) LiveRoot.SetActive(on);
    }

    Transform FindHead(Transform root)
    {
        if (root == null) return null;

        var hz = root.GetComponentInChildren<HeadZone>(true);
        if (hz != null) return hz.transform;

        var an = root.GetComponentInChildren<Animator>(true);
        if (an != null && an.isHuman)
        {
            var h = an.GetBoneTransform(HumanBodyBones.Head);
            if (h != null) return h;
        }

        string key = string.IsNullOrEmpty(HeadName) ? "head" : HeadName.ToLowerInvariant();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name.ToLowerInvariant().Contains(key)) return t;
        return null;
    }
}
