using Fusion;
using UnityEngine;

/// <summary>
/// 데스 노트 — F → 플레이어 선택 창(스킬 데이터 '플레이어 먼저 고르기') → 고르면 모션 → 효과 타이밍에 이름을 적음:
///   모두에게 "서걱서걱…" 작은 메시지 → Delay초 뒤 그 사람이 심장 마비로 사망 (자살 판정 — 아무도 소울 안 받음) + 글로벌 채팅 기록.
/// 창을 닫으면 아무것도 안 씀. 예약은 능력이 바뀌어도(내가 죽어도) 남음.
/// 데이터: 플레이어 먼저 고르기 켜기 · 효과 타이밍 = 모션 종료(또는 판정 프레임) · 쿨 64 · 대상 필요 끄기
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Death Note")]
public class DeathNoteAbility : AbilityBehaviour
{
    public int NoteSkill = 0;

    [Tooltip("적은 뒤 사망까지 (초)")]
    public float Delay = 60f;

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == NoteSkill;

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        Write(Ctx.TakePickedTarget());
        ReleaseCooldown(NoteSkill);
        return true;
    }

    void Write(PlayerHealth target)
    {
        var comms = PlayerComms.Local;
        if (target == null || comms == null) return;

        comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.deathnote.write", "서걱서걱… 누군가 노트를 끄적였다"));

        float delay = Delay;

        // 내 화면 HUD — 누구를 적었는지 · 남은 시간 (능력이 바뀌어도 남음). 대상이 먼저 죽으면 사라짐
        string name = TargetUtil.NameOf(target);
        HudExtras.AddTimed(Data, TargetUtil.Text("hud.deathnote.name", "데스 노트"), HudExtras.Number(Data, 1), delay,
            left => target == null || target.IsDead ? null
                  : TargetUtil.Text("hud.deathnote.left", "{0} — {1}초 뒤 심장 마비", name, Mathf.CeilToInt(left)));

        GameTimers.After(delay, () =>
        {
            if (target == null || target.IsDead || PlayerComms.Local == null) return;
            PlayerComms.Local.Notice(NoticeKind.Log,
                TargetUtil.Text("notice.deathnote.die", "{0}이(가) 심장 마비로 사망하였습니다..", TargetUtil.NameOf(target)));
            target.RPC_ForceKill(PlayerRef.None, true);
        });
    }
}
