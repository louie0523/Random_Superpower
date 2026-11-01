using UnityEngine;

/// <summary>
/// 보이스 설정 창 (예전 단독 창 — 이제 키로 안 열림. 보이스 설정은 설정 > 채팅 · 보이스 탭).
/// 화면 왼쪽 아래 마이크 상태 · 말하는 사람 표시는 VoiceStatusHud로 옮김.
/// 씬에 없으면 알아서 생김. 나중에 게임 설정 메뉴가 생기면 VoiceSettings 값을 거기서 바꾸면 됨.
/// 창 모양: Resources/UiPrefabSet의 '보이스 설정 창'을 켜면 프리팹(VoiceSettingsView), 아니면 아래 코드 모양.
/// 관전자도 열 수 있음 (SpectatorVoice).
/// </summary>
public class VoiceSettingsPanel : MonoBehaviour
{
    [Range(0.6f, 2f)][SerializeField] private float uiScale = 1f;

    static VoiceSettingsPanel _inst;
    public static bool IsOpen { get; private set; }

    public enum Bind { None, MatchTalk, TeamTalk, MatchMute, TeamMute, Settings }

    /// <summary>키 바꾸기 대기 중인 칸</summary>
    public static Bind Waiting => _inst != null ? _inst._waiting : Bind.None;

    Bind _waiting;
    Vector2 _micScroll;
    Vector2 _pageScroll;
    GUIStyle _label, _small, _btn, _title;
    float _scale = -1f;
    VoiceSettingsView _view;
    bool _viewChecked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindFirstObjectByType<VoiceSettingsPanel>() != null) return;
        var go = new GameObject("VoiceSettingsPanel");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceSettingsPanel>();
    }

    void Awake()
    {
        if (_inst != null && _inst != this) { Destroy(this); return; }
        _inst = this;
    }

    void OnDestroy()
    {
        if (_inst == this) { _inst = null; IsOpen = false; }
    }

    /// <summary>프리팹 창 (안 켰으면 null)</summary>
    VoiceSettingsView View()
    {
        if (!_viewChecked)
        {
            _viewChecked = true;
            var prefab = UiPrefabSet.VoicePrefab;
            if (prefab != null)
            {
                _view = Instantiate(prefab, transform);
                _view.Hide();
            }
        }
        return _view;
    }

    static bool HasVoice => VoiceChat.Local != null || SpectatorVoice.Local != null;

    public static void Open()
    {
        IsOpen = true;
        if (_inst != null)
        {
            var v = _inst.View();
            if (v != null) v.Show();
        }
    }

    public static void Close()
    {
        IsOpen = false;
        if (_inst != null)
        {
            _inst._waiting = Bind.None;
            if (_inst._view != null) _inst._view.Hide();
        }
        VoiceSettings.Save();
        var ui = UIManager.Instance;
        if ((ui == null || !ui.AnyPanelOpen) && !Spectator.CursorFree)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>이 칸의 키 바꾸기 시작 — 다음에 누른 키로 (Esc 취소)</summary>
    public static void StartBind(Bind b)
    {
        if (_inst != null) _inst._waiting = b;
    }

    void Update()
    {
        if (IsOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // O 키로 여는 건 없앰 — 보이스 설정은 설정(Esc) > 채팅 · 보이스 탭에서만
    }

    void Styles()
    {
        float s = Screen.height / 1080f * uiScale;
        if (Mathf.Approximately(s, _scale) && _label != null) return;
        _scale = s;
        _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * s), richText = true };
        _label.normal.textColor = Color.white;
        _small = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(17 * s) };
        _btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(19 * s) };
        _title = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    void OnGUI()
    {
        if (!IsOpen) return;
        Styles();
        if (HandleKeys()) return;
        if (_view == null) DrawPanel();
    }

    // ── 키 바꾸기 · Esc (코드 · 프리팹 공통) ── true = 창을 닫았음
    bool HandleKeys()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return false;

        if (_waiting != Bind.None)
        {
            if (e.keyCode != KeyCode.Escape) SetBind(_waiting, e.keyCode);
            _waiting = Bind.None;
            e.Use();
            return false;
        }
        if (e.keyCode == KeyCode.Escape)
        {
            e.Use();
            Close();
            return true;
        }
        return false;
    }

    // ── 설정 창 (코드 모양) ──

    void DrawPanel()
    {
        float s = _scale;

        float w = 640f * s, h = Mathf.Min(900f * s, Screen.height - 40f);
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(rect.x + 16f * s, rect.y + 12f * s, rect.width - 32f * s, rect.height - 24f * s));
        _pageScroll = GUILayout.BeginScrollView(_pageScroll);          // 화면이 작으면 휠로 내림
        GUILayout.Label(TargetUtil.Text("voice.settings.title", "보이스 설정"), _title);
        GUILayout.Space(8f * s);

        // 방식
        GUILayout.Label(TargetUtil.Text("voice.settings.mode", "마이크 방식"), _label);
        GUILayout.BeginHorizontal();
        if (Toggle(VoiceSettings.Mode == MicMode.PushToTalk, TargetUtil.Text("voice.mode.ptt.short", "누르고 말하기"))) VoiceSettings.Mode = MicMode.PushToTalk;
        if (Toggle(VoiceSettings.Mode == MicMode.OpenMic, TargetUtil.Text("voice.mode.open", "열린 마이크"))) VoiceSettings.Mode = MicMode.OpenMic;
        GUILayout.EndHorizontal();

        if (VoiceSettings.Mode == MicMode.OpenMic)
        {
            GUILayout.Label(TargetUtil.Text("voice.settings.vad", "목소리 감지 민감도 (왼쪽 = 예민)") +
                            $"  {VoiceSettings.OpenMicThreshold:0.000}", _small);
            VoiceSettings.OpenMicThreshold = GUILayout.HorizontalSlider(VoiceSettings.OpenMicThreshold, 0.001f, 0.1f);
        }
        GUILayout.Space(10f * s);

        // 키
        GUILayout.Label(TargetUtil.Text("voice.settings.keys", "단축키 (누르고 다른 키 입력 · Esc 취소)"), _label);
        KeyRow(TargetUtil.Text("voice.key.matchTalk", "경기 — 누르고 말하기"), Bind.MatchTalk, VoiceSettings.MatchTalkKey);
        KeyRow(TargetUtil.Text("voice.key.matchMute", "경기 마이크 음소거"), Bind.MatchMute, VoiceSettings.MatchMuteKey);
        GUI.enabled = VoiceChat.TeamAvailable;
        KeyRow(TargetUtil.Text("voice.key.teamTalk", "팀 — 누르고 말하기"), Bind.TeamTalk, VoiceSettings.TeamTalkKey);
        KeyRow(TargetUtil.Text("voice.key.teamMute", "팀 마이크 음소거"), Bind.TeamMute, VoiceSettings.TeamMuteKey);
        GUI.enabled = true;
        KeyRow(TargetUtil.Text("voice.key.settings", "이 설정 창"), Bind.Settings, VoiceSettings.SettingsKey);
        if (!VoiceChat.TeamAvailable)
            GUILayout.Label("<color=#999999>" + TargetUtil.Text("voice.team.off", "팀 보이스는 팀전에서만") + "</color>", _small);
        GUILayout.Space(10f * s);

        // 마이크 장치
        GUILayout.Label(TargetUtil.Text("voice.settings.mic", "마이크"), _label);
        string curMic = VoiceSettings.MicDevice;
        _micScroll = GUILayout.BeginScrollView(_micScroll, GUILayout.Height(96f * s));
        if (Toggle(string.IsNullOrEmpty(curMic), TargetUtil.Text("voice.mic.default", "시스템 기본"))) VoiceSettings.MicDevice = "";
        foreach (var d in UnityEngine.Microphone.devices)
            if (Toggle(curMic == d, d)) VoiceSettings.MicDevice = d;
        GUILayout.EndScrollView();
        GUILayout.Space(10f * s);

        // 음소거 · 크기
        GUILayout.BeginHorizontal();
        VoiceSettings.MatchMuted = GUILayout.Toggle(VoiceSettings.MatchMuted, " " + TargetUtil.Text("voice.match.mute", "경기 마이크 음소거"), _label);
        if (VoiceChat.TeamAvailable)
            VoiceSettings.TeamMuted = GUILayout.Toggle(VoiceSettings.TeamMuted, " " + TargetUtil.Text("voice.team.mute", "팀 마이크 음소거"), _label);
        GUILayout.EndHorizontal();

        GUILayout.Label(TargetUtil.Text("voice.vol.match", "경기 보이스 크기") + $"  {VoiceSettings.MatchVolume * 100f:0}%", _small);
        VoiceSettings.MatchVolume = GUILayout.HorizontalSlider(VoiceSettings.MatchVolume, 0f, 1f);
        GUILayout.Label(TargetUtil.Text("voice.vol.team", "팀 보이스 크기") + $"  {VoiceSettings.TeamVolume * 100f:0}%", _small);
        VoiceSettings.TeamVolume = GUILayout.HorizontalSlider(VoiceSettings.TeamVolume, 0f, 1f);

        // (진단 글은 없앰 — 필요하면 VoiceChat.Local.Diagnose()를 콘솔로)

        GUILayout.EndScrollView();
        GUILayout.Space(6f * s);
        if (GUILayout.Button(TargetUtil.Text("voice.settings.close", "닫기 (Esc)"), _btn, GUILayout.Height(40f * s))) Close();
        GUILayout.EndArea();
    }

    bool Toggle(bool on, string label) =>
        GUILayout.Toggle(on, " " + label, _btn, GUILayout.Height(36f * _scale)) && !on;

    void KeyRow(string label, Bind b, KeyCode cur)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _small, GUILayout.Width(300f * _scale));
        string txt = _waiting == b ? TargetUtil.Text("voice.key.wait", "키를 누르세요…") : cur.ToString();
        if (GUILayout.Button(txt, _btn, GUILayout.Height(32f * _scale))) _waiting = b;
        GUILayout.EndHorizontal();
    }

    static void SetBind(Bind b, KeyCode k)
    {
        switch (b)
        {
            case Bind.MatchTalk: VoiceSettings.MatchTalkKey = k; break;
            case Bind.TeamTalk: VoiceSettings.TeamTalkKey = k; break;
            case Bind.MatchMute: VoiceSettings.MatchMuteKey = k; break;
            case Bind.TeamMute: VoiceSettings.TeamMuteKey = k; break;
            case Bind.Settings: VoiceSettings.SettingsKey = k; break;
        }
    }
}
