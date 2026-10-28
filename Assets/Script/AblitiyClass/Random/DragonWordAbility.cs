using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 전설의 용언 — 스킬(F) '전설의 용언': 주문을 외움 → 작은 메시지 "A : (주문)" (주위 ChantRange m 안 사람만 보임).
///   ChantsNeeded번 외우면 '전설의 용'으로 변신 + 글로벌 이벤트:
///     EventTime초 안에 아무도 처치하지 못하면 그 플레이어의 즉시 승리.
///     처치하면 처치한 사람 소울 +KillBonus, 처치 관여 +AssistBonus.
///   전설의 용 (변신 — 깡스탯 · 모습 · 기본 공격(깨물기)은 AlterData, 쥔 무기 스탯 그대로).
///   동작 중(깨물기 · 이륙 · 활공 · 푸스로다 · 브레스)엔 다른 동작을 못 함:
///     스킬 1 활공 — 땅이면 이륙 동작(제자리, Hit Frame에 떠오름) → 바라보는 방향(위아래 포함)으로 계속 비행.
///                   다시 누르면 끝 → 그때부터 쿨. 활공 중엔 공격 · 다른 스킬 X. 부딪힌 적 피해 · 밀치기 · 짧은 기절
///     스킬 2 푸스로다 — roar(×1.25) Hit Frame에 앞으로 외침: 피해 · 기절 · 크게 밀치기, 날아가다 벽에 부딪히면 최대 체력 WallSlamPct%
///     궁극기 브레스 — roar(×0.5) 재생, BreathStartFrame ~ BreathEndFrame 동안 불 (못 움직임, 시점은 돎). 구간 내내 맞으면 BreathTotalDamage
///   다른 사람이 먼저 전설의 용이 되면 내 용언 횟수는 0으로 (작은 메시지).
/// 데이터: Skills 4개 — 0 용언(F) · 1 활공(Q, 처음엔 숨김 · 효과 끝나고 쿨타임 켜기) · 2 푸스로다(E, 처음엔 숨김) · 3 브레스(궁, 처음엔 숨김).
///   용 스킬 3개는 효과 타이밍 = 즉시 · 1인칭 모션 없어도 됨 (동작은 용 모델이 함).
///   랜덤 추첨: '동시에 가질 수 있는 수(MaxConcurrent)' 1 · 추첨 가중치(RollWeight) 아주 낮게
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Dragon Word")]
public class DragonWordAbility : TransformAbility
{
    [Header("스킬 번호 (Skills 배열)")]
    public int ChantSkill = 0;
    public int GlideSkill = 1;
    public int RoarSkill = 2;
    public int BreathSkill = 3;

    [Header("형태")]
    [Tooltip("전설의 용 — AlterData(변신 켜기 · 깡스탯 · 모델). 'Lock Skills'는 끄기 (스킬은 이 능력이 직접 바꿔 끼움)")]
    public TransformForm Dragon = new() { CanFly = false };

    [Header("용언")]
    public int ChantsNeeded = 30;
    [Tooltip("주문이 보이는 거리 (m)")]
    public float ChantRange = 15f;
    [Tooltip("외울 때마다 차례대로 나오는 주문")]
    [TextArea(1, 2)]
    public string[] ChantLines =
    {
        "들어라, 하늘이여…", "잠든 비늘이 깨어나니…", "불꽃의 이름을 부르노라…", "고대의 날개여…", "땅이 울고 바람이 떤다…",
    };

    [Header("글로벌 이벤트")]
    [Tooltip("이 시간(초) 안에 처치 못 하면 즉시 승리")]
    public float EventTime = 180f;
    public int KillBonus = 100;
    public int AssistBonus = 50;
    [Tooltip("처치 관여로 치는 시간 (초) — 이 시간 안에 피해를 준 사람")]
    public float AssistWindow = 10f;

    [Header("모델 동작 — 거신병처럼 AlterClipAnimator의 상태 이름")]
    [Tooltip("이륙 (takeoff) — 이 상태의 Hit Frame = 실제로 떠오르는 프레임. 클립 Root 위치는 Bake Into Pose 켜서 제자리로")]
    public string TakeOffAnim = "TakeOff";
    [Tooltip("활공 중 반복 (fly_loop)")]
    public string FlyAnim = "Fly";
    [Tooltip("푸스로다 (roar × 1.25) — 이 상태의 Hit Frame = 외치는 순간 (밀치기)")]
    public string RoarAnim = "Roar";
    [Tooltip("브레스 (roar × 0.5) — 불은 아래 Breath Start ~ End 프레임 동안")]
    public string BreathAnim = "Breath";

    [Header("활공 (스킬 1)")]
    [Tooltip("활공 속도 (m/초) — 바라보는 방향으로 (위아래 포함). 다시 누르면 끝 → 쿨 시작")]
    public float GlideSpeed = 16f;
    [Tooltip("땅에서 이륙할 때 처음 떠오르는 속도 (m/초) · 시간 (초)")]
    public float LiftSpeed = 9f, LiftTime = 0.6f;
    [Tooltip("이륙 동작이 없을 때 — 누르고 뜰 때까지 (초)")]
    public float TakeOffDelayFallback = 0.4f;
    [Tooltip("부딪힘 판정 반지름 (m)")]
    public float GlideHitRadius = 2.5f;
    [Tooltip("피해 — 공격력 %")]
    public float GlideDamageCoef = 80f;
    public float GlidePush = 12f, GlidePushUp = 5f;
    public float GlideStun = 0.8f;

    [Header("푸스로다 (스킬 2)")]
    public float RoarRange = 12f;
    public float RoarAngle = 70f;
    public float RoarHeight = 3f;
    public float RoarDamageCoef = 60f;
    public float RoarPush = 24f, RoarPushUp = 4f;
    public float RoarStun = 1.5f;
    [Tooltip("밀려나다 벽에 부딪히면 최대 체력의 이 %만큼 추가 피해")]
    public float WallSlamPct = 20f;
    [Tooltip("벽꿍을 보는 시간 (초) — 밀려나는 동안")]
    public float SlamWindow = 1.2f;
    public FeelDef RoarFeel = new() { ShakeRadius = 20f, ShakeAmount = 0.35f, ShakeSelf = 0.3f };

    [Header("브레스 (궁극기)")]
    [Tooltip("불을 뿜기 시작 · 끝 프레임 — roar 클립의 프레임 번호 그대로 (Animation 창 위쪽 숫자)")]
    public int BreathStartFrame = 20, BreathEndFrame = 80;
    [Tooltip("불 뿜는 구간 내내 맞으면 받는 피해 합 (고정 피해)")]
    public float BreathTotalDamage = 3000f;
    [Tooltip("피해 간격 (초)")]
    public float BreathTick = 0.2f;
    public float BreathRange = 14f;
    public float BreathAngle = 45f;
    [Tooltip("동작이 없을 때 — 불 뿜는 시간 (초)")]
    public float BreathTimeFallback = 4f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject ChantFx;
    public GameObject AwakenFx;
    [Tooltip("입 뼈 이름 — 푸스로다 · 브레스 이펙트가 여기서 나옴 (비우면 몸 앞 BreathOffset)")]
    public string MouthBone = "";
    public Vector3 BreathOffset = new(0f, 2.5f, 2.5f);
    public GameObject RoarFx;
    [Tooltip("브레스 불 — 불 뿜는 구간 동안 입(또는 BreathOffset)에 붙음, 앞 +Z")]
    public GameObject BreathFx;
    public GameObject GlideHitFx;
    public GameObject TakeOffFx;

    const int EvChant = 0, EvAwaken = 1, EvTakeOff = 2, EvFly = 3, EvGlideOff = 4, EvRoar = 5, EvRoarHit = 6,
              EvBreath = 7, EvFireOn = 8, EvFireOff = 9, EvGlideHit = 10, EvFell = 11, EvActEnd = 12;

    protected override TransformForm[] Forms => new[] { Dragon };

    enum Act { None, TakeOff, Glide, Roar, Breath }

    private int _chants;
    private bool _awakened;
    private float _eventEnd;
    private bool _won;

    private Act _act;
    private float _actEnd;                      // 동작 끝 (푸스로다 · 브레스)
    private float _hitAt;                       // 이륙 프레임 · 외침 프레임
    private bool _hitDone;
    private float _liftUntil, _flyAnimAt;
    private bool _flyAnimSent;
    private float _fireOn, _fireOff, _fireNext;
    private bool _fireSent;
    private readonly HashSet<PlayerHealth> _glideHit = new();
    private readonly Dictionary<PlayerRef, float> _damagers = new();
    private CharacterController _cc;
    private GameObject _breathFxInst;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    int IndexOf(AbilityData.SkillDef s) => Data.Skills != null ? System.Array.IndexOf(Data.Skills, s) : -1;
    AbilityData.SkillDef Skill(int i) => Data.Skills != null && i >= 0 && i < Data.Skills.Length ? Data.Skills[i] : null;
    bool IsDragonSkill(AbilityData.SkillDef s) { int i = IndexOf(s); return i == GlideSkill || i == RoarSkill || i == BreathSkill; }
    bool Busy => _act != Act.None;
    bool IsGlide(AbilityData.SkillDef s) => IndexOf(s) == GlideSkill;
    public int Chants => _chants;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        base.OnEquip();
        _chants = 0;
        _awakened = false;
        _won = false;
        _act = Act.None;
        _cc = Ctx.GetComponent<CharacterController>();
        if (Ctx.HasStateAuthority && Ctx.Health != null) Ctx.Health.Damaged += OnDamaged;
    }

    public override void OnUnequip()
    {
        if (Ctx.Health != null) Ctx.Health.Damaged -= OnDamaged;
        if (Ctx.HasStateAuthority) { EndAct(false); RestoreSlots(); }
        if (_breathFxInst != null) Destroy(_breathFxInst);
        base.OnUnequip();
    }

    void OnDamaged(DamageInfo info, int amount)
    {
        if (!_awakened || !info.FromPlayer || info.Attacker == PlayerRef.None) return;
        _damagers[info.Attacker] = Now;
    }

    protected override void OnDiedWhileEquipped(PlayerRef killer, DeathCause cause)
    {
        EndAct(false);
        RestoreSlots();
        _chants = 0;
        if (!_awakened) return;
        _awakened = false;

        // 전설의 용 처치 보상
        var me = Ctx.Object.InputAuthority;
        if (killer != PlayerRef.None && killer != me)
        {
            var ko = Ctx.Runner.GetPlayerObject(killer);
            if (ko != null && ko.TryGetComponent<PlayerHealth>(out var kh)) kh.RPC_GainSoul(KillBonus);
        }
        foreach (var kv in _damagers)
        {
            if (kv.Key == killer || kv.Key == me || Now - kv.Value > AssistWindow) continue;
            var ao = Ctx.Runner.GetPlayerObject(kv.Key);
            if (ao != null && ao.TryGetComponent<PlayerHealth>(out var ah)) ah.RPC_GainSoul(AssistBonus);
        }
        _damagers.Clear();

        var comms = PlayerComms.Local;
        if (comms != null)
            comms.Notice(NoticeKind.Big, killer != PlayerRef.None
                ? TargetUtil.Text("notice.dragon.slain", "전설의 용 {0}이(가) {1}에게 쓰러졌다!", TargetUtil.NameOf(Ctx), TargetUtil.NameOf(Ctx.Runner, killer))
                : TargetUtil.Text("notice.dragon.fell", "전설의 용 {0}이(가) 쓰러졌다!", TargetUtil.NameOf(Ctx)), true);
        Broadcast(EvFell, Ctx.transform.position);
    }

    // ══════════════════ 스킬 ══════════════════

    protected override bool AllowedWhileTransformed(AbilityData.SkillDef skill) => IsDragonSkill(skill);
    protected override bool NeedsTransform(AbilityData.SkillDef skill) => IsDragonSkill(skill);

    public override bool CanUseSkill(AbilityData.SkillDef skill)
    {
        bool transformed = IsTransformed || CurrentFormFromAlter() != null;
        if (transformed)
        {
            if (_act == Act.Glide && IsGlide(skill)) return true;        // 활공 중 활공 = 끄기 (쿨이 이미 돌았을 때)
            if (Busy) return false;                           // 동작 중엔 다른 스킬 X
            if (Attack != null && Attack.IsAttacking) return false;     // 깨무는 중
            if (!IsDragonSkill(skill)) return false;          // 용일 땐 용 스킬만 (다른 능력 것 포함)
        }
        return base.CanUseSkill(skill);
    }

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        int i = IndexOf(skill);
        if (i < 0) return false;

        if (i == ChantSkill) { Chant(); return true; }
        if (!IsTransformed) return true;
        if (i == GlideSkill)
        {
            if (_act == Act.Glide) EndAct(true);       // 활공 중 다시 → 끝 (보통은 OnSkillRecast가 먼저 받음)
            else StartGlide();
            return true;
        }
        if (i == RoarSkill) { StartRoar(); return true; }
        if (i == BreathSkill) { StartBreath(); return true; }
        return false;
    }

    // ── 용언 ──

    void Chant()
    {
        if (IsTransformed) return;
        string line = ChantLines != null && ChantLines.Length > 0 ? ChantLines[_chants % ChantLines.Length] : "…";
        _chants++;

        var comms = PlayerComms.Local;
        if (comms != null)
            comms.Notice(NoticeKind.Small, $"{TargetUtil.NameOf(Ctx)} : {LocUtil.Resolve(line)}",
                Ctx.transform.position, ChantRange, PlayerRef.None, true);
        Broadcast(EvChant, Ctx.transform.position + Vector3.up * 2f);

        if (_chants >= ChantsNeeded) Awaken();
    }

    void Awaken()
    {
        BeginForm(Dragon);
        if (!IsTransformed) return;

        _awakened = true;
        _won = false;
        _damagers.Clear();
        _eventEnd = Now + EventTime;
        ApplySlots();

        var comms = PlayerComms.Local;
        if (comms != null)
            comms.Notice(NoticeKind.Big, TargetUtil.Text("notice.dragon.awaken",
                "전설의 용 {0}이(가) 깨어났다! {1}분 안에 쓰러뜨리지 못하면 {0}의 승리",
                TargetUtil.NameOf(Ctx), Mathf.RoundToInt(EventTime / 60f)), true);
        Broadcast(EvAwaken, Ctx.transform.position);
    }

    /// <summary>다른 사람이 먼저 용이 됨 — 내 용언 초기화 (이 사람 화면 = 권한자)</summary>
    void ResetByOther()
    {
        if (_chants <= 0 || IsTransformed) return;
        _chants = 0;
        var comms = PlayerComms.Local;
        if (comms != null)
            comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.dragon.reset", "다른 용이 깨어나 용언이 모두 흩어졌다…"),
                Vector3.zero, 0f, Ctx.Object.InputAuthority, false);
    }

    // ── 칸 바꿔 끼우기 (Q · E · 궁 = 용 스킬) ──

    void ApplySlots()
    {
        foreach (int i in new[] { GlideSkill, RoarSkill, BreathSkill })
        {
            var s = Skill(i);
            if (s == null) continue;
            int slot = s.Slot.Index();
            if (Ctx.GetSlot(slot) != s) Ctx.OverrideSlot(slot, s, true);
        }
    }

    void RestoreSlots()
    {
        foreach (int i in new[] { GlideSkill, RoarSkill, BreathSkill })
        {
            var s = Skill(i);
            if (s == null) continue;
            int slot = s.Slot.Index();
            if (Ctx.GetSlot(slot) == s) Ctx.RestoreSlot(slot);
        }
    }

    // ── 공통 ──

    AlterClipAnimator Anim => Ctx.GetComponentInChildren<AlterClipAnimator>();

    /// <summary>상태의 Hit Frame 시간 · 길이 (없으면 fallback)</summary>
    float AnimHit(string state, float fallback) { var a = Anim; float t = a != null ? a.EventTime(state) : 0f; return t > 0f ? t : fallback; }
    float AnimLen(string state, float fallback) { var a = Anim; float t = a != null ? a.StateDuration(state) : -1f; return t > 0f ? t : fallback; }

    void Lock(bool on)
    {
        if (Attack != null) Attack.Locked = on;
    }

    /// <summary>동작 끝 (권한자). glideCooldown이면 활공 쿨 시작</summary>
    void EndAct(bool glideCooldown)
    {
        if (_act == Act.None) return;
        var was = _act;
        _act = Act.None;
        if (Movement != null) { Movement.ExternalControl = false; Movement.InputLocked = false; }
        Lock(false);
        if (was == Act.Breath && _fireSent) Broadcast(EvFireOff, Ctx.transform.position);
        if (was == Act.TakeOff || was == Act.Glide) Broadcast(EvGlideOff, Ctx.transform.position);
        else Broadcast(EvActEnd, Ctx.transform.position);
        if (glideCooldown || was == Act.TakeOff || was == Act.Glide) ReleaseCooldown(GlideSkill);
    }

    // ── 활공 ──

    void StartGlide()
    {
        if (Movement == null) return;
        _glideHit.Clear();
        Movement.ExternalControl = true;                 // 위치는 이 능력이 직접
        Lock(true);
        _flyAnimSent = false;

        if (Movement.IsAirborne)
        {
            // 공중 — 바로 활공
            _act = Act.Glide;
            _liftUntil = 0f;
            _flyAnimAt = Now;
            return;
        }

        // 땅 — 이륙 동작 (제자리) → Hit Frame에 떠오름
        _act = Act.TakeOff;
        _hitAt = Now + AnimHit(TakeOffAnim, TakeOffDelayFallback);
        _flyAnimAt = Now + AnimLen(TakeOffAnim, TakeOffDelayFallback);
        Broadcast(EvTakeOff, Ctx.transform.position);
    }

    public override bool OnSkillRecast(int slot, AbilityData.SkillDef skill)
    {
        if (!IsGlide(skill)) return false;
        if (_act == Act.Glide) { EndAct(true); return true; }        // 다시 누르면 활공 끝 → 쿨
        return _act == Act.TakeOff;                                    // 이륙 중엔 무시
    }

    public override bool CanRecast(AbilityData.SkillDef skill) => IsGlide(skill) && _act == Act.Glide;

    void TickGlide(float dt)
    {
        if (_act == Act.TakeOff)
        {
            if (Now < _hitAt) return;                     // 제자리에서 이륙 동작
            _act = Act.Glide;
            _liftUntil = Now + LiftTime;
        }

        if (!_flyAnimSent && Now >= _flyAnimAt) { _flyAnimSent = true; Broadcast(EvFly, Ctx.transform.position); }

        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        Vector3 dir = cam.forward.normalized;
        Vector3 vel = dir * GlideSpeed;
        if (Now < _liftUntil) vel += Vector3.up * LiftSpeed;
        if (_cc != null && _cc.enabled) _cc.Move(vel * dt);
        else Ctx.transform.position += vel * dt;

        // 부딪힌 적 (한 번 활공에 한 번씩)
        Vector3 c = Ctx.transform.position + Vector3.up + dir * 1.2f;
        var me = Ctx.Object.InputAuthority;
        foreach (var h in AbilityAim.InRadius(Ctx, c, GlideHitRadius))
        {
            if (!_glideHit.Add(h)) continue;
            float dmg = AbilityAim.SkillDamage(Ctx, GlideDamageCoef, out bool crit);
            AbilityAim.Hit(Ctx, h, dmg, c, crit);
            if (GlideStun > 0f && h.TryGetComponent<PlayerStatus>(out var st)) st.Apply(StatusType.Stun, 1f, GlideStun, me);
            if (h.TryGetComponent<PlayerMovement>(out var mv))
                mv.AddKnockback(AbilityAim.PushAway(Ctx.transform.position, h, GlidePush, GlidePushUp, dir) + new Vector3(dir.x, 0f, dir.z) * GlidePush * 0.5f);
            Broadcast(EvGlideHit, TargetUtil.Chest(h));
        }
    }

    // ── 푸스로다 ──

    void StartRoar()
    {
        _act = Act.Roar;
        _hitDone = false;
        _hitAt = Now + AnimHit(RoarAnim, 0.4f);
        _actEnd = Now + AnimLen(RoarAnim, 1f);
        if (Movement != null) Movement.InputLocked = true;
        Lock(true);
        Broadcast(EvRoar, Ctx.transform.position);
    }

    void TickRoar()
    {
        if (!_hitDone && Now >= _hitAt) { _hitDone = true; RoarHit(); }
        if (Now >= _actEnd) EndAct(false);
    }

    void RoarHit()
    {
        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        Vector3 fwd = cam.forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Ctx.transform.forward;
        Vector3 origin = Ctx.transform.position + Vector3.up;
        var me = Ctx.Object.InputAuthority;

        foreach (var h in AbilityAim.InCone(Ctx, origin, fwd, RoarRange, RoarAngle, RoarHeight))
        {
            float dmg = AbilityAim.SkillDamage(Ctx, RoarDamageCoef, out bool crit);
            AbilityAim.Hit(Ctx, h, dmg, origin, crit);
            if (RoarStun > 0f && h.TryGetComponent<PlayerStatus>(out var st)) st.Apply(StatusType.Stun, 1f, RoarStun, me);
            if (h.TryGetComponent<PlayerMovement>(out var mv))
                mv.RPC_KnockbackSlam(AbilityAim.PushAway(Ctx.transform.position, h, RoarPush, RoarPushUp, fwd), WallSlamPct, me, SlamWindow);
        }
        Broadcast(EvRoarHit, Ctx.transform.position + fwd * 2f + Vector3.up * 2f);
    }

    // ── 브레스 ──

    void StartBreath()
    {
        _act = Act.Breath;
        var a = Anim;
        float on = a != null ? a.FrameTime(BreathAnim, BreathStartFrame) : -1f;
        float off = a != null ? a.FrameTime(BreathAnim, BreathEndFrame) : -1f;
        if (on < 0f || off <= on) { on = 0.3f; off = on + BreathTimeFallback; }
        _fireOn = Now + on;
        _fireOff = Now + off;
        _fireNext = _fireOn;
        _fireSent = false;
        _actEnd = Now + Mathf.Max(off + 0.1f, AnimLen(BreathAnim, off + 0.5f));
        if (Movement != null) Movement.InputLocked = true;           // 못 움직임 · 시점은 돎
        Lock(true);
        Broadcast(EvBreath, Ctx.transform.position);
    }

    void TickBreath()
    {
        if (Now >= _fireOn && Now <= _fireOff)
        {
            if (!_fireSent) { _fireSent = true; Broadcast(EvFireOn, Ctx.transform.position); }
            if (Now >= _fireNext)
            {
                float tick = Mathf.Max(0.05f, BreathTick);
                _fireNext += tick;
                float window = Mathf.Max(0.1f, _fireOff - _fireOn);
                float perTick = BreathTotalDamage * tick / window;
                var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
                Vector3 origin = MouthPos();
                foreach (var h in AbilityAim.InCone(Ctx, origin, cam.forward, BreathRange, BreathAngle, 0f, false))
                    AbilityAim.Hit(Ctx, h, perTick, origin, false, false);
            }
        }
        else if (_fireSent && Now > _fireOff)
        {
            _fireSent = false;
            Broadcast(EvFireOff, Ctx.transform.position);
        }
        if (Now >= _actEnd) EndAct(false);
    }

    Transform Mouth()
    {
        var a = Anim;
        return a != null ? a.FindBone(MouthBone) : null;
    }

    Vector3 MouthPos()
    {
        var m = Mouth();
        return m != null ? m.position : Ctx.transform.position + Ctx.transform.rotation * BreathOffset;
    }

    // ══════════════════ 진행 (권한자) ══════════════════

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (!IsTransformed) { if (Busy) EndAct(false); return; }

        ApplySlots();                       // 능력이 다시 만들어져 칸이 원래대로 돌아갔을 때
        switch (_act)
        {
            case Act.TakeOff:
            case Act.Glide: TickGlide(dt); break;
            case Act.Roar: TickRoar(); break;
            case Act.Breath: TickBreath(); break;
        }

        // 시간 안에 못 쓰러뜨림 → 승리
        if (_awakened && !_won && Now >= _eventEnd && Ctx.Health != null && !Ctx.Health.IsDead)
        {
            _won = true;
            var rm = RoundManager.Instance;
            if (rm != null) rm.RPC_DeclareWinner(Ctx.Object.InputAuthority);
            var comms = PlayerComms.Local;
            if (comms != null)
                comms.Notice(NoticeKind.Big, TargetUtil.Text("notice.dragon.win", "전설의 용 {0}의 승리!", TargetUtil.NameOf(Ctx)), true);
        }
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    protected override void OnFormEvent(int key, Vector3 pos)
    {
        switch (key)
        {
            case EvChant:
                Ctx.PlayEffectLocal(ChantFx, pos);
                break;

            case EvAwaken:
                Ctx.PlayEffectLocal(AwakenFx, pos);
                OnAwakenEverywhere();
                break;

            case EvTakeOff:
                if (Alter != null) Alter.PlayPartState(Dragon.Alter, TakeOffAnim);
                Ctx.PlayEffectLocal(TakeOffFx, pos);
                break;
            case EvFly:
                if (Alter != null) Alter.PlayPartState(Dragon.Alter, FlyAnim, 9999f);     // 끌 때까지 반복
                break;
            case EvGlideOff:
            case EvActEnd:
                if (Alter != null) Alter.ReleasePartState(Dragon.Alter);
                StopFire();
                break;

            case EvGlideHit:
                Ctx.PlayEffectLocal(GlideHitFx, pos);
                break;

            case EvRoar:
                if (Alter != null) Alter.PlayPartState(Dragon.Alter, RoarAnim);
                break;
            case EvRoarHit:
            {
                var m = Mouth();
                Vector3 at = m != null ? m.position : pos;
                if (RoarFx != null) Destroy(Instantiate(RoarFx, at, Ctx.transform.rotation), 3f);
                Shockwave.Feel(at, RoarFeel, Ctx.transform);
                break;
            }

            case EvBreath:
                if (Alter != null) Alter.PlayPartState(Dragon.Alter, BreathAnim);
                break;
            case EvFireOn:
            {
                StopFire();
                var m = Mouth();
                _breathFxInst = m != null
                    ? AbilityAim.Attach(BreathFx, m, Vector3.zero, Quaternion.identity, 30f)
                    : AbilityAim.Attach(BreathFx, Ctx.transform, BreathOffset, Quaternion.identity, 30f);
                break;
            }
            case EvFireOff:
                StopFire();
                break;

            case EvFell:
                break;
        }
    }

    void StopFire()
    {
        if (_breathFxInst != null) { Destroy(_breathFxInst); _breathFxInst = null; }
    }

    /// <summary>모든 화면 — 이벤트 HUD 줄 + 다른 용언 가진 사람 초기화 (그 사람 화면에서)</summary>
    void OnAwakenEverywhere()
    {
        // 다른 전설의 용언 — 그 사람이 권한자인 화면에서 초기화
        foreach (var h in TargetUtil.All(true))
        {
            if (h == null || h.transform.root == Ctx.transform.root) continue;
            if (!h.TryGetComponent<AbilityRuntime>(out var ar) || !ar.HasStateAuthority) continue;
            var other = ar.FindAbility<DragonWordAbility>();
            if (other != null) other.ResetByOther();
        }

        // 남은 시간 줄 (내 화면)
        var owner = Ctx;
        var hp = Ctx.Health;
        string who = TargetUtil.NameOf(Ctx);
        bool mine = Ctx.HasInputAuthority;
        HudExtras.AddTimed(Data, TargetUtil.Text("hud.dragon.name", "전설의 용"), HudExtras.Number(Data, 1), EventTime, left =>
        {
            if (owner == null || hp == null || hp.IsDead || CurrentFormFromAlter() == null) return null;
            string t = $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}";
            return mine
                ? TargetUtil.Text("hud.dragon.self", "{0} 동안 살아남으면 승리", t)
                : TargetUtil.Text("hud.dragon.left", "{0}을(를) {1} 안에 쓰러뜨리세요", who, t);
        });
    }

    public override string DescToken(string key, int rank) => key switch
    {
        "chant" => IsTransformed ? "전설의 용" : $"{_chants} / {ChantsNeeded}",
        "chants" => _chants.ToString(),
        "chantsneed" => ChantsNeeded.ToString(),
        "chantsleft" => Mathf.Max(0, ChantsNeeded - _chants).ToString(),
        _ => null,
    };

    public override string DevCommand(string[] a)
    {
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "help";
        switch (sub)
        {
            case "help": return "전설의 용언: /job chant 수 (용언 횟수) · awaken (바로 용) · time 초 (이벤트 시간)";
            case "chant":
                if (a.Length > 1 && int.TryParse(a[1], out int n)) _chants = Mathf.Max(0, n);
                return $"용언 {_chants} / {ChantsNeeded}";
            case "awaken": _chants = ChantsNeeded; Awaken(); return "전설의 용";
            case "time":
                if (a.Length > 1 && float.TryParse(a[1], out float s)) EventTime = Mathf.Max(5f, s);
                return $"이벤트 {EventTime}초";
        }
        return null;
    }

    public override IEnumerable<(string value, string label)> DevComplete(string[] a, int index) =>
        index == 0 ? new[] { ("chant", "용언 횟수"), ("awaken", "바로 용"), ("time", "이벤트 시간") } : null;
}
