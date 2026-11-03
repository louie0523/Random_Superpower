using UnityEngine;

/// <summary>
/// 미리보기용 (프리팹을 열었을 때 모양 보려고 넣은 줄 · 칸) — 실행하면 처음 켜질 때 바로 사라짐
/// </summary>
public class UiPreviewOnly : MonoBehaviour
{
    void Awake()
    {
        if (!Application.isPlaying) return;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
