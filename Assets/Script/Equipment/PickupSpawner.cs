using Fusion;
using UnityEngine;

public enum ChestTier { Common, Rare, Epic }

public class PickupSpawner : NetworkBehaviour
{
    [Header("스폰")]
    [SerializeField] private NetworkObject pickupPrefab;
    [SerializeField] private EquipmentDatabase equipDatabase;

    [Tooltip("이 목록에서 무작위. 비우면 DB의 모든 무기")]
    [SerializeField] private int[] weaponIds;

    [Tooltip("재생성 간격 (초)")]
    [SerializeField] private float respawnInterval = 25f;

    [Networked] private TickTimer Timer { get; set; }
    [Networked] private NetworkObject Current { get; set; }

    private NetworkObject _localCurrent;

    public override void Spawned()
    {
        if (!HasStateAuthority) return;

        Timer = TickTimer.CreateFromSeconds(Runner, respawnInterval);
        SpawnNow();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (!Timer.ExpiredOrNotRunning(Runner)) return;   // 쿨타임 중
        if (Current != null || _localCurrent != null) return;

        Timer = TickTimer.CreateFromSeconds(Runner, respawnInterval);   // ★ 먼저
        SpawnNow();
    }

    void SpawnNow()
    {
        if (pickupPrefab == null) return;

        int id = RollWeaponId();
        if (id == 0) return;

        var obj = Runner.Spawn(pickupPrefab, transform.position, Quaternion.identity);
        if (obj.TryGetComponent<Pickup>(out var pk))
            pk.Init(PickupKind.Weapon, id);

        Current = obj;
        _localCurrent = obj;
        Debug.Log($"[Spawner] {name} 스폰 — 무기 {id}");
    }

    int RollWeaponId()
    {
        if (weaponIds != null && weaponIds.Length > 0)
            return weaponIds[Random.Range(0, weaponIds.Length)];

        if (equipDatabase?.All == null) return 0;

        var pool = new System.Collections.Generic.List<int>();
        foreach (var e in equipDatabase.All)
            if (e != null && e.Slot == EquipSlot.Weapon) pool.Add(e.Id);

        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : 0;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
    }
}