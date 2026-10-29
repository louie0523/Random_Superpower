using Fusion;
using UnityEngine;

public class RoundManager : NetworkBehaviour
{
    [Header("승리 조건 — 제단 (방 없이 바로 실행할 때 값 · 방에서 들어오면 방 설정이 우선)")]
    [Tooltip("서브 제단 하나 여는 데 드는 소울")]
    [SerializeField] private int subAltarCost = 35;

    [Tooltip("중앙 제단 여는 데 드는 소울 (서브보다 낮게)")]
    [SerializeField] private int centralAltarCost = 20;

    [Tooltip("중앙 제단을 열려면 열어야 하는 서브 제단 수")]
    [SerializeField] private int altarsNeeded = 4;

    [Tooltip("켜면 서브 제단을 전부 열어야 함 (위 숫자 무시)")]
    [SerializeField] private bool altarsNeedAll = false;

    [Tooltip("중앙 제단 채널 시간 (초)")]
    [SerializeField] private float centralChannelSeconds = 8f;

    [Header("라운드")]
    [Tooltip("승리 후 재시작(방 모드면 대기실 복귀)까지 (초)")]
    [SerializeField] private float resultDuration = 8f;

    [Tooltip("클래스 고르기를 기다리는 최대 시간 (초) — 이 시간이 지나면 고르는 중이어도 라운드 시간 시작")]
    [SerializeField] private float maxPickWait = 30f;

    [Tooltip("재시작 직후 승리 판정을 쉬는 시간 (초기화 RPC가 반영될 때까지)")]
    [SerializeField] private float restartGrace = 2f;

    [Networked] public NetworkBool GameOver { get; set; }
    [Networked] public PlayerRef Winner { get; set; }
    [Networked] public int WinnerSoul { get; set; }
    [Networked] private TickTimer RestartTimer { get; set; }

    /// <summary>라운드 번호 — 바뀌면 각자 제단 기록 초기화</summary>
    [Networked] public int RoundIndex { get; set; }

    [Networked] public float RoundStartTime { get; set; }
    [Networked] private TickTimer GraceTimer { get; set; }

    // 클래스 고르는 동안은 라운드 시간이 안 흐름
    [Networked] private NetworkBool WaitingForPick { get; set; }
    [Networked] private TickTimer PickCap { get; set; }

    static RoundManager _inst;
    public static RoundManager Instance => _inst;

    // ── 제단 기본값 (AltarRules가 읽음) ──
    public int DefaultSubAltarCost => subAltarCost;
    public int DefaultCentralAltarCost => centralAltarCost;
    public int DefaultAltarsNeeded => altarsNeeded;
    public bool DefaultAltarsNeedAll => altarsNeedAll;
    public float DefaultCentralChannel => centralChannelSeconds;

    /// <summary>중앙 제단을 열려면 필요한 서브 제단 수 (HUD용)</summary>
    public int AltarsNeeded => AltarRules.Needed;

    [System.Obsolete("승리 소울 없어짐 — AltarsNeeded / GetTopAltars() 쓰기")]
    public int TargetSoul => AltarsNeeded;

    public float RestartRemaining =>
        GameOver ? (RestartTimer.RemainingTime(Runner) ?? 0f) : 0f;

    public float RoundMinutes =>
        Object != null && Object.IsValid
            ? Mathf.Max(0f, (Runner.SimulationTime - RoundStartTime) / 60f)
            : 0f;

    /// <summary>라운드 제한 시간 (초). 0 = 무제한 (방 설정)</summary>
    public int RoundLimitSeconds => RoomRules.RoundSeconds(0);

    /// <summary>남은 라운드 시간 (초). 제한이 없으면 -1 (HUD용)</summary>
    public float RoundTimeLeft
    {
        get
        {
            int lim = RoundLimitSeconds;
            if (lim <= 0 || Object == null || !Object.IsValid) return -1f;
            if (WaitingForPick) return lim;
            return Mathf.Max(0f, lim - (Runner.SimulationTime - RoundStartTime));
        }
    }

    public override void Spawned()
    {
        _inst = this;

        if (HasStateAuthority)
        {
            GameOver = false;
            Winner = PlayerRef.None;
            RoundStartTime = Runner.SimulationTime;
            BeginPickWait();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_inst == this) _inst = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (GameOver)
        {
            if (RestartTimer.Expired(Runner))
            {
                if (RoomState.Instance != null)
                {
                    RestartTimer = TickTimer.None;
                    RoomState.Instance.ReturnToRoom();   // ★ 방 모드: 게임 끝 → 모두 대기실
                }
                else
                {
                    Restart();                           // 직접 모드: 예전처럼 반복
                }
            }
            return;
        }

        if (WaitingForPick)
        {
            RoundStartTime = Runner.SimulationTime;      // 고르는 동안 시간 안 흐름
            bool picked = GraceTimer.ExpiredOrNotRunning(Runner) && !ClassPick.AnyonePicking;
            if (picked || PickCap.Expired(Runner)) WaitingForPick = false;
            return;
        }

        if (!GraceTimer.ExpiredOrNotRunning(Runner)) return;   // ★ 재시작 직후 대기

        // 승리는 중앙 제단(PlayerAltars) · 전설의 용이 RPC_DeclareWinner로 알림. 여기선 시간만 봄
        int limit = RoundLimitSeconds;
        if (limit > 0 && Runner.SimulationTime - RoundStartTime >= limit)
            EndByTime();
    }

    void BeginPickWait()
    {
        WaitingForPick = true;
        GraceTimer = TickTimer.CreateFromSeconds(Runner, restartGrace);
        PickCap = TickTimer.CreateFromSeconds(Runner, maxPickWait);
    }

    // ══════════════════ 승리 판정 ══════════════════

    /// <summary>라운드 시간 끝 — 서브 제단 많이 연 사람, 같으면 소울 많은 사람</summary>
    void EndByTime()
    {
        PlayerRef best = PlayerRef.None;
        int bestAltars = -1, bestSoul = -1;

        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null) continue;                   // 옵저버
            if (!obj.TryGetComponent<PlayerHealth>(out var h)) continue;

            int a = PlayerAltars.CountOf(p);
            int s = h.Soul;
            if (a > bestAltars || (a == bestAltars && s > bestSoul))
            {
                bestAltars = a;
                bestSoul = s;
                best = p;
            }
        }

        Debug.Log($"[Round] 시간 끝 — 제단 {bestAltars}개");
        EndRound(best, Mathf.Max(0, bestSoul));
    }

    void EndRound(PlayerRef winner, int soul)
    {
        GameOver = true;
        Winner = winner;
        WinnerSoul = soul;
        RestartTimer = TickTimer.CreateFromSeconds(Runner, resultDuration);

        Debug.Log($"[Round] 승리 — Player {winner.PlayerId} ({soul} 소울)");
    }

    /// <summary>즉시 승리 선언 (중앙 제단 · 전설의 용) — 어디서 불러도 됨</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_DeclareWinner(PlayerRef winner)
    {
        if (GameOver) return;
        int soul = 0;
        var obj = Runner.GetPlayerObject(winner);
        if (obj != null && obj.TryGetComponent<PlayerHealth>(out var h)) soul = h.Soul;
        EndRound(winner, soul);
    }

    // ══════════════════ 재시작 (직접 모드) ══════════════════

    void Restart()
    {
        RoundStartTime = Runner.SimulationTime;
        RoundIndex++;                            // ★ 각자 제단 기록 초기화

        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null) continue;
            if (obj.TryGetComponent<PlayerHealth>(out var h))
                h.RPC_ResetForNewRound();        // ★ 직접 호출 대신 RPC
        }

        foreach (var c in FindObjectsByType<LootChest>(FindObjectsSortMode.None))
            c.ResetForNewRound();

        foreach (var pk in FindObjectsByType<Pickup>(FindObjectsSortMode.None))
            pk.RPC_DespawnForReset();

        //foreach (var sp in FindObjectsByType<PickupSpawner>(FindObjectsSortMode.None))
        //    sp.ResetForNewRound();

        GameOver = false;
        Winner = PlayerRef.None;
        WinnerSoul = 0;
        BeginPickWait();                          // ★ 재시작 직후 대기 + 클래스 고르기 대기

        Debug.Log("[Round] 새 라운드 시작");
    }

    // ══════════════════ 조회 ══════════════════

    /// <summary>가장 많이 연 서브 제단 수 (HUD용)</summary>
    public int GetTopAltars()
    {
        int top = 0;
        foreach (var a in PlayerAltars.All)
        {
            if (a == null || a.Object == null || !a.Object.IsValid || a.Owner == PlayerRef.None) continue;
            if (a.Count > top) top = a.Count;
        }
        return top;
    }

    /// <summary>현재 1등의 소울 (HUD용)</summary>
    public int GetTopSoul()
    {
        int top = 0;
        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null) continue;
            if (!obj.TryGetComponent<PlayerHealth>(out var h)) continue;
            if (h.Soul > top) top = h.Soul;
        }
        return top;
    }
}
