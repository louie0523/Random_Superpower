using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>순서가 네트워크 인덱스 — 중간 삽입·삭제 금지, 뒤에만 추가</summary>
public enum StatusType
{
    Slow,       // 둔화 — 이동속도 N% 감소
    Burn,       // 화상 — 1초마다 최대 체력 N% 고정 피해 (중첩 X, 최고치만)
    Frost,      // 동상 — 공격속도 N% 감소
    Seal,       // 봉인 — 스킬 사용 불가 (수치 없음)
    Stun,       // 기절 — 이동·공격·스킬·상호작용 불가 (수치 없음)
    DeepWound,  // 깊은 상처 — 받는 회복 N% 감소
    Poison,     // 중독 — 1초마다 최대 체력 (N × 중첩)% 고정 피해. 맞을 때마다 중첩 +1, 지속 갱신
    Evade,      // 회피 - 받는 공격을 모두 회피
    ArmorBreak, // 방어력 감소 — 방어력 N% 감소
    Ghost,      // 고스트 — 모든 공격 MISS + 사람 통과
    Blind,      // 암전 — 값(m) 반경만 보이고 나머지는 어둠 (본인 화면만). 작은 값이 우선, 최소 1m
    Root,       // 속박 — 이동·점프 불가 (공격·시점 회전은 가능)
}

/// <summary>
/// 상태이상. 걸린 사람의 SA에서 처리하고, 현재 수치만 동기화(연출·UI용).
///
/// 일반 중첩 규칙 (중독 제외)
///   - 지금은 수치가 가장 높은 것만 적용
///   - 그게 끝나면 남은 것 중 다음으로 높은 것이 이어서 적용
///   - 새 효과보다 수치도 같거나 높고 끝도 같거나 늦은 게 이미 있으면 튕겨냄
///
/// 중독
///   - 걸릴 때마다 중첩 +1 (최대치까지), 중첩당 수치는 지금까지 받은 것 중 최고
///   - 걸릴 때마다 지속시간 갱신, 끝나면 중첩 전부 해제
/// </summary>
public class PlayerStatus : NetworkBehaviour
{
    public const int Capacity = 16;
    static readonly int TypeCount = System.Enum.GetValues(typeof(StatusType)).Length;

    [Tooltip("둔화·동상이 깎을 수 있는 최대치 (%)")]
    [SerializeField] private float maxReduction = 90f;

    /// <summary>지금 적용 중인 수치. 0 = 걸려 있지 않음. 봉인·기절은 1, 중독은 (중첩당 × 중첩)</summary>
    [Networked, Capacity(Capacity)] public NetworkArray<float> Values { get; }

    /// <summary>그 종류가 완전히 풀리는 시각 (UI 남은 시간용)</summary>
    [Networked, Capacity(Capacity)] public NetworkArray<float> EndTimes { get; }

    /// <summary>중독 중첩 수 (UI용)</summary>
    [Networked] public int PoisonStacks { get; set; }

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

    // 중독 (SA 전용)
    private int _poisonStacks;
    private int _poisonMax;
    private float _poisonPerStack;
    private float _poisonEnd;
    private float _poisonTimer;
    private PlayerRef _poisonSource;

    // ══════════════════ 조회 (모든 클라이언트) ══════════════════

    static bool IsFlag(StatusType t) =>
    t == StatusType.Seal || t == StatusType.Stun || t == StatusType.Evade || t == StatusType.Ghost
    || t == StatusType.Root;

    /// <summary>값이 작을수록 강한 상태이상 — 암전(Blind) 값 = 보이는 반경(m)</summary>
    static bool LowerIsStronger(StatusType t) => t == StatusType.Blind;

    /// <summary>a가 b보다 같거나 강함</summary>
    static bool AtLeast(StatusType t, float a, float b) => LowerIsStronger(t) ? a <= b : a >= b;

    /// <summary>암전 최소 시야 (m)</summary>
    public const float MinBlindRadius = 1f;

    public bool Has(StatusType t) => Values[(int)t] > 0f;
    public float Get(StatusType t) => Values[(int)t];

    public float Remaining(StatusType t) =>
        Has(t) ? Mathf.Max(0f, EndTimes[(int)t] - Runner.SimulationTime) : 0f;

    public bool IsStunned => Has(StatusType.Stun);
    public bool IsSealed => Has(StatusType.Seal);
    public bool IsRooted => Has(StatusType.Root);

    public float HealMultiplier => 1f - Mathf.Clamp01(Get(StatusType.DeepWound) * 0.01f);


    public bool IsEvading => Has(StatusType.Evade);

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        for (int i = 0; i < Capacity; i++) _inst[i] = new List<Instance>();

        if (TypeCount > Capacity)
            Debug.LogError($"[PlayerStatus] StatusType이 {TypeCount}개인데 Capacity가 {Capacity}입니다 → Capacity를 늘리세요", this);
    }

    // ══════════════════ 부여 ══════════════════

    /// <summary>어디서 불러도 됨. 내가 SA가 아니면 RPC로 보냄</summary>
    /// <param name="value">수치 (둔화 25 = 25%). 봉인·기절은 무시</param>
    /// <param name="source">건 사람 — 지속 피해로 죽으면 이 사람에게 킬</param>
    /// <param name="maxStacks">중독 최대 중첩. 다른 상태이상은 무시</param>
    public void Apply(StatusType t, float value, float duration, PlayerRef source, int maxStacks = 1)
    {
        if (HasStateAuthority) ApplyLocal(t, value, duration, source, maxStacks);
        else RPC_Apply((int)t, value, duration, source, maxStacks);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Apply(int type, float value, float duration, PlayerRef source, int maxStacks) =>
        ApplyLocal((StatusType)type, value, duration, source, maxStacks);

    void ApplyLocal(StatusType t, float value, float duration, PlayerRef source, int maxStacks)
    {
        if (_health != null && _health.IsDead) return;
        if (duration <= 0f) return;
        if (t != StatusType.Evade && IsEvading && source != Object.InputAuthority) return;
        if (IsFlag(t)) value = 1f;
        if (t == StatusType.Blind) value = Mathf.Max(MinBlindRadius, value);     // 값 = 보이는 반경 (m)
        if (value <= 0f) return;

        if (t == StatusType.Poison)
        {
            ApplyPoison(value, duration, maxStacks, source);
            return;
        }

        int i = (int)t;
        float end = Runner.SimulationTime + duration;
        var list = _inst[i];

        for (int k = 0; k < list.Count; k++)
            if (AtLeast(t, list[k].Value, value) && list[k].End >= end) return;

        for (int k = list.Count - 1; k >= 0; k--)
            if (AtLeast(t, value, list[k].Value) && list[k].End <= end) list.RemoveAt(k);

        list.Add(new Instance { Value = value, End = end, Source = source });
        Refresh(t);
    }

    void ApplyPoison(float perStack, float duration, int maxStacks, PlayerRef source)
    {
        _poisonMax = Mathf.Max(_poisonMax, Mathf.Max(1, maxStacks));
        _poisonStacks = Mathf.Min(_poisonStacks + 1, _poisonMax);
        _poisonPerStack = Mathf.Max(_poisonPerStack, perStack);
        _poisonEnd = Mathf.Max(_poisonEnd, Runner.SimulationTime + duration);   // 맞을 때마다 갱신
        _poisonSource = source;

        SyncPoison();
    }

    void ClearPoison()
    {
        _poisonStacks = 0;
        _poisonMax = 0;
        _poisonPerStack = 0f;
        _poisonEnd = 0f;
        _poisonTimer = 0f;
        _poisonSource = PlayerRef.None;
        SyncPoison();
    }

    void SyncPoison()
    {
        int i = (int)StatusType.Poison;
        PoisonStacks = _poisonStacks;
        Values.Set(i, _poisonPerStack * _poisonStacks);
        EndTimes.Set(i, _poisonStacks > 0 ? _poisonEnd : 0f);
    }

    /// <summary>전부 해제 — 사망, 라운드 초기화</summary>
    public void ClearAll()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < TypeCount; i++)
        {
            if ((StatusType)i == StatusType.Poison) continue;
            _inst[i].Clear();
            Refresh((StatusType)i);
        }
        ClearPoison();
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

        if (_poisonStacks > 0 && now >= _poisonEnd) ClearPoison();

        TickDot(StatusType.Burn, ref _burnTimer, () => TopSource(StatusType.Burn));
        TickDot(StatusType.Poison, ref _poisonTimer, () => _poisonSource);
    }

    /// <summary>1초마다 최대 체력 % 고정 피해 (화상·중독)</summary>
    void TickDot(StatusType t, ref float timer, System.Func<PlayerRef> source)
    {
        float pct = Values[(int)t];
        if (pct <= 0f) { timer = 0f; return; }

        timer += Runner.DeltaTime;
        if (timer < 1f) return;
        timer -= 1f;

        _health?.TakeStatusDamage(pct, source(), t);
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

    void Refresh(StatusType t)
    {
        int i = (int)t;
        bool low = LowerIsStronger(t);
        float best = low ? float.MaxValue : 0f, lastEnd = 0f;

        foreach (var x in _inst[i])
        {
            if (low ? x.Value < best : x.Value > best) best = x.Value;
            if (x.End > lastEnd) lastEnd = x.End;
        }
        if (low && best == float.MaxValue) best = 0f;       // 없음

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
        else if (t == StatusType.ArmorBreak) stat = StatType.Defense;
        else return;

        int src = SourceId.StatusBase + (int)t;
        _stats.RemoveBySource(src);

        if (value > 0f)
            _stats.AddModifier(new StatModifier(stat, ModType.Percent, -Mathf.Min(value, maxReduction), src));
    }
}