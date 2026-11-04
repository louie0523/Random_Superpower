using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public System.Func<TooltipView.Content> Provider;

    private bool _showing;
    private bool _shift;

    /// <summary>Shift를 누르고 있나 — 툴팁이 '늘어난 만큼'을 보여 줄 때</summary>
    public static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    public void OnPointerEnter(PointerEventData e)
    {
        if (Provider == null) return;
        _shift = ShiftHeld;
        UIManager.Instance?.Tooltip?.Show(Provider());
        _showing = true;
    }

    void Update()
    {
        // 올려 둔 채로 Shift를 누르거나 떼면 툴팁 다시 그림
        if (!_showing || Provider == null) return;
        bool sh = ShiftHeld;
        if (sh == _shift) return;
        _shift = sh;
        UIManager.Instance?.Tooltip?.Show(Provider());
    }

    public void OnPointerExit(PointerEventData e)
    {
        UIManager.Instance?.Tooltip?.Hide();
        _showing = false;
    }

    void OnDisable()
    {
        if (!_showing) return;
        UIManager.Instance?.Tooltip?.Hide();
        _showing = false;
    }
}