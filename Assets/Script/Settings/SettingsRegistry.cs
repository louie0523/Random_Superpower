using System;
using System.Collections.Generic;
using UnityEngine;

public enum SettingKind
{
    Toggle,     // 켜기 / 끄기
    Slider,     // 숫자 (밀기)
    Choice,     // 고르기 (< 값 >)
    Key,        // 키 바꾸기
    Action,     // 버튼 (기본값으로 등)
}

/// <summary>설정 한 줄의 정의 — 무엇을 읽고 쓰는지. 화면(SettingRow)은 Key로 이걸 찾아 붙음</summary>
public class SettingDef
{
    public string Key;
    public string Tab;
    public string Label;
    public string Note;              // 아래 작은 설명 (없어도 됨)
    public SettingKind Kind;

    /// <summary>아직 게임에 연결 안 됨 — '(준비 중)'으로 보임</summary>
    public bool Pending;

    /// <summary>보일 조건 (null = 항상)</summary>
    public Func<bool> Visible;

    // Toggle
    public Func<bool> GetB;
    public Action<bool> SetB;

    // Slider
    public Func<float> GetF;
    public Action<float> SetF;
    public float Min, Max = 1f;
    public bool Whole;               // 정수만
    public Func<float, string> Format;

    // Choice
    public Func<string[]> Options;
    public Func<int> GetI;
    public Action<int> SetI;

    // Key
    public Func<KeyCode> GetK;
    public Action<KeyCode> SetK;

    // Action (버튼)
    public System.Action Run;
    public string ButtonText;
    /// <summary>버튼 글자가 바뀌는 경우 (예: 한 번 더 누르면 초기화) — 있으면 ButtonText 대신</summary>
    public Func<string> ButtonTextF;
    public string ButtonLabel() => ButtonTextF != null ? ButtonTextF() : SetLoc.T($"set.{Key}.btn", ButtonText ?? "");

    // ── 번역 (LangTable에 키가 없으면 위의 한국어 그대로) ──
    /// <summary>고르기 칸 글자를 set.{키}.opt{번호}로 번역할지 — 해상도 · 마이크처럼 PC마다 다른 목록은 false</summary>
    public bool LocOptions = true;
    public string LabelText => SetLoc.T($"set.{Key}", Label);
    public string NoteText => string.IsNullOrEmpty(Note) ? null : SetLoc.T($"set.{Key}.note", Note);
    public string OptionText(string[] o, int i) => LocOptions ? SetLoc.T($"set.{Key}.opt{i}", o[i]) : o[i];

    public string ValueText()
    {
        switch (Kind)
        {
            case SettingKind.Toggle: return GetB() ? SetLoc.On : SetLoc.Off;
            case SettingKind.Slider:
                float v = GetF();
                return Format != null ? Format(v) : (Whole ? $"{Mathf.RoundToInt(v)}" : $"{v:0.00}");
            case SettingKind.Choice:
                var o = Options();
                int i = GetI();
                return o != null && i >= 0 && i < o.Length ? OptionText(o, i) : SetLoc.T("set.ui.custom", "(직접)");
            case SettingKind.Key: return KeyBinds.Name(GetK());
            case SettingKind.Action: return ButtonLabel();
        }
        return "";
    }
}

/// <summary>
/// 모든 설정 줄 목록 + 탭. 새 설정을 넣으려면 여기에 한 줄 추가 → 기본 모양에 저절로 나옴
/// (프리팹을 쓰면 프리팹에 SettingRow를 하나 더 두고 Key를 맞추면 됨).
/// </summary>
public static class SettingsRegistry
{
    public static readonly (string Id, string Label)[] Tabs =
    {
        ("general", "일반"),
        ("graphics", "그래픽"),
        ("audio", "소리"),
        ("controls", "조작"),
        ("crosshair", "조준점"),
        ("voice", "채팅 · 보이스"),
        ("hud", "HUD · 접근성"),
    };

    static List<SettingDef> _all;
    static Dictionary<string, SettingDef> _map;

    public static List<SettingDef> All { get { Build(); return _all; } }

    public static SettingDef Get(string key)
    {
        Build();
        return key != null && _map.TryGetValue(key, out var d) ? d : null;
    }

    static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

    static void Build()
    {
        if (_all != null) return;
        _all = new List<SettingDef>();

        // ══════════ 일반 ══════════
        Choice("lang", "general", "언어", () => LanguageOptions.Names, () => LanguageOptions.Index, i => LanguageOptions.Index = i)
            .LocOptions = false;     // 언어 이름은 그 언어 글자 그대로
        Toggle("game.skillRelease", "general", "스킬 — 뗄 때 쓰기", () => GameSettings.SkillOnRelease, b => GameSettings.SkillOnRelease = b,
               "끄면 누르는 순간 씀");
        Toggle("game.dashLook", "general", "대시 — 보는 방향으로", () => GameSettings.DashToLook, b => GameSettings.DashToLook = b,
               "끄면 움직이는 방향으로 (이동 방향 대시만 해당)");
        Toggle("game.autoPickup", "general", "무기 저절로 줍기", () => GameSettings.AutoPickup, b => GameSettings.AutoPickup = b,
               "끄면 상호작용 키로 주움 (소울 · 스톤은 항상 저절로)");

        // ══════════ 그래픽 ══════════
        Choice("gfx.window", "graphics", "화면 모드", () => GraphicsOptions.WindowNames, () => GraphicsOptions.Window, i => GraphicsOptions.Window = i,
               "에디터에선 적용 안 됨 (빌드에서만)");
        Choice("gfx.resolution", "graphics", "해상도", () => GraphicsOptions.ResolutionNames, () => GraphicsOptions.ResolutionIndex, i => GraphicsOptions.ResolutionIndex = i,
               "에디터에선 적용 안 됨 (빌드에서만)").LocOptions = false;
        Toggle("gfx.vsync", "graphics", "수직 동기", () => GraphicsOptions.VSync, b => GraphicsOptions.VSync = b, "켜면 FPS 제한은 무시됨");
        var fps = Choice("gfx.fps", "graphics", "FPS 제한", () => GraphicsOptions.FpsNames, () => GraphicsOptions.FpsIndex, i => GraphicsOptions.FpsIndex = i);
        fps.Visible = () => !GraphicsOptions.VSync;
        fps.LocOptions = false;      // 숫자 — '제한 없음'만 GraphicsOptions에서 번역
        Choice("gfx.quality", "graphics", "품질", () => QualitySettings.names, () => GraphicsOptions.Quality, i => GraphicsOptions.Quality = i)
            .LocOptions = false;     // 품질 이름은 Project Settings > Quality 이름 그대로
        Slider("gfx.scale", "graphics", "렌더 스케일", () => GraphicsOptions.RenderScale, v => GraphicsOptions.RenderScale = v, 0.5f, 1f, Pct,
               note: "낮추면 흐려지고 빨라짐");
        Choice("gfx.shadows", "graphics", "그림자", () => GraphicsOptions.ShadowNames, () => GraphicsOptions.Shadows, i => GraphicsOptions.Shadows = i);
        Slider("gfx.fov", "graphics", "시야각 (FOV)", () => GameSettings.Fov, v => GameSettings.Fov = v, 60f, 110f, v => $"{Mathf.RoundToInt(v)}°",
               note: "세로 기준 — 기본 75", whole: true);

        // ══════════ 소리 ══════════
        Slider("audio.master", "audio", "전체", () => GameAudio.Master, v => GameAudio.Master = v, 0f, 1f, Pct);
        Slider("audio.sfx", "audio", "효과음", () => GameAudio.Sfx, v => GameAudio.Sfx = v, 0f, 1f, Pct);
        Slider("audio.music", "audio", "음악", () => GameAudio.Music, v => GameAudio.Music = v, 0f, 1f, Pct);
        Slider("audio.voice", "audio", "보이스", () => GameAudio.Voice, v => GameAudio.Voice = v, 0f, 1f, Pct);
        Slider("audio.ui", "audio", "UI 소리", () => GameAudio.Ui, v => GameAudio.Ui = v, 0f, 1f, Pct);

        // ══════════ 조작 ══════════
        Slider("ctl.sens", "controls", "마우스 감도", () => GameSettings.Sensitivity, v => GameSettings.Sensitivity = v, 0.1f, 5f, v => $"{v:0.00}");
        Toggle("ctl.invertY", "controls", "마우스 위아래 반전", () => GameSettings.InvertY, b => GameSettings.InvertY = b);
        // 되돌리기는 키 목록 위에 (목록이 길어서). 실수 방지로 두 번 눌러야 됨
        Confirm("key.reset", "controls", "키 전부 기본값으로", "기본값으로",
                () => { KeyBinds.ResetAll(); ResetVoiceKeys(); },
                "보이스 키(채팅 · 보이스 탭)도 같이 돌아감 · 같은 키를 넣으면 두 동작의 키가 서로 바뀜");
        foreach (var (action, label) in KeyBinds.Labels)
        {
            var a = action;
            Key("key." + a, "controls", label, () => KeyBinds.Get(a), k => KeyBinds.Set(a, k));
        }

        // ══════════ 조준점 (편집 중인 무기 종류) ══════════
        XToggle("xh.on", "조준점 보이기", () => CrosshairSettings.Enabled, b => CrosshairSettings.Enabled = b, global: true);
        XToggle("xh.share", "모든 무기에 같은 조준점", () => CrosshairSettings.ShareAll, b => CrosshairSettings.ShareAll = b, global: true);
        Choice("xh.color", "crosshair", "색", () => CrosshairStyle.ColorNames,
               () => CrosshairSettings.Edit.ColorIndex,
               i => { CrosshairSettings.Edit.Color = CrosshairStyle.Colors[i]; CrosshairSettings.EditChanged(); });
        XToggle("xh.outline", "테두리", () => X.Outline, b => X.Outline = b);
        XSlider("xh.outlineOpacity", "테두리 진하기", () => X.OutlineOpacity, v => X.OutlineOpacity = v, 0f, 1f, false).Visible = () => X.Outline;
        XSlider("xh.outlineThick", "테두리 두께", () => X.OutlineThickness, v => X.OutlineThickness = (int)v, 1, 3, true).Visible = () => X.Outline;
        XToggle("xh.dot", "가운데 점", () => X.Dot, b => X.Dot = b);
        XSlider("xh.dotSize", "점 크기", () => X.DotSize, v => X.DotSize = (int)v, 1, 8, true).Visible = () => X.Dot;
        XSlider("xh.dotOpacity", "점 진하기", () => X.DotOpacity, v => X.DotOpacity = v, 0f, 1f, false).Visible = () => X.Dot;

        XToggle("xh.inner", "안쪽 선", () => X.Inner, b => X.Inner = b);
        XSlider("xh.innerLen", "안쪽 선 길이", () => X.InnerLength, v => X.InnerLength = (int)v, 0, 20, true).Visible = () => X.Inner;
        XSlider("xh.innerThick", "안쪽 선 두께", () => X.InnerThickness, v => X.InnerThickness = (int)v, 1, 10, true).Visible = () => X.Inner;
        XSlider("xh.innerOffset", "안쪽 선 간격", () => X.InnerOffset, v => X.InnerOffset = (int)v, 0, 20, true).Visible = () => X.Inner;
        XSlider("xh.innerOpacity", "안쪽 선 진하기", () => X.InnerOpacity, v => X.InnerOpacity = v, 0f, 1f, false).Visible = () => X.Inner;
        XToggle("xh.innerMove", "안쪽 선 — 움직이면 벌어짐", () => X.InnerMove, b => X.InnerMove = b).Visible = () => X.Inner;
        XToggle("xh.innerFire", "안쪽 선 — 쏘면 벌어짐", () => X.InnerFire, b => X.InnerFire = b).Visible = () => X.Inner;

        XToggle("xh.outer", "바깥 선", () => X.Outer, b => X.Outer = b);
        XSlider("xh.outerLen", "바깥 선 길이", () => X.OuterLength, v => X.OuterLength = (int)v, 0, 20, true).Visible = () => X.Outer;
        XSlider("xh.outerThick", "바깥 선 두께", () => X.OuterThickness, v => X.OuterThickness = (int)v, 1, 10, true).Visible = () => X.Outer;
        XSlider("xh.outerOffset", "바깥 선 간격", () => X.OuterOffset, v => X.OuterOffset = (int)v, 0, 40, true).Visible = () => X.Outer;
        XSlider("xh.outerOpacity", "바깥 선 진하기", () => X.OuterOpacity, v => X.OuterOpacity = v, 0f, 1f, false).Visible = () => X.Outer;
        XToggle("xh.outerMove", "바깥 선 — 움직이면 벌어짐", () => X.OuterMove, b => X.OuterMove = b).Visible = () => X.Outer;
        XToggle("xh.outerFire", "바깥 선 — 쏘면 벌어짐", () => X.OuterFire, b => X.OuterFire = b).Visible = () => X.Outer;

        XToggle("xh.circle", "원", () => X.Circle, b => X.Circle = b);
        XSlider("xh.circleRadius", "원 크기", () => X.CircleRadius, v => X.CircleRadius = (int)v, 3, 60, true).Visible = () => X.Circle;
        XSlider("xh.circleThick", "원 두께", () => X.CircleThickness, v => X.CircleThickness = (int)v, 1, 8, true).Visible = () => X.Circle;
        XSlider("xh.circleOpacity", "원 진하기", () => X.CircleOpacity, v => X.CircleOpacity = v, 0f, 1f, false).Visible = () => X.Circle;
        XToggle("xh.circleMove", "원 — 움직이면 벌어짐", () => X.CircleMove, b => X.CircleMove = b).Visible = () => X.Circle;
        XToggle("xh.circleFire", "원 — 쏘면 벌어짐", () => X.CircleFire, b => X.CircleFire = b).Visible = () => X.Circle;

        XToggle("xh.tstyle", "T자 (위쪽 선 없음)", () => X.TStyle, b => X.TStyle = b);
        XSlider("xh.error", "벌어지는 세기", () => X.ErrorScale, v => X.ErrorScale = v, 0f, 2f, false);

        // ══════════ 채팅 · 보이스 ══════════
        Toggle("chat.visible", "voice", "채팅창 보이기", () => GameSettings.ChatVisible, b => GameSettings.ChatVisible = b,
               "꺼도 Enter로 열면 보임");
        Choice("voice.mode", "voice", "마이크 방식", () => new[] { "누르고 말하기", "열린 마이크" },
               () => (int)VoiceSettings.Mode, i => VoiceSettings.Mode = (MicMode)i);
        Slider("voice.vad", "voice", "목소리 감지 민감도", () => VoiceSettings.OpenMicThreshold, v => VoiceSettings.OpenMicThreshold = v,
               0.001f, 0.1f, v => $"{v:0.000}", note: "왼쪽일수록 예민").Visible = () => VoiceSettings.Mode == MicMode.OpenMic;
        Choice("voice.mic", "voice", "마이크", MicNames, MicIndex, SetMic).LocOptions = false;   // 장치 이름
        Toggle("voice.matchMute", "voice", "경기 마이크 음소거", () => VoiceSettings.MatchMuted, b => VoiceSettings.MatchMuted = b);
        Toggle("voice.teamMute", "voice", "팀 마이크 음소거", () => VoiceSettings.TeamMuted, b => VoiceSettings.TeamMuted = b)
            .Visible = () => VoiceChat.TeamAvailable;
        Slider("voice.matchVol", "voice", "경기 보이스 크기", () => VoiceSettings.MatchVolume, v => VoiceSettings.MatchVolume = v, 0f, 1f, Pct);
        Slider("voice.teamVol", "voice", "팀 보이스 크기", () => VoiceSettings.TeamVolume, v => VoiceSettings.TeamVolume = v, 0f, 1f, Pct)
            .Visible = () => VoiceChat.TeamAvailable;
        Key("voice.key.matchTalk", "voice", "경기 — 누르고 말하기", () => VoiceSettings.MatchTalkKey, k => VoiceSettings.MatchTalkKey = k);
        Key("voice.key.matchMute", "voice", "경기 마이크 음소거 키", () => VoiceSettings.MatchMuteKey, k => VoiceSettings.MatchMuteKey = k);
        Key("voice.key.teamTalk", "voice", "팀 — 누르고 말하기", () => VoiceSettings.TeamTalkKey, k => VoiceSettings.TeamTalkKey = k)
            .Visible = () => VoiceChat.TeamAvailable;
        Key("voice.key.teamMute", "voice", "팀 마이크 음소거 키", () => VoiceSettings.TeamMuteKey, k => VoiceSettings.TeamMuteKey = k)
            .Visible = () => VoiceChat.TeamAvailable;
        Confirm("voice.key.reset", "voice", "보이스 키 기본값으로", "기본값으로", ResetVoiceKeys,
                "T · Y · K · L 로 돌아감");

        // ══════════ HUD · 접근성 ══════════
        Slider("hud.scale", "hud", "HUD 크기", () => GameSettings.HudScale, v => GameSettings.HudScale = v, 0.6f, 1.5f, Pct);
        Toggle("hud.dmg", "hud", "데미지 숫자", () => GameSettings.DamageNumbers, b => GameSettings.DamageNumbers = b);
        Toggle("hud.names", "hud", "머리 위 이름", () => GameSettings.OverheadNames, b => GameSettings.OverheadNames = b);
        Toggle("hud.bars", "hud", "머리 위 체력바", () => GameSettings.OverheadBars, b => GameSettings.OverheadBars = b);
        Slider("acc.shake", "hud", "화면 흔들림 (맞을 때 · 폭발)", () => GameSettings.ShakeScale, v => GameSettings.ShakeScale = v, 0f, 1f, Pct);
        Slider("acc.bob", "hud", "걸을 때 화면 출렁임", () => GameSettings.BobScale, v => GameSettings.BobScale = v, 0f, 1f, Pct,
               note: "멀미가 나면 줄이기");
        Slider("acc.flash", "hud", "화면 번쩍임", () => GameSettings.FlashScale, v => GameSettings.FlashScale = v, 0f, 1f, Pct);
        Slider("acc.hitstop", "hud", "타격 멈춤 (히트스톱)", () => GameSettings.HitStopScale, v => GameSettings.HitStopScale = v, 0f, 1f, Pct);
        Choice("acc.enemyColor", "hud", "적 테두리 색", () => GameSettings.EnemyColorNames, () => GameSettings.EnemyColor, i => GameSettings.EnemyColor = i,
               "색을 구분하기 어려우면 바꾸기");

        _map = new Dictionary<string, SettingDef>();
        foreach (var d in _all) _map[d.Key] = d;
    }

    static CrosshairStyle X => CrosshairSettings.Edit;

    // ── 만들기 도우미 ──

    static SettingDef Add(SettingDef d)
    {
        _all.Add(d);
        return d;
    }

    static SettingDef Toggle(string key, string tab, string label, Func<bool> get, Action<bool> set, string note = null, bool pending = false) =>
        Add(new SettingDef { Key = key, Tab = tab, Label = label, Note = note, Kind = SettingKind.Toggle, GetB = get, SetB = set, Pending = pending });

    static SettingDef Slider(string key, string tab, string label, Func<float> get, Action<float> set, float min, float max,
                             Func<float, string> fmt = null, string note = null, bool whole = false, bool pending = false) =>
        Add(new SettingDef
        {
            Key = key, Tab = tab, Label = label, Note = note, Kind = SettingKind.Slider, GetF = get, SetF = set,
            Min = min, Max = max, Format = fmt, Whole = whole, Pending = pending,
        });

    static SettingDef Choice(string key, string tab, string label, Func<string[]> options, Func<int> get, Action<int> set,
                             string note = null, bool pending = false) =>
        Add(new SettingDef { Key = key, Tab = tab, Label = label, Note = note, Kind = SettingKind.Choice, Options = options, GetI = get, SetI = set, Pending = pending });

    static SettingDef Key(string key, string tab, string label, Func<KeyCode> get, Action<KeyCode> set) =>
        Add(new SettingDef { Key = key, Tab = tab, Label = label, Kind = SettingKind.Key, GetK = get, SetK = set });

    /// <summary>
    /// 두 번 눌러야 실행되는 버튼 — 처음 누르면 "한 번 더 누르면 실행" (3초), 그 안에 또 누르면 실행 → "완료!" (1.5초)
    /// </summary>
    static SettingDef Confirm(string key, string tab, string label, string button, System.Action run, string note = null)
    {
        float armedUntil = -1f, doneUntil = -1f;
        return Add(new SettingDef
        {
            Key = key, Tab = tab, Label = label, Note = note, Kind = SettingKind.Action, ButtonText = button,
            ButtonTextF = () =>
            {
                float t = Time.unscaledTime;
                if (t < doneUntil) return SetLoc.T("set.ui.done", "완료!");
                if (t < armedUntil) return SetLoc.T("set.ui.confirm_reset", "한 번 더 누르면 초기화");
                return SetLoc.T($"set.{key}.btn", button);
            },
            Run = () =>
            {
                float t = Time.unscaledTime;
                if (t < armedUntil)
                {
                    armedUntil = -1f;
                    doneUntil = t + 1.5f;
                    run?.Invoke();
                }
                else armedUntil = t + 3f;
            },
        });
    }

    /// <summary>보이스 키 기본값 — VoiceSettings 안의 기본값(T · Y · K · L · O)과 같게 유지</summary>
    static void ResetVoiceKeys()
    {
        VoiceSettings.MatchTalkKey = KeyCode.T;
        VoiceSettings.TeamTalkKey = KeyCode.Y;
        VoiceSettings.MatchMuteKey = KeyCode.K;
        VoiceSettings.TeamMuteKey = KeyCode.L;
        VoiceSettings.SettingsKey = KeyCode.O;
        GameSettings.NotifyChanged();
    }

    // 조준점 — 값을 바꾸면 저장 · 다시 그림
    static SettingDef XToggle(string key, string label, Func<bool> get, Action<bool> set, bool global = false) =>
        Toggle(key, "crosshair", label, get, b => { set(b); if (!global) CrosshairSettings.EditChanged(); });

    static SettingDef XSlider(string key, string label, Func<float> get, Action<float> set, float min, float max, bool whole) =>
        Slider(key, "crosshair", label, get, v => { set(v); CrosshairSettings.EditChanged(); }, min, max,
               whole ? (Func<float, string>)(v => $"{Mathf.RoundToInt(v)}") : Pct, whole: whole);

    // 마이크
    static string[] MicNames()
    {
        var d = UnityEngine.Microphone.devices;
        var r = new string[d.Length + 1];
        r[0] = SetLoc.T("set.voice.mic.default", "시스템 기본");
        Array.Copy(d, 0, r, 1, d.Length);
        return r;
    }

    static int MicIndex()
    {
        string cur = VoiceSettings.MicDevice;
        if (string.IsNullOrEmpty(cur)) return 0;
        int i = Array.IndexOf(UnityEngine.Microphone.devices, cur);
        return i >= 0 ? i + 1 : 0;
    }

    static void SetMic(int i)
    {
        var d = UnityEngine.Microphone.devices;
        VoiceSettings.MicDevice = i <= 0 || i > d.Length ? "" : d[i - 1];
    }
}
