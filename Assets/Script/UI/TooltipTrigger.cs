using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public System.Func<TooltipView.Content> Provider;

    private bool _showing;

    public void OnPointerEnter(PointerEventData e)
    {
        if (Provider == null) return;
        UIManager.Instance?.Tooltip?.Show(Provider());
        _showing = true;
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