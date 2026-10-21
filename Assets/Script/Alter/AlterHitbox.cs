using System;
using UnityEngine;

/// <summary>순서가 저장값 — 뒤에만 추가</summary>
public enum AlterHitboxShape
{
    Capsule,    // 앞뒤(z)로 누운 캡슐 — 용 몸통 등
    Box,
    Sphere,
}

/// <summary>
/// 변화(변신) 모델의 피격 범위 하나. 단위는 m, 기준은 붙는 자리(Attach = Root면 플레이어 발밑)에서 플레이어 방향
/// (x 오른쪽 · y 위 · z 앞) — AlterData의 위치 · 회전 · 크기와 상관없음 (모델을 옮겨도 범위는 그대로).
/// </summary>
[Serializable]
public class AlterHitbox
{
    public AlterHitboxShape Shape = AlterHitboxShape.Capsule;
    [Tooltip("중심 (m) — 붙는 자리(Root면 발밑) 기준, x 오른쪽 · y 위 · z 앞")]
    public Vector3 Center = new(0f, 1f, 0f);
    [Tooltip("캡슐: x = 두께(지름) · z = 길이 / 박스: 크기 / 구: x = 지름")]
    public Vector3 Size = new(0.8f, 0.8f, 3f);
    [Tooltip("회전 (°) — 캡슐은 기본이 앞뒤(z)로 누운 방향")]
    public Vector3 Rotation;
}

/// <summary>AlterData.Hitboxes → 실제 콜라이더 (PlayerAlter가 모든 클라이언트에서 만듦)</summary>
public static class AlterHitboxes
{
    public const string ContainerName = "AlterHitboxes";

    /// <summary>파츠 안에서 '플레이어 방향 · m 단위'가 되는 기준 (파츠 회전 · 크기를 되돌림)</summary>
    public static Quaternion FrameRot(AlterData a) => Quaternion.Inverse(Quaternion.Euler(a.Rotation));
    public static Vector3 FrameScale(AlterData a) => new(Inv(a.Scale.x), Inv(a.Scale.y), Inv(a.Scale.z));
    static float Inv(float v) => Mathf.Abs(v) < 1e-5f ? 1f : 1f / v;

    /// <summary>파츠 아래에 피격 콜라이더를 만듦 — 없으면 null. layer = 플레이어 레이어(공격 판정 마스크에 들어 있는 것)</summary>
    public static Transform Build(GameObject part, AlterData a, int layer, Collider ignore)
    {
        if (part == null || a == null || a.Hitboxes == null || a.Hitboxes.Length == 0) return null;
        var root = new GameObject(ContainerName).transform;
        root.SetParent(part.transform, false);
        root.gameObject.layer = layer;
        root.gameObject.AddComponent<AlterHitboxGizmo>();
        Sync(root, a, ignore);
        return root;
    }

    /// <summary>데이터 → 콜라이더 (모양 · 개수가 바뀌면 다시 만듦). 에디터에선 매 프레임 불러서 플레이 중 값 조절이 바로 보임</summary>
    public static void Sync(Transform root, AlterData a, Collider ignore)
    {
        if (root == null || a == null) return;
        root.localPosition = Vector3.zero;
        root.localRotation = FrameRot(a);
        root.localScale = FrameScale(a);

        int n = a.Hitboxes != null ? a.Hitboxes.Length : 0;
        bool rebuild = root.childCount != n;
        for (int i = 0; !rebuild && i < n; i++)
            rebuild = !Matches(root.GetChild(i).GetComponent<Collider>(), a.Hitboxes[i]);

        if (rebuild)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var c = root.GetChild(i);
                c.SetParent(null);
                UnityEngine.Object.Destroy(c.gameObject);
            }
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject($"Hitbox_{i}");
                go.layer = root.gameObject.layer;
                go.transform.SetParent(root, false);
                var h = a.Hitboxes[i] ?? new AlterHitbox();
                Collider col = h.Shape switch
                {
                    AlterHitboxShape.Box => go.AddComponent<BoxCollider>(),
                    AlterHitboxShape.Sphere => go.AddComponent<SphereCollider>(),
                    _ => go.AddComponent<CapsuleCollider>(),
                };
                if (ignore != null) Physics.IgnoreCollision(col, ignore);      // 내 이동(CharacterController)을 막지 않게
            }
        }

        for (int i = 0; i < n; i++)
        {
            var h = a.Hitboxes[i];
            if (h == null) continue;
            var t = root.GetChild(i);
            t.localPosition = h.Center - a.Position;      // 모델 위치(Position)와 상관없이 붙는 자리 기준
            t.localRotation = Quaternion.Euler(h.Rotation);
            t.localScale = Vector3.one;
            switch (t.GetComponent<Collider>())
            {
                case CapsuleCollider c:
                    c.direction = 2;
                    c.radius = Mathf.Max(0.01f, h.Size.x * 0.5f);
                    c.height = Mathf.Max(h.Size.z, h.Size.x);
                    break;
                case BoxCollider b:
                    b.size = h.Size;
                    break;
                case SphereCollider s:
                    s.radius = Mathf.Max(0.01f, h.Size.x * 0.5f);
                    break;
            }
        }
    }

    static bool Matches(Collider c, AlterHitbox h)
    {
        if (c == null || h == null) return false;
        return h.Shape switch
        {
            AlterHitboxShape.Box => c is BoxCollider,
            AlterHitboxShape.Sphere => c is SphereCollider,
            _ => c is CapsuleCollider,
        };
    }

    // ══════════════════ 그리기 (기즈모) ══════════════════

    /// <summary>데이터 하나를 선으로 — m 기준 행렬(frame) 안에서</summary>
    public static void Draw(Matrix4x4 frame, AlterHitbox h, Vector3 origin)
    {
        if (h == null) return;
        Gizmos.matrix = frame * Matrix4x4.TRS(h.Center - origin, Quaternion.Euler(h.Rotation), Vector3.one);
        switch (h.Shape)
        {
            case AlterHitboxShape.Box:
                Gizmos.DrawWireCube(Vector3.zero, h.Size);
                break;
            case AlterHitboxShape.Sphere:
                Gizmos.DrawWireSphere(Vector3.zero, h.Size.x * 0.5f);
                break;
            default:
                DrawCapsule(h.Size.x * 0.5f, Mathf.Max(h.Size.z, h.Size.x));
                break;
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    /// <summary>z축 캡슐 (현재 Gizmos.matrix 기준)</summary>
    public static void DrawCapsule(float radius, float height)
    {
        float half = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 a = new(0, 0, half), b = new(0, 0, -half);
        Gizmos.DrawWireSphere(a, radius);
        Gizmos.DrawWireSphere(b, radius);
        Gizmos.DrawLine(a + Vector3.right * radius, b + Vector3.right * radius);
        Gizmos.DrawLine(a - Vector3.right * radius, b - Vector3.right * radius);
        Gizmos.DrawLine(a + Vector3.up * radius, b + Vector3.up * radius);
        Gizmos.DrawLine(a - Vector3.up * radius, b - Vector3.up * radius);
    }
}
