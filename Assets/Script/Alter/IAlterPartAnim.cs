/// <summary>
/// 변화 파츠의 코드 애니메이션 (DragonWave 등) — PlayerAlter가 RigAnimator와 같은 신호를 줌.
///   SetMove: 매 프레임 이동 여부 · 속도
///   PlayState: PlayPartState("Attack") 등 — 모르는 상태면 false
///   ReleaseState: ReleasePartState (비행 끝 등)
///   PlayAttack: 변화 기본 공격 — 판정 시간에 맞추라고 길이 · 판정 순간을 같이 줌
/// 파츠 프리팹 안의 아무 MonoBehaviour에 붙이면 PlayerAlter가 자동으로 찾음.
/// </summary>
public interface IAlterPartAnim
{
    void SetMove(bool moving, float speed);
    bool PlayState(string state, float hold);
    void ReleaseState();
    /// <summary>변화 기본 공격 (AlterData 공격) — duration초짜리, hitAt초에 판정</summary>
    void PlayAttack(string state, float duration, float hitAt);
}
