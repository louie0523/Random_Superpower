#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → UI → 클래스 선택 · 랜덤 능력 프리팹 만들기
/// Assets/UI/ClassPick 에 기본 모양 프리팹 4개 + 모양 스프라이트 3개를 만들고 스크립트 연결까지 해 둠.
///   ClassCard.prefab           카드 한 장 (ClassCardView)
///   ClassPickPanel.prefab      선택 화면 (ClassPickPanel → 카드 프리팹 연결됨) — 씬에 놓기
///   RandomAbilityRow.prefab    HUD 한 줄 (RandomAbilityRowView)
///   RandomAbilityHud.prefab    HUD (RandomAbilityHud → 줄 프리팹 연결됨) — HUD 캔버스 안 소울 아래에 놓기
/// Project 창에서 한글 TMP 글꼴 에셋을 선택한 채로 실행하면 그 글꼴로 만듦.
/// </summary>
public static class ClassPickPrefabMaker
{
    const string Folder = "Assets/UI/ClassPick";

    [MenuItem("Tools/UI/클래스 선택 · 랜덤 능력 프리팹 만들기")]
    static void Make()
    {
        var font = Selection.activeObject as TMP_FontAsset;
        if (font == null &&
            !EditorUtility.DisplayDialog("글꼴",
                "선택한 TMP 글꼴이 없어서 TMP 기본 글꼴로 만듭니다.\n한글 글꼴로 하려면 Project 창에서 글꼴 에셋을 선택하고 다시 실행하세요.",
                "기본 글꼴로 계속", "취소"))
            return;

        if (File.Exists(Folder + "/ClassPickPanel.prefab") &&
            !EditorUtility.DisplayDialog("덮어쓰기", Folder + "에 이미 프리팹이 있습니다. 다시 만들까요? (꾸민 내용이 사라짐)", "다시 만들기", "취소"))
            return;

        EnsureFolder(Folder);

        // 모양 스프라이트 — 이미 있으면 그대로 (직접 바꾼 그림 유지)
        int R = AbilityUi.RoundedRadius;
        AbilityUi.RoundedOverride = SaveSprite("Rounded", AbilityUi.RoundedTexture(), new Vector4(R, R, R, R));
        AbilityUi.CircleOverride = SaveSprite("Circle", AbilityUi.CircleTexture(), Vector4.zero);
        AbilityUi.GradientOverride = SaveSprite("Gradient", AbilityUi.GradientTexture(), Vector4.zero);

        var holder = new GameObject("__ClassPickMaker");
        try
        {
            // 1. 카드
            var card = ClassPickUiBuilder.BuildCard(holder.transform, font);
            card.transform.SetParent(null, false);
            var cardAsset = PrefabUtility.SaveAsPrefabAsset(card.gameObject, Folder + "/ClassCard.prefab").GetComponent<ClassCardView>();
            Object.DestroyImmediate(card.gameObject);

            // 2. 선택 화면
            var panelGo = new GameObject("ClassPickPanel");
            var panel = panelGo.AddComponent<ClassPickPanel>();
            var s = ClassPickUiBuilder.BuildScreen(panelGo, font, 30500, new Color(0f, 0f, 0f, 0.62f));
            for (int i = 0; i < 3; i++)     // 미리보기용 (게임 시작하면 지워지고 Card Prefab으로 다시 만듦)
                PrefabUtility.InstantiatePrefab(cardAsset.gameObject, s.Cards);

            var so = new SerializedObject(panel);
            so.FindProperty("root").objectReferenceValue = s.Root;
            so.FindProperty("title").objectReferenceValue = s.Title;
            so.FindProperty("timer").objectReferenceValue = s.Timer;
            so.FindProperty("cardParent").objectReferenceValue = s.Cards;
            so.FindProperty("cardPrefab").objectReferenceValue = cardAsset;
            so.FindProperty("font").objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(panelGo, Folder + "/ClassPickPanel.prefab");
            Object.DestroyImmediate(panelGo);

            // 3. HUD 한 줄
            var row = ClassPickUiBuilder.BuildRow(holder.transform, font, ClassPickUiBuilder.RowStyle.Default);
            row.transform.SetParent(null, false);
            var rowAsset = PrefabUtility.SaveAsPrefabAsset(row.gameObject, Folder + "/RandomAbilityRow.prefab").GetComponent<RandomAbilityRowView>();
            Object.DestroyImmediate(row.gameObject);

            // 4. HUD
            var hudGo = new GameObject("RandomAbilityHud", typeof(RectTransform));
            var hudRt = (RectTransform)hudGo.transform;
            hudRt.anchorMin = hudRt.anchorMax = hudRt.pivot = new Vector2(0f, 1f);
            hudRt.sizeDelta = new Vector2(540f, 114f);
            var hud = hudGo.AddComponent<RandomAbilityHud>();
            PrefabUtility.InstantiatePrefab(rowAsset.gameObject, hudRt);     // 미리보기용
            var so2 = new SerializedObject(hud);
            so2.FindProperty("rowPrefab").objectReferenceValue = rowAsset;
            so2.FindProperty("rowParent").objectReferenceValue = hudRt;
            so2.FindProperty("font").objectReferenceValue = font;
            so2.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(hudGo, Folder + "/RandomAbilityHud.prefab");
            Object.DestroyImmediate(hudGo);
        }
        finally
        {
            Object.DestroyImmediate(holder);
            AbilityUi.RoundedOverride = AbilityUi.CircleOverride = AbilityUi.GradientOverride = null;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(Folder + "/ClassPickPanel.prefab");
        EditorGUIUtility.PingObject(Selection.activeObject);
        Debug.Log($"[ClassPick] 프리팹 4개를 {Folder}에 만들었습니다 — ClassPickPanel은 씬에, RandomAbilityHud는 HUD 캔버스 안 소울 아래에 놓으세요");
    }

    static Sprite SaveSprite(string name, Texture2D tex, Vector4 border)
    {
        string path = $"{Folder}/{name}.png";
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.spriteBorder = border;
            ti.SaveAndReimport();
        }
        Object.DestroyImmediate(tex);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
