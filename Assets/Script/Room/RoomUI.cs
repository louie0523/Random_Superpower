using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

/// <summary>
/// 방 목록 · 대기실 화면 (기본 모양 — 기능 먼저, 디자인 UI는 나중에 이 자리를 바꿈).
/// RoomLauncher와 같은 오브젝트에 붙임.
/// </summary>
public class RoomUI : MonoBehaviour
{
    [Tooltip("밴 목록에 보여 줄 능력들 — 컴포넌트 우클릭 '능력 목록 자동 채우기'")]
    [SerializeField] private List<AbilityData> abilities = new();

    [Tooltip("화면 크기 배율")]
    [SerializeField] private float uiScale = 1f;

    [Tooltip("클래스 ID 범위")]
    [SerializeField] private Vector2Int classIdRange = new(6000, 6999);

    [Tooltip("랜덤 능력 ID 범위")]
    [SerializeField] private Vector2Int randomIdRange = new(8000, 8999);

    string _roomName = "";
    string _code = "";
    string _presetName = "내 설정";
    bool _hidden;
    bool _openClassBan, _openRandBan;
    Vector2 _listScroll, _playerScroll, _setScroll;
    string[] _presetFiles = Array.Empty<string>();
    float _presetRefresh;
    readonly List<RoomPlayer> _players = new();

    GUISkin _skin;
    GUIStyle _title, _small, _center, _left;

    static RoomLauncher L => RoomLauncher.Instance;

    // ══════════════════ 표시 여부 · 커서 ══════════════════

    bool Showing
    {
        get
        {
            if (L == null || L.gameObject != gameObject) return false;
            if (L.State != RoomLauncher.LinkState.InRoom) return true;
            var rs = RoomState.Instance;
            return rs == null || rs.Phase == RoomPhase.Waiting;
        }
    }

    void Update()
    {
        if (!Showing) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ══════════════════ 그리기 ══════════════════

    void OnGUI()
    {
        if (!Showing && (L == null || Time.unscaledTime - L.MessageTime > 6f)) return;
        if (L == null || L.gameObject != gameObject) return;

        EnsureSkin();
        GUI.skin = _skin;

        float s = Screen.height / 1080f * Mathf.Max(0.3f, uiScale);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;

        if (Showing)
        {
            var rs = RoomState.Instance;
            if (L.State != RoomLauncher.LinkState.InRoom) DrawLobby(W, H);
            else if (rs == null) CenterText(W, H, "방 정보 받는 중...");
            else DrawWaiting(rs, W, H);
        }

        DrawMessage(W, H);
    }

    // ── 방 목록 ──
    void DrawLobby(float W, float H)
    {
        float w = Mathf.Min(1000f, W - 40f);
        GUILayout.BeginArea(new Rect((W - w) / 2f, 40f, w, H - 80f), GUI.skin.box);
        GUILayout.Label("방 목록", _title);

        switch (L.State)
        {
            case RoomLauncher.LinkState.ConnectingLobby: GUILayout.Label("서버 연결 중..."); break;
            case RoomLauncher.LinkState.Joining: GUILayout.Label("들어가는 중..."); break;
            case RoomLauncher.LinkState.Leaving: GUILayout.Label("나가는 중..."); break;
            case RoomLauncher.LinkState.Offline:
                if (GUILayout.Button("다시 연결", GUILayout.Width(200))) L.ConnectLobby();
                break;
        }

        bool canAct = L.State == RoomLauncher.LinkState.InLobby;

        // 닉네임
        GUILayout.BeginHorizontal();
        GUILayout.Label("닉네임", GUILayout.Width(110));
        string nick = GUILayout.TextField(RoomLauncher.Nick, 16, GUILayout.Width(300));
        if (nick != RoomLauncher.Nick) RoomLauncher.Nick = nick;
        GUILayout.EndHorizontal();
        GUILayout.Space(10);

        // 표 머리
        GUILayout.BeginHorizontal();
        GUILayout.Label("방 이름", _small, GUILayout.Width(330));
        GUILayout.Label("인원", _small, GUILayout.Width(90));
        GUILayout.Label("잠금", _small, GUILayout.Width(70));
        GUILayout.Label("맵", _small, GUILayout.Width(180));
        GUILayout.Label("상태", _small, GUILayout.Width(110));
        GUILayout.EndHorizontal();

        _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(Mathf.Max(200f, H - 470f)));
        int shown = 0;
        foreach (var si in L.Sessions)
        {
            if (si == null || !si.IsVisible) continue;
            shown++;

            string name = RoomLauncher.PropStr(si, "n", si.Name);
            int max = RoomLauncher.PropInt(si, "m", si.MaxPlayers);
            int map = RoomLauncher.PropInt(si, "map", 0);
            bool playing = RoomLauncher.PropInt(si, "st", 0) == 1;
            bool locked = !si.IsOpen || si.PlayerCount >= max;   // 가득 참 등 — 숨김 방은 목록에 안 나옴

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(name, GUILayout.Width(330));
            GUILayout.Label($"{si.PlayerCount}/{max}", GUILayout.Width(90));
            GUILayout.Label(locked ? "잠금" : "", GUILayout.Width(70));
            GUILayout.Label(L.MapName(map), GUILayout.Width(180));
            GUILayout.Label(playing ? "게임 중" : "대기 중", GUILayout.Width(110));
            GUI.enabled = canAct && !locked;
            if (GUILayout.Button(playing ? "관전" : "참가", GUILayout.Width(110))) L.JoinRoom(si.Name);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        if (shown == 0) GUILayout.Label("열린 방이 없어", _small);
        GUILayout.EndScrollView();

        GUILayout.Space(12);

        // 방 만들기
        GUILayout.BeginHorizontal();
        GUILayout.Label("방 이름", GUILayout.Width(110));
        _roomName = GUILayout.TextField(_roomName, 24, GUILayout.Width(330));
        _hidden = GUILayout.Toggle(_hidden, " 숨김 (코드로만)", GUILayout.Width(220));
        GUI.enabled = canAct;
        if (GUILayout.Button("방 만들기", GUILayout.Width(180)))
            L.CreateRoom(string.IsNullOrWhiteSpace(_roomName) ? $"{RoomLauncher.Nick}의 방" : _roomName.Trim(), _hidden);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        // 코드로 참가
        GUILayout.BeginHorizontal();
        GUILayout.Label("방 코드", GUILayout.Width(110));
        _code = GUILayout.TextField(_code, 8, GUILayout.Width(200));
        GUI.enabled = canAct && _code.Trim().Length > 0;
        if (GUILayout.Button("코드로 참가", GUILayout.Width(180))) L.JoinRoom(_code);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    // ── 대기실 ──
    void DrawWaiting(RoomState rs, float W, float H)
    {
        var runner = RoomLauncher.Runner;
        if (runner == null) return;

        bool host = rs.IsLocalHost;
        var me = runner.LocalPlayer;
        float colW = Mathf.Min(640f, (W - 60f) / 2f);

        // 왼쪽: 명단 · 시작
        GUILayout.BeginArea(new Rect(20f, 20f, colW, H - 40f), GUI.skin.box);
        GUILayout.Label(rs.RoomName.ToString(), _title);

        string code = runner.SessionInfo != null ? runner.SessionInfo.Name : "";
        GUILayout.BeginHorizontal();
        GUILayout.Label($"방 코드  {code}");
        if (GUILayout.Button("복사", GUILayout.Width(100)))
        {
            GUIUtility.systemCopyBuffer = code;
            L.Toast("방 코드 복사함");
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(rs.Hidden ? "숨김 방 — 코드로만 들어올 수 있어" : "공개 방", _small);
        GUILayout.Label($"사람 {rs.PlayerCount}/{rs.MaxPlayers}", _small);
        GUILayout.Space(8);

        rs.SortedPlayers(_players);
        _playerScroll = GUILayout.BeginScrollView(_playerScroll, GUILayout.ExpandHeight(true));
        foreach (var p in _players)
        {
            bool isHost = p.Ref == rs.Host;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(isHost ? "방장" : (p.Ready ? "준비" : "-"), _center, GUILayout.Width(80));
            GUILayout.Label(p.Name.ToString() + (p.Ref == me ? "  (나)" : ""));
            if (host && p.Ref != me && GUILayout.Button("강퇴", GUILayout.Width(90))) rs.Kick(p.Ref);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        if (host)
        {
            bool can = rs.CanStart(out string why);
            GUI.enabled = can;
            if (GUILayout.Button("게임 시작", GUILayout.Height(64))) rs.StartGame();
            GUI.enabled = true;
            if (!can && why.Length > 0) GUILayout.Label(why, _small);
        }
        else
        {
            bool ready = rs.IsReady(me);
            if (GUILayout.Button(ready ? "준비 취소" : "준비", GUILayout.Height(64)))
                rs.RPC_SetReady(me, !ready);
        }
        if (GUILayout.Button("방 나가기", GUILayout.Height(44))) L.Leave();
        GUILayout.EndArea();

        // 오른쪽: 설정 · 프리셋
        GUILayout.BeginArea(new Rect(40f + colW, 20f, colW, H - 40f), GUI.skin.box);
        GUILayout.Label(host ? "방 설정" : "방 설정 (방장만 바꿀 수 있어)", _title);
        _setScroll = GUILayout.BeginScrollView(_setScroll, GUILayout.ExpandHeight(true));
        DrawSettings(rs, host);
        GUILayout.Space(16);
        DrawPresets(rs, host);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawSettings(RoomState rs, bool host)
    {
        var cur = rs.Settings;
        var s = cur.Clone();
        bool changed = false;

        GUI.enabled = host;

        // 맵
        GUILayout.BeginHorizontal();
        GUILayout.Label("맵", GUILayout.Width(170));
        int n = L.MapCount;
        GUI.enabled = host && n > 1;
        if (GUILayout.Button("<", GUILayout.Width(44))) { s.MapIndex = (s.MapIndex - 1 + n) % n; changed = true; }
        GUILayout.Label(L.MapName(s.MapIndex), _center, GUILayout.Width(220));
        if (GUILayout.Button(">", GUILayout.Width(44))) { s.MapIndex = (s.MapIndex + 1) % n; changed = true; }
        GUI.enabled = host;
        GUILayout.EndHorizontal();

        s.MaxPlayers = Step("최대 인원", s.MaxPlayers, 1, Mathf.Max(2, rs.PlayerCount), L.HardMaxPlayers,
                            v => $"{v}명", ref changed, false);

        int m0 = s.RoundSeconds / 60;
        int m1 = Step("라운드 시간", m0, 1, 0, 60, v => v == 0 ? "무제한" : $"{v}분", ref changed, true);
        if (m1 != m0) s.RoundSeconds = m1 * 60;

        s.StartSoul = Step("시작 소울", s.StartSoul, 10, 0, 9999, v => v.ToString(), ref changed, true);

        // 제단 (승리 조건)
        GUILayout.Space(6);
        GUILayout.Label("제단 — 서브 제단 N개 → 중앙 제단", _small);
        s.SubAltarCost = Step("서브 제단 비용", s.SubAltarCost, 5, 0, 9999, v => $"{v} 소울", ref changed, true);
        s.CentralAltarCost = Step("중앙 제단 비용", s.CentralAltarCost, 5, 0, 9999, v => $"{v} 소울", ref changed, true);

        bool all = GUILayout.Toggle(s.AltarsNeedAll, " 서브 제단을 전부 열어야 중앙 제단");
        if (all != s.AltarsNeedAll) { s.AltarsNeedAll = all; changed = true; }
        if (!s.AltarsNeedAll)
            s.AltarsNeeded = Step("필요한 서브 제단", s.AltarsNeeded, 1, 0, Altar.MaxSub,
                                  v => $"{v}개", ref changed, false);

        s.CentralChannelSeconds = Step("중앙 채널 시간", s.CentralChannelSeconds, 1, 1, 60,
                                       v => $"{v}초", ref changed, false);
        GUILayout.Label("맵의 서브 제단이 필요 수보다 적으면 전부 열면 됨", _small);
        GUILayout.Space(6);

        int c0 = Mathf.RoundToInt(s.ChestRefillSeconds);
        int c1 = Step("상자 리필", c0, 5, 5, 600, v => $"{v}초", ref changed, true);
        if (c1 != c0) s.ChestRefillSeconds = c1;

        GUILayout.BeginHorizontal();
        GUILayout.Label("낙사", GUILayout.Width(170));
        int f = GUILayout.Toolbar((int)s.Fall, new[] { "즉사", "부활 지점으로" }, GUILayout.Width(360));
        if (f != (int)s.Fall) { s.Fall = (FallRule)f; changed = true; }
        GUILayout.EndHorizontal();

        bool team = GUILayout.Toggle(s.TeamMode, " 팀전 (설정만 저장 — 팀 나누기는 나중)");
        if (team != s.TeamMode) { s.TeamMode = team; changed = true; }

        bool hid = GUILayout.Toggle(s.Hidden, " 방 숨기기 (코드로만 참가)");
        if (hid != s.Hidden) { s.Hidden = hid; changed = true; }

        GUILayout.Space(8);
        BanSection("클래스 밴", classIdRange, s.BanClass, ref _openClassBan, host, ref changed);
        BanSection("랜덤 능력 밴", randomIdRange, s.BanRandom, ref _openRandBan, host, ref changed);

        GUI.enabled = true;

        if (changed && host) rs.ApplySettings(s);
    }

    void BanSection(string title, Vector2Int range, List<int> list, ref bool open, bool host, ref bool changed)
    {
        bool prev = GUI.enabled;
        GUI.enabled = true;   // 펼치기는 누구나
        if (GUILayout.Button($"{(open ? "[-]" : "[+]")} {title} ({list.Count}개)", _left)) open = !open;
        GUI.enabled = prev;
        if (!open) return;

        var items = abilities.Where(a => a != null && a.Id >= range.x && a.Id <= range.y).ToList();
        if (items.Count == 0)
        {
            GUILayout.Label("RoomUI 능력 목록에 해당 능력이 없어 (컴포넌트 우클릭 → 능력 목록 자동 채우기)", _small);
            return;
        }

        if (host && list.Count > 0 && GUILayout.Button("전부 풀기", GUILayout.Width(160)))
        {
            list.Clear();
            changed = true;
        }

        for (int i = 0; i < items.Count; i += 2)
        {
            GUILayout.BeginHorizontal();
            for (int k = i; k < Mathf.Min(i + 2, items.Count); k++)
            {
                var a = items[k];
                bool on = list.Contains(a.Id);
                string label = string.IsNullOrEmpty(a.DisplayName) ? a.name : a.DisplayName;
                bool nv = GUILayout.Toggle(on, $" {a.Id % 100:00} {label}", GUILayout.Width(280));
                if (nv == on) continue;
                if (nv) list.Add(a.Id); else list.Remove(a.Id);
                changed = true;
            }
            GUILayout.EndHorizontal();
        }
    }

    void DrawPresets(RoomState rs, bool host)
    {
        GUI.enabled = true;
        GUILayout.Label("프리셋", _title);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("설정 복사", GUILayout.Width(200)))
        {
            GUIUtility.systemCopyBuffer = rs.Settings.ToShareCode();
            L.Toast("방 설정을 클립보드에 복사함");
        }
        GUI.enabled = host;
        if (GUILayout.Button("붙여넣기", GUILayout.Width(200)))
        {
            if (RoomSettings.TryParse(GUIUtility.systemCopyBuffer, out var p) && rs.ApplySettings(p))
                L.Toast("붙여 넣은 설정 적용");
            else
                L.Toast("클립보드에 방 설정이 없어");
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        _presetName = GUILayout.TextField(_presetName, 24, GUILayout.Width(300));
        if (GUILayout.Button("파일로 저장", GUILayout.Width(200)))
        {
            if (rs.Settings.SaveAs(_presetName)) L.Toast($"'{_presetName}' 저장함");
            _presetRefresh = 0f;
        }
        GUILayout.EndHorizontal();

        if (Time.unscaledTime >= _presetRefresh)
        {
            _presetRefresh = Time.unscaledTime + 2f;
            _presetFiles = RoomSettings.ListPresets();
        }

        foreach (var f in _presetFiles)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(f, GUILayout.Width(300));
            GUI.enabled = host;
            if (GUILayout.Button("불러오기", GUILayout.Width(200)))
            {
                if (RoomSettings.TryLoad(f, out var p) && rs.ApplySettings(p)) L.Toast($"'{f}' 불러옴");
                else L.Toast($"'{f}' 불러오기 실패");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        if (GUILayout.Button("프리셋 폴더 열기", GUILayout.Width(260)))
        {
            System.IO.Directory.CreateDirectory(RoomSettings.PresetFolder);
            Application.OpenURL("file:///" + RoomSettings.PresetFolder.Replace('\\', '/'));
        }
    }

    // ══════════════════ 작은 도구 ══════════════════

    int Step(string label, int v, int step, int min, int max, Func<int, string> fmt, ref bool changed, bool big)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(170));
        int nv = v;
        if (big && GUILayout.Button("--", GUILayout.Width(52))) nv -= step * 10;
        if (GUILayout.Button("-", GUILayout.Width(44))) nv -= step;
        GUILayout.Label(fmt(v), _center, GUILayout.Width(140));
        if (GUILayout.Button("+", GUILayout.Width(44))) nv += step;
        if (big && GUILayout.Button("++", GUILayout.Width(52))) nv += step * 10;
        GUILayout.EndHorizontal();

        nv = Mathf.Clamp(nv, min, Mathf.Max(min, max));
        if (nv != v) changed = true;
        return nv;
    }

    void CenterText(float W, float H, string text)
    {
        GUI.Label(new Rect(0f, H / 2f - 30f, W, 60f), text, _center);
    }

    void DrawMessage(float W, float H)
    {
        if (L == null || string.IsNullOrEmpty(L.Message)) return;
        if (Time.unscaledTime - L.MessageTime > 6f) return;
        GUI.Box(new Rect(W / 2f - 400f, H - 90f, 800f, 56f), L.Message, _center);
    }

    void EnsureSkin()
    {
        if (_skin != null) return;
        _skin = Instantiate(GUI.skin);
        _skin.label.fontSize = 22;
        _skin.button.fontSize = 22;
        _skin.textField.fontSize = 22;
        _skin.toggle.fontSize = 20;
        _skin.box.fontSize = 22;
        _skin.label.wordWrap = true;

        _title = new GUIStyle(_skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, wordWrap = false };
        _small = new GUIStyle(_skin.label) { fontSize = 18 };
        _small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        _center = new GUIStyle(_skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        _left = new GUIStyle(_skin.button) { alignment = TextAnchor.MiddleLeft };
    }

#if UNITY_EDITOR
    [ContextMenu("능력 목록 자동 채우기")]
    void FillAbilities()
    {
        abilities.Clear();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:AbilityData"))
        {
            var a = UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) abilities.Add(a);
        }
        abilities.Sort((x, y) => x.Id.CompareTo(y.Id));
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[Room] 능력 {abilities.Count}개 채움");
    }
#endif
}
