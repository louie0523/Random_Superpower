using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 전설의 용언 — 스킬(F) '전설의 용언': 주문을 외움 → 작은 메시지 "A : (주문)" (주위 ChantRange m 안 사람만 보임).
///   ChantsNeeded번 외우면 '전설의 용'으로 변신 + 글로벌 이벤트:
///     EventTime초 안에 아무도 처치하지 못하면 그 플레이어의 즉시 승리.
///     처치하면 처치한 사람 소울 +KillBonus, 처치 관여 +AssistBonus.
///   전설의 용 (변신 — 깡스탯 · 모습 · 기본 공격은 형태 칸 / AlterData, 쥔 무기 스탯 그대로):
///     스킬 1 활공 — 바라보는 방향으로 GlideTime초 날며 돌진. 부딪힌 적 피해 · 밀치기 · 잠깐 기절
///     스킬 2 푸스로다 — 앞으로 폭음파: 피해 · 기절 · 크게 밀치기. 밀려나다 벽에 부딪히면 최대 체력 WallSlamPct% 추가 피해
///     궁극기 브레스 — BreathTime초 동안 브레스 (움직일 수 없음, 시점은 돎). 계속 맞으면 최대 BreathTotalDamage
///   다른 사람이 먼저 전설의 용이 되면 내 용언 횟수는 0으로 (작은 메시지).
/// 데이터: Skills 4개 — 0 용언(F) · 1 활공(Q, 처음엔 숨김) · 2 푸스로다(E, 처음엔 숨김) · 3 브레스(궁극기, 처음엔 숨김).
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

    [Header("활공")]
    public float GlideTime = 5f;
    public float GlideSpeed = 18f;
    [Tooltip("부딪힘 판정 반지름 (m)")]
    public float GlideHitRadius = 2.5f;
    [Tooltip("피해 — 공격력 %")]
    public float GlideDamageCoef = 80f;
    public float GlidePush = 12f, GlidePushUp = 5f;
    public float GlideStun = 1f;
    public string GlideAnim = "Glide";

    [Header("푸스로다")]
    public float RoarRange = 12f;
    public float RoarAngle = 70f;
    public float RoarHeight = 3f;
    public float RoarDamageCoef = 60f;
    public float RoarPush = 22f, RoarPushUp = 4f;
    public float RoarStun = 1.5f;
    [Tooltip("밀려나다 벽에 부딪히면 최대 체력의 이 %만큼 추가 피해")]
    public float WallSlamPct = 20f;
    [Tooltip("벽꿍을 보는 시간 (초) — 밀려나는 동안")]
    public float SlamWindow = 1f;
    public string RoarAnim = "Roar";
    public FeelDef RoarFeel = new() { ShakeRadius = 20f, ShakeAmount = 0.35f, ShakeSelf = 0.3f };

    [Header("브레스 (궁극기)")]
    public float BreathTime = 5f;
    [Tooltip("브레스를 끝까지 계속 맞으면 받는 피해 합 (고정 피해)")]
    public float BreathTotalDamage = 3000f;
    [Tooltip("피해 간격 (초)")]
    public float BreathTick = 0.25f;
    public float BreathRange = 14f;
    public float BreathAngle = 45f;
    public string BreathAnim = "Breath";

    [Header("연출 — 모든 클라이언트")]
    public GameObject ChantFx;
    public GameObject AwakenFx;
    public GameObject RoarFx;
    [Tooltip("브레스 — 용에게 붙음 (BreathOffset 위치, 앞 +Z)")]
    public GameObject BreathFx;
    public Vector3 BreathOffset = new(0f, 2.5f, 2.5f);
    public GameObject GlideHitFx;

    const int EvChant = 0, EvAwaken = 1, EvGlideOn = 2, EvGlideOff = 3, EvRoar = 4, EvBreathOn = 5, EvBreathOff = 6, EvGlideHit = 7, EvFell = 8;

    protected override TransformForm[] Forms => new[] { Dragon };

    private int _chants;
    private bool _awakened;
    private float _eventEnd;
    private bool _won;
    private float _glideEnd = -1f, _breathEnd = -1f, _breathNext;
    private readonly HashSet<PlayerHealth> _glideHit = new();
    private readonly Dictionary<PlayerRef, float> _damagers = new();
    private CharacterController _cc;
    private GameObject _breathFxInst;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    int IndexOf(AbilityData.SkillDef s) => Data.Skills != null ? System.Array.IndexOf(Data.Skills, s) : -1;
    AbilityData.SkillDef Skill(int i) => Data.Skills != null && i >= 0 && i < Data.Skills.Length ? Data.Skills[i] : null;
    bool IsDragonSkill(AbilityData.SkillDef s) { int i = IndexOf(s); return i == GlideSkill || i == RoarSkill || i == BreathSkill; }
    bool Gliding => _glideEnd > 0f;
    bool Breathing => _breathEnd > 0f;
    public int Chants => _chants;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        base.OnEquip();
        _chants = 0;
        _awakened = false;
        _won = false;
        _glideEnd = _breathEnd = -1f;
        _cc = Ctx.GetComponent<CharacterController>();
        if (Ctx.HasStateAuthority && Ctx.Health != null) Ctx.Health.Damaged += OnDamaged;
    }

    public override void OnUnequip()
    {
        if (Ctx.Health != null) Ctx.Health.Damaged -= OnDamaged;
        if (Ctx.HasStateAuthority) { StopGlide(); StopBreath(); RestoreSlots(); }
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
        StopGlide();
        StopBreath();
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
            if (Gliding || Breathing) return false;          // 활공 · 브레스 중엔 다른 스킬 X
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
        if (i == GlideSkill) { StartGlide(); return true; }
        if (i == RoarSkill) { Roar(); return true; }
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

    // ── 활공 ──

    void StartGlide()
    {
        _glideEnd = Now + GlideTime;
        _glideHit.Clear();
        if (Movement != null) Movement.ExternalControl = true;
        if (Attack != null) Attack.Locked = true;
        Broadcast(EvGlideOn, Ctx.transform.position);
    }

    void TickGlide(float dt)
    {
        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        Vector3 dir = cam.forward.normalized;
        if (_cc != null && _cc.enabled) _cc.Move(dir * GlideSpeed * dt);
        else Ctx.transform.position += dir * GlideSpeed * dt;

        // 부딪힌 적
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

        if (Now >= _glideEnd) StopGlide();
    }

    void StopGlide()
    {
        if (!Gliding) return;
        _glideEnd = -1f;
        if (Movement != null) Movement.ExternalControl = false;
        if (Attack != null) Attack.Locked = false;
        if (Ctx.HasStateAuthority) Broadcast(EvGlideOff, Ctx.transform.position);
    }

    // ── 푸스로다 ──

    void Roar()
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
        Broadcast(EvRoar, Ctx.transform.position + fwd * 2f + Vector3.up * 2f);
    }

    // ── 브레스 ──

    void StartBreath()
    {
        _breathEnd = Now + BreathTime;
        _breathNext = Now;
        if (Movement != null) Movement.InputLocked = true;
        if (Attack != null) Attack.Locked = true;
        Broadcast(EvBreathOn, Ctx.transform.position);
    }

    void TickBreath()
    {
        if (Now >= _breathNext)
        {
            _breathNext += Mathf.Max(0.05f, BreathTick);
            var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
            Vector3 origin = Ctx.transform.position + Ctx.transform.rotation * new Vector3(0f, BreathOffset.y, 0f);
            float perTick = BreathTotalDamage * Mathf.Max(0.05f, BreathTick) / Mathf.Max(0.1f, BreathTime);
            foreach (var h in AbilityAim.InCone(Ctx, origin, cam.forward, BreathRange, BreathAngle, 0f, false))
                AbilityAim.Hit(Ctx, h, perTick, origin, false, false);
        }
        if (Now >= _breathEnd) StopBreath();
    }

    void StopBreath()
    {
        if (!Breathing) return;
        _breathEnd = -1f;
        if (Movement != null) Movement.InputLocked = false;
        if (Attack != null) Attack.Locked = false;
        if (Ctx.HasStateAuthority) Broadcast(EvBreathOff, Ctx.transform.position);
    }

    // ══════════════════ 진행 (권한자) ══════════════════

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (!IsTransformed) return;

        ApplySlots();                       // 능력이 다시 만들어져 칸이 원래대로 돌아갔을 때
        if (Gliding) TickGlide(dt);
        if (Breathing) TickBreath();

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

            case EvGlideOn:
                if (Alter != null) Alter.PlayPartState(Dragon.Alter, GlideAnim, GlideTime);
                break;
            case EvGlideOff:
            case EvBreathOff:
                if (Alter != null) Alter.ReleasePartState(Dragon.Alter);
                if (key == EvBreathOff && _breathFxInst != null) { Destroy(_breathFxInst); _breathFxInst = null; }
                break;

            case EvGlideHit:
                Ctx.PlayEffectLocal(GlideHitFx, pos);
                break;

            case EvRoar:
                if (Alter != null) Alter.PlayPartState(Dragon.Alter, RoarAnim);
                if (RoarFx != null) Destroy(Instantiate(RoarFx, pos, Ctx.transform.rotation), 3f);
                Shockwave.Feel(pos, RoarFeel, Ctx.transform);
                break;

            case EvBreathOn:
                if (Alter != null) Alter.PlayPartState(Dragon.Alter, BreathAnim, BreathTime);
                if (_breathFxInst != null) Destroy(_breathFxInst);
                _breathFxInst = AbilityAim.Attach(BreathFx, Ctx.transform, BreathOffset, Quaternion.identity, BreathTime + 0.5f);
                break;

            case EvFell:
                break;
        }
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
