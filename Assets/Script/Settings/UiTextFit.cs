using TMPro;
using UnityEngine;

/// <summary>
/// 글자가 칸보다 길면(다른 언어) 잘리지 않게 — 줄바꿈 + 글자 크기 자동 줄이기.
/// 설정 창 · ESC 메뉴가 열릴 때 그 안 글자 전부에 한 번 적용 (프리팹에서 직접 Auto Size를 켜 둔 글자는 건드리지 않음).
/// </summary>
public static class UiTextFit
{
    /// <summary>최소 크기 = 원래 크기 × 이 값</summary>
    const float MinRatio = 0.55f;
    const float MinSize = 10f;

    public static void Apply(Transform root)
    {
        if (root == null) return;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) Fit(t);
    }

    public static void Fit(TMP_Text t)
    {
        if (t == null || t.enableAutoSizing) return;
        float size = t.fontSize;
        t.fontSizeMax = size;
        t.fontSizeMin = Mathf.Max(MinSize, Mathf.Round(size * MinRatio));
        t.textWrappingMode = TextWrappingModes.Normal;
        t.enableAutoSizing = true;
        if (t.overflowMode == TextOverflowModes.Overflow) t.overflowMode = TextOverflowModes.Ellipsis;   // 최소 크기로도 넘치면 … (칸 밖으로 안 삐져나오게)
    }
}
