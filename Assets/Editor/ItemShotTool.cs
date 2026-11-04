using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 · 장비 모델 사진 (인벤토리 칸 그림) — Assets/UI/Game/Resources/ItemShots/{Id}.png 로 저장.
/// 게임은 이 그림을 먼저 쓰고, 없으면 실행 중에 찍음 (ItemThumbnail).
/// 찍는 건 전부 '아이템 사진관'(ItemPhotoStudio)과 같은 무대 · 설정으로 — 사진관에서 맞춘 각도 · 조명 그대로
///   - Tools → UI → 아이템 사진관 (각도 · 조명 맞추기 · 여러 개 한꺼번에)
///   - 장비 에셋 인스펙터 ⋮ → '모델 사진 찍기' (맞춰 둔 설정으로 바로) / '아이템 사진관에서 열기'
///   - Tools → UI → 아이템 모델 사진 전부 찍기
///   - 다른 창에서: ItemShotTool.Capture(데이터, 모델 회전) — 회전을 주면 그 각도로 (조명 · 확대는 설정 그대로)
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
        var set = ItemPhotoStudio.LoadSettings(true);
        var stage = new ItemShotStage();
        var guids = AssetDatabase.FindAssets("t:EquipmentData");
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                var d = AssetDatabase.LoadAssetAtPath<EquipmentData>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (d == null) continue;
                if (EditorUtility.DisplayCancelableProgressBar("아이템 모델 사진", d.name, i / (float)guids.Length)) break;
                if (d.ModelPrefab == null) { skip++; continue; }
                if (ItemPhotoStudio.Shoot(d, set, stage, null, false) != null) n++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            stage.Dispose();
        }
        AssetDatabase.Refresh();
        Debug.Log($"[아이템 사진] {n}개 찍음 · 모델 없음 {skip}개 → {Folder}");
    }

    /// <summary>
    /// 찍어서 저장 — view = 모델 회전 (null이면 사진관에 맞춰 둔 모습). 저장한 스프라이트를 돌려줌
    /// </summary>
    public static Sprite Capture(EquipmentData d, Quaternion? view, bool ping = true)
    {
        var set = ItemPhotoStudio.LoadSettings(true);
        ShotLook look = null;
        if (view.HasValue && d != null)
        {
            look = set.LookFor(d).Clone();
            look.Angle = ShotAngle.Custom;
            look.Spin = 0f;
            look.Euler = view.Value.eulerAngles;
        }
        var sp = ItemPhotoStudio.Shoot(d, set, null, look, ping);
        if (sp != null) Debug.Log($"[아이템 사진] {d.name} → {Folder}/{d.Id}.png", sp);
        return sp;
    }
}
