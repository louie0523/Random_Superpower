using Fusion;
using UnityEngine;

public enum PickupKind { Weapon, EnchantStone, Soul }

public class Pickup : NetworkBehaviour
{
    [Header("내용")]
    [Networked] public int Kind { get; set; }
    [Networked] public int ItemId { get; set; }     // 무기 ID 등
    [Networked] public int Amount { get; set; }     // 스톤/소울 개수

    [Header("설정")]
    [SerializeField] private float pickupRadius = 1.2f;
    [SerializeField] private float spinSpeed = 60f;
    [SerializeField] private float bobAmount = 0.15f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private Transform visualRoot;

    [Header("무기 표시")]
    [SerializeField] private EquipmentDatabase equipDatabase;
    [SerializeField] private float modelScale = 0.6f;

    [Networked] private TickTimer NoPickupTimer { get; set; }

    private GameObject _model;
    private Vector3 _baseLocalPos;
    private int _shownId = -1;

    public PickupKind PickupKind => (PickupKind)Kind;
    private bool _consumed;
    public override void Spawned()
    {
        if (visualRoot == null) visualRoot = transform;
        _baseLocalPos = visualRoot.localPosition;

        BuildVisual();
    }

    /// <summary>드롭 직후 잠깐 못 줍게 (버린 무기를 바로 다시 줍는 것 방지)</summary>
    public void SetPickupDelay(float seconds)
    {
        if (!HasStateAuthority) return;
        NoPickupTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    // ══════════════════ 표시 ══════════════════

    void BuildVisual()
    {
        if (_shownId == ItemId) return;
        _shownId = ItemId;

        if (_model != null) Destroy(_model);

        if (PickupKind != PickupKind.Weapon) return;

        var data = equipDatabase != null ? equipDatabase.Get(ItemId) : null;
        if (data == null || data.ModelPrefab == null) return;

        _model = Instantiate(data.ModelPrefab, visualRoot);
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
        _model.transform.localScale = Vector3.one * modelScale;

        // 무기 프리팹의 판정용 컴포넌트 제거
        foreach (var c in _model.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
    }

    public override void Render()
    {
        if (_shownId != ItemId) BuildVisual();
        if (visualRoot == null) return;

        visualRoot.Rotate(Vector3.up * spinSpeed * Time.deltaTime, Space.Self);
        visualRoot.localPosition = _baseLocalPos +
            Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobAmount;
    }

    // ══════════════════ 획득 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (_consumed) return;
        if (NoPickupTimer.IsRunning && !NoPickupTimer.Expired(Runner)) return;

        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null) continue;

            float d = Vector3.Distance(obj.transform.position, transform.position);
            if (d > pickupRadius) continue;

            if (!obj.TryGetComponent<PlayerHealth>(out var h) || h.IsDead) continue;

            if (TryGive(obj)) {
                _consumed = true;
                Runner.Despawn(Object); 
                return; 
            }
        }
    }

    bool TryGive(NetworkObject player)
    {
        switch (PickupKind)
        {
            case PickupKind.Weapon:
                if (!player.TryGetComponent<PlayerEquipment>(out var eq)) return false;
                Debug.Log($"[Pickup] 획득 시도 — {ItemId}");
                return eq.PickUpWeapon(ItemId);      // ← 실패하면 픽업이 안 사라짐

            case PickupKind.Soul:
                if (!player.TryGetComponent<PlayerHealth>(out var hp)) return false;
                hp.RPC_GainSoul(Amount);
                return true;

            case PickupKind.EnchantStone:
                // PlayerEnchant가 생기면 연결
                return false;
        }
        return false;
    }



    /// <summary>스폰 직후 내용 설정. StateAuthority에서만</summary>
    public void Init(PickupKind kind, int itemId, int amount = 1)
    {
        Kind = (int)kind;
        ItemId = itemId;
        Amount = amount;
    }
}