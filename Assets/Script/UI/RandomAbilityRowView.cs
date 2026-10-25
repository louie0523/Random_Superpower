using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랜덤 능력 HUD 한 줄 — 줄 프리팹 루트에 붙임. RandomAbilityHud가 필요한 만큼 복제해서 씀
/// (랜덤 능력 1~2줄 + 추가 줄 — 복수 시간 · 데스 노트 등).
///   [아이콘][이름표]  NO. N
///                    효과 요약
/// </summary>
public class RandomAbilityRowView : MonoBehaviour
{
    [Header("내용")]
    public Image Icon;
    [Tooltip("이름표 배경 — 능력 색(UiAccent)")]
    public Image NamePlate;
    public TMP_Text Name;
    [Tooltip("NO. N — 능력 번호 색(UiNumberColor)")]
    public TMP_Text Number;
    [Tooltip("한 줄 요약")]
    public TMP_Text Summary;

    [Header("능력 색")]
    [Tooltip("이름표 말고도 능력 색으로 칠할 것 (테두리 · 띠 등)")]
    public Graphic[] AccentTargets;
    [Tooltip("아이콘이 없을 때 아이콘 자리 색")]
    public Color IconPlaceholder = new(1f, 1f, 1f, 0.12f);

    [Header("배치")]
    [Tooltip("이름 길이에 맞춰 이름표 폭을 바꾸고, 번호 · 요약을 이름표 오른쪽으로 옮김.\n" +
             "Horizontal Layout Group + Content Size Fitter로 직접 배치했으면 끄기")]
    public bool AutoArrange = true;
    public float PlatePadding = 22f, PlateMin = 56f, PlateMax = 220f;
    [Tooltip("이름표와 번호 · 요약 사이")]
    public float Gap = 10f;

    [Header("요약 줄 수")]
    [Tooltip("요약이 길면 이 줄 수까지는 줄(배경)이 아래로 늘어남. 넘으면 글자만 아래로 삐져나감. 0 = 제한 없음 (몇 줄이든 배경이 같이 늘어남)\n" +
             "아이콘 · 이름표 · 번호는 그대로 (위쪽 기준으로 붙어 있어야 함)")]
    [Min(0)] public int MaxSummaryLines = 0;

    [Header("연출")]
    [Tooltip("능력이 바뀔 때 살짝 튐 (비우면 이 오브젝트)")]
    public RectTransform PopTarget;

    public int ShownId { get; private set; } = -1;
    private float _pop;
    private bool _based;
    private float _baseRowH, _baseSumH;

    void Awake()
    {
        if (PopTarget == null) PopTarget = (RectTransform)transform;
    }

    void OnDisable()
    {
        ShownId = -1;
        _pop = 0f;
        if (PopTarget != null) PopTarget.localScale = Vector3.one;
    }

    public void Bind(AbilityData a)
    {
        int id = a != null ? a.Id : 0;
        if (id == ShownId) return;
        ShownId = id;
        _pop = 1f;
        _live = false;

        ApplyStyle(a);
        if (Name != null) Name.text = AbilityUi.Name(a);
        if (Number != null) Number.text = AbilityUi.NumberText(a);
        if (Summary != null) Summary.text = AbilityUi.Summary(a);

        if (AutoArrange) Arrange();
        FitHeight();
    }

    /// <summary>추가 줄 (복수 시간 등) — 요약은 매 프레임 바뀔 수 있음 (남은 초)</summary>
    public void BindExtra(HudExtraRow r)
    {
        if (r.Key != ShownId)
        {
            ShownId = r.Key;
            _pop = 1f;
            ApplyStyle(r.Style);
            if (Name != null) Name.text = r.Name ?? "";
            if (Number != null) Number.text = r.Number ?? "";
            if (AutoArrange) Arrange();
            SetText(Summary, r.Summary ?? "");
            FitHeight();
            return;
        }
        if (SetText(Summary, r.Summary ?? "")) FitHeight();
    }

    /// <summary>
    /// 능력 줄의 요약을 지금 상태로 (Behaviour의 DescToken("hud") — 예: "폰 3 / 6").
    /// null이면 원래 요약으로 돌아감
    /// </summary>
    public void SetLiveSummary(string live, AbilityData a)
    {
        bool changed = false;
        if (live != null) { changed = SetText(Summary, live); _live = true; }
        else if (_live) { changed = SetText(Summary, AbilityUi.Summary(a)); _live = false; }
        if (changed) FitHeight();
    }

    /// <summary>글꼴 · 글자 크기 (0이면 그대로) — RandomAbilityHudStyle에서</summary>
    public void ApplyFont(TMP_FontAsset font, float nameSize, float numberSize, float summarySize)
    {
        void One(TMP_Text t, float size)
        {
            if (t == null) return;
            if (font != null) t.font = font;
            if (size > 0f) t.fontSize = size;
        }
        One(Name, nameSize);
        One(Number, numberSize);
        One(Summary, summarySize);
        if (AutoArrange && ShownId != -1) Arrange();
        FitHeight();
    }

    /// <summary>
    /// 요약 글 길이에 맞춰 높이 — MaxSummaryLines 줄까지는 요약 칸과 줄 전체(배경)가 아래로 늘어남.
    /// 아이콘 · 이름표 · 번호는 위쪽 기준이라 그대로. 그보다 길면 글자만 아래로 넘침
    /// </summary>
    void FitHeight()
    {
        if (Summary == null) return;
        var sr = Summary.rectTransform;
        var row = (RectTransform)transform;
        if (!_based)
        {
            _based = true;
            _baseRowH = row.rect.height;
            _baseSumH = sr.rect.height;
        }

        float w = sr.rect.width;
        if (w <= 1f) return;

        float pref = Summary.GetPreferredValues(Summary.text, w, 0f).y;
        float maxH = float.MaxValue;                                   // 0 = 제한 없음
        if (MaxSummaryLines > 0)
        {
            var sb = new System.Text.StringBuilder("가");
            for (int i = 1; i < MaxSummaryLines; i++) sb.Append("\n가");
            maxH = Mathf.Max(_baseSumH, Summary.GetPreferredValues(sb.ToString(), w, 0f).y);
        }
        float h = Mathf.Clamp(pref, _baseSumH, maxH);
        float extra = h - _baseSumH;

        bool stretched = !Mathf.Approximately(sr.anchorMin.y, sr.anchorMax.y);     // 요약이 줄 높이에 붙어 늘어나는 프리팹
        if (!stretched) sr.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _baseRowH + extra);

        // Layout Group 아래면 LayoutElement로 높이를 알려야 배경 · 다음 줄이 같이 밀림 (없으면 붙임)
        var le = GetComponent<LayoutElement>();
        if (le == null && transform.parent != null && transform.parent.GetComponent<LayoutGroup>() != null)
            le = gameObject.AddComponent<LayoutElement>();
        if (le != null)
        {
            le.preferredHeight = _baseRowH + extra;
            le.minHeight = _baseRowH + extra;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }

    bool _live;

    static bool SetText(TMP_Text t, string s)
    {
        if (t == null || t.text == s) return false;
        t.text = s;
        return true;
    }

    void ApplyStyle(AbilityData a)
    {
        if (Icon != null)
        {
            Icon.sprite = a != null ? a.Icon : null;
            Icon.color = Icon.sprite != null ? Color.white : IconPlaceholder;
        }

        Color accent = AbilityUi.Accent(a);
        if (NamePlate != null) NamePlate.color = accent;
        if (AccentTargets != null)
            foreach (var g in AccentTargets) if (g != null) g.color = new Color(accent.r, accent.g, accent.b, g.color.a > 0f ? g.color.a : 1f);

        if (Name != null) Name.color = AbilityUi.NameColor(a);
        if (Number != null) Number.color = AbilityUi.NumberColor(a);
    }

    /// <summary>이름표 폭 = 이름 길이, 번호 · 요약은 그 오른쪽부터 줄 끝까지</summary>
    void Arrange()
    {
        if (NamePlate == null || Name == null) return;
        var row = (RectTransform)transform;
        var plate = NamePlate.rectTransform;

        float w = Mathf.Clamp(Name.GetPreferredValues(Name.text).x + PlatePadding, PlateMin, PlateMax);
        plate.sizeDelta = new Vector2(w, plate.sizeDelta.y);

        // 이름표 오른쪽 끝 (줄 왼쪽 기준)
        float plateLeft = plate.anchoredPosition.x - plate.pivot.x * w + plate.anchorMin.x * row.rect.width;
        float x = plateLeft + w + Gap;
        float width = Mathf.Max(40f, row.rect.width - x - 8f);

        foreach (var t in new[] { Number, Summary })
        {
            if (t == null) continue;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(0f, rt.anchorMax.y);
            rt.pivot = new Vector2(0f, rt.pivot.y);
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        }
    }

    void Update()
    {
        if (_pop <= 0f || PopTarget == null) return;
        _pop = Mathf.MoveTowards(_pop, 0f, Time.unscaledDeltaTime * 4f);
        float s = 1f + Mathf.Sin(_pop * Mathf.PI) * 0.06f;
        PopTarget.localScale = new Vector3(s, s, 1f);
    }
}
