using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 예시 프리팹 모양 — 에디터 메뉴 'Tools → UI → 방 · 대상 선택 · 보이스 설정 프리팹 만들기'가 씀.
/// 모양을 바꾸려면 만들어진 프리팹을 직접 고치면 됨 (이 코드는 안 건드려도 됨).
/// </summary>
public static class GameUiBuilders
{
    // ══════════════════ 줄 (따로 프리팹) ══════════════════

    public static UiListRow PickRow(Transform parent, TMP_FontAsset font)
    {
        var r = K.ListRow("PlayerPickRow", parent, font, true, null, 64f);
        ((RectTransform)r.transform).sizeDelta = new Vector2(540f, 64f);
        r.Label.text = "플레이어";
        r.Sub.text = "(나)";
        return r;
    }

    public static UiListRow PresetRow(Transform parent, TMP_FontAsset font)
    {
        var r = K.ListRow("PresetRow", parent, font, false, "불러오기", 56f, false);
        ((RectTransform)r.transform).sizeDelta = new Vector2(780f, 56f);
        r.Label.text = "내 설정";
        return r;
    }

    public static UiOption MicOption(Transform parent, TMP_FontAsset font)
    {
        var o = K.Option("MicOption", parent, font, "마이크", false, 46f, 20f);
        ((RectTransform)o.transform).sizeDelta = new Vector2(680f, 46f);
        return o;
    }

    public static UiOption BanToggle(Transform parent, TMP_FontAsset font)
    {
        var o = K.Option("BanToggle", parent, font, "01 능력 이름", true, 46f, 20f);
        ((RectTransform)o.transform).sizeDelta = new Vector2(372f, 46f);
        o.OnFace = new Color(0.29f, 0.13f, 0.16f, 1f);  // 밴 = 어두운 빨강
        if (o.Check != null) o.Check.color = K.Red;
        o.SetOn(false);
        return o;
    }

    public static RoomRowView RoomRow(Transform parent, TMP_FontAsset font)
    {
        if (K.Graffiti) return RoomRowV7(parent, font);
        var rt = K.Rect("RoomRow", parent);
        rt.sizeDelta = new Vector2(1160f, 62f);
        K.Size(rt, -1f, 62f);
        var face = K.Sticker(rt, K.Raised, 0f, 3f);
        var hl = face.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hl.padding = new RectOffset(20, 8, 6, 6);
        hl.spacing = 10f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;

        var v = rt.gameObject.AddComponent<RoomRowView>();
        v.Face = face;
        v.Name = Col(face.transform, font, "Name", "방 이름", 340f, 24f, K.Ink, TextAlignmentOptions.MidlineLeft, true, 1f);
        v.Count = Col(face.transform, font, "Count", "1/12", 100f, 22f, K.Ink, TextAlignmentOptions.Center, true, 0f);
        v.Lock = Col(face.transform, font, "Lock", "", 80f, 20f, K.Red, TextAlignmentOptions.Center, true, 0f);
        v.Map = Col(face.transform, font, "Map", "기본 맵", 200f, 22f, K.Muted, TextAlignmentOptions.MidlineLeft, false, 0f);
        v.State = Col(face.transform, font, "State", "대기 중", 120f, 20f, K.Muted, TextAlignmentOptions.Center, false, 0f);
        v.Join = K.Btn("Join", face.transform, font, "참가", K.Yellow, 22f, out var jl);
        K.Size(v.Join, 130f);
        v.JoinLabel = jl;
        return v;
    }

    public static RoomPlayerRowView PlayerRow(Transform parent, TMP_FontAsset font)
    {
        if (K.Graffiti) return PlayerCardV7(parent, font);
        var rt = K.Rect("RoomPlayerRow", parent);
        rt.sizeDelta = new Vector2(780f, 60f);
        K.Size(rt, -1f, 60f);
        var face = K.Sticker(rt, K.Raised, 0f, 3f);
        var hl = face.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hl.padding = new RectOffset(10, 8, 7, 7);
        hl.spacing = 14f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;

        var v = rt.gameObject.AddComponent<RoomPlayerRowView>();
        v.Face = face;

        var badge = K.Rect("Badge", face.transform);
        K.Size(badge, 96f);
        v.Badge = K.Sticker(badge, K.Gold, 0f, 0f);
        v.BadgeText = K.Text("Text", v.Badge.transform, font, 19f, K.TextOn(K.Gold), TextAlignmentOptions.Center, true);
        K.Fill(v.BadgeText.rectTransform, 2f);
        v.BadgeText.text = "방장";

        v.Name = Col(face.transform, font, "Name", "플레이어  (나)", 300f, 24f, K.Ink, TextAlignmentOptions.MidlineLeft, true, 1f);

        v.Kick = K.Btn("Kick", face.transform, font, "강퇴", K.Red, 20f, out _, Color.white);
        K.Size(v.Kick, 100f);
        return v;
    }

    static TextMeshProUGUI Col(Transform parent, TMP_FontAsset font, string name, string text, float w, float size,
                               Color color, TextAlignmentOptions align, bool bold, float flex)
    {
        var t = K.Text(name, parent, font, size, color, align, bold);
        K.Size(t, w, -1f, flex);
        t.text = text;
        return t;
    }

    static TextMeshProUGUI Label(Transform row, TMP_FontAsset font, string text, float w)
    {
        var t = K.Text("Label", row, font, 22f, K.Ink, TextAlignmentOptions.MidlineLeft, true);
        K.Size(t, w);
        t.text = text;
        return t;
    }

    // ══════════════════ 대상 선택 창 ══════════════════

    /// <summary>
    /// PlayerPickView (Canvas 30600)
    ///  └ Root ─ Backdrop · Window ─ Header · List(스크롤) · Cancel
    /// </summary>
    public static PlayerPickView PlayerPick(GameObject host, TMP_FontAsset font, UiListRow rowPrefab)
    {
        var root = K.MakeScreen(host, 30600, K.DimColor);
        var v = host.AddComponent<PlayerPickView>();
        v.Root = root.gameObject;
        v.RowPrefab = rowPrefab;

        K.Window("Window", root, 620f, 720f, out var c);
        v.Title = K.Header(c, font, "대상 고르기");

        var list = K.Scroll("List", c, out var sr, 10f);
        K.Size(sr, -1f, -1f, -1f, 1f);
        v.ListContent = list;
        v.EmptyText = K.Note(list, font, "고를 수 있는 사람이 없어", 22f);
        K.Size(v.EmptyText, -1f, 44f);
        v.EmptyText.gameObject.SetActive(false);

        v.CancelButton = K.Btn("Cancel", c, font, "취소 (Esc)", K.Raised, 24f, out _);
        K.Size(v.CancelButton, -1f, 64f);
        v.RowColor = K.Graffiti ? K.Soft : K.Raised;
        v.SelfColor = K.MintDark;
        v.DeadColor = new Color(0.1f, 0.11f, 0.13f, 1f);
        K.Finish(host.transform);
        return v;
    }

    /// <summary>
    /// 바리에이션 — 같은 구조에 색만 다르게 (창 바탕 · 제목 · 밑줄 · 줄 · 취소 버튼)
    /// </summary>
    public static PlayerPickView PlayerPickThemed(GameObject host, TMP_FontAsset font, UiListRow rowPrefab, string title,
                                                  Color window, Color titleColor, Color accent, Color row, Color dead,
                                                  Color cancel, Color cancelText, Color backdrop)
    {
        var v = PlayerPick(host, font, rowPrefab);
        v.Title.text = title;
        v.Title.color = titleColor;

        var acc = v.Title.transform.parent.Find("Accent");
        if (acc != null) acc.GetComponent<UnityEngine.UI.Image>().color = accent;

        UnityEngine.UI.Image face = null;                   // 창 면 (v7은 제목이 한 칸 더 안쪽)
        foreach (var img in v.Title.GetComponentsInParent<UnityEngine.UI.Image>(true))
            if (img.name == "Face") { face = img; break; }
        if (face != null) face.color = window;

        var bd = v.Root.transform.Find("Backdrop");
        if (bd != null) bd.GetComponent<UnityEngine.UI.Image>().color = backdrop;

        v.RowColor = row;
        v.SelfColor = Color.Lerp(row, accent, 0.35f);
        v.DeadColor = dead;
        if (v.EmptyText != null) v.EmptyText.color = titleColor;

        if (v.CancelButton.targetGraphic is UnityEngine.UI.Image ci) ci.color = cancel;
        var ct = v.CancelButton.GetComponentInChildren<TMP_Text>();
        if (ct != null) ct.color = K.TextOn(cancel);      // 면에 맞춰 (cancelText는 예전 호환 — 무시)
        return v;
    }

    /// <summary>데스 노트 — 새까만 노트 + 빨간 마름모</summary>
    public static PlayerPickView DeathNotePick(GameObject host, TMP_FontAsset font, UiListRow rowPrefab) =>
        PlayerPickThemed(host, font, rowPrefab, "이름을 적을 사람",
            window: new Color(0.06f, 0.06f, 0.07f, 0.98f), titleColor: K.Light, accent: K.Danger,
            row: new Color(0.13f, 0.12f, 0.13f), dead: new Color(0.08f, 0.08f, 0.09f),
            cancel: new Color(0.2f, 0.19f, 0.2f), cancelText: K.Light,
            backdrop: new Color(0f, 0f, 0f, 0.8f));

    /// <summary>저주 인형 — 어두운 보라 천 + 노란 마름모</summary>
    public static PlayerPickView VoodooPick(GameObject host, TMP_FontAsset font, UiListRow rowPrefab) =>
        PlayerPickThemed(host, font, rowPrefab, "저주할 사람",
            window: new Color(0.17f, 0.12f, 0.24f, 0.98f), titleColor: K.Light, accent: K.Gold,
            row: new Color(0.25f, 0.18f, 0.34f), dead: new Color(0.12f, 0.09f, 0.16f),
            cancel: new Color(0.3f, 0.22f, 0.4f), cancelText: K.Light,
            backdrop: new Color(0.06f, 0.02f, 0.1f, 0.72f));

    // ══════════════════ 보이스 설정 창 ══════════════════

    /// <summary>
    /// VoiceSettingsView (Canvas 30700)
    ///  └ Root ─ Backdrop · Window ─ Header · Body(스크롤: 방식 · 단축키 · 마이크 · 음소거 · 크기 · 진단) · Close
    /// </summary>
    public static VoiceSettingsView Voice(GameObject host, TMP_FontAsset font, UiOption micPrefab)
    {
        var root = K.MakeScreen(host, 30700, K.DimColor);
        var v = host.AddComponent<VoiceSettingsView>();
        v.Root = root.gameObject;
        v.MicOptionPrefab = micPrefab;

        K.Window("Window", root, 800f, 960f, out var c);
        v.Title = K.Header(c, font, "보이스 설정");

        var body = K.Scroll("Body", c, out var sr, 12f);
        K.Size(sr, -1f, -1f, -1f, 1f);

        // 방식
        K.Section(body, font, "마이크 방식");
        var mode = K.Row("Mode", body, 58f, 12f);
        mode.childForceExpandWidth = true;
        v.PttOption = K.Option("PushToTalk", mode.transform, font, "누르고 말하기", false, 52f);
        K.Size(v.PttOption, -1f, -1f, 1f);
        v.OpenOption = K.Option("OpenMic", mode.transform, font, "열린 마이크", false, 52f);
        K.Size(v.OpenOption, -1f, -1f, 1f);

        var vad = K.Group("VadGroup", body, 4f);
        v.VadGroup = vad.gameObject;
        v.VadLabel = K.Text("Label", vad.transform, font, 20f, K.Muted);
        K.Size(v.VadLabel, -1f, 30f);
        v.VadLabel.text = "목소리 감지 민감도 (왼쪽 = 예민)  0.010";
        v.VadSlider = K.SliderBar("Slider", vad.transform);

        // 단축키
        K.Section(body, font, "단축키 (누르고 다른 키 · Esc 취소)");
        v.MatchTalkRow = KeyRow(body, font, "경기 — 누르고 말하기", "T");
        v.MatchMuteRow = KeyRow(body, font, "경기 마이크 음소거", "K");
        v.TeamTalkRow = KeyRow(body, font, "팀 — 누르고 말하기", "Y");
        v.TeamMuteRow = KeyRow(body, font, "팀 마이크 음소거", "L");
        v.SettingsRow = KeyRow(body, font, "이 설정 창", "O");
        v.TeamNote = K.Note(body, font, "팀 보이스는 팀전에서만");
        K.Size(v.TeamNote, -1f, 28f);

        // 마이크
        K.Section(body, font, "마이크");
        var mics = K.Group("MicList", body, 8f);
        mics.padding = new RectOffset(0, 0, 0, 4);
        v.MicListContent = (RectTransform)mics.transform;

        // 음소거 · 크기
        K.Section(body, font, "음소거 · 크기");
        var mute = K.Row("Mute", body, 54f, 12f);
        mute.childForceExpandWidth = true;
        v.MatchMuteOption = K.Option("MatchMute", mute.transform, font, "경기 마이크 음소거", true, 48f);
        K.Size(v.MatchMuteOption, -1f, -1f, 1f);
        v.TeamMuteOption = K.Option("TeamMute", mute.transform, font, "팀 마이크 음소거", true, 48f);
        K.Size(v.TeamMuteOption, -1f, -1f, 1f);

        var mv = K.Group("MatchVolume", body, 4f);
        v.MatchVolLabel = K.Text("Label", mv.transform, font, 20f, K.Muted);
        K.Size(v.MatchVolLabel, -1f, 30f);
        v.MatchVolLabel.text = "경기 보이스 크기  100%";
        v.MatchVolSlider = K.SliderBar("Slider", mv.transform);

        var tv = K.Group("TeamVolume", body, 4f);
        v.TeamVolGroup = tv.gameObject;
        v.TeamVolLabel = K.Text("Label", tv.transform, font, 20f, K.Muted);
        K.Size(v.TeamVolLabel, -1f, 30f);
        v.TeamVolLabel.text = "팀 보이스 크기  100%";
        v.TeamVolSlider = K.SliderBar("Slider", tv.transform);

        v.Diagnose = K.Note(body, font, "진단 (보이스 연결 상태)", 17f);

        v.CloseButton = K.Btn("Close", c, font, "닫기 (Esc)", K.Yellow, 24f, out _);
        K.Size(v.CloseButton, -1f, 64f);
        K.Finish(host.transform);
        return v;
    }

    static UiListRow KeyRow(Transform parent, TMP_FontAsset font, string label, string key)
    {
        var r = K.ListRow("Key", parent, font, false, key, 56f, false);
        r.Label.text = label;
        r.Label.fontStyle = FontStyles.Normal;
        K.Size(r.Action, 170f);
        if (r.Action.targetGraphic is UnityEngine.UI.Image img) img.color = K.Field;
        if (r.ActionLabel != null) r.ActionLabel.color = K.Light;
        return r;
    }

    // ══════════════════ 방 목록 · 대기실 ══════════════════

    /// <summary>
    /// RoomScreenView (Canvas 30000)
    ///  └ Root ─ Backdrop(하늘색) · 장식
    ///     ├ Lobby ─ Window: 제목 · 상태 · 닉네임 · 표 머리 · 방 목록 · 방 만들기 · 코드로 참가
    ///     ├ Waiting ─ Players(왼쪽): 방 이름 · 코드 · 명단 · 시작/준비 · 나가기
    ///     │         └ Settings(오른쪽): 게임 · 제단 · 밴 · 프리셋
    ///     ├ Center (방 정보 받는 중)
    ///     └ Toast
    /// </summary>
    public static RoomScreenView Room(GameObject host, TMP_FontAsset font, RoomRowView roomRow,
                                      RoomPlayerRowView playerRow, UiOption banToggle, UiListRow presetRow)
    {
        if (K.Graffiti) return RoomV7(host, font, roomRow, playerRow, banToggle, presetRow);
        var root = K.MakeScreen(host, 30000, K.Sky);
        var v = host.AddComponent<RoomScreenView>();
        v.Root = root.gameObject;
        v.RoomRowPrefab = roomRow;
        v.PlayerRowPrefab = playerRow;
        v.BanTogglePrefab = banToggle;
        v.PresetRowPrefab = presetRow;

        Blob(root, -760f, 380f, 520f, -8f);
        Blob(root, 820f, -360f, 640f, 6f);
        Blob(root, 620f, 470f, 220f, -14f);

        BuildLobby(v, root, font);
        BuildWaiting(v, root, font);

        v.CenterText = K.Text("Center", root, font, 34f, K.Ink, TextAlignmentOptions.Center, true);
        v.CenterText.font = K.Display(font);
        v.CenterText.fontStyle = FontStyles.Normal;
        K.Center(v.CenterText.rectTransform, 0f, 0f, 1000f, 80f);
        v.CenterText.text = "방 정보 받는 중...";
        v.CenterText.gameObject.SetActive(false);

        var tb = K.Rect("Toast", root);
        K.Center(tb, 0f, -478f, 900f, 62f);
        var tf = K.Sticker(tb, new Color(K.Dark.r, K.Dark.g, K.Dark.b, 0.94f), 0f, 8f);
        v.ToastText = K.Text("Text", tf.transform, font, 24f, K.Light, TextAlignmentOptions.Center, true);
        K.Fill(v.ToastText.rectTransform, 6f);
        v.ToastText.text = "알림";
        v.ToastBox = tb.gameObject;
        tb.gameObject.SetActive(false);

        v.HostBadge = K.Gold;
        v.ReadyBadge = K.Ok;
        v.WaitBadge = K.LineColor;
        v.StartColor = K.Mint;
        v.ReadyColor = K.Ok;
        v.UnreadyColor = K.Raised;

        v.WaitingPanel.SetActive(false);        // 편집할 땐 켜서 보기
        return v;
    }

    /// <summary>배경 장식 — 아주 옅은 육각형 (살짝 기울임)</summary>
    static void Blob(RectTransform root, float x, float y, float size, float tilt)
    {
        var b = K.Img("Blob", root, K.Hex, new Color(K.Mint.r, K.Mint.g, K.Mint.b, 0.045f));
        K.Center(b.rectTransform, x, y, size, size * 0.875f);
        b.rectTransform.localRotation = K.Tilt(tilt);
    }

    static void BuildLobby(RoomScreenView v, RectTransform root, TMP_FontAsset font)
    {
        var lobby = K.Rect("Lobby", root);
        K.Fill(lobby);
        v.LobbyPanel = lobby.gameObject;

        K.Window("Window", lobby, 1240f, 960f, out var c);
        K.Header(c, font, "방 목록");

        var st = K.Row("Status", c, 50f, 12f);
        v.LobbyStatus = K.Text("Text", st.transform, font, 22f, K.Muted);
        K.Size(v.LobbyStatus, -1f, -1f, 1f);
        v.LobbyStatus.text = "서버 연결 중...";
        v.ReconnectButton = K.Btn("Reconnect", st.transform, font, "다시 연결", K.Yellow, 22f, out _);
        K.Size(v.ReconnectButton, 200f);

        var nick = K.Row("Nick", c, 58f, 12f);
        Label(nick.transform, font, "닉네임", 140f);
        v.NickInput = K.Input("NickInput", nick.transform, font, 22f, "닉네임", 16);
        K.Size(v.NickInput, 360f);

        var head = K.Row("TableHead", c, 30f, 10f);
        head.padding = new RectOffset(23, 15, 0, 0);
        Col(head.transform, font, "Name", "방 이름", 340f, 18f, K.Muted, TextAlignmentOptions.MidlineLeft, true, 1f);
        Col(head.transform, font, "Count", "인원", 100f, 18f, K.Muted, TextAlignmentOptions.Center, true, 0f);
        Col(head.transform, font, "Lock", "잠금", 80f, 18f, K.Muted, TextAlignmentOptions.Center, true, 0f);
        Col(head.transform, font, "Map", "맵", 200f, 18f, K.Muted, TextAlignmentOptions.MidlineLeft, true, 0f);
        Col(head.transform, font, "State", "상태", 120f, 18f, K.Muted, TextAlignmentOptions.Center, true, 0f);
        Col(head.transform, font, "Join", "", 130f, 18f, K.Muted, TextAlignmentOptions.Center, true, 0f);

        var list = K.Scroll("RoomList", c, out var sr, 10f);
        K.Size(sr, -1f, -1f, -1f, 1f);
        v.RoomListContent = list;
        v.EmptyText = K.Note(list, font, "열린 방이 없어 — 새로 만들어 봐", 22f);
        K.Size(v.EmptyText, -1f, 44f);

        var create = K.Row("Create", c, 62f, 12f);
        Label(create.transform, font, "방 이름", 140f);
        v.RoomNameInput = K.Input("RoomNameInput", create.transform, font, 22f, "비우면 '닉네임의 방'", 24);
        K.Size(v.RoomNameInput, 300f, -1f, 1f);
        v.HiddenOption = K.Option("Hidden", create.transform, font, "숨김 (코드로만)", true, 54f);
        K.Size(v.HiddenOption, 240f);
        v.CreateButton = K.Btn("Create", create.transform, font, "방 만들기", K.Yellow, 24f, out _);
        K.Size(v.CreateButton, 200f);

        var code = K.Row("Code", c, 62f, 12f);
        Label(code.transform, font, "방 코드", 140f);
        v.CodeInput = K.Input("CodeInput", code.transform, font, 22f, "ABC123", 8);
        K.Size(v.CodeInput, 260f);
        v.JoinCodeButton = K.Btn("JoinCode", code.transform, font, "코드로 참가", K.Raised, 22f, out _);
        K.Size(v.JoinCodeButton, 200f);
    }

    static void BuildWaiting(RoomScreenView v, RectTransform root, TMP_FontAsset font)
    {
        var wait = K.Rect("Waiting", root);
        K.Fill(wait);
        v.WaitingPanel = wait.gameObject;

        // ── 왼쪽: 명단 ──
        var left = K.Window("Players", wait, 860f, 990f, out var lc);
        left.anchoredPosition = new Vector2(-450f, 0f);
        v.RoomTitle = K.Header(lc, font, "방 이름");

        var codeLine = K.Row("CodeLine", lc, 54f, 12f);
        v.CodeText = K.Text("Code", codeLine.transform, font, 26f, K.Ink, TextAlignmentOptions.MidlineLeft, true);
        v.CodeText.font = K.Display(font);
        v.CodeText.fontStyle = FontStyles.Normal;
        K.Size(v.CodeText, -1f, -1f, 1f);
        v.CodeText.text = "방 코드  ABC123";
        v.CopyCodeButton = K.Btn("Copy", codeLine.transform, font, "복사", Color.white, 22f, out _);
        K.Size(v.CopyCodeButton, 120f);

        v.VisibilityText = K.Note(lc, font, "공개 방", 20f);
        K.Size(v.VisibilityText, -1f, 28f);
        v.CountText = K.Note(lc, font, "사람 1/12", 20f);
        K.Size(v.CountText, -1f, 28f);

        var plist = K.Scroll("PlayerList", lc, out var psr, 10f);
        K.Size(psr, -1f, -1f, -1f, 1f);
        v.PlayerListContent = plist;

        v.MainButton = K.Btn("Main", lc, font, "게임 시작", K.Mint, 34f, out var ml);
        ml.font = K.Display(font);
        ml.fontStyle = FontStyles.Normal;
        K.Size(v.MainButton, -1f, 88f);
        v.MainButtonLabel = ml;
        v.MainButtonFace = (UnityEngine.UI.Image)v.MainButton.targetGraphic;

        v.ReasonText = K.Note(lc, font, "", 20f);
        v.ReasonText.alignment = TextAlignmentOptions.Center;
        K.Size(v.ReasonText, -1f, 28f);

        v.LeaveButton = K.Btn("Leave", lc, font, "방 나가기", Color.white, 24f, out _);
        K.Size(v.LeaveButton, -1f, 58f);

        // ── 오른쪽: 설정 ──
        var right = K.Window("Settings", wait, 860f, 990f, out var rc);
        right.anchoredPosition = new Vector2(450f, 0f);
        v.SettingsTitle = K.Header(rc, font, "방 설정");

        var body = K.Scroll("Body", rc, out var bsr, 10f);
        K.Size(bsr, -1f, -1f, -1f, 1f);

        K.Section(body, font, "게임");
        v.MapRow = K.Stepper("Map", body, font, "맵", false, "<", ">");
        v.MaxPlayersRow = K.Stepper("MaxPlayers", body, font, "최대 인원", false);
        v.RoundTimeRow = K.Stepper("RoundTime", body, font, "라운드 시간", true);
        v.StartSoulRow = K.Stepper("StartSoul", body, font, "시작 소울", true);
        v.ChestRow = K.Stepper("ChestRefill", body, font, "상자 리필", true);

        var fall = K.Row("Fall", body, 56f, 8f);
        Label(fall.transform, font, "낙사", 220f);
        v.FallDeath = K.Option("Death", fall.transform, font, "즉사", false, 50f);
        K.Size(v.FallDeath, -1f, -1f, 1f);
        v.FallReturn = K.Option("Return", fall.transform, font, "부활 지점으로", false, 50f);
        K.Size(v.FallReturn, -1f, -1f, 1f);

        v.TeamOption = K.Option("TeamMode", body, font, "팀전 (설정만 저장 — 팀 나누기는 나중)", true, 50f);
        v.HiddenSetting = K.Option("Hidden", body, font, "방 숨기기 (코드로만 참가)", true, 50f);

        K.Section(body, font, "제단 — 서브 제단 N개 → 중앙 제단");
        v.SubCostRow = K.Stepper("SubCost", body, font, "서브 제단 비용", true);
        v.CentralCostRow = K.Stepper("CentralCost", body, font, "중앙 제단 비용", true);
        v.NeedAllOption = K.Option("NeedAll", body, font, "서브 제단을 전부 열어야 중앙 제단", true, 50f);
        v.NeededRow = K.Stepper("Needed", body, font, "필요한 서브 제단", false);
        v.ChannelRow = K.Stepper("Channel", body, font, "중앙 채널 시간", false);
        var note = K.Note(body, font, "맵의 서브 제단이 필요 수보다 적으면 전부 열면 됨", 18f);
        K.Size(note, -1f, 26f);

        K.Section(body, font, "밴");
        BanSection(body, font, "Class", "클래스 밴", out v.ClassBanHeader, out v.ClassBanLabel, out v.ClassBanClear, out v.ClassBanContent);
        BanSection(body, font, "Random", "랜덤 능력 밴", out v.RandBanHeader, out v.RandBanLabel, out v.RandBanClear, out v.RandBanContent);

        K.Section(body, font, "프리셋");
        var pr1 = K.Row("PresetButtons", body, 56f, 12f);
        pr1.childForceExpandWidth = true;
        v.CopySettingsButton = K.Btn("Copy", pr1.transform, font, "설정 복사", Color.white, 22f, out _);
        K.Size(v.CopySettingsButton, -1f, -1f, 1f);
        v.PasteButton = K.Btn("Paste", pr1.transform, font, "붙여넣기", Color.white, 22f, out _);
        K.Size(v.PasteButton, -1f, -1f, 1f);

        var pr2 = K.Row("PresetSave", body, 58f, 12f);
        v.PresetNameInput = K.Input("PresetName", pr2.transform, font, 22f, "프리셋 이름", 24);
        K.Size(v.PresetNameInput, 200f, -1f, 1f);
        v.SaveButton = K.Btn("Save", pr2.transform, font, "파일로 저장", K.Yellow, 22f, out _);
        K.Size(v.SaveButton, 200f);

        var plist2 = K.Group("PresetList", body, 8f);
        plist2.padding = new RectOffset(0, 0, 0, 4);
        v.PresetListContent = (RectTransform)plist2.transform;

        v.OpenFolderButton = K.Btn("OpenFolder", body, font, "프리셋 폴더 열기", Color.white, 20f, out _);
        K.Size(v.OpenFolderButton, -1f, 52f);
    }

    static void BanSection(Transform parent, TMP_FontAsset font, string name, string title,
                           out UnityEngine.UI.Button header, out TMP_Text label, out UnityEngine.UI.Button clear,
                           out RectTransform content)
    {
        var row = K.Row(name + "BanHead", parent, 56f, 10f);
        header = K.Btn("Header", row.transform, font, "[+] " + title + " (0개)", Color.white, 22f, out var lb);
        K.Size(header, -1f, -1f, 1f);
        lb.alignment = TextAlignmentOptions.MidlineLeft;
        lb.rectTransform.offsetMin = new Vector2(16f, 4f);
        label = lb;
        clear = K.Btn("Clear", row.transform, font, "전부 풀기", K.Red, 20f, out _, Color.white);
        K.Size(clear, 160f);

        var grid = K.Rect(name + "BanList", parent);
        var g = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        g.cellSize = new Vector2(372f, 46f);
        g.spacing = new Vector2(10f, 10f);
        g.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = 2;
        g.padding = new RectOffset(0, 0, 0, 6);
        content = grid;
    }

    // ══════════════════ v7 — 방 목록 · 대기실 (시안 RoomsV7 · LobbyV7) ══════════════════

    /// <summary>
    /// RoomScreenView v7 (Canvas 30000) — 바탕 = 시멘트 벽 + 스프레이
    ///  ├ Lobby ─ 제목 · 상태 / 왼쪽 판: 표 머리 · 방 목록 / 오른쪽 파란 판: 닉네임 · 방 이름 · 숨김 · 방 만들기 · 방 코드 · 참가
    ///  ├ Waiting ─ 제목 · 코드 · 인원 / 파란 띠: 캐릭터 카드 라인업 / 아래 왼쪽: 방 설정 (스크롤) / 아래 오른쪽: 시작 · 나가기
    ///  ├ Center · Toast
    /// RoomScreenView 칸은 v2와 같음 — RoomUI가 그대로 채움
    /// </summary>
    static RoomScreenView RoomV7(GameObject host, TMP_FontAsset font, RoomRowView roomRow,
                                 RoomPlayerRowView playerRow, UiOption banToggle, UiListRow presetRow)
    {
        var root = K.MakeScreen(host, 30000, K.Sky);
        var v = host.AddComponent<RoomScreenView>();
        v.Root = root.gameObject;
        v.RoomRowPrefab = roomRow;
        v.PlayerRowPrefab = playerRow;
        v.BanTogglePrefab = banToggle;
        v.PresetRowPrefab = presetRow;

        LobbyV7(v, root, font);
        WaitingV7(v, root, font);

        v.CenterText = K.Text("Center", root, K.Display(font), 40f, K.Ink, TextAlignmentOptions.Center);
        v.CenterText.fontStyle = FontStyles.Italic;
        K.InkOutline(v.CenterText);
        K.Center(v.CenterText.rectTransform, 0f, 0f, 1000f, 90f);
        v.CenterText.text = "방 정보 받는 중...";
        v.CenterText.gameObject.SetActive(false);

        var tb = K.Rect("Toast", root);
        K.Center(tb, 0f, -478f, 900f, 66f);
        var tf = K.ParaFace(tb, K.PaperFace);
        v.ToastText = K.Text("Text", tf.transform, font, 24f, K.InkLine, TextAlignmentOptions.Center, true);
        K.Fill(v.ToastText.rectTransform, 8f);
        v.ToastText.text = "알림";
        v.ToastBox = tb.gameObject;
        tb.gameObject.SetActive(false);

        v.HostBadge = K.Gold;
        v.ReadyBadge = K.Mint;
        v.WaitBadge = K.LineColor;
        v.StartColor = K.Gold;
        v.ReadyColor = K.Mint;
        v.UnreadyColor = K.Raised;

        v.WaitingPanel.SetActive(false);        // 편집할 땐 켜서 보기
        K.Finish(host.transform);
        return v;
    }

    static void LobbyV7(RoomScreenView v, RectTransform root, TMP_FontAsset font)
    {
        var lobby = K.Rect("Lobby", root);
        K.Fill(lobby);
        v.LobbyPanel = lobby.gameObject;

        // 제목 줄
        var top = TopLine(lobby, "방 목록", font, out _);
        v.LobbyStatus = K.Text("Status", top, font, 22f, K.Dim);
        K.Size(v.LobbyStatus, 520f);
        v.LobbyStatus.text = "서버 연결 중...";
        v.ReconnectButton = K.Btn("Reconnect", top, font, "다시 연결", K.Raised, 22f, out _);
        K.Size(v.ReconnectButton, 200f, 52f);

        // ── 왼쪽: 방 목록 ──
        var list = Panel(lobby, "List", K.Panel, false, new Vector2(60f, 70f), new Vector2(1250f, 930f), out var lc);
        lc.padding = new RectOffset(30, 30, 40, 28);
        EdgeTag(list, font, "열린 방", K.Danger);

        var head = K.Row("TableHead", lc.transform, 30f, 10f);
        head.padding = new RectOffset(36, 30, 0, 0);
        Col(head.transform, font, "Name", "방 이름", 340f, 17f, K.Dim, TextAlignmentOptions.MidlineLeft, true, 1f);
        Col(head.transform, font, "Count", "인원", 100f, 17f, K.Dim, TextAlignmentOptions.Center, true, 0f);
        Col(head.transform, font, "Lock", "잠금", 80f, 17f, K.Dim, TextAlignmentOptions.Center, true, 0f);
        Col(head.transform, font, "Map", "맵", 180f, 17f, K.Dim, TextAlignmentOptions.MidlineLeft, true, 0f);
        Col(head.transform, font, "State", "상태", 120f, 17f, K.Dim, TextAlignmentOptions.Center, true, 0f);
        Col(head.transform, font, "Join", "", 130f, 17f, K.Dim, TextAlignmentOptions.Center, true, 0f);

        var rows = K.Scroll("RoomList", lc.transform, out var sr, 10f);
        K.Size(sr, -1f, -1f, -1f, 1f);
        v.RoomListContent = rows;
        v.EmptyText = K.Note(rows, font, "열린 방이 없어 — 새로 만들어 봐", 22f);
        K.Size(v.EmptyText, -1f, 44f);

        // ── 오른쪽: 파란 판 — 방 만들기 · 코드로 참가 ──
        var make = Panel(lobby, "Make", K.BluePanel, true, new Vector2(1290f, 70f), new Vector2(1860f, 930f), out var mc);
        mc.padding = new RectOffset(34, 34, 46, 30);
        mc.spacing = 12f;
        EdgeTag(make, font, "방 만들기", K.Danger);

        FieldLabel(mc.transform, font, "닉네임");
        v.NickInput = K.Input("NickInput", mc.transform, font, 22f, "닉네임", 16);
        K.Size(v.NickInput, -1f, 58f);

        FieldLabel(mc.transform, font, "방 이름");
        v.RoomNameInput = K.Input("RoomNameInput", mc.transform, font, 22f, "비우면 '닉네임의 방'", 24);
        K.Size(v.RoomNameInput, -1f, 58f);

        v.HiddenOption = K.Option("Hidden", mc.transform, font, "숨김 (코드로만)", true, 56f);
        v.CreateButton = K.Btn("Create", mc.transform, font, "방 만들기", K.Mint, 28f, out var cl);
        cl.font = K.Display(font);
        cl.fontStyle = FontStyles.Italic;
        K.Size(v.CreateButton, -1f, 76f);

        var gap = K.Rect("Divider", mc.transform);
        K.Size(gap, -1f, 26f);
        var dash = K.Img("Line", gap, null, new Color(1f, 1f, 1f, 0.4f));
        dash.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        dash.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        dash.rectTransform.sizeDelta = new Vector2(0f, 3f);

        FieldLabel(mc.transform, font, "방 코드");
        var code = K.Row("Code", mc.transform, 60f, 12f);
        v.CodeInput = K.Input("CodeInput", code.transform, font, 24f, "ABC123", 8);
        K.Size(v.CodeInput, -1f, -1f, 1f);
        v.JoinCodeButton = K.Btn("JoinCode", code.transform, font, "코드로 참가", K.PaperFace, 22f, out _);
        K.Size(v.JoinCodeButton, 200f);
    }

    static void WaitingV7(RoomScreenView v, RectTransform root, TMP_FontAsset font)
    {
        var wait = K.Rect("Waiting", root);
        K.Fill(wait);
        v.WaitingPanel = wait.gameObject;

        // 제목 줄: 방 이름 · 코드 · 복사 · 공개 여부 / 오른쪽 끝 인원
        var top = TopLine(wait, "방 이름", font, out v.RoomTitle);
        var chip = K.Rect("CodeChip", top);
        K.Size(chip, 380f, 56f);
        var cf = K.ParaFace(chip, K.PaperFace);
        var ch = cf.gameObject.AddComponent<HorizontalLayoutGroup>();
        ch.padding = new RectOffset(26, 16, 6, 8);
        ch.spacing = 10f;
        ch.childAlignment = TextAnchor.MiddleLeft;
        ch.childControlWidth = ch.childControlHeight = true;
        ch.childForceExpandWidth = false;
        ch.childForceExpandHeight = true;
        v.CodeText = K.Text("Code", cf.transform, K.Display(font), 24f, K.InkLine, TextAlignmentOptions.MidlineLeft);
        K.Size(v.CodeText, -1f, -1f, 1f);
        v.CodeText.text = "방 코드  ABC123";
        v.CopyCodeButton = K.Btn("Copy", cf.transform, font, "복사", K.Mint, 18f, out _);
        K.Size(v.CopyCodeButton, 90f);
        v.VisibilityText = K.Text("Visibility", top, font, 20f, K.Dim);
        K.Size(v.VisibilityText, 360f);
        v.VisibilityText.text = "공개 방";
        var fill = K.Rect("Space", top);
        K.Size(fill, -1f, -1f, 1f);
        v.CountText = K.Text("Count", top, K.Display(font), 44f, K.Ink, TextAlignmentOptions.MidlineRight);
        v.CountText.fontStyle = FontStyles.Italic;
        K.InkOutline(v.CountText);
        K.Size(v.CountText, 280f);
        v.CountText.text = "사람 1/12";

        // ── 파란 띠: 캐릭터 카드 라인업 ──
        var band = Panel(wait, "Lineup", K.BluePanel, true, new Vector2(60f, 560f), new Vector2(1860f, 950f), out var bc);
        Object.DestroyImmediate(bc);                   // 세로 쌓기 대신 가로 줄
        var line = K.Rect("Players", band);
        K.Fill(line, 22f);
        var lh = line.gameObject.AddComponent<HorizontalLayoutGroup>();
        lh.spacing = 10f;
        lh.childAlignment = TextAnchor.MiddleCenter;
        lh.childControlWidth = lh.childControlHeight = false;
        lh.childForceExpandWidth = lh.childForceExpandHeight = false;
        v.PlayerListContent = line;

        // ── 아래 왼쪽: 방 설정 ──
        var set = Panel(wait, "Settings", K.Panel, false, new Vector2(60f, 50f), new Vector2(1270f, 520f), out var sc);
        sc.padding = new RectOffset(28, 22, 40, 20);
        var stag = EdgeTag(set, font, "방 설정", K.Danger);
        v.SettingsTitle = stag;

        var body = K.Scroll("Body", sc.transform, out var bsr, 8f);
        K.Size(bsr, -1f, -1f, -1f, 1f);

        K.Section(body, font, "게임");
        v.MapRow = K.Stepper("Map", body, font, "맵", false, "<", ">");
        v.MaxPlayersRow = K.Stepper("MaxPlayers", body, font, "최대 인원", false);
        v.RoundTimeRow = K.Stepper("RoundTime", body, font, "라운드 시간", true);
        v.StartSoulRow = K.Stepper("StartSoul", body, font, "시작 소울", true);
        v.ChestRow = K.Stepper("ChestRefill", body, font, "상자 리필", true);

        var fall = K.Row("Fall", body, 56f, 8f);
        Label(fall.transform, font, "낙사", 220f);
        v.FallDeath = K.Option("Death", fall.transform, font, "즉사", false, 50f);
        K.Size(v.FallDeath, -1f, -1f, 1f);
        v.FallReturn = K.Option("Return", fall.transform, font, "부활 지점으로", false, 50f);
        K.Size(v.FallReturn, -1f, -1f, 1f);

        v.TeamOption = K.Option("TeamMode", body, font, "팀전 (설정만 저장 — 팀 나누기는 나중)", true, 50f);
        v.HiddenSetting = K.Option("Hidden", body, font, "방 숨기기 (코드로만 참가)", true, 50f);

        K.Section(body, font, "제단 — 서브 제단 N개 → 중앙 제단");
        v.SubCostRow = K.Stepper("SubCost", body, font, "서브 제단 비용", true);
        v.CentralCostRow = K.Stepper("CentralCost", body, font, "중앙 제단 비용", true);
        v.NeedAllOption = K.Option("NeedAll", body, font, "서브 제단을 전부 열어야 중앙 제단", true, 50f);
        v.NeededRow = K.Stepper("Needed", body, font, "필요한 서브 제단", false);
        v.ChannelRow = K.Stepper("Channel", body, font, "중앙 채널 시간", false);
        var note = K.Note(body, font, "맵의 서브 제단이 필요 수보다 적으면 전부 열면 됨", 18f);
        K.Size(note, -1f, 26f);

        K.Section(body, font, "밴");
        BanSection(body, font, "Class", "클래스 밴", out v.ClassBanHeader, out v.ClassBanLabel, out v.ClassBanClear, out v.ClassBanContent);
        BanSection(body, font, "Random", "랜덤 능력 밴", out v.RandBanHeader, out v.RandBanLabel, out v.RandBanClear, out v.RandBanContent);

        K.Section(body, font, "프리셋");
        var pr1 = K.Row("PresetButtons", body, 56f, 12f);
        pr1.childForceExpandWidth = true;
        v.CopySettingsButton = K.Btn("Copy", pr1.transform, font, "설정 복사", Color.white, 22f, out _);
        K.Size(v.CopySettingsButton, -1f, -1f, 1f);
        v.PasteButton = K.Btn("Paste", pr1.transform, font, "붙여넣기", Color.white, 22f, out _);
        K.Size(v.PasteButton, -1f, -1f, 1f);

        var pr2 = K.Row("PresetSave", body, 58f, 12f);
        v.PresetNameInput = K.Input("PresetName", pr2.transform, font, 22f, "프리셋 이름", 24);
        K.Size(v.PresetNameInput, 200f, -1f, 1f);
        v.SaveButton = K.Btn("Save", pr2.transform, font, "파일로 저장", K.Mint, 22f, out _);
        K.Size(v.SaveButton, 200f);

        var plist2 = K.Group("PresetList", body, 8f);
        plist2.padding = new RectOffset(0, 0, 0, 4);
        v.PresetListContent = (RectTransform)plist2.transform;

        v.OpenFolderButton = K.Btn("OpenFolder", body, font, "프리셋 폴더 열기", Color.white, 20f, out _);
        K.Size(v.OpenFolderButton, -1f, 52f);

        // ── 아래 오른쪽: 시작 · 나가기 ──
        var act = K.Rect("Actions", wait);
        Place(act, new Vector2(1310f, 50f), new Vector2(1860f, 520f));
        var av = act.gameObject.AddComponent<VerticalLayoutGroup>();
        av.spacing = 14f;
        av.childAlignment = TextAnchor.LowerCenter;
        av.childControlWidth = av.childControlHeight = true;
        av.childForceExpandWidth = true;
        av.childForceExpandHeight = false;

        v.ReasonText = K.Text("Reason", act, font, 21f, K.Ink, TextAlignmentOptions.Center, true);
        v.ReasonText.textWrappingMode = TextWrappingModes.Normal;
        K.Size(v.ReasonText, -1f, 60f);
        v.ReasonText.text = "";

        v.MainButton = K.Btn("Main", act, font, "게임 시작", K.Gold, 58f, out var ml);
        ml.font = K.Display(font);
        ml.fontStyle = FontStyles.Italic;
        K.Size(v.MainButton, -1f, 150f);
        v.MainButtonLabel = ml;
        v.MainButtonFace = (UnityEngine.UI.Image)v.MainButton.targetGraphic;

        v.LeaveButton = K.Btn("Leave", act, font, "방 나가기", K.Raised, 24f, out _);
        K.Size(v.LeaveButton, -1f, 64f);
    }

    /// <summary>
    /// 방 목록 한 줄 v7 — 기울어진 띠 · 이름 · 인원 · 잠금 · 맵 · 상태 · 노랑 참가
    /// </summary>
    static RoomRowView RoomRowV7(Transform parent, TMP_FontAsset font)
    {
        var rt = K.Rect("RoomRow", parent);
        rt.sizeDelta = new Vector2(1120f, 66f);
        K.Size(rt, -1f, 66f);
        var face = K.ParaFace(rt, K.Soft);
        var hl = face.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hl.padding = new RectOffset(36, 30, 8, 8);
        hl.spacing = 10f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;

        var v = rt.gameObject.AddComponent<RoomRowView>();
        v.Face = face;
        v.Name = Col(face.transform, font, "Name", "방 이름", 340f, 24f, K.Ink, TextAlignmentOptions.MidlineLeft, true, 1f);
        v.Count = Col(face.transform, font, "Count", "1/12", 100f, 22f, K.Ink, TextAlignmentOptions.Center, true, 0f);
        v.Lock = Col(face.transform, font, "Lock", "", 80f, 20f, K.Danger, TextAlignmentOptions.Center, true, 0f);
        v.Map = Col(face.transform, font, "Map", "기본 맵", 180f, 21f, K.Dim, TextAlignmentOptions.MidlineLeft, false, 0f);
        v.State = Col(face.transform, font, "State", "대기 중", 120f, 20f, K.Mint, TextAlignmentOptions.Center, true, 0f);
        v.Join = K.Btn("Join", face.transform, font, "참가", K.Gold, 22f, out var jl);
        jl.font = K.Display(font);
        K.Size(v.Join, 130f);
        v.JoinLabel = jl;
        return v;
    }

    // 카드 색 — 사람마다 (PlayerId로 고름)
    static readonly Color[] CardBack =
    {
        new(0.118f, 0.357f, 0.549f), new(0.122f, 0.420f, 0.278f), new(0.294f, 0.180f, 0.549f), new(0.549f, 0.180f, 0.180f),
        new(0.549f, 0.416f, 0.118f), new(0.118f, 0.431f, 0.478f), new(0.478f, 0.118f, 0.357f), new(0.290f, 0.333f, 0.118f),
    };
    static readonly Color[] CardShirt =
    {
        new(0.310f, 0.639f, 0.878f), new(0.435f, 0.812f, 0.592f), new(0.725f, 0.549f, 1f), new(1f, 0.478f, 0.420f),
        new(1f, 0.847f, 0.420f), new(0.373f, 0.878f, 0.910f), new(1f, 0.541f, 0.847f), new(0.659f, 0.847f, 0.420f),
    };

    /// <summary>
    /// 대기실 사람 카드 v7 (136×330) — 색 바탕 + 망점 · 카툰 인형(셔츠 색) · 이름판 · 도장(방장 · 준비 · 대기) · 강퇴 ×.
    /// 나 = 민트 테두리 (RoomPlayerRowView.Look)
    /// </summary>
    static RoomPlayerRowView PlayerCardV7(Transform parent, TMP_FontAsset font)
    {
        const float W = 136f, H = 330f;
        var rt = K.Rect("RoomPlayerRow", parent);
        rt.sizeDelta = new Vector2(W, H);
        K.Size(rt, W, H);

        var ring = K.Img("MeRing", rt, K.Plain, K.Mint);
        K.Fill(ring.rectTransform, -7f);
        ring.gameObject.SetActive(false);

        var face = K.Img("Face", rt, K.Plain, CardBack[0]);
        K.Fill(face.rectTransform);

        // 캐릭터 사진 (그 사람이 맞춘 꾸미기로 찍은 3D 사진 — RoomUI가 넣음). 이름판 위 전부
        var photoRt = K.Rect("Photo", face.transform);
        photoRt.anchorMin = new Vector2(0f, 0f);
        photoRt.anchorMax = new Vector2(1f, 1f);
        photoRt.offsetMin = new Vector2(7f, 58f);
        photoRt.offsetMax = new Vector2(-7f, -7f);
        var photo = photoRt.gameObject.AddComponent<RawImage>();
        photo.raycastTarget = false;
        photo.enabled = false;

        var plate = K.Img("Plate", face.transform, null, K.InkLine);
        var pr = plate.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(1f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.offsetMin = new Vector2(5f, 5f);
        pr.offsetMax = new Vector2(-5f, 54f);

        var v = rt.gameObject.AddComponent<RoomPlayerRowView>();
        v.Face = face;
        v.Photo = photo;
        v.MeRing = ring.gameObject;
        v.CardBacks = (Color[])CardBack.Clone();
        v.CardShirts = (Color[])CardShirt.Clone();

        v.Name = K.Text("Name", plate.transform, K.Display(font), 19f, K.Light, TextAlignmentOptions.Center);
        K.Fill(v.Name.rectTransform, 4f);
        v.Name.enableAutoSizing = true;
        v.Name.fontSizeMin = 12f;
        v.Name.fontSizeMax = 19f;
        v.Name.text = "플레이어";

        // 도장 (오른쪽 위, 살짝 돌림)
        var badge = K.Rect("Badge", rt);
        badge.anchorMin = badge.anchorMax = new Vector2(1f, 1f);
        badge.pivot = new Vector2(1f, 1f);
        badge.anchoredPosition = new Vector2(6f, -10f);
        badge.sizeDelta = new Vector2(86f, 40f);
        badge.localRotation = Quaternion.Euler(0f, 0f, -10f);
        v.Badge = K.ParaFace(badge, K.Gold);
        v.BadgeText = K.Text("Text", v.Badge.transform, K.Display(font), 20f, K.TextOn(K.Gold), TextAlignmentOptions.Center);
        v.BadgeText.fontStyle = FontStyles.Italic;
        K.Fill(v.BadgeText.rectTransform, 3f);
        v.BadgeText.text = "방장";

        // 강퇴 (왼쪽 위)
        v.Kick = K.Btn("Kick", rt, font, "×", K.Danger, 24f, out _);
        var kr = (RectTransform)v.Kick.transform;
        kr.anchorMin = kr.anchorMax = new Vector2(0f, 1f);
        kr.pivot = new Vector2(0f, 1f);
        kr.anchoredPosition = new Vector2(8f, -8f);
        kr.sizeDelta = new Vector2(48f, 38f);
        return v;
    }

    // ── v7 도우미 ──

    /// <summary>화면 위쪽 제목 줄 (왼쪽 큰 제목 + 오른쪽으로 붙는 것들) — 돌려주는 건 줄</summary>
    static Transform TopLine(RectTransform parent, string title, TMP_FontAsset font, out TMP_Text titleText)
    {
        var top = K.Rect("Top", parent);
        Place(top, new Vector2(60f, 960f), new Vector2(1860f, 1046f));
        var h = top.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 22f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        titleText = K.Header(top, font, title, 50f);
        return top;
    }

    /// <summary>판 — 1920×1080 기준 왼쪽 아래(min) · 오른쪽 위(max) 좌표. stripes = 파란 판 줄무늬. 내용은 세로 쌓기(out)</summary>
    static RectTransform Panel(RectTransform parent, string name, Color color, bool stripes, Vector2 min, Vector2 max,
                               out VerticalLayoutGroup content)
    {
        var rt = K.Rect(name, parent);
        Place(rt, min, max);
        var face = K.Sticker(rt, color, 0f, 0f);
        face.raycastTarget = true;
        if (stripes) K.StripesOn(face, 0.14f);
        content = face.gameObject.AddComponent<VerticalLayoutGroup>();
        content.padding = new RectOffset(28, 28, 36, 24);
        content.spacing = 10f;
        content.childControlWidth = content.childControlHeight = true;
        content.childForceExpandWidth = true;
        content.childForceExpandHeight = false;
        return (RectTransform)face.transform;
    }

    /// <summary>판 위쪽 가장자리에 걸친 이름표 — 돌려주는 건 글자</summary>
    static TMP_Text EdgeTag(RectTransform face, TMP_FontAsset font, string label, Color color)
    {
        var tag = K.Tag(face, font, label, color, 22f, out var t, "EdgeTag");
        var le = tag.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
        tag.pivot = new Vector2(0f, 0.5f);
        tag.anchoredPosition = new Vector2(26f, 2f);
        var fit = tag.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return t;
    }

    static void FieldLabel(Transform parent, TMP_FontAsset font, string text)
    {
        var t = K.Text("Label", parent, font, 18f, K.Ink, TextAlignmentOptions.BottomLeft, true);
        K.Size(t, -1f, 26f);
        t.text = text;
    }

    /// <summary>1920×1080 기준 좌표로 놓기 (왼쪽 아래 → 오른쪽 위)</summary>
    static void Place(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = new Vector2(min.x / 1920f, min.y / 1080f);
        rt.anchorMax = new Vector2(max.x / 1920f, max.y / 1080f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
