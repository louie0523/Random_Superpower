using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 변화 · 변신 모델을 '그냥 애니메이션 클립'으로 움직임 — 믹사모 등 리깅된 모델 + 기존 클립.
/// Animator Controller(상태 그래프)를 만들 필요 없음: 클립만 칸에 넣으면 코드가 섞어서 재생.
///
///   Idle / Walk / Run : 이동 속도에 따라 자동 (Walk · Run은 속도에 맞춰 재생 속도도)
///   States            : 이름 붙인 한 번짜리 · 반복 동작 — "Attack", "Leap", "Land" 등.
///                       PlayerAlter.PlayPartState("이름") · TransformAbility의 AttackAnim · 변화 기본 공격(AlterData)이 부름
///   변화 기본 공격(AlterData의 Attack)이면 공격 판정 순간에 클립의 HitAt(타격 순간)이 맞도록 재생 속도를 자동 조절
///   발걸음: Walk · Run 클립의 발 닿는 순간마다 쿵쿵 소리 + 주변 사람 화면 흔들림 (Footsteps 켜기)
///
/// 세팅: 모델 루트(또는 Animator가 있는 곳)에 붙이고 클립 넣기. Animator의 Controller는 비워 두기 (Avatar는 필요).
/// PlayerAlter의 IAlterPartAnim 패치(용 변신 때 넣은 것)가 있어야 신호를 받음.
/// </summary>
[DisallowMultipleComponent]
public class AlterClipAnimator : MonoBehaviour, IAlterPartAnim
{
    [System.Serializable]
    public class ClipState
    {
        [Tooltip("상태 이름 — PlayPartState · AttackAnim에 쓰는 이름 (대소문자 구분)")]
        public string Name;
        public AnimationClip Clip;
        [Tooltip("재생 속도")]
        public float Speed = 1f;
        [Tooltip("섞여 들어가는 시간 (초)")]
        public float FadeIn = 0.12f;
        [Tooltip("타격 순간 (0~1, 클립 길이 대비). 0 이상이면 변화 기본 공격의 판정 순간에 맞춰 재생 속도를 조절. 음수면 안 맞춤")]
        public float HitAt = -1f;
        [Tooltip("타격 순간을 프레임 번호로 (Animation 창 위쪽 숫자). 0 이상이면 Hit At 대신 이걸 씀")]
        public int HitFrame = -1;
        [Tooltip("다 재생한 뒤 마지막 자세로 이만큼 더 머무름 (초)")]
        public float HoldEnd = 0f;
    }

    [Header("연결")]
    [Tooltip("비우면 자식에서 찾음. Controller는 비워 두고 Avatar만 (믹사모 Humanoid면 그 Avatar)")]
    public Animator Animator;

    [Header("기본 (자동)")]
    public AnimationClip Idle;
    public AnimationClip Walk;
    [Tooltip("비우면 빨라도 Walk")]
    public AnimationClip Run;
    [Tooltip("Walk 클립이 1배속일 때의 이동 속도 (m/초) — 실제 속도에 맞춰 발이 덜 미끄러지게")]
    public float WalkRefSpeed = 3f;
    [Tooltip("이 속도(m/초) 이상이면 Run")]
    public float RunThreshold = 6f;
    [Tooltip("Run 클립이 1배속일 때의 이동 속도 (m/초)")]
    public float RunRefSpeed = 7f;
    [Tooltip("이 속도 이상이면 '움직임' (PlayerAlter 신호가 없을 때 스스로 판단)")]
    public float MoveThreshold = 0.6f;
    public float BaseFade = 0.2f;

    [Header("발걸음 (Walk · Run 재생 중) — 주변 쿵쿵 · 화면 흔들림")]
    [Tooltip("켜면 발이 땅에 닿을 때마다 소리 · 흔들림")]
    public bool Footsteps = false;
    [Tooltip("Walk 클립에서 발이 땅에 닿는 프레임 — Animation 창 위쪽 프레임 번호 그대로 (예: 16, 32).\n" +
             "비우면 아래 Step Times(0~1 비율)를 씀")]
    public int[] WalkStepFrames = { };
    [Tooltip("Run 클립에서 발이 땅에 닿는 프레임. 비우면 Walk와 같은 비율 (Step Times)")]
    public int[] RunStepFrames = { };
    [Tooltip("프레임을 안 쓸 때 — 발 닿는 순간 (0~1, 클립 길이 대비)")]
    public float[] StepTimes = { 0.05f, 0.55f };
    public FeelDef Footstep = new() { ShakeRadius = 14f, ShakeAmount = 0.08f, ShakeSelf = 0.3f, Volume = 0.9f, SoundRange = 30f };

    [Header("공중 — 점프 · 착지 (높이를 보고 자동)")]
    [Tooltip("뜨면 Jump State, 떨어지다 땅이 가까워지면 Land State, 땅에 닿으면 쿵 (발걸음 × Land Feel Mul)")]
    public bool AirAnims = true;
    public string JumpState = "Leap";
    public string LandState = "Land";
    [Tooltip("떨어지는 중 땅까지 이 거리(m) 이하가 되면 착지 동작")]
    public float LandHeight = 1.5f;
    [Tooltip("이 높이(m) 이상 떠 봐야 착지 동작이 나옴 — 계단 · 턱 · 막 뛴 순간엔 안 나오게")]
    public float MinAirHeight = 1f;
    [Tooltip("위로 이 속도(m/초) 이상이면 '뛰었다'")]
    public float JumpUpSpeed = 1.5f;
    [Tooltip("땅 판정 레이어")]
    public LayerMask GroundMask = ~0;
    [Tooltip("착지 쿵 — 발걸음(Footstep) 흔들림 · 범위의 이 배율 (1.5 = 50% 크게). 0이면 없음")]
    public float LandFeelMul = 1.5f;
    [Tooltip("착지 동작이 있으면, 그 동작이 끝나고 이 시간(초)이 더 지나야 움직일 수 있음 (내 캐릭터만). 음수면 막지 않음")]
    public float LandRecover = 0.15f;
    [Tooltip("착지 동작 중엔 모델이 돌지 않음 (마우스로 시점을 돌려도 몸은 그대로) → 끝나면 부드럽게 다시 시선 쪽으로")]
    public bool FreezeFacingOnLand = true;
    [Tooltip("다시 시선 쪽으로 도는 빠르기")]
    public float FacingReturnSpeed = 6f;

    [Header("동작 (이름으로 재생)")]
    public List<ClipState> States = new()
    {
        new ClipState { Name = "Attack", HitAt = 0.4f },
        new ClipState { Name = "Leap" },
        new ClipState { Name = "Land" },
    };

    // ── 그래프 ──
    PlayableGraph _graph;
    AnimationMixerPlayable _mixer;
    readonly List<AnimationClipPlayable> _plays = new();
    readonly List<float> _weights = new();
    readonly Dictionary<string, int> _index = new();
    int _idle = -1, _walk = -1, _run = -1;

    // ── 상태 ──
    int _cur = -1;              // 지금 목표 입력
    float _fade = 0.2f;
    int _oneShot = -1;          // 재생 중인 동작 (없으면 -1)
    float _oneShotEnd;          // 끝나는 시각 (반복 · 유지면 hold로)
    bool _holdLoop;             // hold > 0 반복 상태 (Flying 등)

    bool _extMoving;
    float _extSpeed, _extTime = -10f, _selfSpeed;
    Vector3 _lastPos;
    bool _hasLast;
    bool _running;
    int _stepClip = -1;
    float _stepT;

    // 공중
    bool _air, _landPlayed, _abilityLeap;
    float _airTime, _vy, _lastY, _groundBase = 999f, _groundedFor, _maxH;
    bool _rose;                     // 이번 공중에서 올라간 적 있음 (꼭대기를 지나야 착지 동작)
    bool _hasY;
    FeelDef _landFeel;
    Transform _body;                // 플레이어 루트 — 높이 · 땅 판정 기준 (모델 위치를 내려 둬도 정확하게)
    PlayerMovement _move;
    bool _jumpPlayed;

    // 착지 중 방향 고정
    float _freezeUntil = -1f, _returnT = 1f;
    Quaternion _frozenRot, _baseLocalRot, _returnFrom;
    bool _baseSaved;
    static readonly RaycastHit[] _rh = new RaycastHit[8];

    void Awake()
    {
        if (Animator == null) Animator = GetComponentInChildren<Animator>(true);
        if (Animator == null) { Debug.LogWarning("[AlterClipAnimator] Animator가 없음 — 모델에 Animator(Avatar 포함)를 붙이세요", this); enabled = false; return; }
        Animator.applyRootMotion = false;
        _body = transform.root;
        _move = GetComponentInParent<PlayerMovement>();
        Build();
    }

    void Build()
    {
        _graph = PlayableGraph.Create($"AlterClip_{name}");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(_graph, "Anim", Animator);

        var clips = new List<(string name, AnimationClip clip)>();
        if (Idle != null) { _idle = clips.Count; clips.Add(("Idle", Idle)); }
        if (Walk != null) { _walk = clips.Count; clips.Add(("Walk", Walk)); }
        if (Run != null) { _run = clips.Count; clips.Add(("Run", Run)); }
        foreach (var s in States)
        {
            if (s == null || s.Clip == null || string.IsNullOrEmpty(s.Name) || _index.ContainsKey(s.Name)) continue;
            _index[s.Name] = clips.Count;
            clips.Add((s.Name, s.Clip));
        }

        _mixer = AnimationMixerPlayable.Create(_graph, clips.Count);
        output.SetSourcePlayable(_mixer);
        for (int i = 0; i < clips.Count; i++)
        {
            var p = AnimationClipPlayable.Create(_graph, clips[i].clip);
            p.SetApplyFootIK(true);
            _graph.Connect(p, 0, _mixer, i);
            _mixer.SetInputWeight(i, 0f);
            _plays.Add(p);
            _weights.Add(0f);
        }

        _cur = _idle >= 0 ? _idle : (clips.Count > 0 ? 0 : -1);
        if (_cur >= 0) { _weights[_cur] = 1f; _mixer.SetInputWeight(_cur, 1f); }
        _graph.Play();
    }

    void Start()
    {
        // PlayerAlter가 넣은 기본 회전 (방향 고정이 끝나면 여기로 돌아옴)
        _baseLocalRot = transform.localRotation;
        _baseSaved = true;
        if (_move != null) _move.Jumped += OnJumped;      // 내 캐릭터: 점프 순간을 정확히
    }

    void OnDestroy()
    {
        if (_move != null) _move.Jumped -= OnJumped;
        if (_graph.IsValid()) _graph.Destroy();
    }

    void OnJumped()
    {
        if (!AirAnims || _abilityLeap || _jumpPlayed) return;
        _jumpPlayed = true;
        _groundedFor = 0f;
        PlayNamed(JumpState);
    }

    void LateUpdate()
    {
        if (!FreezeFacingOnLand || !_baseSaved || transform.parent == null) return;
        if (Time.time < _freezeUntil)
        {
            transform.rotation = _frozenRot;                 // 착지 중 — 몸은 그 자리 방향 그대로
            _returnT = 0f;
            _returnFrom = transform.localRotation;
            return;
        }
        if (_returnT < 1f)
        {
            if (_returnT <= 0f) _returnFrom = Quaternion.Inverse(transform.parent.rotation) * _frozenRot;
            _returnT = Mathf.MoveTowards(_returnT, 1f, Time.deltaTime * FacingReturnSpeed);
            float k = _returnT * _returnT * (3f - 2f * _returnT);
            transform.localRotation = Quaternion.Slerp(_returnFrom, _baseLocalRot, k);
        }
    }

    void FreezeFacing(float until)
    {
        if (!FreezeFacingOnLand) return;
        if (Time.time >= _freezeUntil) _frozenRot = transform.rotation;      // 새로 고정 — 지금 방향
        _freezeUntil = Mathf.Max(_freezeUntil, until);
    }

    void OnDisable() { if (_graph.IsValid()) _graph.Stop(); }
    void OnEnable() { if (_graph.IsValid()) _graph.Play(); }

    ClipState Find(string name)
    {
        foreach (var s in States) if (s != null && s.Name == name) return s;
        return null;
    }

    // ══════════════════ 신호 (PlayerAlter) ══════════════════

    public void SetMove(bool moving, float speed)
    {
        _extMoving = moving;
        _extSpeed = speed;
        _extTime = Time.time;
    }

    public bool PlayState(string state, float hold)
    {
        var s = Find(state);
        if (s == null || !_index.TryGetValue(state, out int i)) return false;
        Begin(i, s, s.Speed, hold);
        if (state == JumpState) { _abilityLeap = true; _groundedFor = 0f; }    // 능력이 시킨 도약 — 착지 쿵은 능력 쪽이
        return true;
    }

    void PlayNamed(string state)
    {
        if (string.IsNullOrEmpty(state)) return;
        var s = Find(state);
        if (s != null && _index.TryGetValue(state, out int i)) Begin(i, s, s.Speed, -1f);
    }

    public void ReleaseState()
    {
        if (_oneShot >= 0 && _holdLoop) EndOneShot();
    }

    public void PlayAttack(string state, float duration, float hitAt)
    {
        var s = Find(state);
        if (s == null || !_index.TryGetValue(state, out int i)) return;

        float len = s.Clip.length;
        float speed = s.Speed;
        float hit01 = s.HitFrame >= 0 ? Mathf.Clamp01(s.HitFrame / Mathf.Max(1f, len * s.Clip.frameRate)) : s.HitAt;
        if (hit01 >= 0f && hitAt > 0.01f) speed = hit01 * len / hitAt;               // 타격 순간 = 판정 순간
        else if (duration > 0.01f) speed = len / duration;                            // 길이 = 공격 길이
        Begin(i, s, Mathf.Clamp(speed, 0.1f, 5f), -1f);
    }

    void Begin(int i, ClipState s, float speed, float hold)
    {
        var p = _plays[i];
        p.SetTime(0);
        p.SetDone(false);
        p.SetSpeed(speed);
        _oneShot = i;
        _holdLoop = hold > 0f;
        _oneShotEnd = _holdLoop ? Time.time + hold
                                : Time.time + s.Clip.length / Mathf.Max(0.01f, speed) + s.HoldEnd;
        CrossTo(i, s.FadeIn);
    }

    void EndOneShot()
    {
        _oneShot = -1;
        _holdLoop = false;
        CrossTo(BaseIndex(out _), BaseFade);
    }

    void CrossTo(int i, float fade)
    {
        if (i < 0) return;
        _cur = i;
        _fade = Mathf.Max(0.01f, fade);
    }

    // ══════════════════ 매 프레임 ══════════════════

    /// <summary>걷기 · 달리기 클립에서 발 닿는 순간을 지날 때마다 쿵 (모든 화면에서 각자 — 네트워크 필요 없음)</summary>
    // ══════════════════ 공중 ══════════════════

    /// <summary>뜸 → 점프 동작 / 떨어지다 땅 가까움 → 착지 동작 / 닿음 → 쿵. 모든 화면에서 각자 (위치만 봄)</summary>
    void TickAir(float dt, Vector3 modelPos)
    {
        if (!AirAnims || dt <= 0f) return;
        Vector3 pos = _body != null ? _body.position : modelPos;      // 플레이어 루트 기준

        float vy = _hasY ? (pos.y - _lastY) / dt : 0f;
        _lastY = pos.y;
        _hasY = true;
        if (Mathf.Abs(vy) > 80f) vy = 0f;                                // 순간이동 · 리스폰
        _vy = Mathf.Lerp(_vy, vy, 1f - Mathf.Exp(-30f * dt));

        float g = GroundDistance(pos);
        if (g < _groundBase) _groundBase = g;                            // 서 있을 때 땅까지 거리 (모델 기준점이 발이 아니어도)
        float h = g - _groundBase;
        bool grounded = h <= 0.2f;

        if (!_air)
        {
            if (grounded)
            {
                _groundedFor += dt;
                if (_groundedFor > 0.4f) _jumpPlayed = false;            // 점프 신호는 왔는데 거의 안 뜸
                if (_groundedFor > 1f) _abilityLeap = false;             // 혹시 남은 표시 정리
                return;
            }
            if (_vy > JumpUpSpeed || h > 0.6f)
            {
                _air = true;
                _airTime = 0f;
                _landPlayed = false;
                _maxH = h;
                _rose = _vy > 0.3f;
            }
            return;
        }

        _airTime += dt;
        _maxH = Mathf.Max(_maxH, h);
        if (_vy > 0.3f) _rose = true;

        // 점프 동작 — 다른 화면(또는 신호를 못 받았을 때): 뜬 뒤 0.5초 안에 올라가는 게 보이면
        if (!_jumpPlayed && !_abilityLeap && !_landPlayed && _airTime < 0.5f && _vy > JumpUpSpeed)
        {
            _jumpPlayed = true;
            PlayNamed(JumpState);
        }

        // 점프 동작이 끝나도 공중에선 마지막 자세 유지
        if (_oneShot >= 0 && _index.TryGetValue(JumpState ?? "", out int ji) && _oneShot == ji)
            _oneShotEnd = Mathf.Max(_oneShotEnd, Time.time + 0.05f);

        // 착지 동작 — 한 번 제대로 떴다가(최고 높이 ≥ MinAirHeight) 내려오는 중일 때만
        if (!_landPlayed && _vy < -0.5f && h <= LandHeight && _maxH >= MinAirHeight && _airTime > 0.15f
            && (_rose || _maxH >= MinAirHeight * 1.5f))
        {
            _landPlayed = true;
            PlayNamed(LandState);
            if (_index.TryGetValue(LandState ?? "", out int lsi) && _oneShot == lsi)
            {
                FreezeFacing(_oneShotEnd + Mathf.Max(0f, LandRecover));
                // 착지 동작이 시작될 때 바로 잠금 — 땅에 닿는 틱에 Space를 누르고 있어도 바로 또 뛰지 않게
                if (LandRecover >= 0f && _move != null)
                    _move.LockMoveUntil = Mathf.Max(_move.LockMoveUntil, _oneShotEnd + LandRecover);
            }
        }

        if (grounded && _airTime > 0.1f)
        {
            _air = false;
            _groundedFor = 0f;
            if (!_abilityLeap && _airTime > 0.25f && LandFeelMul > 0f)
                Shockwave.Feel(modelPos, LandFeel(), transform);
            _abilityLeap = false;
            _jumpPlayed = false;

            // 착지 동작이 재생 중이면 끝날 때까지 + LandRecover 동안 이동 막기
            if (LandRecover >= 0f && _move != null && _landPlayed && _oneShot >= 0
                && _index.TryGetValue(LandState ?? "", out int li) && _oneShot == li)
            {
                _move.LockMoveUntil = Mathf.Max(_move.LockMoveUntil, _oneShotEnd + LandRecover);
                FreezeFacing(_oneShotEnd + LandRecover);
            }
        }
    }

    float GroundDistance(Vector3 pos)
    {
        Vector3 o = pos + Vector3.up * 1f;            // 몸 안쪽에서 아래로 (발밑이 기준점보다 아래여도 걸리게)
        int n = Physics.RaycastNonAlloc(o, Vector3.down, _rh, 60f, GroundMask, QueryTriggerInteraction.Ignore);
        float best = 60f;
        var root = transform.root;
        for (int i = 0; i < n; i++)
        {
            var c = _rh[i].collider;
            if (c == null || c.transform.root == root) continue;             // 내 몸 · 피격 범위
            best = Mathf.Min(best, _rh[i].distance - 1f);
        }
        return best;
    }

    /// <summary>착지 쿵 = 발걸음 × 배율</summary>
    FeelDef LandFeel()
    {
        if (_landFeel != null) return _landFeel;
        var f = Footstep ?? new FeelDef();
        _landFeel = new FeelDef
        {
            ShakeRadius = f.ShakeRadius * LandFeelMul,
            ShakeAmount = f.ShakeAmount * LandFeelMul,
            ShakeSelf = f.ShakeSelf,
            Sound = f.Sound,
            Volume = Mathf.Clamp01(f.Volume * Mathf.Sqrt(LandFeelMul)),
            SoundRange = f.SoundRange * LandFeelMul,
            PitchJitter = f.PitchJitter,
            Fx = f.Fx,
            FxLife = f.FxLife,
        };
        return _landFeel;
    }

    void TickFootsteps()
    {
        int c = _cur;
        bool walking = Footsteps && !_air && _oneShot < 0 && c >= 0 && (c == _walk || c == _run) && _weights[c] > 0.5f;
        if (!walking) { _stepClip = -1; return; }

        var clip = c == _walk ? Walk : Run;
        float len = clip != null ? clip.length : 0f;
        if (len <= 0.01f) return;

        float t = (float)(_plays[c].GetTime() % len) / len;
        if (_stepClip == c)
        {
            var frames = c == _walk ? WalkStepFrames : RunStepFrames;
            if (frames != null && frames.Length > 0)
            {
                // 프레임 번호 → 비율 (클립 프레임 수 = 길이 × 초당 프레임)
                float total = Mathf.Max(1f, len * clip.frameRate);
                foreach (var f in frames)
                    if (Crossed(_stepT, t, Mathf.Repeat(f / total, 1f)))
                        Shockwave.Feel(transform.position, Footstep, transform);
            }
            else if (StepTimes != null)
            {
                foreach (var st in StepTimes)
                    if (Crossed(_stepT, t, st))
                        Shockwave.Feel(transform.position, Footstep, transform);
            }
        }
        _stepClip = c;
        _stepT = t;
    }

    static bool Crossed(float from, float to, float at) =>
        to >= from ? (at > from && at <= to) : (at > from || at <= to);       // 한 바퀴 돌아 처음으로 넘어간 경우

    int BaseIndex(out float playSpeed)
    {
        bool ext = Time.time - _extTime < 0.3f;
        bool moving = ext ? _extMoving : _selfSpeed > MoveThreshold;
        float speed = ext ? _extSpeed : _selfSpeed;
        playSpeed = 1f;

        if (moving && _walk >= 0 || moving && _run >= 0)
        {
            _running = _run >= 0 && (_running ? speed >= RunThreshold * 0.85f : speed >= RunThreshold);
            if (_running || _walk < 0)
            {
                playSpeed = RunRefSpeed > 0.01f ? Mathf.Clamp(speed / RunRefSpeed, 0.5f, 2.5f) : 1f;
                return _run;
            }
            playSpeed = WalkRefSpeed > 0.01f ? Mathf.Clamp(speed / WalkRefSpeed, 0.4f, 3f) : 1f;
            return _walk;
        }
        _running = false;
        return _idle;
    }

    void Update()
    {
        if (!_graph.IsValid() || _plays.Count == 0) return;
        float dt = Time.deltaTime;

        // 스스로 이동 속도 (PlayerAlter 신호가 없을 때)
        Vector3 pos = transform.position;
        if (_hasLast && dt > 0f)
        {
            Vector3 d = pos - _lastPos; d.y = 0f;
            float s = d.magnitude / dt;
            if (s > 60f) s = 0f;
            _selfSpeed = Mathf.Lerp(_selfSpeed, s, 1f - Mathf.Exp(-10f * dt));
        }
        _lastPos = pos;
        _hasLast = true;

        TickAir(dt, pos);

        // 동작 끝 → 기본으로
        if (_oneShot >= 0 && Time.time >= _oneShotEnd) EndOneShot();

        // 기본 상태 (동작 중이 아닐 때)
        if (_oneShot < 0)
        {
            int b = BaseIndex(out float sp);
            if (b >= 0)
            {
                if (b != _cur) CrossTo(b, BaseFade);
                _plays[b].SetSpeed(sp);
            }
        }

        TickFootsteps();

        // 섞기 — 목표 입력은 1로, 나머지는 0으로
        float step = dt / _fade, sum = 0f;
        for (int i = 0; i < _weights.Count; i++)
        {
            _weights[i] = Mathf.MoveTowards(_weights[i], i == _cur ? 1f : 0f, step);
            sum += _weights[i];
        }
        // 합이 1이 되게 (빠르게 여러 번 바뀌어도 자세가 쪼그라들지 않게)
        for (int i = 0; i < _weights.Count; i++)
            _mixer.SetInputWeight(i, sum > 0.0001f ? _weights[i] / sum : (i == _cur ? 1f : 0f));
    }
}
