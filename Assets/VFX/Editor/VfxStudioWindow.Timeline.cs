using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ToonVfx.EditorTools
{
    /// <summary>
    /// 블록 타임라인 — 레이어마다 등장 / 유지 / 사라짐 세 칸.
    ///   블록 몸통 드래그 = 시작 시각 이동 · 블록 오른쪽 끝 드래그 = 그 칸의 길이
    ///   위 눈금 클릭·드래그 = 시간 이동 · 빈 곳 오른쪽 클릭 = 레이어 추가 · 행 오른쪽 클릭 = 복제·삭제
    /// </summary>
    public partial class VfxStudioWindow
    {
        const float TransportH = 24f;
        const float RulerH = 20f;
        const float RowH = 24f;
        const float LabelW = 160f;
        const float EdgeGrab = 5f;
        static readonly string[] SpeedNames = { "0.1x", "0.25x", "0.5x", "1x" };
        static readonly float[] SpeedValues = { 0.1f, 0.25f, 0.5f, 1f };

        [SerializeField] float _tlZoom = 1f;
        float _tlScrollX;
        Vector2 _rowScroll;

        enum DragKind { None, Scrub, Move, AppearEnd, HoldEnd, VanishEnd, LightEnd }
        DragKind _drag;
        Component _dragTarget;
        float _dragGrab;
        int _dragUndo;

        // 이번 그리기의 시간 ↔ 좌표 변환 값
        float _viewStart, _pps, _trackX0;

        float TimeAt(float x) => _viewStart + (x - _trackX0) / _pps;
        float XAt(float t) => _trackX0 + (t - _viewStart) * _pps;

        static float Snap(float t, bool fine) => fine ? Mathf.Round(t / 0.001f) * 0.001f : Mathf.Round(t / 0.01f) * 0.01f;

        float TimelineLength()
        {
            float d = 0.3f;
            foreach (var c in _rows)
            {
                if (c is VfxLayer l && l != null) d = Mathf.Max(d, l.EndTime(_fx.Linger));
                else if (c is VfxLight li && li != null) d = Mathf.Max(d, li.EndTime);
            }
            return d + 0.08f;
        }

        void DrawTimeline(Rect r)
        {
            DrawTransport(new Rect(r.x, r.y, r.width, TransportH));

            var area = new Rect(r.x, r.y + TransportH, r.width, r.height - TransportH);
            float len = TimelineLength();
            float visible = len / Mathf.Max(1f, _tlZoom);
            _tlScrollX = Mathf.Clamp(_tlScrollX, 0f, Mathf.Max(0f, len - visible));
            float trackW = Mathf.Max(50f, area.width - LabelW - 16f - 8f);
            _pps = trackW / visible;
            _viewStart = _tlScrollX;

            // ── 눈금 (스크롤 밖, 전역 좌표) ──
            var ruler = new Rect(area.x, area.y, area.width, RulerH);
            _trackX0 = ruler.x + LabelW + 4f;
            DrawRuler(ruler, len);

            // ── 행 (스크롤 안, 지역 좌표) ──
            bool hScroll = _tlZoom > 1.001f;
            var rowsRect = new Rect(area.x, area.y + RulerH, area.width, area.height - RulerH - (hScroll ? 14f : 0f));
            float contentH = (_rows.Count + 1) * RowH;
            _rowScroll = GUI.BeginScrollView(rowsRect, _rowScroll, new Rect(0, 0, rowsRect.width - 16f, Mathf.Max(contentH, rowsRect.height - 1f)));
            _trackX0 = LabelW + 4f;
            DrawRows(rowsRect.width - 16f, Mathf.Max(contentH, rowsRect.height));
            GUI.EndScrollView();

            if (hScroll)
            {
                var sb = new Rect(area.x + LabelW, rowsRect.yMax, area.width - LabelW - 16f, 14f);
                _tlScrollX = GUI.HorizontalScrollbar(sb, _tlScrollX, visible, 0f, len);
            }
        }

        // ══════════════════════ 재생 막대 ══════════════════════

        void DrawTransport(Rect r)
        {
            GUILayout.BeginArea(r);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_playing ? "■ 정지" : "▶ 재생", EditorStyles.toolbarButton, GUILayout.Width(60))) TogglePlay();
                _loopPlay = GUILayout.Toggle(_loopPlay, new GUIContent("반복", "끝나면 처음부터 다시"), EditorStyles.toolbarButton, GUILayout.Width(40));

                bool loopMode = _fx.PlayMode == VfxPlayMode.StartLoopEnd;
                if (loopMode)
                {
                    using (new EditorGUI.DisabledScope(!_playing || _fx.IsStopping))
                        if (GUILayout.Button(new GUIContent("◼ 끝내기", "반복 중인 이펙트를 끝내고 사라짐 구간 재생 (게임에서 Stop()을 부른 것과 같음)"),
                                EditorStyles.toolbarButton, GUILayout.Width(64)))
                            _fx.Stop();
                }

                GUILayout.Space(6);
                float now = _playing ? _fx.CurrentTime : _scrubTime;
                string state = _playing && loopMode && !_fx.IsStopping ? " · 반복 중" : "";
                GUILayout.Label($"{now:0.00}초 / {TimelineLength() - 0.08f:0.00}초{state}", EditorStyles.miniLabel, GUILayout.Width(150));

                int si = Array.IndexOf(SpeedValues, _playSpeed);
                if (si < 0) si = 3;
                si = EditorGUILayout.Popup(si, SpeedNames, EditorStyles.toolbarPopup, GUILayout.Width(52));
                _playSpeed = SpeedValues[si];

                GUILayout.FlexibleSpace();

                if (_fx.FrameRate > 0)
                    GUILayout.Label($"{_fx.FrameRate}fps 끊기", EditorStyles.miniLabel);
                GUILayout.Label("확대", EditorStyles.miniLabel);
                _tlZoom = GUILayout.HorizontalSlider(_tlZoom, 1f, 8f, GUILayout.Width(80));
                GUILayout.Space(6);
            }
            GUILayout.EndArea();
        }

        // ══════════════════════ 눈금 ══════════════════════

        void DrawRuler(Rect r, float len)
        {
            EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.7f, 0.7f, 0.7f));
            GUI.Label(new Rect(r.x + 6, r.y + 2, LabelW - 8, r.height), "레이어", EditorStyles.miniBoldLabel);

            float[] steps = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f };
            float step = steps[steps.Length - 1];
            foreach (var s in steps) if (s * _pps >= 46f) { step = s; break; }
            float minor = step / 5f;

            var tickCol = EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.25f) : new Color(0, 0, 0, 0.3f);
            float t0 = Mathf.Floor(_viewStart / minor) * minor;
            float tEnd = TimeAt(r.xMax);
            for (float t = t0; t <= tEnd; t += minor)
            {
                float x = XAt(t);
                if (x < _trackX0 - 0.5f) continue;
                bool major = Mathf.Abs(t / step - Mathf.Round(t / step)) < 0.001f;
                EditorGUI.DrawRect(new Rect(x, r.yMax - (major ? 8 : 4), 1, major ? 8 : 4), tickCol);
                if (major) GUI.Label(new Rect(x + 2, r.y, 40, 14), t.ToString(step < 0.1f ? "0.00" : "0.0"), EditorStyles.miniLabel);
            }

            // 재생 위치 표시
            float ph = PlayheadTime();
            if (ph >= 0f)
            {
                float x = XAt(ph);
                if (x >= _trackX0) EditorGUI.DrawRect(new Rect(x - 4, r.yMax - 6, 9, 6), VfxStudioGUI.Playhead);
            }

            // 클릭·드래그로 시간 이동
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            var track = new Rect(_trackX0, r.y, r.xMax - _trackX0, r.height);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && track.Contains(e.mousePosition))
                    {
                        StopPlay();
                        GUIUtility.hotControl = id;
                        SetScrub(Snap(Mathf.Max(0f, TimeAt(e.mousePosition.x)), e.shift));
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        SetScrub(Snap(Mathf.Clamp(TimeAt(e.mousePosition.x), 0f, len), e.shift));
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
            EditorGUIUtility.AddCursorRect(track, MouseCursor.SlideArrow);
        }

        /// <summary>재생 위치 — 반복 재생 중에는 블록 위치와 맞지 않으므로 숨김 (-1)</summary>
        float PlayheadTime()
        {
            if (!_playing) return _scrubTime;
            if (_fx.PlayMode == VfxPlayMode.StartLoopEnd) return -1f;
            return _fx.CurrentTime;
        }

        // ══════════════════════ 행 ══════════════════════

        void DrawRows(float width, float height)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            float linger = Mathf.Max(0.01f, _fx.Linger);
            bool loopMode = _fx.PlayMode == VfxPlayMode.StartLoopEnd;

            for (int i = 0; i < _rows.Count; i++)
            {
                var c = _rows[i];
                if (c == null) continue;
                var row = new Rect(0, i * RowH, width, RowH);
                bool selected = c == _sel;
                EditorGUI.DrawRect(row, selected ? VfxStudioGUI.SelectedRow : VfxStudioGUI.RowBg(i));

                bool active = c.gameObject.activeInHierarchy;
                float alpha = active ? 1f : 0.35f;

                if (c is VfxLayer l) DrawLayerBlocks(row, l, linger, loopMode, alpha);
                else if (c is VfxLight li) DrawLightBlock(row, li, alpha);

                // 이름 칸 (블록이 왼쪽으로 넘쳐도 덮음)
                var label = new Rect(0, row.y, LabelW, row.height);
                EditorGUI.DrawRect(label, selected ? new Color(0.22f, 0.33f, 0.5f) : VfxStudioGUI.RowBg(i));
                var eye = new Rect(4, row.y + 4, 16, 16);
                bool nowActive = GUI.Toggle(eye, c.gameObject.activeSelf, new GUIContent("", "켜기/끄기"));
                if (nowActive != c.gameObject.activeSelf) ToggleActive(c);

                string tag = c is VfxLayer ll ? VfxStudioGUI.ShapeName(ll.Shape) : "조명";
                GUI.Label(new Rect(22, row.y + 4, LabelW - 24, 16), $"<b>{tag}</b>  {c.gameObject.name}", RowLabel);
                EditorGUI.DrawRect(new Rect(LabelW, row.y, 1, row.height), VfxStudioGUI.Separator);
            }

            // ── 맨 아래: 추가 ──
            var add = new Rect(4, _rows.Count * RowH + 2, LabelW - 8, RowH - 4);
            if (GUI.Button(add, "+ 레이어 추가", EditorStyles.miniButton))
                AddLayerMenu(Mathf.Max(0f, _scrubTime)).DropDown(add);

            // ── 재생 위치 선 ──
            float ph = PlayheadTime();
            if (ph >= 0f && e.type == EventType.Repaint)
            {
                float x = XAt(ph);
                if (x >= _trackX0) EditorGUI.DrawRect(new Rect(x, 0, 1, height), VfxStudioGUI.Playhead);
            }

            HandleRowEvents(id, e, width, linger);
        }

        static GUIStyle _rowLabel;
        static GUIStyle RowLabel => _rowLabel ??= new GUIStyle(EditorStyles.label) { richText = true, clipping = TextClipping.Clip, fontSize = 11 };

        void DrawLayerBlocks(Rect row, VfxLayer l, float linger, bool loopMode, float alpha)
        {
            var b = l.Blocks;
            float s = l.StartTime, a = b.AppearTime, h = b.HoldTime, v = b.VanishTime * linger;

            // 뿌리기 — 수명 랜덤·계속 뿜기로 더 길게 남는 구간
            float end = l.EndTime(linger);
            if (l.Shape == VfxShape.Burst && end > s + a + h + v + 0.001f)
                Block(row, s + a + h + v, end, WithA(VfxStudioGUI.TailColor, alpha), l.Burst.Continuous ? "계속 뿜기" : "");

            string holdLabel = VfxStudioGUI.Short(b.Hold);
            if (l.Shape == VfxShape.Burst && l.Burst.Continuous) holdLabel = "계속";
            else if (loopMode) holdLabel = "↻ " + holdLabel;

            Block(row, s, s + a, WithA(VfxStudioGUI.AppearColor, alpha), VfxStudioGUI.Short(b.Appear));
            Block(row, s + a, s + a + h, WithA(VfxStudioGUI.HoldColor, alpha), holdLabel);
            Block(row, s + a + h, s + a + h + v, WithA(VfxStudioGUI.VanishColor, alpha), VfxStudioGUI.Short(b.Vanish));

            if (l == _sel && Event.current.type == EventType.Repaint)
            {
                float x0 = XAt(s), x1 = XAt(s + a + h + v);
                VfxStudioGUI.Outline(new Rect(x0, row.y + 2, Mathf.Max(2f, x1 - x0), row.height - 4), Color.white);
            }

            // 커서 모양
            foreach (float t in new[] { s + a, s + a + h, s + a + h + v })
                EditorGUIUtility.AddCursorRect(new Rect(XAt(t) - EdgeGrab, row.y, EdgeGrab * 2, row.height), MouseCursor.ResizeHorizontal);
        }

        void DrawLightBlock(Rect row, VfxLight li, float alpha)
        {
            Block(row, li.StartTime, li.StartTime + li.Duration, WithA(VfxStudioGUI.LightColor, alpha),
                li.Mode == VfxLightMode.Sustain ? "지속 조명" : "번쩍");
            EditorGUIUtility.AddCursorRect(new Rect(XAt(li.StartTime + li.Duration) - EdgeGrab, row.y, EdgeGrab * 2, row.height), MouseCursor.ResizeHorizontal);
        }

        static Color WithA(Color c, float a) => new(c.r, c.g, c.b, c.a * a);

        void Block(Rect row, float t0, float t1, Color col, string label)
        {
            float x0 = XAt(t0), x1 = XAt(t1);
            float minX = _trackX0;
            if (x1 < minX) return;
            x0 = Mathf.Max(x0, minX);
            var r = new Rect(x0, row.y + 3, Mathf.Max(2f, x1 - x0), row.height - 6);
            EditorGUI.DrawRect(r, col);
            if (r.width > 26 && !string.IsNullOrEmpty(label)) GUI.Label(r, label, VfxStudioGUI.BlockLabel);
        }

        // ══════════════════════ 행 조작 ══════════════════════

        void HandleRowEvents(int id, Event e, float width, float linger)
        {
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                {
                    int i = Mathf.FloorToInt(e.mousePosition.y / RowH);
                    bool onRow = i >= 0 && i < _rows.Count && _rows[i] != null;

                    if (e.button == 1)
                    {
                        if (onRow) { Select(_rows[i]); RowMenu(_rows[i]).ShowAsContext(); }
                        else AddLayerMenu(Mathf.Max(0f, Snap(TimeAt(e.mousePosition.x), false))).ShowAsContext();
                        e.Use();
                        break;
                    }
                    if (e.button != 0 || !onRow) break;

                    var c = _rows[i];
                    Select(c);
                    if (e.mousePosition.x < LabelW) { e.Use(); break; }

                    float x = e.mousePosition.x;
                    float t = TimeAt(x);
                    _drag = PickDrag(c, x, t, linger);
                    _dragTarget = c;

                    if (_drag == DragKind.None)
                    {
                        // 블록 밖 빈 곳 → 그 시각으로 이동
                        StopPlay();
                        SetScrub(Snap(Mathf.Max(0f, t), e.shift));
                        e.Use();
                        break;
                    }

                    StopPlay();
                    Undo.IncrementCurrentGroup();
                    _dragUndo = Undo.GetCurrentGroup();
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                }

                case EventType.MouseDrag:
                {
                    if (GUIUtility.hotControl != id || _dragTarget == null) break;
                    float t = Snap(TimeAt(e.mousePosition.x), e.shift);
                    ApplyDrag(t, linger);
                    e.Use();
                    break;
                }

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    if (_drag != DragKind.None) Undo.CollapseUndoOperations(_dragUndo);
                    _drag = DragKind.None;
                    _dragTarget = null;
                    e.Use();
                    break;
            }
        }

        DragKind PickDrag(Component c, float x, float t, float linger)
        {
            if (c is VfxLight li)
            {
                if (Mathf.Abs(x - XAt(li.StartTime + li.Duration)) <= EdgeGrab) return DragKind.LightEnd;
                if (t >= li.StartTime && t <= li.StartTime + li.Duration) { _dragGrab = t - li.StartTime; return DragKind.Move; }
                return DragKind.None;
            }

            var l = (VfxLayer)c;
            var b = l.Blocks;
            float s = l.StartTime, eA = s + b.AppearTime, eH = eA + b.HoldTime, eV = eH + b.VanishTime * linger;

            // 끝이 겹치면 뒤쪽 칸을 우선 — 길이 0인 칸도 늘릴 수 있게
            if (Mathf.Abs(x - XAt(eV)) <= EdgeGrab) return DragKind.VanishEnd;
            if (Mathf.Abs(x - XAt(eH)) <= EdgeGrab) return DragKind.HoldEnd;
            if (Mathf.Abs(x - XAt(eA)) <= EdgeGrab) return DragKind.AppearEnd;

            float end = Mathf.Max(eV, l.EndTime(linger));
            if (t >= s && t <= end) { _dragGrab = t - s; return DragKind.Move; }
            return DragKind.None;
        }

        void ApplyDrag(float t, float linger)
        {
            var target = (Object)_dragTarget;
            Undo.RecordObject(target, "VFX 타이밍");

            if (_dragTarget is VfxLight li)
            {
                if (_drag == DragKind.LightEnd) li.Duration = Mathf.Max(0.01f, t - li.StartTime);
                else li.StartTime = Mathf.Max(0f, t - _dragGrab);
                Changed(li);
                return;
            }

            var l = (VfxLayer)_dragTarget;
            var b = l.Blocks;
            float s = l.StartTime;
            switch (_drag)
            {
                case DragKind.Move:
                    l.StartTime = Mathf.Max(0f, Snap(t - _dragGrab, false));
                    break;
                case DragKind.AppearEnd:
                    b.AppearTime = Mathf.Max(0f, t - s);
                    _scrubTime = t;
                    break;
                case DragKind.HoldEnd:
                    b.HoldTime = Mathf.Max(0f, t - s - b.AppearTime);
                    _scrubTime = t;
                    break;
                case DragKind.VanishEnd:
                    b.VanishTime = Mathf.Max(0.01f, (t - s - b.AppearTime - b.HoldTime) / linger);
                    _scrubTime = Mathf.Max(0f, t - 0.02f);     // 다 사라지기 직전 모습
                    break;
            }
            Changed(l);
        }

        // ══════════════════════ 메뉴 ══════════════════════

        GenericMenu RowMenu(Component c)
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent("복제  (Ctrl+D)"), false, DuplicateSelected);
            m.AddItem(new GUIContent("삭제  (Delete)"), false, DeleteSelected);
            m.AddItem(new GUIContent(c.gameObject.activeSelf ? "끄기" : "켜기"), false, () => ToggleActive(c));
            m.AddSeparator("");
            m.AddItem(new GUIContent("씬에서 보기"), false, () => { Selection.activeGameObject = c.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); });
            return m;
        }

        GenericMenu AddLayerMenu(float time)
        {
            var m = new GenericMenu();
            void Add(string label, VfxShape shape) =>
                m.AddItem(new GUIContent(label), false, () => AddLayerAt(shape, time));

            Add("호 (슬래시)", VfxShape.Arc);
            Add("링 (충격파)", VfxShape.Ring);
            Add("기둥 (오라)", VfxShape.Pillar);
            Add("카드 (스탬프)", VfxShape.Card);
            Add("뿌리기 (조각들)", VfxShape.Burst);
            Add("속도선", VfxShape.Streak);
            Add("잔상 줄", VfxShape.Trail);
            Add("원뿔 (돌진 바람)", VfxShape.Cone);
            m.AddSeparator("");
            m.AddItem(new GUIContent("조명"), false, () => AddLightAt(time));
            return m;
        }

        void AddLayerAt(VfxShape shape, float time)
        {
            if (_fx == null) return;
            var l = CreateLayer(shape, VfxStudioGUI.ShapeName(shape), _drawStyle);
            l.StartTime = time;
            Undo.RegisterCreatedObjectUndo(l.gameObject, "VFX 레이어 추가");
            CollectRows();
            Select(l);
            SetScrub(time + l.Blocks.AppearTime + l.Blocks.HoldTime * 0.5f);
            MarkDirty();
        }

        void AddLightAt(float time)
        {
            if (_fx == null) return;
            var li = CreateLight(_fx.transform.position);
            li.StartTime = time;
            Undo.RegisterCreatedObjectUndo(li.gameObject, "VFX 조명 추가");
            CollectRows();
            Select(li);
            MarkDirty();
        }
    }
}
