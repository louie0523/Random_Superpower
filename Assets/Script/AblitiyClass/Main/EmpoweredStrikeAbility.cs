using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Behaviour/Empowered Strike")]
public class EmpoweredStrikeAbility : AbilityBehaviour
{
    [Header("게이지")]
    [Range(0, 3)] public int ChargeSlot = 0;
    public float MaxCharge = 100f;

    [Tooltip("이동 몇 m마다 충전할지")]
    public float ChargePerDistance = 7.5f;
    [Tooltip("위 거리를 이동할 때마다 얻는 게이지")]
    public float ChargeOnDistance = 3f;

    public float ChargePerHit = 5f;

    [Header("일격")]
    public float StrikeCost = 100f;
    [Tooltip("레벨 연동 Var가 없을 때 쓰는 기본 배율 (4 = 400%)")]
    public float StrikeDamageMul = 4f;
    public AbilityMotionData StrikeMotion;
    public DashParams Dash = new()
    {
        Distance = 6f,
        Duration = 0.2f,
        Direction = DashDirection.Horizontal,
        PassThroughPlayers = true,
        StopOnContact = false,
    };

    [Header("일격 피해 계수 — 레벨 연동")]
    [Tooltip("계수 Var가 있는 항목이 스킬이면 체크, 패시브면 해제")]
    public bool StrikeFromSkill = false;
    [Tooltip("그 항목의 번호 (Passives 또는 Skills 배열 안에서)")]
    public int StrikeIndex = 0;
    [Tooltip("그 항목 Vars 중 몇 번째가 피해 계수(%)인지")]
    public int StrikeVarIndex = 0;

    [Header("일격의 대가 상태")]
    public int StateId = 6001;
    public float MasteryDuration = 8f;
    public float MasterySpeedBonus = 2f;
    [Tooltip("상태 중 게이지 획득 배율")]
    public float MasteryChargeMul = 3f;

    private float _strikeFlagTimer;
    private int _speedSrc;
    private Vector3 _lastPos;
    private float _distAccum;

    /// <summary>일격이 방금 적중했는가 (상태 중 만충 판정용)</summary>
    private bool _strikeInProgress;

    public float ChargeRatio => Ctx.GetChargeRatio(ChargeSlot);
    public bool IsReady => Ctx.GetCharge(ChargeSlot) >= StrikeCost;
    public bool InMastery => Ctx.HasState(StateId);

    int StrikeEntry => StrikeFromSkill ? SkillEntry(StrikeIndex) : PassiveEntry(StrikeIndex);

    /// <summary>현재 레벨의 일격 배율 (4 = 400%)</summary>
    public float CurrentStrikeMul => Var(StrikeEntry, StrikeVarIndex, StrikeDamageMul * 100f) / 100f;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _speedSrc = SourceId.StateBase + StateId;   // 항상 고정값 (재빌드 후에도 같은 소스)

        Ctx.SetMaxCharge(ChargeSlot, MaxCharge);
        _distAccum = 0f;
        _lastPos = Ctx.transform.position;
        _strikeInProgress = false;
        _strikeFlagTimer = 0f;

        // 상태 도중 재빌드(사망 리롤·진화)되면 이속 버프 복원
        if (InMastery) ApplySpeed();
    }

    public override void OnUnequip() => RemoveSpeed();

    public override void Tick(float dt)
    {
        if (_strikeFlagTimer > 0f)
        {
            _strikeFlagTimer -= dt;
            if (_strikeFlagTimer <= 0f) _strikeInProgress = false;
        }

        TickDistanceCharge();
    }

    void TickDistanceCharge()
    {
        Vector3 now = Ctx.transform.position;
        Vector3 delta = now - _lastPos;
        _lastPos = now;

        // 수평 이동만 (낙하는 제외)
        delta.y = 0f;
        float moved = delta.magnitude;

        // 순간이동/리스폰 방지
        if (moved > 3f) return;

        _distAccum += moved;

        float unit = Mathf.Max(0.1f, ChargePerDistance);
        while (_distAccum >= unit)
        {
            _distAccum -= unit;
            Ctx.AddCharge(ChargeSlot, ChargeOnDistance * ChargeMul);
        }
    }

    /// <summary>상태에 따른 게이지 배율</summary>
    float ChargeMul => InMastery ? MasteryChargeMul : 1f;

    // ══════════════════ 트리거 ══════════════════

    public override void OnTrigger(TriggerType t, GameObject other)
    {
        if (t != TriggerType.OnHit) return;

        // 대가 상태 중 '일격'이 적중하면 즉시 만충
        if (InMastery && _strikeInProgress)
        {
            Ctx.AddCharge(ChargeSlot, MaxCharge);
            _strikeInProgress = false;
            return;
        }

        Ctx.AddCharge(ChargeSlot, ChargePerHit * ChargeMul);
    }

    // ══════════════════ 스킬 ══════════════════

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (slot == (int)SkillSlot.Ultimate)
        {
            Ctx.EnterState(StateId, MasteryDuration);
            return false;
        }
        return false;   // 숨고르기는 데이터가 처리
    }

    // ══════════════════ 기본 공격 대체 ══════════════════

    public override bool TryOverrideAttack(out AttackOverride ovr)
    {
        ovr = AttackOverride.None;

        var weapon = Ctx.Equipment?.GetWeapon();
        if (weapon == null || !weapon.IsArmedMelee) return false;

        var step = StrikeMotion != null ? StrikeMotion.Step : null;
        if (step == null) return false;

        if (Ctx.GetCharge(ChargeSlot) < StrikeCost) return false;
        if (!Ctx.SpendCharge(ChargeSlot, StrikeCost)) return false;

        ovr.Step = step;
        ovr.DamageMul = CurrentStrikeMul;       // ★ 레벨 연동
        ovr.Dash = Dash;
        ovr.HasDash = true;

        _strikeInProgress = true;
        _strikeFlagTimer = step.WindUp + step.ActiveWindow + 0.1f;

        return true;
    }

    // ══════════════════ 상태 진입/해제 ══════════════════

    public override void OnStateEnter(int stateId)
    {
        if (stateId != StateId) return;
        ApplySpeed();
    }

    public override void OnStateExit(int stateId)
    {
        if (stateId != StateId) return;
        RemoveSpeed();
        _strikeInProgress = false;
        _strikeFlagTimer = 0f;
    }

    // 스탯은 소유자 클라이언트 것만 의미 있음 → SA에서만 건드림
    void ApplySpeed()
    {
        if (!Ctx.HasStateAuthority) return;
        Ctx.Stats?.RemoveBySource(_speedSrc);   // 중복 방지
        Ctx.Stats?.AddModifier(new StatModifier(
            StatType.MoveSpeed, ModType.Flat, MasterySpeedBonus, _speedSrc));
    }

    void RemoveSpeed()
    {
        if (!Ctx.HasStateAuthority) return;
        Ctx.Stats?.RemoveBySource(_speedSrc);
    }

    /// <summary>근접 무기(맨손 제외)를 들고 있는가</summary>
    bool HasMeleeWeapon
    {
        get
        {
            var w = Ctx.Equipment?.GetWeapon();
            return w != null && w.IsArmedMelee;
        }
    }

    /// <summary>지금 일격을 쓸 수 있는 무기인가 — HUD 표시용</summary>
    public bool CanUseNow => HasMeleeWeapon;

    public ComboStep GetStrikeStep() => StrikeMotion?.Step;
}