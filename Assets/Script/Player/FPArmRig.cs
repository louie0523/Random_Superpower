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

    [Header("전신 모션 (선택)")]
    [Tooltip("모션의 Body 채널이 움직일 몸통 (Player/Body). 다른 사람 화면에 보임. 비우면 무시")]
    public Transform Body;
    [Tooltip("모션의 Head 채널이 움직일 머리. 비우면 무시")]
    public Transform Head;

    // 기준 자세 — 처음 쓸 때 잡음. 모션 오프셋은 이 위에 더해짐
    private Vector3 _bodyRestPos, _headRestPos;
    private Quaternion _bodyRestRot = Quaternion.identity, _headRestRot = Quaternion.identity;
    private Transform _bodyRestOf, _headRestOf;

    /// <summary>들고 있는 무기 모델의 WeaponRig (투사체 미리보기 등)</summary>
    public WeaponRig CurrentWeaponRig { get; private set; }

    private Transform _stringBone;
    private Vector3 _stringRestPos;
    private Transform _muzzle;
    private float _drawAmount;
    private float _drawTarget;
    private float _handBlend;

    private float _recoilAmount;      // 현재 반동 자세 강도 0~N
    private float _recoilTarget;      // 목표 (발사 시 튀어오름)
    private float _recoilSeed;

    public float RecoilAmount => _recoilAmount;
    public float DrawAmount => _drawAmount;
    public bool HasBowString => _stringBone != null;
    public Transform StringBone => _stringBone;
    public Transform Muzzle => _muzzle != null ? _muzzle : WeaponTip;


    private GameObject _weaponObj;
    private Transform _leftGripNode;
    private EquipmentData _weapon;
    private ChargeVisual _chargeVisual;
    private MuzzleFlash _muzzleFlash;
    public MuzzleFlash MuzzleFlash => _muzzleFlash;

    public Transform WeaponTip { get; private set; }
    public EquipmentData CurrentWeapon => _weapon;

    public Transform GetWeaponTransform() =>
        _weaponObj != null ? _weaponObj.transform : null;

    public Vector3 StringRestPos => _stringRestPos;



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

        _weaponObj.transform.localScale = weapon.ScaleOverride;

        var rig = _weaponObj.GetComponentInChildren<WeaponRig>(true);
        CurrentWeaponRig = rig;

        _chargeVisual = _weaponObj.GetComponentInChildren<ChargeVisual>(true);

        _muzzleFlash = rig != null ? rig.MuzzleFlash : null;

        if (rig == null)
            Debug.LogWarning(
                $"[FPArmRig] {weapon.DisplayName}의 모델에 WeaponRig가 없습니다.", this);

        _leftGripNode = rig != null ? rig.LeftGrip : null;
        _muzzle = rig != null ? rig.Muzzle : null;
        WeaponTip = rig != null && rig.Tip != null ? rig.Tip : _weaponObj.transform;

        _stringBone = rig != null ? rig.StringBone : null;
        _drawAmount = 0f;
        _handBlend = 0f;
        if (_stringBone != null) _stringRestPos = _stringBone.localPosition;

        ApplyPose(GetEffectiveIdle());
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
    /// <summary>시위 당김 정도 0~1. 매 프레임 호출</summary>
    public void SetDrawAmount(float t01, float dt = 0f)
    {
        TickRecoil(dt);

        _drawTarget = Mathf.Clamp01(t01);


        if (dt > 0f)
        {
            // 올라갈 땐 즉시, 내려올 땐 부드럽게
            if (_drawTarget >= _drawAmount)
                _drawAmount = _drawTarget;
            else
                _drawAmount = Mathf.MoveTowards(_drawAmount, _drawTarget, dt * 4f);
        }
        else
        {
            _drawAmount = _drawTarget;   // 에디터 미리보기는 즉시
        }



        if (_weapon != null && dt > 0f)
        {
            float speed = 1f / Mathf.Max(0.01f, _weapon.StringHandBlendTime);
            _handBlend = Mathf.MoveTowards(_handBlend, _drawAmount > 0f ? 1f : 0f, dt * speed);
        }
        else
        {
            _handBlend = _drawAmount > 0f ? 1f : 0f;
        }

        //Debug.Log($"[Charge] amount={_drawAmount:F2} visual={(_chargeVisual == null ? "NULL" : "OK")}");

        // ★ 차징 연출 — 시위 유무와 무관
        if (_chargeVisual != null) _chargeVisual.SetAmount(_drawAmount);

        // 투사체 미리보기 (활 화살)
        if (CurrentWeaponRig != null) CurrentWeaponRig.OnDraw(_drawAmount);

        if (_stringBone == null || _weapon == null) return;

        Vector3 dir = _weapon.StringPullDir.sqrMagnitude > 0.0001f
            ? _weapon.StringPullDir.normalized
            : Vector3.back;

        _stringBone.localPosition = _stringRestPos
            + dir * (_weapon.StringPullDistance * _drawAmount);
    }


    void DestroyWeapon()
    {
        if (_weaponObj == null) return;

        if (Application.isPlaying) Destroy(_weaponObj);
        else DestroyImmediate(_weaponObj);

        _weaponObj = null;
        CurrentWeaponRig = null;
        _stringBone = null;
        _muzzle = null;
        _drawAmount = 0f;
        _handBlend = 0f;
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
        CurrentWeaponRig = null;
        _leftGripNode = null;
        WeaponTip = null;
        _weapon = null;
        _stringBone = null;
        _muzzle = null;
        _drawAmount = 0f;
        _handBlend = 0f;
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
            p.UseLeftHand = _weapon.UseLeftHand;
        }
        else
        {
            p.UseLeftHand = _weapon.UseLeftHand;
        }

        p.UseWeaponGrip = true;

        if (_weapon.UseChargePose && _drawAmount > 0.001f && _weapon.ChargePose != null)
        {
            float t = _weapon.ChargeCurve != null
                ? _weapon.ChargeCurve.Evaluate(_drawAmount)
                : _drawAmount;

            p = ArmPose.Lerp(p, _weapon.ChargePose, t);
            p.UseWeaponGrip = true;
        }

        if (_weapon.UseRecoilPose && _recoilAmount > 0.001f && _weapon.RecoilPose != null)
        {
            float r = Mathf.Min(_recoilAmount, 1f);
            p = ArmPose.Lerp(p, _weapon.RecoilPose, r);
            p.UseWeaponGrip = true;

            // 1을 넘는 누적분은 위치만 더 밀어냄
            if (_recoilAmount > 1f)
            {
                Vector3 extra = (_weapon.RecoilPose.RightPos - p.RightPos) * (_recoilAmount - 1f);
                p.RightPos += extra;
                p.LeftPos += extra;
            }

            float phase = (_recoilSeed + Time.time) * _weapon.RecoilSwaySpeed;

            float swayX = Mathf.Sin(phase) * _weapon.RecoilSwayAmount * _recoilAmount;
            float swayY = Mathf.Sin(phase * 1.7f + 1.3f) * _weapon.RecoilSwayVertical * _recoilAmount;

            Vector3 sway = new Vector3(swayX, swayY, 0f);
            p.RightPos += sway;
            p.LeftPos += sway;

            p.RightRot.z += swayX * 60f;
            p.RightRot.x += swayY * 80f;
        }

        return p;
    }

    public void SetRecoilPreview(float amount)
    {
        _recoilAmount = amount;
        _recoilTarget = 0f;
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
                Vector3 pos = pose.LeftPos;
                Quaternion rot = Quaternion.Euler(pose.LeftRot);

                // ★ 시위 잡기 — 포즈 위치 ↔ 시위 본 위치 사이 보간
                if (_stringBone != null && _weapon != null && _handBlend > 0.001f)
                {
                    Vector3 stringLocal = LeftHand.parent.InverseTransformPoint(
                        _stringBone.TransformPoint(_weapon.StringHandOffset));

                    pos = Vector3.Lerp(pos, stringLocal, _handBlend);
                    rot = Quaternion.Slerp(rot, Quaternion.Euler(_weapon.StringHandRot), _handBlend);
                }

                LeftHand.localPosition = pos;
                LeftHand.localRotation = rot;
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
        ApplyBodyPose(pose);
    }

    // ══════════════════ 몸 · 머리 ══════════════════

    void EnsureRest()
    {
        if (Body != null && _bodyRestOf != Body)
        {
            _bodyRestOf = Body;
            _bodyRestPos = Body.localPosition;
            _bodyRestRot = Body.localRotation;
        }
        if (Head != null && _headRestOf != Head)
        {
            _headRestOf = Head;
            _headRestPos = Head.localPosition;
            _headRestRot = Head.localRotation;
        }
    }

    /// <summary>지금 몸·머리 위치를 기준 자세로 다시 잡음 (편집 창이 열릴 때 등)</summary>
    public void CaptureBodyRest()
    {
        _bodyRestOf = null;
        _headRestOf = null;
        EnsureRest();
    }

    /// <summary>몸·머리를 기준 자세로 되돌림 (편집 창을 닫을 때 씬에 흔적이 남지 않게)</summary>
    public void RestoreBodyRest()
    {
        if (Body != null && _bodyRestOf == Body)
            Body.SetLocalPositionAndRotation(_bodyRestPos, _bodyRestRot);
        if (Head != null && _headRestOf == Head)
            Head.SetLocalPositionAndRotation(_headRestPos, _headRestRot);
    }

    /// <summary>지금 몸·머리가 기준 자세에서 얼마나 움직였는지 (편집 창의 '씬 포즈 저장'용)</summary>
    public void GetBodyOffset(out Vector3 bodyPos, out Vector3 bodyRot, out Vector3 headPos, out Vector3 headRot)
    {
        EnsureRest();
        bodyPos = bodyRot = headPos = headRot = Vector3.zero;

        if (Body != null)
        {
            bodyPos = Body.localPosition - _bodyRestPos;
            bodyRot = (Quaternion.Inverse(_bodyRestRot) * Body.localRotation).eulerAngles;
        }
        if (Head != null)
        {
            headPos = Head.localPosition - _headRestPos;
            headRot = (Quaternion.Inverse(_headRestRot) * Head.localRotation).eulerAngles;
        }
    }

    void ApplyBodyPose(ArmPose pose)
    {
        if (Body == null && Head == null) return;
        EnsureRest();

        if (Body != null)
            Body.SetLocalPositionAndRotation(
                _bodyRestPos + pose.BodyPos, _bodyRestRot * Quaternion.Euler(pose.BodyRot));

        if (Head != null)
            Head.SetLocalPositionAndRotation(
                _headRestPos + pose.HeadPos, _headRestRot * Quaternion.Euler(pose.HeadRot));
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

        // ★ 차징 손가락 — 그립에서 차징 포즈로 부드럽게
        if (_weapon != null && _weapon.UseChargePose && _drawAmount > 0.001f)
        {
            var cp = _weapon.ChargePose;
            if (cp != null && !cp.UseWeaponGrip)
            {
                float t = _weapon.ChargeCurve != null
                    ? _weapon.ChargeCurve.Evaluate(_drawAmount)
                    : _drawAmount;

                if (cp.RightFingers != null)
                    right = t >= 0.999f ? cp.RightFingers
                                        : HandPose.Lerp(_weapon.RightGrip, cp.RightFingers, t);

                if (cp.LeftFingers != null)
                    left = t >= 0.999f ? cp.LeftFingers
                                       : HandPose.Lerp(_weapon.LeftGrip, cp.LeftFingers, t);
            }
        }

        // 시위 잡는 손 (활)
        if (_handBlend > 0.001f && _weapon != null && _weapon.StringHandPose != null)
            left = _handBlend >= 0.999f
                ? _weapon.StringHandPose
                : HandPose.Lerp(left, _weapon.StringHandPose, _handBlend);

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

    /// <summary>발사 시 호출. stack은 연사 누적 배율</summary>
    public void AddRecoil(float stack = 1f)
    {
        if (_weapon == null) return;
        _recoilTarget = Mathf.Min(stack, _weapon.RecoilPoseMaxStack);
        _recoilSeed = Random.Range(0f, 100f);
    }

    void TickRecoil(float dt)
    {
        if (_weapon == null || dt <= 0f) return;

        // 목표까지 빠르게 올라갔다가 천천히 내려옴
        if (_recoilAmount < _recoilTarget)
        {
            float rise = 1f / Mathf.Max(0.005f, _weapon.RecoilRiseTime);
            _recoilAmount = Mathf.MoveTowards(_recoilAmount, _recoilTarget, dt * rise);

            if (_recoilAmount >= _recoilTarget) _recoilTarget = 0f;   // 정점 찍으면 하강 시작
        }
        else
        {
            float fall = 1f / Mathf.Max(0.01f, _weapon.RecoilFallTime);
            _recoilAmount = Mathf.MoveTowards(_recoilAmount, 0f, dt * fall);
            _recoilTarget = 0f;
        }
    }
}