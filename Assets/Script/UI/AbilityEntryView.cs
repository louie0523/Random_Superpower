using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AbilityEntryView : MonoBehaviour
{
    public struct Model
    {
        public Sprite Icon;
        public string SlotLabel;
        public string Title;
        public string Body;
        public string Detail;
    }

    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text slotLabel;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text body;
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private Button expandButton;
    [SerializeField] private LocText expandLabel;

    private bool _expanded;
    private Model _model;

    void Awake()
    {
        expandButton?.onClick.AddListener(Toggle);
    }

    public void Bind(Model m)
    {
        _model = m;

        if (icon != null)
        {
            icon.enabled = m.Icon != null;
            icon.sprite = m.Icon;
        }

        if (slotLabel != null) slotLabel.text = m.SlotLabel;
        if (title != null) title.text = m.Title;
        if (body != null) body.text = m.Body;

        bool hasDetail = !string.IsNullOrWhiteSpace(m.Detail);

        if (expandButton != null) expandButton.gameObject.SetActive(hasDetail);
        if (detailText != null) detailText.text = m.Detail ?? "";

        _expanded = false;
        if (detailRoot != null) detailRoot.SetActive(false);

        UpdateExpandLabel();
    }

    void Toggle()
    {
        _expanded = !_expanded;
        if (detailRoot != null) detailRoot.SetActive(_expanded);
        UpdateExpandLabel();
    }

    void UpdateExpandLabel()
    {
        expandLabel?.SetKey(_expanded ? "ui.common.collapse" : "ui.common.expand");
    }
}