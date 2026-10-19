using UnityEngine;
using System.Collections.Generic;

/// <summary>네트워크를 모르는 순수 투사체. 피해는 발사자 클라이언트만, 멈추는 판정은 모두가 같게.</summary>
public class Projectile : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("진행 방향으로 회전")]
    [SerializeField] private bool alignToVelocity = true;
    [Tooltip("적중 시 생성")]
    [SerializeField] private GameObject hitEffect;

    [Header("박히기")]
    [Tooltip("박힌 채 남는 시간 (0이면 즉시 제거)")]
    [SerializeField] private float stickTime = 0f;

    [Tooltip("모델 피벗에서 화살촉까지 앞쪽(+Z) 거리 (m). 촉이 표면에 닿게 위치를 맞춤.\n" +
             "피벗이 촉에 있으면 0, 화살 가운데면 길이의 절반")]
    [SerializeField] private float tipOffset = 0f;

    [Tooltip("표면 안으로 파고드는 깊이 (m)")]
    [SerializeField] private float embedDepth = 0.12f;

    [Tooltip("0 = 날아온 각도 그대로 박힘 (자연스러움)\n1 = 표면에 수직으로 박힘")]
    [Range(0f, 1f)][SerializeField] private float alignToSurface = 0f;

    [Tooltip("박힐 때 각도를 살짝 흔들기 (도). 여러 발이 똑같이 꽂혀 보이지 않게")]
    [SerializeField] private float stickJitter = 3f;

    [Tooltip("움직이는 대상(플레이어 등)에 박히면 따라다님")]
    [SerializeField] private bool stickToTargets = true;

    private Vector3 _vel;
    private float _gravity;
    private float _life;
    private bool _doHitCheck;
    private LayerMask _hitMask;

    private int _pierceLeft;
    private float _falloff;
    private float _damageMul = 1f;

    private System.Action<Collider, Vector3, float> _onHit;
    private readonly HashSet<int> _hitIds = new();

    private bool _dead;
    private float _stickTimer;

    private Collider _ignore;

    [Header("Hit")]
    [Tooltip("판정 반경 (m). 크면 잘 맞고 작으면 정밀해짐")]
    [SerializeField] private float hitRadius = 0.08f;

    public void Launch(
        Vector3 pos, Vector3 dir, float speed, float gravity, float lifetime,
        bool doHitCheck, LayerMask hitMask,
        int pierce, float falloff, Collider ignore,
        System.Action<Collider, Vector3, float> onHit)
    {
        transform.position = pos;
        _vel = dir.normalized * speed;
        _gravity = gravity;
        _life = lifetime;
        _doHitCheck = doHitCheck;
        _hitMask = hitMask;
        _pierceLeft = pierce;
        _falloff = falloff;
        _ignore = ignore;
        _onHit = onHit;

        _damageMul = 1f;
        _dead = false;
        _hitIds.Clear();

        if (alignToVelocity && _vel.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(_vel);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (_dead)
        {
            _stickTimer -= dt;
            if (_stickTimer <= 0f) Destroy(gameObject);
            return;
        }

        _life -= dt;
        if (_life <= 0f) { Destroy(gameObject); return; }

        Vector3 prev = transform.position;

        _vel += Vector3.down * (_gravity * dt);
        Vector3 next = prev + _vel * dt;

#if UNITY_EDITOR
        _lastFrom = prev;
        _lastTo = next;
#endif

        if (_doHitCheck) CheckHit(prev, next);

        if (_dead) return;

        transform.position = next;

        if (alignToVelocity && _vel.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(_vel);
    }

    void CheckHit(Vector3 from, Vector3 to)
    {
        Vector3 seg = to - from;
        float dist = seg.magnitude;
        if (dist < 0.0001f) return;

        // 프레임 사이를 뚫지 않도록 구체로 훑는다
        var hits = Physics.SphereCastAll(
            from, hitRadius, seg / dist, dist, _hitMask, QueryTriggerInteraction.Ignore);

        if (hits.Length == 0) return;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
            if (h.collider == _ignore) continue;
            if (_ignore != null && h.collider.transform.root == _ignore.transform.root) continue;   // 추가

            int id = h.collider.gameObject.GetInstanceID();
            if (_hitIds.Contains(id)) continue;
            _hitIds.Add(id);

            _onHit?.Invoke(h.collider, h.point, _damageMul);

            if (_pierceLeft > 0)
            {
                _pierceLeft--;
                _damageMul *= (1f - _falloff);
                continue;
            }

            // 시작부터 겹쳐 있으면 point가 (0,0,0) — 이번 프레임 시작 위치로 대신
            Vector3 point = h.distance <= 0f && h.point == Vector3.zero ? from : h.point;
            Vector3 normal = h.normal.sqrMagnitude > 0.001f ? h.normal : -seg / dist;

            Stop(point, normal, h.collider);
            return;
        }
    }

    void Stop(Vector3 point, Vector3 normal, Collider hitCol)
    {
        _dead = true;

        if (hitEffect != null)
            Instantiate(hitEffect, point, Quaternion.LookRotation(normal));

        if (stickTime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        _stickTimer = stickTime;

        // ── 박히는 방향: 날아온 방향(포물선 끝 기울기 포함) ↔ 표면 수직 사이 ──
        Vector3 flight = _vel.sqrMagnitude > 0.001f ? _vel.normalized : transform.forward;
        Vector3 dir = Vector3.Slerp(flight, -normal, alignToSurface).normalized;

        // 표면 뒤쪽을 향하지 않게 (스치듯 맞았을 때 표면을 뚫고 튀어나오는 것 방지)
        if (Vector3.Dot(dir, -normal) < 0.15f)
            dir = Vector3.Slerp(dir, -normal, 0.35f).normalized;

        var rot = Quaternion.LookRotation(dir);
        if (stickJitter > 0f)
            rot *= Quaternion.Euler(Random.Range(-stickJitter, stickJitter),
                                    Random.Range(-stickJitter, stickJitter),
                                    Random.Range(0f, 360f));   // 축 회전은 깃털 방향만 바뀜

        // ── 위치: 촉이 표면에서 embedDepth만큼 들어가게 ──
        Vector3 fwd = rot * Vector3.forward;
        transform.SetPositionAndRotation(point + fwd * (embedDepth - tipOffset), rot);

        // ── 움직이는 대상이면 따라다님 ──
        if (stickToTargets && hitCol != null && !hitCol.gameObject.isStatic)
        {
            var rb = hitCol.attachedRigidbody;
            Transform parent = rb != null ? rb.transform : hitCol.transform;

            // 비균등 스케일 부모 아래에선 모델이 찌그러지므로 그럴 땐 붙이지 않음
            Vector3 s = parent.lossyScale;
            bool uniform = Mathf.Approximately(s.x, s.y) && Mathf.Approximately(s.y, s.z);
            if (uniform) transform.SetParent(parent, true);
        }
    }

    /// <summary>판정 없이 그림만 그리는 원격용 — 수명만 관리 (지금은 원격도 판정함, 호환용)</summary>
    public void SetVisualOnly() => _doHitCheck = false;

#if UNITY_EDITOR
    [Header("Debug")]
    [Tooltip("씬 뷰에 판정 범위 표시")]
    [SerializeField] private bool drawGizmos = true;

    private Vector3 _lastFrom, _lastTo;

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // 현재 위치의 판정 구체
        Gizmos.color = _dead
            ? new Color(0.4f, 0.4f, 0.4f, 0.4f)
            : new Color(1f, 0.85f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, hitRadius);

        // 화살촉 위치 (tipOffset 맞추기용)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * tipOffset, 0.015f);

        if (!Application.isPlaying) return;

        // 이번 프레임에 훑은 구간
        if (_lastFrom != _lastTo)
        {
            Gizmos.color = _doHitCheck
                ? new Color(1f, 0.3f, 0.1f, 0.8f)
                : new Color(0.3f, 0.6f, 1f, 0.4f);

            Gizmos.DrawLine(_lastFrom, _lastTo);
            Gizmos.DrawWireSphere(_lastFrom, hitRadius);
            Gizmos.DrawWireSphere(_lastTo, hitRadius);
        }

        // 속도 벡터
        if (!_dead && _vel.sqrMagnitude > 0.01f)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.7f);
            Gizmos.DrawRay(transform.position, _vel * 0.1f);
        }
    }
#endif
}