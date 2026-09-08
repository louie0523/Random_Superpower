using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkObject playerPrefab;
    private NetworkRunner _runner;

    [Header("Debug")]
    [SerializeField] private KeyCode spawnDummyKey = KeyCode.F5;
    [SerializeField] private float dummySpawnDistance = 5f;

    private readonly List<NetworkObject> _dummies = new();

    async void StartGame()
    {
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
        var sceneInfo = new NetworkSceneInfo();
        if (scene.IsValid)
            sceneInfo.AddSceneRef(scene, LoadSceneMode.Additive);

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = "TestRoom",
            Scene = sceneInfo,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        Debug.Log(result.Ok ? "접속 성공" : $"접속 실패: {result.ShutdownReason}");
    }

    void OnGUI()
    {
        if (_runner == null && GUI.Button(new Rect(20, 20, 200, 60), "접속"))
            StartGame();
    }

    void Update()
    {
        if (_runner == null || !_runner.IsRunning) return;

        if (Input.GetKeyDown(spawnDummyKey))
            SpawnDummy();

        if (Input.GetKeyDown(KeyCode.F6) && _dummies.Count > 0)
        {
            int last = _dummies.Count - 1;
            if (_dummies[last] != null) _runner.Despawn(_dummies[last]);
            _dummies.RemoveAt(last);
        }
    }

    void SpawnDummy()
    {
        var localObj = _runner.GetPlayerObject(_runner.LocalPlayer);
        if (localObj == null) return;

        Vector3 pos = localObj.transform.position
                    + localObj.transform.forward * dummySpawnDistance;

        var dummy = _runner.Spawn(playerPrefab, pos, Quaternion.identity);
        dummy.gameObject.name = "Dummy";

        // 입력 계열 비활성화
        if (dummy.TryGetComponent<PlayerMovement>(out var mv)) mv.enabled = false;
        if (dummy.TryGetComponent<PlayerAttack>(out var at)) at.enabled = false;
        if (dummy.TryGetComponent<PlayerSkillInput>(out var si)) si.enabled = false;
        if (dummy.TryGetComponent<AbilityRuntime>(out var ab)) ab.enabled = false;

        // 카메라 끄기
        var cam = dummy.GetComponentInChildren<Camera>(true);
        if (cam != null) cam.gameObject.SetActive(false);

        _dummies.Add(dummy);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // Shared Mode에선 각자 자기 캐릭터만 스폰
        if (player == runner.LocalPlayer)
        {
            Vector3 pos = new Vector3(UnityEngine.Random.Range(-4f, 4f), 1f,
                                      UnityEngine.Random.Range(-4f, 4f));
            var obj = runner.Spawn(playerPrefab, pos, Quaternion.identity, player);
            runner.SetPlayerObject(player, obj);   // ← 이 줄
        }
    }

    // --- 아래는 전부 빈 스텁 ---
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason reason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException();
    }
}