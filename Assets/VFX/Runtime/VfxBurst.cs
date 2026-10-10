using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToonVfx
{
    /// <summary>
    /// 뿌리기 — 조각 여러 개를 사방으로 흩뿌림 (가시·반짝이·불똥·파편).
    /// 파티클 시뮬레이션이 아니라 '시간 → 위치'를 바로 계산 → 아무 시점으로 드래그해도 같은 모습 (미리보기·되감기 가능)
    /// </summary>
    [Serializable]
    public class VfxBurstSettings
    {
        [Range(1, 64)] public int Count = 12;
        [Tooltip("바꾸면 조각 배치가 달라짐 — 같은 이펙트를 여러 벌 만들 때")]
        public int Seed = 1;

        [Header("방향")]
        public VfxBurstShape Shape = VfxBurstShape.Sphere;
        [Tooltip("앞쪽 원뿔일 때 퍼지는 각도 (°)")]
        [Range(1f, 180f)] public float ConeAngle = 45f;
        [Tooltip("처음부터 중심에서 이만큼 떨어져 시작 (m)")]
        [Min(0f)] public float StartRadius;

        [Header("움직임")]
        [Tooltip("튀어나가는 속도 (m/초)")]
        public float Speed = 4f;
        [Range(0f, 1f)] public float SpeedRandom = 0.5f;
        [Tooltip("감속 — 클수록 빨리 멈춤 (폭발은 4~8)")]
        [Min(0f)] public float Drag = 4f;
        [Tooltip("중력 (m/초²). + 떨어짐(파편), − 떠오름(불똥)")]
        public float Gravity;

        [Header("모양")]
        public VfxBurstFacing Facing = VfxBurstFacing.Billboard;
        public Vector2 SizeRange = new(0.15f, 0.35f);
        [Tooltip("길이 방향으로 늘림. 가시 마스크에만 쓰는 걸 권장 (2~4) — 불꽃·별·돌은 0.\n'날아가는 방향' 모드: 머리는 고정, 꼬리만 뒤로 늘고 날아온 거리보다 길어지지 않음")]
        [Min(0f)] public float Stretch;
        [Tooltip("조각마다 다른 각도로 돌려 놓음")]
        public bool RandomRotation = true;

        [Header("타이밍")]
        [Tooltip("조각마다 사라지는 때가 이만큼 들쭉날쭉 (0~1)")]
        [Range(0f, 1f)] public float LifeRandom = 0.3f;
        [Tooltip("켜면 한 번 터지지 않고 계속 뿜음 (버프의 피어오르는 반짝이·불똥). 개수 = 동시에 떠 있는 조각 수.\n" +
                 "'시작 → 반복 → 끝'에서 끝내면 새 조각은 멈추고 남은 것만 사라짐. 1회 재생에서는 한 주기만 뿜음")]
        public bool Continuous;
    }

    /// <summary>뿌리기 메시를 매 프레임 만들어 냄 — 조각마다 사각형 하나, 사라짐 값은 정점 색으로</summary>
    public static class VfxBurstBuilder
    {
        static readonly List<Vector3> V = new();
        static readonly List<Vector2> UV = new();
        static readonly List<Color> C = new();
        static readonly List<Vector4> UV1 = new();      // x = 조각 세로/가로 비율 — 셰이더가 노이즈를 비율에 맞춰 늘리지 않게
        static readonly List<int> T = new();

        /// <param name="lt">레이어 시작 기준 시각 (초)</param>
        /// <param name="toLocal">월드 → 메시 로컬 (카메라·중력 방향 변환용)</param>
        /// <param name="stopLt">계속 뿜기가 멈추는 레이어 시각 — 이후 태어나는 조각은 없음</param>
        public static void Build(Mesh mesh, VfxBurstSettings b, VfxBlocks blocks, float lt, float stopLt, in VfxContext ctx, Transform toLocal)
        {
            V.Clear(); UV.Clear(); C.Clear(); T.Clear(); UV1.Clear();

            Vector3 camR = Vector3.right, camU = Vector3.up, camF = Vector3.forward;
            if (ctx.Camera != null)
            {
                var ct = ctx.Camera.transform;
                camR = toLocal.InverseTransformDirection(ct.right);
                camU = toLocal.InverseTransformDirection(ct.up);
                camF = toLocal.InverseTransformDirection(ct.forward);
            }
            Vector3 down = toLocal.InverseTransformDirection(Vector3.down);
            float size = Mathf.Max(0.01f, ctx.Size);
            float cycle = blocks.Length(ctx.Linger) * (1f + b.LifeRandom);

            for (int i = 0; i < b.Count; i++)
            {
                // 계속 뿜기: 조각마다 출발 위상을 흩어 두고 주기마다 다시 태어남 (태어날 때마다 다른 방향)
                int gen = i;
                float age = lt;
                if (b.Continuous && cycle > 0.001f)
                {
                    float phase = H(i, 6, b.Seed) * cycle;
                    float n = Mathf.Floor((lt + phase) / cycle);
                    float birth = n * cycle - phase;
                    if (birth < 0f || birth > stopLt) continue;    // 아직 안 태어났거나, 끝낸 뒤라 새로 안 나옴
                    age = lt - birth;
                    gen = i + (int)n * 131;
                }
                float tt = Mathf.Max(0f, age);

                // 조각마다 수명 배율 — 일찍 사라지는 것, 늦게 사라지는 것
                float lifeMul = 1f + (H(gen, 0, b.Seed) * 2f - 1f) * b.LifeRandom;
                var s = blocks.Evaluate(age / Mathf.Max(0.05f, lifeMul), ctx.Linger);
                if (!s.Visible) continue;

                // 방향·속도
                Vector3 dir = Direction(b, H(gen, 1, b.Seed), H(gen, 2, b.Seed));
                float speed = b.Speed * (1f + (H(gen, 3, b.Seed) * 2f - 1f) * b.SpeedRandom);

                float travel = b.Drag > 0.001f ? speed * (1f - Mathf.Exp(-b.Drag * tt)) / b.Drag : speed * tt;
                Vector3 pos = dir * (b.StartRadius + travel) + down * (0.5f * b.Gravity * tt * tt) + s.Offset;
                Vector3 vel = dir * speed * Mathf.Exp(-b.Drag * tt) + down * (b.Gravity * tt);
                pos *= size;

                float sz = Mathf.Lerp(b.SizeRange.x, b.SizeRange.y, H(gen, 4, b.Seed)) * size;
                float halfW = sz * 0.5f * s.Scale.x;
                float halfH = sz * 0.5f * s.Scale.y;

                Vector3 ax, side;
                Vector3 center = pos;
                if (b.Facing == VfxBurstFacing.Velocity)
                {
                    // 가시: 날아가는 방향으로 늘어나고, 폭은 카메라를 향함
                    ax = vel.sqrMagnitude > 1e-4f ? vel.normalized : dir;
                    side = Vector3.Cross(ax, camF);
                    if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(ax, Vector3.up);
                    side.Normalize();

                    // 머리는 조각 위치에 고정하고 꼬리만 뒤로 늘림.
                    // 꼬리는 지금까지 날아온 거리보다 길어지지 않음 → 출발점 너머(중심 반대편)로 뻗지 않음
                    float spd01 = Mathf.Clamp01(vel.magnitude / Mathf.Max(0.01f, Mathf.Abs(speed)));
                    float traveled = (pos - dir * (b.StartRadius * size)).magnitude;
                    float len = Mathf.Min(2f * halfH * (1f + b.Stretch * spd01), 2f * halfH + traveled);
                    Vector3 front = pos + ax * halfH;
                    center = front - ax * (len * 0.5f);
                    halfH = len * 0.5f;
                }
                else
                {
                    float ang = (b.RandomRotation ? H(gen, 5, b.Seed) * 360f : 0f) + s.Spin;
                    float rad = ang * Mathf.Deg2Rad, cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                    side = camR * cs + camU * sn;
                    ax = -camR * sn + camU * cs;
                    halfH *= 1f + b.Stretch;
                }

                var anim = new Color(s.Erosion, s.ColorShift, s.Flash, s.Alpha);
                int v0 = V.Count;
                V.Add(center - side * halfW - ax * halfH); UV.Add(new Vector2(0f, 0f));
                V.Add(center + side * halfW - ax * halfH); UV.Add(new Vector2(1f, 0f));
                V.Add(center - side * halfW + ax * halfH); UV.Add(new Vector2(0f, 1f));
                V.Add(center + side * halfW + ax * halfH); UV.Add(new Vector2(1f, 1f));
                C.Add(anim); C.Add(anim); C.Add(anim); C.Add(anim);
                var aspect = new Vector4(halfH / Mathf.Max(halfW, 1e-4f), 0f, 0f, 0f);
                UV1.Add(aspect); UV1.Add(aspect); UV1.Add(aspect); UV1.Add(aspect);
                T.Add(v0); T.Add(v0 + 2); T.Add(v0 + 1);
                T.Add(v0 + 1); T.Add(v0 + 2); T.Add(v0 + 3);
            }

            mesh.Clear();
            mesh.SetVertices(V);
            mesh.SetUVs(0, UV);
            mesh.SetColors(C);
            mesh.SetUVs(1, UV1);
            mesh.SetTriangles(T, 0);
            mesh.RecalculateBounds();
        }

        static Vector3 Direction(VfxBurstSettings b, float a, float c)
        {
            float phi = c * Mathf.PI * 2f;
            switch (b.Shape)
            {
                case VfxBurstShape.Up:
                {
                    float y = a;
                    float r = Mathf.Sqrt(1f - y * y);
                    return new Vector3(r * Mathf.Cos(phi), y, r * Mathf.Sin(phi));
                }
                case VfxBurstShape.Forward:
                {
                    float cz = Mathf.Lerp(1f, Mathf.Cos(b.ConeAngle * 0.5f * Mathf.Deg2Rad), a);
                    float r = Mathf.Sqrt(Mathf.Max(0f, 1f - cz * cz));
                    return new Vector3(r * Mathf.Cos(phi), r * Mathf.Sin(phi), cz);
                }
                case VfxBurstShape.Flat:
                    return new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                default:
                {
                    float y = a * 2f - 1f;
                    float r = Mathf.Sqrt(1f - y * y);
                    return new Vector3(r * Mathf.Cos(phi), y, r * Mathf.Sin(phi));
                }
            }
        }

        /// <summary>조각 번호·항목·시드 → 0~1 (항상 같은 값)</summary>
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
