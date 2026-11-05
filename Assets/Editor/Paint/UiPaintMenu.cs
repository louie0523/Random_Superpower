#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools → UI → 선택한 UI 칠하기 (플레이 중) — Hierarchy에서 RawImage · Image UI를 고르고 누르면 그 위에 바로 칠함.
/// '완료'를 누르면 그 그림 PNG에 저장 (원본 파일을 덮어씀)
/// </summary>
public static class UiPaintMenu
{
    [MenuItem("Tools/UI/선택한 UI 칠하기 (플레이 중)")]
    static void PaintSelected()
    {
        var go = Selection.activeGameObject;
        var c = go.GetComponent<UiPaintCanvas>();
        if (c == null) c = go.AddComponent<UiPaintCanvas>();
        if (c.Painting) c.End(true);
        else c.Begin();
    }

    [MenuItem("Tools/UI/선택한 UI 칠하기 (플레이 중)", true)]
    static bool Can()
    {
        var go = Selection.activeGameObject;
        return Application.isPlaying && go != null && (go.GetComponent<RawImage>() != null || go.GetComponent<Image>() != null);
    }

    [MenuItem("CONTEXT/Image/그림 그리기 창에서 열기")]
    static void OpenImage(MenuCommand cmd) => OpenTex(((Image)cmd.context).sprite != null ? ((Image)cmd.context).sprite.texture : null);

    [MenuItem("CONTEXT/RawImage/그림 그리기 창에서 열기")]
    static void OpenRaw(MenuCommand cmd) => OpenTex(((RawImage)cmd.context).texture as Texture2D);

    static void OpenTex(Texture2D t)
    {
        string p = t != null ? AssetDatabase.GetAssetPath(t) : null;
        if (string.IsNullOrEmpty(p)) { EditorUtility.DisplayDialog("그림 그리기", "이 UI에 파일로 된 그림이 없어", "확인"); return; }
        PaintArtWindow.OpenFile(p);
    }
}
#endif
