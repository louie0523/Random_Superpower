using UnityEngine;

/// <summary>
/// 범골의 의지 — '언데드' 상태: 체력이 1 아래로 안 떨어짐.
/// 처음으로 체력이 1이 되면 이동 속도 · 공격력 대폭 증가 → 그때부터 20초 뒤 언데드 · 버프가 영구히 사라짐.
/// (즉사 효과 — 데스 노트 · 필연 등 — 는 언데드도 무시)
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Undead Will")]
public class UndeadWillAbility : AbilityBehaviour
{
    [Tooltip("체력 1이 된 뒤 버티는 시간 (초)")]
    public float LastStand = 20f;
    [Tooltip("이동 속도 증가 (%)")]
    public float MoveSpeedPercent = 60f;
    [Tooltip("공격력 증가 (%)")]
    public float AttackPercent = 100f;

    [Header("연출 — 모든 클라이언트")]
    public GameObject AwakenFx;
    public GameObject EndFx;
    const int EvAwaken = 0, EvEnd = 1;

    private enum Phase { Waiting, LastStand, Spent }
    private Phase _phase;
    private float _endAt;
    private PlayerStatus _status;

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;

    public override void OnEquip()
    {
        _status = Ctx.GetComponent<PlayerStatus>();
        _phase = Phase.Waiting;
        if (Ctx.HasStateAuthority && _status != null && Ctx.Health != null && !Ctx.Health.IsDead)
            _status.Apply(StatusType.Undead, 1f, 99999f, Ctx.Object.InputAuthority);
    }

    public override void OnUnequip()
    {
        if (!Ctx.HasStateAuthority) return;
        if (Ctx.Stats != null) Ctx.Stats.RemoveBySource(Src(0));
        if (_status != null && _phase != Phase.Spent) _status.Clear(StatusType.Undead);
    }

    public override void OnTrigger(TriggerType t, GameObject other)
    {
        // 라운드 초기화 등으로 부활했는데 아직 안 쓴 상태면 다시 언데드
        if (t == TriggerType.OnRespawn && _phase == Phase.Waiting && _status != null)
            _status.Apply(StatusType.Undead, 1f, 99999f, Ctx.Object.InputAuthority);
    }

    public override void Tick(float dt)
    {
        var h = Ctx.Health;
        if (h == null || h.IsDead) return;

        switch (_phase)
        {
            case Phase.Waiting:
                if (h.HP <= 1 && _status != null && _status.Has(StatusType.Undead))
                {
                    _phase = Phase.LastStand;
                    _endAt = Now + LastStand;
                    Ctx.Stats.AddModifier(new StatModifier(StatType.MoveSpeed, ModType.Percent, MoveSpeedPercent, Src(0)));
                    Ctx.Stats.AddModifier(new StatModifier(StatType.AttackPower, ModType.Percent, AttackPercent, Src(0)));
                    Broadcast(EvAwaken, Ctx.transform.position + Vector3.up);
                }
                break;

            case Phase.LastStand:
                if (Now >= _endAt)
                {
                    _phase = Phase.Spent;
                    Ctx.Stats.RemoveBySource(Src(0));
                    if (_status != null) _status.Clear(StatusType.Undead);
                    Broadcast(EvEnd, Ctx.transform.position + Vector3.up);
                }
                break;
        }
    }

    public override string DescToken(string key, int rank)
    {
        if (key == "left")            // 최후의 발악 남은 초 (숫자만, 발악 중일 때만)
            return _phase == Phase.LastStand ? Mathf.CeilToInt(_endAt - Now).ToString() : null;
        if (key != "undead") return null;
        return _phase switch
        {
            Phase.Waiting => "언데드 — 아직 버팀",
            Phase.LastStand => $"최후의 발악 {Mathf.CeilToInt(_endAt - Now)}초",
            _ => "다 써 버림",
        };
    }

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        Ctx.PlayEffectLocal(key == EvAwaken ? AwakenFx : EndFx, pos);
    }
}
