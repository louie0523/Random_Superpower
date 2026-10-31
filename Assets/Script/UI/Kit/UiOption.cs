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
    public Color OnFace = new(1f, 0.81f, 0.24f, 1f);
    public Color OffFace = Color.white;

    public bool IsOn { get; private set; }

    public void SetOn(bool on)
    {
        IsOn = on;
        if (Face != null) Face.color = on ? OnFace : OffFace;
        if (Check != null && Check.enabled != on) Check.enabled = on;
    }

    public void SetText(string text)
    {
        if (Label != null && Label.text != text) Label.text = text;
    }
}
