using UnityEngine;

/// <summary>
/// 쉐도우 워커의 그림자 — 모든 클라이언트에서 각자 그림 (네트워크 오브젝트 아님).
/// 주인 뒤를 살짝 늦게 따라감. 위치 교체(그림자 이동) 때 Snap으로 그 자리에 놓임.
/// 그림자 프리팹에 붙어 있지 않으면 자동으로 붙음.
/// </summary>
public class ShadowFollower : MonoBehaviour
{
    public Transform Target;
    [Tooltip("주인이 죽으면 숨김")]
    public PlayerHealth Owner;
    [Tooltip("주인 뒤 거리 (m)")]
    public float Distance = 1.4f;
    [Tooltip("옆으로 비켜서는 거리 (m)")]
    public float Side = 0.6f;
    [Tooltip("따라가는 빠르기 (클수록 바짝)")]
    public float Follow = 4f;
    [Tooltip("이 거리보다 멀어지면 한 번에 따라붙음")]
    public float TeleportDistance = 25f;

    private bool _hidden;

    /// <summary>주인이 죽어서 숨긴 상태 — ShadowMimic이 복제본 렌더러에 반영</summary>
    public bool Hidden => _hidden;

    public void Snap(Vector3 pos)
    {
        transform.position = pos;
        if (Target != null) transform.rotation = Quaternion.LookRotation(Flat(Target.forward), Vector3.up);
    }

    /// <summary>주인이 죽었을 때 등 — 렌더러만 끄고 켬</summary>
    public void SetVisible(bool on)
    {
        if (_hidden == !on) return;
        _hidden = !on;
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    void LateUpdate()
    {
        if (Target == null) { Destroy(gameObject); return; }
        if (Owner != null) SetVisible(!Owner.IsDead);

        Vector3 fwd = Flat(Target.forward);
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 want = Target.position - fwd * Distance + right * Side;

        if ((want - transform.position).sqrMagnitude > TeleportDistance * TeleportDistance)
            transform.position = want;
        else
            transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-Follow * Time.deltaTime));

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd, Vector3.up),
                                              1f - Mathf.Exp(-Follow * 1.5f * Time.deltaTime));
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}