using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 변화(파츠) — 네트워크 배열에 ID를 들고, 모든 클라이언트가 같은 파츠를 붙임.
/// 스탯은 SA(소유자)에서만. 어디서 불러도 됨 — SA가 아니면 RPC로 보냄.
/// </summary>
[DefaultExecutionOrder(9000)]    // 카메라·팔·몸이 자리를 잡은 뒤 탄 높이를 더함 (3인칭 카메라(10000)보다는 먼저)
public class PlayerAlter : NetworkBehaviour
{
    public const int MaxAlters = 8;

    [SerializeField] private AlterDatabase database;
    [Tooltip("비우면 자식에서 찾음")]
    [SerializeField] private FPArmRig armRig;

    [Header("탄 높이 (말 등)")]
    [Tooltip("탔을 때 위로 올릴 것들. 비우면 자동 — CameraRoot(카메라 · 팔 · 무기) + FPArmRig의 Body · Head.\n" +
             "3인칭 몸 모델이 따로 있으면 그것도 넣기. 말 같은 파츠는 Attach = Root로 두면 안 올라감")]
    [SerializeField] private Transform[] liftTargets;
    [Tooltip("올라타고 내리는 빠르기 (m/초). 0이면 즉시")]
    [SerializeField] private float liftSpeed = 6f;

    [Networked, Capacity(MaxAlters), OnChangedRender(nameof(OnAltersChanged))]
    public NetworkArray<int> Alters { get; }

    private readonly List<GameObject> _spawned = new();
    private readonly List<Renderer> _renderers = new();
    private readonly List<RigAnimator> _rigAnims = new();
    private readonly List<(GameObject root, IAlterPartAnim anim)> _partAnims = new();   // 코드 애니메이션 파츠 (용 등)
    private readonly List<(Transform box, AlterData a)> _hitboxes = new();              // 변신 피격 범위
    private static readonly HashSet<int> _warnedLift = new();     // 파츠의 애니메이션 (말 걷기 등)
    private Vector3 _lastPos;
    private float _moveSpeed;
    private Vector3 _vel;               // 부드럽게 거른 이동 속도 (방향용)

    // 이동 방향을 바라보는 파츠 (말 등)
    private readonly List<(Transform t, AlterData a)> _facers = new();
    private Quaternion _faceRot = Quaternion.identity;
    private bool _faceInit;
    private PlayerStats _stats;
    private PlayerHealth _health;
    private Transform _weaponParent;
    private bool _wasDead;

    // 탈것 높이 — 피격 판정(CharacterController)을 늘려서 올라탄 몸까지 덮음
    private CharacterController _cc;
    private FPSCamera _cam;
    private ThirdPersonView _tpv;
    private float _ccHeight, _ccCenterY;
    private float _lift;            // 목표 높이
    private float _liftShown;       // 지금 보이는 높이 (부드럽게 따라감)
    private Transform[] _liftResolved;
    private Vector3[] _liftBase, _liftLastSet;
    private bool _liftLogged;
    private bool _bodyViaRig;
    private Vector3 _bodyRest, _headRest;
    private bool _bodyForced;

    public AlterDatabase Database => database;

    /// <summary>지금 올라타 있는 높이 (m)</summary>
    public float Lift => _lift;

    /// <summary>붙은 변화들의 점프 세기 배율 (곱)</summary>
    public float JumpMultiplier
    {
        get
        {
            if (database == null || Object == null || !Object.IsValid) return 1f;
            float m = 1f;
            for (int i = 0; i < MaxAlters; i++)
            {
                var a = database.Get(Alters[i]);
                if (a != null) m *= a.JumpMultiplier;
            }
            return m;
        }
    }

    public float FallGravityMultiplier
    {
        get
        {
            if (database == null || Object == null || !Object.IsValid) return 1f;
            float m = 1f;
            for (int i = 0; i < MaxAlters; i++)
            {
                var a = database.Get(Alters[i]);
                if (a != null) m *= a.FallGravity;
            }
            return m;
        }
    }

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _cc = GetComponent<CharacterController>();
        _cam = GetComponentInChildren<FPSCamera>(true);
        if (armRig == null) armRig = GetComponentInChildren<FPArmRig>(true);

        _tpv = GetComponent<ThirdPersonView>();
        if (_tpv != null) _tpv.Changed += OnViewChanged;

        if (_cc != null)
        {
            _ccHeight = _cc.height;
            _ccCenterY = _cc.center.y;
        }

        // 몸 · 머리 기본 위치 (탔을 때 이 위치 + 탄 높이로 고정)
        if (armRig != null)
        {
            if (armRig.Body != null) _bodyRest = armRig.Body.localPosition;
            if (armRig.Head != null) _headRest = armRig.Head.localPosition;
        }
        RenderPipelineManager.beginContextRendering += OnBeforeRender;

        Rebuild();
        if (HasStateAuthority) ApplyStats();
    }



    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_tpv != null) _tpv.Changed -= OnViewChanged;
        RenderPipelineManager.beginContextRendering -= OnBeforeRender;
        ClearVisuals();
        ApplyLift(0f);
        _liftShown = 0f;
        if (_liftResolved != null)
            for (int i = 0; i < _liftResolved.Length; i++)
                if (_liftResolved[i] != null) _liftResolved[i].localPosition = _liftBase[i];
        _liftResolved = null;
    }

    /// <summary>모든 클라이언트 — 몸·카메라를 올리고 피격 판정을 위로 늘림 (발 위치는 그대로)</summary>
    void ApplyLift(float lift)
    {
        if (Mathf.Approximately(lift, _lift)) return;
        _lift = lift;

        if (_cc != null)
        {
            float scaleY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
            float add = lift / scaleY;
            _cc.height = _ccHeight + add;
            var c = _cc.center;
            c.y = _ccCenterY + add * 0.5f;
            _cc.center = c;
        }

        // 카메라는 LateUpdate에서, 몸 · 머리는 FPArmRig.BodyLift로 (모션이 몸을 움직여도 항상 올라간 채로)
    }

    /// <summary>지금 붙은 변신 (없으면 null) — 모든 클라이언트</summary>
    public AlterData ActiveTransform
    {
        get
        {
            if (database == null || Object == null || !Object.IsValid) return null;
            for (int i = 0; i < MaxAlters; i++)
            {
                var a = database.Get(Alters[i]);
                if (a != null && a.IsTransform) return a;
            }
            return null;
        }
    }

    /// <summary>지금 붙은 변화 중 기본 공격을 바꾸는 것 (없으면 null) — 모든 클라이언트</summary>
    public AlterData ActiveAttack
    {
        get
        {
            if (database == null || Object == null || !Object.IsValid) return null;
            for (int i = 0; i < MaxAlters; i++)
            {
                var a = database.Get(Alters[i]);
                if (a != null && a.AttackStep != null) return a;
            }
            return null;
        }
    }

    /// <summary>이 슬롯 스킬이 변화 때문에 잠겼는가 — 랜덤 능력 슬롯은 안 잠김</summary>
    public bool LocksSkill(SkillSlot slot)
    {
        if (slot.IsRandom() || database == null || Object == null || !Object.IsValid) return false;
        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database.Get(Alters[i]);
            if (a != null && a.LockSkills) return true;
        }
        return false;
    }

    /// <summary>
    /// 변화 기본 공격의 모델 연출 — 공격 판정 시간에 맞춰서 (모든 클라이언트, PlayerAttack의 RPC_PlaySwing에서).
    /// AttackWave가 켜져 있으면 판정 순간에 쿵: 모든 화면 소리 · 흔들림, 권한자는 기절 · 밀치기 · 피해
    /// </summary>
    public void PlayAttackAnim(float speed)
    {
        var a = ActiveAttack;
        if (a == null) return;
        var step = a.AttackStep;
        float spd = Mathf.Max(0.05f, speed);
        float dur = step.Duration / spd;
        float hit = step.Duration * step.HitStart / spd;

        if (!string.IsNullOrEmpty(a.AttackAnim))
        {
            string prefix = $"Alter_{a.name}";
            foreach (var (root, pa) in _partAnims)
                if (root != null && root.name.StartsWith(prefix)) pa.PlayAttack(a.AttackAnim, dur, hit);
            foreach (var ra in _rigAnims)
                if (ra != null && ra.name.StartsWith(prefix)) ra.Play(a.AttackAnim);
        }

        var wave = a.AttackWave;
        if (wave != null && wave.Enabled)
        {
            var self = this;
            GameTimers.After(hit, () =>
            {
                if (self == null || self.Object == null || !self.Object.IsValid) return;
                if (self._health != null && self._health.IsDead) return;
                if (!self.Has(a)) return;                                    // 그 사이 변신이 풀림
                Vector3 c = Shockwave.Center(self.transform, wave.Forward);
                Shockwave.Feel(c, wave.Feel, self.transform);
                if (self.HasStateAuthority) Shockwave.Hit(self.GetComponent<AbilityRuntime>(), c, wave);
            });
        }
    }

    /// <summary>특정 변화의 파츠를 Idle/Walk 자동 전환으로 되돌림 (비행 끝 등)</summary>
    public void ReleasePartState(AlterData alter)
    {
        if (alter == null) return;
        foreach (var ra in _rigAnims)
            if (ra != null && ra.name.StartsWith($"Alter_{alter.name}")) ra.Release();
        foreach (var (root, pa) in _partAnims)
            if (root != null && root.name.StartsWith($"Alter_{alter.name}")) pa.ReleaseState();
    }

    /// <summary>t가 변화로 붙은 파츠(말 · 가면 등) 안에 있는가</summary>
    public bool IsPart(Transform t)
    {
        if (t == null) return false;
        foreach (var go in _spawned)
            if (go != null && t.IsChildOf(go.transform)) return true;
        return false;
    }

    // ══════════════════ 탄 높이 — 보이는 것 올리기 (모든 클라이언트) ══════════════════

    /// <summary>올릴 대상 자동 — CameraRoot + 몸 · 머리 (서로 겹치면 위쪽 것만)</summary>
    /// <summary>
    /// 탄 동안 몸 · 머리 위치를 '기본 위치 + 탄 높이'로 고정 — 모션 · 스킬이 몸 위치를 바꿔도 무시 (회전은 그대로 적용).
    /// 모든 LateUpdate가 끝나고 그리기 직전에 한 번 더 걸어서 어떤 코드가 늦게 써도 내려가지 않음
    /// </summary>
    void EnforceBodyLift()
    {
        if (armRig == null) return;
        if (_liftShown <= 0f)
        {
            if (_bodyForced)
            {
                _bodyForced = false;
                if (armRig.Body != null) armRig.Body.localPosition = _bodyRest;
                if (armRig.Head != null && (armRig.Body == null || !armRig.Head.IsChildOf(armRig.Body))) armRig.Head.localPosition = _headRest;
            }
            return;
        }
        if (_liftResolved != null && !_bodyViaRig) return;      // 몸이 이미 카메라 쪽과 함께 올라가는 구조

        _bodyForced = true;
        var body = armRig.Body;
        if (body != null) body.localPosition = _bodyRest + LocalUp(body, _liftShown);

        var head = armRig.Head;
        if (head != null && (body == null || !head.IsChildOf(body)))
            head.localPosition = _headRest + LocalUp(head, _liftShown);
    }

    static Vector3 LocalUp(Transform t, float h) =>
        t.parent != null ? t.parent.InverseTransformVector(Vector3.up * h) : Vector3.up * h;

    void OnBeforeRender(ScriptableRenderContext ctx, System.Collections.Generic.List<Camera> cams)
    {
        if (this == null) { RenderPipelineManager.beginContextRendering -= OnBeforeRender; return; }
        EnforceBodyLift();
    }

    static bool UnderAny(Transform t, Transform[] roots)
    {
        foreach (var r in roots) if (r != null && t.IsChildOf(r)) return true;
        return false;
    }

    public static List<Transform> AutoLiftTargets(Transform root, FPArmRig rig, Transform[] manual = null, bool includeBody = true)
    {
        var list = new List<Transform>();
        void Add(Transform t)
        {
            if (t == null || t == root) return;
            foreach (var x in list) if (t.IsChildOf(x)) return;
            list.RemoveAll(x => x.IsChildOf(t));
            list.Add(t);
        }

        if (manual != null && manual.Length > 0)
        {
            foreach (var t in manual) Add(t);
            return list;
        }

        Transform camRoot = FindDeep(root, "CameraRoot");
        if (camRoot == null && rig != null)
        {
            // 팔 리그를 품고 있는, 루트 바로 아래 오브젝트
            var t = rig.transform;
            while (t.parent != null && t.parent != root) t = t.parent;
            if (t.parent == root) camRoot = t;
        }
        Add(camRoot);
        if (includeBody && rig != null) { Add(rig.Body); Add(rig.Head); }
        return list;
    }

    Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
    }

    /// <summary>이동 방향을 바라보는 파츠 — 부모(플레이어)가 시선 따라 돌아도 월드 방향은 이동 방향</summary>
    void TickFacers(float dt)
    {
        if (_facers.Count == 0) { _faceInit = false; return; }

        float turn = 8f;
        foreach (var (_, a) in _facers) if (a != null) turn = a.FaceTurnSpeed;

        // 확실히 움직일 때만 방향을 바꿈 (멈추는 중 · 살짝 밀리는 건 무시 → 멈추면 그 자리 그대로)
        const float minSpeed = 1.5f;
        if (_vel.sqrMagnitude > minSpeed * minSpeed)
            _faceRot = Quaternion.Slerp(_faceRot, Quaternion.LookRotation(_vel.normalized, Vector3.up), 1f - Mathf.Exp(-turn * dt));

        foreach (var (t, a) in _facers)
            if (t != null && a != null) t.rotation = _faceRot * Quaternion.Euler(a.Rotation);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        TickFacers(dt);
        _liftShown = liftSpeed > 0f ? Mathf.MoveTowards(_liftShown, _lift, liftSpeed * dt) : _lift;
        if (armRig != null) armRig.BodyLift = 0f;      // 몸은 아래 EnforceBodyLift가 직접 고정
        EnforceBodyLift();
        if (_liftShown <= 0f && _liftResolved == null) return;

        if (_liftResolved == null)
        {
            // 몸 · 머리는 FPArmRig가 자세를 쓸 때마다 직접 올림(BodyLift) → 여기선 뺌.
            // 모션 · 스킬이 몸 위치를 덮어써도 순간적으로 내려가지 않음
            var list = AutoLiftTargets(transform, armRig, liftTargets, includeBody: false);
            if (armRig != null) list.RemoveAll(t => t == armRig.Body || t == armRig.Head);
            _liftResolved = list.ToArray();
            _bodyViaRig = armRig != null && armRig.Body != null && !UnderAny(armRig.Body, _liftResolved);
            _liftBase = new Vector3[_liftResolved.Length];
            _liftLastSet = new Vector3[_liftResolved.Length];
            for (int i = 0; i < _liftResolved.Length; i++)
                _liftBase[i] = _liftLastSet[i] = _liftResolved[i] != null ? _liftResolved[i].localPosition : Vector3.zero;

            if (!_liftLogged && Application.isEditor && HasInputAuthority)
            {
                _liftLogged = true;
                var names = new List<string>();
                foreach (var t in _liftResolved) if (t != null) names.Add(t.name);
                if (_bodyViaRig) names.Add(armRig.Body.name + " (탄 높이로 고정)");
                Debug.Log($"[PlayerAlter] 탄 높이 {_lift:0.##}m — 올리는 것: {(names.Count > 0 ? string.Join(", ", names) : "없음! liftTargets를 지정하세요")}", this);
            }
        }

        for (int i = 0; i < _liftResolved.Length; i++)
        {
            var t = _liftResolved[i];
            if (t == null) continue;

            // 다른 코드가 이번 프레임에 위치를 새로 썼으면 그게 기준 (몸 모션 · 카메라 등), 아니면 지난 기준 유지
            Vector3 cur = t.localPosition;
            if ((cur - _liftLastSet[i]).sqrMagnitude > 1e-10f) _liftBase[i] = cur;

            Vector3 up = t.parent != null ? t.parent.InverseTransformVector(Vector3.up * _liftShown) : Vector3.up * _liftShown;
            Vector3 set = _liftBase[i] + up;
            t.localPosition = set;
            _liftLastSet[i] = set;
        }

        // 다 내려왔으면 원래 자리로 두고 정리
        if (_liftShown <= 0f && _lift <= 0f)
        {
            for (int i = 0; i < _liftResolved.Length; i++)
                if (_liftResolved[i] != null) _liftResolved[i].localPosition = _liftBase[i];
            _liftResolved = null;
        }
    }

    // ══════════════════ 조회 ══════════════════

    public int Count(int id)
    {
        int n = 0;
        for (int i = 0; i < MaxAlters; i++) if (Alters[i] == id) n++;
        return n;
    }

    public bool Has(int id) => id != 0 && Count(id) > 0;
    public bool Has(AlterData a) => a != null && Has(a.Id);

    // ══════════════════ 붙이기 · 떼기 ══════════════════

    public void Add(AlterData a) { if (a != null) Add(a.Id); }
    public void Remove(AlterData a, bool all = false) { if (a != null) Remove(a.Id, all); }

    public void Add(int id)
    {
        if (HasStateAuthority) AddLocal(id);
        else RPC_Add(id);
    }

    public void Remove(int id, bool all = false)
    {
        if (HasStateAuthority) RemoveLocal(id, all);
        else RPC_Remove(id, all);
    }

    public void ClearAll()
    {
        if (!HasStateAuthority) return;
        for (int i = 0; i < MaxAlters; i++) Alters.Set(i, 0);
        ApplyStats();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Add(int id) => AddLocal(id);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Remove(int id, NetworkBool all) => RemoveLocal(id, all);

    void AddLocal(int id)
    {
        var a = database != null ? database.Get(id) : null;
        if (a == null) return;
        if (_health != null && _health.IsDead) return;

        // 같은 그룹은 교체
        if (!string.IsNullOrEmpty(a.Group))
            for (int i = 0; i < MaxAlters; i++)
            {
                var other = database.Get(Alters[i]);
                if (other != null && other.Id != a.Id && other.Group == a.Group) Alters.Set(i, 0);
            }

        if (Count(id) >= a.MaxStack) return;

        for (int i = 0; i < MaxAlters; i++)
            if (Alters[i] == 0)
            {
                Alters.Set(i, id);
                ApplyStats();
                return;
            }

        Debug.LogWarning($"[PlayerAlter] 변화 칸이 가득 찼습니다 ({MaxAlters}). {a.name} 무시", this);
    }

    void RemoveLocal(int id, bool all)
    {
        bool changed = false;
        for (int i = MaxAlters - 1; i >= 0; i--)
            if (Alters[i] == id)
            {
                Alters.Set(i, 0);
                changed = true;
                if (!all) break;
            }

        if (changed) ApplyStats();
    }

    public void ResetForNewRound() => ClearAll();

    // ══════════════════ 스탯 (SA) ══════════════════

    void ApplyStats()
    {
        if (!HasStateAuthority || _stats == null) return;

        float maxBefore = _stats.Get(StatType.MaxHP);

        for (int i = 0; i < MaxAlters; i++)
            _stats.RemoveBySource(SourceId.AlterBase + i);

        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database != null ? database.Get(Alters[i]) : null;
            if (a?.Stats == null) continue;

            foreach (var s in a.Stats)
                if (s != null) _stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, SourceId.AlterBase + i));
        }

        // 변화로 최대 체력이 오르면 현재 체력도 (상점·인챈트와 같은 규칙)
        _health?.ApplyMaxHpGain(maxBefore);
    }

    // ══════════════════ 파츠 (모든 클라이언트) ══════════════════

    void OnAltersChanged() => Rebuild();

    /// <summary>3인칭이 되면 본인에게 숨기던 파츠(말 등)도 보이게 다시 만듦</summary>
    void OnViewChanged(bool thirdPerson) => Rebuild();

    public override void Render()
    {
        TickPartAnims();

        // 무기를 바꾸면 무기 파츠를 새 무기로 옮김
        Transform tip = armRig != null ? armRig.WeaponTip : null;
        if (tip != _weaponParent && HasWeaponAlter()) Rebuild();

        // 사망·부활 시 보이기 다시 계산 (PlayerHealth가 렌더러를 끄고 켜는 것과 맞춤)
        bool dead = _health != null && _health.IsDead;
        if (dead != _wasDead) ApplyVisibility();

#if UNITY_EDITOR
        // 플레이 중 AlterData의 피격 범위 값을 바꾸면 바로 반영
        foreach (var (box, a) in _hitboxes)
            if (box != null) AlterHitboxes.Sync(box, a, _cc);
#endif
    }

    // ══════════════════ 파츠 애니메이션 (모든 클라이언트) ══════════════════

    /// <summary>움직이면 파츠의 RigAnimator를 Walk로 — 이동 속도에 맞춰 재생 속도도</summary>
    void TickPartAnims()
    {
        float dt = Time.deltaTime;
        Vector3 p = transform.position;
        Vector3 d = p - _lastPos;
        d.y = 0f;
        _lastPos = p;
        if (dt <= 0f) return;

        float speed = d.magnitude / dt;
        if (speed > 60f) speed = 0f;                                 // 순간이동 · 리스폰
        _moveSpeed = Mathf.Lerp(_moveSpeed, speed, 1f - Mathf.Exp(-10f * dt));
        // 방향은 몇 프레임에 걸쳐 거른 속도로 — 멈출 때 한 프레임 뒤로 밀리는 보정(1cm 남짓)에 휙 돌지 않게
        if (speed > 0f) _vel = Vector3.Lerp(_vel, d / dt, 1f - Mathf.Exp(-12f * dt));
        else _vel = Vector3.Lerp(_vel, Vector3.zero, 1f - Mathf.Exp(-12f * dt));

        bool moving = _moveSpeed > 0.6f;
        foreach (var (root, pa) in _partAnims)
            if (root != null) pa.SetMove(moving, _moveSpeed);
        if (_rigAnims.Count == 0) return;

        for (int i = 0; i < _rigAnims.Count; i++)
        {
            var ra = _rigAnims[i];
            if (ra == null) continue;
            ra.Moving = moving;
            ra.MoveSpeed = _moveSpeed;
        }
    }

    /// <summary>붙어 있는 파츠들에 상태 재생 (예: 죽음의 기사 돌진 때 말 "Charge"). 그 상태가 없는 파츠는 무시</summary>
    public void PlayPartState(string state, float hold = -1f)
    {
        foreach (var ra in _rigAnims)
            if (ra != null) ra.Play(state, hold);
        foreach (var (root, pa) in _partAnims)
            if (root != null) pa.PlayState(state, hold);
    }

    /// <summary>특정 변화의 파츠에만</summary>
    public void PlayPartState(AlterData alter, string state, float hold = -1f)
    {
        if (alter == null) return;
        foreach (var ra in _rigAnims)
            if (ra != null && ra.name.StartsWith($"Alter_{alter.name}")) ra.Play(state, hold);
        foreach (var (root, pa) in _partAnims)
            if (root != null && root.name.StartsWith($"Alter_{alter.name}")) pa.PlayState(state, hold);
    }

    bool HasWeaponAlter()
    {
        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database != null ? database.Get(Alters[i]) : null;
            if (a != null && a.Attach == AlterAttach.Weapon && a.Prefab != null) return true;
        }
        return false;
    }

    void Rebuild()
    {
        ClearVisuals();
        if (database == null) return;

        _weaponParent = armRig != null ? armRig.WeaponTip : null;
        var stackIndex = new Dictionary<int, int>();

        float lift = 0f;
        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database.Get(Alters[i]);
            if (a != null) lift = Mathf.Max(lift, a.Lift);
        }
        ApplyLift(lift);

        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database.Get(Alters[i]);
            if (a == null || a.Prefab == null) continue;

            stackIndex.TryGetValue(a.Id, out int k);
            stackIndex[a.Id] = k + 1;
            if (k > 0 && a.StackOffset == Vector3.zero) continue;      // 스탯만 쌓이는 변화

            Transform parent = Resolve(a);
            if (parent == null) continue;

            if (a.Lift > 0f && (a.Attach == AlterAttach.Body || a.Attach == AlterAttach.Head) && _warnedLift.Add(a.Id))
                Debug.LogWarning($"[PlayerAlter] {a.name}: 탄 높이(Lift)가 있는데 몸/머리에 붙어 있어 같이 올라갑니다. " +
                                 "말처럼 땅에 있어야 하면 Attach를 Root로 (Rig Animator → 변화 배치의 '발밑으로 옮기기')", a);

            var go = Instantiate(a.Prefab, parent);
            go.name = $"Alter_{a.name}{(k > 0 ? $"_{k + 1}" : "")}";
            go.transform.localPosition = a.Position + a.StackOffset * k;
            go.transform.localRotation = Quaternion.Euler(a.Rotation);
            go.transform.localScale = a.Scale;

            // 파츠는 장식 — 충돌·물리가 판정을 막지 않게
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

            // 변신 피격 범위 — 파츠 콜라이더를 다 끈 뒤에 만듦 (플레이어 레이어 = 공격 판정에 걸림)
            var hb = AlterHitboxes.Build(go, a, gameObject.layer, _cc);
            if (hb != null) _hitboxes.Add((hb, a));
            foreach (var pa in go.GetComponentsInChildren<IAlterPartAnim>(true)) _partAnims.Add((go, pa));

            _spawned.Add(go);
            ConfigureSelfView(go, a);

            var ra = go.GetComponent<RigAnimator>();
            if (ra != null) _rigAnims.Add(ra);

            if (a.FaceMoveDirection)
            {
                if (!_faceInit) { _faceRot = Quaternion.LookRotation(FlatForward(), Vector3.up); _faceInit = true; }
                _facers.Add((go.transform, a));
            }
        }

        ApplyVisibility();
    }

    Transform Resolve(AlterData a)
    {
        switch (a.Attach)
        {
            case AlterAttach.Body: return armRig != null && armRig.Body != null ? armRig.Body : transform;
            case AlterAttach.Head: return armRig != null && armRig.Head != null ? armRig.Head : (armRig != null && armRig.Body != null ? armRig.Body : transform);
            case AlterAttach.RightHand: return armRig != null ? armRig.RightHand : null;
            case AlterAttach.LeftHand: return armRig != null ? armRig.LeftHand : null;
            case AlterAttach.Weapon: return armRig != null ? armRig.WeaponTip : null;
            case AlterAttach.Root: return transform;
            case AlterAttach.Bone: return string.IsNullOrEmpty(a.BoneName) ? null : FindDeep(transform, a.BoneName);
        }
        return transform;
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

    /// <summary>붙은 변화 중 '땅에서만 공격'이 하나라도 있으면</summary>
    public bool GroundOnlyAttack
    {
        get
        {
            if (database == null || Object == null || !Object.IsValid) return false;
            for (int i = 0; i < MaxAlters; i++)
            {
                var a = database.Get(Alters[i]);
                if (a != null && a.GroundOnlyAttack) return true;
            }
            return false;
        }
    }

    /// <summary>본인 화면에서 시야를 가리는 파츠는 숨김 (그림자만 남길 수 있음)</summary>
    void ConfigureSelfView(GameObject go, AlterData a)
    {
        bool self = HasInputAuthority && (_tpv == null || !_tpv.IsThirdPerson);   // 3인칭이면 남들처럼 보임
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (self && !a.ShowForSelf)
            {
                if (a.SelfShadow) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                else { r.enabled = false; continue; }
            }
            _renderers.Add(r);
        }
    }

    void ApplyVisibility()
    {
        _wasDead = _health != null && _health.IsDead;
        foreach (var r in _renderers)
            if (r != null) r.enabled = !_wasDead;
    }

    void ClearVisuals()
    {
        foreach (var go in _spawned)
            if (go != null) Destroy(go);
        _spawned.Clear();
        _renderers.Clear();
        _rigAnims.Clear();
        _facers.Clear();
        _partAnims.Clear();
        _hitboxes.Clear();
    }
}