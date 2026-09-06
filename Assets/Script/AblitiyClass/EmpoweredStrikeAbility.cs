using UnityEngine;

[CreateAssetMenu(menuName = "Game/Ability Behaviour/Empowered Strike")]
public class EmpoweredStrikeAbility : AbilityBehaviour
{
    [Header("게이지")]
    public float MaxCharge = 100f;
    public float ChargePerSecondMoving = 3f;
    public float ChargePerHit = 5f;

    [Header("일격")]
    public float StrikeCost = 100f;
    public float StrikeDamageMul = 4f;
    public AbilityMotionData StrikeMotion;

    [Header("돌진")]
    public DashParams Dash = new()
    {
        Distance = 6f,
        Duration = 0.2f,
        PassThroughPlayers = true,
        StopOnContact = false,
    };


    [Header("숨고르기")]
    public float BreathCharge = 50f;

    [Header("대가 상태")]
    public int StateId = 6001;
    public float MasteryDuration = 8f;
    public float MasterySpeedBonus = 2f;

    private float _moveAccum;
    private int _speedSourceId;

    // ── 조회 ──
    public float ChargeRatio => Ctx.MaxCharge <= 0f ? 0f : Ctx.Charge / Ctx.MaxCharge;
    public bool IsReady => Ctx.Charge >= StrikeCost;
    public bool InMastery => Ctx.HasState(StateId);

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        Ctx.SetMaxCharge(MaxCharge);
        _moveAccum = 0f;
    }

    public override void OnUnequip()
    {
        if (_speedSourceId != 0) Ctx.Stats?.RemoveBySource(_speedSourceId);
    }

    public override void Tick(float dt)
    {
        if (!Ctx.IsMoving) { _moveAccum = 0f; return; }

        _moveAccum += dt;
        if (_moveAccum >= 1f)
        {
            _moveAccum -= 1f;
            Ctx.AddCharge(ChargePerSecondMoving);
        }
    }

    // ══════════════════ 트리거 ══════════════════

    public override void OnTrigger(TriggerType t, GameObject other)
    {
        if (t != TriggerType.OnHit) return;

        // 대가 상태면 즉시 만충
        Ctx.AddCharge(InMastery ? MaxCharge : ChargePerHit);
    }

    // ══════════════════ 스킬 ══════════════════

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        Debug.Log($"[Strike] OnSkillUsed slot={slot}");

        if (slot == (int)SkillSlot.Skill1)
        {
            Ctx.AddCharge(BreathCharge);
            Debug.Log($"[Strike] 숨고르기 → {Ctx.Charge}");
            return true;
        }

        if (slot == (int)SkillSlot.Ultimate)
        {
            Ctx.EnterState(StateId, MasteryDuration);
            return true;
        }

        return false;
    }

    // ══════════════════ 기본 공격 대체 ══════════════════


    public override bool TryOverrideAttack(out AttackOverride ovr)
    {
        ovr = AttackOverride.None;

        if (Ctx.Charge < StrikeCost) return false;
        if (!Ctx.SpendCharge(StrikeCost)) return false;

        ovr.Step = StrikeMotion != null ? StrikeMotion.Step : null;
        ovr.DamageMul = StrikeDamageMul;
        ovr.Dash = Dash;
        ovr.HasDash = true;
        return true;
    }

    // ══════════════════ 상태 ══════════════════

    public override void OnStateEnter(int stateId)
    {
        if (stateId != StateId) return;

        _speedSourceId = SourceId.BuffBase + StateId;
        Ctx.Stats?.AddModifier(new StatModifier(
            StatType.MoveSpeed, ModType.Flat, MasterySpeedBonus, _speedSourceId));
    }

    public override void OnStateExit(int stateId)
    {
        if (stateId != StateId) return;
        Ctx.Stats?.RemoveBySource(_speedSourceId);
    }

    // ══════════════════ 외부 참조 ══════════════════

    public ComboStep GetStrikeStep() => StrikeMotion?.Step;
    public float GetCameraRoll() => StrikeMotion != null ? StrikeMotion.CameraRoll : -10f;
}