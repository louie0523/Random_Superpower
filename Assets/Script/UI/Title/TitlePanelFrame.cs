using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀의 큰 창 하나 (무기 보기 · 스킨/뽑기 · 도감 · 프로필 · 꾸미기) — 위 탭 · 가운데 카드 격자 · 오른쪽(아래) 자세히.
/// 내용은 창마다 붙은 TitlePanel이 채움. 탭 · 카드는 숨겨 둔 견본을 복사해서 씀
/// </summary>
public class TitlePanelFrame : MonoBehaviour
{
    [Header("위")]
    public TMP_Text Title;
    public Button Back;

    [Header("탭")]
    public RectTransform TabRow;
    public UiOption TabTemplate;

    [Header("카드 격자")]
    public RectTransform Grid;
    public ScrollRect GridScroll;
    public UiItemCard CardTemplate;
    [Tooltip("카드가 없을 때 글")]
    public TMP_Text Empty;

    [Header("자세히")]
    [Tooltip("3D 모델 칸 (꾸미기 창은 없음 — 가운데 캐릭터가 대신)")]
    public RawImage Preview;
    public ModelViewport Viewport;
    public Image PreviewIcon;
    [Tooltip("그림 칸 바탕 스프레이 — 능력이면 그 능력 색 (없으면 원래 색)")]
    public Image PreviewPaint;
    public TMP_Text Name;
    public TMP_Text Sub;
    public TMP_Text Body;
    public ScrollRect BodyScroll;
    [Tooltip("버튼 줄 — 버튼이 둘 다 없으면 숨김 (설명 칸이 그만큼 커짐)")]
    public GameObject ActionRow;
    public Button ActionA;
    public TMP_Text ActionALabel;
    public Button ActionB;
    public TMP_Text ActionBLabel;
    [Tooltip("버튼 아래 작은 글 (돈 모자람 등)")]
    public TMP_Text Note;

    public event Action Closed;

    readonly List<UiOption> _tabs = new();
    readonly List<UiItemCard> _cards = new();
    Action<int> _onTab;
    Action _a, _b;

    public bool IsOpen => gameObject.activeSelf;
    Color _paintDefault;
    bool _paintSaved;
    public IReadOnlyList<UiItemCard> Cards => _cards;

    void Awake()
    {
        if (PreviewPaint == null && Preview != null && Preview.transform.parent != null)
        {
            var t = Preview.transform.parent.Find("Back/SprayFill");       // 예전 프리팹
            if (t != null) PreviewPaint = t.GetComponent<Image>();
        }
        if (TabTemplate != null) TabTemplate.gameObject.SetActive(false);
        if (CardTemplate != null) CardTemplate.gameObject.SetActive(false);
        if (Back != null) Back.onClick.AddListener(Close);
        if (ActionA != null) ActionA.onClick.AddListener(() => _a?.Invoke());
        if (ActionB != null) ActionB.onClick.AddListener(() => _b?.Invoke());
    }

    public void Open() => gameObject.SetActive(true);

    public void Close()
    {
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        Closed?.Invoke();
    }

    // ── 탭 ──

    public void Tabs(IReadOnlyList<string> labels, int current, Action<int> pick)
    {
        _onTab = pick;
        if (TabTemplate == null) return;
        while (_tabs.Count < labels.Count)
        {
            var t = Instantiate(TabTemplate, TabTemplate.transform.parent);
            int idx = _tabs.Count;
            t.Button.onClick.AddListener(() => { SetTab(idx); _onTab?.Invoke(idx); });
            _tabs.Add(t);
        }
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool on = i < labels.Count;
            _tabs[i].gameObject.SetActive(on);
            if (on) _tabs[i].SetText(labels[i]);
        }
        SetTab(current);
    }

    public void SetTabText(int i, string label)
    {
        if (i >= 0 && i < _tabs.Count) _tabs[i].SetText(label);
    }

    public void SetTab(int i)
    {
        for (int k = 0; k < _tabs.Count; k++) _tabs[k].SetOn(k == i);
    }

    // ── 카드 ──

    /// <summary>카드 n개만 보이게 (모자라면 견본 복사) — 돌려준 목록을 채우면 됨</summary>
    public IReadOnlyList<UiItemCard> Layout(int n, Action<UiItemCard> click)
    {
        if (CardTemplate != null)
            while (_cards.Count < n)
            {
                var c = Instantiate(CardTemplate, Grid);
                if (c.Button != null) c.Button.onClick.AddListener(() => _click?.Invoke(c));
                _cards.Add(c);
            }
        _click = click;
        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = i < n;
            if (_cards[i].gameObject.activeSelf != on) _cards[i].gameObject.SetActive(on);
            _cards[i].SetSelected(false);
            _cards[i].Id = 0;
            _cards[i].Data = null;
        }
        if (Empty != null) Empty.gameObject.SetActive(n == 0 && !string.IsNullOrEmpty(Empty.text));
        if (GridScroll != null) GridScroll.verticalNormalizedPosition = 1f;
        return _cards;
    }

    Action<UiItemCard> _click;

    public void Select(UiItemCard card)
    {
        foreach (var c in _cards) c.SetSelected(c == card);
    }

    public void SetEmpty(string text)
    {
        if (Empty == null) return;
        Empty.text = text ?? "";
    }

    // ── 자세히 ──

    /// <summary>자세히 칸 비우기</summary>
    public void Clear()
    {
        ShowModel(null);
        ShowIcon(null, false);
        Set(Name, "");
        Set(Sub, "");
        SetBody("");
        SetNote("");
        SetPaint(null);
        Action1(null, null);
        Action2(null, null);
    }

    /// <summary>그림 칸 바탕 스프레이 색 (null = 원래 색) — 그림(아이콘)은 흰색 그대로</summary>
    public void SetPaint(Color? c)
    {
        if (PreviewPaint == null) return;
        if (!_paintSaved) { _paintDefault = PreviewPaint.color; _paintSaved = true; }
        PreviewPaint.color = c.HasValue ? new Color(c.Value.r, c.Value.g, c.Value.b, 0.85f) : _paintDefault;
    }

    public void ShowModel(GameObject prefab, WeaponSkinData skin = null)
    {
        if (Preview != null) Preview.uvRect = new Rect(0f, 0f, 1f, 1f);
        if (Viewport != null) Viewport.Show(prefab, skin);
        else if (Preview != null) Preview.enabled = false;
        if (prefab != null && PreviewIcon != null) PreviewIcon.enabled = false;
    }

    /// <summary>그림 한 장 (잠김 = 검은 실루엣)</summary>
    public void ShowIcon(Sprite icon, bool locked)
    {
        if (PreviewIcon == null) return;
        PreviewIcon.sprite = icon;
        PreviewIcon.enabled = icon != null;
        PreviewIcon.color = locked ? new Color(0f, 0f, 0f, 0.85f) : Color.white;
        if (icon != null && Viewport != null) Viewport.Show(null);
    }

    /// <summary>모델 대신 사진 (프로필)</summary>
    public void ShowTexture(Texture tex)
    {
        if (Viewport != null) Viewport.Show(null);
        if (Preview == null) return;
        Preview.texture = tex;
        Preview.enabled = tex != null;
        Preview.color = Color.white;
        Preview.uvRect = Crop(tex, Preview.rectTransform.rect);
        if (tex != null && PreviewIcon != null) PreviewIcon.enabled = false;
    }

    /// <summary>그림 비율 ≠ 칸 비율이면 가운데를 잘라 맞춤 (옆으로 눌려 보이지 않게)</summary>
    static Rect Crop(Texture tex, Rect box)
    {
        if (tex == null || box.width < 1f || box.height < 1f || tex.height == 0) return new Rect(0f, 0f, 1f, 1f);
        float ta = tex.width / (float)tex.height, ba = box.width / box.height;
        if (ta > ba)
        {
            float w = ba / ta;
            return new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        float h = ta / ba;
        return new Rect(0f, 1f - h, 1f, h);      // 세로로 길면 위쪽(얼굴) 기준
    }

    public void Action1(string label, Action click, bool interactable = true)
    {
        SetAction(ActionA, ActionALabel, label, click, interactable, ref _a);
        RowVisible();
    }

    public void Action2(string label, Action click, bool interactable = true)
    {
        SetAction(ActionB, ActionBLabel, label, click, interactable, ref _b);
        RowVisible();
    }

    void RowVisible()
    {
        if (ActionRow == null) return;
        bool on = (ActionA != null && ActionA.gameObject.activeSelf) || (ActionB != null && ActionB.gameObject.activeSelf);
        if (ActionRow.activeSelf != on) ActionRow.SetActive(on);
    }

    /// <summary>설명 — 바뀌면 맨 위부터</summary>
    public void SetBody(string s)
    {
        Set(Body, s);
        if (BodyScroll != null && BodyScroll.content != null)
        {
            BodyScroll.velocity = Vector2.zero;
            BodyScroll.content.anchoredPosition = Vector2.zero;     // 위 기준(pivot 위) → 맨 위
        }
    }

    /// <summary>버튼 아래 작은 글 — 비면 칸도 숨김</summary>
    public void SetNote(string s)
    {
        if (Note == null) return;
        Note.text = s ?? "";
        bool on = !string.IsNullOrEmpty(s);
        if (Note.gameObject.activeSelf != on) Note.gameObject.SetActive(on);
    }

    static void SetAction(Button b, TMP_Text t, string label, Action click, bool interactable, ref Action slot)
    {
        slot = click;
        if (b == null) return;
        bool on = !string.IsNullOrEmpty(label);
        b.gameObject.SetActive(on);
        b.interactable = interactable;
        if (t != null && on) t.text = label;
    }

    public static void Set(TMP_Text t, string s)
    {
        if (t != null) t.text = s ?? "";
    }
}

/// <summary>타이틀 창 내용 — TitlePanelFrame 하나를 채움</summary>
public abstract class TitlePanel : MonoBehaviour
{
    public TitlePanelFrame Frame;

    public bool IsOpen => Frame != null && Frame.IsOpen;

    protected virtual void Awake()
    {
        if (Frame == null) Frame = GetComponent<TitlePanelFrame>();
        if (Frame != null) Frame.Closed += OnClosed;
        PlayerSave.Changed += OnSaveChanged;
    }

    protected virtual void OnDestroy() => PlayerSave.Changed -= OnSaveChanged;

    public virtual void Show()
    {
        Frame.Open();
        Refresh();
    }

    public void Hide() => Frame.Close();

    protected abstract void Refresh();
    protected virtual void OnClosed() { }
    void OnSaveChanged() { if (IsOpen) OnSave(); }
    /// <summary>저장(SC · 가진 것 · 장착)이 바뀜 — 보이는 카드 표시만 다시</summary>
    protected virtual void OnSave() { }

    protected static GameMeta Meta => GameMeta.Get();

    protected static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    /// <summary>받침 있으면 a, 없으면 b — 을/를 · 이/가</summary>
    protected static string Josa(string word, string a, string b)
    {
        if (string.IsNullOrEmpty(word)) return b;
        char c = word[word.Length - 1];
        if (c < 0xAC00 || c > 0xD7A3) return a + "(" + b + ")";
        return (c - 0xAC00) % 28 != 0 ? a : b;
    }

    protected static string Pct(float f) => Mathf.RoundToInt(f * 100f) + "%";
}
