using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 씬의 스폰 담당.
/// - 방 모드: 대기실(RoomLauncher)에서 들어온 경우 — 그 러너를 그대로 쓰고, 참가자면 내 캐릭터 스폰 / 아니면 옵저버
/// - 직접 모드: 게임 씬에서 바로 실행한 경우 — 예전처럼 '접속' 버튼 (테스트용)
/// </summary>
public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkObject playerPrefab;
    private NetworkRunner _runner;

    [SerializeField] private float dummySpawnDistance = 5f;
    [SerializeField] private AlterData debugAlter;

    [Header("Debug Keys")]
    [SerializeField] private KeyCode StoneGet = KeyCode.Keypad3;
    [SerializeField] private KeyCode spawnDummyKey = KeyCode.F5;
    [SerializeField] private KeyCode clearDummyKey = KeyCode.F6;
    [SerializeField] private KeyCode rerollAbilityKey = KeyCode.F7;
    [SerializeField] private KeyCode suicideKey = KeyCode.F8;
    [SerializeField] private KeyCode healKey = KeyCode.F9;
    [SerializeField] private KeyCode soulGainKey = KeyCode.F1;
    [SerializeField] private KeyCode soulLoseKey = KeyCode.F2;
    [SerializeField] private KeyCode AddAlter = KeyCode.Keypad4;
    [SerializeField] private KeyCode RemoveAlter = KeyCode.Keypad5;
    [SerializeField] private int soulCheatAmount = 50;
    NetworkObject GetLocal() => _runner.GetPlayerObject(_runner.LocalPlayer);

    private readonly List<NetworkObject> _dummies = new();

    // 방 모드
    bool _roomMode;
    bool _roomDecided;

    void Awake()
    {
        if (!SceneManager.GetSceneByName("UI").isLoaded)
            SceneManager.LoadScene("UI", LoadSceneMode.Additive);

        SpawnPoint.GroundMask = ~LayerMask.GetMask("Player");
    }

    void Start()
    {
        // 대기실에서 넘어왔으면 그 러너를 이어받음
        var r = RoomLauncher.Runner;
        if (r != null && r.IsRunning)
        {
            _runner = r;
            _runner.AddCallbacks(this);
            _roomMode = true;
        }
    }

    void OnDestroy()
    {
        if (_roomMode && _runner != null) _runner.RemoveCallbacks(this);
    }

    async void StartGame()
    {
        _runner = gameObject.AddComponent<NetworkRunner>();
        var voice = gameObject.AddComponent<Photon.Voice.Fusion.FusionVoiceClient>();
        _runner.AddCallbacks(voice);          // 추가 — 러너 신호(입장 · 퇴장)를 보이스 클라이언트도 받게
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
        if (_roomMode || RoomLauncher.Instance != null) return;   // 방 모드에선 버튼 없음
        if (_runner == null && GUI.Button(new Rect(20, 20, 200, 60), "접속"))
            StartGame();
    }

    void Update()
    {
        if (_runner == null || !_runner.IsRunning) return;

        if (_roomMode && !_roomDecided) TryRoomSpawn();

        if (Input.GetKeyDown(spawnDummyKey)) SpawnDummy();

        if (Input.GetKeyDown(clearDummyKey) && _dummies.Count > 0)
        {
            int last = _dummies.Count - 1;
            if (_dummies[last] != null) _runner.Despawn(_dummies[last]);
            _dummies.RemoveAt(last);
        }

        //if (Input.GetKeyDown(rerollAbilityKey)) RerollAbility();
        //if (Input.GetKeyDown(suicideKey)) Suicide();
        //if (Input.GetKeyDown(healKey)) FullHeal();
        //if (Input.GetKeyDown(soulGainKey)) GainSoul(soulCheatAmount); 
        //if (Input.GetKeyDown(soulLoseKey)) GainSoul(-soulCheatAmount / 2);
        //if (Input.GetKeyDown(KeyCode.F10)) StatusSelf(StatusType.Frost, 15f, 3f);
        //if (Input.GetKeyDown(KeyCode.F11)) StatusSelf(StatusType.Poison, 5f, 4f);
        //if (Input.GetKeyDown(KeyCode.F12)) StatusSelf(StatusType.Stun, 2f, 2f);
        //if (Input.GetKeyDown(StoneGet)) StoneGive(100);
        //if (Input.GetKeyDown(KeyCode.Alpha9)) GetLocal()?.GetComponent<PlayerAlter>()?.Add(debugAlter);
        //if (Input.GetKeyDown(KeyCode.Alpha8)) GetLocal()?.GetComponent<PlayerAlter>()?.Remove(debugAlter, true);

    }

    // ══════════════════ 방 모드 스폰 ══════════════════

    /// <summary>게임 씬이 열리고 방 상태가 '게임 중'이면 — 참가자는 캐릭터 스폰, 아니면 옵저버</summary>
    void TryRoomSpawn()
    {
        var rs = RoomState.Instance;
        if (rs == null || Time.timeSinceLevelLoad < 0.3f) return;
        if (rs.Phase != RoomPhase.Playing) return;   // 대기실로 돌아가는 중이면 아무것도 안 함

        _roomDecided = true;

        if (_runner.GetPlayerObject(_runner.LocalPlayer) != null) return;

        if (rs.IsInGame(_runner.LocalPlayer)) SpawnLocal(_runner, _runner.LocalPlayer);
        else Spectator.Begin();
    }

    void SpawnLocal(NetworkRunner runner, PlayerRef player)
    {
        var body = playerPrefab.GetComponent<CharacterController>();

        if (!SpawnPoint.TryPick(runner, player, 15f, out var pos, out var rot, body))
        {
            pos = SpawnPoint.GroundedPosition(
                new Vector3(UnityEngine.Random.Range(-4f, 4f), 0f, UnityEngine.Random.Range(-4f, 4f)), body);
            rot = Quaternion.identity;
        }

        var obj = runner.Spawn(playerPrefab, pos, rot, player);
        runner.SetPlayerObject(player, obj);      // ★ 이게 빠지면 GetPlayerObject가 전부 null
    }

    // ══════════════════ 디버그 ══════════════════

    void StoneGive(int num)
    {
        var local = GetLocal();
        if (local == null) return;
        if (!local.TryGetComponent<PlayerEnchant>(out var enchant)) return;

        enchant.RPC_AddStones(num);
        Debug.Log($"[Debug] 스톤 +{num}");
    }

    void StatusSelf(StatusType t, float v, float d) =>
    GetLocal()?.GetComponent<PlayerStatus>()?.Apply(t, v, d, PlayerRef.None);

    void RerollAbility()
    {
        var local = GetLocal();
        if (local == null) return;
        if (!local.TryGetComponent<AbilityRuntime>(out var ar)) return;

        ar.RerollRandomAbility();
        Debug.Log($"[Debug] 랜덤 능력 교체 → {ar.GetRandomAbility()?.DisplayName ?? "없음"}");
    }

    void GainSoul(int amount)
    {
        var local = GetLocal();
        if (local == null) return;
        if (!local.TryGetComponent<PlayerHealth>(out var h)) return;

        h.RPC_GainSoul(amount);
        Debug.Log($"[Debug] 소울 {amount:+#;-#;0}");
    }

    void Suicide()
    {
        var local = GetLocal();
        if (local == null) return;
        if (!local.TryGetComponent<PlayerHealth>(out var h)) return;
        if (h.IsDead) return;

        h.RPC_TakeDamage(DamageInfo.FromEnvironment(100, local.transform.position));
        Debug.Log("[Debug] 자해");
    }

    void FullHeal()
    {
        var local = GetLocal();
        if (local == null) return;
        if (!local.TryGetComponent<PlayerHealth>(out var h)) return;

        h.RPC_Heal(9999);
        Debug.Log("[Debug] 체력 회복");
    }

    void SpawnDummy()
    {
        var local = GetLocal();
        if (local == null) return;

        Vector3 pos = local.transform.position
                    + local.transform.forward * dummySpawnDistance;

        var dummy = _runner.Spawn(playerPrefab, pos, Quaternion.identity);
        dummy.gameObject.name = "Dummy";

        // 입력 계열 비활성화
        if (dummy.TryGetComponent<PlayerMovement>(out var mv)) mv.enabled = false;
        if (dummy.TryGetComponent<PlayerAttack>(out var at)) at.enabled = false;
        if (dummy.TryGetComponent<PlayerSkillInput>(out var si)) si.enabled = false;
        if (dummy.TryGetComponent<AbilityRuntime>(out var ab)) ab.enabled = false;

        // 카메라는 컴포넌트만 끔 (오브젝트를 끄면 팔까지 사라짐)
        var cam = dummy.GetComponentInChildren<Camera>(true);
        if (cam != null) cam.enabled = false;

        var listener = dummy.GetComponentInChildren<AudioListener>(true);
        if (listener != null) listener.enabled = false;

        var fps = dummy.GetComponentInChildren<FPSCamera>(true);
        if (fps != null) fps.enabled = false;

        _dummies.Add(dummy);
        Debug.Log("[Debug] 더미 스폰");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (_roomMode) return;                      // 방 모드는 TryRoomSpawn이 처리

        // Shared Mode에선 각자 자기 캐릭터만 스폰
        if (player != runner.LocalPlayer) return;
        SpawnLocal(runner, player);
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

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
}
