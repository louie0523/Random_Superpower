using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>Tab을 누르고 있는 동안 순위표. 소울 → 킬 순 정렬. GameHUD의 OnGUI 순위표 대체</summary>
public class ScoreboardView : MonoBehaviour
{
    [Tooltip("순위표 전체 (평소엔 꺼둠)")]
    [SerializeField] private GameObject root;
    [Tooltip("줄들이 놓일 부모 (Vertical Layout Group)")]
    [SerializeField] private Transform rowParent;
    [SerializeField] private ScoreboardRow rowPrefab;
    [SerializeField] private KeyCode key = KeyCode.Tab;
    [SerializeField] private float refreshInterval = 0.25f;

    struct Entry
    {
        public string Name;
        public int K, A, D, Soul;
        public bool Local;
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
        bool show = Input.GetKey(key) && !DevConsole.IsOpen;

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
        if (_runner == null) _runner = FindFirstObjectByType<NetworkRunner>();
        if (_runner == null || !_runner.IsRunning) return;

        _entries.Clear();
        foreach (var p in _runner.ActivePlayers)
        {
            var obj = _runner.GetPlayerObject(p);
            if (obj == null || !obj.TryGetComponent<PlayerHealth>(out var h)) continue;

            _entries.Add(new Entry
            {
                Name = LocUtil.Get("ui.score.player", p.PlayerId),
                K = h.Kills,
                A = h.Assists,
                D = h.Deaths,
                Soul = h.Soul,
                Local = p == _runner.LocalPlayer,
            });
        }

        _entries.Sort((a, b) =>
        {
            int c = b.Soul.CompareTo(a.Soul);
            return c != 0 ? c : b.K.CompareTo(a.K);
        });

        while (_rows.Count < _entries.Count)
            _rows.Add(Instantiate(rowPrefab, rowParent));

        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < _entries.Count;
            _rows[i].gameObject.SetActive(used);
            if (!used) continue;

            var e = _entries[i];
            _rows[i].Bind(i + 1, e.Name, e.K, e.A, e.D, e.Soul, e.Local);
        }
    }
}
