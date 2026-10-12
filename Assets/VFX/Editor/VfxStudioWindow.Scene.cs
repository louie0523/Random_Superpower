using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ToonVfx.EditorTools
{
    /// <summary>
    /// 씬 뷰 — 그리기 도구(드래그해서 레이어 만들기)와 선택한 레이어의 크기 손잡이.
    /// 그리는 면: '화면' = 카메라와 마주 보는 면 (보이는 그대로), '바닥' = 수평면
    /// </summary>
    public partial class VfxStudioWindow
    {
        bool _stroking;
        bool _strokeOnGround;
        readonly List<Vector3> _stroke = new();
        Plane _strokePlane;
        Vector3 _strokeCamFwd;
        float _pillarHeight;

        static readonly Color HandleColor = new(1f, 0.85f, 0.3f);

        public static VfxShaderProfile ProfileFor(VfxLayerStyle s) =>
            AssetDatabase.LoadAssetAtPath<VfxShaderProfile>(
                $"{VfxSetup.Root}/Shaders/{(s == VfxLayerStyle.Glow ? "Profile_Additive" : "Profile_Clip")}.asset");

        void OnSceneGUI(SceneView sv)
        {
            if (_fx == null || EditorApplication.isPlayingOrWillChangePlaymode) { Tools.hidden = false; return; }
            VfxEffect.CameraOverride = sv.camera;
            Tools.hidden = _tool != DrawTool.Select;      // 그리는 동안 Unity 이동 손잡이를 숨겨 드래그가 그리기로만 가게

            if (_tool != DrawTool.Select)
            {
                HandleDrawing(sv);
                DrawSceneHint();
                return;
            }

            if (_sel is VfxLayer l && l != null) DrawLayerHandles(l, sv);
            else if (_sel is VfxLight li && li != null) DrawLightHandles(li);
        }

        void DrawSceneHint()
        {
            string tip = _tool switch
            {
                DrawTool.Arc => "<b>호</b> — 칼을 휘두르듯 드래그하세요. 그은 궤적이 슬래시가 돼요.",
                DrawTool.Ring => _drawOnGround ? "<b>원</b> — 바닥 고리: 가운데를 누르고 드래그해 크기" : "<b>원</b> — 정면 충격파: 가운데를 누르고 드래그해 크기",
                DrawTool.Pillar => "<b>기둥</b> — 바닥을 누르고 위로 드래그해 높이",
                DrawTool.Streak => "<b>속도선</b> — 달려가는 방향으로 드래그",
                DrawTool.Trail => "<b>잔상 줄</b> — 움직이는 방향으로 드래그 (게임에서 실제 이동 경로를 따라 그려짐)",
                DrawTool.Stamp => $"<b>스탬프</b> ({VfxStudioGUI.MaskName(_stampMask)}) — 누르고 드래그해 크기",
                DrawTool.Burst => $"<b>뿌리기</b> ({VfxStudioGUI.MaskName(_stampMask)}) — 누르고 드래그해 퍼지는 범위",
                DrawTool.Light => "<b>조명</b> — 누른 자리에 순간 조명",
                DrawTool.Cone => "<b>원뿔</b> — 몸에서 돌진하는 방향으로 드래그 (길이 = 원뿔 길이)",
                _ => "",
            };
            Handles.BeginGUI();
            GUI.Label(new Rect(10, 10, 430, 44), tip + "\n<size=10>Shift를 누른 채 놓으면 계속 그리기 · Alt+드래그 화면 회전 · Esc 그리기 끄기</size>", VfxStudioGUI.SceneHint);
            Handles.EndGUI();
        }

        // ══════════════════════ 그리기 ══════════════════════

        void HandleDrawing(SceneView sv)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _stroking = false;
                _tool = DrawTool.Select;
                e.Use();
                Repaint();
                return;
            }

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || e.alt) break;
                    if (!BeginStroke(sv, e.mousePosition)) break;
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    ContinueStroke(e.mousePosition);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    ContinueStroke(e.mousePosition);
                    FinishStroke(e.shift);
                    e.Use();
                    break;

                case EventType.Repaint:
                    DrawStrokePreview();
                    break;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) sv.Repaint();
        }

        Plane DrawPlane(SceneView sv, bool ground)
        {
            var root = _fx.transform.position;
            if (ground) return new Plane(Vector3.up, root);

            // 화면면 — 이펙트 기준점을 지나되, 카메라에서 너무 가까우면 (1인칭) 1.2m 앞으로
            var cam = sv.camera.transform;
            float depth = Vector3.Dot(root - cam.position, cam.forward);
            var point = depth >= 1.2f ? root : cam.position + cam.forward * 1.2f;
            return new Plane(-cam.forward, point);
        }

        bool Hit(Vector2 mouse, out Vector3 p)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (_strokePlane.Raycast(ray, out float d) && d > 0f)
            {
                p = ray.GetPoint(d);
                return true;
            }
            p = default;
            return false;
        }

        bool BeginStroke(SceneView sv, Vector2 mouse)
        {
            _strokeOnGround = _tool == DrawTool.Pillar || _drawOnGround;
            _strokePlane = DrawPlane(sv, _strokeOnGround);
            _strokeCamFwd = sv.camera.transform.forward;
            _pillarHeight = 0f;

            if (!Hit(mouse, out var p)) return false;
            StopPlay();
            _stroking = true;
            _stroke.Clear();
            _stroke.Add(p);
            return true;
        }

        void ContinueStroke(Vector2 mouse)
        {
            if (!_stroking) return;

            if (_tool == DrawTool.Pillar)
            {
                _pillarHeight = HeightOnVertical(HandleUtility.GUIPointToWorldRay(mouse), _stroke[0]);
                return;
            }
            if (Hit(mouse, out var p) && (p - _stroke[_stroke.Count - 1]).sqrMagnitude > 0.0004f)
                _stroke.Add(p);
        }

        /// <summary>시선 광선과 기둥 세로선이 가장 가까운 지점의 높이</summary>
        static float HeightOnVertical(Ray ray, Vector3 basePoint)
        {
            Vector3 u = ray.direction, v = Vector3.up, w0 = ray.origin - basePoint;
            float a = Vector3.Dot(u, u), b = Vector3.Dot(u, v), c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w0), e = Vector3.Dot(v, w0);
            float den = a * c - b * b;
            return Mathf.Abs(den) < 1e-6f ? 0f : (a * e - b * d) / den;
        }

        Vector3 StrokeStart => _stroke[0];
        Vector3 StrokeEnd => _stroke[_stroke.Count - 1];
        float StrokeLength => (StrokeEnd - StrokeStart).magnitude;

        void DrawStrokePreview()
        {
            if (!_stroking || _stroke.Count == 0) return;
            Handles.color = VfxStudioGUI.DrawStroke;
            var n = _strokeOnGround ? Vector3.up : -_strokeCamFwd;
            float len = StrokeLength;
            string label = null;

            switch (_tool)
            {
                case DrawTool.Arc:
                    if (_stroke.Count > 1) Handles.DrawAAPolyLine(4f, _stroke.ToArray());
                    break;
                case DrawTool.Ring:
                    Handles.DrawWireDisc(StrokeStart, n, Mathf.Max(0.1f, len));
                    label = $"반지름 {len:0.00}m";
                    break;
                case DrawTool.Pillar:
                {
                    float h = Mathf.Max(0.3f, _pillarHeight);
                    Handles.DrawWireDisc(StrokeStart, Vector3.up, 0.5f);
                    Handles.DrawWireDisc(StrokeStart + Vector3.up * h, Vector3.up, 0.3f);
                    Handles.DrawAAPolyLine(3f, StrokeStart, StrokeStart + Vector3.up * h);
                    label = $"높이 {h:0.00}m";
                    break;
                }
                case DrawTool.Streak:
                case DrawTool.Trail:
                case DrawTool.Cone:
                    Handles.DrawAAPolyLine(4f, StrokeStart, StrokeEnd);
                    if (len > 0.05f)
                        Handles.ConeHandleCap(0, StrokeEnd, Quaternion.LookRotation(StrokeEnd - StrokeStart), HandleUtility.GetHandleSize(StrokeEnd) * 0.15f, EventType.Repaint);
                    break;
                case DrawTool.Stamp:
                    Handles.DrawWireDisc(StrokeStart, n, Mathf.Max(0.12f, len));
                    label = $"크기 {Mathf.Max(0.25f, len * 2f):0.00}m";
                    break;
                case DrawTool.Burst:
                    Handles.DrawWireDisc(StrokeStart, n, Mathf.Max(0.3f, len));
                    label = $"퍼지는 범위 {Mathf.Max(0.3f, len):0.00}m";
                    break;
                case DrawTool.Light:
                    Handles.DrawWireDisc(StrokeStart, n, 0.2f);
                    break;
            }
            if (label != null) Handles.Label(StrokeEnd + Vector3.up * 0.1f, label, EditorStyles.whiteBoldLabel);
        }

        void FinishStroke(bool keepTool)
        {
            if (!_stroking || _fx == null) { _stroking = false; return; }
            _stroking = false;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            Component made = _tool switch
            {
                DrawTool.Arc => MakeArc(),
                DrawTool.Ring => MakeRing(),
                DrawTool.Pillar => MakePillar(),
                DrawTool.Streak => MakeStreak(),
                DrawTool.Trail => MakeTrail(),
                DrawTool.Stamp => MakeStamp(),
                DrawTool.Burst => MakeBurst(),
                DrawTool.Light => CreateLight(StrokeStart),
                DrawTool.Cone => MakeCone(),
                _ => null,
            };

            if (made != null)
            {
                Undo.RegisterCreatedObjectUndo(made.gameObject, "VFX 그리기");
                Undo.CollapseUndoOperations(group);
                CollectRows();
                Select(made);

                float t = made is VfxLayer l
                    ? l.StartTime + l.Blocks.AppearTime + l.Blocks.HoldTime * 0.5f + 0.01f
                    : ((VfxLight)made).StartTime + 0.02f;
                SetScrub(t);
                MarkDirty();
            }

            if (!keepTool) _tool = DrawTool.Select;
            Repaint();
            SceneView.RepaintAll();
        }

        float SizeDiv => Mathf.Max(0.01f, _fx.Size);

        // ── 호: 그은 점들에 원을 맞춰 반지름·각도·방향을 정함 ──
        Component MakeArc()
        {
            if (_stroke.Count < 2 || StrokeLength < 0.15f)
            {
                ShowNotification(new GUIContent("조금 더 길게 그어 주세요"));
                return null;
            }

            Vector3 N = _strokeOnGround ? Vector3.up : -_strokeCamFwd;
            Vector3 U = Vector3.Cross(N, Vector3.up);
            if (U.sqrMagnitude < 1e-4f) U = Vector3.Cross(N, Vector3.right);
            U.Normalize();
            Vector3 V = Vector3.Cross(N, U).normalized;
            Vector3 o = StrokeStart;

            var pts = new List<Vector2>(_stroke.Count);
            foreach (var p in _stroke) pts.Add(new Vector2(Vector3.Dot(p - o, U), Vector3.Dot(p - o, V)));

            Vector2 rootP = new(Vector3.Dot(_fx.transform.position - o, U), Vector3.Dot(_fx.transform.position - o, V));
            FitArc(pts, rootP, out var c, out float R, out float sweep);

            float absSweep = Mathf.Clamp(Mathf.Abs(sweep), 10f, 360f);
            float sign = sweep >= 0f ? 1f : -1f;
            float a0 = Mathf.Atan2(pts[0].y - c.y, pts[0].x - c.x);
            float aMid = a0 + sign * absSweep * 0.5f * Mathf.Deg2Rad;
            Vector3 mid = (U * Mathf.Cos(aMid) + V * Mathf.Sin(aMid)).normalized;
            Vector3 center = o + U * c.x + V * c.y;

            var l = CreateLayer(VfxShape.Arc, "호", _drawStyle);
            l.transform.SetPositionAndRotation(center, Quaternion.LookRotation(mid, N));
            l.Arc.Radius = R / SizeDiv;
            l.Arc.Angle = absSweep;
            l.Arc.Reverse = sign < 0f;     // 메시는 지역 -X → +X로 그어짐 = 각도가 커지는 방향
            l.Arc.Width = Mathf.Clamp(R * 0.35f, 0.08f, 0.8f) / SizeDiv;
            return l;
        }

        /// <summary>
        /// 2D 점들에 원을 맞춤 (중심 이동 대수 피팅). 거의 직선이면 바깥쪽(기준점 반대)으로 살짝 휜 40° 호로 대신함.
        /// sweep = 시작점 → 끝점으로 돈 각도 (°, 부호 = 방향)
        /// </summary>
        static void FitArc(List<Vector2> pts, Vector2 root, out Vector2 center, out float radius, out float sweep)
        {
            int n = pts.Count;
            Vector2 mean = Vector2.zero;
            foreach (var p in pts) mean += p;
            mean /= n;

            double suu = 0, svv = 0, suv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
            foreach (var p in pts)
            {
                double u = p.x - mean.x, v = p.y - mean.y;
                suu += u * u; svv += v * v; suv += u * v;
                suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
            }

            Vector2 a = pts[0], b = pts[n - 1];
            float chord = (b - a).magnitude;
            double det = suu * svv - suv * suv;
            bool ok = n >= 3 && det > 1e-10;

            center = default;
            radius = 0f;
            if (ok)
            {
                double ru = 0.5 * (suuu + suvv), rv = 0.5 * (svvv + svuu);
                double uc = (ru * svv - rv * suv) / det;
                double vc = (rv * suu - ru * suv) / det;
                center = new Vector2((float)uc + mean.x, (float)vc + mean.y);
                radius = Mathf.Sqrt((float)(uc * uc + vc * vc + (suu + svv) / n));
                ok = radius > 0.05f && radius < chord * 3f;     // 너무 평평하면 직선으로 봄
            }

            if (ok)
            {
                // 지나온 각도를 이어 붙여 실제로 돈 양과 방향을 셈
                float total = 0f;
                float prev = Mathf.Atan2(a.y - center.y, a.x - center.x);
                for (int i = 1; i < n; i++)
                {
                    float ang = Mathf.Atan2(pts[i].y - center.y, pts[i].x - center.x);
                    total += Mathf.DeltaAngle(prev * Mathf.Rad2Deg, ang * Mathf.Rad2Deg);
                    prev = ang;
                }
                sweep = total;
                return;
            }

            // 대신: 40° 호. 기준점(몸) 반대쪽으로 볼록하게 — 몸을 감싸며 휘두른 모양
            const float half = 20f * Mathf.Deg2Rad;
            Vector2 m = (a + b) * 0.5f;
            Vector2 dir = chord > 1e-4f ? (b - a) / chord : Vector2.right;
            Vector2 nrm = new(-dir.y, dir.x);
            float bulge = Vector2.Dot(root - m, nrm) > 0f ? -1f : 1f;
            radius = Mathf.Max(0.1f, chord / (2f * Mathf.Sin(half)));
            center = m - nrm * bulge * radius * Mathf.Cos(half);

            Vector2 ca = a - center, cb = b - center;
            float cross = ca.x * cb.y - ca.y * cb.x;
            sweep = (cross >= 0f ? 1f : -1f) * 40f;
        }

        Component MakeRing()
        {
            float R = Mathf.Max(0.1f, StrokeLength);
            var l = CreateLayer(VfxShape.Ring, "링", _drawStyle);
            l.transform.SetPositionAndRotation(StrokeStart, Quaternion.identity);
            l.Ring.Facing = _strokeOnGround ? VfxFacing.Fixed : VfxFacing.Billboard;
            l.Ring.Radius = R / SizeDiv;
            l.Ring.Width = Mathf.Clamp(R * 0.18f, 0.04f, 0.5f) / SizeDiv;
            return l;
        }

        Component MakePillar()
        {
            float h = _pillarHeight >= 0.3f ? _pillarHeight : 2f;
            var l = CreateLayer(VfxShape.Pillar, "기둥", _drawStyle);
            l.transform.SetPositionAndRotation(StrokeStart, Quaternion.identity);
            l.Pillar.Height = h / SizeDiv;
            l.Pillar.Radius = 0.5f / SizeDiv;
            l.Pillar.TopRadius = 0.3f / SizeDiv;
            return l;
        }

        /// <summary>드래그 방향 (너무 짧으면 이펙트 앞쪽)</summary>
        Quaternion StrokeDirection(out float len)
        {
            len = StrokeLength;
            var dir = StrokeEnd - StrokeStart;
            if (len < 0.3f) { dir = _fx.transform.forward; len = 0f; }
            var up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(dir, up);
        }

        Component MakeStreak()
        {
            var rot = StrokeDirection(out float len);
            if (len <= 0f) len = 4f;
            var l = CreateLayer(VfxShape.Streak, "속도선", _drawStyle);
            l.transform.SetPositionAndRotation(StrokeStart, rot);
            l.Streak.Front = len * 0.5f / SizeDiv;
            l.Streak.Back = -len * 0.7f / SizeDiv;
            return l;
        }

        Component MakeCone()
        {
            var rot = StrokeDirection(out float len);
            if (len <= 0f) len = 2.6f;
            var l = CreateLayer(VfxShape.Cone, "원뿔", _drawStyle);
            l.transform.SetPositionAndRotation(StrokeStart, rot);
            l.Cone.Front = len * 0.55f / SizeDiv;
            l.Cone.Back = -len * 0.45f / SizeDiv;
            l.Cone.BaseRadius = Mathf.Clamp(len * 0.42f, 0.3f, 2.5f) / SizeDiv;
            return l;
        }

        Component MakeTrail()
        {
            var rot = StrokeDirection(out float len);
            var l = CreateLayer(VfxShape.Trail, "잔상", _drawStyle);
            l.transform.SetPositionAndRotation(StrokeStart, rot);
            l.Trail.PreviewLength = Mathf.Max(1f, len) / SizeDiv;
            return l;
        }

        Component MakeStamp()
        {
            float s = Mathf.Max(0.25f, StrokeLength * 2f) / SizeDiv;
            var l = CreateLayer(VfxShape.Card, "스탬프", _drawStyle);
            l.transform.position = StrokeStart;
            l.Card.Size = new Vector2(s, s);
            return l;
        }

        Component MakeBurst()
        {
            float reach = Mathf.Max(0.3f, StrokeLength) / SizeDiv;
            var l = CreateLayer(VfxShape.Burst, "뿌리기", _drawStyle);
            l.transform.position = StrokeStart;
            l.Burst.Speed = reach * l.Burst.Drag;      // 감속이 있으면 멈추는 거리 = 속도 / 감속
            return l;
        }

        // ══════════════════════ 레이어 기본값 ══════════════════════

        /// <summary>도형마다 바로 쓸 만한 기본값으로 레이어를 만듦 (Undo 등록은 부르는 쪽)</summary>
        VfxLayer CreateLayer(VfxShape shape, string baseName, VfxLayerStyle style)
        {
            var go = new GameObject(UniqueName(baseName));
            go.transform.SetParent(_fx.transform, false);
            var l = go.AddComponent<VfxLayer>();
            l.Shape = shape;
            l.Style = style;
            l.Profile = ProfileFor(style);

            if (style == VfxLayerStyle.Glow)
            {
                l.Softness = 0.25f;
                l.CoreGlow = 0.8f;
                l.Brightness = 2f;
            }
            else
            {
                l.Brightness = 1.5f;
                l.OutlineWidth = 0.06f;
            }

            var b = l.Blocks;
            switch (shape)
            {
                case VfxShape.Arc:
                    // 블록 기본값 = 쓸고 지나감 → 꼬리가 따라오며
                    l.Arc.Radius = 1.2f;
                    l.Arc.Angle = 140f;
                    l.Arc.Width = 0.4f;
                    if (style == VfxLayerStyle.Glow) { l.FlameEdge = 0.35f; l.FlameScale = 8f; l.Wobble = 0.2f; }
                    else l.FlameEdge = 0.4f;
                    break;

                case VfxShape.Ring:
                    SetBlocks(b, VfxAppear.Grow, 0.08f, VfxHold.Still, 0f, VfxVanish.ExpandFade, 0.22f);
                    l.Ring.Radius = 1f;
                    l.Ring.Width = 0.18f;
                    break;

                case VfxShape.Pillar:
                    SetBlocks(b, VfxAppear.Rise, 0.2f, VfxHold.Wobble, 0.6f, VfxVanish.ErodeFromEnd, 0.3f);
                    b.HoldStrength = 0.3f;
                    l.FlameEdge = 0.45f;
                    l.FlameScale = 6f;
                    l.FlameSpeed = 2.5f;
                    break;

                case VfxShape.Streak:
                    SetBlocks(b, VfxAppear.Fade, 0.08f, VfxHold.Still, 0.3f, VfxVanish.ErodeFromEnd, 0.15f);
                    l.Wobble = 0f;
                    break;

                case VfxShape.Trail:
                    SetBlocks(b, VfxAppear.Fade, 0.05f, VfxHold.Still, 0.3f, VfxVanish.ErodeFromEnd, 0.2f);
                    l.Wobble = 0.1f;
                    break;

                case VfxShape.Cone:
                    SetBlocks(b, VfxAppear.Grow, 0.06f, VfxHold.Still, 0.25f, VfxVanish.ErodeFromEnd, 0.15f);
                    l.Wobble = 0.05f;
                    if (style == VfxLayerStyle.Glow) { l.Brightness = 2.2f; l.Softness = 0.3f; l.CoreGlow = 1f; }
                    break;

                case VfxShape.Card:
                    SetBlocks(b, VfxAppear.Grow, 0.06f, VfxHold.Still, 0.05f, VfxVanish.Hole, 0.2f);
                    l.AutoMask = false;
                    l.BuiltinMask = _stampMask;
                    l.Card.Facing = VfxFacing.Billboard;
                    l.Wobble = 0.1f;
                    break;

                case VfxShape.Burst:
                    SetBlocks(b, VfxAppear.Pop, 0.03f, VfxHold.Still, 0.05f, VfxVanish.ErodeFromEnd, 0.2f);
                    l.Burst.Count = 12;
                    l.Burst.Drag = 5f;
                    l.Burst.Speed = 5f;
                    l.Burst.LifeRandom = 0.4f;
                    l.Wobble = 0.05f;
                    if (_stampMask == VfxBuiltinMask.Spike)
                    {
                        l.AutoMask = true;
                        l.Burst.Facing = VfxBurstFacing.Velocity;
                        l.Burst.Stretch = 2.5f;
                    }
                    else
                    {
                        l.AutoMask = false;
                        l.BuiltinMask = _stampMask;
                        l.Burst.RandomRotation = !IsSymbol(_stampMask);
                    }
                    break;
            }
            return l;
        }

        /// <summary>똑바로 서 있어야 알아보는 모양 — 돌리지 않음</summary>
        static bool IsSymbol(VfxBuiltinMask m) =>
            m is VfxBuiltinMask.Star or VfxBuiltinMask.Star5 or VfxBuiltinMask.ArrowDown or VfxBuiltinMask.Skull
              or VfxBuiltinMask.FlameTongue or VfxBuiltinMask.Flame or VfxBuiltinMask.Droplet;

        static void SetBlocks(VfxBlocks b, VfxAppear a, float at, VfxHold h, float ht, VfxVanish v, float vt)
        {
            b.Appear = a; b.AppearTime = at;
            b.Hold = h; b.HoldTime = ht;
            b.Vanish = v; b.VanishTime = vt;
        }

        VfxLight CreateLight(Vector3 worldPos)
        {
            var go = new GameObject(UniqueName("조명"));
            go.transform.SetParent(_fx.transform, false);
            go.transform.position = worldPos;
            var li = go.AddComponent<VfxLight>();
            li.Mode = _fx.PlayMode == VfxPlayMode.Once ? VfxLightMode.Flash : VfxLightMode.Sustain;
            li.Intensity = li.Mode == VfxLightMode.Flash ? 5f : 1.5f;
            li.Range = 4f;
            li.Duration = 0.25f;
            return li;
        }

        // ══════════════════════ 크기 손잡이 ══════════════════════

        void DrawLayerHandles(VfxLayer l, SceneView sv)
        {
            float size = SizeDiv;
            var t = l.transform;
            var camRot = sv.camera.transform.rotation;

            Matrix4x4 m;
            if (l.Shape == VfxShape.Ring && l.Ring.Facing == VfxFacing.Billboard)
                m = Matrix4x4.TRS(t.position, camRot * Quaternion.Euler(-90f, 0f, 0f), t.lossyScale * size);
            else if (l.Shape == VfxShape.Card && l.Card.Facing == VfxFacing.Billboard)
                m = Matrix4x4.TRS(t.position, camRot, t.lossyScale * size);
            else if (l.Shape == VfxShape.Burst)
                m = Matrix4x4.TRS(t.position, t.rotation, Vector3.one * size);
            else
                m = t.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * size);

            using (new Handles.DrawingScope(HandleColor, m))
            {
                switch (l.Shape)
                {
                    case VfxShape.Arc: ArcHandles(l); break;
                    case VfxShape.Ring: RingHandles(l); break;
                    case VfxShape.Pillar: PillarHandles(l); break;
                    case VfxShape.Card: CardHandles(l); break;
                    case VfxShape.Streak: StreakHandles(l); break;
                    case VfxShape.Burst: BurstHandles(l); break;
                    case VfxShape.Trail: TrailHandles(l); break;
                    case VfxShape.Cone: ConeHandles(l); break;
                }
            }
        }

        static bool Slider(Vector3 pos, Vector3 dir, out Vector3 np)
        {
            float hs = HandleUtility.GetHandleSize(pos) * 0.09f;
            EditorGUI.BeginChangeCheck();
            np = Handles.Slider(pos, dir, hs, Handles.DotHandleCap, 0f);
            return EditorGUI.EndChangeCheck();
        }

        void Edit(VfxLayer l, string what, System.Action change)
        {
            Undo.RecordObject(l, what);
            change();
            Changed(l);
        }

        void ArcHandles(VfxLayer l)
        {
            var a = l.Arc;
            float thick = Mathf.Max(0.01f, _fx.Thickness);
            float R = a.Radius, W = Mathf.Min(a.Width * thick, R);
            float half = a.Angle * 0.5f * Mathf.Deg2Rad;
            var from = new Vector3(Mathf.Sin(-half), 0f, Mathf.Cos(-half));
            Handles.DrawWireArc(Vector3.zero, Vector3.up, from, a.Angle, R);
            Handles.DrawWireArc(Vector3.zero, Vector3.up, from, a.Angle, R - W);

            if (Slider(new Vector3(0f, 0f, R), Vector3.forward, out var np))
                Edit(l, "VFX 호 반지름", () => l.Arc.Radius = Mathf.Max(0.05f, np.z));

            if (Slider(new Vector3(0f, 0f, R - W), Vector3.forward, out np))
                Edit(l, "VFX 호 두께", () => l.Arc.Width = Mathf.Clamp(R - np.z, 0.01f, R) / thick);

            // 머리(그어지는 끝) — 접선 방향으로 끌면 각도가 양쪽으로 같이 바뀜
            float sign = a.Reverse ? -1f : 1f;
            float ang = sign * half;
            var head = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * R;
            var tangent = new Vector3(Mathf.Cos(ang), 0f, -Mathf.Sin(ang)) * sign;
            if (Slider(head, tangent, out np))
                Edit(l, "VFX 호 각도", () => l.Arc.Angle = Mathf.Clamp(2f * Mathf.Abs(Mathf.Atan2(np.x, np.z)) * Mathf.Rad2Deg, 10f, 360f));

            if (Event.current.type == EventType.Repaint)
                Handles.ConeHandleCap(0, head + tangent * HandleUtility.GetHandleSize(head) * 0.2f,
                    Quaternion.LookRotation(tangent, Vector3.up), HandleUtility.GetHandleSize(head) * 0.12f, EventType.Repaint);

            Handles.Label(new Vector3(0f, 0f, R) + Vector3.up * 0.05f, $"반지름 {R * SizeDiv:0.00}m  ·  {a.Angle:0}°", EditorStyles.whiteMiniLabel);
        }

        void RingHandles(VfxLayer l)
        {
            float thick = Mathf.Max(0.01f, _fx.Thickness);
            float R = l.Ring.Radius, W = Mathf.Min(l.Ring.Width * thick, R);
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, R);
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, Mathf.Max(0f, R - W));

            if (Slider(new Vector3(0f, 0f, R), Vector3.forward, out var np))
                Edit(l, "VFX 링 반지름", () => l.Ring.Radius = Mathf.Max(0.02f, np.z));
            if (Slider(new Vector3(0f, 0f, R - W), Vector3.forward, out np))
                Edit(l, "VFX 링 두께", () => l.Ring.Width = Mathf.Clamp(R - np.z, 0.01f, R) / thick);

            Handles.Label(new Vector3(0f, 0f, R), $"반지름 {R * SizeDiv:0.00}m", EditorStyles.whiteMiniLabel);
        }

        void PillarHandles(VfxLayer l)
        {
            var p = l.Pillar;
            float thick = Mathf.Max(0.01f, _fx.Thickness);
            float r0 = p.Radius * thick, r1 = p.TopRadius * thick, h = p.Height;
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, r0);
            Handles.DrawWireDisc(Vector3.up * h, Vector3.up, r1);
            Handles.DrawLine(new Vector3(r0, 0f, 0f), new Vector3(r1, h, 0f));
            Handles.DrawLine(new Vector3(-r0, 0f, 0f), new Vector3(-r1, h, 0f));

            if (Slider(new Vector3(0f, 0f, r0), Vector3.forward, out var np))
                Edit(l, "VFX 기둥 반지름", () => l.Pillar.Radius = Mathf.Max(0.02f, np.z) / thick);
            if (Slider(new Vector3(0f, h, r1), Vector3.forward, out np))
                Edit(l, "VFX 기둥 꼭대기", () => l.Pillar.TopRadius = Mathf.Max(0f, np.z) / thick);
            if (Slider(new Vector3(0f, h, 0f), Vector3.up, out np))
                Edit(l, "VFX 기둥 높이", () => l.Pillar.Height = Mathf.Max(0.05f, np.y));

            Handles.Label(new Vector3(0f, h, 0f), $"높이 {h * SizeDiv:0.00}m", EditorStyles.whiteMiniLabel);
        }

        void CardHandles(VfxLayer l)
        {
            var s = l.Card.Size;
            Handles.DrawWireCube(Vector3.zero, new Vector3(s.x, s.y, 0f));
            if (Slider(new Vector3(s.x * 0.5f, 0f, 0f), Vector3.right, out var np))
                Edit(l, "VFX 카드 너비", () => l.Card.Size.x = Mathf.Max(0.05f, np.x * 2f));
            if (Slider(new Vector3(0f, s.y * 0.5f, 0f), Vector3.up, out np))
                Edit(l, "VFX 카드 높이", () => l.Card.Size.y = Mathf.Max(0.05f, np.y * 2f));
        }

        void StreakHandles(VfxLayer l)
        {
            var s = l.Streak;
            Handles.DrawWireDisc(new Vector3(0f, 0f, s.Front), Vector3.forward, s.OuterRadius);
            Handles.DrawWireDisc(new Vector3(0f, 0f, s.Back), Vector3.forward, s.OuterRadius);
            Handles.DrawWireDisc(new Vector3(0f, 0f, s.Front), Vector3.forward, s.InnerRadius);
            Handles.DrawLine(new Vector3(0f, s.OuterRadius, s.Front), new Vector3(0f, s.OuterRadius, s.Back));

            if (Slider(new Vector3(0f, 0f, s.Front), Vector3.forward, out var np))
                Edit(l, "VFX 속도선 앞", () => l.Streak.Front = Mathf.Max(l.Streak.Back + 0.1f, np.z));
            if (Slider(new Vector3(0f, 0f, s.Back), Vector3.forward, out np))
                Edit(l, "VFX 속도선 뒤", () => l.Streak.Back = Mathf.Min(l.Streak.Front - 0.1f, np.z));
            if (Slider(new Vector3(0f, s.OuterRadius, s.Front), Vector3.up, out np))
                Edit(l, "VFX 속도선 바깥", () => l.Streak.OuterRadius = Mathf.Max(l.Streak.InnerRadius, np.y));
            if (Slider(new Vector3(0f, s.InnerRadius, s.Front), Vector3.up, out np))
                Edit(l, "VFX 속도선 안쪽", () => l.Streak.InnerRadius = Mathf.Clamp(np.y, 0f, l.Streak.OuterRadius));
        }

        void BurstHandles(VfxLayer l)
        {
            var b = l.Burst;
            float life = Mathf.Max(0.05f, l.Blocks.Length(1f));
            float reach = b.StartRadius + (b.Drag > 0.001f ? b.Speed / b.Drag : b.Speed * life);
            reach = Mathf.Max(0.05f, reach);
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, reach);
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, reach);
            Handles.DrawWireDisc(Vector3.zero, Vector3.right, reach);

            if (Slider(new Vector3(0f, 0f, reach), Vector3.forward, out var np))
                Edit(l, "VFX 뿌리기 범위", () =>
                {
                    float r = Mathf.Max(0.05f, np.z - l.Burst.StartRadius);
                    l.Burst.Speed = l.Burst.Drag > 0.001f ? r * l.Burst.Drag : r / life;
                });

            Handles.Label(new Vector3(0f, 0f, reach), $"범위 약 {reach * SizeDiv:0.00}m", EditorStyles.whiteMiniLabel);
        }

        void ConeHandles(VfxLayer l)
        {
            var c = l.Cone;
            float thick = Mathf.Max(0.01f, _fx.Thickness);
            float rb = c.BaseRadius * thick, rt = c.TipRadius * thick;
            Handles.DrawWireDisc(new Vector3(0f, 0f, c.Back), Vector3.forward, rb);
            Handles.DrawWireDisc(new Vector3(0f, 0f, c.Front), Vector3.forward, Mathf.Max(0.02f, rt));
            Handles.DrawLine(new Vector3(0f, rt, c.Front), new Vector3(0f, rb, c.Back));
            Handles.DrawLine(new Vector3(0f, -rt, c.Front), new Vector3(0f, -rb, c.Back));

            if (Slider(new Vector3(0f, 0f, c.Front), Vector3.forward, out var np))
                Edit(l, "VFX 원뿔 앞", () => l.Cone.Front = Mathf.Max(l.Cone.Back + 0.1f, np.z));
            if (Slider(new Vector3(0f, 0f, c.Back), Vector3.forward, out np))
                Edit(l, "VFX 원뿔 뒤", () => l.Cone.Back = Mathf.Min(l.Cone.Front - 0.1f, np.z));
            if (Slider(new Vector3(0f, rb, c.Back), Vector3.up, out np))
                Edit(l, "VFX 원뿔 뒤 반지름", () => l.Cone.BaseRadius = Mathf.Max(0.02f, np.y) / thick);

            Handles.Label(new Vector3(0f, rb, c.Back), $"길이 {(c.Front - c.Back) * SizeDiv:0.00}m", EditorStyles.whiteMiniLabel);
        }

        void TrailHandles(VfxLayer l)
        {
            float len = l.Trail.PreviewLength;
            Handles.DrawLine(Vector3.zero, Vector3.back * len);
            if (Slider(Vector3.back * len, Vector3.back, out var np))
                Edit(l, "VFX 잔상 미리보기 길이", () => l.Trail.PreviewLength = Mathf.Max(0.1f, -np.z));
        }

        void DrawLightHandles(VfxLight li)
        {
            using (new Handles.DrawingScope(VfxStudioGUI.LightColor))
            {
                EditorGUI.BeginChangeCheck();
                float r = Handles.RadiusHandle(Quaternion.identity, li.transform.position, li.Range * SizeDiv);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(li, "VFX 조명 범위");
                    li.Range = Mathf.Max(0.1f, r / SizeDiv);
                    Changed(li);
                }
            }
        }
    }
}
