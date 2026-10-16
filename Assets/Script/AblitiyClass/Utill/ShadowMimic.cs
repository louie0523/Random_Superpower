using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 쉐도우 워커의 그림자가 주인의 모습과 모션을 그대로 따라 하게 함 (모든 클라이언트, 로컬 연출).
///
/// 주인의 팔 리그(FPArmRig 아래 손·무기)와 몸통(Body)·머리(Head)를 겉모습만 복제함.
/// 복제할 때 스크립트·콜라이더는 떼어내고 재질은 그림자 재질로 바꿈.
/// 그 뒤 매 프레임 주인의 뼈 위치를 Delay초 늦게 그대로 옮겨 옴.
///   → 손 IK(양손 그립)·손가락 굽힘·무기 휘두르기·몸통·머리 모션이 따로 설정 없이 다 따라옴
///   → 무기를 바꾸면 자동으로 다시 복제
/// ShadowFollower와 같은 오브젝트에 붙음 (이 오브젝트가 그림자의 '발 위치')
/// </summary>
[DefaultExecutionOrder(10000)]      // 주인 리그·IK가 다 끝난 뒤에 따라 함
public class ShadowMimic : MonoBehaviour
{
    [Tooltip("주인보다 몇 초 늦게 따라 할지 — 쉐도우 워커는 그림자 공격 지연(EchoDelay)을 넣음")]
    public float Delay = 0.25f;

    [Tooltip("그림자 재질. 비우면 검은 Unlit")]
    public Material Material;

    [Tooltip("그림자 레이어 — 모든 카메라에 보이는 레이어 (보통 Default)")]
    public int Layer = 0;

    private Transform _owner;       // 주인 루트 (플레이어)
    private FPArmRig _rig;
    private ShadowFollower _follower;

    // 복제본 ↔ 원본
    private readonly List<Transform> _src = new();
    private readonly List<Transform> _dst = new();
    private readonly List<(Renderer src, Renderer dst)> _renderers = new();
    private readonly List<Transform> _cloneRoots = new();

    // 지연용 기록 — 프레임마다 주인 기준 상대 위치·회전
    struct Frame { public float T; public Vector3[] P; public Quaternion[] R; }
    private const int BufferSize = 90;
    private readonly Frame[] _buf = new Frame[BufferSize];
    private int _head, _count;

    private Transform _lastWeapon;
    private int _lastTransformCount;
    private float _checkTimer;

    // ══════════════════ 설정 ══════════════════

    [Tooltip("팔 · 손 · 무기만 복제 (몸 · 머리 제외) — 쉐도우 워커 본인 화면용 (내 그림자 몸이 시야를 가리지 않게)")]
    public bool ArmsOnly;

    public void Init(Transform owner, FPArmRig rig, Material mat, float delay, bool armsOnly = false)
    {
        ArmsOnly = armsOnly;
        _owner = owner;
        _rig = rig;
        Material = mat;
        Delay = delay;
        _follower = GetComponent<ShadowFollower>();
        Rebuild();
    }

    /// <summary>주인 겉모습을 다시 복제 (무기 교체 등)</summary>
    public void Rebuild()
    {
        foreach (var r in _cloneRoots) if (r != null) Destroy(r.gameObject);
        _cloneRoots.Clear();
        _src.Clear();
        _dst.Clear();
        _renderers.Clear();
        _count = 0;

        if (_owner == null || _rig == null) return;

        foreach (var root in Sources())
            CloneTree(root);

        _lastWeapon = _rig.GetWeaponTransform();
        _lastTransformCount = CountSourceTransforms();
    }

    /// <summary>복제할 원본 — 팔 리그, 몸통, 머리 (서로 겹치면 위쪽 것만)</summary>
    IEnumerable<Transform> Sources()
    {
        var list = new List<Transform>();
        void Add(Transform t)
        {
            if (t == null) return;
            foreach (var x in list) if (t.IsChildOf(x)) return;
            list.RemoveAll(x => x.IsChildOf(t));
            list.Add(t);
        }
        Add(_rig.transform);
        if (!ArmsOnly)
        {
            Add(_rig.Body);
            Add(_rig.Head);
        }
        return list;
    }

    int CountSourceTransforms()
    {
        int n = 0;
        foreach (var s in Sources()) n += s.GetComponentsInChildren<Transform>(true).Length;
        return n;
    }

    void CloneTree(Transform source)
    {
        // 꺼진 부모 아래에 만들면 복제본의 스크립트 Awake/OnEnable이 안 돎 → 떼어낸 뒤 켬
        var holder = new GameObject("ShadowHolder");
        holder.SetActive(false);

        var clone = Instantiate(source.gameObject, holder.transform);
        clone.name = "Shadow_" + source.name;
        Strip(clone);

        clone.transform.SetParent(transform, false);
        Destroy(holder);

        // 크기 — 주인 기준 원본 크기 그대로
        Vector3 ls = source.lossyScale, os = _owner.lossyScale;
        clone.transform.localScale = new Vector3(ls.x / Safe(os.x), ls.y / Safe(os.y), ls.z / Safe(os.z));

        _cloneRoots.Add(clone.transform);

        // 같은 구조로 복제됐으므로 순서대로 짝지음 (부모가 먼저)
        var a = source.GetComponentsInChildren<Transform>(true);
        var b = clone.GetComponentsInChildren<Transform>(true);
        var map = new Dictionary<Transform, Transform>();
        int n = Mathf.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            _src.Add(a[i]);
            _dst.Add(b[i]);
            map[b[i]] = a[i];
            b[i].gameObject.layer = Layer;
            b[i].gameObject.SetActive(a[i].gameObject.activeSelf);
        }

        // 렌더러 짝 — 보이기 여부를 원본에 맞춤 (한손 무기일 때 왼손 숨김 등)
        var mat = Material != null ? Material : Fallback();
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
        {
            Renderer sr = null;
            if (map.TryGetValue(r.transform, out var st)) sr = st.GetComponent(r.GetType()) as Renderer;

            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int m = 0; m < mats.Length; m++) mats[m] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            _renderers.Add((sr, r));
        }
    }

    static float Safe(float v) => Mathf.Abs(v) < 0.0001f ? 1f : v;

    /// <summary>겉모습만 남기고 나머지 컴포넌트 제거 (의존 순서대로)</summary>
    static void Strip(GameObject go)
    {
        var all = go.GetComponentsInChildren<Component>(true);

        // 1) 스크립트 — 뒤에서부터 (나중에 붙은 게 앞의 것에 의존하는 경우가 많음)
        for (int i = all.Length - 1; i >= 0; i--) if (all[i] is MonoBehaviour) DestroyImmediate(all[i]);
        // 2) 조인트
        foreach (var c in all) if (c != null && c is Joint) DestroyImmediate(c);
        // 3) 파티클 본체 (렌더러보다 먼저)
        foreach (var c in all) if (c != null && c is ParticleSystem) DestroyImmediate(c);
        // 4) 나머지 — Transform · 메쉬 · 메쉬 렌더러만 남김
        foreach (var c in all)
        {
            if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
            DestroyImmediate(c);
        }
    }

    static Material _fallback;
    static Material Fallback()
    {
        if (_fallback != null) return _fallback;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        _fallback = new Material(sh) { name = "ShadowFallback", color = new Color(0.04f, 0.02f, 0.08f, 1f) };
        return _fallback;
    }

    // ══════════════════ 매 프레임 ══════════════════

    void LateUpdate()
    {
        if (_owner == null || _rig == null) return;

        if (NeedsRebuild()) Rebuild();
        if (_src.Count == 0) return;

        Record();
        Apply(Time.time - Mathf.Max(0f, Delay));

        bool hidden = _follower != null && _follower.Hidden;
        foreach (var (s, d) in _renderers)
            if (d != null) d.enabled = !hidden && s != null && s.enabled && s.gameObject.activeInHierarchy;
    }

    bool NeedsRebuild()
    {
        // 무기 모델이 바뀜 (교체 · 모션 전용 무기)
        if (_rig.GetWeaponTransform() != _lastWeapon) return true;

        // 원본이 사라짐
        for (int i = 0; i < _src.Count; i++) if (_src[i] == null) return true;

        // 구조가 바뀜 (손 프리팹이 늦게 붙는 경우 등) — 가끔만 확인
        _checkTimer -= Time.deltaTime;
        if (_checkTimer <= 0f)
        {
            _checkTimer = 0.5f;
            if (CountSourceTransforms() != _lastTransformCount) return true;
        }
        return false;
    }

    void Record()
    {
        int n = _src.Count;
        ref var f = ref _buf[_head];
        if (f.P == null || f.P.Length != n) { f.P = new Vector3[n]; f.R = new Quaternion[n]; }

        f.T = Time.time;
        Quaternion inv = Quaternion.Inverse(_owner.rotation);
        for (int i = 0; i < n; i++)
        {
            var s = _src[i];
            f.P[i] = _owner.InverseTransformPoint(s.position);
            f.R[i] = inv * s.rotation;
        }

        _head = (_head + 1) % BufferSize;
        _count = Mathf.Min(_count + 1, BufferSize);
    }

    void Apply(float t)
    {
        // t 앞뒤 두 기록을 찾아 섞음 (가장 최근 → 과거 순)
        int newest = (_head - 1 + BufferSize) % BufferSize;
        int a = newest, b = newest;
        for (int k = 0; k < _count; k++)
        {
            int idx = (newest - k + BufferSize) % BufferSize;
            b = a;
            a = idx;
            if (_buf[idx].T <= t) break;
        }

        ref var fa = ref _buf[a];
        ref var fb = ref _buf[b];
        int n = _src.Count;
        if (fa.P == null || fa.P.Length != n || fb.P == null || fb.P.Length != n) return;

        float span = fb.T - fa.T;
        float w = span > 0.0001f ? Mathf.Clamp01((t - fa.T) / span) : 1f;

        var root = transform;
        Quaternion rootRot = root.rotation;
        for (int i = 0; i < n; i++)
        {
            var d = _dst[i];
            if (d == null) continue;
            Vector3 p = Vector3.LerpUnclamped(fa.P[i], fb.P[i], w);
            Quaternion r = Quaternion.SlerpUnclamped(fa.R[i], fb.R[i], w);
            d.SetPositionAndRotation(root.TransformPoint(p), rootRot * r);
        }
    }

    void OnDestroy()
    {
        foreach (var r in _cloneRoots) if (r != null) Destroy(r.gameObject);
    }
}