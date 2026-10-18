using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랜덤 능력 HUD 한 줄 — 줄 프리팹 루트에 붙임. RandomAbilityHud가 두 줄(조커용) 복제해서 씀.
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

    [Header("연출")]
    [Tooltip("능력이 바뀔 때 살짝 튐 (비우면 이 오브젝트)")]
    public RectTransform PopTarget;

    public int ShownId { get; private set; } = -1;
    private float _pop;

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

        if (Icon != null)
        {
            Icon.sprite = a != null ? a.Icon : null;
            Icon.color = Icon.sprite != null ? Color.white : IconPlaceholder;
        }

        Color accent = AbilityUi.Accent(a);
        if (NamePlate != null) NamePlate.color = accent;
        if (AccentTargets != null)
            foreach (var g in AccentTargets) if (g != null) g.color = new Color(accent.r, accent.g, accent.b, g.color.a > 0f ? g.color.a : 1f);

        if (Name != null)
        {
            Name.text = AbilityUi.Name(a);
            Name.color = AbilityUi.NameColor(a);
        }
        if (Number != null)
        {
            Number.text = AbilityUi.NumberText(a);
            Number.color = AbilityUi.NumberColor(a);
        }
        if (Summary != null) Summary.text = AbilityUi.Summary(a);

        if (AutoArrange) Arrange();
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
