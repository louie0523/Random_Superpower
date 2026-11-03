using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 · 장비 모델 사진 (인벤토리 칸 그림) — Assets/UI/Game/Resources/ItemShots/{Id}.png 로 저장.
/// 게임은 이 그림을 먼저 쓰고, 없으면 실행 중에 찍음 (ItemThumbnail).
///   - 장비 에셋 인스펙터 ⋮ → '모델 사진 찍기'
///   - Tools → UI → 아이템 모델 사진 전부 찍기
///   - 모션 셋업 창 등에서: ItemShotTool.Capture(데이터, 모델 회전) — 지금 보는 각도로
/// </summary>
public static class ItemShotTool
{
    public const string Folder = "Assets/UI/Game/Resources/ItemShots";
    public const int Size = 512;

    [MenuItem("CONTEXT/EquipmentData/모델 사진 찍기 (인벤토리 그림)")]
    static void CaptureContext(MenuCommand cmd)
    {
        var d = cmd.context as EquipmentData;
        if (d != null) Capture(d, null, true);
    }

    [MenuItem("Tools/UI/아이템 모델 사진 전부 찍기")]
    static void CaptureAll()
    {
        int n = 0, skip = 0;
        var guids = AssetDatabase.FindAssets("t:EquipmentData");
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                var d = AssetDatabase.LoadAssetAtPath<EquipmentData>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (d == null) continue;
                if (EditorUtility.DisplayCancelableProgressBar("아이템 모델 사진", d.name, i / (float)guids.Length)) break;
                if (d.ModelPrefab == null) { skip++; continue; }
                if (Capture(d, null, false) != null) n++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        AssetDatabase.Refresh();
        Debug.Log($"[아이템 사진] {n}개 찍음 · 모델 없음 {skip}개 → {Folder}");
    }

    /// <summary>
    /// 찍어서 저장 — view = 모델 회전 (null이면 가장 긴 쪽이 ↗ 대각선). 저장한 스프라이트를 돌려줌
    /// </summary>
    public static Sprite Capture(EquipmentData d, Quaternion? view, bool ping = true)
    {
        if (d == null || d.ModelPrefab == null)
        {
            Debug.LogWarning($"[아이템 사진] {(d != null ? d.name : "?")} — 모델(ModelPrefab)이 없음");
            return null;
        }

        var tex = ItemThumbnail.RenderTexture2D(d.ModelPrefab, Size, view);
        if (tex == null) return null;

        Directory.CreateDirectory(Folder);
        string path = $"{Folder}/{d.Id}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.SaveAndReimport();

        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (ping && sp != null) EditorGUIUtility.PingObject(sp);
        Debug.Log($"[아이템 사진] {d.name} → {path}", sp);
        return sp;
    }
}
