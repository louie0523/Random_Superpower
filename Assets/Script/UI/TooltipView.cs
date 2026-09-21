using UnityEngine;
using TMPro;

public class TooltipView : MonoBehaviour
{
    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private GameObject detailSection;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private Vector2 offset = new(16f, -16f);

    private bool _showDetail;
    private string _detailCache;

    void Awake() => Hide();

    void Update()
    {
        if (!root.gameObject.activeSelf) return;

        // 커서 따라감 (화면 밖으로 안 나가게)
        Vector2 pos = (Vector2)Input.mousePosition + offset;
        var size = root.sizeDelta;

        pos.x = Mathf.Min(pos.x, Screen.width - size.x - 8f);
        pos.y = Mathf.Max(pos.y, size.y + 8f);

        root.position = pos;

        // 세부 토글
        if (!string.IsNullOrEmpty(_detailCache) && Input.GetMouseButtonDown(0))
        {
            _showDetail = !_showDetail;
            if (detailSection != null) detailSection.SetActive(_showDetail);
        }
    }

    public void Show(string title, string body, string detail = null)
    {
        if (titleText != null) titleText.text = title;
        if (bodyText != null) bodyText.text = body;

        _detailCache = detail;
        _showDetail = false;

        if (detailText != null) detailText.text = detail ?? "";
        if (detailSection != null) detailSection.SetActive(false);

        if (hintText != null)
        {
            bool hasDetail = !string.IsNullOrEmpty(detail);
            hintText.gameObject.SetActive(hasDetail);
            if (hasDetail) hintText.text = LocUtil.Get("ui.tooltip.click_detail");
        }

        root.gameObject.SetActive(true);
    }

    public void Hide()
    {
        _showDetail = false;
        _detailCache = null;
        root.gameObject.SetActive(false);
    }
}