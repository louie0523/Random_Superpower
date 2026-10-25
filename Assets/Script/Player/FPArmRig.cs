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

    /// <summary>탈것 높이 (m) — 몸·머리를 이만큼 올림. PlayerAlter가 설정</summary>
    public float BodyLift { get; set; }

    private Transform _stringBone;
    private Vector3 _stringRestPos;
    private Transform _muzzle;
    private float _drawAmount;
    private float _drawTarget;
    private float _handBlend;

    private float _recoilAmount;      // 현재 반동 자세 강도 0~N
    private float _recoilTarget;      // 목표 (발사 시 튀어오름)
    private float _recoilSeed;

    private bool _weaponHidden;
    public bool WeaponHidden => _weaponHidden;

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

    // ══════════════════ 파츠 (모델 여러 개) · 둥둥 ══════════════════
    //  0번 = 메인 모델 (EquipmentData.ModelPrefab — 판정 · 총구 · 끝점 · 왼손 그립 기준)
    //  1번~ = EquipmentData.ExtraParts. 파츠마다 부모(오른손 · 왼손 · 팔)를 따로, 모션이 잠깐 바꿀 수 있음

    class PartInst
    {
        public GameObject Obj;
        public float Phase;
        public Vector3 FloatPos, FloatRot;      // 마지막으로 더한 둥둥
    }
    private readonly System.Collections.Generic.List<PartInst> _parts = new();
    private WeaponParent[] _parentOverride;

    /// <summary>파츠 배치(부모 · 개수)가 바뀔 때마다 +1 — 그림자 복제 등이 다시 만들 때 봄</summary>
    public int PartLayoutVersion { get; private set; }

    /// <summary>파츠 수 (0번 메인 포함). 무기가 없으면 0</summary>
    public int PartCount => _parts.Count;

    /// <summary>파츠 i의 모델 (0 = 메인 = GetWeaponTransform)</summary>
    public Transform GetPartTransform(int i) =>
        i >= 0 && i < _parts.Count && _parts[i].Obj != null ? _parts[i].Obj.transform : null;

    /// <summary>마지막으로 메인 무기에 더한 둥둥 흔들림 — 편집 창이 '씬 포즈 저장' 때 빼려고 봄</summary>
    public Vector3 LastWeaponFloatPos => GetPartLastFloat(0, out var p, out _) ? p : Vector3.zero;
    public Vector3 LastWeaponFloatRot => GetPartLastFloat(0, out _, out var r) ? r : Vector3.zero;

    public bool GetPartLastFloat(int i, out Vector3 pos, out Vector3 rot)
    {
        pos = rot = Vector3.zero;
        if (i < 0 || i >= _parts.Count) return false;
        pos = _parts[i].FloatPos;
        rot = _parts[i].FloatRot;
        return true;
    }

    /// <summary>파츠 i의 기본 위치 · 회전 · 크기 (무기 설정 — 키 값은 이 위에 더해짐)</summary>
    public bool GetPartBase(int i, out Vector3 pos, out Vector3 rot, out Vector3 scale)
    {
        pos = rot = Vector3.zero;
        scale = Vector3.one;
        if (_weapon == null) return false;
        if (i <= 0)
        {
            pos = _weapon.GetPosOffset(true);
            rot = _weapon.GetRotOffset(true);
            scale = _weapon.ScaleOverride;
            return true;
        }
        var d = PartDef(i);
        if (d == null) return false;
        pos = d.Position;
        rot = d.Rotation;
        scale = d.Scale;
        return true;
    }

    WeaponPart PartDef(int i) =>
        _weapon != null && _weapon.ExtraParts != null && i >= 1 && i - 1 < _weapon.ExtraParts.Length ? _weapon.ExtraParts[i - 1] : null;

    /// <summary>무기 설정의 부모 (모션이 안 바꿨을 때)</summary>
    public WeaponParent DefaultParent(int i)
    {
        if (_weapon == null) return WeaponParent.RightHand;
        if (i <= 0)
        {
            if (_weapon.MainParent != WeaponParent.Default) return _weapon.MainParent;
            if (_weapon.FloatFree) return WeaponParent.Arm;
            return _weapon.Grip == GripType.LeftHand ? WeaponParent.LeftHand : WeaponParent.RightHand;
        }
        var d = PartDef(i);
        return d == null || d.Parent == WeaponParent.Default ? WeaponParent.RightHand : d.Parent;
    }

    /// <summary>지금 파츠 i가 붙는 곳 (모션이 바꿨으면 그것)</summary>
    public WeaponParent CurrentParent(int i)
    {
        if (_parentOverride != null && i >= 0 && i < _parentOverride.Length && _parentOverride[i] != WeaponParent.Default)
            return _parentOverride[i];
        return DefaultParent(i);
    }

    public Transform ParentTransform(WeaponParent p)
    {
        switch (p)
        {
            case WeaponParent.LeftHand: return LeftHand != null ? LeftHand : RightHand;
            case WeaponParent.Arm:
                if (RightHand != null && RightHand.parent != null) return RightHand.parent;
                if (LeftHand != null && LeftHand.parent != null) return LeftHand.parent;
                return ArmRoot != null ? ArmRoot : transform;
            default: return RightHand != null ? RightHand : LeftHand;
        }
    }

    /// <summary>
    /// 모션 동안 파츠 부모 바꾸기 — [i] = 파츠 i (Default = 무기 설정). null이면 전부 무기 설정대로.
    /// 바뀐 파츠만 옮김 (매 프레임 불러도 됨)
    /// </summary>
    public void SetPartParents(WeaponParent[] parents)
    {
        _parentOverride = parents;
        ApplyParents();
    }

    void ApplyParents()
    {
        bool changed = false;
        for (int i = 0; i < _parts.Count; i++)
        {
            var go = _parts[i].Obj;
            if (go == null) continue;
            var want = ParentTransform(CurrentParent(i));
            if (want == null || go.transform.parent == want) continue;
            go.transform.SetParent(want, false);
            changed = true;
        }
        if (changed) PartLayoutVersion++;
    }

    /// <summary>편집 모드에서 무기 설정의 둥둥을 움직여 볼지 — 편집 창이 켬</summary>
    public static bool EditorFloatPreview;

    static float FloatClock()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
        return Time.time;
    }

    /// <summary>무기 설정의 '항상 둥둥' (파츠 i)</summary>
    void PartIdleFloat(int i, out Vector3 pos, out Vector3 rot)
    {
        pos = rot = Vector3.zero;
        if (_weapon == null || i < 0 || i >= _parts.Count) return;
        if (!Application.isPlaying && !EditorFloatPreview) return;

        bool use; FloatWave wave;
        if (i == 0) { use = _weapon.UseFloat; wave = _weapon.Float; }
        else { var d = PartDef(i); use = d != null && d.UseFloat; wave = d?.Float; }
        if (!use || wave == null) return;
        wave.Sample(FloatClock() + _parts[i].Phase, out pos, out rot);
    }

    public EquipmentData CurrentWeapon => _weapon;

    public Transform GetWeaponTransform() =>
        _weaponObj != null ? _weaponObj.transform : null;

    public Vector3 StringRestPos => _stringRestPos;



    // ══════════════════ 무기 장착 ══════════════════

    // ══════════════════ 모션 동안 다른 무기 (모델만) ══════════════════

    private bool _overriding;
    private EquipmentData _overrideRestore;

    /// <summary>모션 전용 무기를 쥐고 있는 중 (실제 장착 무기는 그대로)</summary>
    public bool IsWeaponOverridden => _overriding;

    /// <summary>
    /// 모델만 잠깐 바꿔 쥠 — 판정·스탯은 실제 무기 그대로. EndWeaponOverride로 원래 무기 복귀.
    /// 보이기 여부(사망 중 숨김)와 레이어는 바꾸기 전 무기를 따라감
    /// </summary>
    public void BeginWeaponOverride(EquipmentData temp)
    {
        if (temp == null) return;
        if (_overriding && temp == _weapon) return;

        if (!_overriding)
        {
            _overriding = true;
            _overrideRestore = _weapon;
        }
        SwapKeepingLook(temp);
    }

    public void EndWeaponOverride()
    {
        if (!_overriding) return;
        _overriding = false;
        var back = _overrideRestore;
        _overrideRestore = null;
        SwapKeepingLook(back);
    }

    /// <summary>모션 동안 무기 모델 숨기기 (맨손 스킬). 무기를 바꿔 쥐어도 유지</summary>
    public void SetWeaponHidden(bool hide)
    {
        _weaponHidden = hide;
        foreach (var part in _parts)
            if (part.Obj != null && part.Obj.activeSelf == hide) part.Obj.SetActive(!hide);
    }

    void SwapKeepingLook(EquipmentData w)
    {
        int layer = _weaponObj != null ? _weaponObj.layer : -1;
        bool visible = true;
        if (_weaponObj != null)
        {
            var rs = _weaponObj.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                visible = false;
                foreach (var r in rs) if (r.enabled) { visible = true; break; }
            }
        }

        MountInternal(w);

        foreach (var part in _parts)
        {
            if (part.Obj == null) continue;
            foreach (var t in part.Obj.GetComponentsInChildren<Transform>(true))
                if (layer >= 0) t.gameObject.layer = layer;
            if (!visible)
                foreach (var r in part.Obj.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }
    }

    // ══════════════════ 무기 장착 ══════════════════

    /// <summary>실제 장착. 모션 무기를 쥐고 있는 중이면 끝난 뒤 이 무기로 돌아감</summary>
    public void Mount(EquipmentData weapon)
    {
        if (_overriding && Application.isPlaying)
        {
            _overrideRestore = weapon;
            return;
        }
        _overriding = false;
        _overrideRestore = null;
        MountInternal(weapon);
    }

    void MountInternal(EquipmentData weapon)
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

        // 0번 (메인) — 붙는 곳: 무기 설정 (잡는 손 · 손을 따라가지 않음 · 파츠 부모) 또는 모션이 바꾼 곳
        Transform hand = ParentTransform(CurrentParentFor(weapon, 0));
        if (hand == null) return;

        _weaponObj = SpawnModel(weapon.ModelPrefab, hand);
        _parts.Add(new PartInst { Obj = _weaponObj, Phase = Application.isPlaying ? Random.Range(0f, 100f) : 0f });

        // 1번~ — 추가 모델 (장식: 콜라이더 끔)
        if (weapon.ExtraParts != null)
            for (int i = 0; i < weapon.ExtraParts.Length; i++)
            {
                var d = weapon.ExtraParts[i];
                GameObject go = null;
                if (d != null && d.ModelPrefab != null)
                {
                    var parent = ParentTransform(CurrentParentFor(weapon, i + 1));
                    if (parent != null)
                    {
                        go = SpawnModel(d.ModelPrefab, parent);
                        go.name = $"{d.ModelPrefab.name} (파츠 {i + 1})";
                        if (Application.isPlaying)
                            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                        if (_weaponHidden) go.SetActive(false);
                    }
                }
                // 모델이 없어도 자리는 남김 — 번호가 밀리지 않게
                _parts.Add(new PartInst { Obj = go, Phase = Application.isPlaying ? Random.Range(0f, 100f) : 0f });
            }
        PartLayoutVersion++;

        _weaponObj.transform.localPosition = weapon.GetPosOffset(true);
        _weaponObj.transform.localRotation = Quaternion.Euler(weapon.GetRotOffset(true));
        _weaponObj.transform.localScale = weapon.ScaleOverride;

        if (_weaponHidden) _weaponObj.SetActive(false);

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

    GameObject SpawnModel(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
        return Instantiate(prefab, parent);
    }

    /// <summary>Mount 중 — _weapon이 바뀌는 중이라 무기를 직접 받아서</summary>
    WeaponParent CurrentParentFor(EquipmentData w, int i)
    {
        if (_parentOverride != null && i >= 0 && i < _parentOverride.Length && _parentOverride[i] != WeaponParent.Default)
            return _parentOverride[i];
        var keep = _weapon;
        _weapon = w;
        var p = DefaultParent(i);
        _weapon = keep;
        return p;
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
        foreach (var part in _parts)
        {
            if (part.Obj == null || part.Obj == _weaponObj) continue;
            if (Application.isPlaying) Destroy(part.Obj);
            else DestroyImmediate(part.Obj);
        }
        if (_parts.Count > 0) PartLayoutVersion++;
        _parts.Clear();

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
        DestroyWeapon();
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

        ApplyParts(pose);

        ApplyFingers(pose, showLeft);
        ApplyCameraPose(pose);
        ApplyBodyPose(pose);
    }

    /// <summary>
    /// 파츠마다: 기본(무기 설정) + 키 값 + 둥둥(무기 설정 · 모션 구간). 부모 기준 로컬 값.
    /// 회전은 오일러를 더함 (편집 창의 '씬 포즈 저장'도 같은 식으로 뺌)
    /// </summary>
    void ApplyParts(ArmPose pose)
    {
        if (_weapon == null) return;
        for (int i = 0; i < _parts.Count; i++)
        {
            var part = _parts[i];
            if (part.Obj == null) continue;
            if (!GetPartBase(i, out var bp, out var br, out var bs)) continue;

            PartIdleFloat(i, out var fp, out var fr);
            fp += pose.PartFloatPos(i);
            fr += pose.PartFloatRot(i);
            part.FloatPos = fp;
            part.FloatRot = fr;

            var t = part.Obj.transform;
            t.localPosition = bp + pose.PartPos(i) + fp;
            t.localRotation = Quaternion.Euler(br + pose.PartRot(i) + fr);
            t.localScale = Vector3.Scale(bs, pose.PartScale(i));
        }
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

        Vector3 lift = Vector3.up * BodyLift;

        if (Body != null)
            Body.SetLocalPositionAndRotation(
                _bodyRestPos + lift + pose.BodyPos, _bodyRestRot * Quaternion.Euler(pose.BodyRot));

        if (Head != null)
        {
            // 머리가 몸의 자식이면 몸과 함께 올라가므로 따로 올리지 않음
            Vector3 headLift = Body != null && Head.IsChildOf(Body) ? Vector3.zero : lift;
            Head.SetLocalPositionAndRotation(
                _headRestPos + headLift + pose.HeadPos, _headRestRot * Quaternion.Euler(pose.HeadRot));
        }
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

        // Auto — 모션 키의 '왼손 표시'가 켜져 있으면 무기 설정과 상관없이 보임 (모션 우선)
        //        꺼져 있으면 무기 설정대로 (양손 · 왼손 무기 · 무기의 왼손 표시)
        if (pose.UseLeftHand) return true;
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