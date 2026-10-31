using UnityEngine;
using UnityEngine.Audio;

/// <summary>소리 묶음 — 설정에서 따로 크기를 조절</summary>
public enum SoundBus
{
    Sfx = 0,      // 효과음 (기본)
    Music = 1,    // 음악
    Voice = 2,    // 보이스 채팅
    Ui = 3,       // UI 소리
}

/// <summary>
/// 소리 크기 (전체 · 효과 · 음악 · 보이스 · UI) — 이 PC에 저장 (PlayerPrefs).
/// Resources 폴더의 'GameMixer' 믹서를 씀. 없으면 전체 크기만 (AudioListener.volume).
/// 소리 내는 곳은 AudioRouter가 알아서 묶음에 연결함 (코드 수정 없이).
/// </summary>
public static class GameAudio
{
    public const string MixerName = "GameMixer";

    // 믹서 그룹 이름 · 노출 파라미터 이름 (믹서를 만들 때 이대로)
    public static readonly string[] GroupNames = { "SFX", "Music", "Voice", "UI" };
    public static readonly string[] ParamNames = { "SfxVol", "MusicVol", "VoiceVol", "UiVol" };
    public const string MasterParam = "MasterVol";

    const string P = "audio.";

    static AudioMixer _mixer;
    static bool _loaded;
    static readonly AudioMixerGroup[] _groups = new AudioMixerGroup[4];

    public static AudioMixer Mixer
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _mixer = Resources.Load<AudioMixer>(MixerName);
                if (_mixer == null)
                    Debug.LogWarning($"[Audio] Resources/{MixerName} 믹서가 없어 — 전체 크기만 조절됨 (만드는 법: 설정-오디오 md)");
                else
                    for (int i = 0; i < GroupNames.Length; i++) _groups[i] = FindGroup(GroupNames[i]);
            }
            return _mixer;
        }
    }

    static AudioMixerGroup FindGroup(string name)
    {
        foreach (var g in _mixer.FindMatchingGroups(string.Empty))       // 전부 — 대소문자 상관없이 (Sfx = SFX)
            if (string.Equals(g.name, name, System.StringComparison.OrdinalIgnoreCase)) return g;
        Debug.LogWarning($"[Audio] 믹서에 '{name}' 그룹이 없어");
        return null;
    }

    public static AudioMixerGroup Group(SoundBus bus)
    {
        var m = Mixer;
        int i = (int)bus;
        return m != null && i >= 0 && i < _groups.Length ? _groups[i] : null;
    }

    // ══════════════════ 크기 (0~1) ══════════════════

    public static float Master { get => Get("master", 1f); set => Set("master", value); }
    public static float Sfx { get => Get("sfx", 1f); set => Set("sfx", value); }
    public static float Music { get => Get("music", 0.7f); set => Set("music", value); }
    public static float Voice { get => Get("voice", 1f); set => Set("voice", value); }
    public static float Ui { get => Get("ui", 0.8f); set => Set("ui", value); }

    public static float GetBus(SoundBus b) => b switch
    {
        SoundBus.Music => Music,
        SoundBus.Voice => Voice,
        SoundBus.Ui => Ui,
        _ => Sfx,
    };

    public static void SetBus(SoundBus b, float v)
    {
        switch (b)
        {
            case SoundBus.Music: Music = v; break;
            case SoundBus.Voice: Voice = v; break;
            case SoundBus.Ui: Ui = v; break;
            default: Sfx = v; break;
        }
    }

    static float Get(string k, float def) => PlayerPrefs.GetFloat(P + k, def);

    static void Set(string k, float v)
    {
        PlayerPrefs.SetFloat(P + k, Mathf.Clamp01(v));
        Apply();
    }

    /// <summary>저장된 크기를 믹서에 넣음 (믹서는 Start 이후에만 먹음 — AudioRouter가 시작 때 부름)</summary>
    public static void Apply()
    {
        var m = Mixer;
        if (m == null)
        {
            AudioListener.volume = Master;
            return;
        }

        AudioListener.volume = 1f;
        m.SetFloat(MasterParam, ToDb(Master));
        for (int i = 0; i < ParamNames.Length; i++)
            m.SetFloat(ParamNames[i], ToDb(GetBus((SoundBus)i)));
    }

    /// <summary>0~1 → 데시벨 (0이면 -80 = 무음)</summary>
    public static float ToDb(float v) => v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;

    public static void Save() => PlayerPrefs.Save();
}