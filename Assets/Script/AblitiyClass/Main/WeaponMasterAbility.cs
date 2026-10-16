using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무구의 달인
///   패시브 0  무구 저장     — 무기 칸이 늘어남 (최대 8)
///   패시브 1  무구의 달인   — 들고 있지 않은 무기가 머리 위를 돎 (총기 제외). 전투 중이면 무기 공속에 맞춰
///                            마지막으로 때린 적에게 자동 공격 — 근접은 날아갔다 돌아오고, 활·지팡이는 최대 충전으로 발사.
///                            이 공격의 특수 인챈트는 확률 절반, 수치·지속 절반
///   패시브 2  수집가        — 인챈트 스톤 획득 +50%
///   스킬 0    명령          — 떠다니는 무기들이 보는 방향으로 일제히 돌진 (지나가는 적 모두 피해)
///   스킬 1    가속          — N초간 자동 공격 간격 N% 감소, 명령 속도 +50%
///   스킬 2    무희          — N초간 보는 곳에 떠다니는 무기들을 난사. 원거리는 투사체, 근접은 복제체를 던짐.
///                            이때 특수 인챈트 감소가 33%로 줄어듦
/// 수치는 레벨별(LevelValue). 설명 {x:orbit} 떠다니는 무기 수 · {x:slots} 무기 칸
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Weapon Master")]
public class WeaponMasterAbility : AbilityBehaviour
{
    [Header("항목 번호 (Passives / Skills 배열)")]
    public int StoragePassive = 0;
    public int MasterPassive = 1;
    public int CollectorPassive = 2;
    public int CommandSkill = 0;
    public int HasteSkill = 1;
    public int DancerSkill = 2;

    [Header("무구 저장 · 수집가")]
    [ForPassive(nameof(StoragePassive), "무기 칸 수 (최대 7)")] public LevelValue SlotCount = new(5f, 6f, 7f);
    [ForPassive(nameof(CollectorPassive), "스톤 획득 배율 (1.5 = +50%)")] public LevelValue StoneGain = 1.5f;

    [Header("무구의 달인 — 자동 공격")]
    [ForPassive(nameof(MasterPassive), "기본 공격 간격 (초)")] public LevelValue OrbitInterval = 2.2f;
    [ForPassive(nameof(MasterPassive), "피해 계수 (공격력 %)")] public LevelValue OrbitCoef = 35f;
    [ForPassive(nameof(MasterPassive), "무기 공격력 반영 (%)")] public LevelValue WeaponAtkPct = 100f;
    [ForPassive(nameof(MasterPassive), "자동 공격 사거리 (m)")] public LevelValue OrbitRange = 18f;
    [Tooltip("자동 공격·명령 특수 인챈트 — 확률 배율")]
    public float EnchantChanceMul = 0.5f;
    [Tooltip("자동 공격·명령 특수 인챈트 — 수치·지속 배율")]
    public float EnchantPowerMul = 0.5f;
    [Tooltip("근접 무기 비행 속도 (m/초)")]
    public float FlySpeed = 28f;
    [Tooltip("투사체가 부딪히는 레이어 — PlayerAttack의 Projectile Mask와 같게")]
    public LayerMask ProjectileMask = ~0;
    [Tooltip("벽 레이어 (명령·무희가 멈추는 곳)")]
    public LayerMask WallMask = 1;

    [Header("명령")]
    [ForSkill(nameof(CommandSkill), "피해 계수 (공격력 %)")] public LevelValue CommandCoef = 60f;
    [ForSkill(nameof(CommandSkill), "거리 (m)")] public LevelValue CommandRange = 16f;
    [ForSkill(nameof(CommandSkill), "폭 (m)")] public LevelValue CommandWidth = 1.4f;
    [ForSkill(nameof(CommandSkill), "속도 (m/초)")] public LevelValue CommandSpeed = 30f;

    [Header("가속")]
    [ForSkill(nameof(HasteSkill), "지속 (초)")] public LevelValue HasteDuration = 5f;
    [ForSkill(nameof(HasteSkill), "자동 공격 간격 감소 (%)")] public LevelValue HasteIntervalPct = 40f;
    [ForSkill(nameof(HasteSkill), "명령 속도 증가 (%)")] public LevelValue HasteCommandPct = 50f;

    [Header("무희")]
    [ForSkill(nameof(DancerSkill), "지속 (초)")] public LevelValue DancerDuration = 3f;
    [ForSkill(nameof(DancerSkill), "무기당 발사 간격 (초)")] public LevelValue DancerInterval = 0.35f;
    [ForSkill(nameof(DancerSkill), "피해 계수 (공격력 %)")] public LevelValue DancerCoef = 30f;
    [ForSkill(nameof(DancerSkill), "조준 최대 거리 (m)")] public LevelValue DancerRange = 30f;
    [Tooltip("무희 특수 인챈트 — 확률·수치 배율 (감소 33% → 0.67)")]
    public float DancerEnchantMul = 0.67f;
    [Tooltip("근접 복제체 속도 (m/초)")]
    public float CloneSpeed = 35f;

    [Header("연출")]
    public GameObject OrbitHitFx;

    // 이벤트 키 — 슬롯 번호를 더해서 보냄
    const int EvFly = 100, EvShoot = 200, EvCommand = 300, EvClone = 400, EvHit = 1;
    const int Slots = PlayerEquipment.WeaponSlots;

    private PlayerEquipment _equip;
    private PlayerEnchant _enchant;
    private PlayerHealth _health;
    private PlayerAttack _attack;
    private WeaponOrbit _orbit;
    private CharacterController _cc;

    private PlayerHealth _lastTarget;
    private readonly float[] _timers = new float[Slots];
    private readonly List<int> _orbiting = new();

    private float _hasteEnd;
    private float _dancerEnd;
    private Vector3 _dancerPoint;
    private readonly float[] _dancerTimers = new float[Slots];

    struct PendingHit { public float At; public PlayerHealth Target; public int Slot; public float Coef; public float ChanceMul, PowerMul; }
    private readonly List<PendingHit> _pending = new();

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[32];

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    bool Hasted => _hasteEnd > Now;
    bool Dancing => _dancerEnd > Now;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _equip = Ctx.GetComponent<PlayerEquipment>();
        _enchant = Ctx.GetComponent<PlayerEnchant>();
        _health = Ctx.GetComponent<PlayerHealth>();
        _attack = Ctx.GetComponent<PlayerAttack>();
        _cc = Ctx.GetComponent<CharacterController>();

        // 떠다니는 무기 — 모든 클라이언트
        var go = new GameObject("WeaponOrbit");
        go.transform.SetParent(Ctx.transform, false);
        _orbit = go.AddComponent<WeaponOrbit>();
        _orbit.Equip = _equip;
        _orbit.Health = _health;

        if (!Ctx.HasStateAuthority) return;

        _equip?.SetSlotCount(Mathf.RoundToInt(Lv(SlotCount)));
        if (_enchant != null) _enchant.StoneGainMul = Lv(StoneGain);
        if (_attack != null) _attack.WeaponHit += OnWeaponHit;
    }

    public override void OnUnequip()
    {
        if (_orbit != null) Destroy(_orbit.gameObject);
        if (!Ctx.HasStateAuthority) return;

        // 칸 수는 줄이지 않음 — 사망 때마다 재빌드되므로 줄이면 무기가 바닥에 떨어짐 (라운드 리셋이 3칸으로 돌림)
        if (_enchant != null) _enchant.StoneGainMul = 1f;
        if (_attack != null) _attack.WeaponHit -= OnWeaponHit;
        ReleaseIfEnded(true);
    }

    public override void Tick(float dt)
    {
        // 레벨업으로 칸 수가 바뀌면 반영
        int want = Mathf.RoundToInt(Lv(SlotCount));
        if (_equip != null && want > _equip.UsableSlots) _equip.SetSlotCount(want);
        if (_enchant != null) _enchant.StoneGainMul = Lv(StoneGain);

        bool dead = _health != null && _health.IsDead;
        if (dead) { _pending.Clear(); _dancerEnd = 0f; }

        TickPending();
        ReleaseIfEnded(false);
        if (dead || _orbit == null) return;

        _orbit.OrbitingSlots(_orbiting);

        if (Dancing) { TickDancer(dt); return; }
        TickAuto(dt);
    }

    void OnWeaponHit(PlayerHealth target, float outgoing, bool enchants)
    {
        if (target != null && !target.IsDead) _lastTarget = target;
    }

    // ══════════════════ 자동 공격 ══════════════════

    void TickAuto(float dt)
    {
        bool fight = _health != null && _health.InCombat
                     && _lastTarget != null && !_lastTarget.IsDead
                     && (_lastTarget.transform.position - Ctx.transform.position).sqrMagnitude < Sq(Lv(OrbitRange));

        foreach (int slot in _orbiting)
        {
            if (_timers[slot] > 0f) _timers[slot] -= dt;
            if (!fight || _timers[slot] > 0f || _orbit.IsFlying(slot)) continue;

            var w = WeaponAt(slot);
            if (w == null) continue;

            _timers[slot] = Interval(w);
            AttackWith(slot, w, _lastTarget, Lv(OrbitCoef), EnchantChanceMul, EnchantPowerMul);
        }
    }

    float Interval(EquipmentData w)
    {
        // 무기 자체의 빠르기 — 근접은 1타 모션 길이, 활·지팡이는 최대 충전 시간 기준
        float k;
        if (w.Class == WeaponClass.Melee)
        {
            var st = w.GetStep(0);
            k = st != null ? st.Duration / 0.6f : 1f;
        }
        else k = Mathf.Max(0.3f, w.MaxDrawTime) / 1.2f;

        float atkSpd = Mathf.Max(0.1f, Ctx.Stats.Get(StatType.AttackSpeed));
        float haste = Hasted ? 1f - Mathf.Clamp01(Lv(HasteIntervalPct) * 0.01f) : 1f;
        return Lv(OrbitInterval) * Mathf.Clamp(k, 0.5f, 2.5f) / atkSpd * haste;
    }

    void AttackWith(int slot, EquipmentData w, PlayerHealth target, float coefPct, float chanceMul, float powerMul)
    {
        Vector3 from = _orbit.SlotPosition(slot);
        Vector3 aim = target.transform.position + Vector3.up * 1.1f;

        if (w.Class == WeaponClass.Melee)
        {
            float travel = Vector3.Distance(from, aim) / FlySpeed;
            _pending.Add(new PendingHit { At = Now + travel, Target = target, Slot = slot, Coef = coefPct, ChanceMul = chanceMul, PowerMul = powerMul });
            Broadcast(EvFly + slot, aim);
        }
        else
        {
            ShootFrom(slot, w, from, aim, true, coefPct, chanceMul, powerMul);
            Broadcast(EvShoot + slot, aim);
        }
    }

    // ══════════════════ 피해 ══════════════════

    EquipmentData WeaponAt(int slot)
    {
        int id = _equip != null ? _equip.GetWeaponIdAt(slot) : 0;
        return id != 0 && _equip.Database != null ? _equip.Database.Get(id) : null;
    }

    static float WeaponAtk(EquipmentData w)
    {
        float a = 0f;
        if (w?.BaseStats != null)
            foreach (var s in w.BaseStats)
                if (s.Stat == StatType.AttackPower && s.Type == ModType.Flat) a += s.Value;
        return a;
    }

    /// <summary>떠다니는 무기 한 번 — 공격력 × 계수 + 그 무기 공격력 × 반영%. 특수 인챈트는 배율 적용</summary>
    void HitWith(int slot, PlayerHealth target, float coefPct, float chanceMul, float powerMul, float extraMul = 1f)
    {
        if (target == null || target.IsDead) return;

        var w = WeaponAt(slot);
        var stats = Ctx.Stats;
        float baseDmg = (stats.Get(StatType.AttackPower) * coefPct * 0.01f + WeaponAtk(w) * Lv(WeaponAtkPct) * 0.01f) * extraMul;
        var src = w != null && w.Class == WeaponClass.Ranged ? DamageSource.RangedWeapon : DamageSource.MeleeWeapon;
        float dmg = DamageCalc.CalcOutgoing(stats, baseDmg, src, true, out bool crit);

        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), Ctx.Object.InputAuthority, _orbit != null ? _orbit.SlotPosition(slot) : Ctx.transform.position);
        info.IsCrit = crit;
        info.IsMelee = src == DamageSource.MeleeWeapon;
        _enchant?.ModifySlotHit(slot, ref info, chanceMul);
        target.RPC_TakeDamage(info);
        _enchant?.OnSlotHit(slot, target, chanceMul, powerMul);

        Broadcast(EvHit, target.transform.position + Vector3.up * 1.1f);
    }

    void TickPending()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (Now < p.At) continue;
            _pending.RemoveAt(i);
            HitWith(p.Slot, p.Target, p.Coef, p.ChanceMul, p.PowerMul);
        }
    }

    /// <summary>활·지팡이 — 최대 충전 투사체. 판정은 SA(authoritative)만</summary>
    void ShootFrom(int slot, EquipmentData w, Vector3 from, Vector3 aim, bool authoritative,
                   float coefPct = 0f, float chanceMul = 0f, float powerMul = 0f)
    {
        if (w == null || w.ProjectilePrefab == null) return;

        Vector3 dir = (aim - from).normalized;
        var go = Instantiate(w.ProjectilePrefab, from, Quaternion.LookRotation(dir));
        var proj = go.GetComponent<Projectile>();
        if (proj == null) { Destroy(go, 3f); return; }

        float drawMul = w.GetDrawDamageMul(1f);
        proj.Launch(from, dir, w.GetDrawSpeed(1f), w.ProjectileGravity, w.ProjectileLifetime,
                    true, ProjectileMask, w.PierceCount, w.PierceDamageFalloff, _cc,
                    authoritative
                        ? (col, point, mul) =>
                        {
                            if (col == null || col.transform.root == Ctx.transform.root) return;
                            var h = col.GetComponentInParent<PlayerHealth>();
                            if (h != null) HitWith(slot, h, coefPct, chanceMul, powerMul, drawMul * mul);
                        }
        : null);
    }

    // ══════════════════ 스킬 ══════════════════

    int IndexOf(AbilityData.SkillDef s) => System.Array.IndexOf(Data.Skills, s);

    public override bool CanUseSkill(AbilityData.SkillDef skill)
    {
        int i = IndexOf(skill);
        if (i == CommandSkill || i == DancerSkill)
            return _orbit != null && _orbit.OrbitingSlots(_orbiting).Count > 0;    // 떠다니는 무기가 있어야
        return true;
    }

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        int i = IndexOf(skill);
        if (i == CommandSkill) { Command(); return true; }
        if (i == HasteSkill) { _hasteEnd = Now + Lv(HasteDuration); return true; }
        if (i == DancerSkill) { BeginDancer(); return true; }
        return false;
    }

    Vector3 LookPoint(float range)
    {
        var cam = Ctx.CameraRootTransform;
        if (cam == null) return Ctx.transform.position + Ctx.transform.forward * range;
        return Physics.Raycast(cam.position, cam.forward, out var hit, range, WallMask | Ctx.HitMask, QueryTriggerInteraction.Ignore)
               && hit.collider.transform.root != Ctx.transform.root
            ? hit.point
            : cam.position + cam.forward * range;
    }

    // ── 명령 ──

    void Command()
    {
        var cam = Ctx.CameraRootTransform;
        Vector3 origin = Ctx.transform.position + Vector3.up * 1.1f;
        Vector3 dir = cam != null ? cam.forward : Ctx.transform.forward;
        float range = Lv(CommandRange);

        if (Physics.Raycast(origin, dir, out var wall, range, WallMask, QueryTriggerInteraction.Ignore))
            range = wall.distance;
        Vector3 end = origin + dir * range;

        float speed = Lv(CommandSpeed) * (Hasted ? 1f + Lv(HasteCommandPct) * 0.01f : 1f);
        float width = Lv(CommandWidth);

        // 길 위의 적 — 무기마다 한 번씩, 도착하는 시점에
        var targets = new List<(PlayerHealth h, float along)>();
        int n = Physics.OverlapCapsuleNonAlloc(origin, end, width, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        var seen = new HashSet<PlayerHealth>();
        for (int k = 0; k < n; k++)
        {
            if (_hits[k].transform.root == Ctx.transform.root) continue;
            var h = _hits[k].GetComponentInParent<PlayerHealth>();
            if (h == null || h.IsDead || !seen.Add(h)) continue;
            targets.Add((h, Vector3.Dot(h.transform.position - origin, dir)));
        }

        foreach (int slot in _orbiting)
        {
            Vector3 from = _orbit.SlotPosition(slot);
            foreach (var (h, along) in targets)
                _pending.Add(new PendingHit
                {
                    At = Now + (Vector3.Distance(from, origin) + Mathf.Max(0f, along)) / speed,
                    Target = h,
                    Slot = slot,
                    Coef = Lv(CommandCoef),
                    ChanceMul = EnchantChanceMul,
                    PowerMul = EnchantPowerMul,
                });

            Broadcast(EvCommand + slot, end);
            _timers[slot] = Mathf.Max(_timers[slot], range / speed * 2f);     // 돌아올 때까지 자동 공격 쉼
        }
    }

    // ── 무희 ──

    void BeginDancer()
    {
        _dancerEnd = Now + Lv(DancerDuration);
        _dancerPoint = LookPoint(Lv(DancerRange));

        // 무기마다 살짝 엇갈려 시작
        for (int k = 0; k < _orbiting.Count; k++)
            _dancerTimers[_orbiting[k]] = k * Lv(DancerInterval) / Mathf.Max(1, _orbiting.Count);
    }

    void TickDancer(float dt)
    {
        float mul = DancerEnchantMul;
        foreach (int slot in _orbiting)
        {
            _dancerTimers[slot] -= dt;
            if (_dancerTimers[slot] > 0f) continue;
            _dancerTimers[slot] += Lv(DancerInterval);

            var w = WeaponAt(slot);
            if (w == null) continue;

            Vector3 from = _orbit.SlotPosition(slot);
            Vector3 aim = _dancerPoint + Random.insideUnitSphere * 0.6f;

            if (w.Class == WeaponClass.Melee)
                ThrowClone(slot, w, from, aim, true, mul);
            else
                ShootFrom(slot, w, from, aim, true, Lv(DancerCoef), mul, mul);

            Broadcast((w.Class == WeaponClass.Melee ? EvClone : EvShoot) + slot, aim);
        }
    }

    void ThrowClone(int slot, EquipmentData w, Vector3 from, Vector3 aim, bool authoritative, float mul)
    {
        if (w == null) return;
        Vector3 dir = aim - from;
        float dist = dir.magnitude + 4f;
        ThrownWeapon.Launch(w.ModelPrefab, from, dir, CloneSpeed, dist, 0.35f, Ctx.HitMask, WallMask,
                            Ctx.transform.root, w.ScaleOverride * 0.9f,
                            authoritative ? (h, p) => HitWith(slot, h, Lv(DancerCoef), mul, mul) : null);
    }

    void ReleaseIfEnded(bool force)
    {
        if (_hasteEnd > 0f && (force || Now >= _hasteEnd)) { _hasteEnd = 0f; ReleaseCooldown(HasteSkill); }
        if (_dancerEnd > 0f && (force || Now >= _dancerEnd)) { _dancerEnd = 0f; ReleaseCooldown(DancerSkill); }
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        if (key == EvHit) { Ctx.PlayEffectLocal(OrbitHitFx, pos); return; }
        if (_orbit == null) return;

        int kind = key / 100 * 100;
        int slot = key - kind;
        if (slot < 0 || slot >= Slots) return;

        switch (kind)
        {
            case EvFly: _orbit.Fly(slot, pos, FlySpeed, true); break;
            case EvCommand:
                float speed = Lv(CommandSpeed) * (Hasted ? 1f + Lv(HasteCommandPct) * 0.01f : 1f);
                _orbit.Fly(slot, pos, speed, true);
                break;
            case EvShoot:
                if (!Ctx.HasStateAuthority) ShootFrom(slot, WeaponAt(slot), _orbit.SlotPosition(slot), pos, false);
                break;
            case EvClone:
                if (!Ctx.HasStateAuthority) ThrowClone(slot, WeaponAt(slot), _orbit.SlotPosition(slot), pos, false, 0f);
                break;
        }
    }

    // ══════════════════ 설명 ══════════════════

    public override string DescToken(string key, int rank)
    {
        switch (key)
        {
            case "orbit":
                return _orbit != null ? $"떠다니는 무기 {_orbit.OrbitingSlots(_orbiting).Count}개" : null;
            case "slots":
                return _equip != null ? $"무기 칸 {_equip.UsableSlots}" : null;
        }
        return null;
    }

    static float Sq(float v) => v * v;

    // ══════════════════ 치트 (/job) ══════════════════

    public override string DevCommand(string[] a)
    {
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "help";
        switch (sub)
        {
            case "help":
                return "무구의 달인: /job slots [n] (무기 칸 수, 1~7) · fill (빈 칸을 무작위 무기로)";
            case "slots":
            case "칸":
                {
                    if (_equip == null) return "PlayerEquipment 없음";
                    int n = a.Length > 1 && int.TryParse(a[1], out int v) ? v : PlayerEquipment.WeaponSlots;
                    _equip.SetSlotCount(Mathf.Clamp(n, 1, PlayerEquipment.WeaponSlots));
                    return $"무기 칸 {_equip.UsableSlots}";
                }
            case "fill":
            case "채우기":
                {
                    if (_equip == null || _equip.Database == null || _equip.Database.All == null) return "장비 데이터 없음";
                    var pool = new List<EquipmentData>();
                    foreach (var w in _equip.Database.All)
                        if (w != null && w.Slot == EquipSlot.Weapon && !w.IsUnarmed) pool.Add(w);
                    if (pool.Count == 0) return "무기가 없음";
                    int filled = 0;
                    while (_equip.HasEmptyWeaponSlot() && filled < PlayerEquipment.WeaponSlots)
                    {
                        if (!_equip.PickUpWeapon(pool[Random.Range(0, pool.Count)].Id)) break;
                        filled++;
                    }
                    return $"무기 {filled}개 채움";
                }
        }
        return null;
    }

    public override IEnumerable<(string value, string label)> DevComplete(string[] a, int index)
    {
        if (index == 0) return new[] { ("slots", "무기 칸 수"), ("fill", "빈 칸 무작위 무기로") };
        if (index == 1 && a.Length > 0 && a[0] == "slots") return new[] { ("5", ""), ("6", ""), ("7", "") };
        return null;
    }
}