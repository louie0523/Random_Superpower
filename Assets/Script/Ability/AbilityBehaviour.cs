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
}