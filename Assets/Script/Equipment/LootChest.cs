using Fusion;
using UnityEngine;

public class LootChest : NetworkBehaviour
{
    [Header("등급")]
    [SerializeField] private ChestTier tier = ChestTier.Common;

    [Tooltip("여는 데 걸리는 시간 (초)")]
    [SerializeField] private float openTime = 3f;

    [Tooltip("이 거리를 벗어나면 진행도 초기화")]
    [SerializeField] private float interactRange = 2.5f;

    [Tooltip("재충전 시간. 0이면 한 번만")]
    [SerializeField] private float refillTime = 45f;

    [Header("드롭")]
    [SerializeField] private NetworkObject pickupPrefab;
    [SerializeField] private EquipmentDatabase equipDatabase;
    [SerializeField] private int[] weaponIds;
    [SerializeField] private int dropCount = 1;
    [SerializeField] private float dropSpread = 1.2f;

    [Header("연출")]
    [SerializeField] private Transform lid;
    [SerializeField] private Vector3 lidOpenRot = new(-100f, 0f, 0f);
    [SerializeField] private float lidSpeed = 6f;

    [Networked] public NetworkBool IsOpened { get; set; }
    [Networked] public float Progress { get; set; }          // 0~1
    [Networked] public PlayerRef Opener { get; set; }
    [Networked] private TickTimer RefillTimer { get; set; }

    private Quaternion _lidClosed;

    public ChestTier Tier => tier;
    public float OpenTime => openTime;
    public float InteractRange => interactRange;

    public override void Spawned()
    {
        if (lid != null) _lidClosed = lid.localRotation;
    }

    // ══════════════════ 상호작용 ══════════════════

    /// <summary>플레이어가 키를 누르고 있는 동안 매 틱 호출</summary>
    public void Hold(PlayerRef player, Vector3 playerPos)
    {
        if (!HasStateAuthority) return;
        if (IsOpened) return;

        // 다른 사람이 열고 있으면 뺏지 못함
        if (Opener != PlayerRef.None && Opener != player) return;

        if (Vector3.Distance(playerPos, transform.position) > interactRange)
        {
            Cancel(player);
            return;
        }

        Opener = player;
        Progress += Runner.DeltaTime / Mathf.Max(0.1f, openTime);

        if (Progress >= 1f) Open();
    }

    /// <summary>키를 떼거나 멀어지면</summary>
    public void Cancel(PlayerRef player)
    {
        if (!HasStateAuthority) return;
        if (Opener != player) return;

        Opener = PlayerRef.None;
        Progress = 0f;
    }

    void Open()
    {
        IsOpened = true;
        Progress = 1f;
        Opener = PlayerRef.None;

        for (int i = 0; i < dropCount; i++) DropOne(i);

        if (refillTime > 0f)
            RefillTimer = TickTimer.CreateFromSeconds(Runner, refillTime);
    }

    void DropOne(int index)
    {
        if (pickupPrefab == null) return;

        int id = RollWeaponId();
        if (id == 0) return;

        float angle = index * 137f;      // 골든 앵글로 퍼뜨림
        Vector3 off = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dropSpread;

        var obj = Runner.Spawn(pickupPrefab,
            transform.position + off + Vector3.up * 0.6f, Quaternion.identity);

        if (obj.TryGetComponent<Pickup>(out var pk))
        {
            pk.Init(PickupKind.Weapon, id);
            pk.SetPickupDelay(0.6f);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (IsOpened)
        {
            if (refillTime > 0f && RefillTimer.Expired(Runner))
            {
                IsOpened = false;
                Progress = 0f;
                RefillTimer = default;
            }
            return;
        }

        // 아무도 안 누르고 있으면 진행도 감소
        if (Opener == PlayerRef.None && Progress > 0f)
            Progress = Mathf.Max(0f, Progress - Runner.DeltaTime * 0.5f);
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

    // ══════════════════ 연출 ══════════════════

    public override void Render()
    {
        if (lid == null) return;

        Quaternion target = IsOpened
            ? _lidClosed * Quaternion.Euler(lidOpenRot)
            : _lidClosed;

        lid.localRotation = Quaternion.Slerp(
            lid.localRotation, target, Time.deltaTime * lidSpeed);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}