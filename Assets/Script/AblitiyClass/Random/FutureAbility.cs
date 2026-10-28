using Fusion;
using UnityEngine;

/// <summary>
/// 미래로 가자! — 스킬(F): 앞으로 손을 뻗는 모션 → 판정에 닿은 적 한 명을 껴안음 (무기 내려놓음).
///   껴안긴 적은 시전자 앞(CarryDistance m)에 고정 · 시점만 돌릴 수 있음. 둘 다 고스트.
///   잠시 뒤 작은 메시지 "A : 나와 함께 미래로 가자 B!" → 시전자 뒤에서 불꽃 · 벽이든 뭐든 뚫고 앞으로 아주 빠르게 돌진
///   → DashTime초 뒤(맵 밖) 대상 확정 처치 + 시전자도 사망. 시전자는 그 처치 소울 × SoulMultiplier.
///   누가 먼저 죽으면 전부 풀림 (대상이 먼저 죽으면 시전자는 출발한 자리로 돌아옴).
/// 데이터: 스킬 1개 F · 효과 타이밍 = 판정 프레임 · Range / HitAngle / HitHeight = 손 판정 범위 · 대상 필요 끄기
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Future")]
public class FutureAbility : AbilityBehaviour
{
    public int GoSkill = 0;

    [Header("껴안기")]
    [Tooltip("껴안은 대상 위치 — 시전자 기준 앞 (m). 이미 만든 에셋은 값이 그대로라 인스펙터에서 1.5로")]
    public float CarryDistance = 1.5f;
    [Tooltip("껴안은 대상 높이 (m)")]
    public float CarryHeight = 0f;
    [Tooltip("껴안고 출발까지 (초)")]
    public float HugDelay = 1.5f;
    [Tooltip("껴안는 팔 자세 — 모션 편집 창에서 만든 모션. '무기 숨기기' 켜기 (무기 내려놓음). 끝 자세로 계속 버팀")]
    public AbilityMotionData HugMotion;

    [Header("돌진")]
    [Tooltip("돌진 속도 (m/초) — 벽 · 땅 무시")]
    public float DashSpeed = 60f;
    [Tooltip("돌진 시간 (초) — 끝나면 둘 다 사망")]
    public float DashTime = 10f;
    [Tooltip("처치 소울 배율 (3 = 평소 처치의 3배)")]
    public float SoulMultiplier = 3f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject GrabFx;
    [Tooltip("돌진 중 시전자 뒤 불꽃 — 시전자에게 붙음")]
    public GameObject FireFx;
    public Vector3 FireOffset = new(0f, 1f, -0.8f);
    public GameObject VanishFx;

    const int EvHug = 0, EvLaunch = 1, EvEnd = 2;

    enum Phase { None, Hug, Dash }
    private Phase _phase;
    private float _phaseEnd;
    private PlayerHealth _target;
    private Vector3 _start;
    private Quaternion _startRot;
    private Vector3 _dir;

    private PlayerMovement _move;
    private PlayerAttack _attack;
    private PlayerStatus _status;
    private CharacterController _cc;
    private WeaponAnimator _weaponAnim;
    private GameObject _fire;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == GoSkill;
    bool Busy => _phase != Phase.None;

    public override void OnEquip()
    {
        _move = Ctx.GetComponent<PlayerMovement>();
        _attack = Ctx.GetComponent<PlayerAttack>();
        _status = Ctx.GetComponent<PlayerStatus>();
        _cc = Ctx.GetComponent<CharacterController>();
        _weaponAnim = Ctx.GetComponentInChildren<WeaponAnimator>(true);
        _phase = Phase.None;
    }

    public override void OnUnequip()
    {
        if (Ctx.HasStateAuthority && Busy) Abort(false);
        StopVisuals();
    }

    // 껴안고 있는 동안엔 어떤 스킬도 못 씀
    public override bool CanUseSkill(AbilityData.SkillDef skill) => !Busy;

    // ══════════════════ 시작 (판정 프레임) ══════════════════

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        if (Busy) return true;

        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        float range = skill.Range > 0f ? skill.Range : 2.5f;
        float angle = skill.HitAngle > 0f ? skill.HitAngle : 90f;
        float height = skill.HitHeight > 0f ? skill.HitHeight : 1.5f;
        var list = AbilityAim.InCone(Ctx, Ctx.transform.position + Vector3.up, cam.forward, range, angle, height);

        PlayerHealth target = null;
        foreach (var h in list)
        {
            var m = h.GetComponent<PlayerMovement>();
            if (m != null && m.IsCarried) continue;           // 이미 누가 껴안음
            target = h;
            break;
        }
        if (target == null) return true;                      // 헛손질 — 쿨은 그대로

        Begin(target);
        return true;
    }

    void Begin(PlayerHealth target)
    {
        _target = target;
        _phase = Phase.Hug;
        _phaseEnd = Now + HugDelay;
        _start = Ctx.transform.position;
        _startRot = Ctx.transform.rotation;

        float total = HugDelay + DashTime + 3f;
        var me = Ctx.Object.InputAuthority;

        // 대상: 내 앞에 고정 + 고스트 + 기절 (시점만 돌릴 수 있음)
        var tm = target.GetComponent<PlayerMovement>();
        if (tm != null) tm.RPC_SetCarrier(Ctx.Object, new Vector3(0f, CarryHeight, CarryDistance));
        if (target.TryGetComponent<PlayerStatus>(out var ts))
        {
            ts.Apply(StatusType.Ghost, 1f, total, me);
            ts.Apply(StatusType.Stun, 1f, total, me);
        }

        // 나: 고스트 · 제자리 · 공격 막음
        _status?.Apply(StatusType.Ghost, 1f, total, me);
        if (_move != null) _move.ExternalControl = true;
        if (_attack != null) _attack.Locked = true;

        Broadcast(EvHug, TargetUtil.Chest(target));
    }

    // ══════════════════ 진행 (권한자) ══════════════════

    public override void Tick(float dt)
    {
        if (_phase == Phase.None) return;

        if (Ctx.Health != null && Ctx.Health.IsDead) { Abort(false); return; }
        if (_target == null || _target.IsDead || _target.Object == null || !_target.Object.IsValid)
        {
            Abort(_phase == Phase.Dash);          // 대상이 먼저 죽음 → 돌진 중이면 출발한 자리로
            return;
        }

        if (_phase == Phase.Hug)
        {
            if (Now < _phaseEnd) return;

            var comms = PlayerComms.Local;
            if (comms != null)
                comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.future", "{0} : 나와 함께 미래로 가자 {1}!",
                    TargetUtil.NameOf(Ctx), TargetUtil.NameOf(_target)), true);

            var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
            _dir = cam.forward;
            _dir.y = 0f;
            _dir = _dir.sqrMagnitude > 0.001f ? _dir.normalized : Ctx.transform.forward;

            if (_cc != null) _cc.enabled = false;                // 벽 · 땅 무시
            _phase = Phase.Dash;
            _phaseEnd = Now + DashTime;
            Broadcast(EvLaunch, Ctx.transform.position);
            return;
        }

        // 돌진
        Ctx.transform.position += _dir * DashSpeed * dt;
        if (Now >= _phaseEnd) Finish();
    }

    /// <summary>미래 도착 — 대상 확정 처치 + 소울 배율 + 나도 사망</summary>
    void Finish()
    {
        var t = _target;
        var me = Ctx.Object.InputAuthority;

        Broadcast(EvEnd, Ctx.transform.position);
        Release(t);

        if (t != null && !t.IsDead)
        {
            t.RPC_ForceKill(me, false);                          // 처치 인정 (어시스트 포함)
            int extra = Mathf.RoundToInt(t.SoulPerKill * Mathf.Max(0f, SoulMultiplier - 1f));
            if (extra > 0 && Ctx.Health != null) Ctx.Health.RPC_GainSoul(extra);
        }

        CleanupSelf();
        if (Ctx.Health != null && !Ctx.Health.IsDead) Ctx.Health.RPC_Sacrifice();
    }

    /// <summary>중간에 끝남 — 다 풀기. home이면 출발한 자리로</summary>
    void Abort(bool home)
    {
        Broadcast(EvEnd, Ctx.transform.position);
        Release(_target);
        CleanupSelf();

        if (home && Ctx.Health != null && !Ctx.Health.IsDead)
        {
            var cc = Ctx.GetComponent<CharacterController>();
            Vector3 safe = SpawnPoint.GroundedPosition(_start, cc, 10f, Ctx.transform.root);
            Ctx.Health.Teleport(safe, _startRot);
        }
    }

    void Release(PlayerHealth t)
    {
        if (t == null || t.Object == null || !t.Object.IsValid) return;
        var tm = t.GetComponent<PlayerMovement>();
        if (tm != null) tm.RPC_SetCarrier(null, Vector3.zero);
        if (t.TryGetComponent<PlayerStatus>(out var ts))
        {
            ts.Clear(StatusType.Stun);
            ts.Clear(StatusType.Ghost);
        }
    }

    void CleanupSelf()
    {
        _phase = Phase.None;
        _target = null;
        if (_move != null) _move.ExternalControl = false;
        if (_attack != null) _attack.Locked = false;
        _status?.Clear(StatusType.Ghost);
        if (_cc != null && !_cc.enabled) _cc.enabled = Ctx.Health == null || !Ctx.Health.IsDead;
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        switch (key)
        {
            case EvHug:
                Ctx.PlayEffectLocal(GrabFx, pos);
                if (_weaponAnim != null && HugMotion != null && HugMotion.Step != null)
                    _weaponAnim.PlayHold(HugMotion.Step);        // 껴안은 자세로 버팀
                break;

            case EvLaunch:
                if (_fire != null) Destroy(_fire);
                _fire = AbilityAim.Attach(FireFx, Ctx.transform, FireOffset, Quaternion.identity, DashTime + 1f);
                break;

            case EvEnd:
                Ctx.PlayEffectLocal(VanishFx, pos);
                StopVisuals();
                break;
        }
    }

    void StopVisuals()
    {
        if (_fire != null) { Destroy(_fire); _fire = null; }
        if (_weaponAnim != null && _weaponAnim.IsHolding) _weaponAnim.StopSwing();
    }

    public override string DevCommand(string[] a)
    {
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "help";
        if (sub == "help") return "미래로 가자: /job time 초 (돌진 시간 바꾸기 — 테스트용)";
        if (sub == "time" && a.Length > 1 && float.TryParse(a[1], out float s)) { DashTime = Mathf.Max(0.5f, s); return $"돌진 {DashTime}초"; }
        return null;
    }
}
