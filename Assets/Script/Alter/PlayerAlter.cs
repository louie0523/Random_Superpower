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
    private static readonly HashSet<int> _warnedLift = new();     // 파츠의 애니메이션 (말 걷기 등)
    private Vector3 _lastPos;
    private float _moveSpeed;
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

    public AlterDatabase Database => database;

    /// <summary>지금 올라타 있는 높이 (m)</summary>
    public float Lift => _lift;

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

        Rebuild();
        if (HasStateAuthority) ApplyStats();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_tpv != null) _tpv.Changed -= OnViewChanged;
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

        // 몸 · 카메라는 LateUpdate에서 올림 (모든 클라이언트 같은 방식)
        if (armRig != null) armRig.BodyLift = 0f;
    }

    // ══════════════════ 탄 높이 — 보이는 것 올리기 (모든 클라이언트) ══════════════════

    /// <summary>올릴 대상 자동 — CameraRoot + 몸 · 머리 (서로 겹치면 위쪽 것만)</summary>
    public static List<Transform> AutoLiftTargets(Transform root, FPArmRig rig, Transform[] manual = null)
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
        if (rig != null) { Add(rig.Body); Add(rig.Head); }
        return list;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        _liftShown = liftSpeed > 0f ? Mathf.MoveTowards(_liftShown, _lift, liftSpeed * dt) : _lift;
        if (_liftShown <= 0f && _liftResolved == null) return;

        if (_liftResolved == null)
        {
            _liftResolved = AutoLiftTargets(transform, armRig, liftTargets).ToArray();
            _liftBase = new Vector3[_liftResolved.Length];
            _liftLastSet = new Vector3[_liftResolved.Length];
            for (int i = 0; i < _liftResolved.Length; i++)
                _liftBase[i] = _liftLastSet[i] = _liftResolved[i] != null ? _liftResolved[i].localPosition : Vector3.zero;

            if (!_liftLogged && Application.isEditor && HasInputAuthority)
            {
                _liftLogged = true;
                var names = new List<string>();
                foreach (var t in _liftResolved) if (t != null) names.Add(t.name);
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

        if (_rigAnims.Count == 0) return;
        bool moving = _moveSpeed > 0.6f;
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
    }

    /// <summary>특정 변화의 파츠에만</summary>
    public void PlayPartState(AlterData alter, string state, float hold = -1f)
    {
        if (alter == null) return;
        foreach (var ra in _rigAnims)
            if (ra != null && ra.name.StartsWith($"Alter_{alter.name}")) ra.Play(state, hold);
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

            _spawned.Add(go);
            ConfigureSelfView(go, a);

            var ra = go.GetComponent<RigAnimator>();
            if (ra != null) _rigAnims.Add(ra);
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
    }
}