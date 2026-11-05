#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// Tools → UI → 타이틀 씬 만들기
///   Assets/Scenes/Title.unity — 카메라 · 조명 · 바닥 · 그래피티 벽(스프레이 · 물감 · 망점) · 내 캐릭터 자리(TitleStage) · 타이틀 UI
///   Assets/UI/Game/TitleScreen.prefab — 타이틀 화면 (프리팹에서 마음대로 꾸며도 됨, 다시 만들 때 물어봄)
///   Build Settings 맨 앞에 넣음 (이미 있으면 물어보고 옮김)
/// 캐릭터 모델은 Resources/GameMeta의 '캐릭터 모델' — 비어 있으면 빈 무대
/// </summary>
public static class TitleSceneMaker
{
    const string ScenePath = "Assets/Scenes/Title.unity";
    const string PrefabPath = "Assets/UI/Game/TitleScreen.prefab";
    const string ArtFolder = "Assets/Scenes/TitleArt";

    [MenuItem("Tools/UI/타이틀 씬 만들기")]
    static void Make()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("타이틀 씬", "Title.unity가 이미 있어.\n새로 만들면 씬에 손댄 내용이 사라짐 (UI 프리팹은 따로 물어봄)", "새로 만들기", "취소"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var prefab = EnsurePrefab(false);
        if (prefab == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildStage();

        var ui = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        ui.name = "TitleUI";
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuild();
        AssetDatabase.SaveAssets();
        Debug.Log($"[타이틀] {ScenePath} 만듦 — GameMeta의 '캐릭터 모델'을 넣으면 가운데에 나옴");
        if (GameMeta.Get() == null)
            EditorUtility.DisplayDialog("타이틀", "Resources/GameMeta가 아직 없어.\nProject 창 우클릭 → Create → Game/Meta/Game Meta → Resources 폴더에 'GameMeta'로 만들고\n캐릭터 모델 · 목록들을 넣어 줘.", "확인");
    }

    [MenuItem("Tools/UI/타이틀 화면 프리팹만 다시 만들기")]
    static void RemakePrefab()
    {
        if (EnsurePrefab(true) != null) Debug.Log($"[타이틀] {PrefabPath} 다시 만듦");
    }

    /// <summary>타이틀 UI 프리팹 — 이미 있으면 물어봄 (force = 묻지 않고 다시)</summary>
    static GameObject EnsurePrefab(bool force)
    {
        var old = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (old != null && !force)
        {
            int c = EditorUtility.DisplayDialogComplex("타이틀 화면 프리팹", "TitleScreen.prefab이 이미 있어", "그대로 쓰기", "취소", "코드 모양으로 다시 (꾸민 것 사라짐)");
            if (c == 0) return old;
            if (c == 1) return null;
        }

        var set = UiPrefabSet.Get();
        var font = set != null ? set.Font : null;
        if (font == null &&
            !EditorUtility.DisplayDialog("글꼴", "UiPrefabSet의 글꼴이 비어 있어 TMP 기본 글꼴로 만듦 (한글 깨짐).\n먼저 'UI 프리팹 만들기 창'에서 글꼴을 정하는 걸 추천. 계속할까?", "계속", "취소"))
            return null;

        GameObject saved = null;
        GameUiPrefabMaker.WithSavedArt(() =>
        {
            var go = new GameObject("TitleScreen");
            try
            {
                var view = TitleUiBuilder.Build(go, font);
                var ctrl = go.AddComponent<TitleController>();
                ctrl.View = view;
                EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
                saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally { Object.DestroyImmediate(go); }
        });
        return saved;
    }

    // ══════════════════ 무대 ══════════════════

    static void BuildStage()
    {
        EnsureFolder(ArtFolder);
        var wallColor = new Color(0.20f, 0.17f, 0.26f);

        // 카메라 — 가운데 캐릭터 (키 1.8 기준 머리 위까지)
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        cam.fieldOfView = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.106f, 0.090f, 0.141f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 60f;
        camGo.transform.position = new Vector3(0f, 1.2f, 4.3f);
        camGo.transform.LookAt(new Vector3(0f, 0.98f, 0f));
        var cd = cam.GetUniversalAdditionalCameraData();
        if (cd != null) cd.renderPostProcessing = false;

        // 조명 — 따뜻한 정면 + 민트 뒤쪽 테두리 + 평평한 주변광 (카툰)
        AddLight("Key", new Vector3(32f, 155f, 0f), new Color(1f, 0.95f, 0.88f), 1.25f, LightShadows.Soft);
        AddLight("Rim", new Vector3(18f, 10f, 0f), new Color(0.45f, 1f, 0.88f), 1.4f, LightShadows.None);
        AddLight("Pink", new Vector3(10f, -120f, 0f), new Color(1f, 0.45f, 0.7f), 0.5f, LightShadows.None);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.36f, 0.34f, 0.44f);
        RenderSettings.fog = false;

        // 바닥 · 벽
        Quad("Ground", new Vector3(0f, 0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(16f, 16f, 1f),
             Lit("Ground", new Color(0.15f, 0.13f, 0.19f)));
        Quad("Wall", new Vector3(0f, 3.5f, -2.6f), new Vector3(0f, 180f, 0f), new Vector3(18f, 9f, 1f), Lit("Wall", wallColor));

        // 그래피티 — 벽 위 스프레이 · 물감 · 망점, 발밑 물감
        var spray = Tex("Spray_v8", () => UiGraffitiArt.Spray(true, 9, 1024), false);     // 4:1 — 늘려서 번지지 않게 붙이는 칸도 4:1
        var splat = Tex("Splat", () => UiGraffitiArt.Splat(), false);
        var dots = Tex("Dots", UiGraffitiArt.Dots, true);
        float z = -2.58f;
        Quad("Dots", new Vector3(0f, 3.5f, z + 0.005f), new Vector3(0f, 180f, 0f), new Vector3(18f, 9f, 1f),
             Unlit("Dots", dots, new Color(1f, 1f, 1f, 0.04f), new Vector2(14f, 7f)));
        Decal("SprayPink", spray, new Color(1f, 0.36f, 0.62f, 0.95f), new Vector3(-3.4f, 2.6f, z), new Vector2(7.6f, 1.9f), -8f, 0.01f);
        Decal("SprayMint", spray, new Color(0.31f, 0.84f, 0.75f, 0.9f), new Vector3(3.6f, 1.5f, z), new Vector2(6.4f, 1.6f), 7f, 0.015f);
        Decal("SprayYellow", spray, new Color(1f, 0.84f, 0.2f, 0.85f), new Vector3(0.6f, 4.6f, z), new Vector2(5.2f, 1.3f), -3f, 0.012f);
        Decal("SplatGold", splat, new Color(1f, 0.84f, 0.2f, 1f), new Vector3(-2.2f, 4.9f, z), new Vector2(2.2f, 2.2f), 0f, 0.02f);
        Decal("SplatPurple", splat, new Color(0.62f, 0.4f, 1f, 1f), new Vector3(2.8f, 4.1f, z), new Vector2(1.7f, 1.7f), 25f, 0.025f);
        Decal("SplatPink", splat, new Color(1f, 0.36f, 0.62f, 1f), new Vector3(4.6f, 3.3f, z), new Vector2(1.1f, 1.1f), -15f, 0.03f);
        var floor = Quad("FloorSplat", new Vector3(0f, 0.006f, 0f), new Vector3(90f, 0f, 0f), new Vector3(2.6f, 2.6f, 1f),
                         Unlit("FloorSplat", splat, new Color(0.31f, 0.84f, 0.75f, 0.9f), Vector2.one));
        floor.transform.localRotation = Quaternion.Euler(90f, 20f, 0f);

        // 내 캐릭터 자리
        var stage = new GameObject("TitleStage");
        stage.transform.position = Vector3.zero;
        var ts = stage.AddComponent<TitleStage>();
        ts.Spot = stage.transform;
    }

    static void AddLight(string name, Vector3 euler, Color c, float intensity, LightShadows shadows)
    {
        var go = new GameObject(name);
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = c;
        l.intensity = intensity;
        l.shadows = shadows;
    }

    static GameObject Quad(string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    /// <summary>벽에 붙은 그림 — 겹쳐도 깜빡이지 않게 벽 앞으로 조금씩 (front)</summary>
    static void Decal(string name, Texture2D tex, Color c, Vector3 pos, Vector2 size, float rot, float front)
    {
        var go = Quad(name, pos + new Vector3(0f, 0f, front), new Vector3(0f, 180f, rot), new Vector3(size.x, size.y, 1f),
                      Unlit(name, tex, c, Vector2.one));
        go.GetComponent<MeshRenderer>().receiveShadows = false;
    }

    // ══════════════════ 재질 · 그림 ══════════════════

    static Material Lit(string name, Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = name };
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
        return Save(m);
    }

    static Material Unlit(string name, Texture2D tex, Color c, Vector2 tiling)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_BaseMap"))
        {
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling);
        }
        m.mainTexture = tex;
        m.mainTextureScale = tiling;
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        // 투명 (URP Unlit — 인스펙터의 Surface Type: Transparent와 같게)
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        return Save(m);
    }

    static Material Save(Material m)
    {
        string path = $"{ArtFolder}/{m.name}.mat";
        var old = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (old != null) { old.shader = m.shader; old.CopyPropertiesFromMaterial(m); old.shaderKeywords = m.shaderKeywords; old.renderQueue = m.renderQueue; EditorUtility.SetDirty(old); Object.DestroyImmediate(m); return old; }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>코드 그림 → PNG (있으면 그대로 — 손으로 다시 그린 PNG로 바꿔도 됨)</summary>
    static Texture2D Tex(string name, Func<Texture2D> draw, bool repeat)
    {
        string path = $"{ArtFolder}/{name}.png";
        if (!File.Exists(path))
        {
            var t = draw();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (ti.textureType != TextureImporterType.Default || ti.wrapMode != wrap || !ti.alphaIsTransparency)
            {
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.wrapMode = wrap;
                ti.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ══════════════════ 빌드 순서 · 폴더 ══════════════════

    static void AddToBuild()
    {
        var list = EditorBuildSettings.scenes.ToList();
        int i = list.FindIndex(s => s.path == ScenePath);
        if (i < 0) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        else if (i > 0 && EditorUtility.DisplayDialog("빌드 순서", "타이틀을 맨 앞(게임을 켜면 처음 나오는 씬)으로 옮길까?", "옮기기", "그대로"))
        {
            var e = list[i];
            list.RemoveAt(i);
            list.Insert(0, e);
        }
        EditorBuildSettings.scenes = list.ToArray();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
