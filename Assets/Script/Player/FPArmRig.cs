using UnityEngine;

/// <summary>1인칭 팔/무기 배치. 카메라 자식으로 둡니다.</summary>
public class FPArmRig : MonoBehaviour
{
    [Header("Editor")]
    [Tooltip("에디터에서 왼손을 항상 표시 (편집용)")]
    public bool ForceShowLeftHand = false;

    [Header("Bones")]
    public Transform ArmRoot;
    public Transform RightHand;
    public Transform LeftHand;

    public HandRig RightHandRig;
    public HandRig LeftHandRig;

    [Header("Idle Pose")]
    public ArmPose IdlePose = new();

    [Header("Idle Fingers")]
    [Tooltip("무기가 없거나 그립이 지정되지 않았을 때의 손 모양")]
    public HandPose IdleRightFingers;
    public HandPose IdleLeftFingers;



    [Header("Hand Models")]
    [Tooltip("손 모델 프리팹 (재생성용)")]
    public GameObject RightHandPrefab;
    public GameObject LeftHandPrefab;

    [Header("Left Hand IK")]
    [Tooltip("양손 무기일 때 왼손이 붙을 지점. 무기 프리팹의 자식")]
    public string LeftGripNodeName = "EndGrip";




    [Header("Camera")]
    [Tooltip("모션 카메라 오프셋을 적용할 대상")]
    public FPSCamera Camera;

    [Header("Float")]
    public bool EnableFloat = true;
    public float FloatAmount = 0.012f;
    public float FloatSpeed = 1.4f;
    public float RotDrift = 2.5f;

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

    /// <summary>현재 무기를 반영한 기본 자세</summary>
    public ArmPose GetEffectiveIdle()
    {
        var p = IdlePose.Clone();

        if (_weapon == null)
        {
            // 무기 없음 → 양손 다 표시
            p.UseLeftHand = true;
            p.UseWeaponGrip = false;
            p.RightFingers = null;   // rest 유지
            p.LeftFingers = null;
            return p;
        }

        if (_weapon.OverrideIdlePose)
        {
            p.RightPos = _weapon.RightHandPos;
            p.RightRot = _weapon.RightHandRot;
            p.LeftPos = _weapon.LeftHandPos;
            p.LeftRot = _weapon.LeftHandRot;
        }

        // 왼손 표시 여부
        p.UseLeftHand = _weapon.Grip switch
        {
            GripType.TwoHand => true,
            GripType.LeftHand => true,
            _ => _weapon.UseLeftHand,   // 오른손 무기는 설정 따름
        };

        return p;
    }

    public void Mount(EquipmentData weapon)
    {
        DestroyWeapon();
        _leftGripNode = null;
        WeaponTip = null;
        _weapon = weapon;

        if (weapon == null || weapon.ModelPrefab == null)
        {
            // 무기 없음 → 손가락 초기화
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

    public void ApplyFloat(float time)
    {
        if (!EnableFloat) return;

        ApplyFloatTo(RightHand, time, 0f);
        if (LeftHand != null && LeftHand.gameObject.activeSelf)
            ApplyFloatTo(LeftHand, time, 1.7f);
    }

    void ApplyFloatTo(Transform hand, float t, float phase)
    {
        if (hand == null) return;

        float y = Mathf.Sin(t * FloatSpeed + phase) * FloatAmount;
        float x = Mathf.Cos(t * FloatSpeed * 0.7f + phase) * FloatAmount * 0.6f;

        hand.localPosition += new Vector3(x, y, 0f);

        float rz = Mathf.Sin(t * FloatSpeed * 0.5f + phase) * RotDrift;
        float rx = Mathf.Cos(t * FloatSpeed * 0.6f + phase) * RotDrift * 0.5f;

        hand.localRotation *= Quaternion.Euler(rx, 0f, rz);
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

    void ApplyFingers(ArmPose pose)
    {
        HandPose right = null;
        HandPose left = null;

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

        // null이면 손가락을 건드리지 않음 (현재 상태 유지)
        if (RightHandRig != null && right != null) RightHandRig.Apply(right);

        if (LeftHandRig != null && pose.UseLeftHand && left != null)
            LeftHandRig.Apply(left);
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


    // ── 포즈 적용 ──

    public void ApplyPose(ArmPose pose)
    {
        if (pose == null) return;

        if (RightHand != null)
        {
            RightHand.localPosition = pose.RightPos;
            RightHand.localRotation = Quaternion.Euler(pose.RightRot);
        }

        bool showLeft = pose.UseLeftHand;

#if UNITY_EDITOR
        if (!Application.isPlaying && ForceShowLeftHand) showLeft = true;
#endif

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
            _weaponObj.transform.localPosition = _weapon.GetPosOffset(true) + pose.WeaponPos;
            _weaponObj.transform.localRotation =
                Quaternion.Euler(_weapon.GetRotOffset(true) + pose.WeaponRot);
            _weaponObj.transform.localScale = _weapon.ScaleOverride;
        }

        ApplyFingers(pose);
        ApplyCameraPose(pose);
    }

    /// <summary>스윙 진행도 적용</summary>
    public void ApplySwing(SwingMotion motion, float t01)
    {
        var idle = GetEffectiveIdle();
        if (motion == null || !motion.IsValid) { ApplyPose(idle); return; }
        ApplyPose(motion.Evaluate(t01, idle));
    }

    /// <summary>스웨이/바빙은 ArmRoot에 적용 (포즈와 분리)</summary>
    public void ApplyOffset(Vector3 pos, Vector3 rot)
    {
        if (ArmRoot == null) return;
        ArmRoot.localPosition = pos;
        ArmRoot.localRotation = Quaternion.Euler(rot);
    }

    /// <summary>무기만 정리 (손 보존)</summary>
    public void CleanWeaponOnly()
    {
        DestroyWeapon();
        _leftGripNode = null;
        WeaponTip = null;
    }

    /// <summary>손까지 전부 정리</summary>
    public void CleanAll()
    {
        CleanChildren(RightHand);
        CleanChildren(LeftHand);
        _weaponObj = null;
        _leftGripNode = null;
        WeaponTip = null;
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

    /// <summary>손 모델을 프리팹에서 다시 생성</summary>
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
        go.transform.localScale = Vector3.one;

        var rig = go.GetComponent<HandRig>();
        if (rig == null) rig = go.AddComponent<HandRig>();
        rig.Rebuild();

        return rig;
    }
}