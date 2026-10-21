using Fusion;
using UnityEngine;

/// <summary>
/// 투명 망토 — F 켜기/끄기. 켜면 '투명'(몸 · 손 안 보임, 무기는 보임) + 가하는 모든 피해 -50%. 끄면 둘 다 해제.
/// 데이터: 스킬의 '효과 끝나고 쿨타임'(CooldownAfterEffect)을 켤 것 → 끌 때부터 쿨타임.
/// 피해 감소는 맞는 쪽에서 계산 (DamageRules — 망토를 가진 사람이 투명일 때).
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Cloak")]
public class CloakAbility : AbilityBehaviour
{
    public int CloakSkill = 0;

    [Tooltip("켜져 있는 동안 가하는 피해 감소 (%)")]
    public float DamagePenalty = 50f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject OnFx;
    public GameObject OffFx;
    const int EvOn = 0, EvOff = 1;

    private bool _on;
    private PlayerHealth _health;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Register() => DamageRules.Register(Rule);

    static float Rule(PlayerHealth target, NetworkObject attacker, DamageInfo info)
    {
        if (attacker == null) return 1f;
        if (!attacker.TryGetComponent<PlayerStatus>(out var st) || !st.Has(StatusType.Invisible)) return 1f;
        if (!attacker.TryGetComponent<AbilityRuntime>(out var ar)) return 1f;

        var me = ar.FindAbility<CloakAbility>();
        return me != null ? Mathf.Clamp01(1f - me.DamagePenalty * 0.01f) : 1f;
    }

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == CloakSkill;

    public override void OnEquip()
    {
        _on = false;
        _health = Ctx.Health;
        if (_health != null && Ctx.HasStateAuthority) _health.Died += OnDied;
    }

    public override void OnUnequip()
    {
        if (_health != null) _health.Died -= OnDied;
        if (_on && Ctx.HasStateAuthority) SetOn(false);
    }

    void OnDied(PlayerRef killer, DeathCause cause) => _on = false;      // 상태이상은 사망 때 전부 풀림

    public override void Tick(float dt)
    {
        // 꺼져 있는데 쿨타임이 대기 중으로 남았으면 바로 시작 (켜 둔 채 대기 한도가 지난 뒤 끈 경우)
        if (_on || Data.Skills == null || CloakSkill >= Data.Skills.Length) return;
        int slot = Ctx.SlotOf(Data.Skills[CloakSkill]);
        if (Ctx.IsCooldownHeld(slot)) ReleaseCooldown(CloakSkill);
    }

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        SetOn(!_on);
        if (!_on) ReleaseCooldown(CloakSkill);
        return true;
    }

    /// <summary>켜져 있는 동안(쿨타임 대기 중) 다시 누름 → 끔</summary>
    public override bool OnSkillRecast(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill) || !_on) return false;
        SetOn(false);
        ReleaseCooldown(CloakSkill);
        return true;
    }

    public override bool CanRecast(AbilityData.SkillDef skill) => IsMine(skill) && _on;

    void SetOn(bool on)
    {
        _on = on;
        if (!Ctx.TryGetComponent<PlayerStatus>(out var st)) return;

        if (on) st.Apply(StatusType.Invisible, 1f, 99999f, Ctx.Object.InputAuthority);
        else st.Clear(StatusType.Invisible);

        Broadcast(on ? EvOn : EvOff, Ctx.transform.position + Vector3.up);
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        Ctx.PlayEffectLocal(key == EvOn ? OnFx : OffFx, pos);
    }
}
