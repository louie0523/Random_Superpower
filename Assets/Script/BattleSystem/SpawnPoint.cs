using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 부활·시작 지점. 씬에 빈 오브젝트로 여러 개 배치 (파란 캡슐 기즈모, 화살표 = 바라볼 방향).
/// 고를 때 — 반경 안에 살아 있는 적이 없는 지점 중 랜덤, 그런 곳이 없으면 적에게서 가장 먼 3곳 중 랜덤.
/// 위치는 지점 아래 바닥을 찾아, 캐릭터 크기(CharacterController)를 계산해 발이 정확히 바닥에 닿게.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    static readonly List<SpawnPoint> All = new();
    static readonly List<Vector3> _enemies = new();
    static readonly List<(SpawnPoint p, float d)> _scored = new();

    /// <summary>바닥으로 인정할 레이어 — 플레이어 레이어는 빼는 걸 권장</summary>
    public static LayerMask GroundMask = ~0;

    [Tooltip("이 지점 아래 바닥을 찾는 거리 (m)")]
    [SerializeField] private float probeDown = 10f;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <param name="self">고르는 사람 — 적 목록에서 제외</param>
    /// <param name="safeRadius">이 거리 안에 적이 없으면 '안전'</param>
    /// <param name="body">캐릭터 크기 계산용. 비우면 지점 위치 그대로</param>
    public static bool TryPick(NetworkRunner runner, PlayerRef self, float safeRadius,
                               out Vector3 pos, out Quaternion rot, CharacterController body = null)
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

        pos = GroundedPosition(pick.transform.position, body, pick.probeDown);
        rot = Quaternion.Euler(0f, pick.transform.eulerAngles.y, 0f);
        return true;
    }

    /// <summary>
    /// point 아래 바닥을 찾아, 캐릭터 발바닥이 바닥에 딱 닿는 루트 위치를 돌려줌.
    /// body가 없으면 바닥 위치 그대로 (피벗이 발에 있는 경우).
    /// ignoreRoot: 이 오브젝트(보통 나 자신)의 콜라이더는 바닥으로 안 봄
    /// </summary>
    public static Vector3 GroundedPosition(Vector3 point, CharacterController body, float probeDown = 10f, Transform ignoreRoot = null)
    {
        float footOffset = FootOffset(body);           // 루트에서 발바닥까지 (아래로, m)
        Vector3 from = point + Vector3.up * 1.5f;      // 지점이 바닥에 살짝 묻혀 있어도 찾도록

        if (CastUtil.Raycast(from, Vector3.down, probeDown + 1.5f, GroundMask, ignoreRoot, out var hit))
        {
            float skin = body != null ? body.skinWidth : 0f;
            return hit.point + Vector3.up * (footOffset + skin + 0.02f);
        }

        // 바닥을 못 찾으면 지점 높이를 발바닥으로 보고 올림
        return point + Vector3.up * (footOffset + 0.02f);
    }

    /// <summary>캐릭터 루트(피벗)에서 캡슐 맨 아래까지의 거리. 피벗이 발이면 0, 몸 중앙이면 키의 절반</summary>
    public static float FootOffset(CharacterController cc)
    {
        if (cc == null) return 0f;

        float scaleY = Mathf.Abs(cc.transform.lossyScale.y);
        float scaleXZ = Mathf.Max(Mathf.Abs(cc.transform.lossyScale.x), Mathf.Abs(cc.transform.lossyScale.z));

        float height = Mathf.Max(cc.height * scaleY, cc.radius * 2f * scaleXZ);
        float bottom = cc.center.y * scaleY - height * 0.5f;   // 루트 기준 캡슐 바닥 높이 (음수면 아래)

        return Mathf.Max(0f, -bottom);
    }

    void OnDrawGizmos()
    {
        Vector3 p = transform.position;

        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireSphere(p + Vector3.up * 0.4f, 0.4f);
        Gizmos.DrawWireSphere(p + Vector3.up * 1.4f, 0.4f);
        Gizmos.DrawLine(p + new Vector3(0.4f, 0.4f, 0f), p + new Vector3(0.4f, 1.4f, 0f));
        Gizmos.DrawLine(p + new Vector3(-0.4f, 0.4f, 0f), p + new Vector3(-0.4f, 1.4f, 0f));
        Gizmos.DrawLine(p + Vector3.up, p + Vector3.up + transform.forward * 1.2f);
    }

    void OnDrawGizmosSelected()
    {
        // 실제로 서게 될 바닥 표시
        Vector3 from = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(from, Vector3.down, out var hit, probeDown + 1.5f, GroundMask, QueryTriggerInteraction.Ignore))
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(from, hit.point);
            Gizmos.DrawWireCube(hit.point, new Vector3(0.8f, 0.02f, 0.8f));
        }
        else
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(from, from + Vector3.down * (probeDown + 1.5f));
        }
    }
}