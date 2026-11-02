using Fusion;
using UnityEngine;

public enum PickupKind { Weapon, EnchantStone, Soul, Armor }     // 뒤에만 추가 (저장값)

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

    /// <summary>무기에 딸린 인챈트 (버린 사람 → 주운 사람)</summary>
    [Networked, Capacity(PlayerEnchant.MaxEnchants)]
    public NetworkArray<int> Enchants { get; }

    /// <summary>무기에 딸린 처치 수 (진화 조건)</summary>
    [Networked] public int WeaponKills { get; set; }

    /// <summary>남은 탄약 + 1 (0 = 모름 → 가득). 버린 총의 탄창 유지용</summary>
    [Networked] public int AmmoPlus1 { get; set; }

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
    [Tooltip("획득 흐름 단계별 로그")]
    [SerializeField] private bool logFlow = false;

    [Networked] private TickTimer NoPickupTimer { get; set; }
    [Networked] public PlayerRef Claimer { get; set; }
    [Networked] private TickTimer ClaimTimer { get; set; }

    private float _nextRequestTime;
    private float _manualUntil = -1f;      // 손으로 줍기 — 키를 누른 뒤 이 시간까지 요청
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

    public void InitWeapon(int weaponId, int[] enchants, int kills = 0, int ammo = -1)
    {
        Init(PickupKind.Weapon, weaponId);
        WeaponKills = Mathf.Max(0, kills);
        AmmoPlus1 = ammo >= 0 ? ammo + 1 : 0;

        if (enchants == null) return;
        for (int i = 0; i < PlayerEnchant.MaxEnchants && i < enchants.Length; i++)
            Enchants.Set(i, enchants[i]);
    }

    /// <summary>방어구 — 특수 인챈트는 Enchants[0]에 실음</summary>
    public void InitArmor(int armorId, int enchantId)
    {
        Init(PickupKind.Armor, armorId);
        Enchants.Set(0, enchantId);
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

        if (Claimer != PlayerRef.None && ClaimTimer.Expired(Runner))
        {
            Log($"임자 {Claimer} 타임아웃 → 해제");
            Claimer = PlayerRef.None;
        }
    }

    // ══════════════════ 모든 클라이언트 — 표시 + 내 플레이어 획득 판정 ══════════════════
    // Render는 SA·프록시 구분 없이 매 프레임 돈다. 상태는 읽기만, 변경은 전부 RPC.

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

            // 설정 '무기 저절로 줍기'를 끄면 — 무기는 상호작용 키로 (소울 · 스톤은 그대로 저절로)
            // 방어구는 늘 키로 (끼고 있던 걸 바꾸게 되니까)
            if ((PickupKind == PickupKind.Weapon && !GameSettings.AutoPickup) || PickupKind == PickupKind.Armor)
            {
                if (!UIManager.InputBlocked)
                {
                    var data = equipDatabase != null ? equipDatabase.Get(ItemId) : null;
                    string nm = data != null ? data.GetName() : "?";
                    AltarHud.Prompt(PickupKind == PickupKind.Armor
                        ? SetLoc.F("ui.pickup.wear", "[{0}] 착용 — {1}", KeyBinds.Name(GameAction.Interact), ArmorLabel(nm))
                        : SetLoc.F("ui.pickup.take", "[{0}] 줍기 — {1}", KeyBinds.Name(GameAction.Interact), nm));
                    if (KeyBinds.Down(GameAction.Interact)) _manualUntil = Time.time + 0.8f;
                }
                if (Time.time > _manualUntil) return;
            }

            if (Time.time < _nextRequestTime) return;
            _nextRequestTime = Time.time + requestInterval;

            Log($"요청 보냄 — 거리 {dist:F2}m");
            RPC_Request(Runner.LocalPlayer);
            return;
        }

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
                RPC_Release(Runner.LocalPlayer);
            }
        }
    }

    // ══════════════════ RPC ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Request(PlayerRef p)
    {
        Log($"요청 받음 from {p} — 현재 임자 {Claimer}");

        if (Claimer != PlayerRef.None) return;
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
        if (Claimer == p) Claimer = PlayerRef.None;
    }

    /// <summary>라운드 초기화 — RoundManager가 호출</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_DespawnForReset()
    {
        if (Object != null && Object.IsValid) Runner.Despawn(Object);
    }

    // ══════════════════ 지급 ══════════════════

    public int[] ExportEnchants()
    {
        var arr = new int[PlayerEnchant.MaxEnchants];
        for (int i = 0; i < arr.Length; i++) arr[i] = Enchants[i];
        return arr;
    }

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

            case PickupKind.Armor:
                return player.TryGetComponent<PlayerEquipment>(out var ae) && !ae.Locked;
        }
        return false;
    }

    /// <summary>방어구 이름 + 특수 인챈트 이름</summary>
    string ArmorLabel(string nm)
    {
        int ench = Enchants[0];
        if (ench == 0) return nm;
        var db = ArmorEnchantDatabase.Default;
        var me = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (me != null && me.TryGetComponent<PlayerEquipment>(out var eq)) db = eq.ArmorEnchants;
        var e = db != null ? db.Get(ench) : null;
        return e != null ? $"{nm} <color=#{ColorUtility.ToHtmlStringRGB(e.Color)}>◆ {e.GetName()}</color>" : nm;
    }

    bool TryGive(NetworkObject player)
    {
        switch (PickupKind)
        {
            case PickupKind.Weapon:
                if (!player.TryGetComponent<PlayerEquipment>(out var eq)) return false;
                if (!eq.PickUpWeapon(ItemId, out int slot)) return false;
                if (AmmoPlus1 > 0) eq.SetAmmoAt(slot, AmmoPlus1 - 1);     // 버린 총 — 남은 탄 그대로

                // 무기를 끼운 뒤에 인챈트·처치 수를 덮어씀 (순서 중요)
                if (player.TryGetComponent<PlayerEnchant>(out var pe))
                    pe.ImportSlot(slot, ItemId, ExportEnchants(), WeaponKills);

                return true;

            case PickupKind.Soul:
                if (!player.TryGetComponent<PlayerHealth>(out var hp)) return false;
                hp.RPC_GainSoul(Amount);
                return true;

            case PickupKind.EnchantStone:
                if (!player.TryGetComponent<PlayerEnchant>(out var en)) return false;
                en.RPC_AddStones(Amount);
                return true;

            case PickupKind.Armor:
                if (!player.TryGetComponent<PlayerEquipment>(out var ae)) return false;
                return ae.EquipArmor(ItemId, Enchants[0], true);       // 끼고 있던 건 인챈트째 바닥에
        }
        return false;
    }

    // ══════════════════ 표시 ══════════════════

    void BuildVisual()
    {
        if (_shownId == ItemId) return;
        _shownId = ItemId;

        if (_model != null) Destroy(_model);

        if (PickupKind != PickupKind.Weapon && PickupKind != PickupKind.Armor) return;

        var data = equipDatabase != null ? equipDatabase.Get(ItemId) : null;
        if (data == null) return;
        if (data.ModelPrefab == null)
        {
            if (PickupKind == PickupKind.Armor) BuildArmorPlaceholder(data);     // 모델이 없는 방어구 — 임시 모양
            return;
        }

        _model = Instantiate(data.ModelPrefab, visualRoot);
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
        Vector3 baseScale = data.ScaleOverride.sqrMagnitude > 0.0001f
            ? data.ScaleOverride
            : Vector3.one;

        _model.transform.localScale = baseScale * modelScale;

        foreach (var c in _model.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
    }

    /// <summary>방어구에 모델이 없을 때 — 칸별 간단한 모양 (등급 색). 모델을 넣으면 그걸 씀</summary>
    void BuildArmorPlaceholder(EquipmentData d)
    {
        var shape = d.Slot switch
        {
            EquipSlot.Helmet => PrimitiveType.Sphere,
            EquipSlot.Boots => PrimitiveType.Cube,
            _ => PrimitiveType.Capsule,
        };
        _model = GameObject.CreatePrimitive(shape);
        _model.name = "ArmorPlaceholder";
        _model.transform.SetParent(visualRoot, false);
        _model.transform.localScale = d.Slot == EquipSlot.Boots ? new Vector3(0.35f, 0.2f, 0.45f) : Vector3.one * 0.4f;
        foreach (var c in _model.GetComponentsInChildren<Collider>(true)) Destroy(c);
        var r = _model.GetComponent<Renderer>();
        if (r != null)
        {
            var col = d.Grade switch
            {
                ItemGrade.B => new Color(0.35f, 0.65f, 1f),
                ItemGrade.A => new Color(0.75f, 0.45f, 1f),
                ItemGrade.S => new Color(1f, 0.75f, 0.2f),
                _ => new Color(0.85f, 0.85f, 0.85f),
            };
            r.material.color = col;
        }
    }

    void Log(string msg)
    {
        if (!logFlow) return;
        string who = Runner != null ? $"P{Runner.LocalPlayer.PlayerId}" : "?";
        Debug.Log($"[Pickup:{who}] {name} — {msg}", this);
    }
}