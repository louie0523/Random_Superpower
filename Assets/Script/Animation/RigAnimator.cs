using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생성물(탈것 · 그림자 손 등) 애니메이션 재생기. 모델 루트에 붙이고 Data(RigAnimData)를 넣음.
///   - 가만히 있으면 Idle, Moving = true면 Walk (Data의 IdleState / MoveState)
///   - Play("Grab", 1.5f) — 그 상태를 1.5초 동안 (반복 안 하는 상태는 한 번 재생 후 Idle·Walk로)
///   - AimAt(위치, 시간) — '실행 중 목표 따라가기'를 켠 IK가 그쪽으로 뻗고, 모델이 그쪽을 봄
/// 네트워크 없음 — 모든 클라이언트가 같은 신호(PlayerAlter, 능력 이벤트)를 받아 각자 재생.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public class RigAnimator : MonoBehaviour
{
    public RigAnimData Data;
    [Tooltip("시작 상태 (비우면 Data의 Idle)")]
    public string StartState;
    [Tooltip("전체 재생 속도")]
    public float SpeedMul = 1f;

    [Header("목표 보기 (AimAt)")]
    [Tooltip("AimAt 때 모델이 목표 쪽으로 돎 (좌우만)")]
    public bool TurnToAim = true;
    [Tooltip("도는 빠르기")]
    public float TurnSpeed = 10f;
    [Tooltip("IK가 목표로 뻗는 빠르기")]
    public float AimBlendSpeed = 8f;

    /// <summary>움직이는 중 — Walk 재생</summary>
    public bool Moving { get; set; }
    /// <summary>지금 이동 속도 (m/초) — Walk 재생 속도를 맞춤</summary>
    public float MoveSpeed { get; set; }

    public string CurrentState => _cur != null ? _cur.Name : null;

    [Header("확인용 (게임 중)")]
    [Tooltip("지금 이동 속도 — Run으로 바뀌는 속도(RunThreshold)를 정할 때 참고")]
    [SerializeField] private float currentSpeed;
    [SerializeField] private string currentState;
    private bool _running;

    private Transform[] _bones;
    private Transform[][] _chains;
    private readonly RigPose _pose = new();
    private readonly RigPose _from = new();
    private RigState _cur;
    private bool _isBase = true;
    private float _t;
    private float _blend, _blendDur;
    private float _holdUntil = -1f;

    private Vector3 _aim;
    private float _aimUntil = -1f;
    private float _aimW;
    private bool _aimTurn;

    void Awake() => Bind();

    /// <summary>본·IK 이름으로 다시 찾기 (Data를 바꿨을 때)</summary>
    public void Bind()
    {
        _bones = null;
        _chains = null;
        if (Data == null) return;

        var map = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (!map.ContainsKey(t.name)) map[t.name] = t;

        _bones = new Transform[Data.Bones.Count];
        for (int i = 0; i < _bones.Length; i++) map.TryGetValue(Data.Bones[i], out _bones[i]);

        _chains = new Transform[Data.Chains.Count][];
        for (int c = 0; c < _chains.Length; c++)
        {
            var ch = Data.Chains[c];
            if (ch?.Bones == null) continue;
            var arr = new Transform[ch.Bones.Length];
            bool ok = true;
            for (int i = 0; i < arr.Length; i++) ok &= map.TryGetValue(ch.Bones[i], out arr[i]);
            _chains[c] = ok ? arr : null;
        }

        string start = string.IsNullOrEmpty(StartState) ? Data.IdleState : StartState;
        _cur = Data.Find(start) ?? Data.Find(Data.IdleState);
        _isBase = true;
        _t = 0f;
        _blend = 0f;
    }

    public bool Has(string state) => Data != null && Data.Find(state) != null;

    /// <summary>
    /// 상태 재생. hold &gt; 0 이면 그 시간 동안 유지 (반복 상태는 계속 돌고, 한 번짜리는 마지막 자세로 멈춤),
    /// hold ≤ 0 이면 반복 상태는 계속 · 한 번짜리는 끝나면 Idle/Walk로
    /// </summary>
    public bool Play(string state, float hold = -1f)
    {
        var s = Data != null ? Data.Find(state) : null;
        if (s == null) return false;
        Switch(s, false);
        _holdUntil = hold > 0f ? Time.time + hold : -1f;
        return true;
    }

    /// <summary>Idle/Walk 자동 전환으로 돌아감</summary>
    public void Release()
    {
        _isBase = true;
        _holdUntil = -1f;
    }

    /// <summary>목표를 향해 — '실행 중 목표 따라가기' IK가 뻗고, TurnToAim이면 몸도 돎</summary>
    public void AimAt(Vector3 world, float duration, bool turn = true)
    {
        _aim = world;
        _aimUntil = Time.time + Mathf.Max(0.05f, duration);
        _aimTurn = turn && TurnToAim;
    }

    public void StopAim() => _aimUntil = -1f;

    void Switch(RigState s, bool isBase)
    {
        if (s == null) return;
        if (_cur == s && _isBase == isBase && s.Loop) return;

        Capture(_from);
        _blendDur = s.BlendIn;
        _blend = s.BlendIn;
        _cur = s;
        _isBase = isBase;
        _t = 0f;
    }

    RigState BaseState()
    {
        if (Data == null) return null;
        if (Moving)
        {
            // 달리기 — 문턱을 넘으면 Run, 문턱의 85% 아래로 떨어지면 Walk (왔다갔다 깜빡임 방지)
            var run = Data.Find(Data.RunState);
            if (run != null)
            {
                float th = Mathf.Max(0.1f, Data.RunThreshold);
                _running = _running ? MoveSpeed >= th * 0.85f : MoveSpeed >= th;
                if (_running) return run;
            }
            var m = Data.Find(Data.MoveState);
            if (m != null) return m;
            if (run != null) return run;
        }
        _running = false;
        return Data.Find(Data.IdleState);
    }

    void LateUpdate()
    {
        if (Data == null || _bones == null) return;
        float dt = Time.deltaTime;

        // 기본 상태 자동 전환
        var baseState = BaseState();
        if (_isBase && _cur != baseState && baseState != null) Switch(baseState, true);
        if (_cur == null) _cur = baseState;
        if (_cur == null) return;

        // 진행
        float spd = _cur.Speed * SpeedMul;
        if (_isBase && Moving)
        {
            if (_cur.Name == Data.RunState && Data.RunRefSpeed > 0.01f)
                spd *= Mathf.Clamp(MoveSpeed / Data.RunRefSpeed, 0.5f, 2.5f);
            else if (_cur.Name == Data.MoveState && Data.WalkRefSpeed > 0.01f)
                spd *= Mathf.Clamp(MoveSpeed / Data.WalkRefSpeed, 0.4f, 3f);
        }
        currentSpeed = Moving ? MoveSpeed : 0f;
        currentState = _cur.Name;
        _t += dt * spd;

        // 한 번짜리 · 유지 끝 → 기본으로
        if (!_isBase)
        {
            bool done = _holdUntil > 0f ? Time.time >= _holdUntil : (!_cur.Loop && _t >= _cur.Length);
            if (done && baseState != null) { Switch(baseState, true); _holdUntil = -1f; }
        }

        Data.Sample(_cur, _t, _pose);

        if (_blend > 0f)
        {
            _blend -= dt;
            float w = _blendDur > 0.0001f ? 1f - Mathf.Clamp01(_blend / _blendDur) : 1f;
            w = w * w * (3f - 2f * w);
            int n = Mathf.Min(_pose.Count, _from.Count);
            for (int i = 0; i < n; i++)
            {
                _pose.Rot[i] = Quaternion.Slerp(_from.Rot[i], _pose.Rot[i], w);
                _pose.Pos[i] = Vector3.Lerp(_from.Pos[i], _pose.Pos[i], w);
            }
        }

        Apply(_pose);
        TickAim(dt);
    }

    void TickAim(float dt)
    {
        bool on = Time.time < _aimUntil;
        _aimW = Mathf.MoveTowards(_aimW, on ? 1f : 0f, dt * AimBlendSpeed);
        if (_aimW <= 0.0001f) return;

        if (on && _aimTurn)
        {
            Vector3 d = _aim - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized, Vector3.up),
                                                      1f - Mathf.Exp(-TurnSpeed * dt));
        }

        float ease = _aimW * _aimW * (3f - 2f * _aimW);
        for (int c = 0; c < _chains.Length; c++)
        {
            var def = Data.Chains[c];
            if (_chains[c] == null || def == null || !def.FollowRuntimeTarget) continue;
            RigIK.SolveWeighted(_chains[c], _aim, ease * def.RuntimeWeight);
        }
    }

    void Capture(RigPose p)
    {
        if (_bones == null) return;
        p.Resize(_bones.Length, Data.Rest);
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) continue;
            p.Rot[i] = _bones[i].localRotation;
            p.Pos[i] = _bones[i].localPosition;
        }
    }

    public void Apply(RigPose p)
    {
        if (_bones == null || p == null) return;
        int n = Mathf.Min(_bones.Length, p.Count);
        for (int i = 0; i < n; i++)
            if (_bones[i] != null) _bones[i].SetLocalPositionAndRotation(p.Pos[i], p.Rot[i]);
    }

    /// <summary>에디터 미리보기 — 상태의 time초 자세를 바로 적용 (재생 상태는 안 바꿈)</summary>
    public void PreviewState(string state, float time)
    {
        if (Data == null) return;
        if (_bones == null || _bones.Length != Data.Bones.Count) Bind();
        var s = Data.Find(state);
        Data.Sample(s, time, _pose);
        Apply(_pose);
    }

    /// <summary>에디터 미리보기 끝 — 기본 자세로</summary>
    public void PreviewRest()
    {
        if (Data == null) return;
        if (_bones == null || _bones.Length != Data.Bones.Count) Bind();
        Apply(Data.Rest);
    }
}