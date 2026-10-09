using System.Collections.Generic;
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
        public string BodyFull;
        public string Detail;
        public Color Accent;

        public bool ShowRank;
        public int Level;
        public int MaxLevel;
        public bool CanUpgrade;


    }

    [SerializeField] private Image background;
    [SerializeField] private Image accentBar;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text slotLabel;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text body;

    [Header("레벨")]
    [Tooltip("점들이 들어 있는 줄. 자식 Image를 왼쪽부터 점으로 사용. 모자라면 첫 점을 복제")]
    [SerializeField] private RectTransform pipParent;
    [SerializeField] private Color pipOn = Color.white;
    [SerializeField] private Color pipOff = new(0.35f, 0.35f, 0.35f, 1f);
    [SerializeField] private Button upgradeButton;

    [Header("색")]
    [SerializeField] private Color normalColor = new(1f, 1f, 1f, 0.04f);
    [SerializeField] private Color hoverColor = new(1f, 1f, 1f, 0.10f);
    [SerializeField] private Color selectedColor = new(1f, 1f, 1f, 0.16f);

    public event System.Action<AbilityEntryView> Clicked;
    public event System.Action<AbilityEntryView> UpgradeClicked;

    public Model Data { get; private set; }
    public bool HasDetail { get; private set; }

    private bool _selected;
    private bool _init;
    private CanvasGroup _upgradeGroup;
    private readonly List<Image> _pips = new();

    void Awake() => EnsureInit();

    /// <summary>Awake 전에 Bind가 불려도 안전하게 (프리팹이 꺼진 채 복제된 경우 등)</summary>
    void EnsureInit()
    {
        if (_init) return;
        _init = true;

        if (background == null) background = GetComponent<Image>();

        _pips.Clear();
        if (pipParent != null)
            for (int i = 0; i < pipParent.childCount; i++)
                if (pipParent.GetChild(i).TryGetComponent<Image>(out var img))
                    _pips.Add(img);

        if (upgradeButton != null)
        {
            upgradeButton.onClick.AddListener(() => UpgradeClicked?.Invoke(this));
            _upgradeGroup = upgradeButton.GetComponent<CanvasGroup>();
            if (_upgradeGroup == null) _upgradeGroup = upgradeButton.gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void Bind(Model m)
    {
        EnsureInit();

        Data = m;
        HasDetail = !string.IsNullOrWhiteSpace(m.Detail)
                 || !string.IsNullOrWhiteSpace(m.Body);

        if (icon != null)
        {
            icon.enabled = m.Icon != null;
            icon.sprite = m.Icon;
        }
        if (accentBar != null)
        {
            bool hasAccent = m.Accent.a > 0.01f;
            accentBar.gameObject.SetActive(hasAccent);
            if (hasAccent) accentBar.color = m.Accent;
        }

        SetText(slotLabel, m.SlotLabel);
        SetText(title, m.Title);
        SetText(body, m.Body);

        BindRank(m);
        SetSelected(false);
    }

    // ══════════════════ 레벨 점 ══════════════════

    void EnsurePips(int count)
    {
        if (pipParent == null || _pips.Count == 0) return;

        var source = _pips[0];
        while (_pips.Count < count)
        {
            var clone = Instantiate(source, pipParent);
            clone.name = $"{source.name}_{_pips.Count + 1}";
            _pips.Add(clone);
        }
    }

    void BindRank(Model m)
    {
        if (pipParent != null) pipParent.gameObject.SetActive(m.ShowRank);

        if (m.ShowRank)
        {
            EnsurePips(m.MaxLevel);

            for (int i = 0; i < _pips.Count; i++)
            {
                var pip = _pips[i];
                if (pip == null) continue;

                bool used = i < m.MaxLevel;              // 최대 레벨 넘는 점은 끔
                pip.gameObject.SetActive(used);
                if (used) pip.color = i < m.Level ? pipOn : pipOff;
            }
        }

        if (upgradeButton != null)
        {
            // 레벨 있는 카드는 버튼 자리 유지 → 업그레이드 가능 여부로 레이아웃이 안 흔들림
            upgradeButton.gameObject.SetActive(m.ShowRank);
            upgradeButton.interactable = m.CanUpgrade;

            if (_upgradeGroup != null)
            {
                _upgradeGroup.alpha = m.CanUpgrade ? 1f : 0f;
                _upgradeGroup.blocksRaycasts = m.CanUpgrade;
            }
        }
    }

    // ══════════════════ 선택 · 클릭 ══════════════════

    public void SetSelected(bool on)
    {
        _selected = on;
        Paint(false);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (!HasDetail) return;
        Clicked?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData e) { if (HasDetail) Paint(true); }
    public void OnPointerExit(PointerEventData e) => Paint(false);

    void Paint(bool hover)
    {
        if (background == null) return;
        background.color = _selected ? selectedColor : hover ? hoverColor : normalColor;
    }

    static void SetText(TMP_Text t, string s)
    {
        if (t == null) return;
        bool has = !string.IsNullOrWhiteSpace(s);
        t.text = s ?? "";
        t.gameObject.SetActive(has);
    }
}