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

    public override void Spawned()
    {
        _inst = this;

        if (HasStateAuthority)
        {
            GameOver = false;
            Winner = PlayerRef.None;
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
        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null) continue;
            if (!obj.TryGetComponent<PlayerHealth>(out var h)) continue;

            h.ResetForNewRound();
            if (obj.TryGetComponent<PlayerShop>(out var shop)) shop.ResetForNewRound();
        }

        GameOver = false;
        Winner = PlayerRef.None;
        WinnerSoul = 0;

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