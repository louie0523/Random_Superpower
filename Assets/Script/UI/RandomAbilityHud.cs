using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랜덤 능력 HUD — 왼쪽 위 소울 아래. 조커처럼 랜덤 능력이 둘이면 두 줄.
/// 세팅
///   1. HUD 캔버스 안, 소울 표시 아래에 이 컴포넌트가 붙은 오브젝트 (프리팹 만들기 메뉴로 만든 RandomAbilityHud 프리팹을 끌어다 놓아도 됨)
///   2. Row Prefab = RandomAbilityRowView가 붙은 한 줄 프리팹 (비우면 기본 모양)
///   3. Row Parent = 줄이 들어갈 곳 (비우면 이 오브젝트). Vertical Layout Group이 있으면 그걸로 쌓고, 없으면 코드가 위에서부터 쌓음
/// </summary>
public class RandomAbilityHud : MonoBehaviour
{
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

    private RandomAbilityRowView[] _rows;
    private bool _manualStack;

    void Awake()
    {
        if (rowParent == null) rowParent = transform as RectTransform;

        // 디자인용으로 넣어 둔 줄은 지움
        for (int i = rowParent.childCount - 1; i >= 0; i--)
        {
            var child = rowParent.GetChild(i);
            if (child.GetComponent<RandomAbilityRowView>() != null) Destroy(child.gameObject);
        }

        RandomAbilityRowView template = rowPrefab;
        GameObject temp = null;
        if (template == null)
        {
            temp = new GameObject("RowTemplate");
            temp.SetActive(false);
            temp.transform.SetParent(transform, false);
            var st = ClassPickUiBuilder.RowStyle.Default;
            st.Width = width; st.Height = rowHeight; st.IconSize = iconSize;
            st.NameSize = nameSize; st.NumberSize = numberSize; st.SummarySize = summarySize;
            st.Background = background;
            template = ClassPickUiBuilder.BuildRow(temp.transform, font, st);
        }

        _rows = new RandomAbilityRowView[2];
        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i] = Instantiate(template, rowParent);
            _rows[i].name = "RandomAbility_" + i;
            _rows[i].gameObject.SetActive(false);
        }
        if (temp != null) Destroy(temp);

        _manualStack = rowParent.GetComponent<LayoutGroup>() == null;
    }

    void LateUpdate()
    {
        var ar = AbilityDescTokens.Local;
        if (ar != null) AbilityUi.DbOf(ar);         // 진화 번호 계산용
        AbilityData a1 = ar != null ? ar.GetRandomAbility() : null;
        AbilityData a2 = ar != null ? ar.GetRandomAbility2() : null;

        Show(_rows[0], a1);
        Show(_rows[1], a2);

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
                y += rt.rect.height + rowGap;
            }
        }
    }

    static void Show(RandomAbilityRowView r, AbilityData a)
    {
        bool on = a != null;
        if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
        if (on) r.Bind(a);
    }
}