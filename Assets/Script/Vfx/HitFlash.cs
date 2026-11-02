using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 피격 번쩍임 — 맞은 부위만 잠깐 색이 변함 (머리 맞으면 머리, 몸 맞으면 몸통). 두 가지를 섞어 씀 (Style):
///   Tint    — 재질의 색 프로퍼티(_BaseColor · _Color · 툰 셰이더의 그림자 색 등)를 실제로 그 색 쪽으로 (MaterialPropertyBlock — 재질 복사본 안 생김)
///   Overlay — '덮는 재질'(Hidden/Game/HitFlash)을 잠깐 얹음. 가장자리 진하고 가운데 옅은 카툰식 테두리
///   Both    — 둘 다 (기본). 밝은 면은 Tint가, 윤곽은 Overlay가 맡음
/// 아웃라인(PlayerOutline)과 같은 방식이라 같이 써도 됨.
///   머리 = FPArmRig.Head 아래 (머리에 붙은 변화 파츠 포함) · 몸 = 나머지 (무기 모델 제외)
///   PlayerHealth · 조커 분신이 알아서 붙임 (프리팹에 안 넣어도 됨)
/// 셰이더는 Vfx/Resources/HitFlash.shader — Resources 폴더라 빌드에 자동 포함
/// </summary>
[DisallowMultipleComponent]
public class HitFlash : MonoBehaviour
{
    public enum Part { Body, Head, All }
    public enum FlashStyle { Both, Tint, Overlay }

    /// <summary>번쩍이는 방식 (PlayerHealth가 넣음)</summary>
    public FlashStyle Style = FlashStyle.Both;
    /// <summary>Tint — 원래 색에서 번쩍임 색으로 얼마나 (1 = 완전히 그 색)</summary>
    [Range(0f, 1f)] public float TintAmount = 0.75f;
    /// <summary>Both일 때 덮개 진하기 배율 (Tint와 겹쳐 너무 과하지 않게)</summary>
    [Range(0f, 1f)] public float OverlayInBoth = 0.55f;

    /// <summary>색으로 바꿀 프로퍼티 — 일반 · 툰 셰이더(UTS · lilToon 등)의 기본 · 그림자 색</summary>
    static readonly int[] TintProps =
    {
        Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color"), Shader.PropertyToID("_MainColor"),
        Shader.PropertyToID("_Tint"), Shader.PropertyToID("_ColorTint"),
        Shader.PropertyToID("_1st_ShadeColor"), Shader.PropertyToID("_2nd_ShadeColor"),
        Shader.PropertyToID("_ShadeColor"), Shader.PropertyToID("_ShadowColor"),
    };

    struct TintRec
    {
        public Renderer R;
        public int Index;       // 재질 칸
        public int Prop;
        public Color Orig;
        public bool WasEmpty;   // 원래 PropertyBlock이 비어 있었으면 끝날 때 통째로 비움 (SRP Batcher 유지)
    }
    static readonly MaterialPropertyBlock _mpb = new();

    static readonly int IdColor = Shader.PropertyToID("_FlashColor");
    static Shader _shader;
    static bool _warned;

    class Group
    {
        public readonly List<Renderer> Rs = new();
        public Material Mat;
        public Color Color;
        public float T, Dur;
        public bool On;
        public readonly List<TintRec> Tints = new();
    }

    readonly Group _body = new(), _head = new();
    FPArmRig _rig;
    NetworkObject _no;
    ThirdPersonView _tpv;

    /// <summary>내 캐릭터 1인칭에서도 보이게 (보통 끔 — 팔이 번쩍이면 거슬림)</summary>
    public bool ShowForSelf;

    public static HitFlash For(GameObject go)
    {
        if (go == null) return null;
        var f = go.GetComponent<HitFlash>();
        return f != null ? f : go.AddComponent<HitFlash>();
    }

    void Awake()
    {
        _rig = GetComponentInChildren<FPArmRig>(true);
        _no = GetComponent<NetworkObject>();
        _tpv = GetComponent<ThirdPersonView>();
    }

    void OnDisable() { Clear(_body); Clear(_head); }
    void OnDestroy()
    {
        Clear(_body); Clear(_head);
        if (_body.Mat != null) Destroy(_body.Mat);
        if (_head.Mat != null) Destroy(_head.Mat);
    }

    // ══════════════════ 재생 ══════════════════

    /// <param name="strength">0~1 — 색의 진하기 (도트 피해는 옅게)</param>
    public void Play(Part part, Color color, float duration = 0.14f, float strength = 1f)
    {
        if (!isActiveAndEnabled) return;
        if (Style != FlashStyle.Tint) Ready();      // 셰이더가 없으면 덮개만 빠지고 색 바꾸기는 그대로

        bool self = _no != null && _no.IsValid && _no.HasInputAuthority;
        if (self && !ShowForSelf && (_tpv == null || !_tpv.IsThirdPerson)) return;

        color.a *= Mathf.Clamp01(strength);

        // 머리가 따로 없으면 (분신 등) 머리 맞아도 몸 전체
        bool hasHead = Collect();
        if (part == Part.Head && !hasHead) part = Part.All;

        if (part != Part.Head) Begin(_body, color, duration);
        if (part != Part.Body) Begin(_head, color, duration);
    }

    bool Ready()
    {
        if (_shader == null) _shader = Shader.Find("Hidden/Game/HitFlash");
        if (_shader != null) return true;
        if (!_warned) { _warned = true; Debug.LogWarning("[HitFlash] 셰이더 Hidden/Game/HitFlash 없음 — Vfx/Resources/HitFlash.shader 확인"); }
        return false;
    }

    bool UseOverlay => Style != FlashStyle.Tint && _shader != null;
    bool UseTint => Style != FlashStyle.Overlay;

    void Begin(Group g, Color c, float dur)
    {
        if (g.Rs.Count == 0) return;
        g.Color = c;
        g.T = 0f;
        g.Dur = Mathf.Max(0.03f, dur);

        if (!g.On)
        {
            if (UseTint) CollectTints(g);           // 덮개를 얹기 전에 — 원래 재질만
            if (UseOverlay)
            {
                if (g.Mat == null) g.Mat = new Material(_shader) { hideFlags = HideFlags.DontSave };
                foreach (var r in g.Rs) Add(r, g.Mat);
            }
            g.On = true;
        }
        Apply(g, 1f);
    }

    void CollectTints(Group g)
    {
        g.Tints.Clear();
        foreach (var r in g.Rs)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name.StartsWith("Hidden/")) continue;   // 아웃라인 · 덮개 제외
                r.GetPropertyBlock(_mpb, i);
                bool empty = _mpb.isEmpty;
                foreach (int p in TintProps)
                {
                    if (!m.HasProperty(p)) continue;
                    var orig = _mpb.isEmpty || !_mpb.HasColor(p) ? m.GetColor(p) : _mpb.GetColor(p);
                    g.Tints.Add(new TintRec { R = r, Index = i, Prop = p, Orig = orig, WasEmpty = empty });
                }
            }
        }
    }

    /// <summary>k = 1 → 0 (시작 → 끝)</summary>
    void Apply(Group g, float k)
    {
        if (UseOverlay && g.Mat != null)
        {
            var c = g.Color;
            c.a *= k * k * (Style == FlashStyle.Both ? OverlayInBoth : 1f);   // 처음 확 · 빠르게 빠짐
            g.Mat.SetColor(IdColor, c);
        }

        if (UseTint && g.Tints.Count > 0)
        {
            float t = TintAmount * g.Color.a * k * k;
            for (int i = 0; i < g.Tints.Count; i++)
            {
                var rec = g.Tints[i];
                if (rec.R == null) continue;
                var col = Color.Lerp(rec.Orig, new Color(g.Color.r, g.Color.g, g.Color.b, rec.Orig.a), t);
                rec.R.GetPropertyBlock(_mpb, rec.Index);
                _mpb.SetColor(rec.Prop, col);
                rec.R.SetPropertyBlock(_mpb, rec.Index);
            }
        }
    }

    void RestoreTints(Group g)
    {
        // 같은 칸에 프로퍼티가 여러 개일 수 있음 — 원래 비어 있던 칸은 통째로 비우고, 아니면 원래 색으로
        for (int i = 0; i < g.Tints.Count; i++)
        {
            var rec = g.Tints[i];
            if (rec.R == null) continue;
            if (rec.WasEmpty) { rec.R.SetPropertyBlock(null, rec.Index); continue; }
            rec.R.GetPropertyBlock(_mpb, rec.Index);
            _mpb.SetColor(rec.Prop, rec.Orig);
            rec.R.SetPropertyBlock(_mpb, rec.Index);
        }
        g.Tints.Clear();
    }

    void Update()
    {
        Tick(_body);
        Tick(_head);
    }

    void Tick(Group g)
    {
        if (!g.On) return;
        g.T += Time.deltaTime;
        float k = 1f - Mathf.Clamp01(g.T / g.Dur);
        if (k <= 0f) { Clear(g); return; }
        Apply(g, k);
    }

    void Clear(Group g)
    {
        if (!g.On) return;
        if (g.Mat != null) foreach (var r in g.Rs) Remove(r, g.Mat);
        RestoreTints(g);
        g.On = false;
    }

    // ══════════════════ 렌더러 모으기 ══════════════════

    static readonly List<Renderer> _tmp = new();
    readonly HashSet<Transform> _skip = new();

    /// <summary>머리 · 몸 렌더러 다시 모음 (변화 파츠 · 무기가 바뀌었을 수 있어서 매번). 머리가 있으면 true</summary>
    bool Collect()
    {
        // 번쩍이는 중이면 목록을 바꾸지 않음 (덮개를 뺄 대상이 달라지지 않게)
        if (_body.On || _head.On) return _head.Rs.Count > 0;

        _body.Rs.Clear();
        _head.Rs.Clear();
        _skip.Clear();

        // 무기 모델은 제외
        if (_rig != null)
            for (int i = 0; i < _rig.PartCount; i++)
            {
                var wt = _rig.GetPartTransform(i);
                if (wt != null) _skip.Add(wt);
            }

        Transform head = _rig != null ? _rig.Head : null;
        GetComponentsInChildren(false, _tmp);
        foreach (var r in _tmp)
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled) continue;
            if (Under(r.transform, _skip)) continue;
            if (head != null && r.transform.IsChildOf(head)) _head.Rs.Add(r);
            else _body.Rs.Add(r);
        }
        return _head.Rs.Count > 0;
    }

    static bool Under(Transform t, HashSet<Transform> roots)
    {
        for (var p = t; p != null; p = p.parent)
            if (roots.Contains(p)) return true;
        return false;
    }

    // 재질 배열 끝에 덮개를 붙이고, 뺄 때는 우리 것만 (아웃라인 등 다른 코드가 붙인 건 그대로)
    static void Add(Renderer r, Material m)
    {
        if (r == null) return;
        var cur = r.sharedMaterials;
        foreach (var x in cur) if (x == m) return;
        var next = new Material[cur.Length + 1];
        cur.CopyTo(next, 0);
        next[cur.Length] = m;
        r.sharedMaterials = next;
    }

    static void Remove(Renderer r, Material m)
    {
        if (r == null) return;
        var cur = r.sharedMaterials;
        int n = 0;
        foreach (var x in cur) if (x != m) n++;
        if (n == cur.Length) return;
        var next = new Material[n];
        int i = 0;
        foreach (var x in cur) if (x != m) next[i++] = x;
        r.sharedMaterials = next;
    }
}
