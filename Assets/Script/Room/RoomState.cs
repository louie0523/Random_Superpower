using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum RoomPhase : byte
{
    Waiting = 0,    // 대기실
    Playing = 1,    // 게임 중
}

/// <summary>대기실 명단 한 줄</summary>
public struct RoomPlayer : INetworkStruct
{
    public PlayerRef Ref;
    public int Order;                  // 들어온 순서 (0 = 빈칸)
    public NetworkBool Ready;
    public NetworkBool InGame;         // 이번 게임 참가자 (false면 옵저버)
    public NetworkString<_32> Name;
    public NetworkString<_32> Uid;     // 강퇴 기록용 (기기마다 고정)
}

/// <summary>
/// 방 하나의 공유 상태 — 명단 · 준비 · 설정 · 진행 단계.
/// 권한자 = 방장(마스터 클라이언트). 방장이 나가면 Photon이 다음 마스터(먼저 들어온 사람)에게 넘김.
/// 씬이 바뀌어도 남음 (대기실 ↔ 게임 씬).
/// 프리팹 NetworkObject: Is Master Client Object 켬 · Destroy When State Authority Leaves 끔.
/// </summary>
public class RoomState : NetworkBehaviour
{
    public const int MaxSlots = 16;
    public const int MaxBanClass = 64;
    public const int MaxBanRandom = 128;

    static RoomState _inst;
    public static RoomState Instance =>
        _inst != null && _inst.Object != null && _inst.Object.IsValid ? _inst : null;

    // ── 진행 ──
    [Networked, OnChangedRender(nameof(OnPhaseChanged))] public RoomPhase Phase { get; set; }
    [Networked] public NetworkString<_32> RoomName { get; set; }
    [Networked, Capacity(MaxSlots)] public NetworkArray<RoomPlayer> Players => default;
    [Networked] int NextOrder { get; set; }
    [Networked, Capacity(16)] NetworkArray<NetworkString<_32>> Kicked => default;
    [Networked] int KickedNext { get; set; }
    [Networked] TickTimer SceneTimer { get; set; }
    [Networked] int SceneToLoad { get; set; }

    // ── 설정 ──
    [Networked] public int SettingsVersion { get; set; }
    [Networked] public int MaxPlayers { get; set; }
    [Networked] public int MapIndex { get; set; }
    [Networked] public int RoundSeconds { get; set; }
    [Networked] public int StartSoul { get; set; }
    [Networked] public int SubAltarCost { get; set; }
    [Networked] public int CentralAltarCost { get; set; }
    [Networked] public int AltarsNeeded { get; set; }
    [Networked] public NetworkBool AltarsNeedAll { get; set; }
    [Networked] public int CentralChannelSeconds { get; set; }
    [Networked] public float ChestRefillSeconds { get; set; }
    [Networked] public int FallRuleValue { get; set; }
    [Networked] public NetworkBool TeamMode { get; set; }
    [Networked] public NetworkBool Hidden { get; set; }
    [Networked, Capacity(MaxBanClass)] NetworkArray<int> BanClass => default;
    [Networked, Capacity(MaxBanRandom)] NetworkArray<int> BanRandom => default;

    RoomSettings _cache;
    int _cacheVer = -1;
    float _nextRegister;
    int _lastPublish;

    // ══════════════════ 생성 ══════════════════

    /// <summary>방 만든 사람이 Spawn 직전에 부름 (onBeforeSpawned)</summary>
    public void InitAsHost(string roomName, RoomSettings s)
    {
        RoomName = Clip(roomName, 24);
        Phase = RoomPhase.Waiting;
        SceneToLoad = -1;
        WriteSettings(s);
    }

    public override void Spawned()
    {
        _inst = this;
        Runner.MakeDontDestroyOnLoad(gameObject);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_inst == this) _inst = null;
    }

    // ══════════════════ 매 틱 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        Prune();

        if (SceneTimer.Expired(Runner))
        {
            SceneTimer = TickTimer.None;
            if (SceneToLoad >= 0)
                Runner.LoadScene(SceneRef.FromIndex(SceneToLoad), LoadSceneMode.Single);
            SceneToLoad = -1;
        }

        if (Runner.Tick % 15 == 0) Publish();
    }

    /// <summary>나간 사람 명단에서 빼기 (방장이 바뀐 직후에도 맞게)</summary>
    void Prune()
    {
        for (int i = 0; i < Players.Length; i++)
        {
            var e = Players[i];
            if (e.Order == 0) continue;
            if (!IsActive(e.Ref)) Players.Set(i, default);
        }
    }

    bool IsActive(PlayerRef p)
    {
        foreach (var a in Runner.ActivePlayers)
            if (a == p) return true;
        return false;
    }

    /// <summary>방 목록에 보이는 정보 갱신 (바뀐 게 있을 때만)</summary>
    void Publish()
    {
        var si = Runner.SessionInfo;
        if (si == null) return;

        bool open = PlayerCount < MaxPlayers;
        string name = RoomName.ToString();
        int hash = System.HashCode.Combine(name, MaxPlayers, MapIndex, (int)Phase, (bool)Hidden, open);
        if (hash == _lastPublish) return;
        _lastPublish = hash;

        si.IsVisible = !Hidden;
        si.IsOpen = open;
        si.UpdateCustomProperties(RoomLauncher.MakeProps(name, MaxPlayers, MapIndex, (int)Phase));
    }

    // 내 등록 — 방장에게 RPC가 안 닿았을 수도 있으니 명단에 없으면 다시 보냄
    void Update()
    {
        if (Object == null || !Object.IsValid || Runner == null) return;
        if (Find(Runner.LocalPlayer) >= 0) return;
        if (Time.unscaledTime < _nextRegister) return;

        _nextRegister = Time.unscaledTime + 1.5f;
        RPC_Register(Runner.LocalPlayer, RoomLauncher.Nick, RoomLauncher.UserId);
    }

    // ══════════════════ 명단 ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Register(PlayerRef p, string name, string uid)
    {
        if (Find(p) >= 0) return;
        if (IsKicked(uid)) { RPC_Removed(p, 1); return; }
        if (PlayerCount >= MaxPlayers) { RPC_Removed(p, 2); return; }

        int slot = -1;
        for (int i = 0; i < Players.Length; i++)
            if (Players[i].Order == 0) { slot = i; break; }
        if (slot < 0) { RPC_Removed(p, 2); return; }

        if (string.IsNullOrWhiteSpace(name)) name = $"플레이어 {p.PlayerId}";

        NextOrder++;
        Players.Set(slot, new RoomPlayer
        {
            Ref = p,
            Order = NextOrder,
            Name = Clip(name.Trim(), 16),
            Uid = Clip(uid, 30),
            InGame = false,   // 게임 중에 들어오면 옵저버
        });
    }

    /// <summary>reason 1 = 강퇴, 2 = 가득 참</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_Removed(PlayerRef target, int reason)
    {
        if (target != Runner.LocalPlayer) return;
        if (RoomLauncher.Instance != null)
            RoomLauncher.Instance.Leave(reason == 1 ? "방장이 내보냈어" : "방이 가득 찼어");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetReady(PlayerRef p, NetworkBool ready)
    {
        if (Phase != RoomPhase.Waiting) return;
        int i = Find(p);
        if (i < 0) return;
        var e = Players[i];
        e.Ready = ready;
        Players.Set(i, e);
    }

    // ══════════════════ 방장 전용 ══════════════════

    public bool IsLocalHost => Object != null && Object.IsValid && HasStateAuthority;
    public PlayerRef Host => Object.StateAuthority;

    public void Kick(PlayerRef p)
    {
        if (!HasStateAuthority || Phase != RoomPhase.Waiting) return;
        if (p == Runner.LocalPlayer) return;
        int i = Find(p);
        if (i < 0) return;

        Kicked.Set(KickedNext % Kicked.Length, Players[i].Uid);
        KickedNext++;
        Players.Set(i, default);
        RPC_Removed(p, 1);
    }

    /// <summary>설정 바꾸기 — 방장 · 대기실에서만</summary>
    public bool ApplySettings(RoomSettings s)
    {
        if (!HasStateAuthority || Phase != RoomPhase.Waiting || s == null) return false;

        var L = RoomLauncher.Instance;
        s.Clamp(L != null ? L.HardMaxPlayers : MaxSlots, L != null ? L.MapCount : 1);
        if (s.MaxPlayers < PlayerCount) s.MaxPlayers = PlayerCount;   // 지금 인원보다 작게는 못 함

        WriteSettings(s);
        return true;
    }

    public bool CanStart(out string why)
    {
        why = "";
        if (!HasStateAuthority) { why = "방장만 시작할 수 있어"; return false; }
        if (Phase != RoomPhase.Waiting || SceneTimer.IsRunning) { why = "준비 중"; return false; }

        var L = RoomLauncher.Instance;
        if (L == null) { why = "RoomLauncher 없음"; return false; }
        if (L.MapBuildIndex(MapIndex) < 0) { why = "맵 씬이 빌드 설정에 없어"; return false; }

        if (L.RequireAllReady)
        {
            int n = NotReadyCount();
            if (n > 0) { why = $"준비 안 한 사람 {n}명"; return false; }
        }
        return true;
    }

    public bool StartGame()
    {
        if (!CanStart(out var why))
        {
            Debug.Log($"[Room] 시작 못 함: {why}");
            return false;
        }

        for (int i = 0; i < Players.Length; i++)
        {
            var e = Players[i];
            if (e.Order == 0) continue;
            e.InGame = true;
            e.Ready = false;
            Players.Set(i, e);
        }

        Phase = RoomPhase.Playing;
        LoadSoon(RoomLauncher.Instance.MapBuildIndex(MapIndex), 0.2f);
        Debug.Log("[Room] 게임 시작");
        return true;
    }

    /// <summary>게임 끝 → 모두 대기실로 (RoundManager가 부름)</summary>
    public void ReturnToRoom()
    {
        if (!HasStateAuthority || Phase != RoomPhase.Playing) return;

        for (int i = 0; i < Players.Length; i++)
        {
            var e = Players[i];
            if (e.Order == 0) continue;
            e.InGame = false;
            e.Ready = false;
            Players.Set(i, e);
        }

        Phase = RoomPhase.Waiting;
        LoadSoon(RoomLauncher.Instance != null ? RoomLauncher.Instance.LobbyBuildIndex : 0, 0.5f);
        Debug.Log("[Room] 대기실로 복귀");
    }

    void LoadSoon(int buildIndex, float delay)
    {
        SceneToLoad = buildIndex;
        SceneTimer = TickTimer.CreateFromSeconds(Runner, delay);
    }

    // 대기실로 돌아갈 때 각자 자기 캐릭터 정리 (씬이 바뀌기 전에)
    void OnPhaseChanged()
    {
        if (Phase != RoomPhase.Waiting) return;

        Spectator.End();
        var po = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (po != null && po.HasStateAuthority) Runner.Despawn(po);
    }

    // ══════════════════ 조회 ══════════════════

    public int Find(PlayerRef p)
    {
        for (int i = 0; i < Players.Length; i++)
        {
            var e = Players[i];
            if (e.Order > 0 && e.Ref == p) return i;
        }
        return -1;
    }

    public int PlayerCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Players.Length; i++)
                if (Players[i].Order > 0) n++;
            return n;
        }
    }

    public bool IsInGame(PlayerRef p)
    {
        int i = Find(p);
        return i >= 0 && Players[i].InGame;
    }

    public bool IsReady(PlayerRef p)
    {
        int i = Find(p);
        return i >= 0 && Players[i].Ready;
    }

    public string NameOf(PlayerRef p)
    {
        int i = Find(p);
        return i >= 0 ? Players[i].Name.ToString() : $"플레이어 {p.PlayerId}";
    }

    public int NotReadyCount()
    {
        int n = 0;
        var host = Host;
        for (int i = 0; i < Players.Length; i++)
        {
            var e = Players[i];
            if (e.Order == 0 || e.Ref == host) continue;
            if (!e.Ready) n++;
        }
        return n;
    }

    /// <summary>들어온 순서대로 명단</summary>
    public void SortedPlayers(List<RoomPlayer> buf)
    {
        buf.Clear();
        for (int i = 0; i < Players.Length; i++)
            if (Players[i].Order > 0) buf.Add(Players[i]);
        buf.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    bool IsKicked(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return false;
        string u = Clip(uid, 30);
        for (int i = 0; i < Kicked.Length; i++)
            if (Kicked[i].ToString() == u) return true;
        return false;
    }

    public bool IsBanned(int abilityId)
    {
        if (abilityId == 0) return false;
        for (int i = 0; i < BanClass.Length; i++)
            if (BanClass[i] == abilityId) return true;
        for (int i = 0; i < BanRandom.Length; i++)
            if (BanRandom[i] == abilityId) return true;
        return false;
    }

    // ══════════════════ 설정 읽기 · 쓰기 ══════════════════

    /// <summary>현재 설정 (읽기 전용으로 쓸 것 — 고치려면 Clone)</summary>
    public RoomSettings Settings
    {
        get
        {
            if (_cache == null || _cacheVer != SettingsVersion)
            {
                _cache = BuildSettings();
                _cacheVer = SettingsVersion;
            }
            return _cache;
        }
    }

    RoomSettings BuildSettings()
    {
        var s = new RoomSettings
        {
            MaxPlayers = MaxPlayers,
            MapIndex = MapIndex,
            RoundSeconds = RoundSeconds,
            StartSoul = StartSoul,
            SubAltarCost = SubAltarCost,
            CentralAltarCost = CentralAltarCost,
            AltarsNeeded = AltarsNeeded,
            AltarsNeedAll = AltarsNeedAll,
            CentralChannelSeconds = CentralChannelSeconds,
            ChestRefillSeconds = ChestRefillSeconds,
            Fall = (FallRule)FallRuleValue,
            TeamMode = TeamMode,
            Hidden = Hidden,
        };
        for (int i = 0; i < BanClass.Length; i++)
            if (BanClass[i] != 0) s.BanClass.Add(BanClass[i]);
        for (int i = 0; i < BanRandom.Length; i++)
            if (BanRandom[i] != 0) s.BanRandom.Add(BanRandom[i]);
        return s;
    }

    void WriteSettings(RoomSettings s)
    {
        MaxPlayers = s.MaxPlayers;
        MapIndex = s.MapIndex;
        RoundSeconds = s.RoundSeconds;
        StartSoul = s.StartSoul;
        SubAltarCost = s.SubAltarCost;
        CentralAltarCost = s.CentralAltarCost;
        AltarsNeeded = s.AltarsNeeded;
        AltarsNeedAll = s.AltarsNeedAll;
        CentralChannelSeconds = s.CentralChannelSeconds;
        ChestRefillSeconds = s.ChestRefillSeconds;
        FallRuleValue = (int)s.Fall;
        TeamMode = s.TeamMode;
        Hidden = s.Hidden;

        var bc = BanClass;
        for (int i = 0; i < bc.Length; i++)
            bc.Set(i, i < s.BanClass.Count ? s.BanClass[i] : 0);
        var br = BanRandom;
        for (int i = 0; i < br.Length; i++)
            br.Set(i, i < s.BanRandom.Count ? s.BanRandom[i] : 0);

        SettingsVersion++;
    }

    static string Clip(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length > n ? s.Substring(0, n) : s;
    }
}
