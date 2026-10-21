using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

/// <summary>
/// 치트 콘솔 — ` (또는 F8)로 열고 /명령 입력. 씬에 놓을 필요 없음 (자동 생성).
/// 에디터 · Development Build에서만 켜짐 (DevConsoleSettings로 바꿀 수 있음).
///   Enter 실행 · Tab 자동완성 · ↑↓ 후보 / 기록 · Esc 닫기
///   맨 뒤 대상: @me(기본) @aim @all @others @dummies
/// </summary>
public class DevConsole : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    static DevConsole _inst;
    static readonly List<string> _pending = new();

    private DevConsoleSettings _set;
    private string _input = "";
    private string _lastInput = null;
    private readonly List<string> _log = new();
    private readonly List<string> _history = new();
    private int _histIdx = -1;
    private List<(string value, string label)> _sugg = new();
    private int _suggIdx = -1;
    private Vector2 _logScroll, _suggScroll;
    private bool _focusNext;
    private bool _scrollToEnd;
    private int _openFrame;

    private CursorLockMode _prevLock;
    private bool _prevVisible;

    private GUIStyle _logStyle, _inputStyle, _suggStyle, _suggSel, _hintStyle;
    private Texture2D _bg, _bgSugg, _bgSel;

    // ══════════════════ 생성 ══════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var set = DevConsoleSettings.Get();
        bool on = Application.isEditor || Debug.isDebugBuild || set.EnableInReleaseBuild;
        if (!on || _inst != null) return;

        var go = new GameObject("DevConsole");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<DevConsole>();
    }

    /// <summary>콘솔에 한 줄 (다른 사람이 실행한 결과도 여기로)</summary>
    public static void Print(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        if (_inst == null) { _pending.Add(msg); Debug.Log("[Dev] " + msg); return; }
        _inst.Add(msg);
    }

    void Awake()
    {
        _set = DevConsoleSettings.Get();
        foreach (var m in _pending) Add(m);
        _pending.Clear();
        Add("<color=#9a9a9a>치트 콘솔 — /help · Tab 자동완성 · 맨 뒤 @aim 으로 조준한 대상에게</color>");
    }

    void Add(string msg)
    {
        foreach (var line in msg.Split('\n')) _log.Add(line);
        while (_log.Count > 300) _log.RemoveAt(0);
        _scrollToEnd = true;
    }

    // ══════════════════ 열고 닫기 ══════════════════

    void Update()
    {
        if (!IsOpen && !CommsHud.ChatOpen && (Input.GetKeyDown(_set.ToggleKey) || Input.GetKeyDown(_set.AltToggleKey)))
            Open();

        // 닫혀 있어도 다른 UI가 커서를 바꿨을 수 있으니 열려 있는 동안만 강제
        if (IsOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Open()
    {
        IsOpen = true;
        _openFrame = Time.frameCount;
        _prevLock = Cursor.lockState;
        _prevVisible = Cursor.visible;
        _focusNext = true;
        _histIdx = -1;
        Input.imeCompositionMode = IMECompositionMode.On;
        Refresh(true);
    }

    void Close()
    {
        IsOpen = false;
        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;
        Input.imeCompositionMode = IMECompositionMode.Auto;
        GUIUtility.keyboardControl = 0;
    }

    void OnDisable() { if (IsOpen) Close(); }

    // ══════════════════ 실행 ══════════════════

    void Submit()
    {
        string line = _input.Trim();
        _input = "";
        Refresh(true);
        if (line.Length == 0) return;

        _history.Remove(line);
        _history.Add(line);
        if (_history.Count > 50) _history.RemoveAt(0);
        _histIdx = -1;

        Add($"<color=#cfcfcf>> {line}</color>");

        if (line == "cls" || line == "/cls") { _log.Clear(); return; }

        var tok = DevCommands.Tokenize(line);
        if (tok.Count == 0) return;

        var cmd = DevCommands.Find(tok[0]);
        if (cmd == null) { Add(DevCommands.Err($"모르는 명령: {tok[0]} — /help")); return; }

        // 대상 표시 분리
        string sel = tok.FirstOrDefault(t => t.StartsWith("@")) ?? "@me";
        var rest = tok.Where(t => !t.StartsWith("@")).ToList();
        string body = string.Join(" ", rest);

        var runner = FindRunner();
        var me = LocalPlayerObject(runner);

        if (!cmd.Targeted)
        {
            Add(me != null ? DevCommands.ExecuteOnSelf(me.gameObject, body)
                           : DevCommands.Err("내 플레이어가 아직 없음 (접속 후 사용)"));
            return;
        }

        var targets = ResolveTargets(sel, runner, me);
        if (targets.Count == 0) { Add(DevCommands.Err($"대상 없음: {sel}")); return; }

        foreach (var t in targets)
        {
            if (t == null) continue;
            if (t.HasStateAuthority)
            {
                Add(DevCommands.ExecuteOnSelf(t.gameObject, body));
                continue;
            }
            var pc = t.GetComponent<PlayerCheats>();
            if (pc != null) pc.RPC_Exec(body, runner != null ? runner.LocalPlayer : PlayerRef.None);
            else Add(DevCommands.Err($"{t.name}: 권한이 없고 PlayerCheats도 없어서 못 걸음 — 프리팹에 PlayerCheats 추가"));
        }
    }

    static NetworkRunner FindRunner()
    {
        var r = FindFirstObjectByType<NetworkRunner>();
        return r != null && r.IsRunning ? r : null;
    }

    static NetworkObject LocalPlayerObject(NetworkRunner runner)
    {
        if (runner == null) return null;
        return runner.GetPlayerObject(runner.LocalPlayer);
    }

    static List<NetworkObject> ResolveTargets(string sel, NetworkRunner runner, NetworkObject me)
    {
        var list = new List<NetworkObject>();
        var players = new HashSet<NetworkObject>();
        if (runner != null)
            foreach (var p in runner.ActivePlayers)
            {
                var o = runner.GetPlayerObject(p);
                if (o != null) players.Add(o);
            }

        IEnumerable<NetworkObject> AllHealth() =>
            FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None)
                .Where(h => h != null && h.Object != null && h.Object.IsValid)
                .Select(h => h.Object);

        switch (sel.ToLowerInvariant())
        {
            case "@me":
                if (me != null) list.Add(me);
                break;
            case "@aim":
            {
                var cam = Camera.main;
                if (cam == null) break;
                var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 200f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits.OrderBy(h => h.distance))
                {
                    var ph = h.collider.GetComponentInParent<PlayerHealth>();
                    if (ph == null || ph.Object == null || ph.Object == me) continue;
                    list.Add(ph.Object);
                    break;
                }
                break;
            }
            case "@all": list.AddRange(AllHealth()); break;
            case "@others": list.AddRange(AllHealth().Where(o => o != me)); break;
            case "@dummies": list.AddRange(AllHealth().Where(o => !players.Contains(o))); break;
        }
        return list.Distinct().ToList();
    }

    // ══════════════════ 자동완성 ══════════════════

    void Refresh(bool force = false)
    {
        if (!force && _input == _lastInput) return;
        _lastInput = _input;
        _sugg = _input.Length == 0
            ? DevCommands.All.Select(c => (c.Name, $"{c.Usage}  — {c.Help}")).ToList()
            : DevCommands.Suggest(_input);
        _suggIdx = _sugg.Count > 0 && _input.Length > 0 ? 0 : -1;
        _suggScroll = Vector2.zero;
    }

    void ApplySuggestion(int i)
    {
        if (i < 0 || i >= _sugg.Count) return;
        string v = _sugg[i].value;

        string s = _input.TrimStart();
        bool slash = s.StartsWith("/");
        if (slash) s = s.Substring(1);
        int cut = s.LastIndexOf(' ');
        string head = cut >= 0 ? s.Substring(0, cut + 1) : "";
        _input = "/" + head + v + " ";
        _focusNext = true;
        Refresh(true);
    }

    // ══════════════════ 그리기 ══════════════════

    void OnGUI()
    {
        if (!IsOpen) return;
        EnsureStyles();

        float scale = Mathf.Max(0.6f, Screen.height / 1080f) * _set.FontScale;
        var old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float W = Screen.width / scale, H = Screen.height / scale;

        HandleKeys();

        // 기록
        float logH = H * 0.38f;
        GUI.DrawTexture(new Rect(0, 0, W, logH + 44), _bg);
        var logRect = new Rect(10, 8, W - 20, logH - 8);
        float lineH = _logStyle.lineHeight + 4;
        var content = new Rect(0, 0, logRect.width - 20, Mathf.Max(logRect.height, _log.Count * lineH));
        if (_scrollToEnd) { _logScroll.y = content.height; _scrollToEnd = false; }
        _logScroll = GUI.BeginScrollView(logRect, _logScroll, content);
        for (int i = 0; i < _log.Count; i++)
            GUI.Label(new Rect(0, i * lineH, content.width, lineH), _log[i], _logStyle);
        GUI.EndScrollView();

        // 입력
        var inRect = new Rect(10, logH + 4, W - 20, 34);
        GUI.SetNextControlName("DevInput");
        string next = GUI.TextField(inRect, _input, 200, _inputStyle);
        if (next != _input) { _input = next; _histIdx = -1; Refresh(); }
        if (_focusNext)
        {
            GUI.FocusControl("DevInput");
            var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (te != null) { te.text = _input; te.MoveTextEnd(); }
            _focusNext = false;
        }

        // 후보
        if (_sugg.Count > 0)
        {
            int show = Mathf.Min(_sugg.Count, 12);
            float rowH = _suggStyle.lineHeight + 8;
            var box = new Rect(10, logH + 42, Mathf.Min(W - 20, 900), show * rowH + 6);
            GUI.DrawTexture(box, _bgSugg);
            var inner = new Rect(0, 0, box.width - 20, _sugg.Count * rowH);
            _suggScroll = GUI.BeginScrollView(new Rect(box.x, box.y + 3, box.width, box.height - 6), _suggScroll, inner);
            for (int i = 0; i < _sugg.Count; i++)
            {
                var r = new Rect(0, i * rowH, inner.width, rowH);
                if (i == _suggIdx) GUI.DrawTexture(r, _bgSel);
                string text = $"<b>{_sugg[i].value}</b>   <color=#a8a8a8>{_sugg[i].label}</color>";
                if (GUI.Button(r, text, i == _suggIdx ? _suggSel : _suggStyle)) ApplySuggestion(i);
            }
            GUI.EndScrollView();
        }

        GUI.Label(new Rect(W - 520, logH + 10, 510, 24), "Enter 실행 · Tab 완성 · ↑↓ 선택/기록 · Esc 닫기", _hintStyle);
        GUI.matrix = old;
    }

    void HandleKeys()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown) return;

        // 글자 이벤트 — 열기 키로 친 ` · Tab · Enter 글자가 입력칸에 들어가지 않게
        if (e.keyCode == KeyCode.None)
        {
            if (e.character == '`' || e.character == '\t' || e.character == '\n') e.Use();
            return;
        }

        // 연 프레임에 같은 키로 바로 닫히지 않게
        if ((e.keyCode == _set.ToggleKey || e.keyCode == _set.AltToggleKey) && Time.frameCount != _openFrame)
        {
            Close(); e.Use(); return;
        }

        switch (e.keyCode)
        {
            case KeyCode.Escape:
                Close(); e.Use(); break;

            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                Submit(); _focusNext = true; e.Use(); break;

            case KeyCode.Tab:
                ApplySuggestion(_suggIdx >= 0 ? _suggIdx : 0); e.Use(); break;

            case KeyCode.UpArrow:
                if (_sugg.Count > 0 && _input.Length > 0 && _suggIdx > 0) { _suggIdx--; KeepVisible(); }
                else if (_history.Count > 0)
                {
                    _histIdx = _histIdx < 0 ? _history.Count - 1 : Mathf.Max(0, _histIdx - 1);
                    _input = _history[_histIdx]; _focusNext = true; _lastInput = _input; _sugg.Clear(); _suggIdx = -1;
                }
                e.Use(); break;

            case KeyCode.DownArrow:
                if (_sugg.Count > 0 && _input.Length > 0 && _histIdx < 0) { _suggIdx = Mathf.Min(_sugg.Count - 1, _suggIdx + 1); KeepVisible(); }
                else if (_histIdx >= 0)
                {
                    _histIdx++;
                    if (_histIdx >= _history.Count) { _histIdx = -1; _input = ""; }
                    else _input = _history[_histIdx];
                    _focusNext = true; _lastInput = _input; _sugg.Clear(); _suggIdx = -1;
                }
                e.Use(); break;
        }
    }

    void KeepVisible()
    {
        float rowH = _suggStyle.lineHeight + 8;
        float y = _suggIdx * rowH;
        float viewH = Mathf.Min(_sugg.Count, 12) * rowH;
        if (y < _suggScroll.y) _suggScroll.y = y;
        else if (y + rowH > _suggScroll.y + viewH) _suggScroll.y = y + rowH - viewH;
    }

    void EnsureStyles()
    {
        if (_logStyle != null) return;
        _bg = Tex(new Color(0.05f, 0.05f, 0.07f, 0.88f));
        _bgSugg = Tex(new Color(0.1f, 0.1f, 0.13f, 0.95f));
        _bgSel = Tex(new Color(0.25f, 0.45f, 0.75f, 0.6f));

        _logStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16, wordWrap = false };
        _logStyle.normal.textColor = Color.white;
        _inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 4, 4) };
        _suggStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 2, 2) };
        _suggStyle.normal.textColor = Color.white;
        _suggStyle.hover.background = _bgSel;
        _suggStyle.hover.textColor = Color.white;
        _suggSel = new GUIStyle(_suggStyle);
        _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight };
        _hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
    }

    static Texture2D Tex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
