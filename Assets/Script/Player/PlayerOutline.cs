using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 플레이어 아웃라인 — 몸 · 손 · 무기(+ 변화 파츠) 둘레에 색 테두리. 플레이어 프리팹 루트에 붙임.
///   평소: GlobalMode (설정) — Visible = 보이는 부분만 / XRay = 벽 너머로도 / Off
///   RequestXRay(대상, 초): 그 시간 동안 벽 너머로도 (쉐도우 워커 궁 영역 안 적 등)
///   내 캐릭터는 기본으로 안 그림 (outlineSelf)
/// 화면마다 각자 계산 — 네트워크 없음.
/// 기법: QuickOutline(Chris Nolet, MIT)과 같은 '스텐실 마스크 + 부풀린 테두리'. URP용 셰이더로 새로 작성
/// 모델 임포트에서 Read/Write를 켜 두면 매끈한 법선을 구워 각진 모델에서도 테두리가 안 끊김
/// </summary>
[DisallowMultipleComponent]
public class PlayerOutline : MonoBehaviour
{
    public enum OutlineMode { Off, Visible, XRay }

    /// <summary>설정 — 평소 아웃라인 방식 (모든 플레이어 공통, 이 화면에서만)</summary>
    public static OutlineMode GlobalMode = OutlineMode.Visible;

    [Header("셰이더 (빌드에 포함되게 연결해 두기)")]
    [SerializeField] private Shader maskShader;
    [SerializeField] private Shader fillShader;

    [Header("모양")]
    [SerializeField] private Color color = new(1f, 0.28f, 0.22f, 1f);
    [Tooltip("벽 너머로 보일 때 색")]
    [SerializeField] private Color xrayColor = new(1f, 0.15f, 0.15f, 0.9f);
    [SerializeField, Range(0f, 10f)] private float width = 2.5f;
    [SerializeField, Range(0f, 10f)] private float xrayWidth = 3f;

    [Header("대상")]
    [Tooltip("내 캐릭터에도 그림 (보통 끔)")]
    [SerializeField] private bool outlineSelf = false;
    [Tooltip("이 레이어의 렌더러는 제외 (UI · 이펙트 등)")]
    [SerializeField] private LayerMask excludeLayers;

    private static readonly int IdColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int IdWidth = Shader.PropertyToID("_OutlineWidth");
    private static readonly int IdZTest = Shader.PropertyToID("_ZTest");

    private static readonly Dictionary<Mesh, Mesh> _baked = new();
    private static readonly HashSet<Mesh> _warned = new();
    private static readonly List<PlayerOutline> _all = new();

    private Material _mask, _fill;
    private readonly Dictionary<Renderer, Material[]> _orig = new();
    private OutlineMode _mode = OutlineMode.Off;
    private float _xrayUntil;
    private float _checkTimer;
    private int _rendererCount;
    private NetworkObject _no;
    private PlayerHealth _health;

    // ══════════════════ 외부에서 ══════════════════

    /// <summary>이 대상을 seconds 동안 벽 너머로도 보이게 (매 프레임 불러도 됨)</summary>
    public static void RequestXRay(GameObject target, float seconds = 0.2f)
    {
        if (target == null) return;
        var o = target.GetComponentInParent<PlayerOutline>();
        if (o != null) o._xrayUntil = Mathf.Max(o._xrayUntil, Time.time + seconds);
    }

    /// <summary>지금 벽 너머로 보이는 중인지</summary>
    public bool IsXRayNow => _mode == OutlineMode.XRay;

    /// <summary>설정에서 평소 방식을 바꿈</summary>
    public static void SetGlobalMode(OutlineMode m) => GlobalMode = m;

    // ══════════════════ 생명주기 ══════════════════

    void Awake()
    {
        _no = GetComponentInParent<NetworkObject>();
        _health = GetComponentInParent<PlayerHealth>();
        if (maskShader == null) maskShader = Shader.Find("Hidden/Game/OutlineMask");
        if (fillShader == null) fillShader = Shader.Find("Hidden/Game/OutlineFill");
        if (maskShader == null || fillShader == null)
        {
            Debug.LogWarning("[PlayerOutline] 아웃라인 셰이더를 못 찾음 — PlayerOutline에 OutlineMask / OutlineFill 셰이더를 연결하세요", this);
            enabled = false;
            return;
        }
        _mask = new Material(maskShader) { name = "OutlineMask (instance)", hideFlags = HideFlags.HideAndDontSave };
        _fill = new Material(fillShader) { name = "OutlineFill (instance)", hideFlags = HideFlags.HideAndDontSave };
    }

    void OnEnable() => _all.Add(this);

    void OnDisable()
    {
        _all.Remove(this);
        Remove();
        _mode = OutlineMode.Off;
    }

    void OnDestroy()
    {
        if (_mask != null) Destroy(_mask);
        if (_fill != null) Destroy(_fill);
    }

    void LateUpdate()
    {
        bool self = _no != null && _no.IsValid && _no.HasInputAuthority;
        bool dead = _health != null && _health.IsDead;

        OutlineMode want;
        if (dead) want = OutlineMode.Off;
        else if (Time.time < _xrayUntil) want = OutlineMode.XRay;
        else if (self && !outlineSelf) want = OutlineMode.Off;
        else want = GlobalMode;

        if (want != _mode)
        {
            if (want == OutlineMode.Off) Remove();
            else if (_mode == OutlineMode.Off) Apply();
            _mode = want;
            SetLook();
        }

        // 무기 교체 · 변화 파츠 등으로 렌더러가 바뀌면 다시 붙임
        if (_mode != OutlineMode.Off)
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 0.4f;
                if (Changed()) { Remove(); Apply(); }
            }
        }
    }

    void SetLook()
    {
        if (_fill == null) return;
        bool xray = _mode == OutlineMode.XRay;
        _fill.SetColor(IdColor, xray ? xrayColor : color);
        _fill.SetFloat(IdWidth, xray ? xrayWidth : width);
        _fill.SetFloat(IdZTest, (float)(xray ? CompareFunction.Always : CompareFunction.LessEqual));
    }

    // ══════════════════ 붙이기 · 떼기 ══════════════════

    List<Renderer> Targets()
    {
        var list = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;     // 그림자만 남긴 파츠
            if ((excludeLayers.value & (1 << r.gameObject.layer)) != 0) continue;
            list.Add(r);
        }
        return list;
    }

    bool Changed()
    {
        foreach (var kv in _orig) if (kv.Key == null) return true;
        return Targets().Count != _rendererCount;
    }

    void Apply()
    {
        if (_mask == null || _fill == null) return;
        var rs = Targets();
        _rendererCount = rs.Count;

        foreach (var r in rs)
        {
            PrepareMesh(r);
            var mats = r.sharedMaterials;
            _orig[r] = mats;

            var next = new Material[mats.Length + 2];
            for (int i = 0; i < mats.Length; i++) next[i] = mats[i];
            next[mats.Length] = _mask;
            next[mats.Length + 1] = _fill;
            r.sharedMaterials = next;
        }
        SetLook();
    }

    void Remove()
    {
        foreach (var kv in _orig)
        {
            var r = kv.Key;
            if (r == null) continue;
            // 우리 것만 빼고 나머지는 지금 상태 유지 (다른 코드가 재질을 바꿨을 수도 있어서)
            var cur = r.sharedMaterials;
            var keep = new List<Material>(cur.Length);
            foreach (var m in cur) if (m != _mask && m != _fill) keep.Add(m);
            r.sharedMaterials = keep.ToArray();
        }
        _orig.Clear();
    }

    // ══════════════════ 메쉬 준비 — 매끈한 법선 · 하위 메쉬 합치기 ══════════════════

    void PrepareMesh(Renderer r)
    {
        Mesh src = null;
        MeshFilter mf = null;
        var smr = r as SkinnedMeshRenderer;
        if (smr != null) src = smr.sharedMesh;
        else
        {
            mf = r.GetComponent<MeshFilter>();
            if (mf != null) src = mf.sharedMesh;
        }
        if (src == null || _baked.ContainsValue(src)) return;      // 이미 구운 복사본

        if (!_baked.TryGetValue(src, out var baked))
        {
            if (!src.isReadable)
            {
                if (_warned.Add(src))
                    Debug.Log($"[PlayerOutline] '{src.name}' 메쉬가 Read/Write 꺼짐 — 각진 곳 테두리가 끊길 수 있음 (모델 Import → Read/Write 켜기)", r);
                return;
            }
            baked = Instantiate(src);
            baked.name = src.name + " (outline)";
            BakeSmoothNormals(baked);
            CombineSubmeshes(baked);
            _baked[src] = baked;
        }

        if (smr != null) smr.sharedMesh = baked;
        else if (mf != null) mf.sharedMesh = baked;
    }

    static void BakeSmoothNormals(Mesh m)
    {
        var verts = m.vertices;
        var norms = m.normals;
        if (norms == null || norms.Length != verts.Length) { m.RecalculateNormals(); norms = m.normals; }

        var sum = new Dictionary<Vector3, Vector3>(verts.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            sum.TryGetValue(verts[i], out var s);
            sum[verts[i]] = s + norms[i];
        }
        var smooth = new List<Vector3>(verts.Length);
        for (int i = 0; i < verts.Length; i++) smooth.Add(sum[verts[i]].normalized);
        m.SetUVs(3, smooth);
    }

    /// <summary>하위 메쉬가 여러 개면 전부 합친 하나를 뒤에 추가 — 덧붙인 아웃라인 재질이 몸 전체에 그려지게</summary>
    static void CombineSubmeshes(Mesh m)
    {
        if (m.subMeshCount <= 1) return;
        var all = new List<int>();
        for (int i = 0; i < m.subMeshCount; i++) all.AddRange(m.GetTriangles(i));
        m.subMeshCount += 1;
        m.SetTriangles(all, m.subMeshCount - 1);
    }
}
