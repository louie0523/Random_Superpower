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

    [Tooltip("SA가 보는 원격 플레이어 위치는 약간 늦으므로 거리 판정에 주는 여유 (m)")]
    [SerializeField] private float rangeTolerance = 0.5f;

    [Tooltip("재충전 시간. 0이면 한 번만")]
    [SerializeField] private float refillTime = 45f;

    [Header("드롭")]
    [SerializeField] private NetworkObject pickupPrefab;
    [SerializeField] private EquipmentDatabase equipDatabase;
    [SerializeField] private int[] weaponIds;
    [SerializeField] private int dropCount = 1;
    [SerializeField] private float dropSpread = 1.2f;

    [Header("드롭 — 스톤")]
    [Tooltip("여는 사람이 받는 인챈트 스톤 개수. 0이면 없음")]
    [SerializeField] private int stoneCountMin = 3;
    [SerializeField] private int stoneCountMax = 10;

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

    /// <summary>
    /// 누르기 시작 / 뗌. 누가 열고 있는지만 SA에 알리고,
    /// 진행도·거리·사망 판정은 SA가 FixedUpdateNetwork에서 직접 한다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetHold(PlayerRef player, NetworkBool on)
    {
        if (IsOpened) return;

        if (on)
        {
            // 다른 사람이 열고 있으면 뺏지 못함 — 그 사람이 떼면 다음 요청 때 잡힘
            if (Opener == PlayerRef.None) Opener = player;
        }
        else if (Opener == player)
        {
            Opener = PlayerRef.None;
            Progress = 0f;
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

        // ── 누군가 열고 있음 → SA가 직접 검증 ──
        if (Opener != PlayerRef.None)
        {
            if (!IsOpenerValid())
            {
                // 멀어짐 · 사망 · 나감 → 취소
                Opener = PlayerRef.None;
                Progress = 0f;
                return;
            }

            Progress += Runner.DeltaTime / Mathf.Max(0.1f, openTime);
            if (Progress >= 1f) Open();
            return;
        }

        // 아무도 안 누르고 있으면 진행도 감소
        if (Progress > 0f)
            Progress = Mathf.Max(0f, Progress - Runner.DeltaTime * 0.5f);
    }

    bool IsOpenerValid()
    {
        var obj = Runner.GetPlayerObject(Opener);
        if (obj == null) return false;                                     // 나감

        if (obj.TryGetComponent<PlayerHealth>(out var h) && h.IsDead) return false;

        float d = Vector3.Distance(obj.transform.position, transform.position);
        return d <= interactRange + rangeTolerance;
    }

    void Open()
    {
        var opener = Opener;      // 초기화 전에 저장

        IsOpened = true;
        Progress = 1f;
        Opener = PlayerRef.None;

        for (int i = 0; i < dropCount; i++) DropOne(i);

        int num = Random.Range(stoneCountMin, stoneCountMax + 1);

        Debug.Log($"[Chest] 열림 — opener={opener.PlayerId}, stones={num}");

        // 스톤은 연 사람에게 직접 (RPC라 남의 플레이어여도 OK)
        if (num > 0 && opener != PlayerRef.None)
        {
            var obj = Runner.GetPlayerObject(opener);
            if (obj != null && obj.TryGetComponent<PlayerEnchant>(out var en))
                en.RPC_AddStones(num);
        }

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

    // ══════════════════ 라운드 ══════════════════

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;
        IsOpened = false;
        Progress = 0f;
        Opener = PlayerRef.None;
        RefillTimer = default;
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