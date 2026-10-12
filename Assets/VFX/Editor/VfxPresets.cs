using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ToonVfx.EditorTools
{
    /// <summary>프리셋 하나 — 새 이펙트 마법사와 테스트 메뉴가 같이 씀</summary>
    public sealed class VfxPreset
    {
        public string Id;
        public string Category;
        public string Name;
        public string Description;
        public string FileName;         // 테스트 메뉴·상태이상 프리팹 이름
        public string Palette;          // Assets/VFX/Palettes 안의 파일 이름
        public VfxPreviewAnchor Anchor;
        public float PreviewTime = 0.15f;
        public Action<Transform, VfxEffect> Build;
    }

    /// <summary>
    /// 기본 프리셋 모음. Build는 빈 이펙트 루트에 레이어를 붙임 (루트 위치·팔레트는 Create가 정함).
    /// 내가 만든 프리셋은 Assets/VFX/Presets 폴더의 프리팹 — 마법사가 같이 보여줌
    /// </summary>
    public static class VfxPresets
    {
        const string Root = VfxSetup.Root;

        public static readonly string[] Categories = { "슬래시", "폭발", "버프", "돌진", "상태이상" };

        public static readonly VfxPreset[] All =
        {
            new() { Id = "SlashGlow", Category = "슬래시", Name = "빛 슬래시", Description = "하얀 심지 + 발광 띠 + 바깥 실선 (메인 스타일)", FileName = "VFX_TestSlash_Glow", Palette = "Palette_Dark", Anchor = VfxPreviewAnchor.Chest, PreviewTime = 0.15f, Build = SlashGlow },
            new() { Id = "SlashToon", Category = "슬래시", Name = "카툰 슬래시", Description = "3단 단색 + 외곽선 + 불꽃 혀", FileName = "VFX_TestSlash_Toon", Palette = "Palette_Fire", Anchor = VfxPreviewAnchor.Chest, PreviewTime = 0.15f, Build = SlashToon },
            new() { Id = "ExplosionGlow", Category = "폭발", Name = "빛 폭발", Description = "섬광 코어 + 충격파 + 방사형 가시 + 반짝이 + 조명", FileName = "VFX_TestExplosion_Glow", Palette = "Palette_Dark", Anchor = VfxPreviewAnchor.Front, PreviewTime = 0.08f, Build = ExplosionGlow },
            new() { Id = "ExplosionToon", Category = "폭발", Name = "카툰 폭발", Description = "불덩이 + 불똥 + 돌 파편 + 바닥 고리 + 조명", FileName = "VFX_TestExplosion_Toon", Palette = "Palette_Fire", Anchor = VfxPreviewAnchor.Front, PreviewTime = 0.1f, Build = ExplosionToon },
            new() { Id = "BuffGlow", Category = "버프", Name = "빛 오라", Description = "오라 기둥 + 발밑 고리 + 피어오르는 반짝이 (시작 → 반복 → 끝)", FileName = "VFX_TestBuff_Glow", Palette = "Palette_Holy", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = BuffGlow },
            new() { Id = "BuffToon", Category = "버프", Name = "불꽃 오라", Description = "불꽃 기둥 + 불꽃 고리 + 불똥 (시작 → 반복 → 끝)", FileName = "VFX_TestBuff_Toon", Palette = "Palette_Fire", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = BuffToon },
            new() { Id = "DashLines", Category = "돌진", Name = "속도선 + 잔상", Description = "플레이어를 따라감 — 속도선 + 양옆 잔상 줄, 0.4초 뒤 자동 종료", FileName = "Dash_Lines", Palette = "Palette_Lightning", Anchor = VfxPreviewAnchor.Chest, PreviewTime = 0.2f, Build = DashLines },
            new() { Id = "DashDust", Category = "돌진", Name = "출발 먼지", Description = "출발 자리에 남음 — 뒤로 퍼지는 먼지구름 + 바닥 고리", FileName = "Dash_Dust", Palette = "Palette_Dust", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.12f, Build = DashDust },
            new() { Id = "DashCone", Category = "돌진", Name = "돌진 원뿔 (바람 가르기)", Description = "몸 앞을 감싸는 빛 원뿔 + 안쪽 원뿔 + 속도선 — 플레이어를 따라감, 0.45초 뒤 자동 종료", FileName = "Dash_Cone", Palette = "Palette_Holy", Anchor = VfxPreviewAnchor.Chest, PreviewTime = 0.15f, Build = DashCone },
            new() { Id = "DashImpact", Category = "돌진", Name = "도착 충격", Description = "번쩍 + 충격파 + 앞으로 튀는 가시 + 조명", FileName = "Dash_Impact", Palette = "Palette_Lightning", Anchor = VfxPreviewAnchor.Front, PreviewTime = 0.06f, Build = DashImpact },
            new() { Id = "Status_Burn", Category = "상태이상", Name = "화상", Description = "세 갈래 불꽃이 피어오름 — 다른 사람 화면에서만", FileName = "Status_Burn", Palette = "Palette_Fire", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = Status_Burn },
            new() { Id = "Status_Frost", Category = "상태이상", Name = "동상", Description = "눈송이가 내려앉음 — 다른 사람 화면에서만", FileName = "Status_Frost", Palette = "Palette_Ice", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = Status_Frost },
            new() { Id = "Status_Poison", Category = "상태이상", Name = "중독", Description = "해골이 떠올랐다 뚫리며 사라짐 — 다른 사람 화면에서만", FileName = "Status_Poison", Palette = "Palette_Poison", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = Status_Poison },
            new() { Id = "Status_Slow", Category = "상태이상", Name = "둔화", Description = "아래 화살표가 내려감 — 다른 사람 화면에서만", FileName = "Status_Slow", Palette = "Palette_Lightning", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = Status_Slow },
            new() { Id = "Status_Stun", Category = "상태이상", Name = "기절", Description = "머리 위 오각별이 돎 — 다른 사람 화면에서만", FileName = "Status_Stun", Palette = "Palette_Holy", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = Status_Stun },
            new() { Id = "Status_DeepWound", Category = "상태이상", Name = "깊은 상처", Description = "핏방울이 흘러 떨어짐 — 다른 사람 화면에서만", FileName = "Status_DeepWound", Palette = "Palette_Blood", Anchor = VfxPreviewAnchor.Feet, PreviewTime = 0.6f, Build = Status_DeepWound },
        };

        public static VfxPreset Get(string id) => Array.Find(All, p => p.Id == id);

        public static IEnumerable<VfxPreset> InCategory(string category)
        {
            foreach (var p in All)
                if (p.Category == category) yield return p;
        }

        /// <summary>미리보기 씬에서 이펙트를 놓는 기준점 (더미 캐릭터 기준)</summary>
        public static Vector3 AnchorPosition(VfxPreviewAnchor a) => a switch
        {
            VfxPreviewAnchor.Feet => Vector3.zero,
            VfxPreviewAnchor.Front => new Vector3(0f, 1f, 1.5f),
            _ => new Vector3(0f, 1.2f, 0f),
        };

        public static VfxPalette LoadPalette(string file) =>
            string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<VfxPalette>($"{Root}/Palettes/{file}.asset");

        /// <summary>프리셋으로 씬에 이펙트를 만듦 (Undo 등록은 부르는 쪽에서)</summary>
        public static VfxEffect Create(VfxPreset p, string name, VfxPalette palette = null)
        {
            var fx = CreateEmpty(name, p.Anchor, palette != null ? palette : LoadPalette(p.Palette));
            p.Build(fx.transform, fx);
            fx.Collect();
            fx.SetTime(p.PreviewTime);
            return fx;
        }

        /// <summary>레이어 없는 빈 이펙트</summary>
        public static VfxEffect CreateEmpty(string name, VfxPreviewAnchor anchor, VfxPalette palette)
        {
            var root = new GameObject(name);
            root.transform.position = AnchorPosition(anchor);
            var fx = root.AddComponent<VfxEffect>();
            fx.FrameRate = 30;      // 12fps면 0.12초 쓸기가 1~2프레임에 끝나 '툭' 생긴 것처럼 보임
            fx.Palette = palette;
            fx.PreviewAnchor = anchor;
            return fx;
        }

        /// <summary>상태이상 공통 — 걸리면 켜지고 풀리면 사라짐, 본인 화면엔 안 보임</summary>
        static void StatusSetup(VfxEffect fx)
        {
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            fx.FrameRate = 24;
            fx.HideForOwner = true;         // 안전장치 — StatusVfx가 본인은 이미 건너뜀
        }

        // ══════════════════════ 프리셋 본문 ══════════════════════

        // 빛 슬래시 — 하얀 심지 + 진한 색 번짐 + 찢긴 가장자리, 더하기 블렌딩
        static void SlashGlow(Transform root, VfxEffect fx)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var tilt = Quaternion.Euler(0f, 0f, -20f);

            // 본체 — 넓고 부드러운 발광 띠, 바깥이 불꽃처럼 찢김
            AddArc(root, "Glow_Slash", tilt, Vector3.zero, profile, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Arc.Radius = 1.3f;
                l.Arc.Angle = 160f;
                l.Arc.Width = 0.6f;
                l.Arc.HeadBias = 0.75f;
                l.Softness = 0.25f;
                l.CoreGlow = 0.6f;
                l.Brightness = 1.8f;
                l.Wobble = 0.25f;
                l.FlameEdge = 0.45f;
                l.FlameScale = 9f;
                l.Blocks.VanishTime = 0.2f;
            });

            // 심지 — 바깥 테두리를 따라 하얗게 타는 가는 선
            AddArc(root, "Glow_Core", tilt, Vector3.zero, profile, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.StartTime = 0.01f;
                l.Arc.Radius = 1.3f;
                l.Arc.Angle = 150f;
                l.Arc.Width = 0.12f;
                l.Arc.HeadBias = 0.8f;
                l.Softness = 0.12f;
                l.CoreGlow = 2f;
                l.Brightness = 3f;
                l.Wobble = 0.05f;
                l.Blocks.AppearTime = 0.11f;
                l.Blocks.HoldTime = 0.02f;
                l.Blocks.VanishTime = 0.14f;
            });

            // 바깥 실선 — 살짝 늦게 그어지고 얇아지며 사라짐
            AddArc(root, "Glow_Wisp", tilt, new Vector3(0f, 0.05f, 0f), profile, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.StartTime = 0.04f;
                l.Arc.Radius = 1.55f;
                l.Arc.Angle = 120f;
                l.Arc.Width = 0.05f;
                l.Arc.HeadBias = 0.85f;
                l.Softness = 0.3f;
                l.Brightness = 2.5f;
                l.Blocks.AppearTime = 0.1f;
                l.Blocks.HoldTime = 0.02f;
                l.Blocks.Vanish = VfxVanish.Thin;
                l.Blocks.VanishTime = 0.12f;
            });
        }

        // 카툰 슬래시 — 3단 단색 + 외곽선 + 불꽃 혀
        static void SlashToon(Transform root, VfxEffect fx)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            var tilt = Quaternion.Euler(0f, 0f, -20f);       // 대각선 베기

            // 본체 — 왼쪽에서 오른쪽으로 쓸고 지나감 → 잠깐 유지 → 꼬리가 따라오며 끝으로 빨려 들어감
            AddArc(root, "Slash", tilt, Vector3.zero, profile, l =>
            {
                l.FlameEdge = 0.55f;
                l.Arc.Radius = 1.3f;
                l.Arc.Angle = 150f;
                l.Arc.Width = 0.5f;
                l.Arc.HeadBias = 0.75f;
                l.Blocks.Appear = VfxAppear.Sweep;
                l.Blocks.AppearTime = 0.12f;
                l.Blocks.HoldTime = 0.03f;
                l.Blocks.Vanish = VfxVanish.TailChase;
                l.Blocks.VanishTime = 0.16f;
            });

            // 안쪽 보조 — 같은 방식, 살짝 늦게 출발해 먼저 사라짐
            AddArc(root, "Slash_Inner", tilt, new Vector3(0f, -0.03f, 0f), profile, l =>
            {
                l.StartTime = 0.03f;
                l.Arc.Radius = 1.0f;
                l.Arc.Angle = 120f;
                l.Arc.Width = 0.2f;
                l.Arc.HeadBias = 0.8f;
                l.Blocks.Appear = VfxAppear.Sweep;
                l.Blocks.AppearTime = 0.1f;
                l.Blocks.HoldTime = 0.01f;
                l.Blocks.Vanish = VfxVanish.TailChase;
                l.Blocks.VanishTime = 0.12f;
            });

            // 바깥 가는 선 — 본체 바로 뒤를 따라 그어지고 얇아지며 사라짐
            AddArc(root, "Slash_Line", tilt, new Vector3(0f, 0.04f, 0f), profile, l =>
            {
                l.StartTime = 0.02f;
                l.Arc.Radius = 1.5f;
                l.Arc.Angle = 140f;
                l.Arc.Width = 0.08f;
                l.Arc.HeadBias = 0.85f;
                l.Blocks.Appear = VfxAppear.Sweep;
                l.Blocks.AppearTime = 0.12f;
                l.Blocks.HoldTime = 0.02f;
                l.Blocks.Vanish = VfxVanish.Thin;
                l.Blocks.VanishTime = 0.12f;
                l.Brightness = 2f;
                l.OutlineWidth = 0f;
            });
        }

        // 빛 폭발 — 하얀 섬광 코어 + 충격파 + 방사형 가시 + 반짝이 + 조명
        static void ExplosionGlow(Transform root, VfxEffect fx)
        {
            fx.FrameRate = 24;
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");

            // 코어 — 커지며 번쩍 → 가운데 구멍 나며 사라짐
            AddLayer(root, "Core", VfxShape.Card, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Card.Size = new Vector2(1.6f, 1.6f);
                l.Brightness = 2.2f;
                l.CoreGlow = 1.5f;
                l.Softness = 0.25f;
                l.FlameEdge = 0.35f;
                l.FlameScale = 5f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.06f;
                l.Blocks.HoldTime = 0.04f;
                l.Blocks.Vanish = VfxVanish.Hole;
                l.Blocks.VanishTime = 0.25f;
            });

            // 충격파 — 카메라를 보는 고리가 퍼지며 사라짐
            AddLayer(root, "Shockwave", VfxShape.Ring, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Ring.Facing = VfxFacing.Billboard;
                l.Ring.Radius = 1.1f;
                l.Ring.Width = 0.18f;
                l.Brightness = 2f;
                l.Softness = 0.3f;
                l.StartTime = 0.02f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.08f;
                l.Blocks.HoldTime = 0f;
                l.Blocks.Vanish = VfxVanish.ExpandFade;
                l.Blocks.VanishTime = 0.22f;
            });

            // 가시 — 날아가는 방향으로 늘어난 날카로운 조각
            AddLayer(root, "Spikes", VfxShape.Burst, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Burst.Count = 14;
                l.Burst.Facing = VfxBurstFacing.Velocity;
                l.Burst.SizeRange = new Vector2(0.14f, 0.24f);
                l.Burst.Stretch = 2.5f;
                l.Burst.Speed = 7f;
                l.Burst.Drag = 7f;
                l.Burst.LifeRandom = 0.4f;
                l.Wobble = 0.05f;
                l.Brightness = 2.5f;
                l.CoreGlow = 1.2f;
                l.Softness = 0.15f;
                l.Blocks.Appear = VfxAppear.Pop;
                l.Blocks.AppearTime = 0.03f;
                l.Blocks.HoldTime = 0.05f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.2f;
            });

            // 반짝이 — 별 모양이 천천히 흩어지며 줄어듦
            AddLayer(root, "Sparkles", VfxShape.Burst, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.AutoMask = false;
                l.BuiltinMask = VfxBuiltinMask.Star;
                l.StartTime = 0.04f;
                l.Burst.Count = 10;
                l.Burst.Seed = 7;
                l.Burst.SizeRange = new Vector2(0.12f, 0.26f);
                l.Burst.Speed = 3f;
                l.Burst.Drag = 3f;
                l.Burst.RandomRotation = false;
                l.Burst.LifeRandom = 0.5f;
                l.Wobble = 0f;
                l.Brightness = 3f;
                l.CoreGlow = 2f;
                l.Softness = 0.1f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.06f;
                l.Blocks.HoldTime = 0.15f;
                l.Blocks.Vanish = VfxVanish.Shrink;
                l.Blocks.VanishTime = 0.25f;
            });

            AddLight(root, 8f, 6f, 0.25f);
        }

        // 카툰 폭발 — 불덩이 + 떠오르는 불똥 + 떨어지는 돌 파편 + 바닥 고리 + 조명
        static void ExplosionToon(Transform root, VfxEffect fx)
        {
            fx.FrameRate = 15;
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            var rock = AssetDatabase.LoadAssetAtPath<VfxPalette>($"{Root}/Palettes/Palette_Rock.asset");

            // 불덩이 — 불꽃 가장자리로 일렁이며 커졌다가 구멍 나며 사라짐
            AddLayer(root, "Fireball", VfxShape.Card, clip, l =>
            {
                l.Card.Size = new Vector2(1.5f, 1.5f);
                l.FlameEdge = 0.6f;
                l.FlameScale = 5f;
                l.FlameSpeed = 2f;
                l.Wobble = 0.3f;
                l.OutlineWidth = 0.08f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.07f;
                l.Blocks.HoldTime = 0.08f;
                l.Blocks.Vanish = VfxVanish.Hole;
                l.Blocks.VanishTime = 0.3f;
            });

            // 불똥 — 불꽃 모양 조각이 사방으로 튀고 위로 떠오름
            AddLayer(root, "Flames", VfxShape.Burst, clip, l =>
            {
                l.AutoMask = false;
                l.BuiltinMask = VfxBuiltinMask.Flame;
                l.Burst.Count = 12;
                l.Burst.Seed = 3;
                l.Burst.SizeRange = new Vector2(0.3f, 0.55f);
                l.Burst.Speed = 3.5f;
                l.Burst.Drag = 4f;
                l.Burst.Gravity = -2f;
                l.Burst.Stretch = 0f;
                l.Burst.RandomRotation = false;
                l.Burst.LifeRandom = 0.5f;
                l.Wobble = 0.2f;
                l.OutlineWidth = 0.08f;
                l.StartTime = 0.03f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.05f;
                l.Blocks.HoldTime = 0.1f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.3f;
            });

            // 돌 파편 — 위로 튀었다가 중력으로 떨어지며 작아짐
            AddLayer(root, "Debris", VfxShape.Burst, clip, l =>
            {
                l.AutoMask = false;
                l.BuiltinMask = VfxBuiltinMask.Rock;
                l.PaletteOverride = rock;
                l.Burst.Count = 8;
                l.Burst.Seed = 11;
                l.Burst.Shape = VfxBurstShape.Up;
                l.Burst.SizeRange = new Vector2(0.16f, 0.3f);
                l.Burst.Facing = VfxBurstFacing.Billboard;
                l.Burst.Stretch = 0f;
                l.Burst.RandomRotation = true;
                l.Burst.Speed = 6f;
                l.Burst.Drag = 1f;
                l.Burst.Gravity = 14f;
                l.Burst.LifeRandom = 0.3f;
                l.OutlineWidth = 0.14f;
                l.CoreThreshold = 0.75f;
                l.MidThreshold = 0.4f;
                l.Wobble = 0f;
                l.Brightness = 1f;
                l.Blocks.Appear = VfxAppear.Pop;
                l.Blocks.AppearTime = 0.02f;
                l.Blocks.HoldTime = 0.35f;
                l.Blocks.Vanish = VfxVanish.Shrink;
                l.Blocks.VanishTime = 0.15f;
            });

            // 바닥 고리 — 발밑에서 퍼지는 충격파
            AddLayer(root, "GroundRing", VfxShape.Ring, clip, l =>
            {
                l.transform.localPosition = new Vector3(0f, -0.95f, 0f);
                l.Ring.Radius = 1.2f;
                l.Ring.Width = 0.25f;
                l.FlameEdge = 0.4f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.08f;
                l.Blocks.HoldTime = 0f;
                l.Blocks.Vanish = VfxVanish.ExpandFade;
                l.Blocks.VanishTime = 0.25f;
            });

            AddLight(root, 6f, 5f, 0.3f);
        }

        // 빛 오라 — 발광 기둥 + 발밑 고리 + 피어오르는 반짝이
        static void BuffGlow(Transform root, VfxEffect fx)
        {
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");

            // 오라 기둥 — 솟아오르며 등장, 위로 흐르는 불꽃 무늬, 위에서부터 깎여 사라짐
            AddLayer(root, "Aura", VfxShape.Pillar, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Pillar.Radius = 0.55f;
                l.Pillar.TopRadius = 0.35f;
                l.Pillar.Height = 2f;
                l.Brightness = 1.5f;
                l.Softness = 0.3f;
                l.CoreGlow = 0.6f;
                l.Wobble = 0.2f;
                l.FlameEdge = 0.5f;
                l.FlameScale = 6f;
                l.FlameSpeed = 2.5f;
                l.Blocks.Appear = VfxAppear.Rise;
                l.Blocks.AppearTime = 0.25f;
                l.Blocks.Hold = VfxHold.Wobble;
                l.Blocks.HoldTime = 1f;
                l.Blocks.HoldStrength = 0.3f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.35f;
            });

            // 발밑 고리 — 커지며 등장, 맥동하며 유지, 퍼지며 사라짐
            AddLayer(root, "FootRing", VfxShape.Ring, add, l =>
            {
                l.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                l.Style = VfxLayerStyle.Glow;
                l.Ring.Radius = 0.9f;
                l.Ring.Width = 0.12f;
                l.Brightness = 2f;
                l.Softness = 0.25f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.2f;
                l.Blocks.Hold = VfxHold.Pulse;
                l.Blocks.HoldTime = 0.8f;
                l.Blocks.HoldStrength = 0.4f;
                l.Blocks.Vanish = VfxVanish.ExpandFade;
                l.Blocks.VanishTime = 0.3f;
            });

            // 반짝이 — 몸 주위에서 계속 피어올라 줄어들며 사라짐
            AddLayer(root, "Sparkles", VfxShape.Burst, add, l =>
            {
                l.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                l.Style = VfxLayerStyle.Glow;
                l.AutoMask = false;
                l.BuiltinMask = VfxBuiltinMask.Star;
                l.Burst.Continuous = true;
                l.Burst.Count = 10;
                l.Burst.Shape = VfxBurstShape.Flat;
                l.Burst.StartRadius = 0.5f;
                l.Burst.Speed = 0.2f;
                l.Burst.SpeedRandom = 0.5f;
                l.Burst.Drag = 0f;
                l.Burst.Gravity = -2.5f;
                l.Burst.SizeRange = new Vector2(0.08f, 0.16f);
                l.Burst.RandomRotation = false;
                l.Burst.LifeRandom = 0.4f;
                l.Wobble = 0f;
                l.Brightness = 3f;
                l.CoreGlow = 2f;
                l.Softness = 0.1f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.15f;
                l.Blocks.HoldTime = 0.4f;
                l.Blocks.Vanish = VfxVanish.Shrink;
                l.Blocks.VanishTime = 0.3f;
            });

            AddLight(root, 4f, 4f, 0.4f).transform.localPosition = new Vector3(0f, 1f, 0f);
        }

        // 불꽃 오라 — 불꽃 기둥 + 발밑 불꽃 고리 + 피어오르는 불똥
        static void BuffToon(Transform root, VfxEffect fx)
        {
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            fx.FrameRate = 15;
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");

            // 불꽃 기둥 — 솟아오르며 등장, 혀처럼 날름거리며 유지, 위로 흩어지며 사라짐
            AddLayer(root, "FlamePillar", VfxShape.Pillar, clip, l =>
            {
                l.Pillar.Radius = 0.55f;
                l.Pillar.TopRadius = 0.3f;
                l.Pillar.Height = 1.8f;
                l.FlameEdge = 0.7f;
                l.FlameScale = 5f;
                l.FlameSpeed = 3f;
                l.Wobble = 0.25f;
                l.OutlineWidth = 0.08f;
                l.Blocks.Appear = VfxAppear.Rise;
                l.Blocks.AppearTime = 0.2f;
                l.Blocks.HoldTime = 1f;
                l.Blocks.Vanish = VfxVanish.ScatterUp;
                l.Blocks.VanishTime = 0.35f;
            });

            // 발밑 불꽃 고리
            AddLayer(root, "FireRing", VfxShape.Ring, clip, l =>
            {
                l.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                l.Ring.Radius = 0.85f;
                l.Ring.Width = 0.2f;
                l.FlameEdge = 0.5f;
                l.OutlineWidth = 0.08f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.15f;
                l.Blocks.Hold = VfxHold.Pulse;
                l.Blocks.HoldTime = 0.6f;
                l.Blocks.HoldStrength = 0.3f;
                l.Blocks.Vanish = VfxVanish.ExpandFade;
                l.Blocks.VanishTime = 0.3f;
            });

            // 불똥 — 계속 피어올라 깎여 사라짐
            AddLayer(root, "Embers", VfxShape.Burst, clip, l =>
            {
                l.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                l.AutoMask = false;
                l.BuiltinMask = VfxBuiltinMask.Flame;
                l.Burst.Continuous = true;
                l.Burst.Count = 8;
                l.Burst.Shape = VfxBurstShape.Up;
                l.Burst.StartRadius = 0.4f;
                l.Burst.Speed = 0.6f;
                l.Burst.Drag = 1f;
                l.Burst.Gravity = -3f;
                l.Burst.SizeRange = new Vector2(0.18f, 0.32f);
                l.Burst.RandomRotation = false;
                l.Burst.LifeRandom = 0.5f;
                l.OutlineWidth = 0.08f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.1f;
                l.Blocks.HoldTime = 0.2f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.3f;
            });
        }

        // 돌진 속도선 + 잔상 줄
        static void DashLines(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            // ── 1. 속도선 + 잔상 줄 — 플레이어를 따라감, 돌진 시간만큼 반복 후 스스로 끝남 ──
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            fx.AutoStopAfter = 0.4f;
            fx.HideForOwner = false;
            fx.OwnerScale = 1f;

            AddLayer(root, "SpeedLines", VfxShape.Streak, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Streak.Count = 16;
                l.Streak.InnerRadius = 0.5f;
                l.Streak.OuterRadius = 1.3f;
                l.Streak.Front = 2f;
                l.Streak.Back = -2.5f;
                l.Streak.LengthRange = new Vector2(0.8f, 1.6f);
                l.Streak.Width = 0.035f;
                l.Streak.Speed = 14f;
                l.Brightness = 2f;
                l.Softness = 0.15f;
                l.Wobble = 0f;
                l.Blocks.Appear = VfxAppear.Fade;
                l.Blocks.AppearTime = 0.08f;
                l.Blocks.HoldTime = 0.3f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.15f;
            });

            foreach (float side in new[] { -0.35f, 0.35f })
            {
                AddLayer(root, side < 0 ? "Trail_L" : "Trail_R", VfxShape.Trail, add, l =>
                {
                    l.transform.localPosition = new Vector3(side, -0.1f, 0f);
                    l.Style = VfxLayerStyle.Glow;
                    l.Trail.Time = 0.22f;
                    l.Trail.Width = 0.28f;
                    l.Trail.PreviewLength = 2f;
                    l.Brightness = 1.6f;
                    l.Softness = 0.3f;
                    l.CoreGlow = 0.8f;
                    l.Wobble = 0.1f;
                    l.Blocks.Appear = VfxAppear.Fade;
                    l.Blocks.AppearTime = 0.05f;
                    l.Blocks.HoldTime = 0.3f;
                    l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                    l.Blocks.VanishTime = 0.2f;
                });
            }
        }

        // 돌진 출발 먼지
        static void DashDust(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            // ── 2. 출발 먼지 — 출발 지점(발밑)에 남음, 뒤로 퍼지는 먼지구름 + 바닥 고리 ──
            fx.FrameRate = 15;

            AddLayer(root, "Puffs", VfxShape.Burst, clip, l =>
            {
                l.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                l.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);     // 뒤쪽으로
                l.AutoMask = false;
                l.BuiltinMask = VfxBuiltinMask.Smoke;
                l.Burst.Count = 8;
                l.Burst.Shape = VfxBurstShape.Forward;
                l.Burst.ConeAngle = 110f;
                l.Burst.Speed = 3f;
                l.Burst.Drag = 5f;
                l.Burst.Gravity = -0.6f;
                l.Burst.SizeRange = new Vector2(0.35f, 0.6f);
                l.Burst.RandomRotation = true;
                l.Burst.LifeRandom = 0.4f;
                l.Wobble = 0.3f;
                l.OutlineWidth = 0.08f;
                l.Brightness = 1f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.1f;
                l.Blocks.HoldTime = 0.1f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.35f;
            });

            AddLayer(root, "GroundRing", VfxShape.Ring, clip, l =>
            {
                l.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                l.Ring.Radius = 0.6f;
                l.Ring.Width = 0.15f;
                l.OutlineWidth = 0.08f;
                l.Brightness = 1f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.08f;
                l.Blocks.HoldTime = 0f;
                l.Blocks.Vanish = VfxVanish.ExpandFade;
                l.Blocks.VanishTime = 0.25f;
            });
        }

        // 돌진 원뿔 — 몸 앞을 감싸며 바람을 가르는 빛 원뿔, 빛줄기가 앞에서 뒤로 흐름
        static void DashCone(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            fx.AutoStopAfter = 0.45f;
            fx.FrameRate = 30;

            // 바깥 원뿔 — 총알처럼 둥근 앞머리, 굵은 빛줄기가 살짝 소용돌이치며 뒤로
            AddLayer(root, "Cone_Outer", VfxShape.Cone, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Cone.Front = 1.4f;
                l.Cone.Back = -1.6f;
                l.Cone.TipRadius = 0.05f;
                l.Cone.BaseRadius = 1.15f;
                l.Cone.Roundness = 0.65f;
                l.Cone.StreakCount = 16;
                l.Cone.StreakAmount = 0.85f;
                l.Cone.StreakSpeed = 3f;
                l.Cone.Twist = 0.2f;
                l.Brightness = 2.2f;
                l.Softness = 0.3f;
                l.CoreGlow = 1f;
                l.Wobble = 0.05f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.06f;
                l.Blocks.Hold = VfxHold.Still;
                l.Blocks.HoldTime = 0.25f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.15f;
            });

            // 안쪽 원뿔 — 더 뾰족하고 밝게, 반대 방향으로 비틀려 겹쳐 보이게
            AddLayer(root, "Cone_Inner", VfxShape.Cone, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.StartTime = 0.02f;
                l.Cone.Front = 1.55f;
                l.Cone.Back = -0.6f;
                l.Cone.TipRadius = 0.02f;
                l.Cone.BaseRadius = 0.55f;
                l.Cone.Roundness = 0.8f;
                l.Cone.StreakCount = 10;
                l.Cone.StreakAmount = 0.6f;
                l.Cone.StreakSpeed = 4f;
                l.Cone.Twist = -0.3f;
                l.Brightness = 3f;
                l.Softness = 0.25f;
                l.CoreGlow = 1.6f;
                l.Wobble = 0.05f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.05f;
                l.Blocks.Hold = VfxHold.Still;
                l.Blocks.HoldTime = 0.22f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.12f;
            });

            // 원뿔 바깥으로 지나가는 속도선
            AddLayer(root, "SpeedLines", VfxShape.Streak, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Streak.Count = 10;
                l.Streak.InnerRadius = 0.9f;
                l.Streak.OuterRadius = 1.6f;
                l.Streak.Front = 1.2f;
                l.Streak.Back = -2.6f;
                l.Streak.LengthRange = new Vector2(0.7f, 1.4f);
                l.Streak.Width = 0.03f;
                l.Streak.Speed = 16f;
                l.Brightness = 2f;
                l.Softness = 0.15f;
                l.Wobble = 0f;
                l.Blocks.Appear = VfxAppear.Fade;
                l.Blocks.AppearTime = 0.06f;
                l.Blocks.HoldTime = 0.3f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.15f;
            });
        }

        // 돌진 도착 충격
        static void DashImpact(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            // ── 3. 도착 충격 — 앞으로 튀는 가시 + 충격파 + 번쩍 ──
            fx.FrameRate = 24;

            AddLayer(root, "Flash", VfxShape.Card, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Card.Size = new Vector2(1.1f, 1.1f);
                l.Brightness = 2.2f;
                l.CoreGlow = 1.5f;
                l.Softness = 0.25f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.05f;
                l.Blocks.HoldTime = 0.02f;
                l.Blocks.Vanish = VfxVanish.Hole;
                l.Blocks.VanishTime = 0.18f;
            });

            AddLayer(root, "Shockwave", VfxShape.Ring, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Ring.Facing = VfxFacing.Billboard;
                l.Ring.Radius = 0.9f;
                l.Ring.Width = 0.14f;
                l.Brightness = 2f;
                l.Softness = 0.3f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.06f;
                l.Blocks.HoldTime = 0f;
                l.Blocks.Vanish = VfxVanish.ExpandFade;
                l.Blocks.VanishTime = 0.2f;
            });

            AddLayer(root, "Spikes", VfxShape.Burst, add, l =>
            {
                l.Style = VfxLayerStyle.Glow;
                l.Burst.Count = 10;
                l.Burst.Shape = VfxBurstShape.Forward;
                l.Burst.ConeAngle = 100f;
                l.Burst.Facing = VfxBurstFacing.Velocity;
                l.Burst.Stretch = 2.5f;
                l.Burst.SizeRange = new Vector2(0.12f, 0.2f);
                l.Burst.Speed = 7f;
                l.Burst.Drag = 7f;
                l.Burst.LifeRandom = 0.4f;
                l.Wobble = 0.05f;
                l.Brightness = 2.5f;
                l.CoreGlow = 1.2f;
                l.Softness = 0.15f;
                l.Blocks.Appear = VfxAppear.Pop;
                l.Blocks.AppearTime = 0.03f;
                l.Blocks.HoldTime = 0.04f;
                l.Blocks.Vanish = VfxVanish.ErodeFromEnd;
                l.Blocks.VanishTime = 0.18f;
            });

            AddLight(root, 5f, 5f, 0.2f);
        }

        // 화상 — 세 갈래 불꽃이 몸에서 피어오르며 날름거림
        static void Status_Burn(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            StatusSetup(fx);

            Sparkles(root, add, new Vector3(0f, 0.4f, 0f), VfxBuiltinMask.FlameTongue, l =>
            {
                l.Burst.Count = 7;
                l.Burst.Shape = VfxBurstShape.Up;
                l.Burst.StartRadius = 0.3f;
                l.Burst.Speed = 0.4f;
                l.Burst.Drag = 1f;
                l.Burst.Gravity = -2f;
                l.Burst.SizeRange = new Vector2(0.16f, 0.26f);
                l.FlameEdge = 0.35f;
                l.FlameScale = 4f;
                l.Wobble = 0.15f;
            });
            AddLight(root, 1.4f, 2.5f, 0.3f).Mode = VfxLightMode.Sustain;
        }

        // 동상 — 눈송이가 천천히 내려앉으며 빙글 돎
        static void Status_Frost(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            StatusSetup(fx);

            Sparkles(root, add, new Vector3(0f, 1.2f, 0f), VfxBuiltinMask.Snowflake, l =>
            {
                l.Burst.Count = 7;
                l.Burst.Shape = VfxBurstShape.Sphere;
                l.Burst.StartRadius = 0.45f;
                l.Burst.Speed = 0.1f;
                l.Burst.Drag = 0f;
                l.Burst.Gravity = 0.6f;
                l.Burst.SizeRange = new Vector2(0.13f, 0.2f);
                l.Burst.RandomRotation = true;
                l.Blocks.Hold = VfxHold.Spin;
                l.Blocks.HoldStrength = 0.15f;
                l.Softness = 0.08f;
            });
            AddLight(root, 1f, 2.5f, 0.3f).Mode = VfxLightMode.Sustain;
        }

        // 중독 — 해골이 떠올랐다가 가운데부터 뚫리며 사라짐
        static void Status_Poison(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            StatusSetup(fx);

            Sparkles(root, add, new Vector3(0f, 0.6f, 0f), VfxBuiltinMask.Skull, l =>
            {
                l.Burst.Count = 4;
                l.Burst.Shape = VfxBurstShape.Up;
                l.Burst.StartRadius = 0.35f;
                l.Burst.Speed = 0.15f;
                l.Burst.Drag = 0f;
                l.Burst.Gravity = -0.8f;
                l.Burst.SizeRange = new Vector2(0.16f, 0.22f);
                l.Softness = 0.08f;
                l.Blocks.HoldTime = 0.5f;
                l.Blocks.Vanish = VfxVanish.Hole;
                l.Blocks.VanishTime = 0.2f;
            });
            AddLight(root, 0.8f, 2.5f, 0.3f).Mode = VfxLightMode.Sustain;
        }

        // 둔화 — 아래를 가리키는 화살표가 다리 쪽으로 천천히 내려감
        static void Status_Slow(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            StatusSetup(fx);

            Sparkles(root, add, new Vector3(0f, 1.4f, 0f), VfxBuiltinMask.ArrowDown, l =>
            {
                l.Burst.Count = 5;
                l.Burst.Shape = VfxBurstShape.Flat;
                l.Burst.StartRadius = 0.45f;
                l.Burst.Speed = 0.05f;
                l.Burst.Drag = 0f;
                l.Burst.Gravity = 1.6f;
                l.Burst.Facing = VfxBurstFacing.Billboard;     // 화면 기준으로 항상 촉이 아래
                l.Burst.SizeRange = new Vector2(0.16f, 0.22f);
                l.Softness = 0.08f;
            });
            AddLight(root, 0.6f, 2f, 0.3f).Mode = VfxLightMode.Sustain;
        }

        // 기절 — 머리 위에서 반짝이며 도는 오각별
        static void Status_Stun(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            StatusSetup(fx);

            Sparkles(root, add, new Vector3(0f, 2.05f, 0f), VfxBuiltinMask.Star5, l =>
            {
                l.Burst.Count = 5;
                l.Burst.Shape = VfxBurstShape.Flat;
                l.Burst.StartRadius = 0.28f;
                l.Burst.Speed = 0.05f;
                l.Burst.Drag = 0f;
                l.Burst.Gravity = 0f;
                l.Burst.SizeRange = new Vector2(0.12f, 0.18f);
                l.Blocks.Hold = VfxHold.Spin;
                l.Blocks.HoldStrength = 0.3f;
                l.Softness = 0.08f;
            });
            var light = AddLight(root, 1f, 2f, 0.2f);
            light.Mode = VfxLightMode.Sustain;
            light.transform.localPosition = new Vector3(0f, 2f, 0f);
        }

        // 깊은 상처 — 불투명한 핏방울이 몸에서 흘러 떨어짐 (빛 스타일은 빨강이 분홍으로 떠서 카툰 스타일)
        static void Status_DeepWound(Transform root, VfxEffect fx)
        {
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            StatusSetup(fx);

            Sparkles(root, clip, new Vector3(0f, 1.15f, 0f), VfxBuiltinMask.Droplet, l =>
            {
                l.Style = VfxLayerStyle.Toon;
                l.Burst.Count = 6;
                l.Burst.Shape = VfxBurstShape.Sphere;
                l.Burst.StartRadius = 0.28f;
                l.Burst.Speed = 0.1f;
                l.Burst.Drag = 0f;
                l.Burst.Gravity = 4f;
                l.Burst.SizeRange = new Vector2(0.08f, 0.13f);
                l.Brightness = 1f;
                l.OutlineWidth = 0.1f;
                l.CoreThreshold = 0.8f;
                l.MidThreshold = 0.35f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.15f;
                l.Blocks.HoldTime = 0.35f;
                l.Blocks.Vanish = VfxVanish.Shrink;
                l.Blocks.VanishTime = 0.15f;
            });
        }

        // ══════════════════════ 공통 도우미 ══════════════════════

        public static VfxLayer AddLayer(Transform parent, string name, VfxShape shape, VfxShaderProfile profile, Action<VfxLayer> setup)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<VfxLayer>();
            l.Shape = shape;
            l.Profile = profile;
            setup(l);
            return l;
        }
        public static VfxLight AddLight(Transform parent, float intensity, float range, float duration)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<VfxLight>();
            light.Intensity = intensity;
            light.Range = range;
            light.Duration = duration;
            return light;
        }
        /// <summary>상태이상 공통 반짝이 — 빛 스타일, 계속 뿜기, 커지며 등장 → 줄어들며 사라짐</summary>
        public static void Sparkles(Transform root, VfxShaderProfile profile, Vector3 pos, VfxBuiltinMask mask, Action<VfxLayer> tweak)
        {
            AddLayer(root, "Sparkles", VfxShape.Burst, profile, l =>
            {
                l.transform.localPosition = pos;
                l.Style = VfxLayerStyle.Glow;
                l.AutoMask = false;
                l.BuiltinMask = mask;
                l.Burst.Continuous = true;
                l.Burst.RandomRotation = false;
                l.Burst.LifeRandom = 0.4f;
                l.Wobble = 0f;
                l.Brightness = 2.5f;
                l.CoreGlow = 1.5f;
                l.Softness = 0.12f;
                l.Blocks.Appear = VfxAppear.Grow;
                l.Blocks.AppearTime = 0.12f;
                l.Blocks.HoldTime = 0.35f;
                l.Blocks.Vanish = VfxVanish.Shrink;
                l.Blocks.VanishTime = 0.25f;
                tweak(l);
            });
        }
        public static void AddArc(Transform parent, string name, Quaternion rot, Vector3 pos, VfxShaderProfile profile, Action<VfxLayer> setup)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = rot;
            go.transform.localPosition = pos;

            var l = go.AddComponent<VfxLayer>();
            l.Shape = VfxShape.Arc;
            l.Profile = profile;
            setup(l);
        }
    }
}
