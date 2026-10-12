using System.Collections.Generic;
using UnityEngine;

/// <summary>능력별 전용 로직. AbilityData에 연결해서 사용.</summary>
public abstract class AbilityBehaviour : ScriptableObject
{
    [System.NonSerialized] protected AbilityRuntime Ctx;
    [System.NonSerialized] protected AbilityData Data;



    // ══════════════════ 설명 ══════════════════

    /// <summary>이 인스턴스가 속한 능력 데이터 (설명 토큰이 찾을 때 씀)</summary>
    public AbilityData SourceData => Data;

    /// <summary>설명의 {x:키} 자리에 넣을 글. null·빈 문자열이면 그 줄째 사라짐</summary>
    public virtual string DescToken(string key, int rank) => null;

    /// <summary>{dmg:계수필드} 표시에 곱할 배율 (이름·상태 보정 등)</summary>
    public virtual float DescDamageMul(string coefField, int rank) => 1f;

    public AbilityBehaviour CreateInstance(AbilityRuntime ctx, AbilityData data)
    {
        var inst = Instantiate(this);
        inst.Ctx = ctx;
        inst.Data = data;
        return inst;
    }

    /// <summary>누르기 전 확인 — false면 스킬이 안 나가고 쿨타임·소울도 안 씀 (뱉어내기·무구 섭취 등)</summary>
    public virtual bool CanUseSkill(AbilityData.SkillDef skill) => true;

    /// <summary>쿨타임·지속 중에 스킬 키를 다시 눌렀을 때. true면 소모(쿨 그대로)</summary>
    public virtual bool OnSkillRecast(int slot, AbilityData.SkillDef skill) => false;

    /// <summary>지금 다시 누르기가 가능한지 (슬롯 UI 강조용)</summary>
    public virtual bool CanRecast(AbilityData.SkillDef skill) => false;

    /// <summary>이 Behaviour가 속한 능력 ID (원격 이벤트 확인용)</summary>
    public int AbilityId => Data != null ? Data.Id : 0;

    /// <summary>SA가 Broadcast로 보낸 이벤트를 모든 클라이언트가 받음 (연출용)</summary>
    public virtual void OnRemoteEvent(int key, Vector3 pos) { }

    /// <summary>모든 클라이언트에 이벤트 전송. SA에서만 동작</summary>
    protected void Broadcast(int key, Vector3 pos) => Ctx.BroadcastBehaviourEvent(this, key, pos);

    /// <summary>TryOverrideAttack이 쓰는 모션. 원격 클라이언트가 같은 모션을 재생할 때 사용</summary>
    public virtual ComboStep GetOverrideStep() => null;
    public virtual void OnEquip() { }
    public virtual void OnUnequip() { }
    public virtual void Tick(float dt) { }
    public virtual void OnTrigger(TriggerType t, GameObject other) { }

    /// <summary>true 반환 시 데이터 기반 Effect 처리를 건너뜀</summary>
    public virtual bool OnSkillUsed(int slot, AbilityData.SkillDef skill) => false;

    public virtual void OnStateEnter(int stateId) { }
    public virtual void OnStateExit(int stateId) { }

    /// <summary>
    /// 스킬 효과 시점 — 데이터의 '효과 타이밍'(판정 프레임 / 모션 종료 / 즉시)에 맞춰 호출.
    /// true면 데이터 Effects 건너뜀
    /// </summary>
    public virtual bool OnSkillEffect(AbilityData.SkillDef skill) => false;

    // ── 이어지는 모션 (2타 등) ──

    /// <summary>지금 모션이 끝나면 tag 모션을 이어서 재생 → 그 판정 프레임에 OnChainHit(tag)</summary>
    protected void ChainMotion(int tag) => Ctx.QueueChain(this, tag);

    /// <summary>tag → 모션 (모든 클라이언트에서 같은 모션을 재생하는 데 씀)</summary>
    public virtual AbilityMotionData GetChainMotion(int tag) => null;

    /// <summary>이어진 모션의 판정 프레임</summary>
    public virtual void OnChainHit(int tag) { }

    /// <summary>기본 공격을 대체. true면 ovr의 내용이 적용됨</summary>
    public virtual bool TryOverrideAttack(out AttackOverride ovr)
    {
        ovr = AttackOverride.None;
        return false;
    }

    /// <summary>'효과 끝나고 쿨타임' 스킬의 쿨타임을 지금 시작. 그 옵션이 꺼져 있거나 대기 중이 아니면 아무 일 없음</summary>
    protected void ReleaseCooldown(int skillIndex)
    {
        if (Data?.Skills == null || skillIndex < 0 || skillIndex >= Data.Skills.Length) return;
        Ctx.ReleaseCooldown(Data.Skills[skillIndex]);
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

    // ══════════════════ 레벨별 수치 (LevelValue) ══════════════════

    [System.NonSerialized] private Dictionary<LevelValue, int> _lvEntry;

    /// <summary>
    /// [ForSkill]/[ForPassive]가 붙은 LevelValue를 그 항목의 현재 레벨 값으로.
    /// 붙어 있지 않으면 첫 값(Lv1)
    /// </summary>
    protected float Lv(LevelValue v)
    {
        if (v == null) return 0f;
        BuildLevelMap();
        return v.At(_lvEntry.TryGetValue(v, out int e) && e >= 0 ? RankOf(e) : 0);
    }

    void BuildLevelMap()
    {
        if (_lvEntry != null) return;
        _lvEntry = new Dictionary<LevelValue, int>();

        foreach (var f in GetType().GetFields(LevelBinding.Flags))
            if (LevelBinding.TryGet(f, this, out bool isSkill, out int idx, out _) && f.GetValue(this) is LevelValue lv)
                _lvEntry[lv] = isSkill ? SkillEntry(idx) : PassiveEntry(idx);
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