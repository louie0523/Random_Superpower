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
    [Tooltip("회전·흔들림 연출용 자식. 루트(NetworkTransform)와 반드시 분리")]
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

    [Header("획득 요청")]
    [Tooltip("임자가 이 시간 안에 지급을 끝내지 못하면 풀어줌 (나감·끊김 대비)")]
    [SerializeField] private float claimTimeout = 2f;
    [Tooltip("요청 재전송 간격")]
    [SerializeField] private float requestInterval = 0.5f;

    [Header("Debug")]
    [Tooltip("획득 흐름 단계별 로그. 원인 찾으면 끄기")]
    [SerializeField] private bool logFlow = true;

    [Networked] private TickTimer NoPickupTimer { get; set; }
    [Networked] public PlayerRef Claimer { get; set; }
    [Networked] private TickTimer ClaimTimer { get; set; }

    private float _nextRequestTime;
    private float _nextBlockedLog;
    private bool _granted;
    private bool _consumed;

    private GameObject _model;
    private Vector3 _baseLocalPos;
    private int _shownId = -1;

    public PickupKind PickupKind => (PickupKind)Kind;

    public override void Spawned()
    {
        if (visualRoot == null)
        {
            visualRoot = transform;
            Debug.LogWarning($"[Pickup] {name}: visualRoot가 비어 루트를 흔듭니다. " +
                             "NetworkTransform과 충돌하니 Visual 자식을 연결하세요.", this);
        }
        _baseLocalPos = visualRoot.localPosition;

        if (HasStateAuthority && lifetime > 0f)
            LifeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);

        BuildVisual();

        Log($"스폰 — SA={HasStateAuthority} pos={transform.position}");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_DespawnForReset()
    {
        if (Object != null && Object.IsValid) Runner.Despawn(Object);
    }

    public void InitWeapon(int weaponId, int[] enchants)
    {
        Init(PickupKind.Weapon, weaponId);

        if (enchants == null) return;
        for (int i = 0; i < PlayerEnchant.MaxEnchants && i < enchants.Length; i++)
            Enchants.Set(i, enchants[i]);
    }

    /// <summary>스폰 직후 내용 설정. StateAuthority에서만</summary>
    public void Init(PickupKind kind, int itemId, int amount = 1)
    {
        Kind = (int)kind;
        ItemId = itemId;
        Amount = amount;
    }

    /// <summary>드롭 직후 잠깐 못 줍게 (버린 무기를 바로 다시 줍는 것 방지)</summary>
    public void SetPickupDelay(float seconds)
    {
        if (!HasStateAuthority) return;
        NoPickupTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    // ══════════════════ SA 전용 — 수명 · 임자 타임아웃 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (lifetime > 0f && LifeTimer.Expired(Runner))
        {
            Runner.Despawn(Object);
            return;
        }

        // 임자가 나가거나 끊겨서 지급을 못 끝냈으면 풀어줌
        if (Claimer != PlayerRef.None && ClaimTimer.Expired(Runner))
        {
            Log($"임자 {Claimer} 타임아웃 → 해제");
            Claimer = PlayerRef.None;
        }
    }

    // ══════════════════ 모든 클라이언트 — 표시 + 내 플레이어 획득 판정 ══════════════════
    // Render는 SA·프록시 구분 없이 매 프레임 돈다.
    // 여기선 네트워크 상태를 읽기만 하고 변경은 전부 RPC로 → 시뮬레이션 설정과 무관하게 동작

    public override void Render()
    {
        TickVisual();
        TickLocalClaim();
    }

    void TickVisual()
    {
        if (_shownId != ItemId) BuildVisual();
        if (visualRoot == null) return;

        visualRoot.Rotate(Vector3.up * spinSpeed * Time.deltaTime, Space.Self);
        visualRoot.localPosition = _baseLocalPos +
            Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobAmount;

        // 수명이 얼마 안 남으면 깜빡
        if (lifetime > 0f && blinkTime > 0f && _model != null)
        {
            float? remain = LifeTimer.RemainingTime(Runner);
            bool show = true;

            if (remain.HasValue && remain.Value < blinkTime)
                show = Mathf.Repeat(Time.time * 6f, 1f) > 0.4f;

            if (_model.activeSelf != show) _model.SetActive(show);
        }
    }

    void TickLocalClaim()
    {
        if (_consumed) return;
        if (NoPickupTimer.IsRunning && !NoPickupTimer.Expired(Runner)) return;

        var me = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (me == null) return;
        if (!me.TryGetComponent<PlayerHealth>(out var h) || h.IsDead) return;

        // ── 아직 임자 없음 → 범위 안이고 받을 수 있으면 요청 ──
        if (Claimer == PlayerRef.None)
        {
            _granted = false;

            float dist = Vector3.Distance(me.transform.position, transform.position);
            if (dist > pickupRadius) return;

            if (!CanReceive(me))
            {
                if (Time.time >= _nextBlockedLog)
                {
                    _nextBlockedLog = Time.time + 2f;
                    Log("범위 안이지만 받을 수 없음 (무기 슬롯 꽉 참 등)");
                }
                return;
            }

            if (Time.time < _nextRequestTime) return;
            _nextRequestTime = Time.time + requestInterval;

            Log($"요청 보냄 — 거리 {dist:F2}m");
            RPC_Request(Runner.LocalPlayer);
            return;
        }

        // ── 내가 임자 → 내 권한으로 지급 ──
        if (Claimer == Runner.LocalPlayer && !_granted)
        {
            _granted = true;

            bool ok = TryGive(me);
            Log($"임자 됨 → 지급 {(ok ? "성공" : "실패")}");

            if (ok)
            {
                _consumed = true;
                RPC_Consumed();
            }
            else
            {
                RPC_Release(Runner.LocalPlayer);    // 그 사이 슬롯이 찼다거나
            }
        }
    }

    // ══════════════════ RPC ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Request(PlayerRef p)
    {
        Log($"요청 받음 from {p} — 현재 임자 {Claimer}");

        if (Claimer != PlayerRef.None) return;        // 먼저 온 사람이 임자
        if (NoPickupTimer.IsRunning && !NoPickupTimer.Expired(Runner)) return;

        Claimer = p;
        ClaimTimer = TickTimer.CreateFromSeconds(Runner, claimTimeout);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Consumed()
    {
        Log("소멸 요청 받음 → Despawn");
        Runner.Despawn(Object);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Release(PlayerRef p)
    {
        if (Claimer == p) Claimer = PlayerRef.None;   // 남이 뒤늦게 보낸 해제는 무시
    }

    // ══════════════════ 지급 ══════════════════

    public int[] ExportEnchants()
    {
        var arr = new int[PlayerEnchant.MaxEnchants];
        for (int i = 0; i < arr.Length; i++) arr[i] = Enchants[i];
        return arr;
    }

    /// <summary>지금 이 픽업을 받을 수 있는 상태인가 (요청 전 사전 확인)</summary>
    bool CanReceive(NetworkObject player)
    {
        switch (PickupKind)
        {
            case PickupKind.Weapon:
                return player.TryGetComponent<PlayerEquipment>(out var eq)
                       && eq.HasEmptyWeaponSlot();

            case PickupKind.Soul:
                return player.TryGetComponent<PlayerHealth>(out _);

            case PickupKind.EnchantStone:
                return player.TryGetComponent<PlayerEnchant>(out _);
        }
        return false;
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
                return true;

            case PickupKind.EnchantStone:
                if (!player.TryGetComponent<PlayerEnchant>(out var en)) return false;
                en.RPC_AddStones(Amount);
                return true;
        }
        return false;
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

    void Log(string msg)
    {
        if (!logFlow) return;
        string who = Runner != null ? $"P{Runner.LocalPlayer.PlayerId}" : "?";
        Debug.Log($"[Pickup:{who}] {name} — {msg}", this);
    }
}