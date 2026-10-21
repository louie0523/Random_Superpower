using Fusion;
using UnityEngine;

/// <summary>
/// 데스 노트 — 플레이어 선택 창에서 아무나(나 · 팀 포함) 지정 → 모두에게 "서걱서걱…" 작은 메시지 →
/// 60초 뒤 그 사람이 심장 마비로 사망 (자살 판정 — 아무도 소울 안 받음) + 글로벌 채팅 기록.
/// 쿨타임 64초는 데이터에서. 창을 닫으면(취소) 쿨타임이 돌아옴.
/// 예약은 능력이 바뀌어도(내가 죽어도) 남음.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Death Note")]
public class DeathNoteAbility : AbilityBehaviour
{
    public int NoteSkill = 0;

    [Tooltip("적은 뒤 사망까지 (초)")]
    public float Delay = 60f;

    bool IsMine(AbilityData.SkillDef s) => Data.Skills != null && System.Array.IndexOf(Data.Skills, s) == NoteSkill;

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        if (!IsMine(skill)) return false;
        if (!Ctx.HasInputAuthority) return true;          // 더미 등 — 고를 사람이 없음

        var ctx = Ctx;
        PlayerPickPanel.Open(TargetUtil.Text("ability.deathnote.pick", "이름을 적을 사람"), true,
            picked => Write(picked),
            () => ctx.ResetCooldown(slot));
        return true;
    }

    void Write(PlayerHealth target)
    {
        var comms = PlayerComms.Local;
        if (target == null || comms == null) return;

        comms.Notice(NoticeKind.Small, TargetUtil.Text("notice.deathnote.write", "서걱서걱… 누군가 노트를 끄적였다"));

        float delay = Delay;
        GameTimers.After(delay, () =>
        {
            if (target == null || target.IsDead || PlayerComms.Local == null) return;
            PlayerComms.Local.Notice(NoticeKind.Log,
                TargetUtil.Text("notice.deathnote.die", "{0}이(가) 심장 마비로 사망하였습니다..", TargetUtil.NameOf(target)));
            target.RPC_ForceKill(PlayerRef.None, true);
        });
    }
}
