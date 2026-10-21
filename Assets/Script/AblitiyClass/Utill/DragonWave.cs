using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 동양 용 — 코드로 움직이는 애니메이션 전부 (RigAnimator 필요 없음).
///   자세: 서 있게 리깅된 모델을 시작할 때 '누워서 떠 있는' 자세로 (척추 · 목은 앞으로, 꼬리는 뒤로 일자, 다리는 아래로)
///   Idle: 머리 → 꼬리로 흘러가는 위아래 물결 + 둥실 떠 있기 / Walk: 같은 물결 MoveSpeedMul배
///   Attack: 고개를 들며 입을 벌림 → 앞으로 덮치며 BiteHitAt초에 '탁' 닫힘 → 제자리
///   Flying: 물결 빠르게 + 머리를 살짝 듦
/// 용 모델 루트(프리팹 맨 위)에 붙임. 뼈는 이름으로 찾음 (dragon_rig.py의 이름).
/// 신호는 PlayerAlter가 IAlterPartAnim으로 줌 (TransformAbility의 AttackAnim / FlyAnim 이름 그대로).
/// 인스펙터 ⋮ → '눕힌 자세 미리보기'로 에디터에서도 자세 확인 (Ctrl+Z로 되돌림, 저장해도 괜찮음).
/// </summary>
[DefaultExecutionOrder(600)]
public class DragonWave : MonoBehaviour, IAlterPartAnim
{
    [Header("자세 — 누워서 떠 있기")]
    [Tooltip("시작할 때 서 있는 모델을 눕힘. 끄면 모델 기본 자세 그대로")]
    public bool LieFlat = true;
    [Tooltip("목을 얼마나 들지 (°, +면 위로)")]
    public float NeckRaise = 6f;
    [Tooltip("꼬리를 얼마나 내릴지 (°, +면 아래로)")]
    public float TailDroop = 3f;
    [Tooltip("다리 방향 (바깥 · 위 · 앞). 1 = 윗다리 · 2 = 아랫다리")]
    public Vector3 FrontLeg1 = new(0.35f, -1f, -0.35f);
    public Vector3 FrontLeg2 = new(0.1f, -1f, 0.4f);
    public Vector3 HindLeg1 = new(0.35f, -1f, -0.6f);
    public Vector3 HindLeg2 = new(0.1f, -0.5f, -1f);

    [Header("떠 있기")]
    [Tooltip("위아래 둥실 (m)")]
    public float HoverAmp = 0.08f;
    [Tooltip("물결 대비 둥실 빠르기")]
    public float HoverFreqMul = 0.5f;

    [Header("속도")]
    [Tooltip("1초에 몇 번 출렁이는지 (Idle)")]
    public float Frequency = 0.45f;
    [Tooltip("움직일 때 몇 배 빠르게 (Walk = Idle × 2)")]
    public float MoveSpeedMul = 2f;
    [Tooltip("움직일 때 진폭 배율")]
    public float MoveAmpMul = 1.15f;
    [Tooltip("다음 뼈로 갈수록 늦어지는 정도 (라디안) — 클수록 물결이 촘촘")]
    public float PhasePerBone = 0.55f;
    [Tooltip("켜면 물결이 머리 → 꼬리로 흐름 (헤엄치듯). 끄면 골반에서 양쪽으로")]
    public bool WaveFromHead = true;
    [Tooltip("이 속도(m/초) 이상이면 Walk")]
    public float MoveThreshold = 0.6f;

    [Header("위아래 진폭 (°)")]
    public float SpineAmp = 3f;
    public float NeckAmp = 5f;
    [Tooltip("꼬리 시작 → 끝 진폭")]
    public Vector2 TailAmp = new(4f, 14f);
    [Tooltip("머리를 몸 출렁임 반대로 얼마나 잡아 줄지 (0 = 같이 흔들림, 1 = 머리 고정)")]
    [Range(0f, 1f)] public float HeadStabilize = 0.6f;

    [Header("좌우 (°) — 살짝 섞으면 덜 기계적")]
    public float SideAmp = 1.5f;
    public float SideFreqMul = 0.5f;

    [Header("수염 · 턱 · 배 · 다리")]
    public float WhiskerAmp = 12f;
    public float WhiskerLag = 1.2f;
    public float JawAmp = 3f;
    [Tooltip("배 숨쉬기 — 크기 변화 비율")]
    public float BellyBreath = 0.04f;
    [Tooltip("다리 앞뒤 흔들림 (°) — 앞다리 · 뒷다리 반대 박자")]
    public float LegAmp = 6f;

    [Header("물어뜯기 (Attack)")]
    public string AttackState = "Attack";
    [Tooltip("전체 길이 (초) — 변화 기본 공격으로 재생되면 공격의 길이가 대신 쓰임 (공속 반영)")]
    public float BiteTime = 0.6f;
    [Tooltip("입이 '탁' 닫히는 순간 (초) — 변화 기본 공격으로 재생되면 공격의 판정 시작 순간이 대신 쓰임")]
    public float BiteHitAt = 0.3f;
    [Tooltip("입 벌리는 각 (°)")]
    public float JawOpen = 40f;
    [Tooltip("준비 때 고개를 드는 각 (°)")]
    public float RearUp = 18f;
    [Tooltip("물 때 고개를 숙이는 각 (°)")]
    public float Strike = 14f;
    [Tooltip("물 때 몸이 앞으로 나가는 거리 (m)")]
    public float Lunge = 0.6f;
    [Tooltip("공격 중 물결 비율")]
    [Range(0f, 1f)] public float AttackWeight = 0.3f;

    [Header("비행 (Flying)")]
    public string FlyState = "Flying";
    public float FlyFreqMul = 1.4f;
    [Tooltip("날 때 머리를 드는 각 (°)")]
    public float FlyPitch = 6f;

    [Header("기타")]
    [Tooltip("같은 오브젝트의 RigAnimator를 끔 (기본 자세를 덮어써서 눕힌 자세가 풀리지 않게)")]
    public bool DisableRigAnimator = true;

    class Chain
    {
        public readonly List<Transform> Bones = new();
        public readonly List<Quaternion> Rest = new();
    }

    readonly Chain _spine = new(), _neck = new(), _tail = new(),
                   _whL = new(), _whR = new(), _wh2L = new(), _wh2R = new(),
                   _front = new(), _hind = new(), _other = new();
    Transform _pelvis, _head, _jaw, _belly;
    readonly List<Transform> _frontL = new(), _frontR = new(), _hindL = new(), _hindR = new();
    Quaternion _pelvisRest, _headRest, _jawRest;
    Vector3 _pelvisPos, _bellyScale;
    Vector3 _fwdL = Vector3.forward;        // 이 오브젝트 기준 용의 앞 (머리 쪽, 수평)
    bool _bound, _ready;

    float _t, _speedMul = 1f, _weight = 1f, _flyW;
    float _selfSpeed, _extSpeedTime = -10f;
    bool _extMoving, _flying, _hasLast;
    Vector3 _lastPos;
    float _biteT = -1f;
    float _biteLen = 0.6f, _biteHit = 0.3f;     // 이번 물어뜯기 길이 · 닫히는 순간 (공격 모션에서 받거나 BiteTime · BiteHitAt)

    // ══════════════════ 준비 ══════════════════

    void Awake()
    {
        if (DisableRigAnimator)
        {
            var ra = GetComponent<RigAnimator>();
            if (ra != null) ra.enabled = false;
        }

        Bind();
        if (LieFlat) Straighten();
        CaptureRest();
        _ready = true;
    }

    void Bind()
    {
        foreach (var c in new[] { _spine, _neck, _tail, _whL, _whR, _wh2L, _wh2R, _front, _hind, _other })
        {
            c.Bones.Clear();
            c.Rest.Clear();
        }
        _frontL.Clear(); _frontR.Clear(); _hindL.Clear(); _hindR.Clear();
        _pelvis = _head = _jaw = _belly = null;

        var all = GetComponentsInChildren<Transform>(true);
        Collect(all, "Spine_", _spine);
        Collect(all, "Neck_", _neck);
        Collect(all, "Tail_", _tail);
        Collect(all, "Whisker_L_", _whL);
        Collect(all, "Whisker_R_", _whR);
        Collect(all, "Whisker2_L_", _wh2L);
        Collect(all, "Whisker2_R_", _wh2R);
        CollectList(all, "FrontLeg_L_", _frontL);
        CollectList(all, "FrontLeg_R_", _frontR);
        CollectList(all, "HindLeg_L_", _hindL);
        CollectList(all, "HindLeg_R_", _hindR);

        foreach (var t in all)
        {
            switch (t.name)
            {
                case "Pelvis": _pelvis = t; break;
                case "Head": _head = t; break;
                case "Jaw": _jaw = t; break;
                case "Belly": _belly = t; break;
            }
        }

        // 다리 전체 (눕힌 자세의 기본값 저장용) + 흔들 뼈 (윗다리)
        foreach (var l in new[] { _frontL, _frontR, _hindL, _hindR })
            foreach (var t in l) Add(_other, t);
        if (_frontL.Count > 0) Add(_front, _frontL[0]);
        if (_frontR.Count > 0) Add(_front, _frontR[0]);
        if (_hindL.Count > 0) Add(_hind, _hindL[0]);
        if (_hindR.Count > 0) Add(_hind, _hindR[0]);

        // 용의 앞 = 골반 → 턱(없으면 머리) 수평 방향
        Transform front = _jaw != null ? _jaw : _head;
        if (_pelvis != null && front != null)
        {
            Vector3 d = transform.InverseTransformDirection(front.position - _pelvis.position);
            d.y = 0f;
            if (d.sqrMagnitude > 1e-8f) _fwdL = d.normalized;
        }
        if (Application.isPlaying && Vector3.Angle(_fwdL, Vector3.forward) > 30f)
            Debug.LogWarning($"[DragonWave] 용 머리가 프리팹 앞(+Z)이 아님 — AlterData 회전 Y를 " +
                             $"{-Vector3.SignedAngle(Vector3.forward, _fwdL, Vector3.up):0}°로 하면 플레이어 앞을 봄", this);

        if (_spine.Bones.Count + _tail.Bones.Count == 0)
            Debug.LogWarning("[DragonWave] Spine_ · Tail_ 뼈를 못 찾음 — 블렌더 스크립트의 뼈 이름인지 확인", this);
        _bound = true;
    }

    static void Add(Chain c, Transform t)
    {
        c.Bones.Add(t);
        c.Rest.Add(t.localRotation);
    }

    static void Collect(Transform[] all, string prefix, Chain c)
    {
        var list = new List<Transform>();
        CollectList(all, prefix, list);
        foreach (var t in list) Add(c, t);
    }

    static void CollectList(Transform[] all, string prefix, List<Transform> outList)
    {
        var list = new List<(int n, Transform t)>();
        foreach (var t in all)
            if (t.name.StartsWith(prefix) && int.TryParse(t.name.Substring(prefix.Length), out int n)) list.Add((n, t));
        list.Sort((a, b) => a.n.CompareTo(b.n));
        foreach (var (_, t) in list) outList.Add(t);
    }

    void CaptureRest()
    {
        foreach (var c in new[] { _spine, _neck, _tail, _whL, _whR, _wh2L, _wh2R, _front, _hind, _other })
            for (int i = 0; i < c.Bones.Count; i++) c.Rest[i] = c.Bones[i].localRotation;
        if (_pelvis != null) { _pelvisRest = _pelvis.localRotation; _pelvisPos = _pelvis.localPosition; }
        if (_head != null) _headRest = _head.localRotation;
        if (_jaw != null) _jawRest = _jaw.localRotation;
        if (_belly != null) _bellyScale = _belly.localScale;
    }

    Vector3 F => transform.TransformDirection(_fwdL);
    Vector3 U => transform.up;
    Vector3 R => Vector3.Cross(U, F).normalized;

    // ══════════════════ 눕히기 ══════════════════

    /// <summary>척추 · 목은 앞으로, 꼬리는 뒤로 일자, 다리는 아래로. 머리 · 발 방향은 원래대로. 여러 번 해도 같은 결과</summary>
    void Straighten()
    {
        Vector3 f = F, u = U, r = R;
        Quaternion root = transform.rotation;
        Quaternion headRel = _head != null ? Quaternion.Inverse(root) * _head.rotation : Quaternion.identity;
        var feet = new List<(Transform t, Quaternion rel)>();
        foreach (var l in new[] { _frontL, _frontR, _hindL, _hindR })
            if (l.Count > 0) feet.Add((l[^1], Quaternion.Inverse(root) * l[^1].rotation));

        // 척추 → 목 → 머리
        var body = new List<Transform>(_spine.Bones);
        body.AddRange(_neck.Bones);
        if (_head != null) body.Add(_head);
        Vector3 neckDir = Quaternion.AngleAxis(-NeckRaise, r) * f;
        for (int i = 0; i + 1 < body.Count; i++)
            Aim(body[i], body[i + 1].position, i < _spine.Bones.Count ? f : neckDir);

        if (_head != null) _head.rotation = root * headRel;

        // 꼬리
        Vector3 tailDir = Quaternion.AngleAxis(-TailDroop, r) * -f;
        for (int i = 0; i + 1 < _tail.Bones.Count; i++)
            Aim(_tail.Bones[i], _tail.Bones[i + 1].position, tailDir);

        // 다리
        LegAim(_frontL, FrontLeg1, FrontLeg2, f, u, r);
        LegAim(_frontR, FrontLeg1, FrontLeg2, f, u, r);
        LegAim(_hindL, HindLeg1, HindLeg2, f, u, r);
        LegAim(_hindR, HindLeg1, HindLeg2, f, u, r);
        foreach (var (t, rel) in feet) t.rotation = root * rel;
    }

    void LegAim(List<Transform> leg, Vector3 d1, Vector3 d2, Vector3 f, Vector3 u, Vector3 r)
    {
        if (leg.Count < 2 || _pelvis == null) return;
        float side = Mathf.Sign(Vector3.Dot(leg[0].position - _pelvis.position, r));
        if (side == 0f) side = 1f;
        Vector3 Dir(Vector3 v) => (r * (v.x * side) + u * v.y + f * v.z).normalized;
        Aim(leg[0], leg[1].position, Dir(d1));
        if (leg.Count >= 3) Aim(leg[1], leg[2].position, Dir(d2));
    }

    static void Aim(Transform b, Vector3 childPos, Vector3 dir)
    {
        Vector3 cur = childPos - b.position;
        if (cur.sqrMagnitude < 1e-10f || dir.sqrMagnitude < 1e-10f) return;
        b.rotation = Quaternion.FromToRotation(cur, dir) * b.rotation;
    }

    [ContextMenu("눕힌 자세 미리보기 (Ctrl+Z로 되돌림)")]
    void PreviewFlat()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Dragon Lie Flat");
#endif
        Bind();
        Straighten();
    }

    // ══════════════════ 신호 (PlayerAlter) ══════════════════

    public void SetMove(bool moving, float speed)
    {
        _extMoving = moving;
        _extSpeedTime = Time.time;
    }

    public bool PlayState(string state, float hold)
    {
        if (state == AttackState) { StartBite(BiteTime, BiteHitAt); return true; }
        if (state == FlyState) { _flying = true; return true; }
        return false;
    }

    public void ReleaseState() => _flying = false;

    /// <summary>변화 기본 공격 — 공격 판정 시간에 맞춰 입이 닫힘 (BiteTime · BiteHitAt 대신 공격의 값)</summary>
    public void PlayAttack(string state, float duration, float hitAt)
    {
        if (state != AttackState) { PlayState(state, -1f); return; }
        StartBite(duration, hitAt);
    }

    void StartBite(float len, float hit)
    {
        _biteLen = Mathf.Max(0.15f, len);
        _biteHit = Mathf.Clamp(hit, 0.05f, _biteLen - 0.05f);
        _biteT = 0f;
    }

    /// <summary>테스트용 — 인스펙터 ⋮ 메뉴 (플레이 중)</summary>
    [ContextMenu("물어뜯기 테스트")]
    void TestBite()
    {
        if (Application.isPlaying) { StartBite(BiteTime, BiteHitAt); return; }
        EditorPreview(BiteTime + 0.3f, true);       // 플레이 전(프리팹 편집 등)에도 재생 후 원래 자세로
    }

    [ContextMenu("물결 미리보기 (4초)")]
    void TestWave()
    {
        if (Application.isPlaying) return;
        EditorPreview(4f, false);
    }

#if UNITY_EDITOR
    Transform[] _pvT;
    Vector3[] _pvPos;
    Quaternion[] _pvRot;
    Vector3[] _pvScale;
    double _pvLast, _pvEnd;
#endif

    /// <summary>에디터 — 잠깐 움직여 보고 원래 자세(미리보기 전)로 되돌림. 프리팹은 바뀌지 않음</summary>
    void EditorPreview(float seconds, bool bite)
    {
#if UNITY_EDITOR
        if (_pvT != null) EndPreview();
        _pvT = GetComponentsInChildren<Transform>(true);
        _pvPos = new Vector3[_pvT.Length];
        _pvRot = new Quaternion[_pvT.Length];
        _pvScale = new Vector3[_pvT.Length];
        for (int i = 0; i < _pvT.Length; i++)
        {
            _pvPos[i] = _pvT[i].localPosition;
            _pvRot[i] = _pvT[i].localRotation;
            _pvScale[i] = _pvT[i].localScale;
        }

        Bind();
        if (LieFlat) Straighten();
        CaptureRest();
        _ready = true;
        _t = 0f; _weight = 1f; _speedMul = 1f; _flyW = 0f; _flying = false;
        _biteT = -1f;
        if (bite) StartBite(BiteTime, BiteHitAt);

        _pvLast = UnityEditor.EditorApplication.timeSinceStartup;
        _pvEnd = _pvLast + seconds;
        UnityEditor.EditorApplication.update -= PreviewStep;
        UnityEditor.EditorApplication.update += PreviewStep;
#endif
    }

#if UNITY_EDITOR
    void PreviewStep()
    {
        if (this == null) { UnityEditor.EditorApplication.update -= PreviewStep; return; }
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        float dt = Mathf.Min(0.05f, (float)(now - _pvLast));
        _pvLast = now;
        Animate(dt, false);
        UnityEditor.SceneView.RepaintAll();
        if (now >= _pvEnd) EndPreview();
    }

    void EndPreview()
    {
        UnityEditor.EditorApplication.update -= PreviewStep;
        if (_pvT != null)
            for (int i = 0; i < _pvT.Length; i++)
                if (_pvT[i] != null) _pvT[i].SetLocalPositionAndRotation(_pvPos[i], _pvRot[i]);
        if (_pvT != null)
            for (int i = 0; i < _pvT.Length; i++)
                if (_pvT[i] != null) _pvT[i].localScale = _pvScale[i];
        _pvT = null;
        _ready = false;
        _biteT = -1f;
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    // ══════════════════ 매 프레임 ══════════════════

    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        Animate(Time.deltaTime, true);
    }

    void Animate(float dt, bool playing)
    {
        if (!_ready || !_bound) return;
        if (dt <= 0f) return;

        // 이동 — PlayerAlter 신호가 있으면 그것, 없으면 내 위치 변화로
        Vector3 p = transform.position;
        if (playing && _hasLast)
        {
            Vector3 d = p - _lastPos;
            d.y = 0f;
            float s = d.magnitude / dt;
            if (s > 60f) s = 0f;
            _selfSpeed = Mathf.Lerp(_selfSpeed, s, 1f - Mathf.Exp(-10f * dt));
        }
        _lastPos = p;
        _hasLast = true;
        bool moving = playing && (Time.time - _extSpeedTime < 0.3f ? _extMoving : _selfSpeed > MoveThreshold);

        _flyW = Mathf.MoveTowards(_flyW, _flying ? 1f : 0f, dt * 3f);
        float target = (moving ? MoveSpeedMul : 1f) * Mathf.Lerp(1f, FlyFreqMul, _flyW);
        _speedMul = Mathf.Lerp(_speedMul, target, 1f - Mathf.Exp(-4f * dt));

        bool biting = _biteT >= 0f;
        _weight = Mathf.MoveTowards(_weight, biting ? AttackWeight : 1f, dt * 6f);

        _t += dt * Frequency * _speedMul * Mathf.PI * 2f;
        float movek = Mathf.Clamp01((_speedMul - 1f) / Mathf.Max(0.01f, MoveSpeedMul - 1f));
        float amp = Mathf.Lerp(1f, MoveAmpMul, movek) * _weight;

        // 물어뜯기 곡선
        float neckPitch = 0f, jawOpen = 0f, lunge = 0f;
        if (biting)
        {
            _biteT += dt;
            Bite(_biteT, out neckPitch, out jawOpen, out lunge);
            if (_biteT >= _biteLen) _biteT = -1f;
        }

        Vector3 f = F, u = U, r = R;

        // 기본 자세로 되돌리고 시작 (누적 방지)
        ResetChain(_spine); ResetChain(_neck); ResetChain(_tail); ResetChain(_whL); ResetChain(_whR); ResetChain(_wh2L); ResetChain(_wh2R); ResetChain(_other);
        if (_head != null) _head.localRotation = _headRest;
        if (_jaw != null) _jaw.localRotation = _jawRest;

        // 골반 — 둥실 · 덮치기 · 비행 때 머리 들기
        if (_pelvis != null)
        {
            _pelvis.localRotation = _pelvisRest;
            Vector3 off = u * (Mathf.Sin(_t * HoverFreqMul) * HoverAmp) + f * lunge;
            _pelvis.localPosition = _pelvisPos + (_pelvis.parent != null ? _pelvis.parent.InverseTransformVector(off) : off);
            if (_flyW > 0f) _pelvis.rotation = Quaternion.AngleAxis(-FlyPitch * _flyW, r) * _pelvis.rotation;
        }

        // 물결 순서 — 머리부터 꼬리로 (또는 골반에서 양쪽으로)
        int nN = _neck.Bones.Count, nS = _spine.Bones.Count;
        float bodyBend = 0f, dummy = 0f;

        for (int i = 0; i < nS; i++)
            Bend(_spine.Bones[i], SpineAmp * amp, WaveFromHead ? nN + (nS - 1 - i) : i, r, u, ref bodyBend);
        for (int i = 0; i < nN; i++)
        {
            var b = _neck.Bones[i];
            Bend(b, NeckAmp * amp, WaveFromHead ? nN - 1 - i : nS + i, r, u, ref bodyBend);
            if (neckPitch != 0f) b.rotation = Quaternion.AngleAxis(neckPitch / nN, r) * b.rotation;
        }

        // 머리 — 몸이 출렁인 만큼 반대로 + 물 때 입이 앞을 향하게 + 입 벌릴 때 윗턱이 살짝 들림
        if (_head != null)
            _head.rotation = Quaternion.AngleAxis(-bodyBend * HeadStabilize - neckPitch * 0.6f - jawOpen * 0.25f, r) * _head.rotation;

        // 꼬리 — 끝으로 갈수록 크게
        int tailStart = WaveFromHead ? nN + nS : 1;
        for (int i = 0; i < _tail.Bones.Count; i++)
        {
            float k = _tail.Bones.Count > 1 ? i / (float)(_tail.Bones.Count - 1) : 0f;
            Bend(_tail.Bones[i], Mathf.Lerp(TailAmp.x, TailAmp.y, k) * amp, tailStart + i, r, u, ref dummy);
        }

        // 수염 — 늦게 따라 흔들림
        float wh = WaveFromHead ? 0f : nS + nN;
        float lag = WhiskerLag / Mathf.Max(0.01f, PhasePerBone);
        Whisker(_whL, WhiskerAmp * amp, wh + lag, r, u);
        Whisker(_whR, WhiskerAmp * amp, wh + lag + 0.4f, r, u);
        Whisker(_wh2L, WhiskerAmp * 0.6f * amp, wh + lag + 0.9f, r, u);
        Whisker(_wh2R, WhiskerAmp * 0.6f * amp, wh + lag + 1.3f, r, u);

        // 다리 — 앞 · 뒤 반대 박자
        foreach (var b in _front.Bones) Swing(b, LegAmp * amp, 0f, r);
        foreach (var b in _hind.Bones) Swing(b, LegAmp * amp, Mathf.PI, r);

        // 턱 — 숨쉬듯 살짝 + 물어뜯기
        if (_jaw != null)
        {
            float j = (Mathf.Sin(_t * 0.5f) * 0.5f + 0.5f) * JawAmp * _weight + jawOpen;
            _jaw.rotation = Quaternion.AngleAxis(j, r) * _jaw.rotation;
        }

        // 배 — 숨쉬기
        if (_belly != null)
            _belly.localScale = _bellyScale * (1f + Mathf.Sin(_t * 0.5f) * BellyBreath * _weight);
    }

    /// <summary>물어뜯기 — 준비(고개 들며 입 벌림) → 덮치기 → BiteHitAt에 닫힘 → 복귀</summary>
    void Bite(float t, out float neckPitch, out float jawOpen, out float lunge)
    {
        float hit = _biteHit;
        float prep = hit * 0.55f;
        const float snap = 0.07f;

        if (t < prep)
        {
            float k = Smooth(t / prep);
            neckPitch = -RearUp * k;
            jawOpen = JawOpen * k;
            lunge = -0.25f * Lunge * k;
        }
        else if (t < hit)
        {
            float k = Smooth((t - prep) / (hit - prep));
            neckPitch = Mathf.Lerp(-RearUp, Strike, k);
            jawOpen = JawOpen;
            lunge = Mathf.Lerp(-0.25f * Lunge, Lunge, k);
        }
        else
        {
            float k = Smooth((t - hit) / Mathf.Max(0.01f, _biteLen - hit));
            neckPitch = Mathf.Lerp(Strike, 0f, k);
            jawOpen = JawOpen * (1f - Mathf.Clamp01((t - hit) / snap));
            lunge = Mathf.Lerp(Lunge, 0f, k);
        }
    }

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    static void ResetChain(Chain c)
    {
        for (int i = 0; i < c.Bones.Count; i++)
            if (c.Bones[i] != null) c.Bones[i].localRotation = c.Rest[i];
    }

    void Bend(Transform b, float amp, float order, Vector3 side, Vector3 up, ref float total)
    {
        if (b == null) return;
        float ph = _t - order * PhasePerBone;
        float a = Mathf.Sin(ph) * amp;
        float s = Mathf.Sin(ph * SideFreqMul + 1.3f) * SideAmp * _weight;
        total += a;
        b.rotation = Quaternion.AngleAxis(a, side) * Quaternion.AngleAxis(s, up) * b.rotation;
    }

    void Whisker(Chain c, float amp, float order, Vector3 side, Vector3 up)
    {
        float dummy = 0f;
        for (int i = 0; i < c.Bones.Count; i++) Bend(c.Bones[i], amp, order + i * 0.8f, side, up, ref dummy);
    }

    void Swing(Transform b, float amp, float offset, Vector3 side)
    {
        if (b == null) return;
        b.rotation = Quaternion.AngleAxis(Mathf.Sin(_t + offset) * amp, side) * b.rotation;
    }
}
