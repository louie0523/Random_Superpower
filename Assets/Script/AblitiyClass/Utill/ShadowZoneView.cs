using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 완전한 암흑 — 그림자 영역 연출 (모든 클라이언트가 각자 만듦).
///   밖: 검은 돔이 보여서 안이 안 보임 (돔은 바깥면만 그려져 안에선 안 보임)
///   안 (쉐도우 워커 제외): 있는 동안 계속 암흑 시야 — 내 둘레 visionRadius(m)만 보임
///   쉐도우 워커 본인: 영역 안 적 머리 위에 위치 표시
/// </summary>
public class ShadowZoneView : MonoBehaviour
{
    private Vector3 _center;
    private float _radius, _end, _start;
    private float _visionRadius = 2f;
    private Transform _owner;
    private bool _ownerIsLocal;
    private GameObject _dome;
    private GameObject _markerPrefab;
    private LayerMask _playerMask;
    private readonly Dictionary<PlayerHealth, GameObject> _markers = new();
    private readonly Collider[] _hits = new Collider[32];

    const float FadeTime = 0.35f;

    public static ShadowZoneView Create(Vector3 center, float radius, float duration, Transform owner, bool ownerIsLocal,
                                        GameObject domePrefab, GameObject markerPrefab, LayerMask playerMask,
                                        float visionRadius = 2f)
    {
        var go = new GameObject("ShadowZone");
        var v = go.AddComponent<ShadowZoneView>();
        v._center = center;
        v._radius = radius;
        v._start = Time.time;
        v._end = Time.time + duration;
        v._owner = owner;
        v._ownerIsLocal = ownerIsLocal;
        v._markerPrefab = markerPrefab;
        v._playerMask = playerMask;
        v._visionRadius = Mathf.Max(PlayerStatus.MinBlindRadius, visionRadius);
        go.transform.position = center;

        if (domePrefab != null)
        {
            v._dome = Instantiate(domePrefab, center, Quaternion.identity, go.transform);
        }
        else
        {
            // 프리팹이 없으면 검은 구 — 기본 머티리얼 색만 바꿈
            v._dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(v._dome.GetComponent<Collider>());
            v._dome.transform.SetParent(go.transform, false);
            var r = v._dome.GetComponent<Renderer>();
            r.material.color = new Color(0.02f, 0.01f, 0.04f, 1f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach (var c in v._dome.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        v._dome.transform.localScale = Vector3.zero;
        return v;
    }

    void Update()
    {
        float t = Time.time;
        if (t >= _end) { Destroy(gameObject); return; }

        // 커졌다가 끝에 줄어듦
        float grow = Mathf.Clamp01((t - _start) / FadeTime);
        float shrink = Mathf.Clamp01((_end - t) / FadeTime);
        float s = Mathf.SmoothStep(0f, 1f, Mathf.Min(grow, shrink));
        if (_dome != null) _dome.transform.localScale = Vector3.one * (_radius * 2f * s);

        var local = AbilityDescTokens.Local;
        if (local == null) return;

        if (_ownerIsLocal)
        {
            UpdateMarkers();
            return;
        }

        // 다른 사람이 영역 안이면 있는 동안 계속 암흑 시야
        Vector3 p = local.transform.position;
        float d = Vector3.Distance(new Vector3(p.x, _center.y, p.z), _center);
        if (d < _radius * s) ScreenDarkness.PushVision(_visionRadius);
    }

    void UpdateMarkers()
    {
        var inside = new HashSet<PlayerHealth>();
        int n = Physics.OverlapSphereNonAlloc(_center, _radius, _hits, _playerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (h == null || h.IsDead) continue;
            if (_owner != null && h.transform.root == _owner.root) continue;
            inside.Add(h);
        }

        // 빠진 사람 표시 지우기
        var gone = new List<PlayerHealth>();
        foreach (var kv in _markers) if (!inside.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var h in gone) { if (_markers[h] != null) Destroy(_markers[h]); _markers.Remove(h); }

        // 영역 안 적 — 벽 너머로도 아웃라인 (PlayerOutline이 있으면)
        foreach (var h in inside) PlayerOutline.RequestXRay(h.gameObject, 0.3f);

        if (_markerPrefab == null) return;
        foreach (var h in inside)
        {
            if (!_markers.TryGetValue(h, out var m) || m == null)
            {
                m = Instantiate(_markerPrefab);
                _markers[h] = m;
            }
            m.transform.position = h.transform.position + Vector3.up * 2.4f;
        }
    }

    void OnDestroy()
    {
        foreach (var m in _markers.Values) if (m != null) Destroy(m);
        _markers.Clear();
    }

    /// <summary>이 위치가 영역 안인가 (높이 무시)</summary>
    public static bool Inside(Vector3 center, float radius, Vector3 p) =>
        new Vector2(p.x - center.x, p.z - center.z).sqrMagnitude < radius * radius;
}