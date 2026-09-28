using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class AbilityEntryView : MonoBehaviour, IPointerClickHandler,
                                IPointerEnterHandler, IPointerExitHandler
{
    public struct Model
    {
        public Sprite Icon;
        public string SlotLabel;
        public string Title;
        public string Body;
        public string Detail;
        public Color Accent;
    }

    [SerializeField] private Image background;
    [SerializeField] private Image accentBar;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text slotLabel;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text body;

    [Header("»ö")]
    [SerializeField] private Color normalColor = new(1f, 1f, 1f, 0.04f);
    [SerializeField] private Color hoverColor = new(1f, 1f, 1f, 0.10f);
    [SerializeField] private Color selectedColor = new(1f, 1f, 1f, 0.16f);

    public event System.Action<AbilityEntryView> Clicked;

    public Model Data { get; private set; }
    public bool HasDetail { get; private set; }

    private bool _selected;

    void Awake()
    {
        if (background == null) background = GetComponent<Image>();
    }

    public void Bind(Model m)
    {
        Data = m;
        HasDetail = !string.IsNullOrWhiteSpace(m.Detail)
                 || !string.IsNullOrWhiteSpace(m.Body);

        if (icon != null)
        {
            icon.enabled = m.Icon != null;
            icon.sprite = m.Icon;
        }
        if (accentBar != null)
        {
            bool hasAccent = m.Accent.a > 0.01f;
            accentBar.gameObject.SetActive(hasAccent);
            if (hasAccent) accentBar.color = m.Accent;
        }

        SetText(slotLabel, m.SlotLabel);
        SetText(title, m.Title);
        SetText(body, m.Body);

        SetSelected(false);
    }

    public void SetSelected(bool on)
    {
        _selected = on;
        Paint(false);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (!HasDetail) return;
        Clicked?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData e) { if (HasDetail) Paint(true); }
    public void OnPointerExit(PointerEventData e) => Paint(false);

    void Paint(bool hover)
    {
        if (background == null) return;
        background.color = _selected ? selectedColor : hover ? hoverColor : normalColor;
    }

    static void SetText(TMP_Text t, string s)
    {
        if (t == null) return;
        bool has = !string.IsNullOrWhiteSpace(s);
        t.text = s ?? "";
        t.gameObject.SetActive(has);
    }
}