using Fusion;
using UnityEngine;

/// <summary>
/// 체스의 거신병 — 적 처치 · 처치 관여마다 '폰' +1. 폰 6개 → 거신병으로 변신 (깡스탯 · 마지막 무기 스탯 그대로 · 좌클릭 공격).
///   스킬 '나이트 이동'(F, 변신 중에만): 앞으로 도약 → 착지 지점 주변 피해 · 기절 · 밀치기 → 한 번 더 앞으로 도약
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
    [Tooltip("착지 피해 반경 (m)")]
    public float LandRadius = 3.5f;
    [Tooltip("착지 피해 — 공격력 %")]
    public float LandCoef = 120f;
    [Tooltip("착지 기절 (초)")]
    public float LandStun = 1f;
    [Tooltip("착지 밀치기 — 옆 · 위 (m/초)")]
    public float LandPush = 9f, LandPushUp = 4f;
    [Tooltip("두 번째 도약 뒤 대각선 도약을 쓸 수 있는 시간 (초)")]
    public float DiagonalWindow = 3f;
    [Tooltip("대각선 각도 (°)")]
    public float DiagonalAngle = 45f;

    [Header("프로모션")]
    [Tooltip("이 체력 비율 이하인 대상에게 피해를 주면 처형 (0.18 = 18%)")]
    [Range(0f, 1f)] public float PromotionBelow = 0.18f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject PawnFx;
    public GameObject TransformFx;
    public GameObject LandFx;
    [Tooltip("도약할 때 거신병 모델이 재생할 상태 (AlterClipAnimator · RigAnimator의 상태 이름). 비우면 없음")]
    public string LeapAnim = "Leap";
    [Tooltip("착지할 때 재생할 상태. 비우면 없음")]
    public string LandAnim = "Land";
    const int EvPawn = 0, EvTransform = 1, EvLand = 2, EvLeap = 3;

    protected override TransformForm[] Forms => new[] { Colossus };

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[24];

    private int _pawns;
    private enum Leap { None, First, Second, Diagonal }
    private Leap _leap;
    private bool _leftGround;
    private float _leapStart;
    private float _diagonalUntil;
    private float _diagonalSide;

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
        if (Ctx.HasStateAuthority && Ctx.Health != null)
        {
            Ctx.Health.KilledPlayer += OnKill;
            Ctx.Health.Assisted += OnAssist;
        }
    }

    public override void OnUnequip()
    {
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
        _leap = Leap.None;
    }

    // ══════════════════ 나이트 이동 ══════════════════

    protected override bool NeedsTransform(AbilityData.SkillDef skill) => IsMine(skill);

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill) || !IsTransformed) return false;
        StartLeap(Leap.First, 0f);
        return true;
    }

    public override bool OnSkillRecast(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill) || !IsTransformed) return false;
        if (_leap != Leap.None || Now > _diagonalUntil) return false;
        _diagonalUntil = 0f;
        StartLeap(Leap.Diagonal, _diagonalSide * DiagonalAngle);
        return true;
    }

    public override bool CanRecast(AbilityData.SkillDef skill) =>
        IsMine(skill) && IsTransformed && _leap == Leap.None && Now <= _diagonalUntil;

    void StartLeap(Leap kind, float yawOffset)
    {
        if (Movement == null) return;
        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        Vector3 fwd = cam.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Ctx.transform.forward;
        fwd = Quaternion.Euler(0f, yawOffset, 0f) * fwd.normalized;

        Movement.AddKnockback(fwd * LeapForward + Vector3.up * LeapUp);
        Broadcast(EvLeap, Ctx.transform.position);
        _leap = kind;
        _leftGround = false;
        _leapStart = Now;
    }

    public override void Tick(float dt)
    {
        base.Tick(dt);
        if (_leap == Leap.None || Movement == null) return;

        bool grounded = Movement.Grounded;
        if (!grounded) { _leftGround = true; return; }
        if (!_leftGround && Now - _leapStart < 0.6f) return;     // 아직 안 떴음

        var done = _leap;
        _leap = Leap.None;

        switch (done)
        {
            case Leap.First:
                LandHit();
                StartLeap(Leap.Second, 0f);
                break;
            case Leap.Second:
                _diagonalSide = Random.value < 0.5f ? -1f : 1f;
                _diagonalUntil = Now + DiagonalWindow;
                break;
            case Leap.Diagonal:
                LandHit();
                break;
        }
    }

    void LandHit()
    {
        Vector3 p = Ctx.transform.position;
        var me = Ctx.Object.InputAuthority;
        var stats = Ctx.Stats;

        int n = Physics.OverlapSphereNonAlloc(p, LandRadius, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        var seen = new System.Collections.Generic.HashSet<PlayerHealth>();
        for (int i = 0; i < n; i++)
        {
            var t = _hits[i].GetComponentInParent<PlayerHealth>();
            if (t == null || t.IsDead || !seen.Add(t) || !TargetUtil.IsEnemy(Ctx, t)) continue;

            float dmg = DamageCalc.CalcOutgoing(stats, stats.Get(StatType.AttackPower) * LandCoef * 0.01f, DamageSource.Skill, true, out bool crit);
            var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), me, p, true);
            info.IsCrit = crit;
            t.RPC_TakeDamage(info);

            if (t.TryGetComponent<PlayerStatus>(out var st)) st.Apply(StatusType.Stun, 1f, LandStun, me);
            if (t.TryGetComponent<PlayerMovement>(out var mv))
            {
                Vector3 away = t.transform.position - p; away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Ctx.transform.forward;
                mv.AddKnockback(away * LandPush + Vector3.up * LandPushUp);
            }
        }
        Broadcast(EvLand, p);
    }

    protected override void OnFormEvent(int key, Vector3 pos)
    {
        // 모델 동작 (모든 화면)
        string anim = key switch { EvLeap => LeapAnim, EvLand => LandAnim, _ => null };
        if (!string.IsNullOrEmpty(anim) && Alter != null) Alter.PlayPartState(Colossus.Alter, anim);

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
