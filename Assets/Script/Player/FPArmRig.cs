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
                // 무기에 그립 노드가 있으면 거기 붙임 (IK 대용)
                if (_leftGripNode != null)
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