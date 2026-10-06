using UnityEngine;

public abstract class UIPanel : MonoBehaviour
{
    [SerializeField] private GameObject root;
    //[SerializeField] private CanvasGroup canvasGroup;

    public bool IsOpen { get; private set; }

    protected virtual void Awake()
    {
        if (root == null) root = gameObject;
        Close(true);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        root.SetActive(true);
        OnOpened();
    }

    public void Close(bool instant = false)
    {
        if (!IsOpen && !instant) return;
        IsOpen = false;
        root.SetActive(false);
        OnClosed();
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    protected virtual void OnOpened() { }
    protected virtual void OnClosed() { }
}