using UnityEngine;

/// <summary>1인칭 팔/무기 배치. 카메라 자식으로 둡니다.</summary>
public class FPArmRig : MonoBehaviour
{
    [Header("Bones")]
    public Transform ArmRoot;
    public Transform RightHand;
    public Transform LeftHand;

    [Header("Idle Pose")]
    public ArmPose IdlePose = new();

    [Header("Left Hand IK")]
    [Tooltip("양손 무기일 때 왼손이 붙을 지점. 무기 프리팹의 자식")]
    public string LeftGripNodeName = "EndGrip";

    [Header("Preview Markers")]
    [Tooltip("이 이름의 자식은 무기 정리 시 삭제하지 않음")]
    public string[] KeepChildNames = { "RightHand", "LeftHand" };

    [Header("Camera")]
    [Tooltip("모션 카메라 오프셋을 적용할 대상")]
    public FPSCamera Camera;

    [Header("Preview")]
    [Tooltip("에디터 프리뷰에서 카메라 오프셋을 적용할 Transform")]
    public Transform PreviewCameraPivot;

    private GameObject _weaponObj;
    private Transform _leftGripNode;
    private EquipmentData _weapon;

    public Transform WeaponTip { get; private set; }

    public Transform GetWeaponTransform() =>
        _weaponObj != null ? _weaponObj.transform : null;

    // ── 무기 장착 ──

    public void Mount(EquipmentData weapon)
    {
        DestroyWeapon();
        _leftGripNode = null;
        WeaponTip = null;
        _weapon = weapon;

        if (weapon == null || weapon.ModelPrefab == null)
        {
            ApplyPose(IdlePose);
            return;
        }

        Transform hand = weapon.Grip == GripType.LeftHand ? LeftHand : RightHand;
        if (hand == null) return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            _weaponObj = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(
                weapon.ModelPrefab, hand);
        else
#endif
            _weaponObj = Instantiate(weapon.ModelPrefab, hand);

        _weaponObj.transform.localPosition = weapon.GetPosOffset(true);
        _weaponObj.transform.localRotation = Quaternion.Euler(weapon.GetRotOffset(true));
        _weaponObj.transform.localScale = weapon.ScaleOverride;

        _leftGripNode = _weaponObj.transform.Find(LeftGripNodeName);
        var tip = _weaponObj.transform.Find("Tip");
        WeaponTip = tip != null ? tip : _weaponObj.transform;

        ApplyPose(IdlePose);
    }

    void ApplyCameraPose(ArmPose pose)
    {
        // 런타임: FPSCamera에 위임
        if (Application.isPlaying && Camera != null)
        {
            Camera.SetMotionOffset(pose.CamPos, pose.CamRot, pose.CamFov);
            return;
        }

        // 에디터: 피벗을 직접 이동
        if (PreviewCameraPivot != null)
        {
            PreviewCameraPivot.localPosition = pose.CamPos;
            PreviewCameraPivot.localRotation = Quaternion.Euler(pose.CamRot);
        }
    }
    void DestroyWeapon()
    {
        if (_weaponObj == null) return;

        if (Application.isPlaying) Destroy(_weaponObj);
        else DestroyImmediate(_weaponObj);

        _weaponObj = null;
    }

    /// <summary>손에 남은 잔여 프리뷰 정리 (에디터용)</summary>
    public void CleanHands()
    {
        CleanChildren(RightHand);
        CleanChildren(LeftHand);
        _weaponObj = null;
        _leftGripNode = null;
        WeaponTip = null;
    }

    void CleanChildren(Transform hand)
    {
        if (hand == null) return;

        for (int i = hand.childCount - 1; i >= 0; i--)
        {
            var go = hand.GetChild(i).gameObject;

            // 보존 목록에 있으면 건너뜀
            bool keep = false;
            foreach (var n in KeepChildNames)
                if (go.name == n) { keep = true; break; }
            if (keep) continue;

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    // ── 포즈 적용 ──

    public void ApplyPose(ArmPose pose)
    {
        if (pose == null) return;

        if (RightHand != null)
        {
            RightHand.localPosition = pose.RightPos;
            RightHand.localRotation = Quaternion.Euler(pose.RightRot);
        }

        if (LeftHand != null)
        {
            LeftHand.gameObject.SetActive(pose.UseLeftHand);

            if (pose.UseLeftHand)
            {
                // 명시적으로 켰을 때만 무기에 붙임
                if (pose.LeftFollowsWeapon && _leftGripNode != null)
                {
                    LeftHand.position = _leftGripNode.position;
                    LeftHand.rotation = _leftGripNode.rotation;
                }
                else
                {
                    LeftHand.localPosition = pose.LeftPos;
                    LeftHand.localRotation = Quaternion.Euler(pose.LeftRot);
                }
            }
        }

        if (_weaponObj != null && _weapon != null)
        {
            _weaponObj.transform.localPosition =
                _weapon.GetPosOffset(true) + pose.WeaponPos;
            _weaponObj.transform.localRotation =
                Quaternion.Euler(_weapon.GetRotOffset(true) + pose.WeaponRot);
        }

        if (Camera != null)
            Camera.SetMotionOffset(pose.CamPos, pose.CamRot, pose.CamFov);

        ApplyCameraPose(pose);
    }

    /// <summary>스윙 진행도 적용</summary>
    public void ApplySwing(SwingMotion motion, float t01)
    {
        if (motion == null || !motion.IsValid) { ApplyPose(IdlePose); return; }
        ApplyPose(motion.Evaluate(t01, IdlePose));
    }

    /// <summary>스웨이/바빙은 ArmRoot에 적용 (포즈와 분리)</summary>
    public void ApplyOffset(Vector3 pos, Vector3 rot)
    {
        if (ArmRoot == null) return;
        ArmRoot.localPosition = pos;
        ArmRoot.localRotation = Quaternion.Euler(rot);
    }
}