using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TooltipView : MonoBehaviour, ILocRefresh
{
    public struct Content
    {
        public string Title;
        public string Body;
        public string Detail;
        public string[] Tags;
        /// <summary>무기 스펙 (사거리 · 탄약 · 분당 발사 등) — Body(능력치) 아래 줄 긋고</summary>
        public string Spec;

        /// <summary>기존 3-튜플 호출부 호환</summary>
        public static implicit operator Content((string title, string body, string detail) t)
            => new Content { Title = t.title, Body = t.body, Detail = t.detail };
    }

    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text detailText;
    [Tooltip("태그 위 줄")]
    [SerializeField] private GameObject divider;
    [SerializeField] private TMP_Text tagsText;

    [Header("구역 (없어도 됨) — 능력치 / 줄 / 스펙 / 줄 / 설명 / 줄 / 태그")]
    [SerializeField] private TMP_Text specText;
    [Tooltip("스펙 위 줄")]
    [SerializeField] private GameObject specDivider;
    [Tooltip("설명 위 줄")]
    [SerializeField] private GameObject detailDivider;

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

        // 스펙 칸이 없는 예전 툴팁이면 능력치 아래에 붙여서
        string spec = c.Spec;
        if (specText == null && !string.IsNullOrWhiteSpace(spec) && bodyText != null)
        {
            SetText(bodyText, string.IsNullOrWhiteSpace(c.Body) ? spec : c.Body + "\n\n" + spec);
            spec = null;
        }
        SetText(specText, spec);
        SetText(detailText, c.Detail);

        bool hasBody = Has(c.Body);
        bool hasSpec = specText != null && Has(spec);
        bool hasDetail = Has(c.Detail);
        bool hasTags = c.Tags != null && c.Tags.Length > 0;

        if (tagsText != null)
        {
            tagsText.gameObject.SetActive(hasTags);
            if (hasTags) tagsText.text = BuildTagLine(c.Tags);
        }

        // 구역 사이 줄 — 위에 뭔가 있고 이 구역도 있을 때만
        if (specDivider != null) specDivider.SetActive(hasSpec && hasBody);
        if (detailDivider != null) detailDivider.SetActive(hasDetail && (hasBody || hasSpec));
        if (divider != null) divider.SetActive(hasTags && (hasBody || hasSpec || hasDetail || detailDivider == null));

        root.gameObject.SetActive(true);

        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        Follow();
    }

    public void Hide() => root.gameObject.SetActive(false);

    /// <summary>언어 바뀜 — 예전 언어 글자라 닫음 (다시 올리면 새 언어)</summary>
    public void OnLocaleChanged() { if (root != null) Hide(); }

    static bool Has(string s) => !string.IsNullOrWhiteSpace(s);

    /// <summary>
    /// 칩처럼 보이도록 리치 텍스트로 배경을 깔아줌.
    /// 칩 안은 줄이 안 바뀌게 (nobr + 붙은 공백) — 칩 중간에서 잘려 다음 줄로 넘어가던 것
    /// </summary>
    static string BuildTagLine(string[] tags)
    {
        const string nb = "\u00A0";
        var sb = new System.Text.StringBuilder();
        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            string word = t.Trim().Replace(" ", nb);
            sb.Append("<nobr><mark=#FFFFFF22 padding=\"2,2,3,3\">").Append(nb).Append(nb).Append(word).Append(nb).Append(nb).Append("</mark></nobr>  ");
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