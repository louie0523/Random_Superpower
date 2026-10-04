using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 상자. 열리면 재충전 후 등급을 다시 뽑음 (시간이 지날수록 좋은 등급 쪽으로).
/// 공통 규칙은 ChestRules, 이 상자만의 조정은 '이 상자만 조정' 칸.
/// </summary>
public class LootChest : NetworkBehaviour
{
    [System.Serializable]
    public class TierMultiplier
    {
        [Tooltip("1 = 그대로. 예) 일반 1 · 희귀 1.3 · 고급 1.5 → 좋은 상자가 조금 더 자주\n" +
                 "고정하려면 나머지를 0 (예: 0, 0, 1 = 항상 고급)")]
        [Min(0)] public float Common = 1f;
        [Min(0)] public float Rare = 1f;
        [Min(0)] public float Epic = 1f;

        public float Get(ChestTier t) => t switch
        {
            ChestTier.Rare => Rare,
            ChestTier.Epic => Epic,
            _ => Common,
        };
    }

    [System.Serializable]
    public class GradeMultiplier
    {
        [Tooltip("1 = 그대로. 예) S만 2 → 이 상자는 S급이 두 배 잘 나옴")]
        [Min(0)] public float C = 1f;
        [Min(0)] public float B = 1f;
        [Min(0)] public float A = 1f;
        [Min(0)] public float S = 1f;

        public float Get(ItemGrade g) => g switch
        {
            ItemGrade.B => B,
            ItemGrade.A => A,
            ItemGrade.S => S,
            _ => C,
        };
    }

    // ══════════════════ 설정 ══════════════════

    [Header("규칙")]
    [Tooltip("공통 규칙 에셋. 특별한 상자는 다른 규칙 에셋을 연결해도 됨")]
    [SerializeField] private ChestRules rules;

    [Header("기본값")]
    [Tooltip("여는 데 걸리는 시간 (초). 규칙의 등급별 OpenTime이 0일 때 사용")]
    [SerializeField] private float openTime = 3f;

    [Tooltip("이 거리를 벗어나면 진행도 초기화")]
    [SerializeField] private float interactRange = 2.5f;

    [Tooltip("SA가 보는 원격 플레이어 위치는 약간 늦으므로 거리 판정에 주는 여유 (m)")]
    [SerializeField] private float rangeTolerance = 0.5f;

    [Tooltip("재충전 시간. 0이면 한 번만")]
    [SerializeField] private float refillTime = 45f;

    [Header("라운드 시작 등급")]
    [Tooltip("켜면 항상 아래 등급으로 시작, 끄면 시간표(0분)로 추첨")]
    [SerializeField] private bool fixedFirstTier = true;
    [SerializeField] private ChestTier firstTier = ChestTier.Common;

    [Header("이 상자만 조정")]
    [Tooltip("상자 등급이 뽑힐 확률 배율")]
    [SerializeField] private TierMultiplier tierMul = new();

    [Tooltip("무기 등급이 뽑힐 확률 배율")]
    [SerializeField] private GradeMultiplier gradeMul = new();

    [Tooltip("이 상자에서 특정 무기가 더(덜) 나오게. 공통 규칙의 배율과 곱해짐")]
    [SerializeField] private ChestRules.WeaponBoost[] weaponBoosts;

    [Tooltip("비우면 데이터베이스 전체(특수 무기 제외). 채우면 이 목록 안에서만")]
    [SerializeField] private EquipmentData[] weaponPool;

    [Header("드롭")]
    [SerializeField] private NetworkObject pickupPrefab;
    [SerializeField] private EquipmentDatabase equipDatabase;
    [SerializeField] private float dropSpread = 1.2f;

    [Header("연출")]
    [SerializeField] private Transform lid;
    [SerializeField] private Vector3 lidOpenRot = new(-100f, 0f, 0f);
    [SerializeField] private float lidSpeed = 6f;

    [Tooltip("등급별 외형 — [0] 일반, [1] 희귀, [2] 고급. 현재 등급 것만 켜짐")]
    [SerializeField] private GameObject[] tierVisuals;

    [Header("Debug")]
    [SerializeField] private bool logRolls = false;

    // ══════════════════ 네트워크 상태 ══════════════════

    [Networked] public NetworkBool IsOpened { get; set; }
    [Networked] public float Progress { get; set; }          // 0~1
    [Networked] public PlayerRef Opener { get; set; }
    [Networked] private TickTimer RefillTimer { get; set; }

    [Networked, OnChangedRender(nameof(OnTierChanged))]
    public int CurrentTier { get; set; }

    private Quaternion _lidClosed;

    public ChestTier Tier => (ChestTier)CurrentTier;
    public float InteractRange => interactRange;

    /// <summary>현재 등급 기준 여는 시간</summary>
    public float OpenTime
    {
        get
        {
            var cfg = rules != null ? rules.Config(Tier) : null;
            return cfg != null && cfg.OpenTime > 0f ? cfg.OpenTime : openTime;
        }
    }

    static float RoundMinutes => RoundManager.Instance != null ? RoundManager.Instance.RoundMinutes : 0f;

    public override void Spawned()
    {
        if (lid != null) _lidClosed = lid.localRotation;

        if (HasStateAuthority) CurrentTier = (int)FirstTier();
        ApplyTierVisual();
    }

    // ══════════════════ 상호작용 ══════════════════

    /// <summary>누르기 시작 / 뗌. 진행도·거리·사망 판정은 SA가 직접 함</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetHold(PlayerRef player, NetworkBool on)
    {
        if (IsOpened) return;

        if (on)
        {
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

                // ★ 재충전 — 지금 시간대의 확률로 다음 등급
                CurrentTier = (int)RollTier(RoundMinutes);
                if (logRolls) Debug.Log($"[Chest] {name} 재충전 → {Tier} ({RoundMinutes:F1}분)", this);
            }
            return;
        }

        if (Opener != PlayerRef.None)
        {
            if (!IsOpenerValid())
            {
                Opener = PlayerRef.None;
                Progress = 0f;
                return;
            }

            Progress += Runner.DeltaTime / Mathf.Max(0.1f, OpenTime);
            if (Progress >= 1f) Open();
            return;
        }

        if (Progress > 0f)
            Progress = Mathf.Max(0f, Progress - Runner.DeltaTime * 0.5f);
    }

    bool IsOpenerValid()
    {
        var obj = Runner.GetPlayerObject(Opener);
        if (obj == null) return false;

        if (obj.TryGetComponent<PlayerHealth>(out var h) && h.IsDead) return false;

        float d = Vector3.Distance(obj.transform.position, transform.position);
        return d <= interactRange + rangeTolerance;
    }

    // ══════════════════ 열기 ══════════════════

    void Open()
    {
        var opener = Opener;
        var cfg = rules != null ? rules.Config(Tier) : null;

        IsOpened = true;
        Progress = 1f;
        Opener = PlayerRef.None;

        int drops = cfg != null ? cfg.DropCount : 1;
        for (int i = 0; i < drops; i++) DropOne(i, cfg);

        int stones = cfg != null ? Random.Range(cfg.StoneMin, cfg.StoneMax + 1) : Random.Range(3, 11);

        if (logRolls) Debug.Log($"[Chest] {name} 열림 — {Tier}, 무기 {drops}개, 스톤 {stones}", this);

        if (stones > 0 && opener != PlayerRef.None)
        {
            var obj = Runner.GetPlayerObject(opener);
            if (obj != null && obj.TryGetComponent<PlayerEnchant>(out var en))
                en.RPC_AddStones(stones);
        }

        if (refillTime > 0f)
            RefillTimer = TickTimer.CreateFromSeconds(Runner, refillTime);
    }

    void DropOne(int index, ChestRules.TierConfig cfg)
    {
        if (pickupPrefab == null) return;

        int id = RollWeaponId(cfg);
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

    // ══════════════════ 추첨 ══════════════════

    ChestTier FirstTier() => fixedFirstTier ? firstTier : RollTier(0f);

    ChestTier RollTier(float minutes)
    {
        var phase = rules != null ? rules.PhaseAt(minutes) : null;
        if (phase == null) return firstTier;

        float c = phase.Get(ChestTier.Common) * tierMul.Get(ChestTier.Common);
        float r = phase.Get(ChestTier.Rare) * tierMul.Get(ChestTier.Rare);
        float e = phase.Get(ChestTier.Epic) * tierMul.Get(ChestTier.Epic);

        float total = c + r + e;
        if (total <= 0f) return firstTier;

        float x = Random.value * total;
        if (x < c) return ChestTier.Common;
        if (x < c + r) return ChestTier.Rare;
        return ChestTier.Epic;
    }

    static readonly List<EquipmentData> _cands = new();
    static readonly List<EquipmentData> _inGrade = new();
    static readonly float[] _gradeW = new float[4];

    /// <summary>① 무기 등급을 뽑고 ② 그 등급 안에서 무기별 배율로 하나</summary>
    int RollWeaponId(ChestRules.TierConfig cfg)
    {
        // 후보
        _cands.Clear();
        var src = weaponPool != null && weaponPool.Length > 0 ? weaponPool : equipDatabase?.All;
        if (src == null) return 0;

        foreach (var w in src)
            if (w != null && w.Slot == EquipSlot.Weapon && !w.IsSpecial && WeaponWeight(w) > 0f)
                _cands.Add(w);

        if (_cands.Count == 0) return 0;

        // ① 등급 — 후보가 있는 등급만
        float total = 0f;
        for (int g = 0; g < 4; g++)
        {
            _gradeW[g] = 0f;
            if (!HasGrade((ItemGrade)g)) continue;

            float baseW = cfg != null ? cfg.Grade((ItemGrade)g) : 1f;
            _gradeW[g] = baseW * gradeMul.Get((ItemGrade)g);
            total += _gradeW[g];
        }

        _inGrade.Clear();
        if (total > 0f)
        {
            float x = Random.value * total;
            int pick = 3;
            for (int g = 0; g < 4; g++)
            {
                x -= _gradeW[g];
                if (x <= 0f && _gradeW[g] > 0f) { pick = g; break; }
            }

            foreach (var w in _cands)
                if ((int)w.Grade == pick) _inGrade.Add(w);
        }

        var pool = _inGrade.Count > 0 ? _inGrade : _cands;   // 등급 가중치가 전부 0이면 전체에서

        // ② 무기
        float sum = 0f;
        foreach (var w in pool) sum += WeaponWeight(w);

        float y = Random.value * sum;
        foreach (var w in pool)
        {
            y -= WeaponWeight(w);
            if (y <= 0f)
            {
                if (logRolls) Debug.Log($"[Chest] {name} 드롭 → {w.name} ({w.Grade})", this);
                return w.Id;
            }
        }
        return pool[^1].Id;
    }

    static bool HasGrade(ItemGrade g)
    {
        foreach (var w in _cands)
            if (w.Grade == g) return true;
        return false;
    }

    float WeaponWeight(EquipmentData w)
    {
        float m = rules != null ? rules.GlobalBoost(w) : 1f;
        if (weaponBoosts != null)
            foreach (var b in weaponBoosts)
                if (b != null && b.Weapon == w) m *= b.Multiplier;
        return m;
    }

    // ══════════════════ 라운드 ══════════════════

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;
        IsOpened = false;
        Progress = 0f;
        Opener = PlayerRef.None;
        RefillTimer = default;
        CurrentTier = (int)FirstTier();
    }

    // ══════════════════ 연출 ══════════════════

    void OnTierChanged() => ApplyTierVisual();

    void ApplyTierVisual()
    {
        if (tierVisuals == null) return;
        for (int i = 0; i < tierVisuals.Length; i++)
            if (tierVisuals[i] != null) tierVisuals[i].SetActive(i == CurrentTier);
    }

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