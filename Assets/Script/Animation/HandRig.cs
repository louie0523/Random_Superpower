using UnityEngine;
using System.Collections.Generic;

public class HandRig : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Armature 루트. 비우면 자식에서 자동 탐색")]
    public Transform Armature;

    [Tooltip("손가락 뿌리 본들 (엄지→약지 순). 자동 탐색됨")]
    public Transform[] FingerRoots = new Transform[HandPose.FingerCount];

    [Header("Curl 축")]
    [Tooltip("굽힘 1.0일 때 관절이 도는 각도")]
    public Vector3 CurlAxis = new(0f, 0f, -70f);

    [Header("손가락 끝")]
    [Tooltip("손가락 끝 위치를 직접 지정 — 마지막 마디 본 아래에 빈 오브젝트를 만들어 손톱 끝에 두고 여기에.\n" +
             "비우면 _end 본 → 그것도 없으면 자동 (마지막 마디 방향으로 마디 길이 × Virtual Tip Ratio)")]
    public Transform[] TipOverrides = new Transform[HandPose.FingerCount];
    [Tooltip("_end 본이 없을 때 자동 끝 길이 = 바로 앞 마디 길이 × 이 값")]
    [Range(0.3f, 1.5f)] public float VirtualTipRatio = 0.8f;

    [Header("IK")]
    [Range(1, 20)] public int IkIterations = 8;
    [Range(0f, 90f)] public float MaxJointAngle = 90f;

    // 각 손가락의 관절 체인 (뿌리 → 끝)
    [System.NonSerialized] public Transform[][] Chains;
    [System.NonSerialized] public Transform[] Tips;      // _end 본 · 지정한 끝 · (가상 끝이면) 마지막 마디
    private Quaternion[][] _rest;
    private bool _built;

    [System.NonSerialized] private Vector3[] _tipLocal;     // 가상 끝 — 마지막 마디 기준 위치
    [System.NonSerialized] private bool[] _virtualTip;
    [System.NonSerialized] private bool[] _overrideTip;

    public bool IsBuilt => _built && Chains != null;

    [SerializeField, HideInInspector]
    private Vector3[] _restEuler;

    [Header("Rest")]
    [Tooltip("기준 자세. 비어 있으면 첫 Build에서 현재 자세로 캡처")]
    public HandPose RestPose;


    void Awake() => Build();

    // ══════════════════ 구성 ══════════════════

    public void Build()
    {
        if (Armature == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.ToLower().Contains("armature")) { Armature = t; break; }
        }

        AutoDetectRoots();

        Chains = new Transform[HandPose.FingerCount][];
        Tips = new Transform[HandPose.FingerCount];
        _rest = new Quaternion[HandPose.FingerCount][];
        _tipLocal = new Vector3[HandPose.FingerCount];
        _virtualTip = new bool[HandPose.FingerCount];
        _overrideTip = new bool[HandPose.FingerCount];
        if (TipOverrides == null || TipOverrides.Length != HandPose.FingerCount)
            System.Array.Resize(ref TipOverrides, HandPose.FingerCount);

        // ── 체인만 구성 (rest는 아직 안 건드림) ──
        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            Chains[f] = new Transform[0];
            _rest[f] = new Quaternion[0];

            if (FingerRoots == null || f >= FingerRoots.Length) continue;
            if (FingerRoots[f] == null) continue;

            var chain = new List<Transform>();
            var cur = FingerRoots[f];

            while (cur != null)
            {
                if (cur.name.EndsWith("_end")) { Tips[f] = cur; break; }
                chain.Add(cur);
                cur = cur.childCount > 0 ? cur.GetChild(0) : null;
            }

            Chains[f] = chain.ToArray();

            if (TipOverrides[f] != null) { Tips[f] = TipOverrides[f]; _overrideTip[f] = true; }
            if (Tips[f] == null && chain.Count > 0)
            {
                // _end 본 없음 → 가상 끝: 마지막 마디가 뻗는 방향(앞 마디들과 같은 로컬 축)으로 마디 길이만큼.
                // 예전엔 마지막 마디 본 자체를 끝으로 써서, 끝 마디는 IK로 절대 안 돌았음
                _virtualTip[f] = true;
                Tips[f] = chain[^1];
                _tipLocal[f] = VirtualTipLocal(chain);
            }
        }

        // ── rest 복원 (없을 때만 캡처) ──
        int total = 0;
        for (int f = 0; f < HandPose.FingerCount; f++)
            total += Chains[f]?.Length ?? 0;

        bool needCapture = RestPose == null || !HasValidRest();

        if (needCapture)
        {
            RestPose = new HandPose();
            for (int f = 0; f < HandPose.FingerCount; f++)
            {
                var chain = Chains[f];
                if (chain == null) continue;

                var fp = RestPose.Get(f);
                fp.EnsureSize(chain.Length);

                for (int j = 0; j < chain.Length; j++)
                    if (chain[j] != null) fp.Joints[j] = chain[j].localRotation;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // 배열로 펼치기
        _rest = new Quaternion[HandPose.FingerCount][];
        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var chain = Chains[f];
            int n = chain?.Length ?? 0;
            _rest[f] = new Quaternion[n];

            var fp = RestPose.Get(f);
            fp.EnsureSize(n);

            for (int j = 0; j < n; j++) _rest[f][j] = fp.Joints[j];
        }

        _built = true;
    }

    bool HasValidRest()
    {
        if (RestPose == null) return false;
        RestPose.EnsureValid();

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            int n = Chains[f]?.Length ?? 0;
            if (n == 0) continue;
            if (RestPose.Get(f).Joints.Length != n) return false;
        }
        return true;
    }

    // ══════════════════ 손가락 끝 ══════════════════

    /// <summary>
    /// 마지막 마디 기준 가상 끝 위치. 본이 뻗는 로컬 축은 '앞 마디에서 본 다음 마디 위치'로 알아냄
    /// (블렌더 본이면 보통 Y) → 모델이 휘어져 있어도 마지막 마디 자체 방향을 따름
    /// </summary>
    Vector3 VirtualTipLocal(List<Transform> chain)
    {
        var last = chain[^1];
        if (chain.Count >= 2 && chain[^2] != null)
            return chain[^2].InverseTransformPoint(last.position) * VirtualTipRatio;
        if (last.parent != null)
            return last.parent.InverseTransformPoint(last.position) * VirtualTipRatio;
        return Vector3.up * 0.02f;
    }

    /// <summary>손가락 끝 (월드) — _end 본 · 직접 지정 · 가상 끝</summary>
    public Vector3 TipPos(int f)
    {
        if (Chains == null || Tips == null || f < 0 || f >= Tips.Length || Tips[f] == null) return transform.position;
        if (_virtualTip != null && f < _virtualTip.Length && _virtualTip[f]) return Tips[f].TransformPoint(_tipLocal[f]);
        return Tips[f].position;
    }

    /// <summary>인스펙터 표시용 — 끝을 어디서 잡았는지</summary>
    public string TipSourceLabel(int f)
    {
        if (_overrideTip != null && f < _overrideTip.Length && _overrideTip[f]) return "지정";
        if (_virtualTip != null && f < _virtualTip.Length && _virtualTip[f]) return "자동(가상)";
        return Tips != null && f < Tips.Length && Tips[f] != null ? "_end 본" : "없음";
    }

    /// <summary>손가락 일자로 — 각 마디(둘째부터)를 바로 앞 마디 방향으로. 첫 마디(손바닥 쪽)는 그대로</summary>
    public void StraightenFinger(int f)
    {
        if (!_built) Build();
        var chain = Chains != null && f >= 0 && f < Chains.Length ? Chains[f] : null;
        if (chain == null || chain.Length < 2) return;

        for (int j = 1; j < chain.Length; j++)
        {
            if (chain[j] == null || chain[j - 1] == null) continue;
            Vector3 parentDir = chain[j].position - chain[j - 1].position;
            Vector3 end = j + 1 < chain.Length && chain[j + 1] != null ? chain[j + 1].position : TipPos(f);
            Vector3 cur = end - chain[j].position;
            if (parentDir.sqrMagnitude < 1e-10f || cur.sqrMagnitude < 1e-10f) continue;
            chain[j].rotation = Quaternion.FromToRotation(cur, parentDir) * chain[j].rotation;
        }
    }

    // ══════════════════ IK (부분) ══════════════════

    /// <summary>
    /// 지정한 마디를 target으로 보냄 (그 앞쪽 관절들만 회전).
    /// jointIndex = 마디 수이면 손가락 끝 — 끝 마디까지 전부 돌림
    /// </summary>
    public void SolveIKPartial(int finger, int jointIndex, Vector3 worldTarget)
    {
        if (!_built) Build();
        if (Chains == null || finger >= Chains.Length) return;

        var chain = Chains[finger];
        if (chain == null || jointIndex > chain.Length) return;

        bool toTip = jointIndex == chain.Length;            // 손가락 끝 (끝 마디까지 돌림)
        Transform endBone = toTip ? null : chain[jointIndex];
        if (!toTip && endBone == null) return;
        Vector3 EndPos() => toTip ? TipPos(finger) : endBone.position;

        // 루트 마디는 회전만 (이동 불가)
        if (!toTip && jointIndex == 0)
        {
            Transform parent = endBone.parent;
            if (parent == null) return;

            Vector3 toCur = endBone.position - parent.position;
            Vector3 toTgt = worldTarget - parent.position;
            if (toCur.sqrMagnitude < 1e-8f || toTgt.sqrMagnitude < 1e-8f) return;

            // 루트는 자기 자신을 회전시켜 방향만 바꿈
            Quaternion delta = Quaternion.FromToRotation(toCur, toTgt);
            endBone.rotation = delta * endBone.rotation;
            ClampJoint(finger, 0);
            return;
        }

        // jointIndex 앞의 관절들로 CCD
        for (int iter = 0; iter < IkIterations; iter++)
        {
            for (int j = jointIndex - 1; j >= 0; j--)
            {
                var bone = chain[j];
                if (bone == null) continue;

                Vector3 toEnd = EndPos() - bone.position;
                Vector3 toTarget = worldTarget - bone.position;

                if (toEnd.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f) continue;

                Quaternion delta = Quaternion.FromToRotation(toEnd, toTarget);
                bone.rotation = delta * bone.rotation;

                ClampJoint(finger, j);
            }
        }
    }

    void ClampJoint(int finger, int j)
    {
        var bone = Chains[finger][j];
        if (bone == null) return;

        Quaternion offset = Quaternion.Inverse(_rest[finger][j]) * bone.localRotation;
        offset.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        if (Mathf.Abs(angle) > MaxJointAngle)
        {
            angle = Mathf.Clamp(angle, -MaxJointAngle, MaxJointAngle);
            bone.localRotation = _rest[finger][j] * Quaternion.AngleAxis(angle, axis);
        }
    }

    void AutoDetectRoots()
    {
        if (FingerRoots == null || FingerRoots.Length != HandPose.FingerCount)
            FingerRoots = new Transform[HandPose.FingerCount];

        bool needScan = false;
        for (int i = 0; i < FingerRoots.Length; i++)
            if (FingerRoots[i] == null) needScan = true;

        if (!needScan) return;

        Transform boneRoot = Armature;
        if (boneRoot != null && boneRoot.childCount > 0)
            boneRoot = boneRoot.GetChild(0);

        if (boneRoot == null)
        {
            Debug.LogWarning($"[HandRig] {name}: Armature를 못 찾았습니다. 수동 지정 필요.", this);
            return;
        }

        var roots = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < boneRoot.childCount; i++)
        {
            var c = boneRoot.GetChild(i);
            if (c.name.EndsWith("_end")) continue;
            roots.Add(c);
        }

        for (int i = 0; i < HandPose.FingerCount && i < roots.Count; i++)
            FingerRoots[i] = roots[i];

        if (roots.Count == 0)
            Debug.LogWarning($"[HandRig] {name}: 손가락 본을 못 찾았습니다.", this);
    }

    public int GetJointCount(int finger)
    {
        if (Chains == null || finger < 0 || finger >= Chains.Length) return 0;
        return Chains[finger]?.Length ?? 0;
    }

    // ══════════════════ 포즈 적용 ══════════════════

    public void Apply(HandPose pose)
    {
        if (!_built) Build();
        if (Chains == null || pose == null) return;

        pose.EnsureValid();

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var chain = Chains[f];
            if (chain == null) continue;

            var fp = pose.Get(f);
            if (fp == null) continue;
            fp.EnsureSize(chain.Length);

            for (int j = 0; j < chain.Length; j++)
            {
                if (chain[j] == null) continue;
                chain[j].localRotation = _rest[f][j] * fp.Joints[j];
            }
        }
    }

    public void ResetToRest()
    {
        if (!_built) Build();
        if (Chains == null || RestPose == null) return;

        RestPose.EnsureValid();

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            if (f >= Chains.Length) break;

            var chain = Chains[f];
            if (chain == null) continue;

            var fp = RestPose.Get(f);
            fp.EnsureSize(chain.Length);

            for (int j = 0; j < chain.Length; j++)
                if (chain[j] != null)
                    chain[j].localRotation = fp.Joints[j];
        }
    }

    // ══════════════════ IK (CCD) ══════════════════

    /// <summary>손가락 끝을 target으로 보내고, 결과를 pose에 기록</summary>
    public void SolveIK(int finger, Vector3 worldTarget, HandPose pose)
    {
        if (!_built) Build();
        if (Chains == null || finger >= Chains.Length) return;

        var chain = Chains[finger];
        if (chain == null || chain.Length == 0) return;

        if (Tips == null || Tips[finger] == null) return;

        for (int iter = 0; iter < IkIterations; iter++)
        {
            for (int j = chain.Length - 1; j >= 0; j--)
            {
                var bone = chain[j];
                if (bone == null) continue;

                Vector3 toTip = TipPos(finger) - bone.position;
                Vector3 toTarget = worldTarget - bone.position;

                if (toTip.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f) continue;

                bone.rotation = Quaternion.FromToRotation(toTip, toTarget) * bone.rotation;
                ClampJoint(finger, j);
            }
        }

        BakeToPose(finger, pose);
    }

    /// <summary>현재 본의 로컬 회전을 그대로 저장</summary>
    public void BakeToPose(int finger, HandPose pose)
    {
        if (pose == null || Chains == null) return;
        if (finger < 0 || finger >= Chains.Length) return;

        pose.EnsureValid();

        var chain = Chains[finger];
        if (chain == null) return;

        var fp = pose.Get(finger);
        fp.EnsureSize(chain.Length);

        for (int j = 0; j < chain.Length; j++)
        {
            if (chain[j] == null) continue;
            fp.Joints[j] = Quaternion.Inverse(_rest[finger][j]) * chain[j].localRotation;
        }
    }

    public void BakeAll(HandPose pose)
    {
        for (int f = 0; f < HandPose.FingerCount; f++) BakeToPose(f, pose);
    }

    public Vector3 GetTipPosition(int finger) => TipPos(finger);

    static Vector3 NormalizeEuler(Vector3 e) => new(N(e.x), N(e.y), N(e.z));
    static float N(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    /// <summary>
    /// 다른 손의 손 모양을 가져옴. 각 손의 기준 자세(Rest) 대비 굽힘으로 옮기므로
    /// 두 손의 본 기본 회전이 달라도 같은 모양이 됨. mirror로 관절 회전 축 반전 가능.
    /// </summary>
    public void CopyBoneRotationsFrom(HandRig other, MirrorMode mirror = MirrorMode.AsIs)
    {
        if (other == null || other == this) return;

        if (!_built) Build();
        if (!other.IsBuilt) other.Build();
        if (Chains == null || other.Chains == null) return;

        var pose = new HandPose();
        other.BakeAll(pose);

        RecordUndo("Copy Hand Pose");
        Apply(Mirror(pose, mirror));
    }

    // ══════════════════ 손 모양 복사 · 붙여넣기 ══════════════════
    // 모션 편집 창(WeaponSetupWindow)의 '손가락 복사'와 같은 클립보드 형식 — 서로 오가며 붙여넣기 가능

    public enum MirrorMode { AsIs, MirrorX, MirrorY, MirrorZ }

    const string ClipPrefix = "MOTIONSETUP_HAND:";
    const string PoseClipPrefix = "MOTIONSETUP_POSE:";

    [System.Serializable] class HandClip { public HandPose Hand; }
    [System.Serializable] class PoseClip { public ArmPose Pose; }

    /// <summary>현재 손 모양을 시스템 클립보드로</summary>
    public void CopyToClipboard()
    {
        if (!_built) Build();
        var pose = new HandPose();
        BakeAll(pose);
        GUIUtility.systemCopyBuffer = ClipPrefix + JsonUtility.ToJson(new HandClip { Hand = pose });
    }

    /// <summary>클립보드의 손 모양을 적용. 모션 창에서 '포즈 복사'한 것도 받음 (이 손 쪽 손가락 사용)</summary>
    public bool PasteFromClipboard(MirrorMode mirror = MirrorMode.AsIs)
    {
        if (!TryReadClipboard(out var pose)) return false;

        RecordUndo("Paste Hand Pose");
        Apply(Mirror(pose, mirror));
        return true;
    }

    bool TryReadClipboard(out HandPose pose)
    {
        pose = null;
        string s = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(s)) return false;

        if (s.StartsWith(ClipPrefix))
        {
            var clip = new HandClip();
            JsonUtility.FromJsonOverwrite(s.Substring(ClipPrefix.Length), clip);
            pose = clip.Hand;
        }
        else if (s.StartsWith(PoseClipPrefix))
        {
            var clip = new PoseClip();
            JsonUtility.FromJsonOverwrite(s.Substring(PoseClipPrefix.Length), clip);
            if (clip.Pose != null)
                pose = IsLeftHand() ? (clip.Pose.LeftFingers ?? clip.Pose.RightFingers)
                                    : (clip.Pose.RightFingers ?? clip.Pose.LeftFingers);
        }

        return pose != null;
    }

    /// <summary>관절 회전을 축 기준으로 반전한 복사본</summary>
    public static HandPose Mirror(HandPose src, MirrorMode mode)
    {
        if (src == null) return null;
        var c = src.Clone();
        if (mode == MirrorMode.AsIs) return c;

        c.EnsureValid();
        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var fp = c.Get(f);
            if (fp?.Joints == null) continue;

            for (int j = 0; j < fp.Joints.Length; j++)
            {
                var q = fp.Joints[j];
                fp.Joints[j] = mode switch
                {
                    MirrorMode.MirrorX => new Quaternion(q.x, -q.y, -q.z, q.w),
                    MirrorMode.MirrorY => new Quaternion(-q.x, q.y, -q.z, q.w),
                    _ => new Quaternion(-q.x, -q.y, q.z, q.w),
                };
            }
        }
        return c;
    }

    /// <summary>같은 팔 리그의 반대쪽 손 (없으면 null)</summary>
    public HandRig FindOpposite()
    {
        var arm = GetComponentInParent<FPArmRig>();
        if (arm == null) arm = FindFirstObjectByType<FPArmRig>();
        if (arm == null) return null;

        if (arm.RightHandRig == this) return arm.LeftHandRig;
        if (arm.LeftHandRig == this) return arm.RightHandRig;
        return null;
    }

    bool IsLeftHand()
    {
        var arm = GetComponentInParent<FPArmRig>();
        return arm != null && arm.LeftHandRig == this;
    }

    void RecordUndo(string label)
    {
#if UNITY_EDITOR
        if (Application.isPlaying || Chains == null) return;

        var list = new List<Object>();
        foreach (var chain in Chains)
            if (chain != null)
                foreach (var t in chain)
                    if (t != null) list.Add(t);

        if (list.Count > 0) UnityEditor.Undo.RecordObjects(list.ToArray(), label);
#endif
    }

    // ── 컴포넌트 ⋮ 메뉴 ──

    [ContextMenu("손 모양/복사")]
    void CtxCopy() => CopyToClipboard();

    [ContextMenu("손 모양/붙여넣기")]
    void CtxPaste() => CtxPasteWith(MirrorMode.AsIs);

    [ContextMenu("손 모양/붙여넣기 (X축 반전)")]
    void CtxPasteX() => CtxPasteWith(MirrorMode.MirrorX);

    [ContextMenu("손 모양/붙여넣기 (Y축 반전)")]
    void CtxPasteY() => CtxPasteWith(MirrorMode.MirrorY);

    [ContextMenu("손 모양/붙여넣기 (Z축 반전)")]
    void CtxPasteZ() => CtxPasteWith(MirrorMode.MirrorZ);

    [ContextMenu("손 모양/반대 손에서 가져오기")]
    void CtxFromOpposite()
    {
        var other = FindOpposite();
        if (other == null) { Debug.LogWarning($"[HandRig] {name}: 반대 손을 못 찾았습니다 (FPArmRig의 Right/Left Hand Rig 확인)", this); return; }
        CopyBoneRotationsFrom(other);
    }

    [ContextMenu("손 모양/반대 손에 보내기")]
    void CtxToOpposite()
    {
        var other = FindOpposite();
        if (other == null) { Debug.LogWarning($"[HandRig] {name}: 반대 손을 못 찾았습니다 (FPArmRig의 Right/Left Hand Rig 확인)", this); return; }
        other.CopyBoneRotationsFrom(this);
    }

    [ContextMenu("손 모양/기준 자세로 되돌리기")]
    void CtxReset()
    {
        RecordUndo("Reset Hand Pose");
        ResetToRest();
    }

    void CtxPasteWith(MirrorMode mode)
    {
        if (!PasteFromClipboard(mode))
            Debug.LogWarning($"[HandRig] {name}: 클립보드에 손 모양이 없습니다. 먼저 '손 모양/복사' 또는 모션 창의 '손가락 복사'를 하세요.", this);
    }

    // ══════════════════ 에디터 ══════════════════

    [ContextMenu("본 자동 탐색")]
    public void Rebuild()
    {
        for (int i = 0; i < FingerRoots.Length; i++) FingerRoots[i] = null;
        _built = false;
        Build();

        string s = "[HandRig] 체인 —";
        for (int f = 0; f < HandPose.FingerCount; f++)
            s += $" [{f}]{(FingerRoots[f] != null ? FingerRoots[f].name : "없음")}({GetJointCount(f)}마디 · 끝 {TipSourceLabel(f)})";
        Debug.Log(s);
    }

    [ContextMenu("현재 자세를 Rest로 재설정")]
    public void CaptureRest()
    {
        RestPose = null;
        _built = false;
        Build();
        Debug.Log($"[HandRig] {name}: Rest 갱신됨");
    }
}