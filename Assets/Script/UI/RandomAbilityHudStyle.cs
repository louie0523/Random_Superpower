using TMPro;
using UnityEngine;

/// <summary>
/// 랜덤 능력 HUD 모양 — Create → Game → UI → Random Ability HUD Style 로 만들어 RandomAbilityHud의 Style 칸에.
/// 줄 프리팹을 쓰든 기본 모양을 쓰든 모든 줄(추가 줄 포함)에 적용됨.
/// </summary>
[CreateAssetMenu(menuName = "Game/UI/Random Ability HUD Style")]
public class RandomAbilityHudStyle : ScriptableObject
{
    [Header("글꼴")]
    [Tooltip("이름 · 번호 · 요약 모두. 비우면 줄 프리팹(또는 TMP 기본) 그대로")]
    public TMP_FontAsset Font;
    [Tooltip("글자 크기 — 0이면 그대로")]
    public float NameSize, NumberSize, SummarySize;

    [Header("요약")]
    [Tooltip("요약이 길면 이 줄 수까지 줄(배경)이 아래로 늘어남. 넘으면 글자만 아래로. 0 = 제한 없음 (3줄 · 4줄도 배경이 같이)")]
    [Min(0)] public int MaxSummaryLines = 0;
    [Tooltip("요약 칸의 {x:} 상태 글(현재 궁극기 사용 횟수 등)을 다시 계산하는 간격 (초)")]
    [Min(0.05f)] public float LiveRefresh = 0.2f;

    [Header("쌓기")]
    [Tooltip("줄 사이 (Layout Group이 없을 때)")]
    public float RowGap = 6f;
}
