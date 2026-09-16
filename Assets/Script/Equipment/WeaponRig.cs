using UnityEngine;

/// <summary>무기 모델 프리팹에 붙는 부착점 정의. 참조는 프리팹 내부로 한정.</summary>
public class WeaponRig : MonoBehaviour
{
    [Header("공통")]
    [Tooltip("무기 끝. 트레일/이펙트 기준")]
    public Transform Tip;

    [Tooltip("양손 무기일 때 왼손이 붙을 지점")]
    public Transform LeftGrip;

    [Header("원거리")]
    [Tooltip("투사체가 나가는 위치")]
    public Transform Muzzle;
    public MuzzleFlash MuzzleFlash;

    [Header("활")]
    [Tooltip("당겨지는 시위 본")]
    public Transform StringBone;
}