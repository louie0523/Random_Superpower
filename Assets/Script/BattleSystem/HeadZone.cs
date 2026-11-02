using UnityEngine;

/// <summary>
/// 이 콜라이더에 맞으면 헤드샷. 플레이어 프리팹의 머리 콜라이더나 특수한 모양의 머리에 붙임.
/// 변화(변신) 피격 범위는 AlterData → 피격 범위 → '머리' 체크로 자동으로 붙음.
/// 없으면 높이(PlayerHealth → 헤드샷 → 머리 높이)로 판정.
/// </summary>
[DisallowMultipleComponent]
public class HeadZone : MonoBehaviour { }
