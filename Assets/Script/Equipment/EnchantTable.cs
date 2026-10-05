using System.Collections.Generic;
using UnityEngine;

/// <summary>구버전 — 기존 에셋 호환용. 새로 만들 땐 Phases를 쓰세요.</summary>
[System.Serializable]
public class EnchantStage
{
    public string Label = "1단계";

    [Tooltip("이 단계에서 나올 수 있는 인챈트들")]
    public EnchantData[] Pool;
}

[System.Serializable]
public class EnchantCandidate
{
    public EnchantData Enchant;

    [Tooltip("0이면 인챈트 자체의 RollWeight 사용")]
    public float Weight = 0f;

    public float GetWeight() =>
        Weight > 0f ? Weight : Mathf.Max(0.01f, Enchant != null ? Enchant.RollWeight : 0f);
}

[System.Serializable]
public class EnchantPhase
{
    public string Label = "초반";

    [Tooltip("이 구간이 시작되는 단계 (1강이면 1)")]
    [Min(1)] public int FromStage = 1;

    [Tooltip("이 구간이 끝나는 단계 (포함)")]
    [Min(1)] public int ToStage = 5;

    public EnchantCandidate[] Candidates;
}

public enum MilestoneMode
{
    Guaranteed,     // 그 단계에서 반드시 이 인챈트
    AddToPool,      // 그 단계의 후보에 추가되어 확률로 등장
}

[System.Serializable]
public class EnchantMilestone
{
    [Tooltip("이 단계를 만들 때 적용 (100강이면 100)")]
    [Min(1)] public int Stage = 100;

    public EnchantData Enchant;

    [Tooltip("Guaranteed = 반드시 등장 / AddToPool = 그 단계 후보에 추가")]
    public MilestoneMode Mode = MilestoneMode.Guaranteed;

    [Tooltip("AddToPool일 때 가중치. 0이면 인챈트 RollWeight")]
    public float Weight = 0f;
}

[System.Serializable]
public class SpecialCandidate
{
    public SpecialEnchantData Special;

    [Tooltip("선택지에 뽑힐 가중치")]
    [Min(0)] public float Weight = 1f;

    [Tooltip("이 단계 이상을 만들 때부터 후보에 나옴. 0 = 처음부터")]
    [Min(0)] public int MinStage = 0;

    [Tooltip("이 단계까지만 후보에 나옴. 0 = 제한 없음")]
    [Min(0)] public int MaxStage = 0;

    public bool AvailableAt(int stage) =>
        stage >= MinStage && (MaxStage <= 0 || stage <= MaxStage);
}

/// <summary>
/// 무기 하나(또는 같은 계열 무기들)의 인챈트 추첨표.
///   Phases     — 단계 구간별 일반 인챈트 후보
///   Milestones — 특정 단계 전용 일반 인챈트
///   특수 단계  — 이 단계에선 특수 인챈트 선택지 (또는 일반으로 변경)
/// </summary>
[CreateAssetMenu(menuName = "Game/Enchant Table")]
public class EnchantTable : ScriptableObject
{
    [Tooltip("이 테이블을 쓰는 무기의 기본 최대 강화 단계.\n대장장이는 이 한계를 넘어 EnchantRules의 해제 단계까지 가능")]
    [SerializeField, Min(0)] private int maxStage = 20;

    [Header("일반 인챈트")]
    [Tooltip("단계 구간별 후보. 구간 밖(한계 이후 등)은 바로 앞 구간 사용")]
    public EnchantPhase[] Phases;

    [Tooltip("특정 단계 전용 인챈트")]
    public EnchantMilestone[] Milestones;

    [Header("특수 인챈트")]
    [Tooltip("이 단계를 만들 때 특수 인챈트 선택지가 나옴. 후보가 없으면 일반 인챈트")]
    public int[] SpecialStages = { 5, 10, 15 };

    [Tooltip("이 무기에 붙을 수 있는 특수 인챈트. 비우면 특수 단계에서도 일반 인챈트")]
    public SpecialCandidate[] SpecialCandidates;

    [Tooltip("특수 단계에서 보여줄 선택지 개수")]
    [Range(1, 3)] public int SpecialChoiceCount = 3;

    [Header("구버전")]
    [Tooltip("구버전 데이터. Phases가 비어 있을 때만 사용됨.\n우클릭(⋮) 메뉴 '구버전 변환'으로 옮기세요")]
    public EnchantStage[] Stages;

    bool UsesLegacy => (Phases == null || Phases.Length == 0) && Stages != null && Stages.Length > 0;

    /// <summary>이 무기의 기본 최대 단계 (대장장이 아닐 때의 한계)</summary>
    public int MaxStage => UsesLegacy ? Stages.Length : maxStage;

    public bool HasSpecials => SpecialCandidates != null && SpecialCandidates.Length > 0;

    static readonly List<(EnchantData e, float w)> _pool = new();

    // ══════════════════ 특수 단계 ══════════════════

    /// <summary>
    /// 이 단계가 특수 단계인가.
    /// 대장장이(unlocked)는 마지막 특수 단계 이후 interval마다 추가.
    /// </summary>
    public bool IsSpecialStage(int stage, bool unlocked, int interval)
    {
        if (!HasSpecials || SpecialStages == null || SpecialStages.Length == 0) return false;

        int last = 0;
        foreach (var s in SpecialStages)
        {
            if (s == stage) return true;
            if (s > last) last = s;
        }

        return unlocked && interval > 0 && stage > last && (stage - last) % interval == 0;
    }

    // ══════════════════ 일반 인챈트 추첨 ══════════════════

    /// <param name="stage">이번에 만들 단계 (1강이면 1)</param>
    public EnchantData Roll(int stage)
    {
        if (stage < 1) return null;
        if (UsesLegacy) return RollLegacy(stage - 1);

        if (Milestones != null)
            foreach (var m in Milestones)
                if (m != null && m.Enchant != null && m.Stage == stage && m.Mode == MilestoneMode.Guaranteed)
                    return m.Enchant;

        _pool.Clear();

        var phase = FindPhase(stage);
        if (phase?.Candidates != null)
            foreach (var c in phase.Candidates)
                if (c?.Enchant != null) _pool.Add((c.Enchant, c.GetWeight()));

        if (Milestones != null)
            foreach (var m in Milestones)
                if (m != null && m.Enchant != null && m.Stage == stage && m.Mode == MilestoneMode.AddToPool)
                    _pool.Add((m.Enchant, m.Weight > 0f ? m.Weight : Mathf.Max(0.01f, m.Enchant.RollWeight)));

        return Pick(_pool);
    }

    /// <summary>단계가 속한 구간. 없으면 바로 앞(끝 단계가 가장 큰) 구간</summary>
    public EnchantPhase FindPhase(int stage)
    {
        if (Phases == null) return null;

        EnchantPhase before = null;
        foreach (var p in Phases)
        {
            if (p == null) continue;
            if (stage >= p.FromStage && stage <= p.ToStage) return p;
            if (p.ToStage < stage && (before == null || p.ToStage > before.ToStage)) before = p;
        }
        return before;
    }

    static EnchantData Pick(List<(EnchantData e, float w)> pool)
    {
        if (pool.Count == 0) return null;

        float total = 0f;
        foreach (var (_, w) in pool) total += w;
        if (total <= 0f) return null;

        float r = Random.value * total;
        foreach (var (e, w) in pool)
        {
            r -= w;
            if (r <= 0f) return e;
        }
        return pool[^1].e;
    }

    EnchantData RollLegacy(int index)
    {
        if (index < 0 || index >= Stages.Length) return null;

        var pool = Stages[index].Pool;
        if (pool == null || pool.Length == 0) return null;

        _pool.Clear();
        foreach (var e in pool)
            if (e != null) _pool.Add((e, Mathf.Max(0.01f, e.RollWeight)));

        return Pick(_pool);
    }

    /// <summary>이 단계에서 나올 수 있는 일반 인챈트 전부 (확정 인챈트가 있으면 그것만)</summary>
    public void GetNormalCandidates(int stage, List<EnchantData> into)
    {
        into.Clear();
        if (stage < 1) return;

        if (UsesLegacy)
        {
            int idx = stage - 1;
            if (idx < Stages.Length && Stages[idx]?.Pool != null)
                foreach (var e in Stages[idx].Pool)
                    if (e != null && !into.Contains(e)) into.Add(e);
            return;
        }

        if (Milestones != null)
            foreach (var m in Milestones)
                if (m != null && m.Enchant != null && m.Stage == stage && m.Mode == MilestoneMode.Guaranteed)
                {
                    into.Add(m.Enchant);
                    return;
                }

        var phase = FindPhase(stage);
        if (phase?.Candidates != null)
            foreach (var c in phase.Candidates)
                if (c?.Enchant != null && !into.Contains(c.Enchant)) into.Add(c.Enchant);

        if (Milestones != null)
            foreach (var m in Milestones)
                if (m != null && m.Enchant != null && m.Stage == stage
                    && m.Mode == MilestoneMode.AddToPool && !into.Contains(m.Enchant))
                    into.Add(m.Enchant);
    }

#if UNITY_EDITOR
    [ContextMenu("구버전 변환 (Stages → Phases)")]
    void ConvertLegacy()
    {
        if (Stages == null || Stages.Length == 0)
        {
            Debug.Log($"[EnchantTable] {name}: 변환할 구버전 데이터가 없습니다.", this);
            return;
        }

        UnityEditor.Undo.RecordObject(this, "Convert EnchantTable");

        var list = new List<EnchantPhase>();
        for (int i = 0; i < Stages.Length; i++)
        {
            var s = Stages[i];
            var cands = new List<EnchantCandidate>();

            if (s?.Pool != null)
                foreach (var e in s.Pool)
                    if (e != null) cands.Add(new EnchantCandidate { Enchant = e, Weight = 0f });

            list.Add(new EnchantPhase
            {
                Label = string.IsNullOrEmpty(s?.Label) ? $"{i + 1}강" : s.Label,
                FromStage = i + 1,
                ToStage = i + 1,
                Candidates = cands.ToArray(),
            });
        }

        Phases = list.ToArray();
        maxStage = Stages.Length;
        Stages = null;

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[EnchantTable] {name}: {Phases.Length}개 단계를 구간으로 변환했습니다. " +
                  "비슷한 단계끼리 구간을 합쳐 정리하세요.", this);
    }

    void OnValidate()
    {
        if (Phases != null)
            foreach (var p in Phases)
                if (p != null && p.ToStage < p.FromStage) p.ToStage = p.FromStage;
    }
#endif
}