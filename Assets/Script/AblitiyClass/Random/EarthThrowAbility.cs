using UnityEngine;

/// <summary>
/// 지구 던지기 — 스킬(F) 순서:
///   ① 준비 모션 (스킬 데이터의 모션) → 효과 타이밍(모션 종료)에 바라본 곳(레이)을 기억
///   ② 천장이든 뭐든 뚫고 RiseHeight m 위로 빠르게 올라감
///   ③ 다 올라가면 던지기 모션(ThrowMotion) → 그 모션의 판정 프레임에 지구를 던짐
///   ④ 닿으면 반경 ExplodeRadius m 폭발, 안의 적 Damage 피해 → 잠시 뒤 원래 자리로 (올라가 있는 동안 고스트 — 끌 수 있음)
/// 데이터: 스킬 1개 F · 쿨 500 · 모션 = 준비 모션 · 효과 타이밍 = 모션 종료 · 대상 필요 끄기
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Earth Throw")]
public class EarthThrowAbility : AbilityBehaviour
{
    public int ThrowSkill = 0;

    [Header("올라가기")]
    public float RiseHeight = 100f;
    [Tooltip("올라가는 속도 (m/초)")]
    public float RiseSpeed = 80f;
    [Tooltip("올라가 있는 동안 고스트 (아무 공격도 안 맞음)")]
    public bool GhostWhileCasting = true;

    [Header("조준")]
    [Tooltip("조준 레이 거리 (m) — 아무것도 안 맞으면 그 거리 끝")]
    public float AimRange = 1500f;
    [Tooltip("조준 레이가 맞는 것 (바닥 · 벽 · 플레이어)")]
    public LayerMask AimMask = ~0;

    [Header("던지기")]
    [Tooltip("다 올라간 뒤 재생할 던지기 모션 — 이 모션의 판정 프레임(판정 시작)에 지구가 손을 떠남. 비우면 WindUp초 뒤 바로")]
    public AbilityMotionData ThrowMotion;
    const int ThrowTag = 1;

    [Header("지구")]
    [Tooltip("지구가 날아가는 시간 (초)")]
    public float FlightTime = 2f;
    [Tooltip("던지기 모션이 없을 때 — 꼭대기에서 던지기 전 잠깐 (초)")]
    public float WindUp = 0.4f;
    public float ExplodeRadius = 50f;
    [Tooltip("고정 피해")]
    public float Damage = 3000f;
    [Tooltip("폭발 뒤 원래 자리로 돌아가기까지 (초)")]
    public float ReturnDelay = 1.5f;

    [Header("연출 — 모든 클라이언트")]
    [Tooltip("지구 모델/VFX (비우면 파란 구) — 이 크기로 늘림")]
    public GameObject EarthPrefab;
    [Tooltip("지구 지름 (m)")]
    public float EarthSize = 14f;
    [Tooltip("지구 회전 (°/초)")]
    public float EarthSpin = 90f;
    public GameObject RiseFx;
    public GameObject ExplosionFx;
    [Tooltip("폭발 이펙트 크기 배율")]
    public float ExplosionFxScale = 5f;
    public FeelDef ExplosionFeel = new() { ShakeRadius = 120f, ShakeAmount = 1.2f, ShakeSelf = 0.5f, SoundRange = 200f };
    public GameObject ReturnFx;

    const int EvRise = 0, EvFrom = 1, EvTo = 2, EvBoom = 3, EvReturn = 4;

    enum Phase { None, Rise, WindUp, Throwing, Fly, Wait }
    private Phase _phase;
    private float _phaseEnd;
    private Vector3 _start, _top, _aim;
    private Quaternion _startRot;
    private Vector3 _from;

    private PlayerMovement _move;
    private PlayerAttack _attack;
    private PlayerStatus _status;
    private CharacterController _cc;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == ThrowSkill;
    bool Busy => _phase != Phase.None;

    public override void OnEquip()
    {
        _move = Ctx.GetComponent<PlayerMovement>();
        _attack = Ctx.GetComponent<PlayerAttack>();
        _status = Ctx.GetComponent<PlayerStatus>();
        _cc = Ctx.GetComponent<CharacterController>();
        _phase = Phase.None;
    }

    public override void OnUnequip()
    {
        if (Ctx.HasStateAuthority && Busy) Finish(false);
    }

    public override bool CanUseSkill(AbilityData.SkillDef skill) => !Busy;

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        if (Busy) return true;

        // 쓴 순간 바라본 곳
        var cam = Ctx.CameraRootTransform != null ? Ctx.CameraRootTransform : Ctx.transform;
        _aim = CastUtil.Raycast(cam.position, cam.forward, AimRange, AimMask, Ctx.transform.root, out var hit)
            ? hit.point
            : cam.position + cam.forward * AimRange;

        _start = Ctx.transform.position;
        _startRot = Ctx.transform.rotation;
        _top = _start + Vector3.up * RiseHeight;

        if (_move != null) _move.ExternalControl = true;
        if (_attack != null) _attack.Locked = true;
        if (_cc != null) _cc.enabled = false;                   // 천장 뚫기
        if (GhostWhileCasting)
            _status?.Apply(StatusType.Ghost, 1f, RiseHeight / Mathf.Max(1f, RiseSpeed) + WindUp + FlightTime + ReturnDelay + 3f, Ctx.Object.InputAuthority);

        _phase = Phase.Rise;
        Broadcast(EvRise, _start);
        return true;
    }

    public override void Tick(float dt)
    {
        if (_phase == Phase.None) return;
        if (Ctx.Health != null && Ctx.Health.IsDead) { Finish(false); return; }

        switch (_phase)
        {
            case Phase.Rise:
                Ctx.transform.position = Vector3.MoveTowards(Ctx.transform.position, _top, RiseSpeed * dt);
                if ((Ctx.transform.position - _top).sqrMagnitude < 0.01f)
                {
                    if (ThrowMotion != null && ThrowMotion.Step != null)
                    {
                        _phase = Phase.Throwing;                 // 던지기 모션 → 판정 프레임에 OnChainHit
                        _phaseEnd = Now + ThrowMotion.Step.TotalTime + 2f;     // 안전장치
                        ChainMotion(ThrowTag);
                    }
                    else
                    {
                        _phase = Phase.WindUp;
                        _phaseEnd = Now + WindUp;
                    }
                }
                break;

            case Phase.WindUp:
                Ctx.transform.position = _top;
                if (Now >= _phaseEnd) Throw();
                break;

            case Phase.Throwing:
                Ctx.transform.position = _top;
                if (Now >= _phaseEnd) Throw();                   // 모션이 끊겼을 때 대비
                break;

            case Phase.Fly:
                Ctx.transform.position = _top;
                if (Now < _phaseEnd) break;
                Explode();
                _phase = Phase.Wait;
                _phaseEnd = Now + ReturnDelay;
                break;

            case Phase.Wait:
                Ctx.transform.position = _top;
                if (Now >= _phaseEnd) Finish(true);
                break;
        }
    }

    public override AbilityMotionData GetChainMotion(int tag) => tag == ThrowTag ? ThrowMotion : null;

    public override void OnChainHit(int tag)
    {
        if (tag == ThrowTag && _phase == Phase.Throwing) Throw();
    }

    /// <summary>지구가 손을 떠남</summary>
    void Throw()
    {
        Vector3 toAim = _aim - _top;
        _from = _top + Vector3.up * 2f + (toAim.sqrMagnitude > 1f ? toAim.normalized : Vector3.down) * (EarthSize * 0.6f);
        Broadcast(EvFrom, _from);
        Broadcast(EvTo, _aim);
        _phase = Phase.Fly;
        _phaseEnd = Now + FlightTime;
    }

    void Explode()
    {
        foreach (var h in AbilityAim.InRadius(Ctx, _aim, ExplodeRadius))
            AbilityAim.Hit(Ctx, h, Damage, _aim, false, true);
        Broadcast(EvBoom, _aim);
    }

    /// <summary>끝 — back이면 원래 자리로</summary>
    void Finish(bool back)
    {
        _phase = Phase.None;
        if (_move != null) _move.ExternalControl = false;
        if (_attack != null) _attack.Locked = false;
        if (GhostWhileCasting) _status?.Clear(StatusType.Ghost);

        bool alive = Ctx.Health != null && !Ctx.Health.IsDead;
        if (back && alive)
        {
            Ctx.Health.Teleport(_start, _startRot);             // CC도 다시 켬
            Broadcast(EvReturn, _start);
        }
        else if (_cc != null && !_cc.enabled) _cc.enabled = alive;
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        switch (key)
        {
            case EvRise: Ctx.PlayEffectLocal(RiseFx, pos); break;
            case EvFrom: _from = pos; break;
            case EvTo: EarthThrowVisual.Spawn(EarthPrefab, _from, pos, FlightTime, EarthSize, EarthSpin); break;
            case EvBoom:
                if (ExplosionFx != null)
                {
                    var fx = Instantiate(ExplosionFx, pos, Quaternion.identity);
                    fx.transform.localScale *= ExplosionFxScale;
                    Destroy(fx, 6f);
                }
                Shockwave.Feel(pos, ExplosionFeel, Ctx.transform);
                break;
            case EvReturn: Ctx.PlayEffectLocal(ReturnFx, pos); break;
        }
    }
}
