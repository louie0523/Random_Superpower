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

    void Awake()
    {
        if (Animator == null) Animator = GetComponentInChildren<Animator>(true);
        if (Animator == null) { Debug.LogWarning("[AlterClipAnimator] Animator가 없음 — 모델에 Animator(Avatar 포함)를 붙이세요", this); enabled = false; return; }
        Animator.applyRootMotion = false;
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

    void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
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
        return true;
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
        if (s.HitAt >= 0f && hitAt > 0.01f) speed = s.HitAt * len / hitAt;           // 타격 순간 = 판정 순간
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
