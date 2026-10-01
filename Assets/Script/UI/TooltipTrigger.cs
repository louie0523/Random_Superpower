using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public System.Func<TooltipView.Content> Provider;

    public void OnPointerEnter(PointerEventData e)
    {
        if (Provider == null) return;
        UIManager.Instance?.Tooltip?.Show(Provider());
    }

    public void OnPointerExit(PointerEventData e)
        => UIManager.Instance?.Tooltip?.Hide();
}