using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RoomUI — 프리팹 모양(RoomScreenView) 쪽. Resources/UiPrefabSet의 '방 목록 · 대기실'을 켰을 때만 씀.
/// 로직은 코드 모양과 같음 (RoomLauncher · RoomState를 부름).
/// </summary>
public partial class RoomUI
{
    RoomScreenView _view;
    Button _showCodeButton;
    TMPro.TMP_Text _showCodeLabel;

    /// <summary>방 코드 보이기 (내 화면만, 처음엔 가림 — 방송 · 화면 공유 대비)</summary>
    public static bool ShowCode;

    readonly List<RoomRowView> _roomRows = new();
    readonly List<RoomPlayerRowView> _playerRows = new();
    readonly List<RoomPlayer> _vPlayers = new();
    readonly List<UiListRow> _presetRows = new();
    readonly List<BanBind> _bans = new();
    readonly List<StepBind> _steps = new();
    string[] _vPresets = Array.Empty<string>();
    float _vPresetAt, _vNextTick;
    bool _classOpen, _randOpen;
    int _classBanCount, _randBanCount;

    class StepBind
    {
        public StepperRowView Row;
        public Func<RoomSettings, string> Show;
    }

    class BanBind
    {
        public UiOption Opt;
        public AbilityData A;
        public int Id;
        public bool Class;
    }

    static RoomState Rs => RoomState.Instance;

    // ══════════════════ 만들기 · 연결 ══════════════════

    void SetupView()
    {
        if (L == null || L.gameObject != gameObject) return;
        var prefab = UiPrefabSet.RoomPrefab;
        if (prefab == null) return;

        _view = Instantiate(prefab, transform);
        var v = _view;
        // 고정 글자 번역 연결 — 코드가 계속 바꾸는 글자는 빼고
        LobbyLoc.TagStatic(v.transform, v.ToastText, v.CenterText, v.LobbyStatus, v.RoomTitle, v.CodeText, v.VisibilityText,
                           v.CountText, v.MainButtonLabel, v.ReasonText, v.SettingsTitle, v.ClassBanLabel, v.RandBanLabel);
        UiTextFit.Apply(v.transform);           // 다른 언어로 길어지면 줄바꿈 · 글자 줄이기
        WireView();
        GameUiKit.Active(_view.Root, false);
    }

    void WireView()
    {
        var v = _view;

        // 방 목록
        On(v.ReconnectButton, () => L.ConnectLobby());
        if (v.NickInput != null)
        {
            v.NickInput.characterLimit = 16;
            v.NickInput.onEndEdit.AddListener(s => RoomLauncher.Nick = s.Trim());
        }
        if (v.RoomNameInput != null) v.RoomNameInput.characterLimit = 24;
        if (v.CodeInput != null)
        {
            v.CodeInput.characterLimit = 8;
            v.CodeInput.contentType = TMPro.TMP_InputField.ContentType.Password;   // 입력하는 코드도 ●로 (방송 · 화면 공유 대비)
            v.CodeInput.asteriskChar = '*';
        }
        On(v.HiddenOption, () => v.HiddenOption.SetOn(!v.HiddenOption.IsOn));
        On(v.CreateButton, () =>
        {
            string n = v.RoomNameInput != null ? v.RoomNameInput.text.Trim() : "";
            L.CreateRoom(string.IsNullOrEmpty(n) ? SetLoc.F("lobby.default_room", "{0}의 방", RoomLauncher.Nick) : n,
                         v.HiddenOption != null && v.HiddenOption.IsOn);
        });
        On(v.JoinCodeButton, () => { if (v.CodeInput != null) L.JoinRoom(v.CodeInput.text); });

        // 대기실
        On(v.CopyCodeButton, CopyCode);

        // 코드 보이기 ON/OFF — 복사 버튼을 복제해서 옆에 (프리팹 안 고쳐도 됨). 내 화면에만 적용
        if (v.CopyCodeButton != null)
        {
            _showCodeButton = Instantiate(v.CopyCodeButton, v.CopyCodeButton.transform.parent);
            _showCodeButton.name = "ShowCode";
            _showCodeButton.transform.SetSiblingIndex(v.CopyCodeButton.transform.GetSiblingIndex() + 1);
            _showCodeButton.onClick.RemoveAllListeners();
            foreach (var t in _showCodeButton.GetComponentsInChildren<SetLocText>(true)) Destroy(t);   // 복사 글자 번역 연결은 떼기
            _showCodeLabel = _showCodeButton.GetComponentInChildren<TMPro.TMP_Text>(true);
            var le = _showCodeButton.GetComponent<UnityEngine.UI.LayoutElement>();       // 가로 최소 120 (글자 '코드 가리기'가 안 잘리게)
            if (le == null) le = _showCodeButton.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.minWidth = Mathf.Max(le.minWidth, 120f);
            le.preferredWidth = Mathf.Max(le.preferredWidth, 120f);
            On(_showCodeButton, () => ShowCode = !ShowCode);
        }
        On(v.MainButton, MainPressed);
        On(v.LeaveButton, () => L.Leave());

        // 설정 — 값 줄
        Step(v.MapRow, s => L.MapName(s.MapIndex),
             (s, d) => { int n = L.MapCount; s.MapIndex = ((s.MapIndex + d) % n + n) % n; }, 1);
        Step(v.MaxPlayersRow, s => SetLoc.F("lobby.unit.people", "{0}명", s.MaxPlayers),
             (s, d) => s.MaxPlayers = Mathf.Clamp(s.MaxPlayers + d, Mathf.Max(2, Rs != null ? Rs.PlayerCount : 2), L.HardMaxPlayers), 1);
        Step(v.RoundTimeRow, s => s.RoundSeconds / 60 == 0 ? SetLoc.T("lobby.unlimited", "무제한") : SetLoc.F("lobby.unit.min", "{0}분", s.RoundSeconds / 60),
             (s, d) => s.RoundSeconds = Mathf.Clamp(s.RoundSeconds / 60 + d, 0, 60) * 60, 1);
        Step(v.StartSoulRow, s => s.StartSoul.ToString(),
             (s, d) => s.StartSoul = Mathf.Clamp(s.StartSoul + d, 0, 9999), 10);
        Step(v.ChestRow, s => SetLoc.F("lobby.unit.sec", "{0}초", Mathf.RoundToInt(s.ChestRefillSeconds)),
             (s, d) => s.ChestRefillSeconds = Mathf.Clamp(Mathf.RoundToInt(s.ChestRefillSeconds) + d, 5, 600), 5);
        Step(v.SubCostRow, s => SetLoc.F("lobby.unit.soul", "{0} 소울", s.SubAltarCost),
             (s, d) => s.SubAltarCost = Mathf.Clamp(s.SubAltarCost + d, 0, 9999), 5);
        Step(v.CentralCostRow, s => SetLoc.F("lobby.unit.soul", "{0} 소울", s.CentralAltarCost),
             (s, d) => s.CentralAltarCost = Mathf.Clamp(s.CentralAltarCost + d, 0, 9999), 5);
        Step(v.NeededRow, s => SetLoc.F("lobby.unit.count", "{0}개", s.AltarsNeeded),
             (s, d) => s.AltarsNeeded = Mathf.Clamp(s.AltarsNeeded + d, 0, Altar.MaxSub), 1);
        Step(v.ChannelRow, s => SetLoc.F("lobby.unit.sec", "{0}초", s.CentralChannelSeconds),
             (s, d) => s.CentralChannelSeconds = Mathf.Clamp(s.CentralChannelSeconds + d, 1, 60), 1);

        // 설정 — 켜고 끄기
        On(v.FallDeath, () => Change(s => s.Fall = FallRule.Death));
        On(v.FallReturn, () => Change(s => s.Fall = FallRule.Return));
        On(v.TeamOption, () => Change(s => s.TeamMode = !s.TeamMode));
        On(v.HiddenSetting, () => Change(s => s.Hidden = !s.Hidden));
        On(v.NeedAllOption, () => Change(s => s.AltarsNeedAll = !s.AltarsNeedAll));

        // 밴
        On(v.ClassBanHeader, () => _classOpen = !_classOpen);
        On(v.RandBanHeader, () => _randOpen = !_randOpen);
        On(v.ClassBanClear, () => Change(s => s.BanClass.Clear()));
        On(v.RandBanClear, () => Change(s => s.BanRandom.Clear()));
        _classBanCount = BuildBans(v.ClassBanContent, classIdRange, true);
        _randBanCount = BuildBans(v.RandBanContent, randomIdRange, false);

        // 프리셋
        On(v.CopySettingsButton, () =>
        {
            if (Rs == null) return;
            GUIUtility.systemCopyBuffer = Rs.Settings.ToShareCode();
            L.Toast(SetLoc.T("lobby.settings_copied", "방 설정을 클립보드에 복사함"));
        });
        On(v.PasteButton, () =>
        {
            if (Rs != null && RoomSettings.TryParse(GUIUtility.systemCopyBuffer, out var p) && Rs.ApplySettings(p))
                L.Toast(SetLoc.T("lobby.settings_pasted", "붙여 넣은 설정 적용"));
            else
                L.Toast(SetLoc.T("lobby.no_settings", "클립보드에 방 설정이 없어"));
        });
        if (v.PresetNameInput != null)
        {
            v.PresetNameInput.characterLimit = 24;
            v.PresetNameInput.text = _presetName;
        }
        On(v.SaveButton, () =>
        {
            if (Rs == null) return;
            string n = v.PresetNameInput != null ? v.PresetNameInput.text.Trim() : _presetName;
            if (Rs.Settings.SaveAs(n)) L.Toast(SetLoc.F("lobby.saved", "[{0}] 저장함", n));
            _vPresetAt = 0f;
        });
        On(v.OpenFolderButton, () =>
        {
            System.IO.Directory.CreateDirectory(RoomSettings.PresetFolder);
            Application.OpenURL("file:///" + RoomSettings.PresetFolder.Replace('\\', '/'));
        });
    }

    void Step(StepperRowView row, Func<RoomSettings, string> show, Action<RoomSettings, int> apply, int step)
    {
        if (row == null) return;
        _steps.Add(new StepBind { Row = row, Show = show });
        On(row.Minus, () => Change(s => apply(s, -step)));
        On(row.Plus, () => Change(s => apply(s, step)));
        On(row.BigMinus, () => Change(s => apply(s, -step * 10)));
        On(row.BigPlus, () => Change(s => apply(s, step * 10)));
    }

    int BuildBans(RectTransform content, Vector2Int range, bool cls)
    {
        if (content == null || _view.BanTogglePrefab == null) return 0;
        int n = 0;
        foreach (var a in abilities.Where(x => x != null && x.Id >= range.x && x.Id <= range.y).OrderBy(x => x.Id))
        {
            var o = Instantiate(_view.BanTogglePrefab, content);
            o.SetText(BanText(a));
            int id = a.Id;
            On(o, () => Change(s =>
            {
                var list = cls ? s.BanClass : s.BanRandom;
                if (list.Contains(id)) list.Remove(id); else list.Add(id);
            }));
            _bans.Add(new BanBind { Opt = o, A = a, Id = id, Class = cls });
            n++;
        }
        return n;
    }

    /// <summary>방 코드 가리기 — 길이만 보이게 (스트리머 · 화면 공유에서 코드 유출 방지)</summary>
    public static string Mask(string code) => string.IsNullOrEmpty(code) ? "" : new string('*', code.Length);

    /// <summary>밴 칸 글자 — 능력 이름은 지금 언어로</summary>
    static string BanText(AbilityData a) => $"{a.Id % 100:00} {AbilityUi.Name(a)}";

    /// <summary>언어 바뀜 — 코드로 만든 칸(밴 능력 이름) 다시. 나머지는 매 틱 / SetLocText가 함</summary>
    public void OnLocaleChanged()
    {
        foreach (var b in _bans)
            if (b.Opt != null && b.A != null) b.Opt.SetText(BanText(b.A));
    }

    /// <summary>설정 바꾸기 — 방장만 (RoomState가 대기실인지 확인)</summary>
    static void Change(Action<RoomSettings> edit)
    {
        var rs = Rs;
        if (rs == null || !rs.IsLocalHost) return;
        var s = rs.Settings.Clone();
        edit(s);
        rs.ApplySettings(s);
    }

    void CopyCode()
    {
        var runner = RoomLauncher.Runner;
        string code = runner != null && runner.SessionInfo != null ? runner.SessionInfo.Name : "";
        GUIUtility.systemCopyBuffer = code;
        L.Toast(SetLoc.T("lobby.code_copied", "방 코드 복사함"));
    }

    void MainPressed()
    {
        var rs = Rs;
        var runner = RoomLauncher.Runner;
        if (rs == null || runner == null) return;
        if (rs.IsLocalHost) rs.StartGame();
        else rs.RPC_SetReady(runner.LocalPlayer, !rs.IsReady(runner.LocalPlayer));
    }

    static void On(Button b, Action a)
    {
        if (b != null) b.onClick.AddListener(() => a());
    }

    static void On(UiOption o, Action a)
    {
        if (o != null && o.Button != null) o.Button.onClick.AddListener(() => a());
    }

    static void Opt(UiOption o, bool on, bool interact)
    {
        if (o == null) return;
        o.SetOn(on);
        GameUiKit.Interact(o.Button, interact);
    }

    // ══════════════════ 매 프레임 ══════════════════

    void TickView()
    {
        var v = _view;
        bool show = Showing;
        GameUiKit.Active(v.Root, show);
        if (!show) return;

        GameUiKit.DropButtonFocus();
        if (Time.unscaledTime < _vNextTick) return;
        _vNextTick = Time.unscaledTime + 0.1f;
        GameUiKit.EnsureEventSystem();

        bool toast = !string.IsNullOrEmpty(L.Message) && Time.unscaledTime - L.MessageTime < 6f;
        GameUiKit.Active(v.ToastBox, toast);
        if (toast) GameUiKit.Set(v.ToastText, LobbyLoc.Msg(L.Message));

        var rs = Rs;
        bool inRoom = L.State == RoomLauncher.LinkState.InRoom;
        GameUiKit.Active(v.LobbyPanel, !inRoom);
        GameUiKit.Active(v.WaitingPanel, inRoom && rs != null);
        if (v.CenterText != null)
        {
            GameUiKit.Active(v.CenterText, inRoom && rs == null);
            GameUiKit.Set(v.CenterText, SetLoc.T("lobby.loading_room", "방 정보 받는 중..."));
        }

        if (!inRoom) TickLobby(v);
        else if (rs != null) TickWaiting(v, rs);
    }

    void TickLobby(RoomScreenView v)
    {
        string status = L.State switch
        {
            RoomLauncher.LinkState.ConnectingLobby => SetLoc.T("lobby.connecting", "서버 연결 중..."),
            RoomLauncher.LinkState.Joining => SetLoc.T("lobby.joining", "들어가는 중..."),
            RoomLauncher.LinkState.Leaving => SetLoc.T("lobby.leaving", "나가는 중..."),
            RoomLauncher.LinkState.Offline => SetLoc.T("lobby.offline", "연결 안 됨"),
            _ => "",
        };
        GameUiKit.Set(v.LobbyStatus, status);
        GameUiKit.Active(v.ReconnectButton, L.State == RoomLauncher.LinkState.Offline);

        bool canAct = L.State == RoomLauncher.LinkState.InLobby;
        if (v.NickInput != null && !v.NickInput.isFocused && v.NickInput.text != RoomLauncher.Nick)
            v.NickInput.SetTextWithoutNotify(RoomLauncher.Nick);
        GameUiKit.Interact(v.CreateButton, canAct);
        GameUiKit.Interact(v.JoinCodeButton, canAct && v.CodeInput != null && v.CodeInput.text.Trim().Length > 0);

        int shown = 0;
        if (v.RoomRowPrefab != null && v.RoomListContent != null)
        {
            foreach (var si in L.Sessions)
            {
                if (si == null || !si.IsVisible) continue;

                int max = RoomLauncher.PropInt(si, "m", si.MaxPlayers);
                bool playing = RoomLauncher.PropInt(si, "st", 0) == 1;
                bool locked = !si.IsOpen || si.PlayerCount >= max;

                var row = RoomRow(shown++);
                GameUiKit.Active(row, true);
                row.Code = si.Name;
                GameUiKit.Set(row.Name, RoomLauncher.PropStr(si, "n", si.Name));
                GameUiKit.Set(row.Count, $"{si.PlayerCount}/{max}");
                GameUiKit.Set(row.Lock, locked ? SetLoc.T("lobby.lock", "잠금") : "");
                GameUiKit.Set(row.Map, L.MapName(RoomLauncher.PropInt(si, "map", 0)));
                GameUiKit.Set(row.State, playing ? SetLoc.T("lobby.playing", "게임 중") : SetLoc.T("lobby.waiting", "대기 중"));
                GameUiKit.Set(row.JoinLabel, playing ? SetLoc.T("lobby.spectate", "관전") : SetLoc.T("lobby.join", "참가"));
                GameUiKit.Interact(row.Join, canAct && !locked);
            }
        }
        for (int i = shown; i < _roomRows.Count; i++) GameUiKit.Active(_roomRows[i], false);
        if (v.EmptyText != null) GameUiKit.Active(v.EmptyText, shown == 0);
    }

    RoomRowView RoomRow(int i)
    {
        while (_roomRows.Count <= i)
        {
            var r = Instantiate(_view.RoomRowPrefab, _view.RoomListContent);
            LobbyLoc.TagStatic(r.transform, r.Name, r.Count, r.Lock, r.Map, r.State, r.JoinLabel);
            UiTextFit.Apply(r.transform);
            On(r.Join, () => { if (!string.IsNullOrEmpty(r.Code)) L.JoinRoom(r.Code); });
            _roomRows.Add(r);
        }
        return _roomRows[i];
    }

    void TickWaiting(RoomScreenView v, RoomState rs)
    {
        var runner = RoomLauncher.Runner;
        if (runner == null) return;

        bool host = rs.IsLocalHost;
        var me = runner.LocalPlayer;
        string code = runner.SessionInfo != null ? runner.SessionInfo.Name : "";

        GameUiKit.Set(v.RoomTitle, rs.RoomName.ToString());
        GameUiKit.Set(v.CodeText, SetLoc.F("lobby.code_line", "방 코드  {0}", ShowCode ? code : Mask(code)));   // 복사 버튼은 항상 진짜 코드
        GameUiKit.Set(_showCodeLabel, ShowCode ? SetLoc.T("lobby.hide_code", "코드 가리기") : SetLoc.T("lobby.show_code", "코드 보이기"));
        GameUiKit.Set(v.VisibilityText, rs.Hidden ? SetLoc.T("lobby.hidden_room", "숨김 방 — 코드로만 들어올 수 있어") : SetLoc.T("lobby.public_room", "공개 방"));
        GameUiKit.Set(v.CountText, SetLoc.F("lobby.people", "사람 {0}/{1}", rs.PlayerCount, rs.MaxPlayers));

        // 명단
        rs.SortedPlayers(_vPlayers);
        if (v.PlayerRowPrefab != null && v.PlayerListContent != null)
        {
            for (int i = 0; i < _vPlayers.Count; i++)
            {
                var p = _vPlayers[i];
                var row = PlayerRow(i);
                GameUiKit.Active(row, true);
                row.Ref = p.Ref;

                bool isHost = p.Ref == rs.Host;
                GameUiKit.Set(row.BadgeText, isHost ? SetLoc.T("lobby.host", "방장") : p.Ready ? SetLoc.T("lobby.ready", "준비") : SetLoc.T("lobby.wait_badge", "대기"));
                if (row.Badge != null)
                {
                    row.Badge.color = isHost ? v.HostBadge : p.Ready ? v.ReadyBadge : v.WaitBadge;
                    if (row.BadgeText != null) row.BadgeText.color = GameUiKit.TextOn(row.Badge.color);
                }
                GameUiKit.Set(row.Name, p.Name.ToString() + (p.Ref == me ? "  (" + SetLoc.T("lobby.me", "나") + ")" : ""));
                GameUiKit.Active(row.Kick, host && p.Ref != me);
                row.Look(p.Ref.PlayerId, p.Ref == me);         // v7 카드 — 사람마다 색 · 나 = 민트 테두리
            }
        }
        for (int i = _vPlayers.Count; i < _playerRows.Count; i++) GameUiKit.Active(_playerRows[i], false);

        // 시작 / 준비
        if (host)
        {
            bool can = rs.CanStart(out string why);
            GameUiKit.Set(v.MainButtonLabel, SetLoc.T("lobby.start", "게임 시작"));
            if (v.MainButtonFace != null) v.MainButtonFace.color = v.StartColor;
            if (v.MainButtonFace != null && v.MainButtonLabel != null) v.MainButtonLabel.color = GameUiKit.TextOn(v.MainButtonFace.color);
            GameUiKit.Interact(v.MainButton, can);
            GameUiKit.Set(v.ReasonText, can ? "" : LobbyLoc.Msg(why));
        }
        else
        {
            bool ready = rs.IsReady(me);
            GameUiKit.Set(v.MainButtonLabel, ready ? SetLoc.T("lobby.unready", "준비 취소") : SetLoc.T("lobby.ready", "준비"));
            if (v.MainButtonFace != null) v.MainButtonFace.color = ready ? v.UnreadyColor : v.ReadyColor;
            if (v.MainButtonFace != null && v.MainButtonLabel != null) v.MainButtonLabel.color = GameUiKit.TextOn(v.MainButtonFace.color);
            GameUiKit.Interact(v.MainButton, true);
            GameUiKit.Set(v.ReasonText, ready ? SetLoc.T("lobby.wait_host", "방장이 시작하면 게임이 시작돼") : "");
        }

        // 설정
        GameUiKit.Set(v.SettingsTitle, host ? SetLoc.T("lobby.settings", "방 설정") : SetLoc.T("lobby.settings_guest", "방 설정 (방장만)"));
        var s = rs.Settings;
        foreach (var b in _steps)
        {
            GameUiKit.Set(b.Row.Value, b.Show(s));
            b.Row.SetInteractable(host);
        }
        if (v.MapRow != null) v.MapRow.SetInteractable(host && L.MapCount > 1);

        Opt(v.FallDeath, s.Fall == FallRule.Death, host);
        Opt(v.FallReturn, s.Fall == FallRule.Return, host);
        Opt(v.TeamOption, s.TeamMode, host);
        Opt(v.HiddenSetting, s.Hidden, host);
        Opt(v.NeedAllOption, s.AltarsNeedAll, host);
        GameUiKit.Active(v.NeededRow, !s.AltarsNeedAll);

        // 밴
        foreach (var b in _bans)
            Opt(b.Opt, (b.Class ? s.BanClass : s.BanRandom).Contains(b.Id), host);
        GameUiKit.Active(v.ClassBanContent, _classOpen);
        GameUiKit.Active(v.RandBanContent, _randOpen);
        GameUiKit.Set(v.ClassBanLabel, BanLabel(SetLoc.T("lobby.ban_class", "클래스 밴"), _classOpen, s.BanClass.Count, _classBanCount));
        GameUiKit.Set(v.RandBanLabel, BanLabel(SetLoc.T("lobby.ban_random", "랜덤 능력 밴"), _randOpen, s.BanRandom.Count, _randBanCount));
        GameUiKit.Active(v.ClassBanClear, host && s.BanClass.Count > 0);
        GameUiKit.Active(v.RandBanClear, host && s.BanRandom.Count > 0);

        // 프리셋
        GameUiKit.Interact(v.PasteButton, host);
        if (Time.unscaledTime >= _vPresetAt)
        {
            _vPresetAt = Time.unscaledTime + 2f;
            _vPresets = RoomSettings.ListPresets();
        }
        if (v.PresetRowPrefab != null && v.PresetListContent != null)
        {
            for (int i = 0; i < _vPresets.Length; i++)
            {
                var row = PresetRow(i);
                GameUiKit.Active(row, true);
                GameUiKit.Set(row.Label, _vPresets[i]);
                GameUiKit.Interact(row.Action, host);
            }
        }
        for (int i = _vPresets.Length; i < _presetRows.Count; i++) GameUiKit.Active(_presetRows[i], false);
    }

    static string BanLabel(string title, bool open, int banned, int total) =>
        total == 0
            ? $"{title} — 능력 목록 비어 있음 (RoomUI 우클릭 → 능력 목록 자동 채우기)"
            : $"{(open ? "[-]" : "[+]")} {title} ({SetLoc.F("lobby.unit.count", "{0}개", banned)})";

    RoomPlayerRowView PlayerRow(int i)
    {
        while (_playerRows.Count <= i)
        {
            var r = Instantiate(_view.PlayerRowPrefab, _view.PlayerListContent);
            LobbyLoc.TagStatic(r.transform, r.Name, r.BadgeText);       // 강퇴 버튼 글자 등
            UiTextFit.Apply(r.transform);
            On(r.Kick, () => { if (Rs != null) Rs.Kick(r.Ref); });
            _playerRows.Add(r);
        }
        return _playerRows[i];
    }

    UiListRow PresetRow(int i)
    {
        while (_presetRows.Count <= i)
        {
            var r = Instantiate(_view.PresetRowPrefab, _view.PresetListContent);
            LobbyLoc.TagStatic(r.transform, r.Label);                   // 불러오기 버튼 글자
            int idx = _presetRows.Count;
            On(r.Action, () =>
            {
                if (Rs == null || idx >= _vPresets.Length) return;
                string f = _vPresets[idx];
                if (RoomSettings.TryLoad(f, out var p) && Rs.ApplySettings(p)) L.Toast(SetLoc.F("lobby.loaded", "[{0}] 불러옴", f));
                else L.Toast(SetLoc.F("lobby.load_failed", "[{0}] 불러오기 실패", f));
            });
            _presetRows.Add(r);
        }
        return _presetRows[i];
    }
}
