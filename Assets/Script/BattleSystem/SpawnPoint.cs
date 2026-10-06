using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 부활·시작 지점. 씬에 빈 오브젝트로 여러 개 배치 (파란 기둥 기즈모, 화살표 = 바라볼 방향).
/// 고를 때 — 반경 안에 살아 있는 적이 없는 지점 중 랜덤, 그런 곳이 없으면 적에게서 가장 먼 3곳 중 랜덤.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    static readonly List<SpawnPoint> All = new();
    static readonly List<Vector3> _enemies = new();
    static readonly List<(SpawnPoint p, float d)> _scored = new();

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <param name="self">고르는 사람 — 적 목록에서 제외</param>
    /// <param name="safeRadius">이 거리 안에 적이 없으면 '안전'</param>
    public static bool TryPick(NetworkRunner runner, PlayerRef self, float safeRadius,
                               out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        if (All.Count == 0) return false;

        // 살아 있는 적 위치
        _enemies.Clear();
        if (runner != null)
            foreach (var p in runner.ActivePlayers)
            {
                if (p == self) continue;
                var obj = runner.GetPlayerObject(p);
                if (obj == null) continue;
                if (obj.TryGetComponent<PlayerHealth>(out var h) && h.IsDead) continue;
                _enemies.Add(obj.transform.position);
            }

        // 지점마다 가장 가까운 적까지 거리
        _scored.Clear();
        int safe = 0;
        foreach (var sp in All)
        {
            float nearest = float.MaxValue;
            foreach (var e in _enemies)
                nearest = Mathf.Min(nearest, Vector3.Distance(e, sp.transform.position));

            _scored.Add((sp, nearest));
            if (nearest >= safeRadius) safe++;
        }

        SpawnPoint pick = null;

        if (safe > 0)
        {
            int k = Random.Range(0, safe);
            foreach (var s in _scored)
            {
                if (s.d < safeRadius) continue;
                if (k == 0) { pick = s.p; break; }
                k--;
            }
        }
        else
        {
            _scored.Sort((a, b) => b.d.CompareTo(a.d));
            pick = _scored[Random.Range(0, Mathf.Min(3, _scored.Count))].p;
        }

        if (pick == null) return false;

        pos = pick.transform.position;
        rot = Quaternion.Euler(0f, pick.transform.eulerAngles.y, 0f);
        return true;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.8f);
        Vector3 p = transform.position;
        Gizmos.DrawWireSphere(p + Vector3.up * 0.5f, 0.5f);
        Gizmos.DrawLine(p, p + Vector3.up * 2f);
        Gizmos.DrawLine(p + Vector3.up, p + Vector3.up + transform.forward * 1.2f);
    }
}
