using UnityEngine;

/// <summary>능력별 전용 로직. AbilityData에 연결해서 사용.</summary>
public abstract class AbilityBehaviour : ScriptableObject
{
    [System.NonSerialized] protected AbilityRuntime Ctx;
    [System.NonSerialized] protected AbilityData Data;

    public AbilityBehaviour CreateInstance(AbilityRuntime ctx, AbilityData data)
    {
        var inst = Instantiate(this);
        inst.Ctx = ctx;
        inst.Data = data;
        return inst;
    }

    public virtual void OnEquip() { }
    public virtual void OnUnequip() { }
    public virtual void Tick(float dt) { }
    public virtual void OnTrigger(TriggerType t, GameObject other) { }

    /// <summary>true 반환 시 데이터 기반 Effect 처리를 건너뜀</summary>
    public virtual bool OnSkillUsed(int slot, AbilityData.SkillDef skill) => false;

    public virtual void OnStateEnter(int stateId) { }
    public virtual void OnStateExit(int stateId) { }

    /// <summary>기본 공격을 대체. true면 ovr의 내용이 적용됨</summary>
    public virtual bool TryOverrideAttack(out AttackOverride ovr)
    {
        ovr = AttackOverride.None;
        return false;
    }

    // ══════════════════ 스탯 소스 ══════════════════

    /// <summary>Behaviour 전용 스탯 소스 ID. 능력마다 10칸 (n = 0~9)</summary>
    protected int Src(int n = 0) => SourceId.AbilityBase + Data.Id * 10 + n;

    // ══════════════════ 레벨 ══════════════════
    // 항목 번호(local) = 패시브 먼저 0, 1, … 이어서 스킬
    // 예) 패시브 2개 + 스킬 3개 → 패시브 0,1 / 스킬 2,3,4

    protected int PassiveEntry(int passiveIndex) => passiveIndex;
    protected int SkillEntry(int skillIndex) => (Data.Passives?.Length ?? 0) + skillIndex;

    /// <summary>항목의 현재 레벨 (0 = Lv1). 랜덤 능력은 항상 0</summary>
    protected int RankOf(int localEntry) => Ctx.GetRank(Data, localEntry);

    /// <summary>항목의 Vars[varIndex]를 현재 레벨 값으로. 없으면 fallback</summary>
    protected float Var(int localEntry, int varIndex, float fallback)
    {
        var vars = GetVars(localEntry);
        if (vars == null || varIndex < 0 || varIndex >= vars.Length || vars[varIndex] == null)
            return fallback;

        return vars[varIndex].Get(RankOf(localEntry));
    }

    AbilityData.RankVar[] GetVars(int localEntry)
    {
        if (Data == null || localEntry < 0) return null;

        int p = Data.Passives?.Length ?? 0;
        if (localEntry < p) return Data.Passives[localEntry]?.Vars;

        int s = localEntry - p;
        return (Data.Skills != null && s < Data.Skills.Length) ? Data.Skills[s]?.Vars : null;
    }
}