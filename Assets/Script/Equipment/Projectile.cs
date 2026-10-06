using UnityEngine;
using System.Collections.Generic;

/// <summary>네트워크를 모르는 순수 투사체. 판정은 발사자 클라이언트만 수행.</summary>
public class Projectile : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("진행 방향으로 회전")]
    [SerializeField] private bool alignToVelocity = true;
    [Tooltip("적중 시 생성")]
    [SerializeField] private GameObject hitEffect;
    [Tooltip("지형 충돌 시 남는 시간 (0이면 즉시 제거)")]
    [SerializeField] private float stickTime = 0f;

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

            Stop(h.point, h.normal);
            return;
        }
    }

    void Stop(Vector3 point, Vector3 normal)
    {
        _dead = true;
        transform.position = point;

        if (hitEffect != null)
            Instantiate(hitEffect, point, Quaternion.LookRotation(normal));

        if (stickTime > 0f)
        {
            _stickTimer = stickTime;
            transform.rotation = Quaternion.LookRotation(-normal);
        }
        else Destroy(gameObject);
    }

    /// <summary>판정 없이 그림만 그리는 원격용 — 수명만 관리</summary>
    //public void SetVisualOnly() => _doHitCheck = false;

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