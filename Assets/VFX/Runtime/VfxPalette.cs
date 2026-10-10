using UnityEngine;

namespace ToonVfx
{
    /// <summary>코어 / 중간 / 외곽 / 외곽선 4색. 기본값은 불</summary>
    [CreateAssetMenu(menuName = "VFX/팔레트", fileName = "Palette")]
    public class VfxPalette : ScriptableObject
    {
        public string DisplayName = "불";
        public Color Core = new(1f, 0.97f, 0.78f);
        public Color Mid = new(1f, 0.78f, 0.15f);
        public Color Edge = new(1f, 0.36f, 0.05f);
        public Color Outline = new(0.55f, 0.05f, 0.02f);
    }

    /// <summary>프로필·팔레트를 비워 뒀을 때 쓰는 기본값</summary>
    public static class VfxDefaults
    {
        public const string ShaderName = "Toon/VfxErosion";

        static Material _material;
        static Material _additive;
        static VfxPalette _palette;
        static bool _warned;

        /// <summary>카툰 기본 — Alpha Clip</summary>
        public static Material Material => _material != null ? _material : (_material = Create("VfxErosion (기본)", false));

        /// <summary>빛 기본 — 더하기 블렌딩</summary>
        public static Material AdditiveMaterial => _additive != null ? _additive : (_additive = Create("VfxErosion (더하기)", true));

        public static Material ForStyle(VfxLayerStyle style) => style == VfxLayerStyle.Glow ? AdditiveMaterial : Material;

        static Material Create(string name, bool additive)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                if (!_warned) Debug.LogError($"[VFX] 셰이더 '{ShaderName}'를 찾을 수 없습니다. 빌드에서는 레이어에 셰이더 프로필을 지정하세요.");
                _warned = true;
                return null;
            }

            var m = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            if (additive) SetAdditive(m);
            return m;
        }

        public static void SetAdditive(Material m)
        {
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        public static VfxPalette Palette
        {
            get
            {
                if (_palette == null)
                {
                    _palette = ScriptableObject.CreateInstance<VfxPalette>();
                    _palette.hideFlags = HideFlags.DontSave;
                }
                return _palette;
            }
        }
    }
}
