using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 끼고 있는 아이템의 고유 특성(EquipmentData.Traits) + 방어구 특수 인챈트(ArmorEnchantData.Traits)를 돌림.
/// PlayerEquipment가 알아서 붙임 (프리팹에 안 넣어도 됨). 모든 판정 · 효과는 내 캐릭터 권한자(SA)에서만.
/// 칸: 0 든 무기 · 1 갑옷 · 2 투구 · 3 신발 · 4 갑옷 인챈트 · 5 투구 인챈트 · 6 신발 인챈트
/// 변신 중에도 그대로 돎 (스탯 · 이벤트 기반이라)
/// </summary>
public class ItemTraitRunner : MonoBehaviour
{
    public const int SlotCount = 7;
    const int MaxTraits = 16, MaxStacks = 50;

    class State
    {
        public float ReadyAt;
        public bool Latched;            // OnLowHealth — 내려갔다가 다시 올라와야 또
        public bool WhileOn;            // While · Always 버프가 걸려 있나
        public float NextPeriodic;
        public int Stacks;
        public float StackExpire;       // 0 = 안 사라짐
    }

    class Slot
    {
        public UnityEngine.Object Owner;
        public ItemTrait[] Traits;
        public State[] States;
    }

    readonly Slot[] _slots = new Slot[SlotCount];

    NetworkObject _no;
    PlayerEquipment _equip;
    PlayerHealth _hp;
    PlayerAttack _atk;
    AbilityRuntime _ab;
    PlayerStats _stats;
    PlayerEnchant _ench;
    PlayerStatus _status;
    bool _hooked, _wasDead;

    bool SA => _no != null && _no.IsValid && _no.HasStateAuthority;
    float Now => _no != null && _no.Runner != null ? _no.Runner.SimulationTime : Time.time;

    static int Src(int slot, int trait, int stack) => SourceId.TraitBase + slot * 1000 + trait * MaxStacks + stack;

    // ══════════════════ 연결 ══════════════════

    void Awake()
    {
        for (int i = 0; i < SlotCount; i++) _slots[i] = new Slot();
    }

    void Hook()
    {
        if (_hooked) return;
        if (_no == null) _no = GetComponent<NetworkObject>();
        if (!SA) return;
        _hooked = true;

        _equip = GetComponent<PlayerEquipment>();
        _hp = GetComponent<PlayerHealth>();
        _atk = GetComponent<PlayerAttack>();
        _ab = GetComponent<AbilityRuntime>();
        _stats = GetComponent<PlayerStats>();
        _ench = GetComponent<PlayerEnchant>();
        _status = GetComponent<PlayerStatus>();

        if (_atk != null)
        {
            _atk.WeaponHit += (t, dmg, ench) => Fire(TraitTrigger.OnWeaponHit, t, null);
            _atk.HeadshotHit += t => Fire(TraitTrigger.OnHeadshot, t, null);
        }
        if (_hp != null)
        {
            _hp.KilledPlayer += v => { Fire(TraitTrigger.OnKill, null, null); Fire(TraitTrigger.OnTakedown, null, null); };
            _hp.Assisted += () => { Fire(TraitTrigger.OnAssist, null, null); Fire(TraitTrigger.OnTakedown, null, null); };
            _hp.Damaged += OnDamaged;
            _hp.ShieldBroken += () => Fire(TraitTrigger.OnShieldBreak, null, null);
        }
        if (_ab != null) _ab.SkillUsed += OnSkillUsed;
    }

    void OnDamaged(DamageInfo info, int final)
    {
        if (info.IsDot) return;      // 지속 피해 · 특성 추가 피해로는 안 터짐 (연쇄 방지)
        var atk = info.Attacker != PlayerRef.None && _no.Runner != null ? _no.Runner.GetPlayerObject(info.Attacker) : null;
        Fire(TraitTrigger.OnHitTaken, null, atk);
    }

    void OnSkillUsed(int slot)
    {
        var s = (SkillSlot)slot;
        if (s == SkillSlot.Ultimate) Fire(TraitTrigger.OnUltimateUse, null, null);
        else if (s.IsRandom()) Fire(TraitTrigger.OnRandomUse, null, null);
        else Fire(TraitTrigger.OnSkillUse, null, null);
    }

    /// <summary>PlayerEquipment.ApplyAll — 칸의 특성 목록을 바꿈 (같은 아이템이면 그대로)</summary>
    public void SetSlot(int slot, UnityEngine.Object owner, ItemTrait[] traits)
    {
        Hook();
        if (slot < 0 || slot >= SlotCount) return;
        var s = _slots[slot];
        if (s.Owner == owner && s.Traits == traits) return;

        ClearSlot(slot);
        s.Owner = owner;
        if (traits != null && traits.Length > MaxTraits)
        {
            var cut = new ItemTrait[MaxTraits];
            System.Array.Copy(traits, cut, MaxTraits);
            traits = cut;
        }
        s.Traits = traits;
        s.States = s.Traits != null ? new State[s.Traits.Length] : null;
        if (s.States != null)
            for (int i = 0; i < s.States.Length; i++) s.States[i] = new State { NextPeriodic = Now + Period(s.Traits[i]) };
    }

    void ClearSlot(int slot)
    {
        var s = _slots[slot];
        if (_stats != null && s.Traits != null)
            for (int t = 0; t < s.Traits.Length; t++)
                for (int k = 0; k < MaxStacks; k++) _stats.RemoveBySource(Src(slot, t, k));
        if (_hp != null && s.Traits != null)
            for (int t = 0; t < s.Traits.Length; t++) _hp.RemoveTempShield(Src(slot, t, 0));
        s.Owner = null;
        s.Traits = null;
        s.States = null;
    }

    static float Period(ItemTrait t) => Mathf.Max(0.25f, t.Threshold);

    // ══════════════════ 주기 (SA) ══════════════════

    void Update()
    {
        Hook();
        if (!SA || _hp == null) return;
        float now = Now;

        bool dead = _hp.IsDead;
        if (dead && !_wasDead) OnDeath();
        if (!dead && _wasDead) Fire(TraitTrigger.OnRespawn, null, null);
        _wasDead = dead;

        float maxHp = _stats != null ? Mathf.Max(1f, _stats.Get(StatType.MaxHP)) : 100f;
        float hpPct = _hp.HP / maxHp * 100f;

        for (int si = 0; si < SlotCount; si++)
        {
            var s = _slots[si];
            if (s.Traits == null) continue;
            for (int ti = 0; ti < s.Traits.Length; ti++)
            {
                var t = s.Traits[ti];
                var st = s.States[ti];
                if (t == null) continue;

                // 쌓인 버프 — 시간 끝
                if (st.Stacks > 0 && st.StackExpire > 0f && now >= st.StackExpire) DropStacks(si, ti, st);

                if (dead) continue;

                switch (t.Trigger)
                {
                    case TraitTrigger.OnLowHealth:
                        if (hpPct < t.Threshold) { if (!st.Latched) { st.Latched = true; TryFire(si, ti, t, st, null, null); } }
                        else st.Latched = false;
                        break;

                    case TraitTrigger.Periodic:
                        if (now >= st.NextPeriodic) { st.NextPeriodic = now + Period(t); TryFire(si, ti, t, st, null, null); }
                        break;
                }

                if (t.IsWhile) SetWhile(si, ti, t, st, Condition(t, hpPct) && WeaponOk(t));
            }
        }
    }

    bool Condition(ItemTrait t, float hpPct) => t.Trigger switch
    {
        TraitTrigger.Always => true,
        TraitTrigger.WhileOutOfCombat => !_hp.InCombat,
        TraitTrigger.WhileInCombat => _hp.InCombat,
        TraitTrigger.WhileLowHealth => hpPct <= t.Threshold,
        TraitTrigger.WhileShielded => _hp.ShieldTotal > 0,
        _ => false,
    };

    /// <summary>조건 동안 켜지는 효과 — StatBuff는 걸고/떼고, 다른 효과는 켜지는 순간 한 번</summary>
    void SetWhile(int si, int ti, ItemTrait t, State st, bool on)
    {
        if (st.WhileOn == on) return;
        st.WhileOn = on;

        if (t.Effect == TraitEffect.StatBuff)
        {
            if (on) _stats?.AddModifier(new StatModifier(t.Stat, t.Mod, t.Value, Src(si, ti, 0)));
            else _stats?.RemoveBySource(Src(si, ti, 0));
            return;
        }
        if (on) TryFire(si, ti, t, st, null, null);
    }

    void OnDeath()
    {
        // 처치 등으로 쌓은 버프 · 조건 버프는 죽으면 사라짐 (Always는 부활해도 Update가 다시 걺)
        for (int si = 0; si < SlotCount; si++)
        {
            var s = _slots[si];
            if (s.Traits == null) continue;
            for (int ti = 0; ti < s.Traits.Length; ti++)
            {
                var st = s.States[ti];
                if (st.Stacks > 0) DropStacks(si, ti, st);
                if (st.WhileOn) { st.WhileOn = false; _stats?.RemoveBySource(Src(si, ti, 0)); }
                st.Latched = false;
            }
        }
    }

    void DropStacks(int si, int ti, State st)
    {
        for (int k = 0; k < st.Stacks && k < MaxStacks; k++) _stats?.RemoveBySource(Src(si, ti, k));
        st.Stacks = 0;
        st.StackExpire = 0f;
    }

    // ══════════════════ 발동 ══════════════════

    void Fire(TraitTrigger trigger, PlayerHealth target, NetworkObject other)
    {
        if (!SA || _hp == null || _hp.IsDead) return;
        for (int si = 0; si < SlotCount; si++)
        {
            var s = _slots[si];
            if (s.Traits == null) continue;
            for (int ti = 0; ti < s.Traits.Length; ti++)
            {
                var t = s.Traits[ti];
                if (t != null && t.Trigger == trigger) TryFire(si, ti, t, s.States[ti], target, other);
            }
        }
    }

    void TryFire(int si, int ti, ItemTrait t, State st, PlayerHealth target, NetworkObject other)
    {
        float now = Now;
        if (now < st.ReadyAt || !WeaponOk(t)) return;
        if (t.Chance < 0.999f && Random.value > t.Chance) return;
        st.ReadyAt = now + t.Cooldown;
        Apply(si, ti, t, st, target, other);
    }

    bool WeaponOk(ItemTrait t)
    {
        if (t.RequireWeapon == WeaponRequirement.None) return true;
        var w = _equip != null ? _equip.GetWeapon() : null;
        return w != null && (w.ToRequirementFlag() & t.RequireWeapon) != 0;
    }

    void Apply(int si, int ti, ItemTrait t, State st, PlayerHealth target, NetworkObject other)
    {
        var me = _no.InputAuthority;
        float maxHp = _stats != null ? _stats.Get(StatType.MaxHP) : 100f;

        // 맞았을 때 — 대상은 때린 사람
        if (target == null && other != null) other.TryGetComponent(out target);

        switch (t.Effect)
        {
            case TraitEffect.StatBuff:
                if (t.IsWhile) break;       // 조건 버프는 SetWhile이 담당
                {
                    int max = Mathf.Clamp(t.MaxStacks, 1, MaxStacks);
                    if (st.Stacks < max)
                    {
                        _stats?.AddModifier(new StatModifier(t.Stat, t.Mod, t.Value, Src(si, ti, st.Stacks)));
                        st.Stacks++;
                    }
                    st.StackExpire = t.Duration > 0f ? Now + t.Duration : 0f;     // 쌓일 때마다 시간 갱신
                }
                break;

            case TraitEffect.CooldownPercent:
            case TraitEffect.CooldownSeconds:
                if (_ab == null) break;
                for (int i = 0; i < SkillSlotExt.Count; i++)
                {
                    if (!SlotMatch(t.Slots, (SkillSlot)i) || _ab.IsCooldownHeld(i)) continue;
                    float remain = _ab.GetCooldownRemain(i);
                    if (remain <= 0f) continue;
                    _ab.ShiftCooldown(i, t.Effect == TraitEffect.CooldownPercent ? remain * t.Value * 0.01f : t.Value);
                }
                break;

            case TraitEffect.TempShield:
                _hp.AddTempShield(Mathf.RoundToInt(t.Value), t.Duration, Src(si, ti, 0));
                break;

            case TraitEffect.TempShieldMaxHp:
                _hp.AddTempShield(Mathf.RoundToInt(maxHp * t.Value * 0.01f), t.Duration, Src(si, ti, 0));
                break;

            case TraitEffect.RefillShield: _hp.RefillRegenShield(); break;
            case TraitEffect.Heal: _hp.RPC_Heal(Mathf.RoundToInt(t.Value)); break;
            case TraitEffect.HealMaxHp: _hp.RPC_Heal(Mathf.RoundToInt(maxHp * t.Value * 0.01f)); break;
            case TraitEffect.GainSoul: _hp.RPC_GainSoul(Mathf.RoundToInt(t.Value)); break;
            case TraitEffect.GainStones: _ench?.RPC_AddStones(Mathf.RoundToInt(t.Value)); break;

            case TraitEffect.StatusToTarget:
                if (target != null && !target.IsDead && !TeamUtil.IsFriendly(me, target) && target.TryGetComponent<PlayerStatus>(out var ts))
                    ts.Apply(t.Status, t.Value, t.Duration, me);
                break;

            case TraitEffect.StatusToSelf:
                _status?.Apply(t.Status, t.Value, t.Duration, me);
                break;

            case TraitEffect.BonusDamage:
                if (target != null && !target.IsDead && !TeamUtil.IsFriendly(me, target) && _stats != null)
                {
                    var info = DamageInfo.FromPlayerHit(Mathf.Max(1, Mathf.RoundToInt(_stats.Get(StatType.AttackPower) * t.Value * 0.01f)), me, transform.position);
                    info.IgnoreDefense = true;
                    info.IsDot = true;          // 맞은 쪽 '피해를 받으면' 특성이 다시 안 터지게
                    target.RPC_TakeDamage(info);
                }
                break;

            case TraitEffect.RefundAmmo:
                _equip?.AddAmmo(Mathf.RoundToInt(t.Value));
                break;
        }
    }

    static bool SlotMatch(TraitSlots m, SkillSlot s) => m switch
    {
        TraitSlots.All => true,
        TraitSlots.Ultimate => s == SkillSlot.Ultimate,
        TraitSlots.Random => s.IsRandom(),
        TraitSlots.SkillsAndUlt => !s.IsRandom(),
        _ => s != SkillSlot.Ultimate && !s.IsRandom(),
    };
}
