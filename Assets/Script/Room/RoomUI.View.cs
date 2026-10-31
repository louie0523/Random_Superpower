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
        if (v.CodeInput != null) v.CodeInput.characterLimit = 8;
        On(v.HiddenOption, () => v.HiddenOption.SetOn(!v.HiddenOption.IsOn));
        On(v.CreateButton, () =>
        {
            string n = v.RoomNameInput != null ? v.RoomNameInput.text.Trim() : "";
            L.CreateRoom(string.IsNullOrEmpty(n) ? $"{RoomLauncher.Nick}의 방" : n,
                         v.HiddenOption != null && v.HiddenOption.IsOn);
        });
        On(v.JoinCodeButton, () => { if (v.CodeInput != null) L.JoinRoom(v.CodeInput.text); });

        // 대기실
        On(v.CopyCodeButton, CopyCode);
        On(v.MainButton, MainPressed);
        On(v.LeaveButton, () => L.Leave());

        // 설정 — 값 줄
        Step(v.MapRow, s => L.MapName(s.MapIndex),
             (s, d) => { int n = L.MapCount; s.MapIndex = ((s.MapIndex + d) % n + n) % n; }, 1);
        Step(v.MaxPlayersRow, s => $"{s.MaxPlayers}명",
             (s, d) => s.MaxPlayers = Mathf.Clamp(s.MaxPlayers + d, Mathf.Max(2, Rs != null ? Rs.PlayerCount : 2), L.HardMaxPlayers), 1);
        Step(v.RoundTimeRow, s => s.RoundSeconds / 60 == 0 ? "무제한" : $"{s.RoundSeconds / 60}분",
             (s, d) => s.RoundSeconds = Mathf.Clamp(s.RoundSeconds / 60 + d, 0, 60) * 60, 1);
        Step(v.StartSoulRow, s => s.StartSoul.ToString(),
             (s, d) => s.StartSoul = Mathf.Clamp(s.StartSoul + d, 0, 9999), 10);
        Step(v.ChestRow, s => $"{Mathf.RoundToInt(s.ChestRefillSeconds)}초",
             (s, d) => s.ChestRefillSeconds = Mathf.Clamp(Mathf.RoundToInt(s.ChestRefillSeconds) + d, 5, 600), 5);
        Step(v.SubCostRow, s => $"{s.SubAltarCost} 소울",
             (s, d) => s.SubAltarCost = Mathf.Clamp(s.SubAltarCost + d, 0, 9999), 5);
        Step(v.CentralCostRow, s => $"{s.CentralAltarCost} 소울",
             (s, d) => s.CentralAltarCost = Mathf.Clamp(s.CentralAltarCost + d, 0, 9999), 5);
        Step(v.NeededRow, s => $"{s.AltarsNeeded}개",
             (s, d) => s.AltarsNeeded = Mathf.Clamp(s.AltarsNeeded + d, 0, Altar.MaxSub), 1);
        Step(v.ChannelRow, s => $"{s.CentralChannelSeconds}초",
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
            L.Toast("방 설정을 클립보드에 복사함");
        });
        On(v.PasteButton, () =>
        {
            if (Rs != null && RoomSettings.TryParse(GUIUtility.systemCopyBuffer, out var p) && Rs.ApplySettings(p))
                L.Toast("붙여 넣은 설정 적용");
            else
                L.Toast("클립보드에 방 설정이 없어");
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
            if (Rs.Settings.SaveAs(n)) L.Toast($"'{n}' 저장함");
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
            string label = string.IsNullOrEmpty(a.DisplayName) ? a.name : a.DisplayName;
            o.SetText($"{a.Id % 100:00} {label}");
            int id = a.Id;
            On(o, () => Change(s =>
            {
                var list = cls ? s.BanClass : s.BanRandom;
                if (list.Contains(id)) list.Remove(id); else list.Add(id);
            }));
            _bans.Add(new BanBind { Opt = o, Id = id, Class = cls });
            n++;
        }
        return n;
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
        L.Toast("방 코드 복사함");
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
        if (toast) GameUiKit.Set(v.ToastText, L.Message);

        var rs = Rs;
        bool inRoom = L.State == RoomLauncher.LinkState.InRoom;
        GameUiKit.Active(v.LobbyPanel, !inRoom);
        GameUiKit.Active(v.WaitingPanel, inRoom && rs != null);
        if (v.CenterText != null)
        {
            GameUiKit.Active(v.CenterText, inRoom && rs == null);
            GameUiKit.Set(v.CenterText, "방 정보 받는 중...");
        }

        if (!inRoom) TickLobby(v);
        else if (rs != null) TickWaiting(v, rs);
    }

    void TickLobby(RoomScreenView v)
    {
        string status = L.State switch
        {
            RoomLauncher.LinkState.ConnectingLobby => "서버 연결 중...",
            RoomLauncher.LinkState.Joining => "들어가는 중...",
            RoomLauncher.LinkState.Leaving => "나가는 중...",
            RoomLauncher.LinkState.Offline => "연결 안 됨",
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
                GameUiKit.Set(row.Lock, locked ? "잠금" : "");
                GameUiKit.Set(row.Map, L.MapName(RoomLauncher.PropInt(si, "map", 0)));
                GameUiKit.Set(row.State, playing ? "게임 중" : "대기 중");
                GameUiKit.Set(row.JoinLabel, playing ? "관전" : "참가");
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
        GameUiKit.Set(v.CodeText, $"방 코드  {code}");
        GameUiKit.Set(v.VisibilityText, rs.Hidden ? "숨김 방 — 코드로만 들어올 수 있어" : "공개 방");
        GameUiKit.Set(v.CountText, $"사람 {rs.PlayerCount}/{rs.MaxPlayers}");

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
                GameUiKit.Set(row.BadgeText, isHost ? "방장" : p.Ready ? "준비" : "대기");
                if (row.Badge != null) row.Badge.color = isHost ? v.HostBadge : p.Ready ? v.ReadyBadge : v.WaitBadge;
                GameUiKit.Set(row.Name, p.Name.ToString() + (p.Ref == me ? "  (나)" : ""));
                GameUiKit.Active(row.Kick, host && p.Ref != me);
            }
        }
        for (int i = _vPlayers.Count; i < _playerRows.Count; i++) GameUiKit.Active(_playerRows[i], false);

        // 시작 / 준비
        if (host)
        {
            bool can = rs.CanStart(out string why);
            GameUiKit.Set(v.MainButtonLabel, "게임 시작");
            if (v.MainButtonFace != null) v.MainButtonFace.color = v.StartColor;
            GameUiKit.Interact(v.MainButton, can);
            GameUiKit.Set(v.ReasonText, can ? "" : why);
        }
        else
        {
            bool ready = rs.IsReady(me);
            GameUiKit.Set(v.MainButtonLabel, ready ? "준비 취소" : "준비");
            if (v.MainButtonFace != null) v.MainButtonFace.color = ready ? v.UnreadyColor : v.ReadyColor;
            GameUiKit.Interact(v.MainButton, true);
            GameUiKit.Set(v.ReasonText, ready ? "방장이 시작하면 게임이 시작돼" : "");
        }

        // 설정
        GameUiKit.Set(v.SettingsTitle, host ? "방 설정" : "방 설정 (방장만)");
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
        GameUiKit.Set(v.ClassBanLabel, BanLabel("클래스 밴", _classOpen, s.BanClass.Count, _classBanCount));
        GameUiKit.Set(v.RandBanLabel, BanLabel("랜덤 능력 밴", _randOpen, s.BanRandom.Count, _randBanCount));
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
            : $"{(open ? "[-]" : "[+]")} {title} ({banned}개)";

    RoomPlayerRowView PlayerRow(int i)
    {
        while (_playerRows.Count <= i)
        {
            var r = Instantiate(_view.PlayerRowPrefab, _view.PlayerListContent);
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
            int idx = _presetRows.Count;
            On(r.Action, () =>
            {
                if (Rs == null || idx >= _vPresets.Length) return;
                string f = _vPresets[idx];
                if (RoomSettings.TryLoad(f, out var p) && Rs.ApplySettings(p)) L.Toast($"'{f}' 불러옴");
                else L.Toast($"'{f}' 불러오기 실패");
            });
            _presetRows.Add(r);
        }
        return _presetRows[i];
    }
}
