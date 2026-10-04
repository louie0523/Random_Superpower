using Fusion;
using UnityEngine;


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

    private bool _hadPickup;    // 직전 틱까지 무기가 있었는가 (로컬)
    private bool _forceSpawn;   // 라운드 시작 — 비면 즉시 생성

    public override void Spawned()
    {
        if (HasStateAuthority) SpawnNow();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        // 무기가 놓여 있는 동안은 대기
        if (Current != null || _localCurrent != null)
        {
            _hadPickup = true;
            return;
        }

        // 라운드 시작 → 타이머 없이 바로
        if (_forceSpawn)
        {
            _forceSpawn = false;
            _hadPickup = false;
            SpawnNow();
            return;
        }

        // 방금 비었음 (누가 주웠거나 수명 만료) → 여기서부터 카운트
        if (_hadPickup)
        {
            _hadPickup = false;
            Timer = TickTimer.CreateFromSeconds(Runner, respawnInterval);
            return;
        }

        if (!Timer.ExpiredOrNotRunning(Runner)) return;
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

    /// <summary>RoundManager가 호출. 기존 무기가 치워지는 대로 즉시 새로 생성</summary>
    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;
        _forceSpawn = true;
        Timer = default;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
    }
}