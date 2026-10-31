using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보이스 설정 창 프리팹. VoiceSettingsPanel이 열고 닫음 — 값은 바로 VoiceSettings에 저장.
/// 단축키 줄은 UiListRow: Label = 이름, Action = 키 버튼 (누르면 다음에 누른 키로 바뀜, Esc 취소).
/// </summary>
public class VoiceSettingsView : MonoBehaviour
{
    public GameObject Root;
    public TMP_Text Title;

    [Header("마이크 방식")]
    public UiOption PttOption;
    public UiOption OpenOption;
    [Tooltip("열린 마이크일 때만 보이는 묶음")]
    public GameObject VadGroup;
    public TMP_Text VadLabel;
    public Slider VadSlider;

    [Header("단축키")]
    public UiListRow MatchTalkRow;
    public UiListRow MatchMuteRow;
    public UiListRow TeamTalkRow;
    public UiListRow TeamMuteRow;
    public UiListRow SettingsRow;
    [Tooltip("팀전이 아닐 때 보이는 안내 (없어도 됨)")]
    public TMP_Text TeamNote;

    [Header("마이크 장치")]
    public RectTransform MicListContent;
    public UiOption MicOptionPrefab;

    [Header("음소거 · 크기")]
    public UiOption MatchMuteOption;
    public UiOption TeamMuteOption;
    public TMP_Text MatchVolLabel;
    public Slider MatchVolSlider;
    [Tooltip("팀전일 때만 보이는 묶음")]
    public GameObject TeamVolGroup;
    public TMP_Text TeamVolLabel;
    public Slider TeamVolSlider;

    [Header("기타")]
    public TMP_Text Diagnose;
    public Button CloseButton;

    readonly List<UiOption> _mics = new();
    readonly List<string> _micNames = new();
    string _micSig;
    float _diagAt;

    void Awake()
    {
        if (MicListContent != null)
            for (int i = MicListContent.childCount - 1; i >= 0; i--)     // 미리보기 줄 지우기
                if (MicListContent.GetChild(i).GetComponent<UiOption>() != null)
                    Destroy(MicListContent.GetChild(i).gameObject);

        Click(PttOption, () => VoiceSettings.Mode = MicMode.PushToTalk);
        Click(OpenOption, () => VoiceSettings.Mode = MicMode.OpenMic);
        Slide(VadSlider, 0.001f, 0.1f, v => VoiceSettings.OpenMicThreshold = v);

        Key(MatchTalkRow, VoiceSettingsPanel.Bind.MatchTalk);
        Key(MatchMuteRow, VoiceSettingsPanel.Bind.MatchMute);
        Key(TeamTalkRow, VoiceSettingsPanel.Bind.TeamTalk);
        Key(TeamMuteRow, VoiceSettingsPanel.Bind.TeamMute);
        Key(SettingsRow, VoiceSettingsPanel.Bind.Settings);

        Click(MatchMuteOption, () => VoiceSettings.MatchMuted = !VoiceSettings.MatchMuted);
        Click(TeamMuteOption, () => VoiceSettings.TeamMuted = !VoiceSettings.TeamMuted);
        Slide(MatchVolSlider, 0f, 1f, v => VoiceSettings.MatchVolume = v);
        Slide(TeamVolSlider, 0f, 1f, v => VoiceSettings.TeamVolume = v);

        if (CloseButton != null) CloseButton.onClick.AddListener(VoiceSettingsPanel.Close);
    }

    static void Click(UiOption o, Action a)
    {
        if (o != null && o.Button != null) o.Button.onClick.AddListener(() => a());
    }

    static void Key(UiListRow r, VoiceSettingsPanel.Bind b)
    {
        if (r != null && r.Action != null) r.Action.onClick.AddListener(() => VoiceSettingsPanel.StartBind(b));
    }

    static void Slide(Slider s, float min, float max, Action<float> set)
    {
        if (s == null) return;
        s.minValue = min;
        s.maxValue = max;
        s.onValueChanged.AddListener(v => set(v));
    }

    public void Show()
    {
        GameUiKit.EnsureEventSystem();
        GameUiKit.Active(Root, true);
        _micSig = null;
        _diagAt = 0f;
        Tick();
    }

    public void Hide() => GameUiKit.Active(Root, false);

    void Update()
    {
        if (Root == null || !Root.activeSelf) return;
        GameUiKit.DropButtonFocus();
        Tick();
    }

    void Tick()
    {
        bool ptt = VoiceSettings.Mode == MicMode.PushToTalk;
        if (PttOption != null) PttOption.SetOn(ptt);
        if (OpenOption != null) OpenOption.SetOn(!ptt);
        GameUiKit.Active(VadGroup, !ptt);
        GameUiKit.Set(VadLabel, TargetUtil.Text("voice.settings.vad", "목소리 감지 민감도 (왼쪽 = 예민)") +
                                $"  {VoiceSettings.OpenMicThreshold:0.000}");
        if (VadSlider != null) VadSlider.SetValueWithoutNotify(VoiceSettings.OpenMicThreshold);

        bool team = VoiceChat.TeamAvailable;
        KeyText(MatchTalkRow, VoiceSettingsPanel.Bind.MatchTalk, VoiceSettings.MatchTalkKey, true);
        KeyText(MatchMuteRow, VoiceSettingsPanel.Bind.MatchMute, VoiceSettings.MatchMuteKey, true);
        KeyText(TeamTalkRow, VoiceSettingsPanel.Bind.TeamTalk, VoiceSettings.TeamTalkKey, team);
        KeyText(TeamMuteRow, VoiceSettingsPanel.Bind.TeamMute, VoiceSettings.TeamMuteKey, team);
        KeyText(SettingsRow, VoiceSettingsPanel.Bind.Settings, VoiceSettings.SettingsKey, true);
        if (TeamNote != null) GameUiKit.Active(TeamNote, !team);

        Mics();

        if (MatchMuteOption != null) MatchMuteOption.SetOn(VoiceSettings.MatchMuted);
        if (TeamMuteOption != null)
        {
            GameUiKit.Active(TeamMuteOption, team);
            TeamMuteOption.SetOn(VoiceSettings.TeamMuted);
        }

        GameUiKit.Set(MatchVolLabel, TargetUtil.Text("voice.vol.match", "경기 보이스 크기") + $"  {VoiceSettings.MatchVolume * 100f:0}%");
        if (MatchVolSlider != null) MatchVolSlider.SetValueWithoutNotify(VoiceSettings.MatchVolume);
        GameUiKit.Active(TeamVolGroup, team);
        GameUiKit.Set(TeamVolLabel, TargetUtil.Text("voice.vol.team", "팀 보이스 크기") + $"  {VoiceSettings.TeamVolume * 100f:0}%");
        if (TeamVolSlider != null) TeamVolSlider.SetValueWithoutNotify(VoiceSettings.TeamVolume);

        if (Diagnose != null && Time.unscaledTime >= _diagAt)
        {
            _diagAt = Time.unscaledTime + 0.25f;
            var vc = VoiceChat.Local;
            GameUiKit.Set(Diagnose, vc != null ? vc.Diagnose()
                : SpectatorVoice.Local != null ? "관전자 보이스 — 관전자끼리만 들림" : "");
        }
    }

    static void KeyText(UiListRow r, VoiceSettingsPanel.Bind b, KeyCode key, bool enabled)
    {
        if (r == null) return;
        GameUiKit.Set(r.ActionLabel, VoiceSettingsPanel.Waiting == b
            ? TargetUtil.Text("voice.key.wait", "키를 누르세요…")
            : key.ToString());
        GameUiKit.Interact(r.Action, enabled);
    }

    void Mics()
    {
        if (MicListContent == null || MicOptionPrefab == null) return;

        var devices = UnityEngine.Microphone.devices;
        string sig = string.Join("|", devices);
        if (sig != _micSig)
        {
            _micSig = sig;
            _micNames.Clear();
            _micNames.Add("");
            _micNames.AddRange(devices);

            while (_mics.Count < _micNames.Count)
            {
                var o = Instantiate(MicOptionPrefab, MicListContent);
                int idx = _mics.Count;
                if (o.Button != null) o.Button.onClick.AddListener(() =>
                {
                    if (idx < _micNames.Count) VoiceSettings.MicDevice = _micNames[idx];
                });
                _mics.Add(o);
            }

            for (int i = 0; i < _mics.Count; i++)
            {
                bool used = i < _micNames.Count;
                GameUiKit.Active(_mics[i], used);
                if (used) _mics[i].SetText(i == 0 ? TargetUtil.Text("voice.mic.default", "시스템 기본") : _micNames[i]);
            }
        }

        string cur = VoiceSettings.MicDevice;
        bool curExists = !string.IsNullOrEmpty(cur) && _micNames.Contains(cur);
        for (int i = 0; i < _micNames.Count && i < _mics.Count; i++)
            _mics[i].SetOn(i == 0 ? !curExists : _micNames[i] == cur);
    }
}
