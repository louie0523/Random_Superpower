using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>순서가 네트워크 인덱스 — 중간 삽입·삭제 금지, 뒤에만 추가</summary>
public enum StatusType
{
    Slow,       // 둔화 — 이동속도 N% 감소
    Burn,       // 화상 — 1초마다 최대 체력 N% 고정 피해
    Frost,      // 동상 — 공격속도 N% 감소
    Seal,       // 봉인 — 스킬 사용 불가 (수치 없음)
    Stun,       // 기절 — 이동·공격·스킬·상호작용 불가 (수치 없음)
    DeepWound,  // 깊은 상처 — 받는 회복 N% 감소
}

/// <summary>
/// 상태이상. 걸린 사람의 SA에서 처리하고, 현재 수치만 동기화(연출·UI용).
///
/// 중첩 규칙 (같은 종류끼리)
///   - 지금은 수치가 가장 높은 것만 적용
///   - 그게 끝나면 남은 것 중 다음으로 높은 것이 이어서 적용
///     예) 둔화 25% 3초 + 90% 1초 → 1초간 90%, 이후 2초간 25%
///   - 새 효과보다 수치도 같거나 높고 끝도 같거나 늦은 게 이미 있으면 튕겨냄
/// </summary>
public class PlayerStatus : NetworkBehaviour
{
    public const int Capacity = 8;
    static readonly int TypeCount = System.Enum.GetValues(typeof(StatusType)).Length;

    [Tooltip("둔화·동상이 깎을 수 있는 최대치 (%)")]
    [SerializeField] private float maxReduction = 90f;

    /// <summary>지금 적용 중인 수치. 0 = 걸려 있지 않음. 봉인·기절은 1</summary>
    [Networked, Capacity(Capacity)] public NetworkArray<float> Values { get; }

    /// <summary>그 종류가 완전히 풀리는 시각 (UI 남은 시간용)</summary>
    [Networked, Capacity(Capacity)] public NetworkArray<float> EndTimes { get; }

    struct Instance
    {
        public float Value;
        public float End;
        public PlayerRef Source;
    }

    private readonly List<Instance>[] _inst = new List<Instance>[Capacity];

    private PlayerStats _stats;
    private PlayerHealth _health;
    private float _burnTimer;

    // ══════════════════ 조회 (모든 클라이언트) ══════════════════

    public bool Has(StatusType t) => Values[(int)t] > 0f;
    public float Get(StatusType t) => Values[(int)t];

    public float Remaining(StatusType t) =>
        Has(t) ? Mathf.Max(0f, EndTimes[(int)t] - Runner.SimulationTime) : 0f;

    public bool IsStunned => Has(StatusType.Stun);
    public bool IsSealed => Has(StatusType.Seal);

    /// <summary>받는 회복에 곱할 값 (깊은 상처 40% → 0.6)</summary>
    public float HealMultiplier => 1f - Mathf.Clamp01(Get(StatusType.DeepWound) * 0.01f);

    static bool IsFlag(StatusType t) => t == StatusType.Seal || t == StatusType.Stun;

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        for (int i = 0; i < Capacity; i++) _inst[i] = new List<Instance>();
    }

    // ══════════════════ 부여 ══════════════════

    /// <summary>어디서 불러도 됨. 내가 SA가 아니면 RPC로 보냄</summary>
    /// <param name="value">수치 (둔화 25 = 25%). 봉인·기절은 무시</param>
    /// <param name="source">건 사람 — 화상으로 죽으면 이 사람에게 킬</param>
    public void Apply(StatusType t, float value, float duration, PlayerRef source)
    {
        if (HasStateAuthority) ApplyLocal(t, value, duration, source);
        else RPC_Apply((int)t, value, duration, source);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Apply(int type, float value, float duration, PlayerRef source) =>
        ApplyLocal((StatusType)type, value, duration, source);

    void ApplyLocal(StatusType t, float value, float duration, PlayerRef source)
    {
        if (_health != null && _health.IsDead) return;
        if (duration <= 0f) return;
        if (IsFlag(t)) value = 1f;
        if (value <= 0f) return;

        int i = (int)t;
        float end = Runner.SimulationTime + duration;
        var list = _inst[i];

        // 수치도 같거나 높고 끝도 같거나 늦은 게 있으면 → 튕겨냄
        for (int k = 0; k < list.Count; k++)
            if (list[k].Value >= value && list[k].End >= end) return;

        // 새 것에 완전히 묻히는 기존 것은 정리
        for (int k = list.Count - 1; k >= 0; k--)
            if (list[k].Value <= value && list[k].End <= end) list.RemoveAt(k);

        list.Add(new Instance { Value = value, End = end, Source = source });
        Refresh(t);
    }

    /// <summary>전부 해제 — 사망, 라운드 초기화</summary>
    public void ClearAll()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < TypeCount; i++)
        {
            _inst[i].Clear();
            Refresh((StatusType)i);
        }
        _burnTimer = 0f;
    }

    // ══════════════════ 진행 (SA) ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        float now = Runner.SimulationTime;

        for (int i = 0; i < TypeCount; i++)
        {
            var list = _inst[i];
            if (list.Count == 0) continue;

            bool changed = false;
            for (int k = list.Count - 1; k >= 0; k--)
                if (list[k].End <= now) { list.RemoveAt(k); changed = true; }

            if (changed) Refresh((StatusType)i);
        }

        TickBurn();
    }

    void TickBurn()
    {
        float pct = Values[(int)StatusType.Burn];
        if (pct <= 0f) { _burnTimer = 0f; return; }

        _burnTimer += Runner.DeltaTime;
        if (_burnTimer < 1f) return;
        _burnTimer -= 1f;

        _health?.TakeStatusDamage(pct, TopSource(StatusType.Burn));
    }

    PlayerRef TopSource(StatusType t)
    {
        var list = _inst[(int)t];
        float best = -1f;
        PlayerRef src = PlayerRef.None;
        foreach (var x in list)
            if (x.Value > best) { best = x.Value; src = x.Source; }
        return src;
    }

    /// <summary>그 종류의 최고 수치를 다시 계산하고 스탯에 반영</summary>
    void Refresh(StatusType t)
    {
        int i = (int)t;
        float best = 0f, lastEnd = 0f;

        foreach (var x in _inst[i])
        {
            if (x.Value > best) best = x.Value;
            if (x.End > lastEnd) lastEnd = x.End;
        }

        Values.Set(i, best);
        EndTimes.Set(i, lastEnd);

        ApplyStat(t, best);
    }

    void ApplyStat(StatusType t, float value)
    {
        if (_stats == null) return;

        StatType stat;
        if (t == StatusType.Slow) stat = StatType.MoveSpeed;
        else if (t == StatusType.Frost) stat = StatType.AttackSpeed;
        else return;

        int src = SourceId.StatusBase + (int)t;
        _stats.RemoveBySource(src);

        if (value > 0f)
            _stats.AddModifier(new StatModifier(stat, ModType.Percent, -Mathf.Min(value, maxReduction), src));
    }
}
