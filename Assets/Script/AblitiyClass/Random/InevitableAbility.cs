using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 필연적인 존재 — 이 능력을 가진 동안 궁극기 쿨타임이 40% 빨리 줄어듦.
/// 궁극기를 5번 쓰면 큰 알림 "○○이(가) 필연적인 힘을 사용합니다!" → 잠시 뒤 살아 있는 적의 절반(내림, 무작위)이 사망 (내 처치로 인정).
/// 그 뒤 스택은 처음부터. 스택은 능력이 바뀌면(죽으면) 사라짐.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Inevitable")]
public class InevitableAbility : AbilityBehaviour
{
    [Tooltip("궁극기 쿨타임이 줄어드는 속도 증가 (%) — 40 = 1.4배 빠르게")]
    public float UltCdrSpeedPercent = 40f;
    [Tooltip("이 횟수만큼 궁극기를 쓰면 발동")]
    public int UsesNeeded = 5;
    [Tooltip("알림 뒤 사망까지 (초)")]
    public float SnapDelay = 3f;
    [Tooltip("살아 있는 적 중 죽는 비율 (0.5 = 절반, 내림)")]
    [Range(0f, 1f)] public float SnapRatio = 0.5f;

    [Header("연출 — 공용 이펙트 (PlayerComms 목록)")]
    public GameObject SnapFx;
    public GameObject VictimFx;

    private int _uses;
    private float _prevRemain;
    private bool _prevHeld;

    static int UltSlot => SkillSlot.Ultimate.Index();

    public override void OnEquip()
    {
        _uses = 0;
        _prevRemain = Ctx.GetCooldownRemain(UltSlot);
        _prevHeld = Ctx.IsCooldownHeld(UltSlot);
    }

    public override void Tick(float dt)
    {
        // 사용 감지 — 쿨타임이 새로 걸렸거나(늘어남) · '효과 끝나고 쿨타임' 대기가 새로 시작됨
        float remain = Ctx.GetCooldownRemain(UltSlot);
        bool held = Ctx.IsCooldownHeld(UltSlot);
        if ((held && !_prevHeld) || (!held && remain > _prevRemain + 0.5f && !_prevHeld)) OnUltUsed();
        _prevHeld = held;

        // 쿨타임 가속
        if (!held && remain > 0f) Ctx.ShiftCooldown(UltSlot, dt * UltCdrSpeedPercent * 0.01f);
        _prevRemain = Ctx.GetCooldownRemain(UltSlot);
    }

    void OnUltUsed()
    {
        _uses++;
        if (_uses < UsesNeeded) return;
        _uses = 0;
        Snap();
    }

    void Snap()
    {
        var comms = PlayerComms.Local;
        if (comms != null)
        {
            comms.Notice(NoticeKind.Big, TargetUtil.Text("notice.inevitable", "{0}이(가) 필연적인 힘을 사용합니다!", TargetUtil.NameOf(Ctx)));
            if (SnapFx != null) comms.PlayFx(SnapFx, Ctx.transform.position + Vector3.up);
        }

        var ctx = Ctx;
        var me = Ctx.Object.InputAuthority;
        float ratio = SnapRatio;
        var victimFx = VictimFx;

        GameTimers.After(SnapDelay, () =>
        {
            if (ctx == null) return;
            var alive = TargetUtil.AliveEnemies(ctx);
            int n = Mathf.FloorToInt(alive.Count * ratio);
            if (n <= 0) return;

            TargetUtil.Shuffle(alive);
            var c = PlayerComms.Local;
            for (int i = 0; i < n; i++)
            {
                if (c != null && victimFx != null) c.PlayFx(victimFx, TargetUtil.Chest(alive[i]));
                alive[i].RPC_ForceKill(me, false);
            }
        });
    }

    public override string DescToken(string key, int rank) => key switch
    {
        "stack" => $"{_uses} / {UsesNeeded}",
        "ult" => _uses.ToString(),                          // 현재 궁극기 사용 횟수 (숫자만)
        "ultneed" => UsesNeeded.ToString(),
        "ultleft" => Mathf.Max(0, UsesNeeded - _uses).ToString(),
        _ => null,
    };
}
