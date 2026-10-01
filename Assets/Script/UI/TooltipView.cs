using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TooltipView : MonoBehaviour
{
    public struct Content
    {
        public string Title;
        public string Body;
        public string Detail;
        public string[] Tags;

        /// <summary>기존 3-튜플 호출부 호환</summary>
        public static implicit operator Content((string title, string body, string detail) t)
            => new Content { Title = t.title, Body = t.body, Detail = t.detail };
    }

    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private GameObject divider;
    [SerializeField] private TMP_Text tagsText;

    [SerializeField] private Vector2 offset = new(16f, -16f);
    [SerializeField] private float edgePadding = 8f;

    private RectTransform _canvasRect;
    private Camera _uiCam;

    void Awake()
    {
        var canvas = GetComponentInParent<Canvas>();
        _canvasRect = canvas.transform as RectTransform;
        _uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
               ? null : canvas.worldCamera;

        Hide();
    }

    void LateUpdate()
    {
        if (root.gameObject.activeSelf) Follow();
    }

    public void Show(Content c)
    {
        SetText(titleText, c.Title);
        SetText(bodyText, c.Body);
        SetText(detailText, c.Detail);

        bool hasTags = c.Tags != null && c.Tags.Length > 0;
        if (tagsText != null)
        {
            tagsText.gameObject.SetActive(hasTags);
            if (hasTags) tagsText.text = BuildTagLine(c.Tags);
        }
        if (divider != null) divider.SetActive(hasTags);

        root.gameObject.SetActive(true);

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        Follow();
    }

    public void Hide() => root.gameObject.SetActive(false);

    /// <summary>칩처럼 보이도록 리치 텍스트로 배경을 깔아줌</summary>
    static string BuildTagLine(string[] tags)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            sb.Append("<mark=#FFFFFF18>  ").Append(t).Append("  </mark> ");
        }
        return sb.ToString().TrimEnd();
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

    static void SetText(TMP_Text t, string s)
    {
        if (t == null) return;
        bool has = !string.IsNullOrWhiteSpace(s);
        t.text = s ?? "";
        t.gameObject.SetActive(has);
    }
}