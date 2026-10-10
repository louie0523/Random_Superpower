using System;
using UnityEngine;

namespace ToonVfx
{
    /// <summary>레이어 한 프레임의 상태. 동작 블록이 계산하고 VfxLayer가 셰이더·트랜스폼에 적용</summary>
    public struct VfxState
    {
        public bool Visible;
        public Vector3 Scale;
        public Vector3 Offset;
        public float Spin;          // 도
        public float Erosion;       // 0 = 온전, 1 = 다 사라짐
        public float ErodeInvert;   // 1 = 가운데부터 구멍
        public float Thin;          // 1 = 원래 두께, 0 = 사라짐
        public float Sweep;         // 머리 위치 — 1 = 끝까지 그려짐
        public float SweepTail;     // 꼬리 위치 — 0 = 시작점부터 보임. 머리를 따라오면 사라짐
        public float Flash;         // 1 = 하얗게 번쩍
        public float ColorShift;    // 0 → 1 로 갈수록 코어 → 중간 → 외곽 색으로 식음
        public float Alpha;
        public float WobbleMul;
        public float BrightnessMul;

        public static VfxState Default => new()
        {
            Visible = true,
            Scale = Vector3.one,
            Thin = 1f,
            Sweep = 1f,
            Alpha = 1f,
            WobbleMul = 1f,
            BrightnessMul = 1f,
        };
    }

    /// <summary>
    /// 등장 → 유지 → 사라짐 3칸. 키프레임 대신 방식 하나 + 길이 + 세기만 고르면 됨.
    /// 시간은 레이어 시작 기준 초.
    /// </summary>
    [Serializable]
    public class VfxBlocks
    {
        [Header("등장")]
        public VfxAppear Appear = VfxAppear.Sweep;
        [Min(0f)] public float AppearTime = 0.12f;
        [Range(0f, 1f)] public float AppearStrength = 1f;

        [Header("유지")]
        public VfxHold Hold = VfxHold.Still;
        [Min(0f)] public float HoldTime = 0.03f;
        [Range(0f, 1f)] public float HoldStrength = 0.5f;

        [Header("사라짐")]
        public VfxVanish Vanish = VfxVanish.TailChase;
        [Min(0f)] public float VanishTime = 0.16f;
        [Range(0f, 1f)] public float VanishStrength = 0.8f;

        public float Length(float linger) => AppearTime + HoldTime + VanishTime * Mathf.Max(0.01f, linger);

        /// <param name="clock">회전·맥동처럼 끊기면 안 되는 유지 효과용 시계 (반복 구간이 돌아도 계속 흐름). 생략하면 t</param>
        public VfxState Evaluate(float t, float linger, float clock = float.NaN)
        {
            var s = VfxState.Default;
            float vanishTime = VanishTime * Mathf.Max(0.01f, linger);
            float total = AppearTime + HoldTime + vanishTime;

            if (t < 0f || t > total)
            {
                s.Visible = false;
                return s;
            }

            ApplyHold(ref s, float.IsNaN(clock) ? t : clock);

            if (t < AppearTime)
                ApplyAppear(ref s, t / AppearTime);
            else if (t >= AppearTime + HoldTime)
                ApplyVanish(ref s, vanishTime > 0f ? (t - AppearTime - HoldTime) / vanishTime : 1f);

            return s;
        }

        // 유지 효과는 생애 전체에 걸쳐 적용 — 회전·맥동이 중간에 끊기지 않게
        void ApplyHold(ref VfxState s, float t)
        {
            float k = HoldStrength;
            switch (Hold)
            {
                case VfxHold.Wobble:
                    s.WobbleMul *= 1f + 3f * k;
                    break;
                case VfxHold.Spin:
                    s.Spin = t * 720f * k;                          // 최대 초당 2바퀴
                    break;
                case VfxHold.Pulse:
                    s.Scale *= 1f + 0.15f * k * Mathf.Sin(t * Mathf.PI * 2f * 3f);
                    break;
            }
        }

        void ApplyAppear(ref VfxState s, float p)
        {
            float k = AppearStrength;
            float e = VfxEase.Out(p);

            switch (Appear)
            {
                case VfxAppear.Pop:
                    s.Flash = k;
                    break;
                case VfxAppear.Grow:
                    s.Scale *= Mathf.Lerp(1f - 0.8f * k, 1f, e);
                    s.Flash = (1f - e) * 0.6f * k;
                    break;
                case VfxAppear.Sweep:
                    s.Sweep = 1f - (1f - p) * (1f - p);              // 빠르게 출발, 끝에서 살짝 감속
                    s.Flash = (1f - p) * 0.25f * k;
                    break;
                case VfxAppear.Rise:
                    s.Offset.y -= (1f - e) * 0.5f * k;
                    s.Scale.y *= Mathf.Lerp(0.2f, 1f, e);
                    break;
                case VfxAppear.Fade:
                    s.Erosion = 1f - e;
                    break;
            }
        }

        void ApplyVanish(ref VfxState s, float p)
        {
            float k = VanishStrength;
            p = Mathf.Clamp01(p);

            switch (Vanish)
            {
                case VfxVanish.ErodeFromEnd:
                    s.Erosion = p;
                    s.ColorShift = Mathf.Clamp01(p * 1.5f) * k;
                    break;
                case VfxVanish.Hole:
                    s.Erosion = p;
                    s.ErodeInvert = 1f;
                    s.Scale *= 1f + 0.2f * k * p;
                    break;
                case VfxVanish.Thin:
                    s.Thin = 1f - VfxEase.Out(p);
                    s.ColorShift = p * k;
                    break;
                case VfxVanish.Shrink:
                    s.Scale *= 1f - VfxEase.In(p);
                    s.ColorShift = p * k;
                    break;
                case VfxVanish.ScatterUp:
                    s.Offset.y += VfxEase.Out(p) * 0.5f * (0.3f + k);
                    s.Erosion = p;
                    s.WobbleMul *= 1f + 3f * p;
                    break;
                case VfxVanish.CoolDown:
                    s.ColorShift = p;
                    s.BrightnessMul *= 1f - 0.7f * p * k;
                    s.Erosion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, p));
                    break;
                case VfxVanish.ExpandFade:
                    // 충격파 — 계속 퍼지면서 얇아지고 깎여 사라짐
                    s.Scale *= 1f + VfxEase.Out(p) * (0.3f + k);
                    s.Thin = 1f - p;
                    s.Erosion = p * 0.5f;
                    s.ColorShift = p * k;
                    break;
                case VfxVanish.TailChase:
                    // 꼬리가 점점 빨라지며 머리를 따라잡음 — 검이 지나간 자국이 끝으로 빨려 들어가듯
                    s.SweepTail = p * p;
                    s.ColorShift = p * k;
                    s.Erosion = p * 0.35f * k;
                    break;
            }
        }
    }

    public static class VfxEase
    {
        public static float Out(float p) { p = 1f - Mathf.Clamp01(p); return 1f - p * p * p; }
        public static float In(float p) { p = Mathf.Clamp01(p); return p * p * p; }
    }
}
