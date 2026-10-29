using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class MapEntry
{
    [Tooltip("목록 · 설정에 보이는 이름")]
    public string Name = "기본 맵";

    [Tooltip("씬 이름 (빌드 설정에 들어 있어야 함)")]
    public string SceneName = "";
}

/// <summary>
/// 네트워크 접속 담당 — 로비(방 목록) · 방 만들기 · 참가 · 나가기.
/// Lobby 씬의 루트 오브젝트에 RoomUI와 같이 붙임. 씬이 바뀌어도 남음.
/// </summary>
public class RoomLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    public enum LinkState { Offline, ConnectingLobby, InLobby, Joining, InRoom, Leaving }

    public static RoomLauncher Instance { get; private set; }
    public static NetworkRunner Runner => Instance != null ? Instance._runner : null;

    [Header("프리팹 · 씬")]
    [Tooltip("NetworkObject + RoomState 프리팹")]
    [SerializeField] private NetworkObject roomStatePrefab;
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private List<MapEntry> maps = new() { new MapEntry() };

    [Header("방 기본값")]
    [Tooltip("새 방을 만들 때 설정. 시작 소울 · 승리 소울 · 상자 리필은 지금 게임 값과 맞춰 둘 것")]
    [SerializeField] private RoomSettings defaultSettings = new();
    [Tooltip("인원 상한 (방 설정의 최대 인원은 이 안에서)")]
    [SerializeField] private int hardMaxPlayers = 16;
    [Tooltip("켜면 모두 준비해야 방장이 시작 가능")]
    [SerializeField] private bool requireAllReady = true;

    public LinkState State { get; private set; } = LinkState.Offline;
    public string Message { get; private set; } = "";
    public float MessageTime { get; private set; } = -99f;
    public IReadOnlyList<SessionInfo> Sessions => _sessions;

    public int HardMaxPlayers => hardMaxPlayers;
    public bool RequireAllReady => requireAllReady;
    public int MapCount => Mathf.Max(1, maps.Count);
    public RoomSettings DefaultSettings => defaultSettings;

    NetworkRunner _runner;
    GameObject _runnerGo;
    NetworkSceneManagerDefault _sceneMgr;
    readonly List<SessionInfo> _sessions = new();
    bool _purposeLeave;
    float _roomWait;
    float _reconnectAt;

    const string NickKey = "room.nick";
    const string UidKey = "room.uid";
    const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Nick
    {
        get => PlayerPrefs.GetString(NickKey, "플레이어");
        set => PlayerPrefs.SetString(NickKey, value ?? "");
    }

    /// <summary>강퇴 기록용 기기 ID. 에디터는 실행마다 새로 (Multiplayer Play Mode가 PlayerPrefs를 같이 씀)</summary>
    public static string UserId { get; private set; } = "";

    // ══════════════════ 시작 ══════════════════

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);   // 대기실 씬을 다시 불러오면 생기는 중복
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        UserId = MakeUserId();
    }

    void Start()
    {
        if (Instance != this) return;

        if (LobbyBuildIndex < 0)
            Debug.LogWarning($"[Room] 대기실 씬 '{lobbySceneName}'이 빌드 설정에 없어");
        for (int i = 0; i < maps.Count; i++)
            if (MapBuildIndex(i) < 0)
                Debug.LogWarning($"[Room] 맵 '{maps[i].Name}'의 씬 '{maps[i].SceneName}'이 빌드 설정에 없어");

        ConnectLobby();
    }

    static string MakeUserId()
    {
#if UNITY_EDITOR
        return Guid.NewGuid().ToString("N").Substring(0, 24);
#else
        string id = PlayerPrefs.GetString(UidKey, "");
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 24);
            PlayerPrefs.SetString(UidKey, id);
        }
        return id;
#endif
    }

    void Update()
    {
        // 방에 들어왔는데 방 상태가 안 오면 (만든 사람이 바로 나간 경우 등) 방장이 새로 만듦
        if (State == LinkState.InRoom && _runner != null && RoomState.Instance == null)
        {
            _roomWait += Time.unscaledDeltaTime;
            if (_roomWait > 3f && _runner.IsSharedModeMasterClient)
            {
                _roomWait = -999f;
                SpawnRoomState($"{Nick}의 방", defaultSettings.Clone());
            }
        }

        if (State == LinkState.Offline && _reconnectAt > 0f && Time.unscaledTime >= _reconnectAt)
        {
            _reconnectAt = 0f;
            ConnectLobby();
        }
    }

    // ══════════════════ 로비 ══════════════════

    public async void ConnectLobby()
    {
        if (_runner != null) return;

        State = LinkState.ConnectingLobby;
        var runner = NewRunner();
        var r = await runner.JoinSessionLobby(SessionLobby.Shared);

        if (runner != _runner) return;   // 그 사이 정리됨
        if (r.Ok)
        {
            State = LinkState.InLobby;
        }
        else
        {
            DropRunner();
            State = LinkState.Offline;
            Toast($"서버 연결 실패 ({r.ShutdownReason})");
        }
    }

    NetworkRunner NewRunner()
    {
        _runnerGo = new GameObject("NetworkRunner");
        DontDestroyOnLoad(_runnerGo);
        _runner = _runnerGo.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);
        _sceneMgr = _runnerGo.AddComponent<NetworkSceneManagerDefault>();
        return _runner;
    }

    void DropRunner()
    {
        if (_runnerGo != null) Destroy(_runnerGo);
        _runnerGo = null;
        _runner = null;
        _sceneMgr = null;
        _sessions.Clear();
    }

    // ══════════════════ 방 만들기 · 참가 ══════════════════

    public void CreateRoom(string roomName, bool hidden)
    {
        var s = defaultSettings.Clone();
        s.Hidden = hidden;
        s.Clamp(hardMaxPlayers, MapCount);
        StartSession(RandomCode(), true, roomName, s);
    }

    /// <summary>목록에서 고르거나 코드를 입력해서 참가</summary>
    public void JoinRoom(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        StartSession(code.Trim().ToUpperInvariant(), false, null, null);
    }

    async void StartSession(string code, bool create, string roomName, RoomSettings s)
    {
        if (State != LinkState.InLobby || _runner == null) return;

        State = LinkState.Joining;
        var runner = _runner;

        // 보이스 — 기존 BasicSpawner와 같은 방식
        var voice = _runnerGo.AddComponent<Photon.Voice.Fusion.FusionVoiceClient>();
        runner.AddCallbacks(voice);

        var args = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = code,
            PlayerCount = hardMaxPlayers,
            EnableClientSessionCreation = create,   // 참가는 없는 방을 새로 만들지 않게
            SceneManager = _sceneMgr,
        };
        if (create)
        {
            args.IsVisible = !s.Hidden;
            args.IsOpen = true;
            args.SessionProperties = MakeProps(roomName, s.MaxPlayers, s.MapIndex, 0);
        }

        var r = await runner.StartGame(args);

        if (runner != _runner) return;   // OnShutdown이 이미 처리함
        if (!r.Ok)
        {
            HandleRunnerGone(runner, r.ShutdownReason);
            return;
        }

        State = LinkState.InRoom;
        _roomWait = 0f;
        Debug.Log($"[Room] 방 입장 — 코드 {code}");

        if (create && runner.IsSharedModeMasterClient)
            SpawnRoomState(roomName, s);
    }

    void SpawnRoomState(string roomName, RoomSettings s)
    {
        if (roomStatePrefab == null)
        {
            Debug.LogError("[Room] RoomLauncher의 Room State Prefab이 비어 있어");
            return;
        }
        s.Clamp(hardMaxPlayers, MapCount);
        _runner.Spawn(roomStatePrefab,
            onBeforeSpawned: (r, o) => o.GetComponent<RoomState>().InitAsHost(roomName, s));
    }

    // ══════════════════ 나가기 ══════════════════

    public async void Leave(string msg = null)
    {
        if (_runner == null || State == LinkState.Leaving) return;

        _purposeLeave = true;
        State = LinkState.Leaving;
        if (!string.IsNullOrEmpty(msg)) Toast(msg);

        var runner = _runner;
        await runner.Shutdown();
        HandleRunnerGone(runner, ShutdownReason.Ok);   // OnShutdown이 먼저 처리했으면 무시됨
    }

    /// <summary>러너가 끝났을 때 (나가기 · 끊김 · 참가 실패) — 대기실 씬으로 돌아가 로비 다시 접속</summary>
    void HandleRunnerGone(NetworkRunner runner, ShutdownReason reason)
    {
        if (runner != _runner) return;

        bool wasJoining = State == LinkState.Joining;
        bool purpose = _purposeLeave;
        _purposeLeave = false;

        DropRunner();
        State = LinkState.Offline;
        Spectator.End();

        if (wasJoining) Toast(JoinFailText(reason));
        else if (!purpose && reason != ShutdownReason.Ok) Toast($"연결이 끊겼어 ({reason})");

        if (SceneManager.GetActiveScene().name != lobbySceneName && LobbyBuildIndex >= 0)
            SceneManager.LoadScene(lobbySceneName);

        _reconnectAt = Time.unscaledTime + 0.5f;
    }

    static string JoinFailText(ShutdownReason r)
    {
        switch (r.ToString())
        {
            case "GameNotFound":
            case "GameDoesNotExist":
                return "그 코드의 방이 없어";
            case "GameIsFull":
                return "방이 가득 찼어";
            case "GameClosed":
                return "들어갈 수 없는 방이야";
            default:
                return $"참가 실패 ({r})";
        }
    }

    public void Toast(string msg)
    {
        Message = msg ?? "";
        MessageTime = Time.unscaledTime;
        if (!string.IsNullOrEmpty(msg)) Debug.Log($"[Room] {msg}");
    }

    // ══════════════════ 맵 · 씬 ══════════════════

    public string MapName(int i) =>
        i >= 0 && i < maps.Count && !string.IsNullOrEmpty(maps[i].Name) ? maps[i].Name : $"맵 {i + 1}";

    public int MapBuildIndex(int i) =>
        i >= 0 && i < maps.Count ? BuildIndexOf(maps[i].SceneName) : -1;

    public int LobbyBuildIndex => BuildIndexOf(lobbySceneName);

    static int BuildIndexOf(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return -1;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName) return i;
        }
        return -1;
    }

    // ══════════════════ 방 목록 정보 ══════════════════

    /// <summary>방 목록에 보이는 작은 값들 — n 이름 · m 최대 · map 맵 · st 0 대기/1 게임 중</summary>
    public static Dictionary<string, SessionProperty> MakeProps(string name, int max, int map, int phase) => new()
    {
        ["n"] = name ?? "",
        ["m"] = max,
        ["map"] = map,
        ["st"] = phase,
    };

    public static int PropInt(SessionInfo si, string key, int def)
    {
        if (si.Properties != null && si.Properties.TryGetValue(key, out var p) && p.PropertyValue is int v)
            return v;
        return def;
    }

    public static string PropStr(SessionInfo si, string key, string def)
    {
        if (si.Properties != null && si.Properties.TryGetValue(key, out var p) && p.PropertyValue is string v
            && !string.IsNullOrEmpty(v))
            return v;
        return def;
    }

    static string RandomCode()
    {
        var c = new char[6];
        for (int i = 0; i < c.Length; i++)
            c[i] = CodeChars[UnityEngine.Random.Range(0, CodeChars.Length)];
        return new string(c);
    }

    // ══════════════════ 러너 신호 ══════════════════

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        if (runner != _runner) return;
        _sessions.Clear();
        _sessions.AddRange(sessionList);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason reason) => HandleRunnerGone(runner, reason);

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}
