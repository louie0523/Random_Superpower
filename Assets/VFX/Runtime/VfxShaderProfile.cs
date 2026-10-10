using System;
using UnityEngine;

namespace ToonVfx
{
    /// <summary>
    /// 셰이더 하나를 툴에 등록하는 설명서.
    /// 표준 속성(_Erosion, _ColorCore …)을 가진 셰이더면 동작 블록·팔레트·다이얼이 그대로 동작.
    /// Shader Graph 셰이더도 같은 이름의 속성을 만들고 프로필만 추가하면 됨.
    /// </summary>
    [CreateAssetMenu(menuName = "VFX/셰이더 프로필", fileName = "Profile")]
    public class VfxShaderProfile : ScriptableObject
    {
        public string DisplayName = "기본 (깎여 사라짐)";
        [Tooltip("모든 레이어가 공유. 값은 MaterialPropertyBlock으로 들어감")]
        public Material Material;

        [Tooltip("툴 인스펙터에 추가로 보여줄 속성 (1단계 창에서 사용)")]
        public VfxExposedProperty[] Exposed = Array.Empty<VfxExposedProperty>();
    }

    public enum VfxPropertyKind { Float, Color, Texture }

    [Serializable]
    public class VfxExposedProperty
    {
        public string Property = "_MyValue";
        public string Label = "내 값";
        public VfxPropertyKind Kind;
        public Vector2 Range = new(0f, 1f);
        [Tooltip("타임라인에서 시간에 따라 바꿀 수 있는지")]
        public bool Animatable;
    }

    /// <summary>표준 속성 이름. 셰이더와 반드시 일치</summary>
    public static class VfxShaderIds
    {
        public static readonly int MaskTex = Shader.PropertyToID("_MaskTex");
        public static readonly int ColorCore = Shader.PropertyToID("_ColorCore");
        public static readonly int ColorMid = Shader.PropertyToID("_ColorMid");
        public static readonly int ColorEdge = Shader.PropertyToID("_ColorEdge");
        public static readonly int ColorOutline = Shader.PropertyToID("_ColorOutline");
        public static readonly int CoreThreshold = Shader.PropertyToID("_CoreThreshold");
        public static readonly int MidThreshold = Shader.PropertyToID("_MidThreshold");
        public static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
        public static readonly int Erosion = Shader.PropertyToID("_Erosion");
        public static readonly int ErodeInvert = Shader.PropertyToID("_ErodeInvert");
        public static readonly int ColorShift = Shader.PropertyToID("_ColorShift");
        public static readonly int Flash = Shader.PropertyToID("_Flash");
        public static readonly int Brightness = Shader.PropertyToID("_Brightness");
        public static readonly int Alpha = Shader.PropertyToID("_Alpha");
        public static readonly int Thin = Shader.PropertyToID("_Thin");
        public static readonly int Sweep = Shader.PropertyToID("_Sweep");
        public static readonly int SweepTail = Shader.PropertyToID("_SweepTail");
        public static readonly int ShapeMode = Shader.PropertyToID("_ShapeMode");
        public static readonly int PerParticle = Shader.PropertyToID("_PerParticle");
        public static readonly int HeadBias = Shader.PropertyToID("_HeadBias");
        public static readonly int Sharpness = Shader.PropertyToID("_Sharpness");
        public static readonly int TipLength = Shader.PropertyToID("_TipLength");
        public static readonly int FaceCamera = Shader.PropertyToID("_FaceCamera");
        public static readonly int Style = Shader.PropertyToID("_Style");
        public static readonly int Softness = Shader.PropertyToID("_Softness");
        public static readonly int CoreGlow = Shader.PropertyToID("_CoreGlow");
        public static readonly int FlameEdge = Shader.PropertyToID("_FlameEdge");
        public static readonly int FlameScale = Shader.PropertyToID("_FlameScale");
        public static readonly int FlameSpeed = Shader.PropertyToID("_FlameSpeed");
        public static readonly int Wobble = Shader.PropertyToID("_Wobble");
        public static readonly int WobbleScale = Shader.PropertyToID("_WobbleScale");
        public static readonly int WobbleSpeed = Shader.PropertyToID("_WobbleSpeed");
        public static readonly int UVScroll = Shader.PropertyToID("_UVScroll");
        public static readonly int VfxTime = Shader.PropertyToID("_VfxTime");
    }
}
