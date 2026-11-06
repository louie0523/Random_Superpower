using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 타이틀 '벽 칠하기' — 플레이어가 벽 · 바닥(PaintSurface)에 그래피티를 그림. 내 PC에 저장.
///   들어가면: 메뉴 숨김 · 카메라가 벽 쪽으로 · 캐릭터 · 소품(PaintHide) 숨김 · 아래 도구 막대
///   마우스 왼쪽 = 칠하기 · Ctrl+Z / Ctrl+Y = 되돌리기 / 다시 · [ ] = 붓 크기 · Esc 또는 '완료' = 저장하고 나가기
/// 씬에 칠할 면이 하나도 없으면 'Wall' · 'Ground' 이름의 물체에 저절로 붙여 줌 (예전 타이틀 씬도 바로 됨)
/// </summary>
public class TitlePaintMode : MonoBehaviour
{
    [Tooltip("칠할 때 카메라 자리 — 벽 (비우면 벽을 보고 저절로)")]
    public Transform PaintView;
    [Tooltip("칠할 때 카메라 자리 — 바닥 (비우면 바닥을 내려다보게 저절로)")]
    public Transform FloorView;
    public Camera Camera;
    [Tooltip("카메라 옮기는 시간 (초)")]
    public float MoveTime = 0.45f;
    [Header("시야 (카메라 FOV) — 도구 막대 '시야' · 마우스 휠")]
    public float MinFov = 22f;
    public float MaxFov = 75f;

    [Header("붓 크기 (미터, 반지름)")]
    public float MinRadius = 0.03f;
    public float MaxRadius = 0.6f;

    public static TitlePaintMode Instance { get; private set; }
    public bool IsOn => PaintMode.On && _bar != null;

    PaintToolbarView _bar;
    Action _onExit;
    readonly StrokeShaper _shaper = new();
    readonly List<PaintDab> _dabs = new();
    readonly List<PaintSurface[]> _history = new(), _redo = new();
    PaintSurface _surface;
    int _seed = 1;
    int _view;

    float _homeFov, _zoom = -1f;
    const string ZoomPref = "paint.fov";
    Vector3 _homePos, _toPos, _fromPos;
    Quaternion _homeRot, _toRot, _fromRot;
    float _moveT = 1f;

    void OnEnable() => Instance = this;
    void OnDisable() { if (Instance == this) Instance = null; }

    // ══════════════════ 들어가기 · 나가기 ══════════════════

    public void Enter(PaintToolbarView bar, Action onExit)
    {
        if (PaintMode.On) return;
        _bar = bar;
        _onExit = onExit;
        if (Camera == null) Camera = Camera.main;
        EnsureSurfaces();
        foreach (var s in PaintSurface.All) s.Init();

        if (_bar != null)
        {
            _bar.gameObject.SetActive(true);
            _bar.UndoClicked += Undo;
            _bar.RedoClicked += Redo;
            _bar.ClearClicked += ClearAll;
            _bar.DoneClicked += Exit;
            _bar.ViewPicked += SetView;
            _bar.ZoomChanged += SetZoom;
            _bar.ShowViews(true);
            _bar.SetView(0);
            _bar.ShowNow();
        }
        _view = 0;

        if (Camera != null)
        {
            _homePos = Camera.transform.position;
            _homeRot = Camera.transform.rotation;
            _homeFov = Camera.fieldOfView;
            if (_zoom < 0f) _zoom = PlayerPrefs.GetFloat(ZoomPref, Mathf.InverseLerp(MinFov, MaxFov, Mathf.Max(_homeFov, 38f)));
            SetZoom(_zoom);
            _bar?.SetZoom(_zoom);
            PaintPose(out _toPos, out _toRot);
            MoveCam(_toPos, _toRot);
        }
        PaintMode.Set(true);
    }

    public void Exit()
    {
        if (!PaintMode.On) return;
        EndStroke();
        foreach (var s in PaintSurface.All) s.SavePlayer();
        if (_bar != null)
        {
            _bar.UndoClicked -= Undo;
            _bar.RedoClicked -= Redo;
            _bar.ClearClicked -= ClearAll;
            _bar.DoneClicked -= Exit;
            _bar.ViewPicked -= SetView;
            _bar.ZoomChanged -= SetZoom;
            _bar.gameObject.SetActive(false);
        }
        if (Camera != null)
        {
            MoveCam(_homePos, _homeRot);
            Camera.fieldOfView = _homeFov;
        }
        PaintMode.Set(false);
        var cb = _onExit;
        _onExit = null;
        cb?.Invoke();
    }

    void OnApplicationQuit()
    {
        if (PaintMode.On) foreach (var s in PaintSurface.All) s.SavePlayer();
    }

    // ══════════════════ 매 프레임 ══════════════════

    void Update()
    {
        StepCam();
        if (!PaintMode.On || Camera == null) return;

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                    Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrl && Input.GetKeyDown(KeyCode.Z)) { if (shift) Redo(); else Undo(); }
        if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
        if (Input.GetKeyDown(KeyCode.Space) && _bar != null) _bar.ToggleHidden();
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f && !OverUi())
        {
            SetZoom(_zoom - wheel * 0.06f);
            _bar?.SetZoom(_zoom);
        }
        if (Input.GetKeyDown(KeyCode.Tab) && _surface == null)
        {
            SetView(1 - _view);
            _bar?.SetView(_view);
        }
        if (_bar != null)
        {
            if (Input.GetKeyDown(KeyCode.LeftBracket)) _bar.Nudge(-0.08f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) _bar.Nudge(0.08f);
        }

        var ray = Camera.ScreenPointToRay(Input.mousePosition);
        float t = Time.unscaledTime;

        if (Input.GetMouseButtonDown(0) && !OverUi()) BeginStroke(ray, t);

        if (_surface == null) return;
        if (Input.GetMouseButton(0))
        {
            if (_surface.Hit(ray, out var px)) _shaper.Move(px, t);
            else _shaper.Tick(t);
            Render();
        }
        else EndStroke();
    }

    static bool OverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    void BeginStroke(Ray ray, float t)
    {
        PaintSurface best = null;
        Vector2 bestPx = default;
        float bestD = float.MaxValue;
        foreach (var s in PaintSurface.All)
        {
            if (s == null || s.Canvas == null || s.Collider == null || !s.Collider.enabled) continue;
            if (!s.Collider.Raycast(ray, out var hit, 500f) || hit.distance >= bestD) continue;
            bestD = hit.distance;
            best = s;
            bestPx = new Vector2(hit.textureCoord.x * s.Size.x, hit.textureCoord.y * s.Size.y);
        }
        if (best == null) return;

        // 다른 물체(소품 등)가 앞에 있으면 안 칠함
        if (Physics.Raycast(ray, out var front, bestD - 0.01f) && front.collider.GetComponent<PaintSurface>() == null) return;

        _surface = best;
        var b = _bar != null ? _bar.Brush : new PaintBrush();
        _shaper.Radius = b.Radius(MinRadius, MaxRadius) * best.PixelsPerMeter;
        _shaper.Stabilize = b.Stabilize;
        _shaper.StabilizeAmount = b.StabilizeAmount;
        _shaper.Spray = b.IsSpray;
        _shaper.Drips = b.Drips;
        _shaper.Snap = b.Snap;
        _shaper.Eraser = b.IsEraser;
        _shaper.Outline = b.Outline;
        _shaper.DripDir = best.DripDirection;
        _shaper.Begin(bestPx, t, _seed++);
        Render();
    }

    void Render()
    {
        if (_surface == null) return;
        _shaper.Build(_dabs);
        var b = _bar != null ? _bar.Brush : new PaintBrush();
        _surface.Canvas.DrawStroke(b, _dabs, PaintBrush.OutlinePx(_shaper.Radius));
    }

    void EndStroke()
    {
        if (_surface == null) return;
        _shaper.Finish(Time.unscaledTime);
        Render();
        _surface.Canvas.Commit();
        _surface.Touched = true;
        _history.Add(new[] { _surface });
        _redo.Clear();
        _surface = null;
    }

    // ══════════════════ 되돌리기 ══════════════════

    void Undo()
    {
        if (_surface != null || _history.Count == 0) return;
        var last = _history[_history.Count - 1];
        _history.RemoveAt(_history.Count - 1);
        foreach (var s in last) if (s != null && s.Canvas != null) { s.Canvas.Undo(); s.Touched = true; }
        _redo.Add(last);
    }

    void Redo()
    {
        if (_surface != null || _redo.Count == 0) return;
        var last = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        foreach (var s in last) if (s != null && s.Canvas != null) { s.Canvas.Redo(); s.Touched = true; }
        _history.Add(last);
    }

    void ClearAll()
    {
        EndStroke();
        var all = PaintSurface.All.ToArray();
        foreach (var s in all)
            if (s.Canvas != null)
            {
                s.Canvas.ClearPaint();
                s.Touched = true;
            }
        _history.Add(all);
        _redo.Clear();
    }

    // ══════════════════ 카메라 ══════════════════

    /// <summary>시야 0~1 → FOV (넓히면 벽 위아래 · 바닥 끝까지)</summary>
    public void SetZoom(float v)
    {
        _zoom = Mathf.Clamp01(v);
        if (Camera != null) Camera.fieldOfView = Mathf.Lerp(MinFov, MaxFov, _zoom);
        try { PlayerPrefs.SetFloat(ZoomPref, _zoom); } catch { /* 기억 못 해도 됨 */ }
    }

    /// <summary>보는 곳 바꾸기 (0 = 벽 · 1 = 바닥) — Tab으로도</summary>
    public void SetView(int view)
    {
        if (!PaintMode.On || Camera == null) return;
        EndStroke();
        _view = Mathf.Clamp(view, 0, 1);
        Vector3 pos;
        Quaternion rot;
        if (_view == 1) FloorPose(out pos, out rot);
        else PaintPose(out pos, out rot);
        MoveCam(pos, rot);
    }

    void FloorPose(out Vector3 pos, out Quaternion rot)
    {
        if (FloorView != null)
        {
            pos = FloorView.position;
            rot = FloorView.rotation;
            return;
        }
        pos = Camera.transform.position;
        rot = Camera.transform.rotation;

        // 가장 큰 눕힌 면(바닥)을 비스듬히 내려다봄 — 캐릭터가 서던 곳 앞쪽
        PaintSurface floor = null;
        float best = 0f;
        foreach (var s in PaintSurface.All)
        {
            if (s == null || Mathf.Abs(s.transform.forward.y) < 0.5f) continue;
            var size = s.GetComponent<Renderer>().bounds.size;
            if (size.x * size.z > best) { best = size.x * size.z; floor = s; }
        }
        if (floor == null) return;
        var b = floor.GetComponent<Renderer>().bounds;
        var toCam = _homePos - b.center;
        toCam.y = 0f;
        toCam = toCam.sqrMagnitude > 0.01f ? toCam.normalized : Vector3.forward;
        var aim = new Vector3(_homePos.x, b.max.y, _homePos.z) - toCam * 3.2f;
        pos = aim + toCam * 3.6f + Vector3.up * 6.5f;
        rot = Quaternion.LookRotation(aim - pos, Vector3.up);
    }

    void PaintPose(out Vector3 pos, out Quaternion rot)
    {
        if (PaintView != null)
        {
            pos = PaintView.position;
            rot = PaintView.rotation;
            return;
        }
        pos = Camera.transform.position;
        rot = Camera.transform.rotation;

        // 가장 큰 세로 면(벽)을 화면 가득
        PaintSurface wall = null;
        float best = 0f;
        foreach (var s in PaintSurface.All)
        {
            if (s == null) continue;
            var n = -s.transform.forward;
            if (Mathf.Abs(n.y) > 0.5f) continue;        // 바닥 빼고
            var size = s.GetComponent<Renderer>().bounds.size;
            float area = size.x * size.y + size.z * size.y;
            if (area > best) { best = area; wall = s; }
        }
        if (wall == null) return;

        var bounds = wall.GetComponent<Renderer>().bounds;
        var normal = -wall.transform.forward;
        if (Vector3.Dot(normal, Camera.transform.position - bounds.center) < 0f) normal = -normal;
        float fov = Camera.fieldOfView * Mathf.Deg2Rad;
        float h = Mathf.Min(bounds.size.y, 6f) * 0.85f;
        float dist = h * 0.5f / Mathf.Tan(fov * 0.5f);
        var aim = new Vector3(bounds.center.x, Mathf.Min(bounds.center.y, bounds.min.y + h * 0.55f), bounds.center.z);
        pos = aim + normal * dist + Vector3.up * 0.4f;
        rot = Quaternion.LookRotation(aim - pos, Vector3.up);
    }

    void MoveCam(Vector3 pos, Quaternion rot)
    {
        _fromPos = Camera.transform.position;
        _fromRot = Camera.transform.rotation;
        _toPos = pos;
        _toRot = rot;
        _moveT = 0f;
    }

    void StepCam()
    {
        if (_moveT >= 1f || Camera == null) return;
        _moveT = Mathf.Min(1f, _moveT + Time.unscaledDeltaTime / Mathf.Max(0.01f, MoveTime));
        float k = _moveT * _moveT * (3f - 2f * _moveT);
        Camera.transform.SetPositionAndRotation(Vector3.Lerp(_fromPos, _toPos, k), Quaternion.Slerp(_fromRot, _toRot, k));
    }

    // ══════════════════ 칠할 면 저절로 ══════════════════

    /// <summary>칠할 면이 없으면 'Wall' · 'Ground' · 'Floor' 물체에 붙임 (재질 색 · 그림을 바탕으로)</summary>
    void EnsureSurfaces()
    {
        if (PaintSurface.All.Count > 0) return;
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            string n = r.name.ToLowerInvariant();
            bool wall = n == "wall", floor = n == "ground" || n == "floor";
            if (!wall && !floor) continue;
            if (r.GetComponent<MeshFilter>() == null) continue;
            var mat = r.sharedMaterial;
            var s = r.gameObject.AddComponent<PaintSurface>();
            s.Id = wall ? "wall" : "floor";
            s.Size = wall ? new Vector2Int(2048, 1024) : new Vector2Int(1024, 1024);
            s.DripDirection = wall ? new Vector2(0f, -1f) : Vector2.zero;
            if (mat != null)
            {
                // 예전 씬: 그래피티가 구워진 그림 → 지울 수 있는 기본 그래피티로 · 지우면 빈 면 색
                var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") as Texture2D : null;
                if (tex != null) s.DefaultPaint = tex;
                else if (mat.HasProperty("_BaseColor")) s.BaseColor = mat.GetColor("_BaseColor");
                if (!wall) s.BaseColor = new Color(0.15f, 0.13f, 0.19f, 1f);
            }
            Debug.Log($"[칠하기] '{r.name}'에 칠할 면을 붙임 — 씬에 PaintSurface를 직접 넣어 두면 이 단계는 안 함", r);
        }
    }
}
