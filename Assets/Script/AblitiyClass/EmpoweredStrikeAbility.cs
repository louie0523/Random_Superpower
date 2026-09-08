using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Behaviour/Empowered Strike")]
public class EmpoweredStrikeAbility : AbilityBehaviour
{
    [Header("게이지")]
    [Range(0, 3)] public int ChargeSlot = 0;
    public float MaxCharge = 100f;
    public float ChargePerSecondMoving = 3f;
    public float ChargePerHit = 5f;

    [Header("일격")]
    public float StrikeCost = 100f;
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

    [Header("일격의 대가 상태")]
    public int StateId = 6001;
    public float MasteryDuration = 8f;
    public float MasterySpeedBonus = 2f;
    [Tooltip("상태 중 게이지 획득 배율")]
    public float MasteryChargeMul = 3f;


    private float _strikeFlagTimer;
    private float _moveAccum;
    private int _speedSrc;

    /// <summary>일격이 방금 적중했는가 (상태 중 만충 판정용)</summary>
    private bool _strikeInProgress;

    public float ChargeRatio => Ctx.GetChargeRatio(ChargeSlot);
    public bool IsReady => Ctx.GetCharge(ChargeSlot) >= StrikeCost;
    public bool InMastery => Ctx.HasState(StateId);

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        Ctx.SetMaxCharge(ChargeSlot, MaxCharge);
        _moveAccum = 0f;
        _strikeInProgress = false;
    }

    public override void OnUnequip()
    {
        if (_speedSrc != 0) Ctx.Stats?.RemoveBySource(_speedSrc);
    }

    public override void Tick(float dt)
    {
        if (_strikeFlagTimer > 0f)
        {
            _strikeFlagTimer -= dt;
            if (_strikeFlagTimer <= 0f) _strikeInProgress = false;
        }

        if (!Ctx.IsMoving) { _moveAccum = 0f; return; }

        _moveAccum += dt;
        if (_moveAccum >= 1f)
        {
            _moveAccum -= 1f;
            Ctx.AddCharge(ChargeSlot, ChargePerSecondMoving * ChargeMul);
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

        if (Ctx.GetCharge(ChargeSlot) < StrikeCost) return false;
        if (!Ctx.SpendCharge(ChargeSlot, StrikeCost)) return false;

        _strikeInProgress = true;   // ★ 이번 공격은 일격

        ovr.Step = StrikeMotion != null ? StrikeMotion.Step : null;
        ovr.DamageMul = StrikeDamageMul;
        ovr.Dash = Dash;
        ovr.HasDash = true;
        _strikeInProgress = true;
        _strikeFlagTimer = 1f;
        return true;
    }

    // ══════════════════ 상태 진입/해제 ══════════════════

    public override void OnStateEnter(int stateId)
    {
        if (stateId != StateId) return;

        _speedSrc = SourceId.BuffBase + StateId;
        Ctx.Stats?.RemoveBySource(_speedSrc);   // 중복 방지
        Ctx.Stats?.AddModifier(new StatModifier(
            StatType.MoveSpeed, ModType.Flat, MasterySpeedBonus, _speedSrc));
    }

    public override void OnStateExit(int stateId)
    {
        if (stateId != StateId) return;
        Ctx.Stats?.RemoveBySource(_speedSrc);
        _strikeInProgress = false;
        _strikeFlagTimer = 0f;
    }

    public ComboStep GetStrikeStep() => StrikeMotion?.Step;
}