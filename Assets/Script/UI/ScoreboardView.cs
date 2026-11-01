using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>Tab을 누르고 있는 동안 순위표. 제단 → 소울 → 킬 순 정렬. GameHUD의 OnGUI 순위표 대체</summary>
public class ScoreboardView : MonoBehaviour
{
    [Tooltip("순위표 전체 (평소엔 꺼둠)")]
    [SerializeField] private GameObject root;
    [Tooltip("줄들이 놓일 부모 (Vertical Layout Group)")]
    [SerializeField] private Transform rowParent;
    [SerializeField] private ScoreboardRow rowPrefab;
    [SerializeField] private float refreshInterval = 0.25f;     // 키 = 설정 키 설정 '순위표' (기본 Tab)

    struct Entry
    {
        public string Name;
        public int K, A, D, Soul, Altars;
        public bool Local;
        public AbilityData Class;
    }

    private readonly List<ScoreboardRow> _rows = new();
    private readonly List<Entry> _entries = new();
    private NetworkRunner _runner;
    private float _next;

    void Awake()
    {
        if (root != null) root.SetActive(false);
    }

    void Update()
    {
        bool show = KeyBinds.Held(GameAction.Scoreboard) && !DevConsole.IsOpen && !CommsHud.ChatOpen
                    && !EscMenu.IsOpen && !SettingsWindow.IsOpen;

        if (root != null && root.activeSelf != show)
        {
            root.SetActive(show);
            if (show) _next = 0f;
        }

        if (!show || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + refreshInterval;

        Refresh();
    }

    void Refresh()
    {
        if (_runner == null || !_runner.IsRunning) _runner = FindFirstObjectByType<NetworkRunner>();
        if (_runner == null || !_runner.IsRunning) return;

        var rs = RoomState.Instance;

        _entries.Clear();
        foreach (var p in _runner.ActivePlayers)
        {
            var obj = _runner.GetPlayerObject(p);
            if (obj == null || !obj.TryGetComponent<PlayerHealth>(out var h)) continue;   // 옵저버는 빠짐

            _entries.Add(new Entry
            {
                Name = rs != null ? rs.NameOf(p) : LocUtil.Get("ui.score.player", p.PlayerId),   // 방 닉네임
                K = h.Kills,
                A = h.Assists,
                D = h.Deaths,
                Soul = h.Soul,
                Altars = PlayerAltars.CountOf(p),
                Local = p == _runner.LocalPlayer,
                Class = obj.TryGetComponent<AbilityRuntime>(out var ar) ? ar.GetMainAbility() : null,   // 고르는 중이면 null
            });
        }

        // 제단 → 소울 → 킬
        _entries.Sort((a, b) =>
        {
            int c = b.Altars.CompareTo(a.Altars);
            if (c != 0) return c;
            c = b.Soul.CompareTo(a.Soul);
            return c != 0 ? c : b.K.CompareTo(a.K);
        });

        var rm = RoundManager.Instance;
        int need = rm != null && rm.Object != null && rm.Object.IsValid ? rm.AltarsNeeded : AltarRules.Needed;

        while (_rows.Count < _entries.Count)
            _rows.Add(Instantiate(rowPrefab, rowParent));

        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < _entries.Count;
            _rows[i].gameObject.SetActive(used);
            if (!used) continue;

            var e = _entries[i];
            _rows[i].Bind(i + 1, e.Name, e.K, e.A, e.D, e.Soul, e.Local);
            _rows[i].SetAltars(e.Altars, need);
            _rows[i].SetClass(e.Class);            // Bind 뒤에 (이름 칸에 붙일 수도 있어서)
        }
    }
}
