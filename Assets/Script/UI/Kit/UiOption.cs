using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>켜고 끄는 버튼 (체크칸 · 고르기 버튼). 켜짐 상태는 코드가 SetOn으로 넣음</summary>
public class UiOption : MonoBehaviour
{
    public Button Button;
    [Tooltip("켜짐 · 꺼짐에 따라 색이 바뀌는 면")]
    public Image Face;
    public TMP_Text Label;
    [Tooltip("체크 표시 (없으면 면 색으로만)")]
    public Image Check;
    public Color OnFace = new(0.306f, 0.839f, 0.745f, 1f);      // 민트 (GameUiKit.Mint)
    public Color OffFace = new(0.169f, 0.196f, 0.235f, 1f);     // GameUiKit.Raised
    [Tooltip("켜고 끌 때 글자 색을 면에 맞춰 바꿈 (밝은 면 = 검정 글자)")]
    public bool AutoTextColor = true;
    [Tooltip("켜짐 · 꺼짐 글자 색을 아래 색으로 (AutoTextColor보다 먼저) — v7 스프레이 탭")]
    public bool FixedTextColors;
    public Color OnText = Color.white;
    public Color OffText = new(0.725f, 0.698f, 0.769f, 1f);

    public bool IsOn { get; private set; }

    public void SetOn(bool on)
    {
        IsOn = on;
        if (Face != null) Face.color = on ? OnFace : OffFace;
        if (FixedTextColors && Label != null) Label.color = on ? OnText : OffText;
        else if (AutoTextColor && Label != null && Face != null) Label.color = GameUiKit.TextOn(Face.color);
        if (Check != null && Check.enabled != on) Check.enabled = on;
    }

    public void SetText(string text)
    {
        if (Label != null && Label.text != text) Label.text = text;
    }
}
