using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToonVfx
{
    [Serializable]
    public class VfxArcSettings
    {
        [Min(0.05f)] public float Radius = 1.3f;
        [Range(10f, 360f)] public float Angle = 150f;
        [Tooltip("가장 두꺼운 곳의 폭 (m)")]
        [Min(0.01f)] public float Width = 0.45f;
        [Tooltip("가장 두꺼운 지점. 0 = 꼬리 쪽, 1 = 머리 쪽")]
        [Range(0.05f, 0.95f)] public float HeadBias = 0.7f;
        [Tooltip("양 끝 뾰족함. 클수록 뾰족")]
        [Range(0.2f, 3f)] public float Sharpness = 1f;
        [Tooltip("그어지는 중·사라지는 중 움직이는 머리/꼬리 끝의 뾰족한 길이 (전체 길이 대비)")]
        [Range(0.01f, 0.5f)] public float TipLength = 0.15f;
        [Tooltip("끄면 왼쪽 → 오른쪽, 켜면 오른쪽 → 왼쪽으로 그어짐")]
        public bool Reverse;

        [Header("보이는 방식")]
        [Tooltip("켜면 궤적을 가운데 두고 폭을 카메라 쪽으로 펼쳐서 어느 각도에서도 보임 (옆에서 봐도 납작해지지 않음)")]
        public bool FaceCamera = true;
        [Tooltip("카메라 향함을 끈 경우 — 안쪽 가장자리를 위(+)/아래(−)로 접어 갓 모양으로. 옆에서 봐도 휜 면이 보임")]
        [Range(-80f, 80f)] public float Tilt;
        [Tooltip("상하 두께 (m). 0보다 크면 세로 띠를 하나 더 세워 단면이 + 모양이 됨 — 어느 각도에서도 두께가 보임. 0 = 끔")]
        [Min(0f)] public float VerticalThickness;

        [Tooltip("끝으로 갈수록 올라가는 높이 (m) — 나선형 베기")]
        public float Lift;
        [Range(8, 128)] public int Segments = 48;

        // 두께 모양(HeadBias·Sharpness·TipLength)과 카메라 향함은 셰이더가 처리 — 메시 다시 만들 필요 없음
        public int Hash() => HashCode.Combine(Radius, Angle, Width, Reverse, Tilt, VerticalThickness, Lift, Segments);
    }

    [Serializable]
    public class VfxRingSettings
    {
        [Min(0.01f)] public float Radius = 1f;
        [Tooltip("띠의 폭 (m)")]
        [Min(0.01f)] public float Width = 0.25f;
        [Tooltip("고정 = 바닥에 눕힌 원 (레이어 회전으로 세울 수 있음), 카메라를 봄 = 항상 정면 원 (타격 충격파)")]
        public VfxFacing Facing = VfxFacing.Fixed;
        [Range(16, 128)] public int Segments = 64;

        public int Hash() => HashCode.Combine(Radius, Width, Segments);
    }

    [Serializable]
    public class VfxPillarSettings
    {
        [Tooltip("바닥 반지름 (m)")]
        [Min(0.01f)] public float Radius = 0.6f;
        [Tooltip("꼭대기 반지름 (m) — 작을수록 위로 모이는 불기둥")]
        [Min(0f)] public float TopRadius = 0.4f;
        [Min(0.05f)] public float Height = 2f;
        [Range(8, 64)] public int Segments = 32;

        public int Hash() => HashCode.Combine(Radius, TopRadius, Height, Segments);
    }

    [Serializable]
    public class VfxCardSettings
    {
        public Vector2 Size = Vector2.one;
        public VfxFacing Facing = VfxFacing.Billboard;

        public int Hash() => HashCode.Combine(Size, Facing);
    }

    /// <summary>
    /// 절차 메시 생성. UV 규칙: U = 길이 방향 (0 시작 → 1 끝), V = 폭 방향 (0 안쪽 → 1 바깥쪽)
    ///
    /// 호 메시의 정점 데이터 (셰이더가 폭을 펼침):
    ///   위치    = 궤적 위의 점 (한 단면의 정점 모두 같은 점)
    ///   노멀    = 펼칠 기본 방향 (가로 띠: 안쪽, 세로 띠: 위)
    ///   탄젠트  = 궤적 진행 방향
    ///   UV1     = x 이 정점이 펼칠 거리, y 띠 전체 폭, z 가운데 정렬(1)/바깥 테두리 정렬(0), w 카메라 향함 허용(1)
    ///   셰이더: 위치 += 방향 × (x − y × 0.5 × z)
    /// </summary>
    public static class VfxMeshBuilder
    {
        static readonly List<Vector3> V = new();
        static readonly List<Vector2> UV = new();
        static readonly List<Vector4> UV1 = new();
        static readonly List<Vector3> N = new();
        static readonly List<Vector4> TG = new();
        static readonly List<int> T = new();

        /// <summary>XY 평면 사각형, 중심 기준</summary>
        public static void Card(Mesh mesh, Vector2 size)
        {
            Begin();
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            Add(new Vector3(-hx, -hy, 0f), new Vector2(0f, 0f));
            Add(new Vector3(hx, -hy, 0f), new Vector2(1f, 0f));
            Add(new Vector3(-hx, hy, 0f), new Vector2(0f, 1f));
            Add(new Vector3(hx, hy, 0f), new Vector2(1f, 1f));
            T.Add(0); T.Add(2); T.Add(1);
            T.Add(1); T.Add(2); T.Add(3);
            End(mesh, 0f);
        }

        /// <summary>
        /// XZ 평면 원호 띠. 중심(원점)에서 +Z 방향으로 펼쳐짐.
        /// 초승달 모양(양끝 뾰족함)은 셰이더가 깎고, 폭 방향은 셰이더가 카메라 쪽으로 돌릴 수 있음
        /// </summary>
        public static void Arc(Mesh mesh, VfxArcSettings a, float widthMul, float radiusMul = 1f, float angleMul = 1f)
        {
            Begin();
            int seg = Mathf.Clamp(a.Segments, 4, 256);
            float radius = a.Radius * radiusMul;
            float half = Mathf.Min(a.Angle * angleMul, 360f) * 0.5f * Mathf.Deg2Rad;
            float w = Mathf.Min(a.Width * widthMul, radius);        // 안쪽 반경이 음수로 넘어가지 않게
            float tilt = a.Tilt * Mathf.Deg2Rad;
            float h = a.VerticalThickness * widthMul;

            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg;
                float th = a.Reverse ? Mathf.Lerp(half, -half, u) : Mathf.Lerp(-half, half, u);
                var dir = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                var tangent = new Vector3(Mathf.Cos(th), 0f, -Mathf.Sin(th)) * (a.Reverse ? -1f : 1f);

                // 안쪽으로 펼칠 방향 — 기울기만큼 위/아래로 접음 (갓 모양)
                var inward = (-dir * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt)).normalized;

                var p = dir * radius + Vector3.up * (a.Lift * u);
                var tg = new Vector4(tangent.x, tangent.y, tangent.z, 1f);

                // 가로 띠 — 바깥 테두리 정렬 (카메라 향함이면 셰이더가 가운데 정렬로 바꿈)
                V.Add(p); UV.Add(new Vector2(u, 0f)); UV1.Add(new Vector4(w, w, 0f, 1f)); N.Add(inward); TG.Add(tg);   // 안쪽
                V.Add(p); UV.Add(new Vector2(u, 1f)); UV1.Add(new Vector4(0f, w, 0f, 1f)); N.Add(inward); TG.Add(tg);  // 바깥

                if (i == seg) break;
                int b = i * 2;
                T.Add(b); T.Add(b + 1); T.Add(b + 2);
                T.Add(b + 1); T.Add(b + 3); T.Add(b + 2);
            }

            // 세로 띠 — 궤적을 가운데 두고 위아래로. 가로 띠와 합쳐 단면이 + 모양
            if (h > 0.001f)
            {
                int start = V.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float u = (float)i / seg;
                    float th = a.Reverse ? Mathf.Lerp(half, -half, u) : Mathf.Lerp(-half, half, u);
                    var dir = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                    var tangent = new Vector3(Mathf.Cos(th), 0f, -Mathf.Sin(th)) * (a.Reverse ? -1f : 1f);
                    var up = (Vector3.up * Mathf.Cos(tilt) + dir * Mathf.Sin(tilt)).normalized;   // 가로 띠에 수직

                    // 가로 띠의 폭 가운데에 세움
                    var p = dir * (radius - w * 0.5f * Mathf.Cos(tilt)) + Vector3.up * (a.Lift * u + w * 0.5f * Mathf.Sin(tilt));
                    var tg = new Vector4(tangent.x, tangent.y, tangent.z, 1f);

                    V.Add(p); UV.Add(new Vector2(u, 0f)); UV1.Add(new Vector4(0f, h, 1f, 0f)); N.Add(up); TG.Add(tg);  // 아래
                    V.Add(p); UV.Add(new Vector2(u, 1f)); UV1.Add(new Vector4(h, h, 1f, 0f)); N.Add(up); TG.Add(tg);  // 위

                    if (i == seg) break;
                    int b = start + i * 2;
                    T.Add(b); T.Add(b + 1); T.Add(b + 2);
                    T.Add(b + 1); T.Add(b + 3); T.Add(b + 2);
                }
            }

            End(mesh, Mathf.Max(w, h));
        }

        /// <summary>XZ 평면 고리. U = 둘레 (0 → 1), V = 안쪽(0) → 바깥(1). 쓸기로 둘레를 따라 그려짐</summary>
        public static void Ring(Mesh mesh, VfxRingSettings r, float widthMul)
        {
            Begin();
            int seg = Mathf.Clamp(r.Segments, 8, 256);
            float outer = r.Radius;
            float inner = Mathf.Max(0f, r.Radius - r.Width * widthMul);

            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg;
                float th = u * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                Add(dir * inner, new Vector2(u, 0f));
                Add(dir * outer, new Vector2(u, 1f));

                if (i == seg) break;
                int b = i * 2;
                T.Add(b); T.Add(b + 1); T.Add(b + 2);
                T.Add(b + 1); T.Add(b + 3); T.Add(b + 2);
            }
            End(mesh, 0f);
        }

        /// <summary>바닥에서 위로 서는 원통 (뚜껑 없음). U = 둘레, V = 아래(0) → 위(1)</summary>
        public static void Pillar(Mesh mesh, VfxPillarSettings c, float widthMul)
        {
            Begin();
            int seg = Mathf.Clamp(c.Segments, 6, 128);
            float r0 = c.Radius * widthMul, r1 = c.TopRadius * widthMul;

            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg;
                float th = u * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                Add(dir * r0, new Vector2(u, 0f));
                Add(dir * r1 + Vector3.up * c.Height, new Vector2(u, 1f));

                if (i == seg) break;
                int b = i * 2;
                T.Add(b); T.Add(b + 1); T.Add(b + 2);
                T.Add(b + 1); T.Add(b + 3); T.Add(b + 2);
            }
            End(mesh, 0f);
        }

        static void Add(Vector3 p, Vector2 uv)
        {
            V.Add(p);
            UV.Add(uv);
            UV1.Add(Vector4.zero);
            N.Add(Vector3.zero);
            TG.Add(new Vector4(1f, 0f, 0f, 1f));
        }

        static void Begin()
        {
            V.Clear();
            UV.Clear();
            UV1.Clear();
            N.Clear();
            TG.Clear();
            T.Clear();
        }

        static void End(Mesh m, float expand)
        {
            m.Clear();
            m.SetVertices(V);
            m.SetUVs(0, UV);
            m.SetUVs(1, UV1);
            m.SetNormals(N);
            m.SetTangents(TG);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();

            // 폭은 셰이더가 펼치므로 그만큼 경계를 넓혀 둠 (안 하면 화면 끝에서 잘려 사라짐)
            if (expand > 0f)
            {
                var b = m.bounds;
                b.Expand(expand * 2f);
                m.bounds = b;
            }
        }
    }
}
