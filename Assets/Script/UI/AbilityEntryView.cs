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
    }

    [SerializeField] private Image background;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text slotLabel;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text body;
    [SerializeField] private TMP_Text expandIcon;      // ▼ / ▲
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private TMP_Text detailText;

    [Header("색")]
    [SerializeField] private Color normalColor = new(1f, 1f, 1f, 0.04f);
    [SerializeField] private Color hoverColor = new(1f, 1f, 1f, 0.10f);

    public event System.Action<AbilityEntryView> Clicked;

    public bool IsExpanded { get; private set; }
    public bool CanExpand { get; private set; }

    public void Bind(Model m)
    {
        if (icon != null)
        {
            icon.enabled = m.Icon != null;
            icon.sprite = m.Icon;
        }

        SetText(slotLabel, m.SlotLabel);
        SetText(title, m.Title);
        SetText(body, m.Body);

        CanExpand = !string.IsNullOrWhiteSpace(m.Detail);
        if (detailText != null) detailText.text = m.Detail ?? "";
        if (expandIcon != null) expandIcon.gameObject.SetActive(CanExpand);

        SetExpanded(false);
        SetHover(false);
    }

    public void SetExpanded(bool on)
    {
        IsExpanded = CanExpand && on;

        if (detailRoot != null) detailRoot.SetActive(IsExpanded);
        if (expandIcon != null) expandIcon.text = IsExpanded ? "▲" : "▼";

        RebuildLayout();
    }

    // ── 입력 ──

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (!CanExpand) return;

        Clicked?.Invoke(this);          // 실제 토글은 패널이 결정
    }

    public void OnPointerEnter(PointerEventData e) { if (CanExpand) SetHover(true); }
    public void OnPointerExit(PointerEventData e) => SetHover(false);

    void SetHover(bool on)
    {
        if (background != null) background.color = on ? hoverColor : normalColor;
    }

    // ── 유틸 ──

    static void SetText(TMP_Text t, string s)
    {
        if (t == null) return;
        bool has = !string.IsNullOrWhiteSpace(s);
        t.text = s ?? "";
        t.gameObject.SetActive(has);
    }

    void RebuildLayout()
    {
        var rt = transform as RectTransform;
        if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        var parent = transform.parent as RectTransform;
        if (parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }
}