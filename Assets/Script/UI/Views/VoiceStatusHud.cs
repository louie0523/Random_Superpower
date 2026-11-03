using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 화면 왼쪽 아래 — 내 마이크 상태 + 지금 말하는 사람들 (예전 VoiceSettingsPanel의 OnGUI 글자 대신 UI).
///   ● 마이크 · 누르고 말하기 (T)   음소거   말하는 중
///   ▶ 경기 · 닉네임            (말하는 사람마다 한 줄, 위로 쌓임)
/// 점 색: 말하는 중 초록 · 음소거 빨강 · 평소 회색. 글자는 번역 키 voice.hud.* (없으면 한국어)
/// 씬에 없으면 알아서 생김. HUD 크기 설정(HudScaler)도 따름.
/// HUD v2 안에 들어 있으면(다른 캔버스 안) 그 자리에 그리고, 혼자 생긴 것은 그동안 숨음
/// </summary>
public class VoiceStatusHud : MonoBehaviour
{
    [SerializeField] private Vector2 margin = new(20f, 96f);
    [SerializeField] private float fontSize = 19f;

    static readonly Color Back = new(0f, 0f, 0f, 0.45f);
    static readonly Color Idle = new(0.72f, 0.72f, 0.76f);
    static readonly Color Talk = new(0.49f, 1f, 0.54f);
    static readonly Color Mute = new(1f, 0.42f, 0.42f);
    const string Red = "#ff6b6b", Green = "#7dff8a", Cyan = "#6fd6ff", Gray = "#b8b8b8", Dim = "#9a9aa5";

    class Line
    {
        public GameObject Go;
        public Image Dot;
        public TMP_Text Text;
    }

    RectTransform _root, _stack;
    bool _embedded;                     // 다른 캔버스(HUD v2) 안
    static VoiceStatusHud _inHud;
    readonly List<Line> _lines = new();
    readonly StringBuilder _sb = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindFirstObjectByType<VoiceStatusHud>() != null) return;
        var go = new GameObject("VoiceStatusHud");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceStatusHud>();
    }

    void Awake()
    {
        _embedded = transform.parent != null && transform.parent.GetComponentInParent<Canvas>(true) != null;
        if (_embedded)
        {
            _inHud = this;
            _root = K.Rect("Root", transform);
            K.Fill(_root);
        }
        else
        {
            _root = K.MakeScreen(gameObject, 450, null);
            var gr = GetComponent<GraphicRaycaster>();
            if (gr != null) gr.enabled = false;                     // 클릭 안 막게
            gameObject.AddComponent<HudScaler>();
        }

        _stack = K.Rect("Stack", _root);
        _stack.anchorMin = _stack.anchorMax = _stack.pivot = Vector2.zero;
        _stack.anchoredPosition = margin;
        var v = _stack.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.LowerLeft;
        v.spacing = 4f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        var f = _stack.gameObject.AddComponent<ContentSizeFitter>();
        f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    Line Get(int i)
    {
        while (_lines.Count <= i)
        {
            var row = K.Rect("Line", _stack);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = K.Round;
            bg.type = Image.Type.Sliced;
            bg.color = Back;
            bg.raycastTarget = false;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, 14, 4, 4);
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            var dot = K.Img("Dot", row, K.Circle, Idle);
            K.Size(dot, 12f, 12f);

            var t = K.Text("Text", row, UiFont.Get(), fontSize, Color.white);
            t.richText = true;
            t.overflowMode = TextOverflowModes.Overflow;

            _lines.Add(new Line { Go = row.gameObject, Dot = dot, Text = t });
        }
        return _lines[i];
    }

    void Set(int i, Color dot, string text)
    {
        var l = Get(i);
        if (!l.Go.activeSelf) l.Go.SetActive(true);
        if (l.Dot.color != dot) l.Dot.color = dot;
        if (l.Text.text != text) l.Text.text = text;
    }

    void OnDestroy()
    {
        if (_inHud == this) _inHud = null;
    }

    void LateUpdate()
    {
        var me = VoiceChat.Local;
        var spec = SpectatorVoice.Local;
        bool has = me != null || spec != null;
        if (!_embedded && _inHud != null && _inHud.isActiveAndEnabled) has = false;     // HUD 안의 것이 보이는 동안은 숨음
        if (_root.gameObject.activeSelf != has) _root.gameObject.SetActive(has);
        if (!has) return;

        int n = 0;

        // 말하는 사람들 (위)
        foreach (var v in VoiceChat.All)
        {
            if (v == null || v == me || !v.IsTalking) continue;
            bool team = (v.Flags & VoiceChat.FlagTeam) != 0 && VoiceChat.TeamAvailable;
            string tag = team ? Col(Cyan, SetLoc.T("voice.hud.team", "팀")) : SetLoc.T("voice.hud.match", "경기");
            Set(n++, Talk, $"{tag}  <color={Dim}>·</color>  {TargetUtil.NameOf(v)}");
        }
        foreach (var v in SpectatorVoice.All)
        {
            if (v == null || v == spec || !v.IsTalking) continue;
            Set(n++, Talk, $"{Col(Gray, SetLoc.T("voice.hud.spectator", "관전"))}  <color={Dim}>·</color>  {v.DisplayName}");
        }

        // 내 상태 (맨 아래)
        bool talking = me != null ? me.IsTalking : spec.IsTalking;
        bool muted = VoiceSettings.MatchMuted;

        _sb.Clear();
        _sb.Append(me != null ? SetLoc.T("voice.hud.mic", "마이크") : SetLoc.T("voice.hud.spec_mic", "관전 마이크"));
        _sb.Append($"  <color={Dim}>·</color>  ");
        _sb.Append(VoiceSettings.Mode == MicMode.PushToTalk
            ? SetLoc.F("voice.hud.ptt", "누르고 말하기 ({0})", VoiceSettings.MatchTalkKey)       // 키 이름은 번역 안 함
            : SetLoc.T("voice.hud.open", "열린 마이크"));
        if (muted) _sb.Append("   ").Append(Col(Red, SetLoc.T("voice.hud.muted", "음소거")));
        if (me != null && VoiceChat.TeamAvailable && VoiceSettings.TeamMuted)
            _sb.Append("   ").Append(Col(Red, SetLoc.T("voice.hud.team_muted", "팀 음소거")));
        if (talking) _sb.Append("   ").Append(Col(Green, SetLoc.T("voice.hud.talking", "말하는 중")));

        Set(n++, talking ? Talk : muted ? Mute : Idle, _sb.ToString());

        for (int i = n; i < _lines.Count; i++)
            if (_lines[i].Go.activeSelf) _lines[i].Go.SetActive(false);
    }

    static string Col(string hex, string s) => $"<color={hex}>{s}</color>";
}
