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

        public static void EnsureFolder(string path)
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
            Palette("Dust", "먼지", C(0.96f, 0.93f, 0.86f), C(0.82f, 0.77f, 0.7f), C(0.62f, 0.58f, 0.52f), C(0.3f, 0.27f, 0.24f));
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

        // ══════════════════════ 테스트 메뉴 (프리셋을 씬에 만듦) ══════════════════════

        [MenuItem(Menu + "테스트 슬래시 — 빛 (메인 스타일)", priority = 40)]
        static void TestSlashGlow() => Test("SlashGlow");

        [MenuItem(Menu + "테스트 슬래시 — 카툰", priority = 41)]
        static void TestSlashToon() => Test("SlashToon");

        [MenuItem(Menu + "테스트 폭발 — 빛 (메인 스타일)", priority = 50)]
        static void TestExplosionGlow() => Test("ExplosionGlow");

        [MenuItem(Menu + "테스트 폭발 — 카툰", priority = 51)]
        static void TestExplosionToon() => Test("ExplosionToon");

        [MenuItem(Menu + "테스트 버프 — 빛 (메인 스타일)", priority = 60)]
        static void TestBuffGlow() => Test("BuffGlow");

        [MenuItem(Menu + "테스트 버프 — 카툰", priority = 61)]
        static void TestBuffToon() => Test("BuffToon");

        [MenuItem(Menu + "테스트 돌진 세트 (속도선 · 먼지 · 도착 충격)", priority = 80)]
        static void TestDash()
        {
            var a = Test("DashLines");
            var b = Test("DashDust");
            var c = Test("DashImpact");
            b.transform.position += new Vector3(0f, 0f, -1f);      // 더미 뒤 — 출발 자리
            c.transform.position += new Vector3(0f, 0f, 1f);       // 더미 앞 — 도착 자리
            Selection.objects = new UnityEngine.Object[] { a, b, c };
        }

        [MenuItem(Menu + "테스트 돌진 원뿔 (바람 가르기)", priority = 81)]
        static void TestDashCone() => Test("DashCone");

        static GameObject Test(string id)
        {
            var p = VfxPresets.Get(id);
            var fx = VfxPresets.Create(p, p.FileName);
            Undo.RegisterCreatedObjectUndo(fx.gameObject, "Create " + p.Name);
            Selection.activeGameObject = fx.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            return fx.gameObject;
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

        /// <summary>덮어쓸 때는 같은 경로에 저장 → 에셋 GUID가 유지되어 StatusVfx 연결이 끊기지 않음</summary>
        static void BuildStatusEffects(bool overwrite)
        {
            string dir = $"{Root}/Effects/Status";
            EnsureFolder(dir);

            int made = 0;
            foreach (var p in VfxPresets.InCategory("상태이상"))
            {
                string path = $"{dir}/{p.FileName}.prefab";
                if (File.Exists(path) && !overwrite) continue;

                var fx = VfxPresets.Create(p, p.FileName);
                fx.transform.position = Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(fx.gameObject, path);
                UnityEngine.Object.DestroyImmediate(fx.gameObject);
                made++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(overwrite
                ? $"[VFX] 상태이상 이펙트 {made}개 다시 만듦 — {dir}"
                : $"[VFX] 상태이상 이펙트 {made}개 생성 — {dir} (이미 있는 것은 건드리지 않음. 새 모양으로 바꾸려면 '다시 만들기')");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dir));
        }
    }
}
