using System.Collections.Generic;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;

/// <summary>
/// 플레이어 하나의 보이스. 플레이어 프리팹 루트에 VoiceNetworkObject · PlayerComms와 같이 붙임.
///
/// 내 캐릭터(권한자):
///   눌러서 말하기 — 경기 키를 누르는 동안 경기 보이스, 팀 키를 누르는 동안 팀 보이스 (팀 모드일 때만)
///   열린 마이크   — 음소거 안 한 쪽으로 항상 (목소리가 감지될 때만 보냄)
///   경기 · 팀 음소거 키로 각각 끄고 켬. 저주 중엔 송신 안 됨. 채팅 · 콘솔 입력 중엔 키 무시
///   지금 어느 쪽으로 말하는지는 PlayerComms.VoiceFlags로 모두에게 알림
///
/// 다른 사람(듣는 쪽 — 각 화면에서):
///   같은 팀이고 상대가 팀 보이스 중 → 거리 상관없이 2D로 들림
///   상대가 경기 보이스 중 → 3D, MatchRange(15m)까지 들리고 멀수록 작아짐
///   그 외 → 안 들림
/// 소리 자체는 모두에게 가고 듣는 쪽에서 걸러짐 (팀 모드가 생기면 서버에서 거르는 방식으로 바꿀 수 있음)
/// </summary>
[DisallowMultipleComponent]
public class VoiceChat : MonoBehaviour
{
    public const byte FlagMatch = 1;
    public const byte FlagTeam = 2;

    [Tooltip("경기 보이스가 들리는 거리 (m)")]
    public float MatchRange = 15f;
    [Tooltip("이 거리까지는 최대 크기 (m)")]
    public float FullVolumeRange = 2f;

    static readonly List<VoiceChat> _all = new();
    public static IReadOnlyList<VoiceChat> All => _all;

    /// <summary>이 화면의 내 보이스</summary>
    public static VoiceChat Local { get; private set; }

    private PlayerComms _comms;
    private VoiceNetworkObject _vno;
    private PlayerStatus _status;
    private AudioSource _out;
    private string _appliedMic;      // 지금 Recorder에 넣은 마이크 (null = 아직)

    public bool IsLocal => _comms != null && _comms.Object != null && _comms.Object.IsValid && _comms.HasStateAuthority;
    public PlayerComms Comms => _comms;

    /// <summary>문제 찾기용 — 보이스 연결 · 녹음 상태 한 줄씩</summary>
    public string Diagnose()
    {
        var sb = new System.Text.StringBuilder();
        var runner = _comms != null ? _comms.Runner : null;
        var conn = runner != null ? runner.GetComponent<Photon.Voice.Unity.VoiceConnection>() : null;

        string appId = Fusion.Photon.Realtime.PhotonAppSettings.Global != null
            ? Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdVoice : null;
        sb.Append("App Id Voice: ").Append(string.IsNullOrEmpty(appId)
            ? "<color=#ff6b6b>비어 있음 — Realtime Settings에 넣기</color>"
            : appId.Substring(0, Mathf.Min(8, appId.Length)) + "… (대시보드의 Voice 앱 ID와 같은지)").Append('\n');

        sb.Append("보이스 클라이언트: ");
        if (conn == null) sb.Append("<color=#ff6b6b>없음 — BasicSpawner에 FusionVoiceClient 추가했는지</color>");
        else sb.Append(conn.Client != null ? conn.Client.State.ToString() : "?").Append(" (Joined여야 정상)");
        sb.Append('\n');

        sb.Append("VoiceNetworkObject: ").Append(_vno != null ? "있음" : "<color=#ff6b6b>없음 — 프리팹 루트에</color>").Append('\n');

        var rec = _vno != null ? _vno.RecorderInUse : null;
        if (rec == null) sb.Append("<color=#ff6b6b>Recorder 없음 — 자식 Voice 오브젝트에 Recorder가 있는지 / 콘솔 에러 확인</color>\n");
        else
        {
            sb.Append($"Recorder: 녹음 {rec.RecordingEnabled} · 송신 허용 {rec.TransmitEnabled} · 목소리 감지 {rec.VoiceDetection} · 실제 송신 {rec.IsCurrentlyTransmitting}\n");
            var lm = rec.LevelMeter;
            sb.Append(lm != null ? $"마이크 입력 크기: {lm.CurrentPeakAmp:0.000} (감지 기준 {VoiceSettings.OpenMicThreshold:0.000})" : "<color=#ff6b6b>마이크 입력 없음 — 녹음이 안 시작됨 (보이스 서버 입장 전이거나 마이크 열기 실패)</color>");
            sb.Append('\n');
        }

        sb.Append($"마이크 장치 {UnityEngine.Microphone.devices.Length}개 · 보내는 쪽 {Flags}");
        return sb.ToString();
    }

    /// <summary>지금 말하는 중 (소리가 실제로 나가는 중 / 들리는 중)</summary>
    public bool IsTalking =>
        _vno != null && (IsLocal ? _vno.IsRecording : (_vno.IsSpeaking && _out != null && _out.volume > 0.001f));

    /// <summary>지금 어느 쪽으로 말하는지 (FlagMatch | FlagTeam)</summary>
    public byte Flags => _comms != null && _comms.Object != null && _comms.Object.IsValid ? _comms.VoiceFlags : (byte)0;

    public static bool TeamAvailable => TeamUtil.AlliesProvider != null;

    void Awake()
    {
        _comms = GetComponent<PlayerComms>();
        _vno = GetComponent<VoiceNetworkObject>();
        _status = GetComponent<PlayerStatus>();
    }

    void OnEnable() => _all.Add(this);

    void OnDisable()
    {
        _all.Remove(this);
        if (Local == this) Local = null;
    }

    void Update()
    {
        if (_comms == null || _comms.Object == null || !_comms.Object.IsValid) return;

        if (IsLocal)
        {
            Local = this;
            TickLocal();
        }
        else TickRemote();
    }

    // ══════════════════ 내 마이크 ══════════════════

    static bool TypingSomewhere => CommsHud.ChatOpen || DevConsole.IsOpen || VoiceSettingsPanel.IsOpen;

    void TickLocal()
    {
        var rec = _vno != null ? _vno.RecorderInUse : null;
        bool typing = TypingSomewhere;

        // 음소거 키 (켜기/끄기)
        if (!typing)
        {
            if (Input.GetKeyDown(VoiceSettings.MatchMuteKey))
            {
                VoiceSettings.MatchMuted = !VoiceSettings.MatchMuted;
                VoiceSettings.Save();
                ChatLog.AddSystem(VoiceSettings.MatchMuted
                    ? TargetUtil.Text("voice.match.muted", "경기 마이크 음소거")
                    : TargetUtil.Text("voice.match.unmuted", "경기 마이크 켜짐"));
            }
            if (TeamAvailable && Input.GetKeyDown(VoiceSettings.TeamMuteKey))
            {
                VoiceSettings.TeamMuted = !VoiceSettings.TeamMuted;
                VoiceSettings.Save();
                ChatLog.AddSystem(VoiceSettings.TeamMuted
                    ? TargetUtil.Text("voice.team.muted", "팀 마이크 음소거")
                    : TargetUtil.Text("voice.team.unmuted", "팀 마이크 켜짐"));
            }
        }

        // 어느 쪽으로 말하는가
        bool match, team;
        if (VoiceSettings.Mode == MicMode.PushToTalk)
        {
            match = !typing && Input.GetKey(VoiceSettings.MatchTalkKey);
            team = !typing && TeamAvailable && Input.GetKey(VoiceSettings.TeamTalkKey);
        }
        else
        {
            match = true;
            team = TeamAvailable;
        }
        match &= !VoiceSettings.MatchMuted;
        team &= !VoiceSettings.TeamMuted;

        bool cursed = _status != null && _status.Has(StatusType.Curse);
        if (cursed) match = team = false;

        byte flags = (byte)((match ? FlagMatch : 0) | (team ? FlagTeam : 0));
        if (_comms.VoiceFlags != flags) _comms.VoiceFlags = flags;

        if (rec == null) return;

        // 마이크 장치 — 바뀌었을 때만 (바꾸면 녹음이 다시 시작됨)
        string mic = VoiceSettings.MicDevice;
        if (_appliedMic != mic)
        {
            _appliedMic = mic;
            if (string.IsNullOrEmpty(mic) || System.Array.IndexOf(UnityEngine.Microphone.devices, mic) < 0)
                rec.MicrophoneDevice = Photon.Voice.DeviceInfo.Default;
            else
            {
                rec.MicrophoneType = Recorder.MicType.Unity;      // 이름으로 고르는 건 Unity 마이크 방식
                rec.MicrophoneDevice = new Photon.Voice.DeviceInfo(mic);
            }
        }

        bool send = flags != 0;
        if (rec.TransmitEnabled != send) rec.TransmitEnabled = send;

        bool vad = VoiceSettings.Mode == MicMode.OpenMic;
        if (rec.VoiceDetection != vad) rec.VoiceDetection = vad;
        if (vad && !Mathf.Approximately(rec.VoiceDetectionThreshold, VoiceSettings.OpenMicThreshold))
            rec.VoiceDetectionThreshold = VoiceSettings.OpenMicThreshold;
    }

    // ══════════════════ 다른 사람 목소리 ══════════════════

    void TickRemote()
    {
        if (_out == null)
        {
            var sp = _vno != null ? _vno.SpeakerInUse : null;
            if (sp == null) return;
            _out = sp.GetComponent<AudioSource>();
            if (_out == null) return;
        }

        byte f = _comms.VoiceFlags;
        var runner = _comms.Runner;
        bool ally = TeamAvailable && runner != null && TeamUtil.AreAllies(_comms.Object.InputAuthority, runner.LocalPlayer);

        if (ally && (f & FlagTeam) != 0)
        {
            _out.spatialBlend = 0f;
            _out.volume = VoiceSettings.TeamVolume;
        }
        else if ((f & FlagMatch) != 0)
        {
            _out.spatialBlend = 1f;
            _out.rolloffMode = AudioRolloffMode.Linear;
            _out.minDistance = FullVolumeRange;
            _out.maxDistance = MatchRange;
            _out.dopplerLevel = 0f;
            _out.volume = VoiceSettings.MatchVolume;
        }
        else _out.volume = 0f;
    }
}
