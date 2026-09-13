using UnityEngine;

/// <summary>무기 배치 툴이 사용하는 프리뷰 리그. 씬에만 존재.</summary>
[ExecuteAlways]
public class WeaponPreviewRig : MonoBehaviour
{
    [Header("Sockets")]
    public Transform RightHand;
    public Transform LeftHand;

    [Header("View")]
    public Transform CameraAnchor;   // 1인칭 눈 위치

    [Header("Runtime")]
    [HideInInspector] public GameObject Spawned;

    public Transform GetSocket(GripType grip) =>
        grip == GripType.LeftHand ? LeftHand : RightHand;

    public void Clear()
    {
        if (Spawned != null)
        {
            if (Application.isPlaying) Destroy(Spawned);
            else DestroyImmediate(Spawned);
            Spawned = null;
        }
    }
}