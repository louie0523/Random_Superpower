using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 알림(큰 알림 · 작은 메시지) · 채팅창 · 플레이어 선택 창.
/// 씬에 없으면 알아서 하나 만들어짐 — 글꼴을 지정하려면 HUD 오브젝트 등에 직접 붙이고 Font에 한글 글꼴을 넣을 것.
///
/// 채팅: Enter 열기 → 입력 → Enter 보내기 · Esc 닫기 · Tab 경기/팀 전환(팀 모드일 때만)
/// </summary>
public class CommsHud : MonoBehaviour
{
    [Header("글꼴")]
    [Tooltip("알림 글꼴 (한글). 비우면 씬의 아무 TMP 글꼴 → TMP 기본")]
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("채팅 · 선택 창 글꼴 (한글 TTF/OTF — 예: 나눔고딕 · Noto Sans KR).\n" +
             "비우면 위 TMP 글꼴의 원본 글꼴 파일 → 그것도 없으면 Unity 기본(OS 글꼴로 대체)")]
    [SerializeField] private UnityEngine.Font chatFont;
    [Tooltip("채팅 · 선택 창 글자 크기 배율")]
    [Range(0.6f, 2f)][SerializeField] private float uiScale = 1f;

    [Header("큰 알림")]
    [SerializeField] private float bigSize = 46f;
    [SerializeField] private float bigHold = 3.5f;
    [SerializeField] private Color bigColor = new(1f, 0.86f, 0.35f, 1f);

    [Header("작은 메시지")]
    [SerializeField] private float smallSize = 26f;
    [SerializeField] private float smallHold = 5f;
    [SerializeField] private int smallMax = 5;
    [SerializeField] private Color smallColor = Color.white;

    [Header("채팅창")]
    [Tooltip("닫혀 있을 때 새 줄이 보이는 시간 (초)")]
    [SerializeField] private float chatFade = 8f;
    [SerializeField] private int chatShownLines = 8;
    [SerializeField] private int chatOpenLines = 14;

    static CommsHud _inst;

    /// <summary>채팅 입력 중 — UIManager.InputBlocked에 포함</summary>
    public static bool ChatOpen { get; private set; }

    // ── 알림 (TMP) ──
    RectTransform _canvas;
    TextMeshProUGUI _big;
    float _bigUntil, _bigStart;
    readonly List<(TextMeshProUGUI t, float until)> _smalls = new();

    // ── 채팅 (IMGUI) ──
    string _input = "";
    ChatChannel _channel = ChatChannel.Match;
    int _openFrame;
    GUIStyle _line, _field, _tag;
    float _styleScale = -1f;

    // ══════════════════ 생성 ══════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindFirstObjectByType<CommsHud>() != null) return;
        var go = new GameObject("CommsHud");
        DontDestroyOnLoad(go);
        go.AddComponent<CommsHud>();
    }

    void Awake()
    {
        if (_inst != null && _inst != this) { Destroy(this); return; }
        _inst = this;
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (_inst == this) { _inst = null; ChatOpen = false; PickShowing = false; }
    }

    /// <summary>IMGUI(채팅 · 선택 창)용 글꼴 — 한글이 □로 안 나오게</summary>
    UnityEngine.Font ChatFont()
    {
        if (chatFont != null) return chatFont;
        var f = Font();
        if (f != null && f.sourceFontFile != null) chatFont = f.sourceFontFile;
        return chatFont;
    }

    TMP_FontAsset Font()
    {
        if (font != null) return font;
        var any = FindFirstObjectByType<TMP_Text>();
        if (any != null && any.font != null) font = any.font;
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    void BuildCanvas()
    {
        var go = new GameObject("Comms Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 60;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        _canvas = go.GetComponent<RectTransform>();

        _big = MakeText("Big", bigSize, bigColor);
        _big.rectTransform.anchorMin = _big.rectTransform.anchorMax = new Vector2(0.5f, 0.78f);
        _big.gameObject.SetActive(false);
    }

    TextMeshProUGUI MakeText(string name, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_canvas, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        var f = Font();
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(0, 0, 0, 220);
        var r = t.rectTransform;
        r.sizeDelta = new Vector2(1400f, size * 2.4f);
        r.pivot = new Vector2(0.5f, 0.5f);
        return t;
    }

    // ══════════════════ 알림 ══════════════════

    public static void ShowNotice(NoticeKind kind, string text)
    {
        if (_inst == null) AutoCreate();
        if (_inst == null) return;
        if (kind == NoticeKind.Big) _inst.ShowBig(text);
        else if (kind == NoticeKind.Small) _inst.ShowSmall(text);
    }

    void ShowBig(string text)
    {
        _big.text = text;
        _big.gameObject.SetActive(true);
        _bigStart = Time.unscaledTime;
        _bigUntil = _bigStart + bigHold;
    }

    void ShowSmall(string text)
    {
        var t = MakeText("Small", smallSize, smallColor);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.70f);
        t.text = text;
        _smalls.Insert(0, (t, Time.unscaledTime + smallHold));
        while (_smalls.Count > smallMax)
        {
            Destroy(_smalls[^1].t.gameObject);
            _smalls.RemoveAt(_smalls.Count - 1);
        }
    }

    void TickNotices()
    {
        float now = Time.unscaledTime;

        if (_big.gameObject.activeSelf)
        {
            float a = Mathf.Min(Mathf.Clamp01((now - _bigStart) / 0.2f), Mathf.Clamp01((_bigUntil - now) / 0.6f));
            _big.alpha = a;
            float pop = 1f + 0.15f * (1f - Mathf.Clamp01((now - _bigStart) / 0.25f));
            _big.rectTransform.localScale = Vector3.one * pop;
            if (now >= _bigUntil) _big.gameObject.SetActive(false);
        }

        for (int i = _smalls.Count - 1; i >= 0; i--)
        {
            var (t, until) = _smalls[i];
            if (now >= until) { Destroy(t.gameObject); _smalls.RemoveAt(i); continue; }
            t.alpha = Mathf.Clamp01((until - now) / 0.6f);
            t.rectTransform.anchoredPosition = new Vector2(0f, -i * smallSize * 1.4f);
        }
    }

    // ══════════════════ 매 프레임 ══════════════════

    void Update()
    {
        TickNotices();

        if (PickShowing)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (!ChatOpen && Input.GetKeyDown(KeyCode.Return) && CanOpenChat()) OpenChat();
        if (ChatOpen && PlayerComms.Local == null) CloseChat();
    }

    static bool CanOpenChat()
    {
        if (PlayerComms.Local == null) return false;
        if (DevConsole.IsOpen || ClassPickPanel.IsShowing || VoiceSettingsPanel.IsOpen) return false;
        var ui = UIManager.Instance;
        return ui == null || !ui.AnyPanelOpen;
    }

    static bool TeamChatAvailable => TeamUtil.AlliesProvider != null;

    void OpenChat()
    {
        ChatOpen = true;
        _openFrame = Time.frameCount;
        _input = "";
        Input.imeCompositionMode = IMECompositionMode.On;      // 한글 · 일본어 · 중국어 등 조합 입력 켜기
        if (!TeamChatAvailable) _channel = ChatChannel.Match;
    }

    void CloseChat()
    {
        ChatOpen = false;
        _input = "";
        Input.imeCompositionMode = IMECompositionMode.Auto;
        GUI.FocusControl(null);
    }

    void SendChat()
    {
        // 한글은 마지막 글자가 아직 '조합 중'일 수 있음 (예: "안녕" 치고 바로 Enter → '녕'이 조합 중) → 붙여서 보냄
        string text = _input + Input.compositionString;
        if (text.Length > PlayerComms.MaxChatLength) text = text.Substring(0, PlayerComms.MaxChatLength);
        if (!string.IsNullOrWhiteSpace(text) && PlayerComms.Local != null)
            PlayerComms.Local.SendChat(_channel, text);
        CloseChat();
    }

    // ══════════════════ 그리기 (IMGUI) ══════════════════

    void Styles()
    {
        float s = Screen.height / 1080f * uiScale;
        if (Mathf.Approximately(s, _styleScale) && _line != null) return;
        _styleScale = s;

        var cf = ChatFont();
        _line = new GUIStyle(GUI.skin.label)
        {
            font = cf,
            fontSize = Mathf.RoundToInt(20 * s),
            richText = true,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
        };
        _line.normal.textColor = Color.white;

        _field = new GUIStyle(GUI.skin.textField)
        {
            font = cf,
            fontSize = Mathf.RoundToInt(21 * s),
            alignment = TextAnchor.MiddleLeft,
        };

        _tag = new GUIStyle(_line) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
    }

    void OnGUI()
    {
        Styles();
        if (PickShowing) DrawPick();
        DrawChat();
    }

    static string ChannelLabel(ChatChannel ch) => ch switch
    {
        ChatChannel.Team => TargetUtil.Text("chat.ch.team", "팀"),
        ChatChannel.Global => TargetUtil.Text("chat.ch.global", "글로벌"),
        _ => TargetUtil.Text("chat.ch.match", "경기"),
    };

    static string ChannelColor(ChatChannel ch) => ch switch
    {
        ChatChannel.Team => "#6fd6ff",
        ChatChannel.Global => "#ffd35a",
        _ => "#ffffff",
    };

    void DrawChat()
    {
        float s = _styleScale;
        float w = 560f * s;
        float lineH = _line.fontSize * 1.45f;
        float x = 20f * s;
        float inputH = 34f * s;
        float bottom = Screen.height - 150f * s;

        // ── 입력 키 (TextField가 먹기 전에) ──
        if (ChatOpen)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Time.frameCount != _openFrame)
                { SendChat(); e.Use(); }
                else if (e.keyCode == KeyCode.Escape) { CloseChat(); e.Use(); }
                else if (e.keyCode == KeyCode.Tab || e.character == '\t')
                {
                    if (TeamChatAvailable) _channel = _channel == ChatChannel.Match ? ChatChannel.Team : ChatChannel.Match;
                    e.Use();
                }
            }
        }

        // ── 줄들 ──
        var lines = ChatLog.Lines;
        int max = ChatOpen ? chatOpenLines : chatShownLines;
        float now = Time.unscaledTime;
        float y = bottom - (ChatOpen ? inputH + 6f * s : 0f);

        if (ChatOpen)
        {
            var bg = new Rect(x - 8f * s, y - max * lineH - 8f * s, w + 16f * s, max * lineH + inputH + 22f * s);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        int shown = 0;
        for (int i = lines.Count - 1; i >= 0 && shown < max; i--)
        {
            var l = lines[i];
            float alpha = 1f;
            if (!ChatOpen)
            {
                float age = now - l.Time;
                if (age > chatFade) break;
                alpha = Mathf.Clamp01((chatFade - age) / 1.5f);
            }

            string body = string.IsNullOrEmpty(l.Sender)
                ? $"<color={ChannelColor(l.Channel)}>{Escape(l.Text)}</color>"
                : $"<color={ChannelColor(l.Channel)}>[{ChannelLabel(l.Channel)}]</color> <b>{Escape(l.Sender)}</b>: {Escape(l.Text)}";

            float h = _line.CalcHeight(new GUIContent(body), w);
            y -= h;
            GUI.color = new Color(0f, 0f, 0f, 0.8f * alpha);
            GUI.Label(new Rect(x + 1.5f, y + 1.5f, w, h), StripColors(body), _line);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(x, y, w, h), body, _line);
            shown++;
        }
        GUI.color = Color.white;

        if (!ChatOpen) return;

        // ── 입력 칸 ──
        float tagW = 70f * s;
        var tagRect = new Rect(x, bottom - inputH, tagW, inputH);
        GUI.Label(tagRect, $"<color={ChannelColor(_channel)}>[{ChannelLabel(_channel)}]</color>", _tag);

        var fieldRect = new Rect(x + tagW, bottom - inputH, w - tagW, inputH);
        GUI.SetNextControlName("ChatInput");
        _input = GUI.TextField(fieldRect, _input, PlayerComms.MaxChatLength, _field);
        // 매 프레임 다시 포커스하면 조합 중인 글자가 끊김 → 포커스가 빠졌을 때만
        if (GUI.GetNameOfFocusedControl() != "ChatInput") GUI.FocusControl("ChatInput");
        // IME 후보 창(한자 · 일본어 변환 등)이 입력 칸 옆에 뜨게
        Input.compositionCursorPos = new Vector2(fieldRect.x + 8f, fieldRect.yMax);
    }

    static string Escape(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("<", "<​");

    static string StripColors(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "</?color[^>]*>", "");

    // ══════════════════ 플레이어 선택 창 ══════════════════

    public static bool PickShowing { get; private set; }
    string _pickTitle;
    bool _pickSelf;
    Action<PlayerHealth> _onPick;
    Action _onCancel;
    Vector2 _pickScroll;

    public static void OpenPick(string title, bool includeSelf, Action<PlayerHealth> onPick, Action onCancel)
    {
        if (_inst == null) AutoCreate();
        if (_inst == null) { onCancel?.Invoke(); return; }
        if (PickShowing) _inst._onCancel?.Invoke();

        if (ChatOpen) _inst.CloseChat();
        _inst._pickTitle = title;
        _inst._pickSelf = includeSelf;
        _inst._onPick = onPick;
        _inst._onCancel = onCancel;
        PickShowing = true;
    }

    void ClosePick(PlayerHealth picked)
    {
        PickShowing = false;
        var pick = _onPick;
        var cancel = _onCancel;
        _onPick = null;
        _onCancel = null;

        var ui = UIManager.Instance;
        if (ui == null || !ui.AnyPanelOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (picked != null) pick?.Invoke(picked);
        else cancel?.Invoke();
    }

    void DrawPick()
    {
        float s = _styleScale;
        var list = TargetUtil.All(true);
        list.Sort((a, b) => string.CompareOrdinal(TargetUtil.NameOf(a), TargetUtil.NameOf(b)));

        float rowH = 40f * s;
        float w = 440f * s;
        float h = Mathf.Min(Screen.height * 0.8f, 110f * s + list.Count * (rowH + 4f * s));
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); ClosePick(null); return; }

        GUILayout.BeginArea(new Rect(rect.x + 12f * s, rect.y + 10f * s, rect.width - 24f * s, rect.height - 20f * s));
        var title = new GUIStyle(_line) { fontSize = Mathf.RoundToInt(24 * s), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        GUILayout.Label(_pickTitle, title);
        GUILayout.Space(6f * s);

        var btn = new GUIStyle(GUI.skin.button) { font = ChatFont(), fontSize = Mathf.RoundToInt(20 * s), richText = true };
        PlayerHealth picked = null;
        var local = PlayerComms.Local != null ? PlayerComms.Local.transform.root : null;

        _pickScroll = GUILayout.BeginScrollView(_pickScroll);
        foreach (var h2 in list)
        {
            bool self = local != null && h2.transform.root == local;
            if (self && !_pickSelf) continue;

            string label = TargetUtil.NameOf(h2) + (self ? TargetUtil.Text("ui.pick.self", "  (나)") : "");
            if (h2.IsDead) label = $"<color=#888888>{label}  {TargetUtil.Text("ui.pick.dead", "(사망)")}</color>";

            GUI.enabled = !h2.IsDead;
            if (GUILayout.Button(label, btn, GUILayout.Height(rowH))) picked = h2;
            GUI.enabled = true;
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6f * s);
        bool cancel = GUILayout.Button(TargetUtil.Text("ui.pick.cancel", "취소 (Esc)"), btn, GUILayout.Height(rowH));
        GUILayout.EndArea();

        if (picked != null) ClosePick(picked);
        else if (cancel) ClosePick(null);
    }
}

/// <summary>플레이어 선택 창 (부두 인형 · 데스 노트). 자신 · 팀도 고를 수 있음</summary>
public static class PlayerPickPanel
{
    public static bool IsShowing => CommsHud.PickShowing;

    public static void Open(string title, bool includeSelf, Action<PlayerHealth> onPick, Action onCancel = null) =>
        CommsHud.OpenPick(title, includeSelf, onPick, onCancel);
}