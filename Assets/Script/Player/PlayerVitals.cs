using Fusion;
using UnityEngine;

/// <summary>
/// 머리 위 체력바용 — 최대 체력을 모든 화면에 맞춰 줌 (능력치는 권한자에서만 계산되므로).
/// 플레이어 프리팹(과 더미 프리팹)에 붙임.
/// </summary>
public class PlayerVitals : NetworkBehaviour
{
    [Networked] public int MaxHp { get; set; }

    private PlayerStats _stats;

    public override void Spawned() => _stats = GetComponent<PlayerStats>();

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || _stats == null) return;
        int m = Mathf.Max(1, Mathf.RoundToInt(_stats.Get(StatType.MaxHP)));
        if (m != MaxHp) MaxHp = m;
    }
}
