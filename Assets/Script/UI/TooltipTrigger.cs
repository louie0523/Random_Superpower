using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public System.Func<(string title, string body, string detail)> Provider;

    public void OnPointerEnter(PointerEventData e)
    {
        if (Provider == null) return;
        var (t, b, d) = Provider();
        UIManager.Instance?.Tooltip?.Show(t, b, d);
    }

    public void OnPointerExit(PointerEventData e)
    {
        UIManager.Instance?.Tooltip?.Hide();
    }
}