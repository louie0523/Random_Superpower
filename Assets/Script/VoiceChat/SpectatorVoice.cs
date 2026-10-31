using System.Collections.Generic;
using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;

/// <summary>
/// 관전자 보이스 — 관전자끼리만 들림 (2D, 거리 상관없음). 플레이어에겐 안 들림.
/// 관전자가 게임 중인 방에 들어오면 BasicSpawner가 만들고, 대기실로 돌아가면 지워짐 (Spectator.End).
/// 말하기 키 · 음소거 · 마이크 방식 · 장치는 경기 보이스 설정을 그대로 씀.
///
/// 프리팹 (Tools → 멀티 테스트 → 관전자 보이스 프리팹 만들기):
///   SpectatorVoice (루트) — NetworkObject · VoiceNetworkObject · SpectatorVoice
///    └ Voice — Recorder · Speaker (플레이어 프리팹 Voice 자식과 같은 설정)
/// </summary>
public class SpectatorVoice : NetworkBehaviour
{
    [Networked] public NetworkBool Talking { get; set; }

    static readonly List<SpectatorVoice> _all = new();
    public static IReadOnlyList<SpectatorVoice> All => _all;

    /// <summary>이 화면의 내 관전 보이스</summary>
    public static SpectatorVoice Local { get; private set; }

    VoiceNetworkObject _vno;
    AudioSource _out;
    string _appliedMic;

    public PlayerRef Owner => Object.InputAuthority;

    public bool IsLocal => Object != null && Object.IsValid && HasStateAuthority;

    public string DisplayName
    {
        get
        {
            var rs = RoomState.Instance;
            return rs != null ? rs.NameOf(Owner) : $"Player {Owner.PlayerId}";
        }
    }

    public bool IsTalking =>
        _vno != null && (IsLocal ? _vno.IsRecording : (_vno.IsSpeaking && _out != null && _out.volume > 0.001f));

    public override void Spawned()
    {
        _vno = GetComponent<VoiceNetworkObject>();
        _all.Add(this);
        if (HasStateAuthority) Local = this;
        if (_vno == null) Debug.LogWarning("[SpectatorVoice] VoiceNetworkObject가 없음 — 프리팹 루트에 붙이기");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _all.Remove(this);
        if (Local == this) Local = null;
    }

    void Update()
    {
        if (Object == null || !Object.IsValid) return;
        if (IsLocal) TickLocal();
        else TickRemote();
    }

    void TickLocal()
    {
        bool typing = CommsHud.ChatOpen || DevConsole.IsOpen || VoiceSettingsPanel.IsOpen;

        if (!typing && Input.GetKeyDown(VoiceSettings.MatchMuteKey))
        {
            VoiceSettings.MatchMuted = !VoiceSettings.MatchMuted;
            VoiceSettings.Save();
            ChatLog.AddSystem(VoiceSettings.MatchMuted ? "관전 마이크 음소거" : "관전 마이크 켜짐");
        }

        bool talk = VoiceSettings.Mode == MicMode.PushToTalk
            ? !typing && Input.GetKey(VoiceSettings.MatchTalkKey)
            : true;
        talk &= !VoiceSettings.MatchMuted;
        if (Talking != talk) Talking = talk;

        var rec = _vno != null ? _vno.RecorderInUse : null;
        if (rec == null) return;

        string mic = VoiceSettings.MicDevice;
        if (_appliedMic != mic)
        {
            _appliedMic = mic;
            if (string.IsNullOrEmpty(mic) || System.Array.IndexOf(UnityEngine.Microphone.devices, mic) < 0)
                rec.MicrophoneDevice = Photon.Voice.DeviceInfo.Default;
            else
            {
                rec.MicrophoneType = Recorder.MicType.Unity;
                rec.MicrophoneDevice = new Photon.Voice.DeviceInfo(mic);
            }
        }

        if (rec.TransmitEnabled != talk) rec.TransmitEnabled = talk;

        bool vad = VoiceSettings.Mode == MicMode.OpenMic;
        if (rec.VoiceDetection != vad) rec.VoiceDetection = vad;
        if (vad && !Mathf.Approximately(rec.VoiceDetectionThreshold, VoiceSettings.OpenMicThreshold))
            rec.VoiceDetectionThreshold = VoiceSettings.OpenMicThreshold;
    }

    void TickRemote()
    {
        if (_out == null)
        {
            var sp = _vno != null ? _vno.SpeakerInUse : null;
            if (sp == null) return;
            _out = sp.GetComponent<AudioSource>();
            if (_out == null) return;
        }

        bool hear = Spectator.Active && Talking;     // 관전자끼리만
        _out.spatialBlend = 0f;
        _out.volume = hear ? VoiceSettings.MatchVolume : 0f;
    }
}
