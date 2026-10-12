using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToonVfx
{
    /// <summary>이펙트가 레이어에 넘겨주는 공통 값 (다이얼·팔레트·카메라)</summary>
    public struct VfxContext
    {
        public float Time;
        public float Size;
        public float Thickness;
        public float Linger;
        public float WobbleMul;
        public float BrightnessMul;
        public float RadiusMul;     // 판정 범위에 맞추기 — 호 반지름 배율 (폭은 그대로)
        public float AngleMul;      // 판정 범위에 맞추기 — 호 각도 배율
        public int FaceCameraOverride;  // -1 레이어 설정, 0 끔, 1 켬 (본인 화면 전용 설정)
        public float RawTime;       // 반복 구간과 무관한 실제 이펙트 시각 (회전·맥동 시계)
        public float StopTime;      // '시작 → 반복 → 끝'에서 끝내기 시작한 이펙트 시각. -1 = 아직
        public bool Endless;        // 1회가 아닌 재생 — '계속 뿜기'가 끝없이 뿜음
        public VfxPalette Palette;
        public Camera Camera;
    }

    /// <summary>
    /// 모양 레이어 하나. 이 오브젝트의 트랜스폼 = 그린 위치(작업자가 편집),
    /// 실제 렌더링은 숨은 자식 __VfxVisual이 하고 동작 블록이 그 자식만 움직임 → 편집값이 오염되지 않음.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, SelectionBase, AddComponentMenu("VFX/VFX Layer")]
    public class VfxLayer : MonoBehaviour
    {
        const string VisualName = "__VfxVisual";

        [Header("모양")]
        public VfxShape Shape = VfxShape.Arc;
        public VfxArcSettings Arc = new();
        public VfxCardSettings Card = new();
        public VfxRingSettings Ring = new();
        public VfxBurstSettings Burst = new();
        public VfxPillarSettings Pillar = new();
        public VfxStreakSettings Streak = new();
        public VfxTrailSettings Trail = new();
        public VfxConeSettings Cone = new();

        [Header("마스크 · 셰이더 · 색")]
        [Tooltip("흑백 모양 텍스처 (직접 그린 것·AI). 비우면 아래 기본 마스크")]
        public Texture2D Mask;
        [Tooltip("켜면 모양에 맞는 기본 마스크 자동 (호 = 칼자국, 링 = 띠, 카드 = 원, 뿌리기 = 가시/원)")]
        public bool AutoMask = true;
        [Tooltip("자동을 끄면 이 기본 마스크 사용")]
        public VfxBuiltinMask BuiltinMask = VfxBuiltinMask.Radial;
        [Tooltip("비우면 스타일에 맞는 기본 셰이더 (카툰 = 깎여 사라짐, 빛 = 더하기)")]
        public VfxShaderProfile Profile;
        [Tooltip("이 레이어만 다른 색 (예: 불 폭발 속 돌 파편). 비우면 이펙트 팔레트")]
        public VfxPalette PaletteOverride;

        [Header("스타일")]
        public VfxLayerStyle Style = VfxLayerStyle.Toon;
        [Tooltip("빛 스타일 — 가장자리가 흐려지는 폭")]
        [Range(0.01f, 0.6f)] public float Softness = 0.2f;
        [Tooltip("빛 스타일 — 가운데가 하얗게 타오르는 정도")]
        [Range(0f, 2f)] public float CoreGlow = 0.8f;

        [Header("불꽃 가장자리")]
        [Tooltip("바깥 가장자리를 불꽃 혀처럼 찢음. 0 = 끔")]
        [Range(0f, 1f)] public float FlameEdge;
        [Tooltip("혀의 촘촘함")]
        [Min(0.5f)] public float FlameScale = 6f;
        [Tooltip("꼬리 쪽으로 흐르는 속도")]
        public float FlameSpeed = 3f;

        [Header("타이밍 (초)")]
        [Min(0f)] public float StartTime;
        public VfxBlocks Blocks = new();
        [Tooltip("끄면 이펙트의 프레임 끊기를 무시하고 부드럽게 (스파크 등)")]
        public bool UseFrameStep = true;

        [Header("모양새")]
        [Range(0f, 1f)] public float CoreThreshold = 0.65f;
        [Range(0f, 1f)] public float MidThreshold = 0.35f;
        [Range(0f, 0.3f)] public float OutlineWidth = 0.06f;
        [Range(0f, 1f)] public float Wobble = 0.15f;
        [Min(0.1f)] public float WobbleScale = 3f;
        public float WobbleSpeed = 1.5f;
        [Min(0f)] public float Brightness = 1.5f;
        public Vector2 UVScroll;

        Transform _visual;
        MeshFilter _mf;
        MeshRenderer _mr;
        Mesh _mesh;
        MaterialPropertyBlock _mpb;
        int _meshHash = int.MinValue;

        /// <summary>뿌리기 조각 하나의 최대 수명 (수명 랜덤 포함)</summary>
        float BurstCycle(float linger) => Blocks.Length(linger) * (1f + Burst.LifeRandom);

        /// <summary>1회 재생 기준으로 이 레이어가 끝나는 이펙트 시각</summary>
        public float EndTime(float linger)
        {
            if (Shape != VfxShape.Burst) return StartTime + Blocks.Length(linger);
            // 계속 뿜기는 1회 재생에서 한 주기 동안 뿜고, 마지막 조각이 사라질 때까지
            return StartTime + BurstCycle(linger) * (Burst.Continuous ? 2f : 1f);
        }

        /// <summary>'시작 → 반복 → 끝'에서 Stop 후 다 사라지기까지 걸리는 시간</summary>
        public float EndLength(float linger)
        {
            if (Shape == VfxShape.Burst) return Burst.Continuous ? BurstCycle(linger) : 0f;
            return Blocks.VanishTime * Mathf.Max(0.01f, linger);
        }

        VfxBuiltinMask ResolvedMask => !AutoMask ? BuiltinMask : Shape switch
        {
            VfxShape.Arc => VfxBuiltinMask.SlashStrip,
            VfxShape.Ring => VfxBuiltinMask.Band,
            VfxShape.Pillar => VfxBuiltinMask.PillarFade,
            VfxShape.Cone => VfxBuiltinMask.PillarFade,     // 앞 끝이 밝고 뒤로 흐려짐
            VfxShape.Streak => VfxBuiltinMask.Spike,
            VfxShape.Trail => VfxBuiltinMask.TrailFade,
            VfxShape.Burst => Burst.Facing == VfxBurstFacing.Velocity ? VfxBuiltinMask.Spike : VfxBuiltinMask.Radial,
            _ => VfxBuiltinMask.Radial,
        };

        void OnEnable() => EnsureVisual();

        void OnDisable()
        {
            if (_mr != null) _mr.enabled = false;
        }

        void OnDestroy()
        {
            if (_visual != null) DestroySafe(_visual.gameObject);
            if (_mesh != null) DestroySafe(_mesh);
        }

        static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        bool EnsureVisual()
        {
            // 프리팹 에셋 자체(씬 밖)에는 자식을 만들 수 없음
            if (!gameObject.scene.IsValid()) return false;

            if (_visual == null)
            {
                _visual = transform.Find(VisualName);       // 도메인 리로드 후 남아 있는 것 재사용
                if (_visual == null)
                {
                    var go = new GameObject(VisualName) { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
                    _visual = go.transform;
                    _visual.SetParent(transform, false);
                }
            }

            if (_mf == null && !_visual.TryGetComponent(out _mf))
                _mf = _visual.gameObject.AddComponent<MeshFilter>();

            if (_mr == null)
            {
                if (!_visual.TryGetComponent(out _mr))
                    _mr = _visual.gameObject.AddComponent<MeshRenderer>();
                _mr.shadowCastingMode = ShadowCastingMode.Off;
                _mr.receiveShadows = false;
                _mr.lightProbeUsage = LightProbeUsage.Off;
                _mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _mr.enabled = false;
            }

            if (_mesh == null)
            {
                // 이름에 인스턴스 ID — 리로드 후엔 내 것을 다시 쓰고, 복제된 오브젝트는 새로 만듦
                string meshName = "VfxLayerMesh_" + GetInstanceID();
                var existing = _mf.sharedMesh;
                if (existing != null && existing.name == meshName) _mesh = existing;
                else
                {
                    _mesh = new Mesh { name = meshName, hideFlags = HideFlags.HideAndDontSave };
                    _mesh.MarkDynamic();
                }
                _meshHash = int.MinValue;
            }
            if (_mf.sharedMesh != _mesh) _mf.sharedMesh = _mesh;
            return true;
        }

        void RebuildMeshIfNeeded(in VfxContext ctx)
        {
            float thickness = ctx.Thickness;
            float rMul = ctx.RadiusMul > 0f ? ctx.RadiusMul : 1f;
            float aMul = ctx.AngleMul > 0f ? ctx.AngleMul : 1f;

            int hash = Shape switch
            {
                VfxShape.Arc => System.HashCode.Combine(Shape, Arc.Hash(), thickness, rMul, aMul),
                VfxShape.Ring => System.HashCode.Combine(Shape, Ring.Hash(), thickness),
                VfxShape.Pillar => System.HashCode.Combine(Shape, Pillar.Hash(), thickness),
                VfxShape.Cone => System.HashCode.Combine(Shape, Cone.Hash(), thickness),
                _ => System.HashCode.Combine(Shape, Card.Hash(), thickness),
            };
            if (hash == _meshHash) return;
            _meshHash = hash;

            switch (Shape)
            {
                case VfxShape.Arc: VfxMeshBuilder.Arc(_mesh, Arc, thickness, rMul, aMul); break;
                case VfxShape.Ring: VfxMeshBuilder.Ring(_mesh, Ring, thickness); break;
                case VfxShape.Pillar: VfxMeshBuilder.Pillar(_mesh, Pillar, thickness); break;
                case VfxShape.Cone: VfxMeshBuilder.Cone(_mesh, Cone, thickness); break;
                default: VfxMeshBuilder.Card(_mesh, new Vector2(Card.Size.x, Card.Size.y * thickness)); break;
            }
        }

        // ── 잔상 줄 경로 기록 (월드 위치, 기록 시각) ──
        readonly List<(Vector3 pos, float time)> _trail = new();

        /// <summary>재생을 새로 시작할 때 — 잔상 줄 경로 등 지난 재생의 기록을 지움</summary>
        public void ResetRuntime() => _trail.Clear();

        void RecordTrail(float now)
        {
            if (!Application.isPlaying) return;

            // 오래된 점 버림 (경계 하나는 남겨 꼬리가 끊기지 않게)
            while (_trail.Count > 1 && now - _trail[1].time > Trail.Time) _trail.RemoveAt(0);

            var pos = transform.position;
            if (_trail.Count == 0 || (pos - _trail[^1].pos).sqrMagnitude >= Trail.MinDistance * Trail.MinDistance)
                _trail.Add((pos, now));
            if (_trail.Count > 128) _trail.RemoveAt(0);
        }

        public void Hide()
        {
            if (_mr != null) _mr.enabled = false;
        }

        /// <summary>t = 이펙트 기준 시각(초)</summary>
        public void Evaluate(float t, in VfxContext ctx)
        {
            if (!EnsureVisual()) return;

            float lt = t - StartTime;
            bool burst = Shape == VfxShape.Burst;
            bool procedural = burst || Shape == VfxShape.Streak || Shape == VfxShape.Trail;   // 매 프레임 메시를 새로 만듦

            // 뿌리기는 조각마다 따로 계산 — 레이어 전체 상태는 셰이더 공통 값(구멍 방향 등)에만 씀
            float clock = ctx.RawTime - StartTime;      // 유지 효과(회전·맥동)는 반복 구간과 무관하게 계속 흐름
            var s = Blocks.Evaluate(burst ? Mathf.Min(lt, Blocks.Length(ctx.Linger)) : lt, ctx.Linger, clock);
            if (burst)
            {
                bool endless = Burst.Continuous && ctx.Endless;
                bool stopped = ctx.StopTime >= 0f;
                s.Visible = lt >= 0f &&
                            (endless && !stopped
                                ? true
                                : lt <= (stopped && endless ? ctx.StopTime - StartTime + BurstCycle(ctx.Linger) : EndTime(ctx.Linger) - StartTime));
            }
            if (!s.Visible) { _mr.enabled = false; return; }

            var mat = Profile != null && Profile.Material != null ? Profile.Material : VfxDefaults.ForStyle(Style);
            if (mat == null) { _mr.enabled = false; return; }
            if (_mr.sharedMaterial != mat) _mr.sharedMaterial = mat;

            // ── 트랜스폼 (숨은 자식만) ──
            if (procedural)
            {
                // 조각 위치·크기를 메시에 직접 넣으므로 자식은 레이어와 겹쳐 둠
                _visual.localPosition = Vector3.zero;
                _visual.localRotation = Quaternion.identity;
                _visual.localScale = Vector3.one;

                if (burst)
                {
                    // 계속 뿜기가 멈추는 시각 (레이어 기준) — 이후로는 새 조각이 안 나오고 남은 것만 사라짐
                    float stopLt = float.PositiveInfinity;
                    if (Burst.Continuous)
                    {
                        if (ctx.StopTime >= 0f) stopLt = ctx.StopTime - StartTime;
                        else if (!ctx.Endless) stopLt = BurstCycle(ctx.Linger);
                    }
                    VfxBurstBuilder.Build(_mesh, Burst, Blocks, lt, stopLt, ctx, _visual);
                }
                else if (Shape == VfxShape.Streak)
                {
                    VfxLineBuilder.Streak(_mesh, Streak, s, clock, ctx, _visual);
                }
                else
                {
                    RecordTrail(ctx.RawTime);
                    VfxLineBuilder.Trail(_mesh, Trail, _trail, ctx.RawTime, !Application.isPlaying, s, ctx, _visual);
                }
                _meshHash = int.MinValue;
            }
            else
            {
                RebuildMeshIfNeeded(ctx);
                _visual.localPosition = s.Offset * ctx.Size;
                _visual.localScale = s.Scale * ctx.Size;

                bool billboard = (Shape == VfxShape.Card && Card.Facing == VfxFacing.Billboard)
                              || (Shape == VfxShape.Ring && Ring.Facing == VfxFacing.Billboard);

                if (billboard && ctx.Camera != null)
                {
                    var camRot = ctx.Camera.transform.rotation;
                    _visual.rotation = Shape == VfxShape.Ring
                        ? camRot * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.AngleAxis(s.Spin, Vector3.up)   // 고리 면이 카메라를 향함
                        : camRot * Quaternion.AngleAxis(s.Spin, Vector3.forward);
                }
                else
                    _visual.localRotation = Quaternion.AngleAxis(s.Spin, Shape == VfxShape.Card || Shape == VfxShape.Cone ? Vector3.forward : Vector3.up);
            }

            // ── 셰이더 값 ──
            var pal = PaletteOverride != null ? PaletteOverride : ctx.Palette != null ? ctx.Palette : VfxDefaults.Palette;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetTexture(VfxShaderIds.MaskTex, Mask != null ? Mask : VfxMaskLibrary.Get(ResolvedMask));
            _mpb.SetColor(VfxShaderIds.ColorCore, pal.Core);
            _mpb.SetColor(VfxShaderIds.ColorMid, pal.Mid);
            _mpb.SetColor(VfxShaderIds.ColorEdge, pal.Edge);
            _mpb.SetColor(VfxShaderIds.ColorOutline, pal.Outline);
            _mpb.SetFloat(VfxShaderIds.CoreThreshold, CoreThreshold);
            _mpb.SetFloat(VfxShaderIds.MidThreshold, MidThreshold);
            _mpb.SetFloat(VfxShaderIds.OutlineWidth, OutlineWidth);
            _mpb.SetFloat(VfxShaderIds.Erosion, s.Erosion);
            _mpb.SetFloat(VfxShaderIds.ErodeInvert, s.ErodeInvert);
            _mpb.SetFloat(VfxShaderIds.ColorShift, s.ColorShift);
            _mpb.SetFloat(VfxShaderIds.Flash, s.Flash);
            _mpb.SetFloat(VfxShaderIds.Brightness, Brightness * s.BrightnessMul * ctx.BrightnessMul);
            _mpb.SetFloat(VfxShaderIds.Alpha, s.Alpha);
            _mpb.SetFloat(VfxShaderIds.Thin, procedural ? 1f : Mathf.Clamp01(s.Thin));
            _mpb.SetFloat(VfxShaderIds.Sweep, procedural ? 1f : Mathf.Clamp01(s.Sweep));
            _mpb.SetFloat(VfxShaderIds.SweepTail, procedural ? 0f : Mathf.Clamp01(s.SweepTail));
            _mpb.SetFloat(VfxShaderIds.ShapeMode, Shape switch { VfxShape.Arc => 1f, VfxShape.Pillar => 2f, VfxShape.Cone => 3f, _ => 0f });
            _mpb.SetFloat(VfxShaderIds.PerParticle, procedural ? 1f : 0f);
            _mpb.SetFloat(VfxShaderIds.HeadBias, Arc.HeadBias);
            _mpb.SetFloat(VfxShaderIds.Sharpness, Arc.Sharpness);
            _mpb.SetFloat(VfxShaderIds.TipLength, Arc.TipLength);
            bool face = ctx.FaceCameraOverride switch { 1 => true, 0 => false, _ => Arc.FaceCamera };
            _mpb.SetFloat(VfxShaderIds.FaceCamera, Shape == VfxShape.Arc && face ? 1f : 0f);
            _mpb.SetFloat(VfxShaderIds.Style, Style == VfxLayerStyle.Glow ? 1f : 0f);
            _mpb.SetFloat(VfxShaderIds.Softness, Softness);
            _mpb.SetFloat(VfxShaderIds.CoreGlow, CoreGlow);
            _mpb.SetFloat(VfxShaderIds.FlameEdge, FlameEdge);
            _mpb.SetFloat(VfxShaderIds.FlameScale, FlameScale);
            _mpb.SetFloat(VfxShaderIds.FlameSpeed, FlameSpeed);
            _mpb.SetFloat(VfxShaderIds.StreakCount, Cone.StreakCount);
            _mpb.SetFloat(VfxShaderIds.StreakAmount, Shape == VfxShape.Cone ? Cone.StreakAmount : 0f);
            _mpb.SetFloat(VfxShaderIds.StreakSpeed, Cone.StreakSpeed);
            _mpb.SetFloat(VfxShaderIds.Twist, Cone.Twist);
            _mpb.SetFloat(VfxShaderIds.Wobble, Mathf.Clamp01(Wobble * s.WobbleMul * ctx.WobbleMul));
            _mpb.SetFloat(VfxShaderIds.WobbleScale, WobbleScale);
            _mpb.SetFloat(VfxShaderIds.WobbleSpeed, WobbleSpeed);
            _mpb.SetVector(VfxShaderIds.UVScroll, new Vector4(UVScroll.x, UVScroll.y, 0f, 0f));
            _mpb.SetFloat(VfxShaderIds.VfxTime, ctx.Time);
            _mr.SetPropertyBlock(_mpb);

            _mr.enabled = true;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            _meshHash = int.MinValue;
            UnityEditor.EditorApplication.delayCall += RefreshInEditor;
        }

        void RefreshInEditor()
        {
            if (this == null || Application.isPlaying || !gameObject.scene.IsValid()) return;
            var fx = GetComponentInParent<VfxEffect>(true);
            if (fx != null) fx.Refresh();
        }
#endif
    }
}
