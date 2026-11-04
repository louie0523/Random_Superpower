using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v7 칸 막대 (숫자 슬라이더) — 노란 채움이 칸 중간에서 잘리지 않고 온전한 칸 단위로 끊기게.
/// 슬라이더와 같은 오브젝트에 붙임. 채움(Fill)의 오른쪽 끝을 칸 폭(PipWidth)의 배수로 맞춤.
/// 칸 폭 = 칸 그림 폭 ÷ 그 Image의 Pixels Per Unit Multiplier (기본 34)
/// </summary>
[DisallowMultipleComponent]
public class UiPipSnap : MonoBehaviour
{
    public Slider Slider;
    [Tooltip("채움 (Fill) — 비우면 슬라이더의 Fill Rect")]
    public RectTransform Fill;
    [Tooltip("칸 하나 폭 (틈 포함)")]
    public float PipWidth = 34f;

    void Awake()
    {
        if (Slider == null) Slider = GetComponent<Slider>();
        if (Fill == null && Slider != null) Fill = Slider.fillRect;
    }

    void LateUpdate()
    {
        if (Slider == null || Fill == null || PipWidth <= 0f) return;
        var track = Fill.parent as RectTransform;
        if (track == null) return;
        float total = track.rect.width;
        if (total <= 0f) return;

        int count = Mathf.Max(1, Mathf.FloorToInt(total / PipWidth + 0.001f));
        int on = Mathf.RoundToInt(Slider.normalizedValue * count);
        if (Slider.normalizedValue > 0f && on == 0) on = 1;          // 조금이라도 있으면 한 칸
        float x = Mathf.Clamp01(on * PipWidth / total);

        var max = Fill.anchorMax;
        if (!Mathf.Approximately(max.x, x)) Fill.anchorMax = new Vector2(x, max.y);
    }
}
