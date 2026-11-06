using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 획의 보정 — 마우스 점들 → 붓 자국(PaintDab) 목록. 그림 픽셀 기준 · 엔진 없이 계산만.
///   손떨림 보정: 끈으로 끄는 붓 (커서가 반지름 밖으로 나가야 따라감)
///   끝 다듬기: 시작은 가늘게, 끝나면 끝도 가늘게
///   스프레이: 둘레에 흩뿌린 점 · 멈춰 있던 곳과 획 끝에 흘러내림
///   반듯하게: 그리고 잠깐(0.45초) 멈추면 직선 · 원 · 타원으로 — 그대로 끌면 끝점 · 크기 조절
/// 매 프레임 처음부터 다시 만들어서 그림 (끝 다듬기 · 도형 바꾸기가 자연스럽게)
/// </summary>
public class StrokeShaper
{
    enum Shape { None, Line, Circle, Ellipse }

    public float Radius = 12f;
    public bool Stabilize = true;
    [Range(0f, 1f)] public float StabilizeAmount = 0.5f;
    public bool Spray;
    public bool Drips;
    public bool Snap = true;
    public bool Eraser;
    [Tooltip("외곽선을 그리면 흩뿌린 점은 안 찍음 (점마다 테두리가 생겨서 지저분)")]
    public bool Outline;
    [Tooltip("흘러내리는 방향 (그림 기준 · 0 = 안 흘러내림)")]
    public Vector2 DripDir = new(0f, -1f);

    public const float HoldToSnap = 0.45f;

    readonly List<Vector2> _pts = new();
    readonly List<(Vector2 p, float t)> _dwell = new();
    readonly List<Vector2> _path = new();
    Vector2 _lazy, _raw, _dwellPos;
    float _lastTime, _lastMoveTime, _dwellAcc;
    bool _snapTried, _finished;
    int _seed;

    Shape _shape;
    Vector2 _a, _b, _c;
    float _rx, _ry, _snapDist;

    public bool Active { get; private set; }
    public bool Snapped => _shape != Shape.None;

    // ══════════════════ 입력 ══════════════════

    public void Begin(Vector2 p, float time, int seed)
    {
        _pts.Clear();
        _dwell.Clear();
        _shape = Shape.None;
        _snapTried = _finished = false;
        _lazy = _raw = _dwellPos = p;
        _pts.Add(p);
        _lastTime = _lastMoveTime = time;
        _dwellAcc = 0f;
        _seed = seed;
        Active = true;
    }

    public void Move(Vector2 p, float time)
    {
        if (!Active) return;
        float dt = Mathf.Max(0f, time - _lastTime);
        _lastTime = time;
        if ((p - _raw).sqrMagnitude > 4f)       // 2픽셀 넘게 움직임
        {
            _lastMoveTime = time;
            if (_shape == Shape.None) _snapTried = false;
        }
        _raw = p;

        if (_shape != Shape.None) { Adjust(p); return; }

        // 손떨림 보정 — 끈 길이만큼 늦게 따라감
        float lazyR = Stabilize && !Eraser ? Mathf.Lerp(0f, Radius * 1.4f + 10f, StabilizeAmount) : 0f;     // 지우개는 누른 곳 그대로
        var d = p - _lazy;
        float m = d.magnitude;
        if (m > lazyR) _lazy += d / m * (m - lazyR);
        if ((_lazy - _pts[_pts.Count - 1]).magnitude >= Mathf.Max(1f, Radius * 0.12f)) _pts.Add(_lazy);

        // 머문 곳 (흘러내림)
        if ((_lazy - _dwellPos).magnitude < Radius * 0.35f) _dwellAcc += dt;
        else
        {
            FlushDwell();
            _dwellPos = _lazy;
        }

        // 반듯하게 — 누른 채 잠깐 멈춤
        if (Snap && !Eraser && !_snapTried && time - _lastMoveTime >= HoldToSnap && Length(_pts) > Radius * 4f)
        {
            _snapTried = true;
            TrySnap();
        }
    }

    /// <summary>안 움직여도 시간은 흐름 (머묾 · 반듯하게 판단) — 매 프레임</summary>
    public void Tick(float time) => Move(_raw, time);

    public void Finish(float time)
    {
        if (!Active) return;
        Tick(time);
        FlushDwell();
        _finished = true;
        Active = false;
    }

    void FlushDwell()
    {
        if (_dwellAcc > 0.25f) _dwell.Add((_dwellPos, _dwellAcc));
        _dwellAcc = 0f;
    }

    // ══════════════════ 반듯하게 ══════════════════

    void TrySnap()
    {
        var s = _pts[0];
        var e = _pts[_pts.Count - 1];
        float len = Length(_pts);
        float chord = (e - s).magnitude;

        // 직선 — 모든 점이 처음~끝 선에서 조금만 벗어남
        if (chord > Radius * 3f)
        {
            float dev = 0f;
            foreach (var p in _pts) dev = Mathf.Max(dev, DistToSegment(p, s, e));
            if (dev < Mathf.Max(chord * 0.07f, Radius * 0.9f))
            {
                _shape = Shape.Line;
                _a = s;
                _b = e;
                return;
            }
        }

        // 원 · 타원 — 처음과 끝이 가까움 (닫힌 모양)
        if (chord < len * 0.22f && len > Radius * 8f)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in _pts) c += p;
            c /= _pts.Count;
            float mean = 0f;
            foreach (var p in _pts) mean += (p - c).magnitude;
            mean /= _pts.Count;
            float var = 0f;
            foreach (var p in _pts) { float k = (p - c).magnitude - mean; var += k * k; }
            float std = Mathf.Sqrt(var / _pts.Count);

            if (mean > 1f && std / mean < 0.13f)
            {
                _shape = Shape.Circle;
                _c = c;
                _rx = _ry = mean;
                _snapDist = Mathf.Max(1f, (_raw - c).magnitude);
                return;
            }

            Vector2 min = _pts[0], max = _pts[0];
            foreach (var p in _pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var cc = (min + max) * 0.5f;
            float rx = Mathf.Max(1f, (max.x - min.x) * 0.5f), ry = Mathf.Max(1f, (max.y - min.y) * 0.5f);
            float err = 0f;
            foreach (var p in _pts)
            {
                float u = (p.x - cc.x) / rx, v = (p.y - cc.y) / ry;
                err += Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 1f);
            }
            if (err / _pts.Count < 0.16f)
            {
                _shape = Shape.Ellipse;
                _c = cc;
                _rx = rx;
                _ry = ry;
                _snapDist = Mathf.Max(1f, (_raw - cc).magnitude);
            }
        }
    }

    /// <summary>반듯해진 뒤 끌기 — 직선은 끝점, 원은 크기</summary>
    void Adjust(Vector2 p)
    {
        if (_shape == Shape.Line) { _b = p; return; }
        float now = Mathf.Max(1f, (p - _c).magnitude);
        float k = now / _snapDist;
        _rx = Mathf.Max(1f, _rx * k);
        _ry = Mathf.Max(1f, _ry * k);
        _snapDist = now;
    }

    // ══════════════════ 붓 자국 만들기 ══════════════════

    /// <summary>지금까지의 획 → 붓 자국 (dabs를 비우고 채움)</summary>
    public void Build(List<PaintDab> dabs)
    {
        dabs.Clear();
        bool closed = BuildPath();
        if (_path.Count == 0) return;

        float R = Mathf.Max(0.5f, Radius);
        float spacing = Mathf.Max(0.75f, R * (Spray ? 0.16f : 0.1f));
        float hard = Eraser ? 0.95f : Spray ? 0.12f : 0.92f;
        float scale = Spray && !Eraser ? 1.35f : 1f;
        float total = Length(_path);
        bool taperStart = !Eraser && (_shape == Shape.None || _shape == Shape.Line);     // 지우개는 끝까지 같은 굵기
        bool taperEnd = taperStart && _finished && !closed;
        var rng = new System.Random(_seed);

        float walked = 0f;
        int index = 0;
        Emit(_path[0], 0f);
        for (int i = 1; i < _path.Count; i++)
        {
            var a = _path[i - 1];
            var b = _path[i];
            float seg = (b - a).magnitude;
            float carry = spacing - (walked % spacing);
            for (float s = carry; s <= seg; s += spacing) Emit(Vector2.Lerp(a, b, s / seg), walked + s);
            walked += seg;
        }
        if (total < spacing) Emit(_path[_path.Count - 1], total);

        // 흘러내림 — 머문 곳 + 획 끝
        if (Spray && Drips && !Eraser && DripDir.sqrMagnitude > 0.01f)
        {
            foreach (var (p, t) in _dwell) Drip(dabs, p, (t - 0.25f) * R * 7f, rng);
            if (_dwellAcc > 0.25f) Drip(dabs, _dwellPos, (_dwellAcc - 0.25f) * R * 7f, rng);
            if (_finished && !closed && rng.NextDouble() < 0.45) Drip(dabs, _path[_path.Count - 1], R * Mathf.Lerp(1.5f, 4f, (float)rng.NextDouble()), rng);
        }

        void Emit(Vector2 p, float at)
        {
            float k = 1f;
            if (taperStart) k *= Mathf.Lerp(0.45f, 1f, Smooth01(at / (R * 2.5f)));
            if (taperEnd) k *= Mathf.Lerp(0.35f, 1f, Smooth01((total - at) / (R * 2.5f)));
            float r = R * k * scale;
            dabs.Add(new PaintDab(p, r, 1f, SafeHard(hard, r)));

            // 스프레이 흩뿌림 — 같은 획은 같은 점 (매 프레임 다시 그려도 안 흔들림)
            if (Spray && !Eraser && !Outline)
            {
                var pr = new System.Random(_seed * 7919 + index * 31);
                if (pr.NextDouble() < 0.4)
                {
                    int n = 1 + pr.Next(2);
                    for (int j = 0; j < n; j++)
                    {
                        float ang = (float)(pr.NextDouble() * Mathf.PI * 2.0);
                        float dist = R * k * (1.05f + (float)pr.NextDouble() * 0.9f);
                        float rr = Mathf.Max(0.7f, R * (0.03f + (float)pr.NextDouble() * 0.07f));
                        dabs.Add(new PaintDab(p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist, rr, 1f, SafeHard(0.8f, rr)));
                    }
                }
            }
            index++;
        }
    }

    void Drip(List<PaintDab> dabs, Vector2 from, float length, System.Random rng)
    {
        float R = Radius;
        length = Mathf.Clamp(length, 0f, R * 9f);
        if (length < R * 0.5f) return;
        var dir = DripDir.normalized;
        float w = R * Mathf.Lerp(0.14f, 0.2f, (float)rng.NextDouble());     // 붓 굵기에 비례 (가늘게)
        float side = ((float)rng.NextDouble() - 0.5f) * R * 0.8f;
        var perp = new Vector2(-dir.y, dir.x);
        var start = from + perp * side + dir * R * 0.4f;
        float step = Mathf.Max(0.75f, w * 0.4f);
        for (float s = 0f; s <= length; s += step)
        {
            float k = Mathf.Lerp(1f, 0.8f, s / length);
            dabs.Add(new PaintDab(start + dir * s, w * k, 1f, SafeHard(0.85f, w)));
        }
        float bulb = w * 1.6f;
        dabs.Add(new PaintDab(start + dir * length, bulb, 1f, SafeHard(0.85f, bulb)));
    }

    /// <summary>지금 모양의 길 (반듯해졌으면 도형) — 닫힌 모양이면 true</summary>
    bool BuildPath()
    {
        _path.Clear();
        switch (_shape)
        {
            case Shape.Line:
                _path.Add(_a);
                _path.Add(_b);
                return false;
            case Shape.Circle:
            case Shape.Ellipse:
                float circ = 2f * Mathf.PI * Mathf.Sqrt((_rx * _rx + _ry * _ry) * 0.5f);
                int n = Mathf.Clamp(Mathf.CeilToInt(circ / Mathf.Max(2f, Radius * 0.5f)), 24, 720);
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n * Mathf.PI * 2f;
                    _path.Add(_c + new Vector2(Mathf.Cos(t) * _rx, Mathf.Sin(t) * _ry));
                }
                return true;
            default:
                _path.AddRange(_pts);
                if (!_finished && (_lazy - _pts[_pts.Count - 1]).sqrMagnitude > 0.01f) _path.Add(_lazy);
                if (Stabilize && !Eraser && _path.Count > 2) SmoothPath();
                return false;
        }
    }

    readonly List<Vector2> _tmp = new();

    /// <summary>손떨림 보정 2단계 — 앞뒤 점 평균으로 구불구불한 떨림을 폄 (끝점은 그대로)</summary>
    void SmoothPath()
    {
        int k = Mathf.Clamp(Mathf.RoundToInt(StabilizeAmount * 10f), 1, 40);     // 점 간격(반지름의 0.12) × k ≈ 반지름 × 세기
        _tmp.Clear();
        _tmp.AddRange(_path);
        for (int i = 1; i < _tmp.Count - 1; i++)
        {
            int w = Mathf.Min(k, Mathf.Min(i, _tmp.Count - 1 - i));
            Vector2 sum = Vector2.zero;
            for (int j = i - w; j <= i + w; j++) sum += _tmp[j];
            _path[i] = sum / (2 * w + 1);
        }
    }

    // ══════════════════ 도우미 ══════════════════

    /// <summary>작은 붓도 가장자리가 1.5픽셀은 부드럽게 (계단 안 생기게)</summary>
    static float SafeHard(float hard, float r) => Mathf.Clamp(Mathf.Min(hard, 1f - 1.5f / Mathf.Max(r, 0.01f)), 0f, 0.97f);

    static float Smooth01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    static float Length(List<Vector2> p)
    {
        float l = 0f;
        for (int i = 1; i < p.Count; i++) l += (p[i] - p[i - 1]).magnitude;
        return l;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return (p - (a + ab * t)).magnitude;
    }
}
