using UnityEngine;

/// <summary>
/// 이 오브젝트의 AudioSource를 어느 묶음에 넣을지 (안 붙이면 효과음).
/// 배경 음악 · UI 소리에만 붙이면 됨. 보이스(Photon Speaker)는 알아서 보이스로 감.
/// </summary>
[DisallowMultipleComponent]
public class SoundBusTag : MonoBehaviour
{
    public SoundBus Bus = SoundBus.Music;
}
