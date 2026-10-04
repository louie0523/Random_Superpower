using Fusion;
using UnityEngine;

public class RoundManager : NetworkBehaviour
{
    [Header("승리 조건")]
    [Tooltip("이 소울에 도달하면 승리")]
    [SerializeField] private int targetSoul = 150;

    [Tooltip("승리 후 재시작까지 (초)")]
    [SerializeField] private float resultDuration = 8f;

    [Networked] public NetworkBool GameOver { get; set; }
    [Networked] public PlayerRef Winner { get; set; }
    [Networked] public int WinnerSoul { get; set; }
    [Networked] private TickTimer RestartTimer { get; set; }

    public int TargetSoul => targetSoul;

    static RoundManager _inst;
    public static RoundManager Instance => _inst;

    [Tooltip("재시작 직후 승리 판정을 쉬는 시간 (초기화 RPC가 반영될 때까지)")]
    [SerializeField] private float restartGrace = 2f;

    [Networked] public float RoundStartTime { get; set; }

    public float RoundMinutes =>
        Object != null && Object.IsValid
            ? Mathf.Max(0f, (Runner.SimulationTime - RoundStartTime) / 60f)
            : 0f;

    [Networked] private TickTimer GraceTimer { get; set; }

    public override void Spawned()
    {
        _inst = this;

        if (HasStateAuthority)
        {
            GameOver = false;
            Winner = PlayerRef.None;
            RoundStartTime = Runner.SimulationTime;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (GameOver)
        {
            if (RestartTimer.Expired(Runner)) Restart();
            return;
        }

        if (!GraceTimer.ExpiredOrNotRunning(Runner)) return;   // ★ 재시작 직후 대기

        CheckWin();
    }

    // ══════════════════ 승리 판정 ══════════════════

    void CheckWin()
    {
        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null) continue;
            if (!obj.TryGetComponent<PlayerHealth>(out var h)) continue;

            if (h.Soul < targetSoul) continue;

            EndRound(p, h.Soul);
            return;
        }
    }

    void EndRound(PlayerRef winner, int soul)
    {
        GameOver = true;
        Winner = winner;
        WinnerSoul = soul;
        RestartTimer = TickTimer.CreateFromSeconds(Runner, resultDuration);

        Debug.Log($"[Round] 승리 — Player {winner.PlayerId} ({soul} 소울)");
    }

    // ══════════════════ 재시작 ══════════════════

    void Restart()
    {
        RoundStartTime = Runner.SimulationTime;

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
        GraceTimer = TickTimer.CreateFromSeconds(Runner, restartGrace);   // ★

        Debug.Log("[Round] 새 라운드 시작");
    }

    // ══════════════════ 조회 ══════════════════

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