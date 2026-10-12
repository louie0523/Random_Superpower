using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToonVfx
{
    /// <summary>속도선 — 몸 주변 원통 안에서 선들이 앞(+Z)에서 뒤(−Z)로 계속 흘러감. 시간만으로 위치 계산</summary>
    [Serializable]
    public class VfxStreakSettings
    {
        [Range(1, 48)] public int Count = 14;
        public int Seed = 1;

        [Header("배치 (레이어 기준, 앞 = +Z)")]
        [Min(0f)] public float InnerRadius = 0.5f;
        [Min(0f)] public float OuterRadius = 1.3f;
        [Tooltip("선이 놓일 둘레 범위 (°). 360 = 사방, 180 = 위쪽 반만 등")]
        [Range(10f, 360f)] public float ArcAngle = 360f;
        [Tooltip("둘레 범위의 가운데 방향 (°). 0 = 위, 90 = 오른쪽")]
        public float ArcCenter;
        [Tooltip("선이 나타나는 앞쪽 끝 (m)")]
        public float Front = 2f;
        [Tooltip("선이 사라지는 뒤쪽 끝 (m, 음수)")]
        public float Back = -2.5f;

        [Header("모양 · 움직임")]
        public Vector2 LengthRange = new(0.8f, 1.6f);
        [Min(0.005f)] public float Width = 0.035f;
        [Tooltip("뒤로 흘러가는 속도 (m/초)")]
        public float Speed = 14f;
    }

    /// <summary>잔상 줄 — 레이어가 지나온 경로를 따라 그려지는 리본</summary>
    [Serializable]
    public class VfxTrailSettings
    {
        [Tooltip("몇 초 전까지의 경로를 남길지 — 길수록 꼬리가 김")]
        [Min(0.02f)] public float Time = 0.25f;
        [Min(0.01f)] public float Width = 0.3f;
        [Tooltip("이만큼 움직일 때마다 점을 하나 찍음 (m)")]
        [Min(0.005f)] public float MinDistance = 0.05f;
        [Tooltip("에디터 미리보기 — 움직이지 않으니 뒤(−Z)로 이 길이만큼 곧게 그려 보여줌")]
        [Min(0.1f)] public float PreviewLength = 2f;
    }

    /// <summary>속도선·잔상 줄 메시 — 조각마다 정점 색(사라짐 값)과 비율(UV1.x)을 넣음 (뿌리기와 같은 셰이더 경로)</summary>
    public static class VfxLineBuilder
    {
        static readonly List<Vector3> V = new();
        static readonly List<Vector2> UV = new();
        static readonly List<Vector4> UV1 = new();
        static readonly List<Color> C = new();
        static readonly List<int> T = new();

        static void Begin() { V.Clear(); UV.Clear(); UV1.Clear(); C.Clear(); T.Clear(); }

        static void End(Mesh m)
        {
            m.Clear();
            m.SetVertices(V);
            m.SetUVs(0, UV);
            m.SetUVs(1, UV1);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
        }

        static Vector3 CamLocal(in VfxContext ctx, Transform toLocal) =>
            ctx.Camera != null ? toLocal.InverseTransformPoint(ctx.Camera.transform.position) : new Vector3(0f, 0.5f, -5f);

        // ══════════════ 속도선 ══════════════

        /// <param name="clock">흐름 시계 — 반복 구간과 무관하게 계속 흐름</param>
        public static void Streak(Mesh mesh, VfxStreakSettings st, in VfxState s, float clock, in VfxContext ctx, Transform toLocal)
        {
            Begin();
            Vector3 cam = CamLocal(ctx, toLocal);
            float size = Mathf.Max(0.01f, ctx.Size);

            for (int i = 0; i < st.Count; i++)
            {
                float ang = (st.ArcCenter + (H(i, 0, st.Seed) - 0.5f) * st.ArcAngle) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(st.InnerRadius, st.OuterRadius, H(i, 1, st.Seed));
                float len = Mathf.Lerp(st.LengthRange.x, st.LengthRange.y, H(i, 2, st.Seed));
                float span = st.Front - st.Back + len;
                if (span <= 0.01f) continue;

                // 선 가운데가 앞 끝 바깥에서 출발해 뒤 끝 바깥까지 흘러가고, 다시 앞에서 (구간 밖은 잘라냄)
                float travel = Mathf.Repeat(clock * st.Speed + H(i, 3, st.Seed) * span, span);
                float c = st.Front + len * 0.5f - travel;
                float z0 = Mathf.Max(c - len * 0.5f, st.Back);
                float z1 = Mathf.Min(c + len * 0.5f, st.Front);
                if (z1 - z0 < 0.02f) continue;

                // 구간에 걸쳐 잘린 만큼 깎여 보이게 — 앞에서 자라 나오고 뒤에서 깎여 사라짐
                float inside = (z1 - z0) / Mathf.Max(len, 0.01f);
                float erosion = Mathf.Max(s.Erosion, 1f - Mathf.Clamp01(inside * 1.5f));
                if (erosion >= 0.999f) continue;

                var radial = new Vector3(Mathf.Sin(ang), Mathf.Cos(ang), 0f) * r;
                Vector3 a = (radial + Vector3.forward * z0) * size;
                Vector3 b = (radial + Vector3.forward * z1) * size;
                Vector3 axis = (b - a).normalized;
                Vector3 side = Vector3.Cross(axis, (cam - (a + b) * 0.5f).normalized);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(axis, Vector3.up);
                side = side.normalized * (st.Width * 0.5f * size * s.Scale.x);

                float aspect = (b - a).magnitude / Mathf.Max(st.Width * size, 1e-4f);
                var anim = new Color(erosion, s.ColorShift, s.Flash, s.Alpha);
                Quad(a, b, side, aspect, anim);
            }
            End(mesh);
        }

        static void Quad(Vector3 a, Vector3 b, Vector3 side, float aspect, Color anim)
        {
            int v0 = V.Count;
            var asp = new Vector4(aspect, 0f, 0f, 0f);
            V.Add(a - side); UV.Add(new Vector2(0f, 0f));
            V.Add(a + side); UV.Add(new Vector2(1f, 0f));
            V.Add(b - side); UV.Add(new Vector2(0f, 1f));
            V.Add(b + side); UV.Add(new Vector2(1f, 1f));
            for (int k = 0; k < 4; k++) { UV1.Add(asp); C.Add(anim); }
            T.Add(v0); T.Add(v0 + 2); T.Add(v0 + 1);
            T.Add(v0 + 1); T.Add(v0 + 2); T.Add(v0 + 3);
        }

        // ══════════════ 잔상 줄 ══════════════

        static readonly List<Vector3> Pts = new();
        static readonly List<float> Ages = new();

        /// <param name="history">월드 위치 + 기록 시각 (오래된 것 → 최신)</param>
        public static void Trail(Mesh mesh, VfxTrailSettings tr, List<(Vector3 pos, float time)> history, float now,
                                 bool preview, in VfxState s, in VfxContext ctx, Transform toLocal)
        {
            Begin();
            Pts.Clear();
            Ages.Clear();
            float size = Mathf.Max(0.01f, ctx.Size);

            if (preview)
            {
                // 에디터: 움직이지 않으니 뒤로 곧게 뻗은 모양
                const int n = 12;
                for (int i = 0; i < n; i++)
                {
                    float k = (float)i / (n - 1);                   // 0 꼬리 → 1 머리
                    Pts.Add(Vector3.back * (tr.PreviewLength * (1f - k)) * size);
                    Ages.Add(k);
                }
            }
            else
            {
                foreach (var h in history)
                {
                    float age = (now - h.time) / tr.Time;
                    if (age > 1f) continue;
                    Pts.Add(toLocal.InverseTransformPoint(h.pos));
                    Ages.Add(1f - Mathf.Clamp01(age));
                }
                Pts.Add(Vector3.zero);                              // 지금 위치가 머리
                Ages.Add(1f);
            }

            if (Pts.Count < 2) { End(mesh); return; }

            Vector3 cam = CamLocal(ctx, toLocal);
            float total = 0f;
            for (int i = 1; i < Pts.Count; i++) total += (Pts[i] - Pts[i - 1]).magnitude;
            if (total < 0.02f) { End(mesh); return; }

            var anim = new Color(s.Erosion, s.ColorShift, s.Flash, s.Alpha);
            var asp = new Vector4(total / Mathf.Max(tr.Width * size, 1e-4f), 0f, 0f, 0f);

            for (int i = 0; i < Pts.Count; i++)
            {
                Vector3 prev = Pts[Mathf.Max(0, i - 1)], next = Pts[Mathf.Min(Pts.Count - 1, i + 1)];
                Vector3 dir = (next - prev).sqrMagnitude > 1e-8f ? (next - prev).normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(dir, (cam - Pts[i]).normalized);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(dir, Vector3.up);

                float k = Ages[i];                                   // 0 꼬리 → 1 머리
                float w = tr.Width * size * s.Scale.x * Mathf.Sqrt(k) * 0.5f;
                side = side.normalized * w;

                V.Add(Pts[i] - side); UV.Add(new Vector2(0f, k));
                V.Add(Pts[i] + side); UV.Add(new Vector2(1f, k));
                UV1.Add(asp); UV1.Add(asp);
                C.Add(anim); C.Add(anim);

                if (i == 0) continue;
                int b = (i - 1) * 2;
                T.Add(b); T.Add(b + 2); T.Add(b + 1);
                T.Add(b + 1); T.Add(b + 2); T.Add(b + 3);
            }
            End(mesh);
        }

        static float H(int i, int k, int seed)
        {
            unchecked
            {
                uint x = (uint)(i * 73856093) ^ (uint)(k * 19349663) ^ (uint)(seed * 83492791);
                x ^= x >> 13;
                x *= 0x5bd1e995;
                x ^= x >> 15;
                x *= 0x27d4eb2d;
                x ^= x >> 16;
                return (x & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
