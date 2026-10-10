using UnityEngine;

namespace ToonVfx
{
    public enum VfxLightMode
    {
        [InspectorName("번쩍 (한 번)")] Flash,
        [InspectorName("지속 (반복 중 켜짐)")] Sustain,
    }

    /// <summary>
    /// 조명 — 이펙트 안의 자식 오브젝트에 붙임.
    ///   번쩍: 폭발·타격 때 한 번 밝아졌다 꺼짐
    ///   지속: 버프·상태이상 동안 은은하게 일렁이며 켜져 있다가, 끝내면 서서히 꺼짐
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Light))]
    [AddComponentMenu("VFX/VFX Light")]
    public class VfxLight : MonoBehaviour
    {
        public VfxLightMode Mode = VfxLightMode.Flash;
        [Min(0f)] public float StartTime;
        [Tooltip("번쩍: 꺼질 때까지 시간 / 지속: 켜지고 꺼지는 데 걸리는 시간")]
        [Min(0.01f)] public float Duration = 0.25f;
        [Min(0f)] public float Intensity = 6f;
        [Min(0.1f)] public float Range = 5f;
        [Tooltip("지속 모드 — 일렁이는 정도 (0 = 일정)")]
        [Range(0f, 0.5f)] public float Flicker = 0.15f;
        [Tooltip("켜면 팔레트의 중간 색, 끄면 아래 색")]
        public bool UsePaletteColor = true;
        public Color Color = Color.white;

        Light _light;

        /// <summary>1회 재생 기준으로 끝나는 시각 (지속 모드는 켜지는 데까지만)</summary>
        public float EndTime => StartTime + Duration;

        /// <summary>끝내기 후 꺼지기까지 걸리는 시간</summary>
        public float EndLength => Mode == VfxLightMode.Sustain ? Duration : 0f;

        Light L
        {
            get
            {
                if (_light == null)
                {
                    _light = GetComponent<Light>();
                    _light.type = LightType.Point;
                    _light.shadows = LightShadows.None;
                }
                return _light;
            }
        }

        void OnEnable() => Hide();

        public void Hide()
        {
            if (L != null) L.enabled = false;
        }

        public void Evaluate(float t, in VfxContext ctx)
        {
            float k;
            if (Mode == VfxLightMode.Flash)
            {
                float p = (t - StartTime) / Duration;
                if (p < 0f || p > 1f) { Hide(); return; }
                k = (1f - p) * (1f - p);
            }
            else
            {
                float fadeIn = Mathf.Clamp01((t - StartTime) / Duration);
                float fadeOut = ctx.StopTime >= 0f ? 1f - Mathf.Clamp01((t - ctx.StopTime) / Duration) : 1f;
                float flick = 1f + Flicker * (Mathf.Sin(ctx.RawTime * 13f) * 0.6f + Mathf.Sin(ctx.RawTime * 7.3f + 1.7f) * 0.4f);
                k = fadeIn * fadeOut * flick;
                if (k <= 0.001f) { Hide(); return; }
            }

            var pal = ctx.Palette != null ? ctx.Palette : VfxDefaults.Palette;
            L.enabled = true;
            L.color = UsePaletteColor ? pal.Mid : Color;
            L.range = Range * Mathf.Max(0.01f, ctx.Size);
            L.intensity = Intensity * k * (ctx.BrightnessMul > 0f ? ctx.BrightnessMul : 1f);
        }
    }
}
