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

    [Header("IK")]
    [Range(1, 20)] public int IkIterations = 8;
    [Range(0f, 90f)] public float MaxJointAngle = 90f;

    // 각 손가락의 관절 체인 (뿌리 → 끝)
    [System.NonSerialized] public Transform[][] Chains;
    [System.NonSerialized] public Transform[] Tips;      // _end 본
    private Quaternion[][] _rest;
    private bool _built;

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
            if (Tips[f] == null && chain.Count > 0) Tips[f] = chain[^1];
        }

        // ── rest 복원 (없을 때만 캡처) ──
        int total = 0;
        for (int f = 0; f < HandPose.FingerCount; f++)
            total += Chains[f]?.Length ?? 0;

        bool needCapture = _restEuler == null || _restEuler.Length != total || total == 0;

        if (needCapture)
        {
            _restEuler = new Vector3[total];
            Debug.Log($"[HandRig] {name}: Rest 최초 캡처 ({total}개 관절)");
        }

        int idx = 0;
        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var chain = Chains[f];
            if (chain == null) continue;

            _rest[f] = new Quaternion[chain.Length];

            for (int j = 0; j < chain.Length; j++)
            {
                if (needCapture && chain[j] != null)
                    _restEuler[idx] = chain[j].localRotation.eulerAngles;

                _rest[f][j] = Quaternion.Euler(_restEuler[idx]);
                idx++;
            }
        }

        _built = true;
    }


    /// <summary>지정한 마디를 target으로 보냄 (그 앞쪽 관절들만 회전)</summary>
    public void SolveIKPartial(int finger, int jointIndex, Vector3 worldTarget)
    {
        if (!_built) Build();
        if (Chains == null || finger >= Chains.Length) return;

        var chain = Chains[finger];
        if (chain == null || jointIndex >= chain.Length) return;

        Transform endBone = chain[jointIndex];
        if (endBone == null) return;

        // 루트 마디는 회전만 (이동 불가)
        if (jointIndex == 0)
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

                Vector3 toEnd = endBone.position - bone.position;
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

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var chain = Chains[f];
            if (chain == null) continue;
            for (int j = 0; j < chain.Length; j++)
                if (chain[j] != null) chain[j].localRotation = _rest[f][j];
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

        Transform tip = Tips[finger];
        if (tip == null) return;

        for (int iter = 0; iter < IkIterations; iter++)
        {
            for (int j = chain.Length - 1; j >= 0; j--)
            {
                var bone = chain[j];
                if (bone == null) continue;

                Vector3 toTip = tip.position - bone.position;
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

    public Vector3 GetTipPosition(int finger) =>
        (Chains != null && Tips != null && finger < Tips.Length && Tips[finger] != null)
            ? Tips[finger].position : transform.position;

    static Vector3 NormalizeEuler(Vector3 e) => new(N(e.x), N(e.y), N(e.z));
    static float N(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    /// <summary>다른 손의 본 로컬 회전을 그대로 가져옴</summary>
    public void CopyBoneRotationsFrom(HandRig other)
    {
        if (other == null) return;

        if (!_built) Build();
        if (!other.IsBuilt) other.Build();
        if (Chains == null || other.Chains == null) return;

        for (int f = 0; f < HandPose.FingerCount; f++)
        {
            var dst = Chains[f];
            var src = other.Chains[f];
            if (dst == null || src == null) continue;

            int n = Mathf.Min(dst.Length, src.Length);
            for (int j = 0; j < n; j++)
            {
                if (dst[j] == null || src[j] == null) continue;
                dst[j].localRotation = src[j].localRotation;
            }
        }
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
            s += $" [{f}]{FingerRoots[f]?.name ?? "없음"}({GetJointCount(f)}마디)";
        Debug.Log(s);
    }

    [ContextMenu("현재 자세를 Rest로 재설정")]
    public void CaptureRest()
    {
        _restEuler = null;      // 강제 재캡처
        _built = false;
        Build();
        Debug.Log("[HandRig] Rest 자세 갱신됨");
    }
}