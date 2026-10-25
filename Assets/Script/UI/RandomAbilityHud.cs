using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랜덤 능력 HUD — 왼쪽 위 소울 아래. 조커처럼 랜덤 능력이 둘이면 두 줄.
/// 그 아래에 추가 줄 (HudExtras — 복수 시간 · 데스 노트 등, 능력이 바뀌어도 남는 효과).
/// 능력 줄의 요약(한 줄 요약 칸)에도 설명처럼 {x:키} · {b:필드}를 쓸 수 있음 — 지금 상태로 계속 바뀜
///   예) 필연: "궁극기를 5번 사용하면 … (현재 {x:ult}회)"
/// 글꼴 · 글자 크기 · 요약 줄 수는 Style(RandomAbilityHudStyle)에서.
/// 세팅
///   1. HUD 캔버스 안, 소울 표시 아래에 이 컴포넌트가 붙은 오브젝트 (프리팹 만들기 메뉴로 만든 RandomAbilityHud 프리팹을 끌어다 놓아도 됨)
///   2. Row Prefab = RandomAbilityRowView가 붙은 한 줄 프리팹 (비우면 기본 모양)
///   3. Row Parent = 줄이 들어갈 곳 (비우면 이 오브젝트). Vertical Layout Group이 있으면 그걸로 쌓고, 없으면 코드가 위에서부터 쌓음
/// </summary>
public class RandomAbilityHud : MonoBehaviour
{
    [Header("모양")]
    [Tooltip("글꼴 · 글자 크기 · 요약 줄 수 — Create → Game → UI → Random Ability HUD Style. 비우면 아래 값 / 줄 프리팹 그대로")]
    [SerializeField] private RandomAbilityHudStyle style;

    [Header("연결 (비우면 기본 모양)")]
    [SerializeField] private RandomAbilityRowView rowPrefab;
    [SerializeField] private RectTransform rowParent;

    [Header("기본 모양 (Row Prefab을 비웠을 때만)")]
    [Tooltip("비우면 TMP 기본 글꼴 (한글이 안 나오면 한글 글꼴 넣기)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float width = 540f;
    [SerializeField] private float rowHeight = 54f;
    [SerializeField] private float iconSize = 44f;
    [SerializeField] private float nameSize = 22f;
    [SerializeField] private float numberSize = 15f;
    [SerializeField] private float summarySize = 18f;
    [SerializeField] private Color background = new(0.05f, 0.06f, 0.09f, 0.72f);

    [Header("쌓기 (Row Parent에 Layout Group이 없을 때)")]
    [SerializeField] private float rowGap = 6f;

    private readonly List<RandomAbilityRowView> _rows = new();
    private readonly List<HudExtraRow> _extras = new();
    private RandomAbilityRowView _template;
    private bool _manualStack;
    private float _liveAt;
    private readonly string[] _live = new string[2];
    private readonly AbilityData[] _liveFor = new AbilityData[2];

    void Awake()
    {
        if (rowParent == null) rowParent = transform as RectTransform;

        // 디자인용으로 넣어 둔 줄은 지움
        for (int i = rowParent.childCount - 1; i >= 0; i--)
        {
            var child = rowParent.GetChild(i);
            if (child.GetComponent<RandomAbilityRowView>() != null) Destroy(child.gameObject);
        }

        _template = rowPrefab;
        if (_template == null)
        {
            // 기본 모양 줄 — 꺼 둔 채 남겨 두고 필요할 때마다 복제
            var holder = new GameObject("RowTemplate");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            var st = ClassPickUiBuilder.RowStyle.Default;
            st.Width = width; st.Height = rowHeight; st.IconSize = iconSize;
            st.NameSize = nameSize; st.NumberSize = numberSize; st.SummarySize = summarySize;
            st.Background = background;
            _template = ClassPickUiBuilder.BuildRow(holder.transform, style != null && style.Font != null ? style.Font : font, st);
        }

        Row(0);
        Row(1);
        _manualStack = rowParent.GetComponent<LayoutGroup>() == null;
    }

    RandomAbilityRowView Row(int i)
    {
        while (_rows.Count <= i)
        {
            var r = Instantiate(_template, rowParent);
            r.name = "RandomAbility_" + _rows.Count;
            if (style != null)
            {
                r.MaxSummaryLines = style.MaxSummaryLines;
                r.ApplyFont(style.Font, style.NameSize, style.NumberSize, style.SummarySize);
            }
            r.gameObject.SetActive(false);
            _rows.Add(r);
        }
        return _rows[i];
    }

    void LateUpdate()
    {
        var ar = AbilityDescTokens.Local;
        if (ar != null) AbilityUi.DbOf(ar);         // 진화 번호 계산용
        AbilityData a1 = ar != null ? ar.GetRandomAbility() : null;
        AbilityData a2 = ar != null ? ar.GetRandomAbility2() : null;

        // 요약의 {x:} 상태 글은 조금씩 띄엄띄엄 계산 (정규식이라)
        if (Time.unscaledTime >= _liveAt || a1 != _liveFor[0] || a2 != _liveFor[1])
        {
            _liveFor[0] = a1;
            _liveFor[1] = a2;
            _liveAt = Time.unscaledTime + (style != null ? style.LiveRefresh : 0.2f);
            _live[0] = LiveSummary(a1);
            _live[1] = LiveSummary(a2);
        }

        int used = 0;
        if (a1 != null) ShowAbility(Row(used++), a1, _live[0]);
        if (a2 != null) ShowAbility(Row(used++), a2, _live[1]);

        _extras.Clear();
        HudExtras.Collect(_extras);
        foreach (var e in _extras)
        {
            var r = Row(used++);
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            r.BindExtra(e);
        }

        for (int i = used; i < _rows.Count; i++)
            if (_rows[i].gameObject.activeSelf) _rows[i].gameObject.SetActive(false);

        // Layout Group이 없으면 보이는 줄만 위에서부터
        if (_manualStack)
        {
            float y = 0f;
            foreach (var r in _rows)
            {
                if (!r.gameObject.activeSelf) continue;
                var rt = (RectTransform)r.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -y);
                y += rt.rect.height + (style != null ? style.RowGap : rowGap);
            }
        }
    }

    static void ShowAbility(RandomAbilityRowView r, AbilityData a, string live)
    {
        if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
        r.Bind(a);
        r.SetLiveSummary(live, a);
    }

    /// <summary>
    /// 지금 보여 줄 요약 — 변신 등으로 바뀐 요약(AbilityUi.Summary가 능력에게 물어봄)까지.
    /// {x:} · {b:} 같은 토큰이 있으면 지금 상태로 바꿈
    /// </summary>
    static string LiveSummary(AbilityData a)
    {
        if (a == null) return null;
        string raw = AbilityUi.Summary(a);
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('{') < 0) return raw ?? "";
        return AbilityDescTokens.Apply(raw, a, null, 1, 1);
    }
}
