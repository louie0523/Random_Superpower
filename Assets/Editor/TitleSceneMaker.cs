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
///   Assets/Scenes/Title.unity — 카메라 · 조명 · 바닥 · 그래피티 벽 · 내 캐릭터 자리(TitleStage) · 벽 칠하기(TitlePaintMode · PaintView) · 타이틀 UI
///   벽 · 바닥 = 칠할 수 있는 면(PaintSurface) — 재질은 빈 면(색만), 기본 그래피티는 TitleArt/wall_paint.png · floor_paint.png (투명 · 지워짐)
///     PNG가 있으면 그대로 씀 · 벽을 골라 인스펙터 '씬에서 칠하기'로 고쳐 저장
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

        // 바닥 · 벽 — 재질은 빈 면(색만). 코드로 그린 기본 그래피티는 그 위 칠 그림(투명 PNG)으로 → 플레이어가 지울 수 있음
        var floorColor = new Color(0.15f, 0.13f, 0.19f);
        var wallTex = DefaultArt("wall", 2048, 1024, new[]
        {
            Decal(Spray, new Color(1f, 0.36f, 0.62f, 0.95f), new Vector2(0.69f, 0.40f), new Vector2(0.42f, 0.21f), -8f),
            Decal(Spray, new Color(0.31f, 0.84f, 0.75f, 0.9f), new Vector2(0.30f, 0.28f), new Vector2(0.36f, 0.18f), 7f),
            Decal(Spray, new Color(1f, 0.84f, 0.2f, 0.85f), new Vector2(0.47f, 0.62f), new Vector2(0.29f, 0.145f), -3f),
            Decal(Splat, new Color(1f, 0.84f, 0.2f, 1f), new Vector2(0.62f, 0.66f), new Vector2(0.122f, 0.244f), 0f),
            Decal(Splat, new Color(0.62f, 0.4f, 1f, 1f), new Vector2(0.34f, 0.57f), new Vector2(0.094f, 0.189f), 25f),
            Decal(Splat, new Color(1f, 0.36f, 0.62f, 1f), new Vector2(0.24f, 0.48f), new Vector2(0.061f, 0.122f), -15f),
        });
        var groundTex = DefaultArt("floor", 1024, 1024, new[]
        {
            Decal(Splat, new Color(0.31f, 0.84f, 0.75f, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(0.165f, 0.165f), 20f),
        });
        var ground = Quad("Ground", new Vector3(0f, 0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(16f, 16f, 1f), Lit("Ground", floorColor));
        Surface(ground, "floor", new Vector2Int(1024, 1024), floorColor, groundTex, Vector2.zero);
        var wall = Quad("Wall", new Vector3(0f, 3.5f, -2.6f), new Vector3(0f, 180f, 0f), new Vector3(18f, 9f, 1f), Lit("Wall", wallColor));
        Surface(wall, "wall", new Vector2Int(2048, 1024), wallColor, wallTex, new Vector2(0f, -1f));

        // 벽 칠하기 — 카메라 자리 (벽을 화면 가득) + 모드
        var view = new GameObject("PaintView");
        view.transform.position = new Vector3(0f, 2.7f, 7.4f);
        view.transform.LookAt(new Vector3(0f, 2.45f, -2.6f));
        var floorView = new GameObject("PaintViewFloor");       // 바닥 칠하기 (Tab · '바닥' 버튼)
        floorView.transform.position = new Vector3(0f, 6.5f, 4.7f);
        floorView.transform.LookAt(new Vector3(0f, 0f, 1.1f));
        var pm = new GameObject("TitlePaintMode").AddComponent<TitlePaintMode>();
        pm.PaintView = view.transform;
        pm.FloorView = floorView.transform;
        pm.Camera = cam;

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

    static void Surface(GameObject go, string id, Vector2Int size, Color baseColor, Texture2D defaultPaint, Vector2 drip)
    {
        var ps = go.AddComponent<PaintSurface>();      // MeshCollider도 같이 붙음 (칠할 곳 찾기)
        ps.Id = id;
        ps.Size = size;
        ps.BaseTexture = null;                          // 바탕 = 빈 면
        ps.BaseColor = baseColor;
        ps.DefaultPaint = defaultPaint;                 // 기본 그래피티 (지워짐)
        ps.DripDirection = drip;
        ps.RebuildPreview();
    }

    // ══════════════════ 재질 · 그림 ══════════════════

    static Material Lit(string name, Color c, Texture tex = null)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = name };
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
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

    // ══════════════════ 기본 그림 굽기 ══════════════════

    struct DecalDef
    {
        public Func<Texture2D> Draw;
        public Color Color;
        public Vector2 Uv, Size;
        public float Rot;
    }

    static DecalDef Decal(Func<Texture2D> draw, Color c, Vector2 uv, Vector2 size, float rot) =>
        new() { Draw = draw, Color = c, Uv = uv, Size = size, Rot = rot };

    static Texture2D Spray() => UiGraffitiArt.Spray(true, 9, 1024);
    static Texture2D Splat() => UiGraffitiArt.Splat();

    /// <summary>
    /// 기본 그래피티 (스프레이 · 물감 · 투명 바탕) → {ArtFolder}/{id}_paint.png.
    /// 이미 있으면 그대로 씀 — 씬에서 칠해 저장한 그림 · 손으로 고친 PNG를 덮지 않게 (새로 받으려면 PNG 지우기)
    /// </summary>
    static Texture2D DefaultArt(string id, int w, int h, DecalDef[] decals)
    {
        string path = $"{ArtFolder}/{id}_paint.png";
        if (!File.Exists(path))
        {
            var px = new Color[w * h];        // 전부 투명

            foreach (var dc in decals)
            {
                var src = dc.Draw();
                Stamp(px, w, h, src, dc);
                Object.DestroyImmediate(src);
            }

            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels(px);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }
        PaintSurfaceEditor.ImportPaint(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>그림 한 장을 돌려서 찍음 (흰 그림 × 색 · 검은 외곽선은 그대로)</summary>
    static void Stamp(Color[] px, int w, int h, Texture2D src, DecalDef d)
    {
        float cx = d.Uv.x * w, cy = d.Uv.y * h, sw = d.Size.x * w, sh = d.Size.y * h;
        float rad = d.Rot * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        float R = 0.5f * Mathf.Sqrt(sw * sw + sh * sh);
        int x0 = Mathf.Max(0, (int)(cx - R)), x1 = Mathf.Min(w - 1, (int)(cx + R));
        int y0 = Mathf.Max(0, (int)(cy - R)), y1 = Mathf.Min(h - 1, (int)(cy + R));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float u = (dx * cos + dy * sin) / sw + 0.5f;
                float v = (-dx * sin + dy * cos) / sh + 0.5f;
                if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                var s = src.GetPixelBilinear(u, v);
                float a = s.a * d.Color.a;
                if (a <= 0.002f) continue;
                int i = y * w + x;
                var dst = px[i];
                float oa = a + dst.a * (1f - a);           // 투명 위에 얹기 (직선 알파)
                var c = new Color(s.r * d.Color.r, s.g * d.Color.g, s.b * d.Color.b);
                px[i] = new Color((c.r * a + dst.r * dst.a * (1f - a)) / oa,
                                  (c.g * a + dst.g * dst.a * (1f - a)) / oa,
                                  (c.b * a + dst.b * dst.a * (1f - a)) / oa, oa);
            }
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
