using UnityEngine;

/// <summary>
/// 게임 중 변신 피격 범위를 초록 선으로 (씬 뷰 · 게임 뷰의 Gizmos 켜면 보임). PlayerAlter가 자동으로 붙임.
/// </summary>
public class AlterHitboxGizmo : MonoBehaviour
{
    public static Color Color = new(0.2f, 1f, 0.35f, 0.9f);

    void OnDrawGizmos()
    {
        Gizmos.color = Color;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (c == null || !c.enabled) continue;
            Gizmos.matrix = c.transform.localToWorldMatrix;
            switch (c)
            {
                case CapsuleCollider cap:
                    AlterHitboxes.DrawCapsule(cap.radius, cap.height);
                    break;
                case BoxCollider box:
                    Gizmos.DrawWireCube(box.center, box.size);
                    break;
                case SphereCollider s:
                    Gizmos.DrawWireSphere(s.center, s.radius);
                    break;
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
