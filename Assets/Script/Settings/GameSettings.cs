using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 게임 설정 값 (이 PC에 저장 — PlayerPrefs). 다른 코드는 여기 값을 읽기만 하면 됨.
/// 바뀌면 Changed가 불림 (예: 감도 → FPSCamera가 다시 읽기).
/// 그래픽은 GraphicsOptions, 소리는 GameAudio, 보이스는 VoiceSettings, 조준점은 CrosshairSettings.
/// </summary>
public static class GameSettings
{
    const string P = "set.";

    /// <summary>어떤 설정이든 바뀌면</summary>
    public static event Action Changed;
    public static void NotifyChanged() => Changed?.Invoke();

    // ── 조작 ──
    public static float Sensitivity { get => F("sens", 1f); set => SetF("sens", Mathf.Clamp(value, 0.05f, 10f)); }
    public static bool InvertY { get => B("invertY", false); set => SetB("invertY", value); }
    /// <summary>시야각 (세로 기준 — Unity 카메라 FOV). 기본 75 = 예전 FPSCamera 값</summary>
    public static float Fov { get => F("fov", 75f); set => SetF("fov", Mathf.Clamp(value, 60f, 110f)); }

    // ── HUD ──
    public static bool DamageNumbers { get => B("hud.dmg", true); set => SetB("hud.dmg", value); }
    public static bool OverheadNames { get => B("hud.names", true); set => SetB("hud.names", value); }
    public static bool OverheadBars { get => B("hud.bars", true); set => SetB("hud.bars", value); }
    public static float HudScale { get => F("hud.scale", 1f); set => SetF("hud.scale", Mathf.Clamp(value, 0.6f, 1.5f)); }
    public static bool ChatVisible { get => B("hud.chat", true); set => SetB("hud.chat", value); }

    // ── 접근성 ──
    /// <summary>화면 흔들림 세기 (0 = 끔, 1 = 기본)</summary>
    public static float ShakeScale { get => F("acc.shake", 1f); set => SetF("acc.shake", Mathf.Clamp01(value)); }
    /// <summary>화면 번쩍임 세기</summary>
    public static float FlashScale { get => F("acc.flash", 1f); set => SetF("acc.flash", Mathf.Clamp01(value)); }
    /// <summary>타격 멈춤(히트스톱) 세기</summary>
    public static float HitStopScale { get => F("acc.hitstop", 1f); set => SetF("acc.hitstop", Mathf.Clamp01(value)); }
    /// <summary>걸을 때 화면 출렁임(헤드밥) · 기울기 세기</summary>
    public static float BobScale { get => F("acc.bob", 1f); set => SetF("acc.bob", Mathf.Clamp01(value)); }
    /// <summary>적 테두리 색 (EnemyColors 번호)</summary>
    public static int EnemyColor { get => I("acc.enemyColor", 0); set => SetI("acc.enemyColor", value); }

    public static readonly string[] EnemyColorNames = { "빨강 (기본)", "노랑", "자홍", "청록" };
    public static readonly Color[] EnemyColors =
    {
        new(1f, 0.25f, 0.25f), new(1f, 0.9f, 0.2f), new(1f, 0.3f, 1f), new(0.2f, 1f, 1f),
    };
    public static Color EnemyOutline => EnemyColors[Mathf.Clamp(EnemyColor, 0, EnemyColors.Length - 1)];

    // ── 게임플레이 ──
    /// <summary>스킬 — 뗄 때 쓰기 (끄면 누르는 순간)</summary>
    public static bool SkillOnRelease { get => B("game.skillRelease", false); set => SetB("game.skillRelease", value); }
    /// <summary>대시 — 시선 방향 (끄면 이동 방향)</summary>
    public static bool DashToLook { get => B("game.dashLook", false); set => SetB("game.dashLook", value); }
    /// <summary>바닥 무기 지나가면 저절로 줍기 (빈 칸이 있을 때). 끄면 상호작용 키 — 소울 · 스톤은 항상 저절로</summary>
    public static bool AutoPickup { get => B("game.autoPickup", true); set => SetB("game.autoPickup", value); }

    // ── 저장 도우미 ──
    static float F(string k, float d) => PlayerPrefs.GetFloat(P + k, d);
    static bool B(string k, bool d) => PlayerPrefs.GetInt(P + k, d ? 1 : 0) != 0;
    static int I(string k, int d) => PlayerPrefs.GetInt(P + k, d);
    static void SetF(string k, float v) { PlayerPrefs.SetFloat(P + k, v); NotifyChanged(); }
    static void SetB(string k, bool v) { PlayerPrefs.SetInt(P + k, v ? 1 : 0); NotifyChanged(); }
    static void SetI(string k, int v) { PlayerPrefs.SetInt(P + k, v); NotifyChanged(); }

    public static void Save() => PlayerPrefs.Save();
}

/// <summary>
/// 그래픽 설정 — 창 모드 · 해상도 · 수직 동기 · FPS 제한 · 품질 · 렌더 스케일 · 그림자.
/// 렌더 스케일 · 그림자는 URP 에셋 값을 바꾸는데, 에디터에선 플레이가 끝나면 원래 값으로 되돌림.
/// </summary>
public static class GraphicsOptions
{
    const string P = "gfx.";

    public static readonly string[] WindowNames = { "전체 화면 (테두리 없음)", "전체 화면 (독점)", "창 모드" };
    static readonly FullScreenMode[] Modes =
        { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };

    public static readonly int[] FpsValues = { 30, 60, 75, 120, 144, 165, 240, 0 };
    public static string[] FpsNames => FpsValues.Select(v => v == 0 ? SetLoc.T("set.gfx.fps.none", "제한 없음") : $"{v}").ToArray();

    public static readonly string[] ShadowNames = { "끔", "낮음", "중간", "높음" };
    static readonly float[] ShadowDistances = { 0f, 30f, 60f, 120f };

    public static int Window { get => PlayerPrefs.GetInt(P + "window", 0); set { PlayerPrefs.SetInt(P + "window", value); Apply(); } }
    public static bool VSync { get => PlayerPrefs.GetInt(P + "vsync", 1) != 0; set { PlayerPrefs.SetInt(P + "vsync", value ? 1 : 0); Apply(); } }
    public static int FpsIndex { get => PlayerPrefs.GetInt(P + "fps", 1); set { PlayerPrefs.SetInt(P + "fps", value); Apply(); } }
    public static int Quality { get => PlayerPrefs.GetInt(P + "quality", QualitySettings.GetQualityLevel()); set { PlayerPrefs.SetInt(P + "quality", value); Apply(); } }
    public static float RenderScale { get => PlayerPrefs.GetFloat(P + "scale", 1f); set { PlayerPrefs.SetFloat(P + "scale", Mathf.Clamp(value, 0.5f, 1f)); Apply(); } }
    public static int Shadows { get => PlayerPrefs.GetInt(P + "shadows", 2); set { PlayerPrefs.SetInt(P + "shadows", value); Apply(); } }

    // ── 해상도 ──
    static List<Vector2Int> _res;

    public static List<Vector2Int> Resolutions
    {
        get
        {
            if (_res == null)
            {
                _res = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct()
                                         .OrderByDescending(v => v.x).ThenByDescending(v => v.y).ToList();
                var cur = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
                if (!_res.Contains(cur)) _res.Insert(0, cur);
            }
            return _res;
        }
    }

    public static string[] ResolutionNames => Resolutions.Select(r => $"{r.x} × {r.y}").ToArray();

    public static int ResolutionIndex
    {
        get
        {
            int w = PlayerPrefs.GetInt(P + "w", Screen.width), h = PlayerPrefs.GetInt(P + "h", Screen.height);
            int i = Resolutions.IndexOf(new Vector2Int(w, h));
            return i >= 0 ? i : 0;
        }
        set
        {
            var r = Resolutions[Mathf.Clamp(value, 0, Resolutions.Count - 1)];
            PlayerPrefs.SetInt(P + "w", r.x);
            PlayerPrefs.SetInt(P + "h", r.y);
            Apply();
        }
    }

    // ── 적용 ──
    static UniversalRenderPipelineAsset _origAsset;
    static float _origScale, _origShadow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Application.quitting += Restore;
        Apply();
    }

    public static void Apply()
    {
        int q = Mathf.Clamp(Quality, 0, QualitySettings.names.Length - 1);
        if (QualitySettings.GetQualityLevel() != q) QualitySettings.SetQualityLevel(q, true);

        QualitySettings.vSyncCount = VSync ? 1 : 0;
        int fps = FpsValues[Mathf.Clamp(FpsIndex, 0, FpsValues.Length - 1)];
        Application.targetFrameRate = VSync ? -1 : (fps > 0 ? fps : -1);

#if !UNITY_EDITOR
        var mode = Modes[Mathf.Clamp(Window, 0, Modes.Length - 1)];
        int w = PlayerPrefs.GetInt(P + "w", Screen.currentResolution.width);
        int h = PlayerPrefs.GetInt(P + "h", Screen.currentResolution.height);
        if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode)
            Screen.SetResolution(w, h, mode);
#endif

        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            if (_origAsset != urp)
            {
                Restore();
                _origAsset = urp;
                _origScale = urp.renderScale;
                _origShadow = urp.shadowDistance;
            }
            urp.renderScale = RenderScale;
            urp.shadowDistance = ShadowDistances[Mathf.Clamp(Shadows, 0, ShadowDistances.Length - 1)];
        }
    }

    /// <summary>에디터에서 URP 에셋 값이 바뀐 채로 저장되지 않게 되돌림</summary>
    static void Restore()
    {
#if UNITY_EDITOR
        if (_origAsset != null)
        {
            _origAsset.renderScale = _origScale;
            _origAsset.shadowDistance = _origShadow;
        }
#endif
        _origAsset = null;
    }
}

/// <summary>언어 — Unity Localization. 고른 언어를 저장했다가 시작할 때 다시 고름</summary>
public static class LanguageOptions
{
    const string Key = "lang.code";

    static List<UnityEngine.Localization.Locale> Locales =>
        UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales != null
            ? UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales.Locales
            : new List<UnityEngine.Localization.Locale>();

    public static bool Ready =>
        UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation.IsDone;

    public static string[] Names =>
        Ready ? Locales.Select(l => l.Identifier.CultureInfo != null ? l.Identifier.CultureInfo.NativeName : l.LocaleName).ToArray()
              : new[] { SetLoc.T("set.ui.loading", "불러오는 중...") };

    public static int Index
    {
        get
        {
            if (!Ready) return 0;
            var cur = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            int i = Locales.IndexOf(cur);
            return i >= 0 ? i : 0;
        }
        set
        {
            if (!Ready || Locales.Count == 0) return;
            var l = Locales[Mathf.Clamp(value, 0, Locales.Count - 1)];
            UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale = l;
            PlayerPrefs.SetString(Key, l.Identifier.Code);
            GameSettings.NotifyChanged();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string code = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(code)) return;
        var op = UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;
        op.Completed += _ =>
        {
            var l = Locales.FirstOrDefault(x => x.Identifier.Code == code);
            if (l != null) UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale = l;
        };
    }
}
