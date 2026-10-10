using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ToonVfx.EditorTools
{
    /// <summary>
    /// Tools → VFX Studio 메뉴.
    ///   0. 초기 설정 — 폴더, 기본 마스크 PNG, 머티리얼, 셰이더 프로필, 팔레트 7종 (여러 번 눌러도 안전)
    ///   미리보기 씬 열기 — 더미 캐릭터 + 바닥 + Bloom 볼륨
    ///   테스트 슬래시 만들기 — 0단계 확인용
    /// </summary>
    public static class VfxSetup
    {
        public const string Root = "Assets/VFX";
        const string Menu = "Tools/VFX Studio/";

        // ══════════════════════ 초기 설정 ══════════════════════

        [MenuItem(Menu + "0. 초기 설정 (폴더·마스크·머티리얼·팔레트)", priority = 0)]
        public static void Setup()
        {
            foreach (var f in new[] { "Presets", "Palettes", "Masks/Builtin", "Masks/Painted", "Masks/AI", "Effects", "Scenes", "Shaders" })
                EnsureFolder($"{Root}/{f}");

            var shader = Shader.Find(VfxDefaults.ShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("VFX Studio",
                    $"셰이더 '{VfxDefaults.ShaderName}'를 찾지 못했습니다.\nAssets/VFX/Shaders/VfxErosion.shader가 있고 컴파일 오류가 없는지 확인하세요.", "확인");
                return;
            }

            SaveBuiltinMasks();

            var clip = GetOrCreateMaterial("VfxErosion_Clip", shader, false);
            var add = GetOrCreateMaterial("VfxErosion_Additive", shader, true);
            GetOrCreateProfile("Profile_Clip", "기본 (깎여 사라짐)", clip);
            GetOrCreateProfile("Profile_Additive", "빛 (더하기)", add);

            CreatePalettes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VFX] 초기 설정 완료 — Assets/VFX");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void SaveBuiltinMasks()
        {
            const int size = VfxMaskLibrary.DefaultSize;

            foreach (VfxBuiltinMask kind in Enum.GetValues(typeof(VfxBuiltinMask)))
            {
                string path = $"{Root}/Masks/Builtin/Mask_{kind}.png";
                if (File.Exists(path)) continue;

                var tex = new Texture2D(size, size, TextureFormat.RGB24, false, true);
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(VfxMaskLibrary.Value(kind, (x + 0.5f) / size, (y + 0.5f) / size)) * 255f);
                        px[y * size + x] = new Color32(b, b, b, 255);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);

                AssetDatabase.ImportAsset(path);
                ApplyMaskImport(path);
            }
        }

        /// <summary>마스크용 임포트 설정 — 데이터 텍스처라 sRGB 끔, 가장자리 반복 안 함</summary>
        public static void ApplyMaskImport(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) return;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        static Material GetOrCreateMaterial(string name, Shader shader, bool additive)
        {
            string path = $"{Root}/Shaders/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(shader);
            if (additive)
            {
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void GetOrCreateProfile(string file, string display, Material mat)
        {
            string path = $"{Root}/Shaders/{file}.asset";
            if (AssetDatabase.LoadAssetAtPath<VfxShaderProfile>(path) != null) return;

            var p = ScriptableObject.CreateInstance<VfxShaderProfile>();
            p.DisplayName = display;
            p.Material = mat;
            AssetDatabase.CreateAsset(p, path);
        }

        static void CreatePalettes()
        {
            Palette("Fire", "불", C(1f, 0.97f, 0.78f), C(1f, 0.78f, 0.15f), C(1f, 0.36f, 0.05f), C(0.55f, 0.05f, 0.02f));
            Palette("Ice", "얼음", C(0.92f, 1f, 1f), C(0.45f, 0.85f, 1f), C(0.15f, 0.45f, 1f), C(0.05f, 0.12f, 0.4f));
            Palette("Poison", "독", C(0.9f, 1f, 0.6f), C(0.55f, 0.95f, 0.2f), C(0.2f, 0.6f, 0.15f), C(0.18f, 0.05f, 0.28f));
            Palette("Lightning", "번개", C(1f, 1f, 1f), C(0.75f, 0.85f, 1f), C(0.45f, 0.4f, 1f), C(0.15f, 0.1f, 0.45f));
            Palette("Dark", "암흑", C(0.85f, 0.6f, 1f), C(0.45f, 0.15f, 0.75f), C(0.18f, 0.05f, 0.35f), C(0.03f, 0f, 0.06f));
            Palette("Holy", "신성", C(1f, 1f, 0.92f), C(1f, 0.93f, 0.55f), C(0.95f, 0.75f, 0.3f), C(0.6f, 0.4f, 0.1f));
            Palette("Blood", "피", C(1f, 0.6f, 0.55f), C(0.85f, 0.1f, 0.12f), C(0.5f, 0.02f, 0.05f), C(0.15f, 0f, 0.02f));
            Palette("Rock", "돌", C(0.6f, 0.57f, 0.54f), C(0.42f, 0.39f, 0.37f), C(0.28f, 0.26f, 0.25f), C(0.07f, 0.06f, 0.06f));
        }

        static Color C(float r, float g, float b) => new(r, g, b, 1f);

        static void Palette(string file, string display, Color core, Color mid, Color edge, Color outline)
        {
            string path = $"{Root}/Palettes/Palette_{file}.asset";
            if (AssetDatabase.LoadAssetAtPath<VfxPalette>(path) != null) return;

            var p = ScriptableObject.CreateInstance<VfxPalette>();
            p.DisplayName = display;
            p.Core = core;
            p.Mid = mid;
            p.Edge = edge;
            p.Outline = outline;
            AssetDatabase.CreateAsset(p, path);
        }

        // ══════════════════════ 미리보기 씬 ══════════════════════

        public const string ScenePath = Root + "/Scenes/VfxStudio.unity";

        [MenuItem(Menu + "미리보기 씬 열기", priority = 20)]
        public static void OpenPreviewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            EnsureFolder($"{Root}/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildPreviewScene(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[VFX] 미리보기 씬 생성 — {ScenePath}");
        }

        static void BuildPreviewScene(Scene scene)
        {
            // 카메라 — 어두운 배경, 후처리 켬 (Bloom)
            var camGo = new GameObject("Preview Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 1.7f, -3.4f);
            camGo.transform.LookAt(new Vector3(0f, 1.1f, 0.6f));
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.13f, 0.15f);
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // 조명
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1f;

            var lit = Shader.Find("Universal Render Pipeline/Lit");

            // 바닥
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            if (lit != null) ground.GetComponent<MeshRenderer>().sharedMaterial = SaveLitMaterial("Preview_Ground", lit, new Color(0.3f, 0.3f, 0.32f));

            // 더미 캐릭터 + 앞쪽 표시
            var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = "Dummy";
            dummy.transform.position = new Vector3(0f, 1f, 0f);
            Material dummyMat = lit != null ? SaveLitMaterial("Preview_Dummy", lit, new Color(0.55f, 0.6f, 0.7f)) : null;
            if (dummyMat != null) dummy.GetComponent<MeshRenderer>().sharedMaterial = dummyMat;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Forward";
            nose.transform.SetParent(dummy.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.5f, 0.5f);
            nose.transform.localScale = new Vector3(0.2f, 0.1f, 0.2f);
            if (dummyMat != null) nose.GetComponent<MeshRenderer>().sharedMaterial = dummyMat;

            // Bloom 볼륨 — 밝기(HDR) 1 넘는 색이 빛남
            var volGo = new GameObject("Bloom Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;

            string profilePath = $"{Root}/Scenes/VfxStudio_Volume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1.1f);
                bloom.intensity.Override(0.7f);
                bloom.scatter.Override(0.6f);
                bloom.name = "Bloom";
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            vol.sharedProfile = profile;
        }

        static Material SaveLitMaterial(string name, Shader lit, Color color)
        {
            string path = $"{Root}/Scenes/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(lit);
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ══════════════════════ 테스트 슬래시 ══════════════════════

        // ── 빛 스타일 (메인) — 하얀 심지 + 진한 색 번짐 + 찢긴 가장자리, 더하기 블렌딩 ──
        [MenuItem(Menu + "테스트 슬래시 — 빛 (메인 스타일)", priority = 40)]
        public static void CreateTestSlashGlow()
        {
            var root = NewEffect("VFX_TestSlash_Glow", "Palette_Dark", out _);
            var profile = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var tilt = Quaternion.Euler(0f, 0f, -20f);

            // 본체 — 넓고 부드러운 발광 띠, 바깥이 불꽃처럼 찢김
            AddArc(root.transform, "Glow_Slash", tilt, Vector3.zero, profile, l =>
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
            AddArc(root.transform, "Glow_Core", tilt, Vector3.zero, profile, l =>
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
            AddArc(root.transform, "Glow_Wisp", tilt, new Vector3(0f, 0.05f, 0f), profile, l =>
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

            Finish(root);
        }

        // ── 카툰 스타일 — 3단 단색 + 외곽선 + 불꽃 혀 ──
        [MenuItem(Menu + "테스트 슬래시 — 카툰", priority = 41)]
        public static void CreateTestSlash()
        {
            var root = NewEffect("VFX_TestSlash_Toon", "Palette_Fire", out _);
            var profile = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            var tilt = Quaternion.Euler(0f, 0f, -20f);       // 대각선 베기

            // 본체 — 왼쪽에서 오른쪽으로 쓸고 지나감 → 잠깐 유지 → 꼬리가 따라오며 끝으로 빨려 들어감
            AddArc(root.transform, "Slash", tilt, Vector3.zero, profile, l =>
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
            AddArc(root.transform, "Slash_Inner", tilt, new Vector3(0f, -0.03f, 0f), profile, l =>
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
            AddArc(root.transform, "Slash_Line", tilt, new Vector3(0f, 0.04f, 0f), profile, l =>
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

            Finish(root);
        }

        // ══════════════════════ 테스트 폭발 ══════════════════════

        // ── 빛 스타일 (메인) — 하얀 섬광 코어 + 충격파 + 방사형 가시 + 반짝이 + 조명 ──
        [MenuItem(Menu + "테스트 폭발 — 빛 (메인 스타일)", priority = 50)]
        public static void CreateTestExplosionGlow()
        {
            var root = NewEffect("VFX_TestExplosion_Glow", "Palette_Dark", out var fx);
            root.transform.position = new Vector3(0f, 1f, 1.5f);
            fx.FrameRate = 24;
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");

            // 코어 — 커지며 번쩍 → 가운데 구멍 나며 사라짐
            AddLayer(root.transform, "Core", VfxShape.Card, add, l =>
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
            AddLayer(root.transform, "Shockwave", VfxShape.Ring, add, l =>
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
            AddLayer(root.transform, "Spikes", VfxShape.Burst, add, l =>
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
            AddLayer(root.transform, "Sparkles", VfxShape.Burst, add, l =>
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

            AddLight(root.transform, 8f, 6f, 0.25f);
            Finish(root, 0.08f);
        }

        // ── 카툰 스타일 — 불덩이 + 떠오르는 불똥 + 떨어지는 돌 파편 + 바닥 고리 + 조명 ──
        [MenuItem(Menu + "테스트 폭발 — 카툰", priority = 51)]
        public static void CreateTestExplosionToon()
        {
            var root = NewEffect("VFX_TestExplosion_Toon", "Palette_Fire", out var fx);
            root.transform.position = new Vector3(0f, 1f, 1.5f);
            fx.FrameRate = 15;
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");
            var rock = AssetDatabase.LoadAssetAtPath<VfxPalette>($"{Root}/Palettes/Palette_Rock.asset");

            // 불덩이 — 불꽃 가장자리로 일렁이며 커졌다가 구멍 나며 사라짐
            AddLayer(root.transform, "Fireball", VfxShape.Card, clip, l =>
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
            AddLayer(root.transform, "Flames", VfxShape.Burst, clip, l =>
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
            AddLayer(root.transform, "Debris", VfxShape.Burst, clip, l =>
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
            AddLayer(root.transform, "GroundRing", VfxShape.Ring, clip, l =>
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

            AddLight(root.transform, 6f, 5f, 0.3f);
            Finish(root, 0.1f);
        }

        // ══════════════════════ 테스트 버프 ══════════════════════
        // '시작 → 반복 → 끝' — 인스펙터에서 ▶ 재생 후 '끝내기'로 사라지는 모습 확인

        // ── 빛 스타일 (메인) — 발광 오라 기둥 + 발밑 고리 + 피어오르는 반짝이 ──
        [MenuItem(Menu + "테스트 버프 — 빛 (메인 스타일)", priority = 60)]
        public static void CreateTestBuffGlow()
        {
            var root = NewEffect("VFX_TestBuff_Glow", "Palette_Holy", out var fx);
            root.transform.position = Vector3.zero;          // 발밑 기준
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");

            // 오라 기둥 — 솟아오르며 등장, 위로 흐르는 불꽃 무늬, 위에서부터 깎여 사라짐
            AddLayer(root.transform, "Aura", VfxShape.Pillar, add, l =>
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
            AddLayer(root.transform, "FootRing", VfxShape.Ring, add, l =>
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
            AddLayer(root.transform, "Sparkles", VfxShape.Burst, add, l =>
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

            AddLight(root.transform, 4f, 4f, 0.4f).transform.localPosition = new Vector3(0f, 1f, 0f);
            Finish(root, 0.6f);
        }

        // ── 카툰 스타일 — 불꽃 기둥 + 발밑 불꽃 고리 + 피어오르는 불똥 ──
        [MenuItem(Menu + "테스트 버프 — 카툰", priority = 61)]
        public static void CreateTestBuffToon()
        {
            var root = NewEffect("VFX_TestBuff_Toon", "Palette_Fire", out var fx);
            root.transform.position = Vector3.zero;
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            fx.FrameRate = 15;
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");

            // 불꽃 기둥 — 솟아오르며 등장, 혀처럼 날름거리며 유지, 위로 흩어지며 사라짐
            AddLayer(root.transform, "FlamePillar", VfxShape.Pillar, clip, l =>
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
            AddLayer(root.transform, "FireRing", VfxShape.Ring, clip, l =>
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
            AddLayer(root.transform, "Embers", VfxShape.Burst, clip, l =>
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

            Finish(root, 0.6f);
        }

        // ══════════════════════ 상태이상 이펙트 ══════════════════════
        // 반짝이(계속 뿜기) + 은은한 지속 조명. '시작 → 반복 → 끝' — 걸리면 켜지고 풀리면 사라짐.
        // 다른 사람 화면에서만 재생 (StatusVfx가 본인 캐릭터는 건너뜀). 기준점 = 발밑

        [MenuItem(Menu + "상태이상 이펙트 6종 만들기 (프리팹)", priority = 70)]
        public static void CreateStatusEffects() => BuildStatusEffects(false);

        [MenuItem(Menu + "상태이상 이펙트 6종 다시 만들기 (덮어쓰기)", priority = 71)]
        public static void RebuildStatusEffects()
        {
            if (!EditorUtility.DisplayDialog("VFX Studio",
                    "Effects/Status의 상태이상 프리팹 6개를 기본값으로 덮어씁니다.\n직접 고친 내용은 사라집니다. (StatusVfx의 연결은 유지됨)",
                    "덮어쓰기", "취소")) return;
            BuildStatusEffects(true);
        }

        static void BuildStatusEffects(bool overwrite)
        {
            string dir = $"{Root}/Effects/Status";
            EnsureFolder(dir);
            var add = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Additive.asset");
            var clip = AssetDatabase.LoadAssetAtPath<VfxShaderProfile>($"{Root}/Shaders/Profile_Clip.asset");

            int made = 0;

            // 화상 — 세 갈래 불꽃이 몸에서 피어오르며 날름거림
            made += SaveStatus(dir, "Status_Burn", "Palette_Fire", overwrite, root =>
            {
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
            });

            // 동상 — 눈송이가 천천히 내려앉으며 빙글 돎
            made += SaveStatus(dir, "Status_Frost", "Palette_Ice", overwrite, root =>
            {
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
            });

            // 중독 — 해골이 떠올랐다가 가운데부터 뚫리며 사라짐
            made += SaveStatus(dir, "Status_Poison", "Palette_Poison", overwrite, root =>
            {
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
            });

            // 둔화 — 아래를 가리키는 화살표가 다리 쪽으로 천천히 내려감
            made += SaveStatus(dir, "Status_Slow", "Palette_Lightning", overwrite, root =>
            {
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
            });

            // 기절 — 머리 위에서 반짝이며 도는 오각별
            made += SaveStatus(dir, "Status_Stun", "Palette_Holy", overwrite, root =>
            {
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
            });

            // 깊은 상처 — 불투명한 핏방울이 몸에서 흘러 떨어짐 (빛 스타일은 빨강이 분홍으로 떠서 카툰 스타일)
            made += SaveStatus(dir, "Status_DeepWound", "Palette_Blood", overwrite, root =>
            {
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
            });

            AssetDatabase.SaveAssets();
            Debug.Log(overwrite
                ? $"[VFX] 상태이상 이펙트 {made}개 다시 만듦 — {dir}"
                : $"[VFX] 상태이상 이펙트 {made}개 생성 — {dir} (이미 있는 것은 건드리지 않음. 새 모양으로 바꾸려면 '다시 만들기')");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dir));
        }

        /// <summary>
        /// 상태이상 이펙트 하나를 임시로 만들어 프리팹으로 저장. 이미 있으면 건너뜀 (직접 고친 것 보호).
        /// 덮어쓸 때는 같은 경로에 저장 → 에셋 GUID가 유지되어 StatusVfx 연결이 끊기지 않음
        /// </summary>
        static int SaveStatus(string dir, string name, string palette, bool overwrite, Action<Transform> build)
        {
            string path = $"{dir}/{name}.prefab";
            if (File.Exists(path) && !overwrite) return 0;

            var root = new GameObject(name);
            var fx = root.AddComponent<VfxEffect>();
            fx.PlayMode = VfxPlayMode.StartLoopEnd;
            fx.FrameRate = 24;
            fx.HideForOwner = true;         // 안전장치 — StatusVfx가 본인은 이미 건너뜀
            fx.Palette = AssetDatabase.LoadAssetAtPath<VfxPalette>($"{Root}/Palettes/{palette}.asset");

            build(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return 1;
        }

        /// <summary>상태이상 공통 반짝이 — 빛 스타일, 계속 뿜기, 커지며 등장 → 줄어들며 사라짐</summary>
        static void Sparkles(Transform root, VfxShaderProfile profile, Vector3 pos, VfxBuiltinMask mask, Action<VfxLayer> tweak)
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

        static VfxLayer AddLayer(Transform parent, string name, VfxShape shape, VfxShaderProfile profile, Action<VfxLayer> setup)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<VfxLayer>();
            l.Shape = shape;
            l.Profile = profile;
            setup(l);
            return l;
        }

        static VfxLight AddLight(Transform parent, float intensity, float range, float duration)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<VfxLight>();
            light.Intensity = intensity;
            light.Range = range;
            light.Duration = duration;
            return light;
        }

        static GameObject NewEffect(string name, string palette, out VfxEffect fx)
        {
            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Create Test Slash");
            root.transform.position = new Vector3(0f, 1.2f, 0f);

            fx = root.AddComponent<VfxEffect>();
            fx.FrameRate = 30;      // 12fps면 0.12초 쓸기가 1~2프레임에 끝나 '툭' 생긴 것처럼 보임
            fx.Palette = AssetDatabase.LoadAssetAtPath<VfxPalette>($"{Root}/Palettes/{palette}.asset");
            return root;
        }

        static void Finish(GameObject root, float previewTime = 0.15f)
        {
            var fx = root.GetComponent<VfxEffect>();
            fx.Collect();
            fx.SetTime(previewTime);
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        static void AddArc(Transform parent, string name, Quaternion rot, Vector3 pos, VfxShaderProfile profile, Action<VfxLayer> setup)
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
