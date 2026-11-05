using UnityEngine;

/// <summary>판 끝날 때 보상 (임시값 — 여기서 고치기). 등수 = 제단 수 → 소울 순</summary>
[CreateAssetMenu(menuName = "Game/Meta/Reward Rules", fileName = "RewardRules")]
public class RewardRules : ScriptableObject
{
    [Tooltip("1등 · 2등 · 3등 … SC (모자라면 아래 '나머지')")]
    public int[] RankSC = { 100, 70, 50 };
    [Tooltip("위 목록 밖 등수")]
    public int OtherSC = 30;
    [Tooltip("처치 1번마다 SC")]
    public int PerKillSC = 5;
    [Tooltip("1등 · 2등 … 티켓 (모자라면 0)")]
    public int[] RankTickets = { 1 };

    public int SCFor(int rank, int kills)
    {
        int b = rank >= 1 && RankSC != null && rank <= RankSC.Length ? RankSC[rank - 1] : OtherSC;
        return Mathf.Max(0, b + Mathf.Max(0, kills) * PerKillSC);
    }

    public int TicketsFor(int rank) =>
        rank >= 1 && RankTickets != null && rank <= RankTickets.Length ? Mathf.Max(0, RankTickets[rank - 1]) : 0;
}
