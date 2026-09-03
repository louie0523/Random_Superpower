using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class AbilityRuntime : NetworkBehaviour
{
    [SerializeField] private AbilityDatabase database;

    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int MainAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int SubAbilityId { get; set; }
    [Networked, OnChangedRender(nameof(OnAbilityChanged))]
    public int RandomAbilityId { get; set; }

    [Networked, Capacity(4)]
    public NetworkArray<float> SkillCooldownEnd { get; }

    // 봉인 상태
    [Networked] public TickTimer SealTimer { get; set; }
    public bool IsSealed => !SealTimer.ExpiredOrNotRunning(Runner);

    private PlayerStats _stats;
    private PlayerHealth _health;
    private readonly List<AbilityData> _active = new();
    private readonly Dictionary<int, float> _perTargetCd = new();

    public IReadOnlyList<AbilityData> Active => _active;

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();

        if (HasStateAuthority && RandomAbilityId == 0)
            RerollRandomAbility();

        RebuildAbilities();
    }

    void OnAbilityChanged() => RebuildAbilities();

    void RebuildAbilities()
    {
        if (_stats == null || database == null) return;

        _stats.RemoveBySource(SourceId.MainAbility);
        _stats.RemoveBySource(SourceId.SubAbility);
        _stats.RemoveBySource(SourceId.RandomAbility);
        _active.Clear();

        AddAbility(database.Get(MainAbilityId), SourceId.MainAbility);
        AddAbility(database.Get(SubAbilityId), SourceId.SubAbility);
        AddAbility(database.Get(RandomAbilityId), SourceId.RandomAbility);
    }

    /// <summary>사망 시 랜덤 능력 재추첨 (주인만)</summary>
    public void RerollRandomAbility()
    {
        if (!HasStateAuthority) return;
        if (database == null) return;

        var next = database.RollRandom(RandomAbilityId);
        if (next != null) RandomAbilityId = next.Id;
    }

    public AbilityData GetRandomAbility() => database?.Get(RandomAbilityId);

    void AddAbility(AbilityData a, int sourceId)
    {
        if (a == null) return;
        _active.Add(a);

        // 패시브 중 StatMod만 즉시 적용
        foreach (var p in a.Passives)
            foreach (var e in p.Effects)
                if (e.Trigger == TriggerType.Passive && e.Effect == EffectType.StatMod)
                    _stats.AddModifier(new StatModifier(
                        e.Stat, e.ModType, e.GetValue(), sourceId));
    }

    // ---------- 트리거 진입점 ----------

    public void FireTrigger(TriggerType trigger, GameObject other = null)
    {
        if (!HasStateAuthority) return;
        if (IsSealed && trigger != TriggerType.OnHitTaken) return;

        foreach (var ability in _active)
        {
            foreach (var p in ability.Passives)
                foreach (var e in p.Effects)
                    if (e.Trigger == trigger) Execute(e, other);

            foreach (var s in ability.Skills)
                foreach (var e in s.Effects)
                    if (e.Trigger == trigger) Execute(e, other);
        }
    }

    // ---------- 스킬 사용 ----------

    public bool TryUseSkill(int index)
    {
        if (!HasStateAuthority) return false;
        if (IsSealed) return false;

        var skill = GetSkill(index, out var owner);
        if (skill == null) return false;

        if (Runner.SimulationTime < SkillCooldownEnd[index]) return false;
        if (_health.Soul < skill.SoulCost) return false;

        float cdr = _stats.Get(StatType.CooldownReduction);
        float cd = skill.Cooldown * (1f - Mathf.Clamp01(cdr));
        SkillCooldownEnd.Set(index, Runner.SimulationTime + cd);

        if (skill.SoulCost > 0) _health.SpendSoul(skill.SoulCost);

        foreach (var e in skill.Effects)
            if (e.Trigger == TriggerType.OnSkillUse) Execute(e, null);

        FireTrigger(TriggerType.OnSkillUse);
        return true;
    }

    public AbilityData.SkillDef GetSkill(int index, out AbilityData owner)
    {
        int i = 0;
        foreach (var a in _active)
            foreach (var s in a.Skills)
            {
                if (i == index) { owner = a; return s; }
                i++;
            }
        owner = null;
        return null;
    }

    public float GetCooldownRemain(int index) =>
        Mathf.Max(0f, SkillCooldownEnd[index] - Runner.SimulationTime);

    // ---------- 효과 실행 ----------

    void Execute(AbilityData.EffectDef e, GameObject other)
    {
        if (e.Chance < 1f && Random.value > e.Chance) return;

        GameObject target = ResolveTarget(e.Target, other);
        if (target == null && e.Target != TargetType.AreaAroundSelf) return;

        // 대상별 쿨타임 (금제처단 등)
        if (e.PerTargetCooldown > 0f && target != null)
        {
            int key = target.GetInstanceID();
            if (_perTargetCd.TryGetValue(key, out float until) &&
                Runner.SimulationTime < until) return;
            _perTargetCd[key] = Runner.SimulationTime + e.PerTargetCooldown;
        }

        switch (e.Effect)
        {
            case EffectType.StatMod:
                ApplyTempStat(target, e);
                break;

            case EffectType.Damage:
                if (target.TryGetComponent<PlayerHealth>(out var h))
                    h.RPC_TakeDamage(Mathf.RoundToInt(e.GetValue()),
                                     Object.InputAuthority, transform.position);
                break;

            case EffectType.Heal:
                if (target.TryGetComponent<PlayerHealth>(out var hh))
                    hh.RPC_Heal(Mathf.RoundToInt(e.GetValue()));
                break;

            case EffectType.Seal:
                if (target.TryGetComponent<AbilityRuntime>(out var ar))
                    ar.RPC_ApplySeal(e.GetValue());
                break;

            case EffectType.GrantSoul:
                if (target.TryGetComponent<PlayerHealth>(out var sh))
                    sh.RPC_GainSoul(Mathf.RoundToInt(e.GetValue()));
                break;

            // Slow, Blind, Dash, Projectile, MarkTarget은 이후 구현
            default:
                Debug.Log($"[Ability] 미구현 효과: {e.Effect}");
                break;
        }
    }

    GameObject ResolveTarget(TargetType t, GameObject other) => t switch
    {
        TargetType.Self => gameObject,
        TargetType.Attacker or TargetType.Victim or
        TargetType.Killer or TargetType.AimTarget => other,
        _ => null,
    };

    void ApplyTempStat(GameObject target, AbilityData.EffectDef e)
    {
        if (!target.TryGetComponent<PlayerStats>(out var st)) return;

        int src = SourceId.BuffBase + Runner.Tick;
        st.AddModifier(new StatModifier(e.Stat, e.ModType, e.GetValue(), src));

        if (e.Duration > 0f)
            StartCoroutine(RemoveAfter(st, src, e.Duration));
    }

    System.Collections.IEnumerator RemoveAfter(PlayerStats st, int src, float t)
    {
        yield return new WaitForSeconds(t);
        st.RemoveBySource(src);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplySeal(float seconds)
    {
        SealTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    // ---------- 입력 ----------

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (Input.GetKeyDown(KeyCode.Q)) TryUseSkill(0);
        if (Input.GetKeyDown(KeyCode.E)) TryUseSkill(1);
    }
}