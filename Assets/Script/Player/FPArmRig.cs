using UnityEngine;

/// <summary>1인칭 팔/무기 배치. 카메라 자식으로 둡니다.</summary>
public class FPArmRig : MonoBehaviour
{
    [Header("Bones")]
    public Transform ArmRoot;
    public Transform RightHand;
    public Transform LeftHand;

    [Header("Hands")]
    public HandRig RightHandRig;
    public HandRig LeftHandRig;
    public GameObject RightHandPrefab;
    public GameObject LeftHandPrefab;

    [Header("Idle Pose")]
    public ArmPose IdlePose = new();

    [Header("Camera")]
    public Transform CamPivot;
    public FPSCamera Camera;

    [Header("Left Hand IK")]
    [Tooltip("양손 무기일 때 왼손이 붙을 지점. 무기 프리팹의 자식")]
    public string LeftGripNodeName = "EndGrip";

    [Header("Editor")]
    [Tooltip("에디터에서 왼손을 항상 표시 (편집용)")]
    public bool ForceShowLeftHand = false;

    [Header("Damped Targets")]
    [Tooltip("Animation Rigging용. 비우면 손을 직접 제어")]
    public Transform RightHandTarget;
    public Transform LeftHandTarget;

    Transform RightPoseTarget => RightHandTarget != null ? RightHandTarget : RightHand;
    Transform LeftPoseTarget => LeftHandTarget != null ? LeftHandTarget : LeftHand;

    private GameObject _weaponObj;
    private Transform _leftGripNode;
    private EquipmentData _weapon;

    public Transform WeaponTip { get; private set; }
    public EquipmentData CurrentWeapon => _weapon;

    public Transform GetWeaponTransform() =>
        _weaponObj != null ? _weaponObj.transform : null;

    // ══════════════════ 무기 장착 ══════════════════

    public void Mount(EquipmentData weapon)
    {
        DestroyWeapon();
        _leftGripNode = null;
        WeaponTip = null;
        _weapon = weapon;

        if (weapon == null || weapon.ModelPrefab == null)
        {
            RightHandRig?.ResetToRest();
            LeftHandRig?.ResetToRest();
            ApplyPose(GetEffectiveIdle());
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

        ApplyPose(GetEffectiveIdle());
    }

    void DestroyWeapon()
    {
        if (_weaponObj == null) return;

        if (Application.isPlaying) Destroy(_weaponObj);
        else DestroyImmediate(_weaponObj);

        _weaponObj = null;
    }

    public void CleanWeaponOnly()
    {
        DestroyWeapon();
        _leftGripNode = null;
        WeaponTip = null;
        _weapon = null;
    }

    public void CleanAll()
    {
        CleanChildren(RightHand);
        CleanChildren(LeftHand);

        _weaponObj = null;
        _leftGripNode = null;
        WeaponTip = null;
        _weapon = null;
        RightHandRig = null;
        LeftHandRig = null;
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

    public void RebuildHands()
    {
        CleanAll();

        RightHandRig = SpawnHand(RightHandPrefab, RightHand, "R");
        LeftHandRig = SpawnHand(LeftHandPrefab, LeftHand, "L");

        ApplyPose(IdlePose);
    }

    HandRig SpawnHand(GameObject prefab, Transform parent, string label)
    {
        if (prefab == null || parent == null) return null;

        GameObject go;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
        else
#endif
            go = Instantiate(prefab, parent);

        go.name = $"Hand_{label}";
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var rig = go.GetComponent<HandRig>();
        if (rig == null) rig = go.AddComponent<HandRig>();
        rig.Build();

        return rig;
    }

    // ══════════════════ 포즈 ══════════════════

    /// <summary>무기 설정을 반영한 기본 자세</summary>
    public ArmPose GetEffectiveIdle()
    {
        var p = IdlePose.Clone();

        if (_weapon == null)
        {
            p.UseLeftHand = true;
            p.UseWeaponGrip = false;
            p.RightFingers = null;
            p.LeftFingers = null;
            return p;
        }

        if (_weapon.OverrideIdlePose)
        {
            p.RightPos = _weapon.RightHandPos;
            p.RightRot = _weapon.RightHandRot;
            p.LeftPos = _weapon.LeftHandPos;
            p.LeftRot = _weapon.LeftHandRot;
            p.UseLeftHand = _weapon.UseLeftHand;   // ★ 추가
        }
        else
        {
            p.UseLeftHand = _weapon.UseLeftHand;   // ★ 추가
        }

        p.UseWeaponGrip = true;
        return p;
    }

    public void ApplyPose(ArmPose pose)
    {
        if (pose == null) return;

        bool showRight = ResolveShowRightHand(pose);
        bool showLeft = ResolveShowLeftHand(pose);

        if (RightHand != null)
        {
            RightHand.gameObject.SetActive(showRight);
            if (showRight)
            {
                RightHand.localPosition = pose.RightPos;
                RightHand.localRotation = Quaternion.Euler(pose.RightRot);
            }
        }


        if (LeftHand != null)
        {
            LeftHand.gameObject.SetActive(showLeft);

            if (showLeft)
            {
                LeftHand.localPosition = pose.LeftPos;
                LeftHand.localRotation = Quaternion.Euler(pose.LeftRot);
            }
        }

        if (_weaponObj != null && _weapon != null)
        {
            _weaponObj.transform.localPosition =
                _weapon.GetPosOffset(true) + pose.WeaponPos;
            _weaponObj.transform.localRotation =
                Quaternion.Euler(_weapon.GetRotOffset(true) + pose.WeaponRot);
            _weaponObj.transform.localScale = _weapon.ScaleOverride;
        }

        ApplyFingers(pose, showLeft);
        ApplyCameraPose(pose);
    }

    bool ResolveShowLeftHand(ArmPose pose)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && ForceShowLeftHand) return true;
#endif
        // 모션 오버라이드 우선
        switch (pose.HandOverride)
        {
            case HandVisibility.RightOnly: return false;
            case HandVisibility.LeftOnly: return true;
            case HandVisibility.Both: return true;
        }

        // Auto — 무기 설정만 본다
        if (_weapon == null) return true;

        return _weapon.Grip == GripType.TwoHand
            || _weapon.Grip == GripType.LeftHand
            || _weapon.UseLeftHand;
    }

    bool ResolveShowRightHand(ArmPose pose)
    {
        return pose.HandOverride != HandVisibility.LeftOnly;
    }

    void ApplyFingers(ArmPose pose, bool showLeft)
    {
        HandPose right, left;

        if (pose.UseWeaponGrip && _weapon != null)
        {
            right = _weapon.RightGrip;
            left = _weapon.LeftGrip;
        }
        else
        {
            right = pose.RightFingers;
            left = pose.LeftFingers;
        }

        if (RightHandRig != null && right != null) RightHandRig.Apply(right);
        if (LeftHandRig != null && showLeft && left != null) LeftHandRig.Apply(left);
    }

    void ApplyCameraPose(ArmPose pose)
    {
        if (CamPivot != null)
        {
            CamPivot.localPosition = pose.CamPos;
            CamPivot.localRotation = Quaternion.Euler(pose.CamRot);
        }

        if (Application.isPlaying && Camera != null)
            Camera.SetMotionFov(pose.CamFov);
    }

    /// <summary>스윙 진행도 적용</summary>
    public void ApplySwing(SwingMotion motion, float t01, HandVisibility handMode = HandVisibility.Auto)
    {
        var idle = GetEffectiveIdle();

        if (motion == null || !motion.IsValid) { ApplyPose(idle); return; }

        var pose = motion.Evaluate(t01, idle);
        pose.HandOverride = handMode;
        ApplyPose(pose);
    }

    /// <summary>스웨이/바빙은 ArmRoot에 적용</summary>
    public void ApplyOffset(Vector3 pos, Vector3 rot)
    {
        if (ArmRoot == null) return;
        ArmRoot.localPosition = pos;
        ArmRoot.localRotation = Quaternion.Euler(rot);
    }
}