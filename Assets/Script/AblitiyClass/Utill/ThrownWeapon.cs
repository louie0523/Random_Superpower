using System;
using UnityEngine;

/// <summary>
/// 던져진 무기 복제체 (무구의 달인 '무희'의 근접 무기). 각 클라이언트가 각자 만듦.
/// 판정은 onHit이 있는 쪽(SA)만 — 다른 클라이언트는 같은 궤적의 연출만.
/// </summary>
public class ThrownWeapon : MonoBehaviour
{
    private Vector3 _dir;
    private float _speed, _left, _radius;
    private LayerMask _hitMask, _stopMask;
    private Transform _ownerRoot;
    private Action<PlayerHealth, Vector3> _onHit;
    private Transform _model;
    private float _spin;

    public static ThrownWeapon Launch(GameObject modelPrefab, Vector3 pos, Vector3 dir, float speed, float maxDist,
                                      float radius, LayerMask hitMask, LayerMask stopMask, Transform ownerRoot,
                                      Vector3 scale, Action<PlayerHealth, Vector3> onHit)
    {
        var go = new GameObject("ThrownWeapon");
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
        var tw = go.AddComponent<ThrownWeapon>();
        tw._dir = dir.normalized;
        tw._speed = Mathf.Max(1f, speed);
        tw._left = maxDist;
        tw._radius = radius;
        tw._hitMask = hitMask;
        tw._stopMask = stopMask;
        tw._ownerRoot = ownerRoot;
        tw._onHit = onHit;
        tw._spin = UnityEngine.Random.Range(900f, 1300f);

        if (modelPrefab != null)
        {
            var m = Instantiate(modelPrefab, go.transform);
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            m.transform.localScale = scale;
            foreach (var c in m.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in m.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            tw._model = m.transform;
        }

        Destroy(go, maxDist / tw._speed + 1f);
        return tw;
    }

    void Update()
    {
        float step = Mathf.Min(_speed * Time.deltaTime, _left);
        Vector3 p = transform.position;

        // 사람 먼저 — 판정하는 쪽만 피해, 연출 쪽도 그 자리에서 멈춤
        if (Physics.SphereCast(p, _radius, _dir, out var hit, step, _hitMask, QueryTriggerInteraction.Ignore)
            && (_ownerRoot == null || hit.collider.transform.root != _ownerRoot))
        {
            var h = hit.collider.GetComponentInParent<PlayerHealth>();
            if (h != null && !h.IsDead)
            {
                _onHit?.Invoke(h, hit.point);
                Destroy(gameObject);
                return;
            }
        }

        // 벽
        if (Physics.Raycast(p, _dir, out var wall, step, _stopMask, QueryTriggerInteraction.Ignore)
            && (_ownerRoot == null || wall.collider.transform.root != _ownerRoot)
            && wall.collider.GetComponentInParent<PlayerHealth>() == null)
        {
            transform.position = wall.point;
            Destroy(gameObject, 0.4f);
            enabled = false;
            return;
        }

        transform.position = p + _dir * step;
        _left -= step;
        if (_model != null) _model.Rotate(Vector3.right, _spin * Time.deltaTime, Space.Self);
        if (_left <= 0f) Destroy(gameObject);
    }
}
