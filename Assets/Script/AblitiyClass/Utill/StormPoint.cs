using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시공의 폭풍이 사람을 보내는 곳 — 맵에 빈 오브젝트로 여러 개 찍어 두기 (발밑 = 이 위치).
/// 갇히지 않는 열린 곳에 둘 것. 하나도 없으면 부활 지점(SpawnPoint)을 대신 씀
/// </summary>
public class StormPoint : MonoBehaviour
{
    static readonly List<StormPoint> _all = new();
    public static IReadOnlyList<StormPoint> All => _all;

    void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
    void OnDisable() => _all.Remove(this);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.55f, 0.3f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 3f);
    }
}
