using UnityEngine;

public enum MicMode
{
    PushToTalk = 0,     // 키를 누르고 있는 동안만
    OpenMic = 1,        // 항상 (목소리가 감지될 때 보냄)
}

/// <summary>보이스 설정 — 이 컴퓨터에 저장 (PlayerPrefs). 설정 창 · VoiceChat이 읽음</summary>
public static class VoiceSettings
{
    const string P = "voice.";

    public static MicMode Mode
    {
        get => (MicMode)PlayerPrefs.GetInt(P + "mode", (int)MicMode.PushToTalk);
        set => PlayerPrefs.SetInt(P + "mode", (int)value);
    }

    /// <summary>경기 보이스 — 누르고 말하기 키</summary>
    public static KeyCode MatchTalkKey { get => Key("matchTalk", KeyCode.T); set => SetKey("matchTalk", value); }
    /// <summary>팀 보이스 — 누르고 말하기 키 (팀 모드일 때만)</summary>
    public static KeyCode TeamTalkKey { get => Key("teamTalk", KeyCode.Y); set => SetKey("teamTalk", value); }
    /// <summary>경기 마이크 음소거 켜기/끄기</summary>
    public static KeyCode MatchMuteKey { get => Key("matchMute", KeyCode.K); set => SetKey("matchMute", value); }
    /// <summary>팀 마이크 음소거 켜기/끄기</summary>
    public static KeyCode TeamMuteKey { get => Key("teamMute", KeyCode.L); set => SetKey("teamMute", value); }
    /// <summary>보이스 설정 창</summary>
    public static KeyCode SettingsKey { get => Key("settings", KeyCode.O); set => SetKey("settings", value); }

    public static bool MatchMuted
    {
        get => PlayerPrefs.GetInt(P + "matchMuted", 0) != 0;
        set => PlayerPrefs.SetInt(P + "matchMuted", value ? 1 : 0);
    }

    public static bool TeamMuted
    {
        get => PlayerPrefs.GetInt(P + "teamMuted", 0) != 0;
        set => PlayerPrefs.SetInt(P + "teamMuted", value ? 1 : 0);
    }

    /// <summary>다른 사람 목소리 크기 (0~1)</summary>
    public static float MatchVolume { get => PlayerPrefs.GetFloat(P + "matchVol", 1f); set => PlayerPrefs.SetFloat(P + "matchVol", Mathf.Clamp01(value)); }
    public static float TeamVolume { get => PlayerPrefs.GetFloat(P + "teamVol", 1f); set => PlayerPrefs.SetFloat(P + "teamVol", Mathf.Clamp01(value)); }

    /// <summary>열린 마이크 — 이 크기 이상이면 목소리로 봄 (0~1, 작을수록 예민)</summary>
    public static float OpenMicThreshold { get => PlayerPrefs.GetFloat(P + "vad", 0.01f); set => PlayerPrefs.SetFloat(P + "vad", Mathf.Clamp(value, 0.001f, 0.2f)); }

    /// <summary>쓸 마이크 이름 (Microphone.devices 중 하나). 빈 글이면 시스템 기본</summary>
    public static string MicDevice
    {
        get => PlayerPrefs.GetString(P + "mic", "");
        set => PlayerPrefs.SetString(P + "mic", value ?? "");
    }

    static KeyCode Key(string k, KeyCode def) => (KeyCode)PlayerPrefs.GetInt(P + k, (int)def);
    static void SetKey(string k, KeyCode v) => PlayerPrefs.SetInt(P + k, (int)v);

    public static void Save() => PlayerPrefs.Save();
}
