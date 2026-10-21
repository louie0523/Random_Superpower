using UnityEngine;

/// <summary>
/// 프리팹 편집 중에 AlterData의 피격 범위를 미리 보기 — 변신 모델 프리팹 루트에 붙이고 Alter 칸에 데이터.
/// 게임과 같은 기준(플레이어 방향 · m)으로 그림. 게임엔 영향 없음 (선만 그림).
/// </summary>
public class AlterHitboxPreview : MonoBehaviour
{
    public AlterData Alter;
    public Color Color = new(0.2f, 1f, 0.35f, 0.9f);

    void OnDrawGizmos()
    {
        if (Alter == null || Alter.Hitboxes == null) return;
        // 프리팹 루트 = 게임에서 파츠 자리. 게임에선 모델에 AlterData의 크기 · 회전이 붙으므로,
        // 크기 · 회전이 없는 프리팹에선 그만큼 거꾸로 줄여 · 돌려서 그려야 모델과 같은 비율로 보임
        var frame = transform.localToWorldMatrix
                    * Matrix4x4.TRS(Vector3.zero, Quaternion.Inverse(Quaternion.Euler(Alter.Rotation)), Vector3.one)
                    * Matrix4x4.Scale(new Vector3(Inv(Alter.Scale.x), Inv(Alter.Scale.y), Inv(Alter.Scale.z)));
        Gizmos.color = Color;
        foreach (var h in Alter.Hitboxes) AlterHitboxes.Draw(frame, h, Alter.Position);
    }

    static float Inv(float v) => Mathf.Abs(v) < 1e-5f ? 1f : 1f / v;
}
