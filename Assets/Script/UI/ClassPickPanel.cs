using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 클래스 선택 화면 — 라운드 시작에 카드 N장, 카드를 누르면 그 클래스로. 카드 밑 새로고침 = 그 카드만 다시 뽑기.
/// 내 ClassPick이 고르는 중일 때만 보임. 보이는 동안 커서 풀림 · 게임 입력 차단(UIManager.InputBlocked에 IsShowing).
///
/// 세팅 방법 (둘 중 하나)
///   A. 프리팹: Tools → UI → '클래스 선택 · 랜덤 능력 프리팹 만들기'로 만든 ClassPickPanel 프리팹을 씬에 놓기 → 마음대로 꾸미기
///   B. 아무것도 안 함: 씬에 없으면 기본 모양으로 스스로 생김
/// 칸 (A일 때 연결되어 나옴)
///   Root      — 켜고 끌 화면 전체 (이 스크립트가 붙은 오브젝트 말고 그 아래 자식)
///   Title · Timer — 제목 · 남은 시간 글
///   Card Parent — 카드가 들어갈 곳 (Horizontal Layout Group 추천). 안에 미리 넣어 둔 카드는 시작할 때 지워짐
///   Card Prefab — ClassCardView가 붙은 카드 프리팹
/// </summary>
public class ClassPickPanel : MonoBehaviour
{
    static ClassPickPanel _inst;

    /// <summary>화면에 떠 있는 중 — 입력 차단용</summary>
    public static bool IsShowing { get; private set; }

    [Header("연결 (비우면 기본 모양을 코드로 만듦)")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text timer;
    [SerializeField] private RectTransform cardParent;
    [SerializeField] private ClassCardView cardPrefab;

    [Header("기본 모양 (위를 비웠을 때만)")]
    [Tooltip("비우면 TMP 기본 글꼴 (한글이 안 나오면 한글 글꼴 넣기)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color dim = new(0f, 0f, 0f, 0.62f);
    [SerializeField] private int sortingOrder = 30500;

    [Header("연출")]
    [Tooltip("나타날 때 카드가 아래에서 올라오는 거리")]
    [SerializeField] private float appearRise = 40f;

    private readonly List<ClassCardView> _cards = new();
    private CanvasGroup _group;
    private Vector2 _cardsBase;
    private float _appear;

    // ══════════════════ 생성 ══════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_inst != null) return;
        if (FindAnyObjectByType<ClassPickPanel>(FindObjectsInactive.Include) != null) return;
        var go = new GameObject("ClassPickPanel");
        DontDestroyOnLoad(go);
        go.AddComponent<ClassPickPanel>();
    }

    void Awake()
    {
        if (_inst != null && _inst != this) { Destroy(gameObject); return; }
        _inst = this;

        // 연결이 비었으면 기본 모양
        if (root == null || cardParent == null)
        {
            var s = ClassPickUiBuilder.BuildScreen(gameObject, font, sortingOrder, dim);
            root = s.Root;
            title = s.Title;
            timer = s.Timer;
            cardParent = s.Cards;
        }

        // 디자인용으로 넣어 둔 카드는 지움
        for (int i = cardParent.childCount - 1; i >= 0; i--)
            Destroy(cardParent.GetChild(i).gameObject);

        // 카드 원본 — 프리팹이 없으면 기본 카드를 하나 만들어 원본으로
        ClassCardView template = cardPrefab;
        GameObject temp = null;
        if (template == null)
        {
            temp = new GameObject("CardTemplate");
            temp.SetActive(false);
            temp.transform.SetParent(transform, false);
            template = ClassPickUiBuilder.BuildCard(temp.transform, font);
        }

        for (int i = 0; i < ClassPick.MaxChoices; i++)
        {
            var c = Instantiate(template, cardParent);
            c.name = "Card_" + i;
            c.Index = i;
            c.gameObject.SetActive(false);
            c.Picked += OnPick;
            c.Rerolled += OnReroll;
            _cards.Add(c);
        }
        if (temp != null) Destroy(temp);

        _group = root.GetComponent<CanvasGroup>();
        if (_group == null) _group = root.AddComponent<CanvasGroup>();
        _cardsBase = cardParent.anchoredPosition;
        root.SetActive(false);
    }

    void OnDestroy()
    {
        if (_inst == this) { _inst = null; IsShowing = false; }
    }

    // ══════════════════ 매 프레임 ══════════════════

    enum Mode { None, Pick, Wait }
    private Mode _mode;

    void LateUpdate()
    {
        var cp = ClassPick.Local;
        bool valid = cp != null && cp.Object != null && cp.Object.IsValid;

        // 내가 고르는 중 → 카드 / 나는 골랐는데 남이 고르는 중 → 대기 (전원 정지)
        Mode want = !valid ? Mode.None
                  : cp.Picking ? Mode.Pick
                  : cp.FreezeUntilAllPicked && ClassPick.AnyonePicking ? Mode.Wait
                  : Mode.None;

        if (want != _mode) SetMode(want);
        if (_mode == Mode.None) return;

        _appear = Mathf.MoveTowards(_appear, 1f, Time.unscaledDeltaTime * 5f);
        float e = 1f - Mathf.Pow(1f - _appear, 3f);
        _group.alpha = e;

        if (_mode == Mode.Wait)
        {
            int n = ClassPick.PickingCount;
            float wl = ClassPick.MaxTimeLeft;
            if (title != null) title.text = LocUtil.GetOr("ui.classpick.waiting", "다른 플레이어가 고르는 중");
            if (timer != null)
                timer.text = LocUtil.GetOr("ui.classpick.waiting_count", "{0}명 남음").Replace("{0}", n.ToString())
                           + (wl >= 0f ? "  ·  " + Mathf.CeilToInt(wl) + "s" : "");
            return;
        }

        // 커서 — 고르는 동안 계속 풀어 둠 (다른 코드가 잠가도)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        cardParent.anchoredPosition = _cardsBase + Vector2.down * appearRise * (1f - e);

        if (title != null) title.text = LocUtil.GetOr("ui.classpick.title", "클래스 선택");
        if (timer != null)
        {
            float left = cp.TimeLeft;
            timer.text = left >= 0f
                ? LocUtil.GetOr("ui.classpick.timer", "{0}초 뒤 무작위 선택").Replace("{0}", Mathf.CeilToInt(left).ToString())
                : "";
        }

        var db = Db(cp);
        int count = cp.Count;
        bool showReroll = cp.AllowReroll && cp.Rerolls > 0;

        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            int id = i < count ? cp.Choices[i] : 0;
            bool on = id != 0;
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
            if (!on) continue;

            c.Bind(db != null ? db.Get(id) : null);
            c.SetReroll(showReroll, cp.CanReroll, cp.RerollsLeft);
        }
    }

    void SetMode(Mode m)
    {
        var prev = _mode;
        _mode = m;
        IsShowing = m != Mode.None;         // 대기 중에도 게임 입력 막힘 (UIManager.InputBlocked)
        root.SetActive(m != Mode.None);
        cardParent.gameObject.SetActive(m == Mode.Pick);

        if (m != Mode.None && prev == Mode.None) _appear = 0f;

        if (m == Mode.Pick && EventSystem.current == null)
            Debug.LogWarning("[ClassPick] 씬에 EventSystem이 없어서 카드를 누를 수 없음");

        // 카드 고르기가 끝나면 커서 다시 잠금 (다른 창이 열려 있으면 그대로)
        if (prev == Mode.Pick && m != Mode.Pick)
        {
            bool otherUi = UIManager.Instance != null && UIManager.Instance.AnyPanelOpen;
            Cursor.lockState = otherUi ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = otherUi;
        }
    }

    static AbilityDatabase Db(ClassPick cp) => AbilityUi.DbOf(cp.GetComponent<AbilityRuntime>());

    // ══════════════════ 버튼 ══════════════════

    void OnPick(ClassCardView c)
    {
        var cp = ClassPick.Local;
        if (cp == null || !cp.Picking) return;
        cp.Choose(c.Index);
    }

    void OnReroll(ClassCardView c)
    {
        var cp = ClassPick.Local;
        if (cp == null || !cp.CanReroll) return;
        cp.Reroll(c.Index);
    }
}