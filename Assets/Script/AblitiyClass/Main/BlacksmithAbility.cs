using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 대장장이
///   패시브    대장장이        — 인챈트 상한 해제 (기존)
///   스킬 0    파쇄            — 전방 망치: 피해 + 0.25초 기절 + 5초간 방어력 감소
///   스킬 1    용광로          — 주위 용암 광역. 내 체력이 낮을수록 강함 (체력 20%에서 최대 ×3). 30% 확률로 화상
///   궁극기    불의 후예       — 20초: 나를 근접으로 때린 적에게 화상, 아이템 효과 +25%,
///                               체력이 15%씩 깎일 때마다 용광로 쿨 초기화, 인챈트 스톤 소모 절반
/// 수치는 레벨별(LevelValue) — 능력 데이터의 각 스킬 탭에서도 같이 편집. 피해 계수는 공격력 대비 %
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Blacksmith")]
public class BlacksmithAbility : AbilityBehaviour
{
    [Header("스킬 번호 (Skills 배열)")]
    public int SmashSkill = 0;
    public int FurnaceSkill = 1;
    public int UltSkill = 2;

    [Header("파쇄")]
    [ForSkill(nameof(SmashSkill), "피해 계수 (공격력 %)")] public LevelValue SmashCoef = 150f;
    [ForSkill(nameof(SmashSkill), "고정 추가 피해")] public LevelValue SmashFlat = 0f;
    [ForSkill(nameof(SmashSkill), "사거리 (m)")] public LevelValue SmashRange = 3f;
    [ForSkill(nameof(SmashSkill), "각도 (°)")] public LevelValue SmashAngle = 100f;
    [ForSkill(nameof(SmashSkill), "최대 대상")] public LevelValue SmashMaxTargets = 4f;
    [ForSkill(nameof(SmashSkill), "기절 (초)")] public LevelValue SmashStun = 0.25f;
    [ForSkill(nameof(SmashSkill), "방어력 감소 (%)")] public LevelValue ArmorBreakPct = 30f;
    [ForSkill(nameof(SmashSkill), "방어력 감소 지속 (초)")] public LevelValue ArmorBreakTime = 5f;
    public float SmashHeight = 1.6f;
    public GameObject SmashFx;

    [Header("용광로")]
    [ForSkill(nameof(FurnaceSkill), "피해 계수 (공격력 %)")] public LevelValue FurnaceCoef = 120f;
    [ForSkill(nameof(FurnaceSkill), "고정 추가 피해")] public LevelValue FurnaceFlat = 0f;
    [ForSkill(nameof(FurnaceSkill), "반경 (m)")] public LevelValue FurnaceRadius = 5f;
    [ForSkill(nameof(FurnaceSkill), "최대 배율 (3 = 300%)")] public LevelValue FurnaceMaxMul = 3f;
    [ForSkill(nameof(FurnaceSkill), "화상 확률 (0.3 = 30%)")] public LevelValue BurnChance = 0.3f;
    [ForSkill(nameof(FurnaceSkill), "화상 (%/초)")] public LevelValue BurnPct = 5f;
    [ForSkill(nameof(FurnaceSkill), "화상 지속 (초)")] public LevelValue BurnTime = 3f;
    [Tooltip("이 체력 비율 이하에서 최대 배율")]
    [Range(0.01f, 1f)] public float FullPowerHp = 0.2f;
    public GameObject FurnaceFx;

    [Header("궁극기 — 불의 후예")]
    [ForSkill(nameof(UltSkill), "지속 (초)")] public LevelValue UltDuration = 20f;
    [ForSkill(nameof(UltSkill), "반격 화상 (%/초)")] public LevelValue ReflectBurnPct = 2f;
    [ForSkill(nameof(UltSkill), "반격 화상 지속 (초)")] public LevelValue ReflectBurnTime = 10f;
    [ForSkill(nameof(UltSkill), "아이템 효과 배율 (1.25)")] public LevelValue ItemBoost = 1.25f;
    [ForSkill(nameof(UltSkill), "스톤 소모 배율 (0.5)")] public LevelValue StoneCostMul = 0.5f;
    [Tooltip("체력이 이만큼 깎일 때마다 용광로 쿨 초기화 (0.15 = 85%, 70%, 55% …)")]
    [Range(0.05f, 0.5f)] public float HpStep = 0.15f;
    [Tooltip("궁극기 동안 붙는 불꽃 (변화). 모두에게 보임")]
    public AlterData UltAlter;

    const int EvSmash = 0, EvFurnace = 1;

    private PlayerHealth _health;
    private PlayerEnchant _enchant;
    private PlayerAlter _alter;

    private float _ultTimer;
    private float _nextThreshold;

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[32];
    [System.NonSerialized] private readonly HashSet<PlayerHealth> _done = new();

    public bool InUlt => _ultTimer > 0f;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _health = Ctx.GetComponent<PlayerHealth>();
        _enchant = Ctx.GetComponent<PlayerEnchant>();
        _alter = Ctx.GetComponent<PlayerAlter>();

        _enchant?.SetUnlimitedStage(true);
        if (_health != null) _health.Damaged += OnDamaged;
        _ultTimer = 0f;
    }

    public override void OnUnequip()
    {
        _enchant?.SetUnlimitedStage(false);
        if (_health != null) _health.Damaged -= OnDamaged;
        EndUlt();
    }

    public override void Tick(float dt)
    {
        if (_ultTimer <= 0f) return;

        _ultTimer -= dt;
        if (_ultTimer <= 0f || (_health != null && _health.IsDead))
        {
            EndUlt();
            return;
        }

        // 체력이 단계(85% → 70% → 55% …)를 지날 때마다 용광로 쿨 초기화
        float ratio = HpRatio();
        while (_nextThreshold > 0f && ratio <= _nextThreshold + 0.0001f)
        {
            ResetFurnaceCooldown();
            _nextThreshold -= HpStep;
        }
    }

    float HpRatio()
    {
        if (_health == null || Ctx.Stats == null) return 1f;
        float max = Mathf.Max(1f, Ctx.Stats.Get(StatType.MaxHP));
        return Mathf.Clamp01(_health.HP / max);
    }

    // ══════════════════ 스킬 ══════════════════

    // 스킬 모션의 효과 타이밍(데이터의 '효과 타이밍')에 발동
    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        int i = System.Array.IndexOf(Data.Skills, skill);

        if (i == SmashSkill) { DoSmash(); return true; }
        if (i == FurnaceSkill) { DoFurnace(); return true; }
        if (i == UltSkill) { BeginUlt(); return true; }
        return false;
    }

    void DoSmash()
    {
        float coef = Lv(SmashCoef) * 0.01f;
        float flatDmg = Lv(SmashFlat);
        float range = Lv(SmashRange);
        float angle = Lv(SmashAngle);
        int max = Mathf.Max(1, Mathf.RoundToInt(Lv(SmashMaxTargets)));
        var self = Ctx.Object.InputAuthority;

        Vector3 o = Ctx.transform.position;
        Vector3 fwd = Forward();

        int hits = 0;
        foreach (var h in FindTargets(o, range))
        {
            Vector3 to = h.transform.position - o;
            if (Mathf.Abs(to.y) > SmashHeight) continue;

            Vector3 flat = new(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.01f && Vector3.Angle(fwd, flat) > angle * 0.5f) continue;

            Hit(h, coef, flatDmg);

            if (h.TryGetComponent<PlayerStatus>(out var st))
            {
                st.Apply(StatusType.Stun, 1f, Lv(SmashStun), self);
                st.Apply(StatusType.ArmorBreak, Lv(ArmorBreakPct), Lv(ArmorBreakTime), self);
            }

            if (++hits >= max) break;
        }

        Broadcast(EvSmash, o + fwd * (range * 0.6f));
    }

    void DoFurnace()
    {
        // 체력 100% → ×1, FullPowerHp 이하 → ×FurnaceMaxMul
        float t = Mathf.InverseLerp(1f, FullPowerHp, HpRatio());
        float mul = Mathf.Lerp(1f, Lv(FurnaceMaxMul), t);
        float coef = Lv(FurnaceCoef) * 0.01f * mul;
        float flatDmg = Lv(FurnaceFlat) * mul;
        var self = Ctx.Object.InputAuthority;

        Vector3 o = Ctx.transform.position;
        foreach (var h in FindTargets(o, Lv(FurnaceRadius)))
        {
            Hit(h, coef, flatDmg);

            if (Random.value < Lv(BurnChance) && h.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.Burn, Lv(BurnPct), Lv(BurnTime), self);
        }

        Broadcast(EvFurnace, o);
    }

    void BeginUlt()
    {
        EndUlt();
        _ultTimer = Lv(UltDuration);

        // 지금 체력 바로 아래 단계부터 (예: 체력 90%면 85%가 첫 단계)
        float ratio = HpRatio();
        int k = Mathf.FloorToInt((1f - ratio) / HpStep + 0.0001f) + 1;
        _nextThreshold = 1f - k * HpStep;

        if (Ctx.Stats != null) Ctx.Stats.ItemEffectMul = Lv(ItemBoost);
        if (_enchant != null) _enchant.StoneCostMul = Lv(StoneCostMul);
        _alter?.Add(UltAlter);
    }

    void EndUlt()
    {
        bool was = _ultTimer > 0f;
        _ultTimer = 0f;

        if (Ctx.Stats != null) Ctx.Stats.ItemEffectMul = 1f;
        if (_enchant != null) _enchant.StoneCostMul = 1f;
        if (was) _alter?.Remove(UltAlter);
        if (was) ReleaseCooldown(UltSkill);       // '효과 끝나고 쿨타임'이면 지금부터 쿨
    }

    void ResetFurnaceCooldown()
    {
        if (Data.Skills == null || FurnaceSkill < 0 || FurnaceSkill >= Data.Skills.Length) return;
        Ctx.ResetCooldown((int)Data.Skills[FurnaceSkill].Slot);
    }

    // ══════════════════ 반격 화상 ══════════════════

    /// <summary>내가 피해를 받을 때 (SA) — 궁극기 중 근접 공격자에게 화상</summary>
    void OnDamaged(DamageInfo info, int amount)
    {
        if (!InUlt || !info.IsMelee) return;
        if (info.Attacker == Ctx.Object.InputAuthority || info.Attacker == Fusion.PlayerRef.None) return;

        var obj = Ctx.Runner.GetPlayerObject(info.Attacker);
        if (obj != null && obj.TryGetComponent<PlayerStatus>(out var st))
            st.Apply(StatusType.Burn, Lv(ReflectBurnPct), Lv(ReflectBurnTime), Ctx.Object.InputAuthority);
    }

    // ══════════════════ 공통 ══════════════════

    Vector3 Forward()
    {
        var cam = Ctx.CameraRootTransform;
        Vector3 f = cam != null ? cam.forward : Ctx.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.001f ? f.normalized : Ctx.transform.forward;
    }

    List<PlayerHealth> FindTargets(Vector3 center, float radius)
    {
        var list = new List<PlayerHealth>();
        _done.Clear();

        int n = Physics.OverlapSphereNonAlloc(center, radius, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = _hits[i];
            if (c.transform.root == Ctx.transform.root) continue;
            var h = c.GetComponentInParent<PlayerHealth>();
            if (h == null || h.IsDead || !_done.Add(h)) continue;
            list.Add(h);
        }

        list.Sort((a, b) =>
            (a.transform.position - center).sqrMagnitude.CompareTo((b.transform.position - center).sqrMagnitude));
        return list;
    }

    /// <summary>피해 = 공격력 × 계수 + 고정 피해</summary>
    void Hit(PlayerHealth h, float coef, float flat = 0f)
    {
        var stats = Ctx.Stats;
        float baseDmg = stats.Get(StatType.AttackPower) * coef + flat;
        float dmg = DamageCalc.CalcOutgoing(stats, baseDmg, DamageSource.Skill, true, out bool crit);

        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), Ctx.Object.InputAuthority, Ctx.transform.position);
        info.IsCrit = crit;
        h.RPC_TakeDamage(info);
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key == EvSmash) Ctx.PlayEffectLocal(SmashFx, pos);
        else if (key == EvFurnace) Ctx.PlayEffectLocal(FurnaceFx, pos);
    }
}