using TMPro;
using UnityEngine;

/// <summary>
/// 게임 화면 위 글자 테두리 — 실행할 때 TMP 외곽선을 켬 (프리팹에 재질을 따로 안 만들어도 됨).
/// HUD 숫자 · 이름처럼 배경이 계속 바뀌는 곳에.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class UiTextOutline : MonoBehaviour
{
    [Range(0f, 0.5f)] public float Width = 0.18f;
    public Color32 Color = new(0, 0, 0, 160);

    void Awake()
    {
        var t = GetComponent<TMP_Text>();
        if (t == null || Width <= 0f) return;
        t.outlineWidth = Width;
        t.outlineColor = Color;
    }
}
