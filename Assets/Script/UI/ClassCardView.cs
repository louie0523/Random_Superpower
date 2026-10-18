using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 클래스 선택 카드 한 장 — 카드 프리팹 루트에 붙임. ClassPickPanel이 장 수만큼 복제해서 씀.
/// 비어 있는 칸은 그냥 안 씀 (아이콘 없이 만들어도 됨).
/// </summary>
public class ClassCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("버튼")]
    [Tooltip("카드를 누르는 버튼 (카드 몸통)")]
    public Button PickButton;
    [Tooltip("카드 밑 새로고침 버튼")]
    public Button RerollButton;
    public TMP_Text RerollText;
    [Tooltip("새로고침 버튼 배경 — 쓸 수 있을 때 / 없을 때 색")]
    public Image RerollBack;
    public Color RerollOn = new(0.22f, 0.26f, 0.34f, 1f);
    public Color RerollOff = new(0.16f, 0.17f, 0.2f, 0.7f);

    [Header("내용")]
    public Image Icon;
    [Tooltip("아이콘 뒤 원 — 능력 색을 어둡게 칠함")]
    public Image IconBack;
    [Tooltip("이름표 배경 — 능력 색")]
    public Image NamePlate;
    public TMP_Text Name;
    [Tooltip("NO. N")]
    public TMP_Text Number;
    [Tooltip("한 줄 요약")]
    public TMP_Text Summary;
    [Tooltip("패시브 · 스킬 이름 목록")]
    public TMP_Text Details;

    [Header("능력 색")]
    [Tooltip("능력 색(UiAccent)으로 칠할 것 — 테두리 등")]
    public Graphic[] AccentTargets;
    [Tooltip("마우스를 올리면 빛나는 것 (능력 색, 알파만 변함)")]
    public Image Glow;

    [Header("이름표 폭")]
    [Tooltip("이름 길이에 맞춰 이름표 폭 조절 (이름표에 Content Size Fitter를 쓰면 끄기)")]
    public bool FitPlate = true;
    public float PlatePadding = 36f, PlateMin = 110f, PlateMax = 270f;

    [Header("연출")]
    [Tooltip("커지고 뒤집히는 대상 — 보통 카드 몸통 (비우면 이 오브젝트)")]
    public RectTransform Visual;
    public float HoverScale = 1.045f;

    public event Action<ClassCardView> Picked;
    public event Action<ClassCardView> Rerolled;

    public int Index { get; set; }
    public int ShownId { get; private set; } = -1;

    private bool _over;
    private float _hover, _flip;

    void Awake()
    {
        if (Visual == null) Visual = (RectTransform)transform;
        if (PickButton != null) PickButton.onClick.AddListener(() => Picked?.Invoke(this));
        if (RerollButton != null) RerollButton.onClick.AddListener(() => Rerolled?.Invoke(this));
    }

    void OnDisable()
    {
        _over = false;
        _hover = 0f;
        _flip = 0f;
        ShownId = -1;
        if (Visual != null) Visual.localScale = Vector3.one;
    }

    public void OnPointerEnter(PointerEventData e) => _over = true;
    public void OnPointerExit(PointerEventData e) => _over = false;

    /// <summary>내용 채우기 — 이미 보이던 카드가 바뀌면 뒤집기 연출</summary>
    public void Bind(AbilityData a)
    {
        int id = a != null ? a.Id : 0;
        if (id == ShownId) return;
        if (ShownId > 0) _flip = 1f;
        ShownId = id;

        Color accent = AbilityUi.Accent(a);
        if (AccentTargets != null)
            foreach (var g in AccentTargets) if (g != null) g.color = new Color(accent.r, accent.g, accent.b, g.color.a > 0f ? g.color.a : 1f);
        if (Glow != null) Glow.color = new Color(accent.r, accent.g, accent.b, 0f);
        if (IconBack != null) IconBack.color = Color.Lerp(accent, Color.black, 0.35f);

        if (Icon != null)
        {
            Icon.sprite = a != null ? a.Icon : null;
            Icon.enabled = Icon.sprite != null;
        }

        if (NamePlate != null) NamePlate.color = accent;
        if (Name != null)
        {
            Name.text = AbilityUi.Name(a);
            Name.color = AbilityUi.NameColor(a);
            if (FitPlate && NamePlate != null)
            {
                var rt = NamePlate.rectTransform;
                float w = Mathf.Clamp(Name.GetPreferredValues(Name.text).x + PlatePadding, PlateMin, PlateMax);
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            }
        }
        if (Number != null)
        {
            Number.text = a != null ? AbilityUi.NumberText(a) : "";
            Number.color = AbilityUi.NumberColor(a);
        }
        if (Summary != null) Summary.text = AbilityUi.Summary(a);
        if (Details != null) Details.text = AbilityUi.Details(a);
    }

    /// <summary>새로고침 버튼 — show: 보일지, can: 누를 수 있는지, left: 남은 횟수</summary>
    public void SetReroll(bool show, bool can, int left)
    {
        if (RerollButton == null) return;
        if (RerollButton.gameObject.activeSelf != show) RerollButton.gameObject.SetActive(show);
        if (!show) return;

        RerollButton.interactable = can;
        if (RerollBack != null) RerollBack.color = can ? RerollOn : RerollOff;
        if (RerollText != null)
        {
            RerollText.text = LocUtil.GetOr("ui.classpick.reroll", "↻ 새로고침 ({0})").Replace("{0}", left.ToString());
            RerollText.alpha = can ? 1f : 0.35f;
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _hover = Mathf.MoveTowards(_hover, _over ? 1f : 0f, dt * 8f);
        _flip = Mathf.MoveTowards(_flip, 0f, dt * 4f);

        float s = Mathf.Lerp(1f, HoverScale, _hover);
        float sx = _flip > 0f ? Mathf.Abs(Mathf.Cos(_flip * Mathf.PI)) : 1f;     // 가로로 접혔다 펴짐
        if (Visual != null) Visual.localScale = new Vector3(s * sx, s, 1f);

        if (Glow != null) { var c = Glow.color; c.a = 0.35f * _hover; Glow.color = c; }
    }
}
