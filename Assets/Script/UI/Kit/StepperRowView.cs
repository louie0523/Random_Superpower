using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>값 한 줄 — 이름 · [--] [-] 값 [+] [++]. 큰 칸(--, ++)은 비워도 됨</summary>
public class StepperRowView : MonoBehaviour
{
    public TMP_Text Label;
    public TMP_Text Value;
    public Button Minus;
    public Button Plus;
    [Tooltip("10칸씩 (없어도 됨)")]
    public Button BigMinus;
    public Button BigPlus;

    public void SetInteractable(bool on)
    {
        GameUiKit.Interact(Minus, on);
        GameUiKit.Interact(Plus, on);
        GameUiKit.Interact(BigMinus, on);
        GameUiKit.Interact(BigPlus, on);
    }
}
