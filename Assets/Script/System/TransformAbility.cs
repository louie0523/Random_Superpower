using System;
using Fusion;
using UnityEngine;

/// <summary>변신 형태 하나 — 모양 · 스탯은 AlterData(변신 켜기), 나머지는 여기</summary>
[Serializable]
public class TransformForm
{
    [Tooltip("모양 · 깡스탯 — AlterData의 '변신(IsTransform)'을 켤 것. 모델은 Attach = Root 권장")]
    public AlterData Alter;

    [Header("기본 공격 (좌클릭)")]
    [Tooltip("비우면 기본 공격 없음. 스텝의 '판정 덮어쓰기'를 켜고 범위 · 피해 배율(1.5 = 공격력 150%)을 넣을 것 — 무기 판정")]
    public AbilityMotionData Attack;
    [Tooltip("공격할 때 모델이 재생할 RigAnimator 상태")]
    public string AttackAnim = "Attack";

    [Header("비행 (Space를 누르는 동안)")]
    public bool CanFly;
    [Tooltip("한 번에 날 수 있는 시간 (초) — 땅에 닿으면 다시 채워짐")]
    public float FlyTime = 5f;
    [Tooltip("올라가는 속도 (m/초)")]
    public float FlyUpSpeed = 6f;
    public string FlyAnim = "Flying";
}

/// <summary>
/// 변신하는 능력의 공통 부분. 변신 중:
///   몸 · 손 · 무기 숨김 (변신 모델만 보임 — InvisibleVisual), 시점은 AlterData 설정 (3인칭 등)
///   무기 · 아이템 교체 잠금, 기존 스킬 · 궁극기 잠금 (패시브는 그대로), 좌클릭 = 형태의 기본 공격 (무기 스탯 그대로 — 판정은 무기 판정)
///   죽으면 풀림
/// 상속한 능력은 BeginForm / EndForm만 부르면 됨. Tick · OnEquip 등을 덮어쓰면 base를 꼭 부를 것.
/// </summary>
public abstract class TransformAbility : AbilityBehaviour
{
    const int EvAttack = 900, EvFlyOn = 901, EvFlyOff = 902;

    protected PlayerAlter Alter { get; private set; }
    protected PlayerMovement Movement { get; private set; }
    protected PlayerEquipment Equip { get; private set; }
    protected PlayerAttack Attack { get; private set; }

    /// <summary>지금 형태 (권한자 기준). 변신 안 했으면 null</summary>
    protected TransformForm Form { get; private set; }
    public bool IsTransformed => Form != null;

    private float _fuel;
    private bool _flying;
    private TransformForm[] _forms;

    /// <summary>이 능력의 모든 형태 — 다른 화면에서 이벤트를 받을 때 형태 번호로 씀</summary>
    protected abstract TransformForm[] Forms { get; }

    /// <summary>변신 중에도 쓸 수 있는 이 능력의 스킬인가 (기본: 이 능력의 스킬은 전부)</summary>
    protected virtual bool AllowedWhileTransformed(AbilityData.SkillDef skill) =>
        Data.Skills != null && Array.IndexOf(Data.Skills, skill) >= 0;

    /// <summary>변신해야만 쓸 수 있는 이 능력의 스킬인가 (기본: 없음)</summary>
    protected virtual bool NeedsTransform(AbilityData.SkillDef skill) => false;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        Alter = Ctx.GetComponent<PlayerAlter>();
        Movement = Ctx.GetComponent<PlayerMovement>();
        Equip = Ctx.GetComponent<PlayerEquipment>();
        Attack = Ctx.GetComponent<PlayerAttack>();
        _forms = Forms;
        Form = null;
        if (Ctx.HasStateAuthority && Ctx.Health != null) Ctx.Health.Died += OnDiedBase;
    }

    public override void OnUnequip()
    {
        if (Ctx.Health != null) Ctx.Health.Died -= OnDiedBase;
        if (Ctx.HasStateAuthority) EndForm();
    }

    void OnDiedBase(PlayerRef killer, DeathCause cause)
    {
        EndForm();
        OnDiedWhileEquipped(killer, cause);
    }

    /// <summary>죽었을 때 (변신은 이미 풀림)</summary>
    protected virtual void OnDiedWhileEquipped(PlayerRef killer, DeathCause cause) { }

    // ══════════════════ 변신 (권한자) ══════════════════

    protected void BeginForm(TransformForm f)
    {
        if (!Ctx.HasStateAuthority || f == null || f.Alter == null) return;
        if (Form == f) return;

        if (Form != null && Form.Alter != null) Alter.Remove(Form.Alter, true);
        Form = f;
        Alter.Add(f.Alter);

        if (Equip != null) Equip.Locked = true;
        if (Attack != null) Attack.ForceMelee = true;
        _fuel = f.FlyTime;
        SetFlying(false);
    }

    protected void EndForm()
    {
        if (!Ctx.HasStateAuthority || Form == null) return;

        SetFlying(false);
        if (Alter != null && Form.Alter != null) Alter.Remove(Form.Alter, true);
        Form = null;

        if (Equip != null) Equip.Locked = false;
        if (Attack != null) Attack.ForceMelee = false;
    }

    // ══════════════════ 진행 (권한자) ══════════════════

    public override void Tick(float dt)
    {
        if (Form == null || Movement == null) return;
        TickFlight(dt);
    }

    void TickFlight(float dt)
    {
        if (!Form.CanFly) return;

        if (Movement.Grounded && !_flying) _fuel = Form.FlyTime;      // 땅에 닿으면 다시 채움

        bool want = Movement.JumpHeld && _fuel > 0f && !Ctx.IsStunned;
        if (want) _fuel -= dt;
        if (want != _flying) SetFlying(want);
    }

    void SetFlying(bool on)
    {
        if (Movement != null)
        {
            Movement.Flying = on;
            if (Form != null) Movement.FlyUpSpeed = Form.FlyUpSpeed;
        }
        if (_flying == on) return;
        _flying = on;
        if (Form != null) Broadcast(on ? EvFlyOn : EvFlyOff, new Vector3(FormIndex(Form), 0f, 0f));
    }

    /// <summary>남은 비행 시간 비율 (HUD용, 권한자)</summary>
    public float FlyFuel01 => Form != null && Form.CanFly && Form.FlyTime > 0f ? Mathf.Clamp01(_fuel / Form.FlyTime) : 0f;

    // ══════════════════ 공격 · 스킬 ══════════════════

    public override bool TryOverrideAttack(out AttackOverride ovr)
    {
        ovr = AttackOverride.None;
        if (Form == null || Form.Attack == null || Form.Attack.Step == null) return false;

        ovr.Step = Form.Attack.Step;
        ovr.DamageMul = 1f;
        Broadcast(EvAttack, new Vector3(FormIndex(Form), 0f, 0f));
        return true;
    }

    public override ComboStep GetOverrideStep()
    {
        var f = Form ?? CurrentFormFromAlter();
        return f != null && f.Attack != null ? f.Attack.Step : null;
    }

    public override bool CanUseSkill(AbilityData.SkillDef skill)
    {
        bool transformed = Form != null || CurrentFormFromAlter() != null;
        if (transformed) return AllowedWhileTransformed(skill);
        return !NeedsTransform(skill);
    }

    /// <summary>다른 화면 — 지금 붙은 변화로 형태 찾기</summary>
    protected TransformForm CurrentFormFromAlter()
    {
        if (Alter == null || _forms == null) return null;
        foreach (var f in _forms)
            if (f != null && f.Alter != null && Alter.Has(f.Alter)) return f;
        return null;
    }

    int FormIndex(TransformForm f) => _forms != null ? Array.IndexOf(_forms, f) : -1;

    TransformForm FormAt(int i) => _forms != null && i >= 0 && i < _forms.Length ? _forms[i] : null;

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key < EvAttack || key > EvFlyOff) { OnFormEvent(key, pos); return; }

        var f = FormAt(Mathf.RoundToInt(pos.x));
        if (f == null || Alter == null) return;

        switch (key)
        {
            case EvAttack:
                Alter.PlayPartState(f.Alter, f.AttackAnim);
                break;
            case EvFlyOn:
                Alter.PlayPartState(f.Alter, f.FlyAnim, 999f);
                break;
            case EvFlyOff:
                Alter.ReleasePartState(f.Alter);
                break;
        }
    }

    /// <summary>상속한 능력의 연출 이벤트 (키 900~902는 쓰지 말 것)</summary>
    protected virtual void OnFormEvent(int key, Vector3 pos) { }
}
