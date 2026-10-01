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

    [Networked, Capacity(PlayerEnchant.MaxEnchants)]
    public NetworkArray<int> Enchants { get; }

    [Header("수명")]
    [Tooltip("이 시간이 지나면 사라짐. 0이면 영구")]
    [SerializeField] private float lifetime = 60f;

    [Tooltip("사라지기 전 깜빡이는 시간")]
    [SerializeField] private float blinkTime = 5f;

    [Networked] private TickTimer LifeTimer { get; set; }

    [Header("무기 표시")]
    [SerializeField] private EquipmentDatabase equipDatabase;
    [SerializeField] private float modelScale = 0.6f;

    [Networked] private TickTimer NoPickupTimer { get; set; }
    [Networked] public PlayerRef Claimer { get; set; }
    private float _nextRequestTime;
    private bool _granted;

    private GameObject _model;
    private Vector3 _baseLocalPos;
    private int _shownId = -1;

    public PickupKind PickupKind => (PickupKind)Kind;
    private bool _consumed;
    public override void Spawned()
    {
        if (visualRoot == null) visualRoot = transform;
        _baseLocalPos = visualRoot.localPosition;

        if (HasStateAuthority && lifetime > 0f)
            LifeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);

        BuildVisual();
    }

    public void InitWeapon(int weaponId, int[] enchants)
    {
        Init(PickupKind.Weapon, weaponId);

        if (enchants == null) return;
        for (int i = 0; i < PlayerEnchant.MaxEnchants && i < enchants.Length; i++)
            Enchants.Set(i, enchants[i]);
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
        Vector3 baseScale = data.ScaleOverride.sqrMagnitude > 0.0001f
            ? data.ScaleOverride
            : Vector3.one;

        _model.transform.localScale = baseScale * modelScale;

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

        // ★ 수명이 얼마 안 남으면 깜빡
        if (lifetime > 0f && blinkTime > 0f && _model != null)
        {
            float? remain = LifeTimer.RemainingTime(Runner);
            bool show = true;

            if (remain.HasValue && remain.Value < blinkTime)
                show = Mathf.Repeat(Time.time * 6f, 1f) > 0.4f;

            if (_model.activeSelf != show) _model.SetActive(show);
        }
    }

    // ══════════════════ 획득 ══════════════════

    public override void FixedUpdateNetwork()
    {
        // ── 수명 관리는 SA만 ──
        if (HasStateAuthority)
        {
            if (lifetime > 0f && LifeTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
                return;
            }
        }

        if (_consumed) return;
        if (NoPickupTimer.IsRunning && !NoPickupTimer.Expired(Runner)) return;

        // ── 각 클라이언트는 자기 플레이어만 본다 ──
        var me = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (me == null) return;

        if (!me.TryGetComponent<PlayerHealth>(out var h) || h.IsDead) return;

        bool inRange = Vector3.Distance(me.transform.position, transform.position) <= pickupRadius;

        // 아직 임자가 없고 내가 범위 안 → 요청
        if (Claimer == PlayerRef.None)
        {
            _granted = false;

            if (inRange && Time.time >= _nextRequestTime)
            {
                _nextRequestTime = Time.time + 0.5f;
                RPC_Request(Runner.LocalPlayer);
            }
            return;
        }

        // 내게 임자가 떨어졌으면 내 권한으로 지급
        if (Claimer == Runner.LocalPlayer && !_granted)
        {
            _granted = true;

            if (TryGive(me))
            {
                _consumed = true;
                RPC_Consumed();
            }
            else
            {
                RPC_Release();      // 슬롯 꽉 참 등
            }
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Request(PlayerRef p)
    {
        if (Claimer != PlayerRef.None) return;        // 먼저 온 사람이 임자
        Claimer = p;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Consumed() => Runner.Despawn(Object);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Release() => Claimer = PlayerRef.None;

    public int[] ExportEnchants()
    {
        var arr = new int[PlayerEnchant.MaxEnchants];
        for (int i = 0; i < arr.Length; i++) arr[i] = Enchants[i];
        return arr;
    }

    bool TryGive(NetworkObject player)
    {
        switch (PickupKind)
        {
            case PickupKind.Weapon:
                if (!player.TryGetComponent<PlayerEquipment>(out var eq)) return false;

                if (!eq.PickUpWeapon(ItemId, out int slot)) return false;

                // 무기를 끼운 뒤에 인챈트를 덮어씀 (순서 중요)
                if (player.TryGetComponent<PlayerEnchant>(out var pe))
                    pe.ImportSlot(slot, ItemId, ExportEnchants());

                return true;

            case PickupKind.Soul:
                if (!player.TryGetComponent<PlayerHealth>(out var hp)) return false;
                hp.RPC_GainSoul(Amount);
                return   true;

            case PickupKind.EnchantStone:
                if (!player.TryGetComponent<PlayerEnchant>(out var en)) return false;
                en.RPC_AddStones(Amount);
                return true;
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
