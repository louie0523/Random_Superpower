using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DetailWindow : MonoBehaviour
{
    public struct Content
    {
        public Sprite Icon;
        public string Title;
        public string Subtitle;
        public string Description;
        public string Body;
    }

    [SerializeField] private CanvasGroup group;
    [SerializeField] private Button backdrop;
    [SerializeField] private Button closeButton;

    [Header("내용")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private GameObject divider;
    [SerializeField] private TMP_Text bodyText;

    public bool IsOpen { get; private set; }

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        backdrop?.onClick.AddListener(Hide);
        closeButton?.onClick.AddListener(Hide);
        Apply(false);
    }

    public void Show(Content c)
    {
        if (icon != null)
        {
            icon.enabled = c.Icon != null;
            icon.sprite = c.Icon;
        }

        SetText(titleText, c.Title);
        SetText(subtitleText, c.Subtitle);
        SetText(descText, c.Description);
        SetText(bodyText, c.Body);

        // 둘 다 있을 때만 구분선
        if (divider != null)
            divider.SetActive(!string.IsNullOrWhiteSpace(c.Description)
                           && !string.IsNullOrWhiteSpace(c.Body));

        Apply(true);
    }

    public void Hide() => Apply(false);

    void Apply(bool on)
    {
        IsOpen = on;
        if (group == null) return;

        group.alpha = on ? 1f : 0f;
        group.interactable = on;
        group.blocksRaycasts = on;
    }

    static void SetText(TMP_Text t, string s)
    {
        if (t == null) return;
        bool has = !string.IsNullOrWhiteSpace(s);
        t.text = s ?? "";
        t.gameObject.SetActive(has);
    }
}