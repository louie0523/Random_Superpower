using UnityEngine;

/// <summary>
/// UI 색 배합 — 코드로 만드는 모든 창 · HUD가 여기서 색을 가져감 (GameUiKit).
/// Resources 폴더에 이름 그대로 'UiTheme'으로 두면 그걸 씀 (없으면 '슬레이트' 기본값).
/// 만들기 · 열기: Tools → UI → 색 배합(테마) 열기
/// 바꾼 뒤: 프리팹 화면은 UI 프리팹 만들기 창에서 '교체'로 다시 만들기 · 코드로 만드는 창은 다음 실행부터
/// 인스펙터 오른쪽 위 ⋮ (또는 우클릭) → 프리셋 고르기
/// </summary>
[CreateAssetMenu(menuName = "Game/UI/UI Theme", fileName = "UiTheme")]
public class UiTheme : ScriptableObject
{
    [Header("바탕 · 판")]
    [Tooltip("로비 같은 전체 화면 바탕")]
    public Color Background = new(0.2f, 0.227f, 0.271f, 1f);
    [Tooltip("창 바탕")]
    public Color Panel = new(0.239f, 0.271f, 0.322f, 0.97f);
    [Tooltip("창 안의 칸 (설정 한 줄 등)")]
    public Color Panel2 = new(0.278f, 0.314f, 0.376f, 1f);
    [Tooltip("보통 버튼 · 목록 한 줄")]
    public Color Raised = new(0.322f, 0.361f, 0.427f, 1f);
    [Tooltip("입력 칸 · 막대 바닥 (판보다 어둡게)")]
    public Color Field = new(0.165f, 0.188f, 0.224f, 1f);
    [Tooltip("구분선 · 꺼진 체크칸")]
    public Color Line = new(0.416f, 0.455f, 0.525f, 1f);
    [Tooltip("뒤 화면 어둡게 (창이 열렸을 때)")]
    public Color Backdrop = new(0.02f, 0.025f, 0.035f, 0.6f);

    [Header("글자")]
    [Tooltip("판 위의 글자")]
    public Color Text = new(0.969f, 0.965f, 0.949f, 1f);
    [Tooltip("흐린 글자 (설명 · 표 머리)")]
    public Color TextDim = new(0.765f, 0.788f, 0.824f, 1f);
    [Tooltip("밝은 면(민트 · 노랑 버튼) 위의 글자")]
    public Color TextOnLight = new(0.051f, 0.059f, 0.071f, 1f);
    [Tooltip("어두운 면 위 글자 (밝은 테마에서도 어두운 버튼이면 이 색)")]
    public Color TextOnDark = new(0.969f, 0.965f, 0.949f, 1f);
    [Tooltip("면이 이보다 밝으면 '밝은 면'으로 봄 (0~1)")]
    [Range(0.2f, 0.8f)] public float LightFaceThreshold = 0.4f;

    [Header("강조 — 쓰임새 고정")]
    [Tooltip("기본 강조 · 주요 버튼 · 켜짐")]
    public Color Accent = new(0.306f, 0.839f, 0.745f, 1f);
    [Tooltip("켜진 체크 줄 바탕 (강조색을 어둡게)")]
    public Color AccentDark = new(0.16f, 0.33f, 0.31f, 1f);
    [Tooltip("랜덤 능력 · SC · 소울 · 뽑기 · 방장")]
    public Color Gold = new(1f, 0.831f, 0.278f, 1f);
    [Tooltip("위험 · 체력 · 강퇴")]
    public Color Danger = new(1f, 0.302f, 0.369f, 1f);
    [Tooltip("준비 · 성공")]
    public Color Ok = new(0.373f, 0.827f, 0.541f, 1f);
    [Tooltip("보조 정보")]
    public Color Info = new(0.357f, 0.784f, 1f, 1f);
    [Tooltip("스티커 흰 테두리")]
    public Color StickerRim = new(0.957f, 0.953f, 0.933f, 1f);

    [Header("게임 HUD")]
    [Tooltip("HUD 글자 판 (반투명) — 게임 화면 위")]
    public Color HudPanel = new(0.07f, 0.08f, 0.1f, 0.62f);
    [Tooltip("쉴드 — 재생 · 소울 · 소모품")]
    public Color ShieldRegen = new(0.96f, 0.97f, 1f, 0.95f);
    public Color ShieldSoul = new(0.55f, 0.82f, 1f, 0.95f);
    public Color ShieldTemp = new(0.33f, 0.58f, 1f, 0.95f);

    [Header("모양")]
    [Tooltip("스티커 · 스킬 칸 · 목표 칩을 살짝 기울임 (꺼짐 = 반듯하게)")]
    public bool TiltStickers = false;

    // ══════════════════ v7 카툰 그래피티 — 메뉴 창 (게임 HUD는 위 색 그대로) ══════════════════

    [Header("v7 그래피티 — 메뉴 창 (HUD 빼고 전부)")]
    [Tooltip("켜짐 = 설정 · 로비 · 인벤토리 · F1 · Tab 등이 v7 모양 (외곽선 · 스프레이). 꺼짐 = 예전 v2 모양")]
    public bool Graffiti = true;
    [Tooltip("로비 같은 전체 화면 바탕 (시멘트 벽)")]
    public Color GWall = new(0.106f, 0.090f, 0.141f, 1f);          // #1B1724
    [Tooltip("창 · 판 (검정에 가까운 보라)")]
    public Color GPanel = new(0.086f, 0.071f, 0.114f, 0.97f);      // #16121D
    [Tooltip("판 안의 줄 · 카드")]
    public Color GRow = new(0.165f, 0.137f, 0.212f, 1f);           // #2A2336
    [Tooltip("보통 버튼 · 꺼진 고르기")]
    public Color GButton = new(0.231f, 0.196f, 0.290f, 1f);        // #3B324A
    [Tooltip("입력 칸 · 막대 바닥 (판보다 어둡게)")]
    public Color GDeep = new(0.063f, 0.051f, 0.082f, 1f);          // #100D15
    [Tooltip("종이 (입력 칸 · 키 캡 · 흰 스티커)")]
    public Color GPaper = new(0.969f, 0.953f, 0.918f, 1f);         // #F7F3EA
    [Tooltip("외곽선 · 밝은 면 위 글자")]
    public Color GInk = new(0.067f, 0.067f, 0.067f, 1f);           // #111111
    [Tooltip("판 위 글자 / 흐린 글자")]
    public Color GText = new(0.969f, 0.953f, 0.918f, 1f);
    public Color GTextDim = new(0.725f, 0.698f, 0.769f, 1f);       // #B9B2C4
    [Tooltip("고르기 · 켜짐 · 준비 (민트)")]
    public Color GTeal = new(0.133f, 0.890f, 0.769f, 1f);          // #22E3C4
    [Tooltip("켜진 줄 바탕 · 고른 칸 (민트를 어둡게)")]
    public Color GTealDark = new(0.078f, 0.420f, 0.376f, 1f);      // #146B60
    [Tooltip("실행 버튼 (시작 · 참가) · 소울 · 방장")]
    public Color GYellow = new(1f, 0.824f, 0.122f, 1f);            // #FFD21F
    [Tooltip("고른 탭 스프레이 · 장식")]
    public Color GPink = new(1f, 0.180f, 0.533f, 1f);              // #FF2E88
    public Color GPurple = new(0.482f, 0.235f, 1f, 1f);            // #7B3CFF
    public Color GOrange = new(1f, 0.541f, 0.122f, 1f);            // #FF8A1F
    [Tooltip("줄무늬 파란 판 (무기 · 라인업 · 방 만들기)")]
    public Color GBlue = new(0.184f, 0.482f, 1f, 1f);              // #2F7BFF
    [Tooltip("빨간 이름표 · 위험")]
    public Color GRed = new(1f, 0.231f, 0.306f, 1f);               // #FF3B4E
    [Tooltip("게임 위에 창이 열렸을 때 뒤 어둡게")]
    public Color GBackdrop = new(0.047f, 0.039f, 0.063f, 0.72f);
    [Tooltip("면이 이보다 밝으면 검정 글자 (v7)")]
    [Range(0.2f, 0.8f)] public float GLightFace = 0.5f;
    [Tooltip("전체 화면 바탕에 스프레이 · 물감 장식")]
    public bool GDecor = true;

    // ══════════════════ 찾기 ══════════════════

    static UiTheme _cur;

    /// <summary>지금 테마 — Resources/UiTheme, 없으면 기본값(슬레이트)</summary>
    public static UiTheme Current
    {
        get
        {
            if (_cur != null) return _cur;
            _cur = Resources.Load<UiTheme>("UiTheme");
            if (_cur == null)
            {
                _cur = CreateInstance<UiTheme>();
                _cur.hideFlags = HideFlags.DontSave;
            }
            return _cur;
        }
    }

    /// <summary>에디터에서 값을 바꾼 뒤 다시 읽게</summary>
    public static void Reload() => _cur = null;

#if UNITY_EDITOR
    void OnValidate() { if (_cur == this) _cur = null; }
#endif

    // ══════════════════ 프리셋 ══════════════════

    static Color H(string hex, float a = 1f)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        c.a = a;
        return c;
    }

    void Common()
    {
        TextOnLight = H("#0D0F12");
        TextOnDark = H("#F7F6F2");
        Gold = H("#FFD447");
        Danger = H("#FF4D5E");
        Ok = H("#5FD38A");
        Info = H("#5BC8FF");
        StickerRim = H("#F4F3EE");
        ShieldRegen = H("#F5F7FF", 0.95f);
        ShieldSoul = H("#8CD1FF", 0.95f);
        ShieldTemp = H("#5494FF", 0.95f);
        LightFaceThreshold = 0.4f;
    }

    [ContextMenu("프리셋: 슬레이트 (기본 · 중간 밝기)")]
    public void PresetSlate()
    {
        Common();
        Background = H("#333A45"); Panel = H("#3D4552", 0.97f); Panel2 = H("#475060"); Raised = H("#525C6D");
        Field = H("#2A3039"); Line = H("#6A7486"); Backdrop = H("#05070A", 0.6f);
        Text = H("#F7F6F2"); TextDim = H("#C3C9D2");
        Accent = H("#4ED6BE"); AccentDark = H("#29544F");
        HudPanel = H("#12141A", 0.62f);
        Touch();
    }

    [ContextMenu("프리셋: 다크 (어둡게)")]
    public void PresetDark()
    {
        Common();
        Background = H("#15181D"); Panel = H("#1D2127", 0.97f); Panel2 = H("#262C34"); Raised = H("#2B323C");
        Field = H("#121418"); Line = H("#3A414B"); Backdrop = H("#05060A", 0.74f);
        Text = H("#F4F3EE"); TextDim = H("#A9B0BA");
        Accent = H("#4ED6BE"); AccentDark = H("#1F3C37");
        HudPanel = H("#0D0F12", 0.7f);
        Touch();
    }

    [ContextMenu("프리셋: 네이비 (푸른 어둠)")]
    public void PresetNavy()
    {
        Common();
        Background = H("#1E2738"); Panel = H("#253047", 0.97f); Panel2 = H("#2D3953"); Raised = H("#36445F");
        Field = H("#18202E"); Line = H("#4A5875"); Backdrop = H("#060912", 0.65f);
        Text = H("#F3F6FB"); TextDim = H("#AEB9CC");
        Accent = H("#58D6C4"); AccentDark = H("#24485A");
        HudPanel = H("#101626", 0.64f);
        Touch();
    }

    [ContextMenu("프리셋: 라이트 (밝은 회색 · 흰 판)")]
    public void PresetLight()
    {
        Common();
        Background = H("#DDE2E8"); Panel = H("#F7F8FA", 0.98f); Panel2 = H("#EBEEF2"); Raised = H("#DFE4EA");
        Field = H("#D2D8E0"); Line = H("#B8C0CB"); Backdrop = H("#1A1E25", 0.45f);
        Text = H("#1A1D22"); TextDim = H("#5A6370");
        Accent = H("#2FBFA5"); AccentDark = H("#BFE9E1");
        HudPanel = H("#12141A", 0.6f);
        Touch();
    }

    [ContextMenu("프리셋: v7 그래피티 색 되돌리기")]
    public void PresetGraffiti()
    {
        Graffiti = true;
        GWall = H("#1B1724"); GPanel = H("#16121D", 0.97f); GRow = H("#2A2336"); GButton = H("#3B324A");
        GDeep = H("#100D15"); GPaper = H("#F7F3EA"); GInk = H("#111111");
        GText = H("#F7F3EA"); GTextDim = H("#B9B2C4");
        GTeal = H("#22E3C4"); GTealDark = H("#146B60"); GYellow = H("#FFD21F"); GPink = H("#FF2E88");
        GPurple = H("#7B3CFF"); GOrange = H("#FF8A1F"); GBlue = H("#2F7BFF"); GRed = H("#FF3B4E");
        GBackdrop = H("#0C0A10", 0.72f); GLightFace = 0.5f; GDecor = true;
        Touch();
    }

    void Touch()
    {
        _cur = null;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
