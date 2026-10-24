using Fusion;
using UnityEngine;

/// <summary>
/// 체스의 거신병 — 적 처치 · 처치 관여마다 '폰' +1. 폰 6개 → 거신병으로 변신 (깡스탯 · 마지막 무기 스탯 그대로 · 좌클릭 공격).
///   스킬 '나이트 이동'(F, 변신 중에만): 앞으로 도약(박찰 때 쿵) → 착지 지점 주변 피해 · 기절 · 밀치기 · 흔들림 → 한 번 더 앞으로 도약
///     → 잠시 동안 다시 누르면 반반 확률로 왼쪽/오른쪽 대각선 앞으로 도약 (착지 때 다시 주변 피해 · 기절 · 밀치기)
///   패시브 '프로모션'(변신 중): 체력 18% 이하인 대상에게 피해를 주면 즉시 처형
/// 죽으면 변신이 풀리고 (랜덤 능력도 바뀜).
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Chess Colossus")]
public class ChessAbility : TransformAbility
{
    public int KnightSkill = 0;

    [Header("형태")]
    public TransformForm Colossus = new();

    [Header("폰")]
    [Tooltip("변신에 필요한 폰")]
    public int PawnsNeeded = 6;

    [Header("나이트 이동")]
    [Tooltip("도약 — 앞으로 속도 (m/초) · 위로 속도 (m/초)")]
    public float LeapForward = 16f, LeapUp = 7f;
    [Tooltip("뛰어오를 때 — 주변 기절 · 밀치기 · 흔들림 (끄면 없음)")]
    public ShockwaveDef TakeoffWave = new()
    {
        Enabled = true,
        Radius = 2.5f,
        DamageCoef = 0f,
        Stun = 0.5f,
        Push = 6f,
        PushUp = 3f,
        Feel = new FeelDef { ShakeRadius = 10f, ShakeAmount = 0.2f },
    };
    [Tooltip("착지할 때 — 주변 피해 · 기절 · 밀치기 · 흔들림")]
    public ShockwaveDef LandWave = new()
    {
        Enabled = true,
        Radius = 3.5f,
        DamageCoef = 120f,
        Stun = 1f,
        Push = 9f,
        PushUp = 4f,
        Feel = new FeelDef { ShakeRadius = 14f, ShakeAmount = 0.45f },
    };
    [Tooltip("두 번째 도약 뒤 대각선 도약을 쓸 수 있는 시간 (초)")]
    public float DiagonalWindow = 3f;
    [Tooltip("대각선 각도 (°)")]
    public float DiagonalAngle = 45f;
    [Tooltip("착지하고 다음 도약까지 쉬는 시간 (초) — 착지 동작 · 충격이 보이게")]
    public float ChainDelay = 0.35f;

    [Header("프로모션")]
    [Tooltip("이 체력 비율 이하인 대상에게 피해를 주면 처형 (0.18 = 18%)")]
    [Range(0f, 1f)] public float PromotionBelow = 0.18f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject PawnFx;
    public GameObject TransformFx;
    public GameObject LandFx;
    [Tooltip("도약할 때 거신병 모델이 재생할 상태 (AlterClipAnimator · RigAnimator의 상태 이름). 비우면 없음")]
    public string LeapAnim = "Leap";
    [Tooltip("착지할 때 재생할 상태. AlterClipAnimator를 쓰면 비우기 — 땅에 가까워지면 알아서 착지 동작 (Air → Land State)")]
    public string LandAnim = "";
    const int EvPawn = 0, EvTransform = 1, EvLand = 2, EvLeap = 3, EvLeapQuiet = 4, EvLandQuiet = 5;

    protected override TransformForm[] Forms => new[] { Colossus };


    private int _pawns;
    private enum Leap { None, First, Second, Diagonal }
    private Leap _leap;
    private bool _leftGround;
    private float _leapStart;
    private float _diagonalUntil;
    private float _diagonalSide;
    private Leap _pending;              // 착지 뒤 잠깐 쉬고 이어 뛸 도약
    private float _pendingAt;
    private bool _locked;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == KnightSkill;

    // ══════════════════ 프로모션 — 맞는 쪽에서 계산 ══════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Register() => DamageRules.Register(Promotion);

    static float Promotion(PlayerHealth target, NetworkObject attacker, DamageInfo info)
    {
        if (attacker == null || info.IsStatusTick) return 1f;
        if (!attacker.TryGetComponent<AbilityRuntime>(out var ar)) return 1f;
        var me = ar.FindAbility<ChessAbility>();
        if (me == null || me.Colossus == null || me.Colossus.Alter == null) return 1f;
        if (!attacker.TryGetComponent<PlayerAlter>(out var alter) || !alter.Has(me.Colossus.Alter)) return 1f;

        var st = target.GetComponent<PlayerStats>();
        float max = st != null ? st.Get(StatType.MaxHP) : 0f;
        if (max <= 0f || target.HP > max * me.PromotionBelow) return 1f;
        return 10000f;          // 처형
    }

    // ══════════════════ 폰 ══════════════════

    public override void OnEquip()
    {
        base.OnEquip();
        _pawns = 0;
        _leap = Leap.None;
        _pending = Leap.None;
        _diagonalUntil = 0f;
        _locked = false;
        if (Ctx.HasStateAuthority && Ctx.Health != null)
        {
            Ctx.Health.KilledPlayer += OnKill;
            Ctx.Health.Assisted += OnAssist;
        }
    }

    public override void OnUnequip()
    {
        if (Ctx.HasStateAuthority) { _diagonalUntil = 0f; FinishSequence(); }
        if (Ctx.Health != null)
        {
            Ctx.Health.KilledPlayer -= OnKill;
            Ctx.Health.Assisted -= OnAssist;
        }
        base.OnUnequip();
    }

    void OnKill(PlayerRef victim) => AddPawn();
    void OnAssist() => AddPawn();

    void AddPawn()
    {
        if (IsTransformed) return;
        _pawns++;
        Broadcast(EvPawn, Ctx.transform.position + Vector3.up * 2f);
        if (_pawns < PawnsNeeded) return;

        BeginForm(Colossus);
        Broadcast(EvTransform, Ctx.transform.position + Vector3.up);
        var comms = PlayerComms.Local;
        if (comms != null)
            comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.chess", "{0}이(가) 체스의 거신병으로 승격했다!", TargetUtil.NameOf(Ctx)));
    }

    protected override void OnDiedWhileEquipped(PlayerRef killer, DeathCause cause)
    {
        _pawns = 0;
        _diagonalUntil = 0f;
        FinishSequence();
    }

    // ══════════════════ 나이트 이동 ══════════════════

    protected override bool NeedsTransform(AbilityData.SkillDef skill) => IsMine(skill);

    /// <summary>도약 중 (이어 뛸 도약 대기 포함) — 이동 · 공격 · 다른 스킬 막음</summary>
    bool Busy => _leap != Leap.None || _pending != Leap.None;

    public override bool CanUseSkill(AbilityData.SkillDef skill)
    {
        if (Busy) return false;                 // 도약 중엔 어떤 스킬도 (다른 능력 것 포함)
        return base.CanUseSkill(skill);
    }

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill) || !IsTransformed) return false;
        StartLeap(Leap.First, 0f);
        return true;
    }

    public override bool OnSkillRecast(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill) || !IsTransformed) return false;
        if (Busy || Now > _diagonalUntil) return false;
        _diagonalUntil = 0f;
        StartLeap(Leap.Diagonal, _diagonalSide * DiagonalAngle);
        return true;
    }

    public override bool CanRecast(AbilityData.SkillDef skill) =>
        IsMine(skill) && IsTransformed && !Busy && Now <= _diagonalUntil;

    void StartLeap(Leap kind, float yawOffset)
    {
        if (Movement == null) return;
        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        Vector3 fwd = cam.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Ctx.transform.forward;
        fwd = Quaternion.Euler(0f, yawOffset, 0f) * fwd.normalized;

        // 박차고 오를 때 쿵 — 착지 직후 이어 뛰는 두 번째 도약은 방금 착지 충격이 있었으니 동작만
        bool quiet = kind == Leap.Second;
        if (!quiet) Shockwave.Hit(Ctx, Ctx.transform.position, TakeoffWave);
        Movement.AddKnockback(fwd * LeapForward + Vector3.up * LeapUp);
        Broadcast(quiet ? EvLeapQuiet : EvLeap, Ctx.transform.position);
        _leap = kind;
        _leftGround = false;
        _leapStart = Now;
        SetLocked(true);
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (Movement == null) return;

        // 대각선 기다리다 시간 다 됨 → 끝 (쿨타임 시작)
        if (!Busy && _diagonalUntil > 0f && Now > _diagonalUntil)
        {
            _diagonalUntil = 0f;
            FinishSequence();
        }

        // 착지 뒤 잠깐 쉬고 이어 뛰기
        if (_pending != Leap.None)
        {
            // 쉬는 시간이 지났고, 착지 동작(+ 회복)도 끝났으면 — 착지 동작을 도약이 끊지 않게
            if (Now >= _pendingAt && Time.time >= Movement.LockMoveUntil)
            {
                var next = _pending;
                _pending = Leap.None;
                StartLeap(next, 0f);
            }
            return;
        }

        if (_leap == Leap.None) return;

        bool grounded = Movement.Grounded;
        if (!grounded) { _leftGround = true; return; }
        if (!_leftGround && Now - _leapStart < 0.6f) return;     // 아직 안 떴음

        var done = _leap;
        _leap = Leap.None;
        LandHit();                                                // 착지마다 쿵 — 피해 · 기절 · 밀치기 · 흔들림

        switch (done)
        {
            case Leap.First:
                _pending = Leap.Second;
                _pendingAt = Now + ChainDelay;
                break;
            case Leap.Second:
                SetLocked(false);
                _diagonalSide = Random.value < 0.5f ? -1f : 1f;
                _diagonalUntil = Now + DiagonalWindow;             // 이 시간 안에 F 다시 → 대각선
                break;
            case Leap.Diagonal:
                FinishSequence();
                break;
        }
    }

    /// <summary>도약 끝 — 조작 풀고, '효과 끝나고 쿨타임'이면 이제 쿨타임 시작</summary>
    void FinishSequence()
    {
        _leap = Leap.None;
        _pending = Leap.None;
        SetLocked(false);
        if (Data.Skills != null && KnightSkill >= 0 && KnightSkill < Data.Skills.Length)
            Ctx.ReleaseCooldown(Data.Skills[KnightSkill]);
    }

    void SetLocked(bool on)
    {
        if (_locked == on) return;
        _locked = on;
        if (Movement != null) Movement.InputLocked = on;
        if (Attack != null) Attack.Locked = on;
    }

    void LandHit()
    {
        Vector3 p = Ctx.transform.position;
        Shockwave.Hit(Ctx, p, LandWave);
        Broadcast(EvLand, p);
    }

    protected override void OnFormEvent(int key, Vector3 pos)
    {
        // 모델 동작 (모든 화면)
        string anim = key switch { EvLeap or EvLeapQuiet => LeapAnim, EvLand or EvLandQuiet => LandAnim, _ => null };
        if (!string.IsNullOrEmpty(anim) && Alter != null) Alter.PlayPartState(Colossus.Alter, anim);

        // 쿵 — 소리 · 흔들림 (모든 화면, 각자 거리 따라)
        if (key == EvLeap && TakeoffWave.Enabled) Shockwave.Feel(pos, TakeoffWave.Feel, Ctx.transform);
        // (EvLandQuiet은 이제 안 씀 — 착지는 매번 EvLand)
        if ((key == EvLand || key == EvLandQuiet) && LandWave.Enabled) Shockwave.Feel(pos, LandWave.Feel, Ctx.transform);

        GameObject fx = key switch { EvPawn => PawnFx, EvTransform => TransformFx, EvLand => LandFx, _ => null };
        if (fx != null) Ctx.PlayEffectLocal(fx, pos);
    }

    public override string DescToken(string key, int rank) => key switch
    {
        "pawn" => IsTransformed ? "승격함" : $"폰 {_pawns} / {PawnsNeeded}",
        "pawns" => _pawns.ToString(),                       // 숫자만 — "현재 폰 {x:pawns}개"
        "pawnsneed" => PawnsNeeded.ToString(),
        "pawnsleft" => Mathf.Max(0, PawnsNeeded - _pawns).ToString(),
        _ => null,
    };
}
