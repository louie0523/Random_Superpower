using UnityEngine;
using UnityEngine.UI;
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
    [SerializeField] private float edgePadding = 8f;

    private RectTransform _canvasRect;
    private Camera _uiCam;
    private bool _showDetail;
    private string _detailCache;

    void Awake()
    {
        var canvas = GetComponentInParent<Canvas>();
        _canvasRect = canvas.transform as RectTransform;
        _uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
               ? null : canvas.worldCamera;

        Hide();
    }

    void Update()
    {
        if (!root.gameObject.activeSelf) return;

        Follow();

        if (!string.IsNullOrEmpty(_detailCache) && Input.GetMouseButtonDown(0))
        {
            _showDetail = !_showDetail;
            if (detailSection != null) detailSection.SetActive(_showDetail);

            LayoutRebuilder.ForceRebuildLayoutImmediate(root);   // 크기 바뀌었으니 갱신
            Follow();
        }
    }

    void Follow()
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, Input.mousePosition, _uiCam, out var local);

        local += offset;

        Vector2 canvasSize = _canvasRect.rect.size;
        Vector2 boxSize = root.rect.size;

        float maxX = canvasSize.x * 0.5f - boxSize.x - edgePadding;
        float minY = -canvasSize.y * 0.5f + boxSize.y + edgePadding;

        if (local.x > maxX) local.x = maxX;
        if (local.y < minY) local.y = minY;

        root.anchoredPosition = local;
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

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);   // 크기 확정 후 배치
        Follow();
    }

    public void Hide()
    {
        _showDetail = false;
        _detailCache = null;
        root.gameObject.SetActive(false);
    }
}