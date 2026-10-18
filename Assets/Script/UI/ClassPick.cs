using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 라운드 시작 클래스(메인 능력) 고르기 — 카드 N장 중 하나. 플레이어 프리팹에 AbilityRuntime과 같이 붙임.
///   AbilityRuntime이 능력을 배정할 때 메인 능력을 추첨하는 대신 Begin()을 부름 (forceMainId가 있으면 건너뜀)
///   카드마다 아래 새로고침 버튼 — 그 카드만 다른 클래스로 바꿈. 라운드당 Rerolls번 (증바람식)
///   시간 안에 못 고르면 무작위로 하나
/// 화면은 ClassPickPanel (씬에 하나, 없으면 자동 생성)
/// </summary>
public class ClassPick : NetworkBehaviour
{
    public const int MaxChoices = 4;

    [Header("클래스 선택")]
    [Tooltip("끄면 예전처럼 메인 능력을 바로 무작위 배정")]
    public bool PickEnabled = true;
    [Tooltip("보여줄 카드 수")]
    [Range(1, MaxChoices)] public int ChoiceCount = 3;
    [Tooltip("새로고침 버튼 사용 — 클래스가 적을 땐 꺼두기")]
    public bool AllowReroll = true;
    [Tooltip("라운드당 새로고침 횟수 (모든 카드 합쳐서)")]
    [Min(0)] public int Rerolls = 1;
    [Tooltip("고르는 시간 (초) — 지나면 무작위. 0이면 무제한")]
    [Min(0f)] public float PickTime = 20f;
    [Tooltip("모든 플레이어가 고를 때까지 전원 이동 · 공격 · 스킬 불가 (먼저 고른 사람이 공격하지 못하게)")]
    public bool FreezeUntilAllPicked = true;

    [Networked] public NetworkBool Picking { get; set; }
    [Networked, Capacity(MaxChoices)] public NetworkArray<int> Choices { get; }
    [Networked] public int RerollsLeft { get; set; }
    [Networked] private TickTimer PickTimer { get; set; }

    /// <summary>내 플레이어의 ClassPick (UI용)</summary>
    public static ClassPick Local { get; private set; }

    static readonly List<ClassPick> _all = new();

    /// <summary>아직 고르는 중인 플레이어가 있는지 (더미 · 주인 없는 오브젝트 제외)</summary>
    public static bool AnyonePicking => PickingCount > 0;

    /// <summary>고르는 중인 플레이어 수</summary>
    public static int PickingCount
    {
        get
        {
            int n = 0;
            foreach (var c in _all) if (c.IsRealPlayer && c.Picking) n++;
            return n;
        }
    }

    /// <summary>고르는 중인 플레이어들의 남은 시간 중 가장 긴 것 (초). 무제한이면 -1</summary>
    public static float MaxTimeLeft
    {
        get
        {
            float best = 0f;
            foreach (var c in _all)
            {
                if (!c.IsRealPlayer || !c.Picking) continue;
                float t = c.TimeLeft;
                if (t < 0f) return -1f;
                best = Mathf.Max(best, t);
            }
            return best;
        }
    }

    bool IsRealPlayer => Object != null && Object.IsValid && Object.InputAuthority != PlayerRef.None;

    private AbilityRuntime _ar;
    private AbilityDatabase _db;
    private readonly List<AbilityData> _pool = new();

    public bool WantsPick => PickEnabled && isActiveAndEnabled;
    public int Count => Mathf.Clamp(ChoiceCount, 1, MaxChoices);
    public bool CanReroll => AllowReroll && RerollsLeft > 0;

    /// <summary>남은 시간 (초). 무제한이면 -1</summary>
    public float TimeLeft
    {
        get
        {
            if (PickTime <= 0f || Runner == null) return -1f;
            var r = PickTimer.RemainingTime(Runner);
            return r.HasValue ? Mathf.Max(0f, r.Value) : 0f;
        }
    }

    public override void Spawned()
    {
        _ar = GetComponent<AbilityRuntime>();
        if (HasInputAuthority) Local = this;
        if (!_all.Contains(this)) _all.Add(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
        _all.Remove(this);
    }

    // ══════════════════ 시작 (SA) ══════════════════

    /// <summary>카드를 뽑고 고르기 시작. AbilityRuntime.AssignAbilities가 부름</summary>
    public void Begin(AbilityDatabase db)
    {
        if (!HasStateAuthority) return;
        _db = db;
        if (_ar == null) _ar = GetComponent<AbilityRuntime>();

        BuildPool();
        var used = new List<int>();
        for (int i = 0; i < MaxChoices; i++)
        {
            int id = 0;
            if (i < Count)
            {
                var a = RollOne(used);
                if (a != null) { id = a.Id; used.Add(id); }
            }
            Choices.Set(i, id);
        }

        RerollsLeft = AllowReroll ? Rerolls : 0;
        PickTimer = PickTime > 0f ? TickTimer.CreateFromSeconds(Runner, PickTime) : default;
        Picking = true;

        // 클래스가 하나도 없으면 고를 것도 없음
        if (used.Count == 0) Picking = false;
    }

    void BuildPool()
    {
        _pool.Clear();
        if (_db == null) return;

        var src = _db.MainAbilities;
        if (src == null || src.Length == 0) src = _db.All;
        if (src == null) return;

        foreach (var a in src)
            if (a != null && a.Kind == AbilityKind.Main && !a.ExcludeFromRoll && !_pool.Contains(a))
                _pool.Add(a);
    }

    /// <summary>가중치 추첨 — exclude 빼고, 같이 노는 사람의 인원 제한(MaxConcurrent) 반영</summary>
    AbilityData RollOne(List<int> exclude)
    {
        if (_pool.Count == 0) BuildPool();

        var taken = TakenByOthers();
        var cand = new List<AbilityData>();
        float total = 0f;

        foreach (var a in _pool)
        {
            if (exclude.Contains(a.Id)) continue;
            if (a.MaxConcurrent > 0 && CountOf(taken, a.Id) >= a.MaxConcurrent) continue;
            cand.Add(a);
            total += Mathf.Max(0.01f, a.RollWeight);
        }

        // 다 빠졌으면 인원 제한 무시 → 그래도 없으면 겹쳐도 됨
        if (cand.Count == 0)
            foreach (var a in _pool)
                if (!exclude.Contains(a.Id)) { cand.Add(a); total += Mathf.Max(0.01f, a.RollWeight); }
        if (cand.Count == 0) return null;

        float r = Random.value * total;
        foreach (var a in cand)
        {
            r -= Mathf.Max(0.01f, a.RollWeight);
            if (r <= 0f) return a;
        }
        return cand[^1];
    }

    static int CountOf(List<int> list, int id)
    {
        int n = 0;
        foreach (int x in list) if (x == id) n++;
        return n;
    }

    List<int> TakenByOthers()
    {
        var list = new List<int>();
        if (Runner == null) return list;
        foreach (var p in Runner.ActivePlayers)
        {
            var obj = Runner.GetPlayerObject(p);
            if (obj == null || obj == Object) continue;
            if (obj.TryGetComponent<AbilityRuntime>(out var other) && other.MainAbilityId != 0) list.Add(other.MainAbilityId);
        }
        return list;
    }

    // ══════════════════ 고르기 · 새로고침 (내 화면 → SA) ══════════════════

    /// <summary>index번 카드 고르기</summary>
    public void Choose(int index)
    {
        if (HasStateAuthority) ChooseLocal(index);
        else RPC_Choose(index);
    }

    /// <summary>index번 카드만 다시 뽑기</summary>
    public void Reroll(int index)
    {
        if (HasStateAuthority) RerollLocal(index);
        else RPC_Reroll(index);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    void RPC_Choose(int index) => ChooseLocal(index);

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    void RPC_Reroll(int index) => RerollLocal(index);

    void ChooseLocal(int index)
    {
        if (!Picking || index < 0 || index >= Count) return;
        int id = Choices[index];
        if (id == 0) return;

        Picking = false;
        if (_ar == null) _ar = GetComponent<AbilityRuntime>();
        if (_ar != null) _ar.MainAbilityId = id;       // OnChangedRender → 능력 다시 구성
    }

    void RerollLocal(int index)
    {
        if (!Picking || !CanReroll || index < 0 || index >= Count) return;

        // 지금 보이는 카드 전부 빼고 뽑음 (같은 게 다시 나오지 않게)
        var shown = new List<int>();
        for (int i = 0; i < Count; i++) if (Choices[i] != 0) shown.Add(Choices[i]);
        var a = RollOne(shown);
        if (a == null) return;          // 바꿀 클래스가 없음 — 횟수도 안 씀

        Choices.Set(index, a.Id);
        RerollsLeft--;
    }

    // ══════════════════ 시간 끝 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !Picking) return;

        // 콘솔 등으로 이미 클래스가 정해졌으면 끝
        if (_ar != null && _ar.MainAbilityId != 0) { Picking = false; return; }

        if (PickTime > 0f && PickTimer.Expired(Runner))
        {
            var valid = new List<int>();
            for (int i = 0; i < Count; i++) if (Choices[i] != 0) valid.Add(i);
            if (valid.Count > 0) ChooseLocal(valid[Random.Range(0, valid.Count)]);
            else Picking = false;
        }
    }
}