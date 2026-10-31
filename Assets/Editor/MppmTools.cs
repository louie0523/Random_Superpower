#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Multiplayer Play Mode 도우미 (Editor 폴더에).
/// - 새로고침 + 저장: 플레이어 창 켜기 전에 — 메인 에디터가 못 끝낸 가져오기를 끝냄
/// - VP 초기화: 'Asset Database is set to Read Only…' 에러가 나면 — Library/VP 지우기 (플레이어 창 다 끄고)
/// </summary>
public static class MppmTools
{
    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    static string VpFolder => Path.Combine(ProjectRoot, "Library", "VP");

    // 가상 플레이어 창 안에서 실행되는 건지 (그 창의 프로젝트는 Library/VP 아래에 있음)
    static bool InVirtualPlayer =>
        ProjectRoot.Replace('\\', '/').Contains("/Library/VP/");

    [MenuItem("Tools/멀티 테스트/새로고침 + 저장 (플레이어 창 켜기 전)")]
    static void RefreshAndSave()
    {
        if (InVirtualPlayer) { Debug.LogWarning("[MPPM] 메인 에디터에서 눌러야 해"); return; }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("[MPPM] 새로고침 + 저장 끝 — 이제 플레이어 창 켜도 됨");
    }

    [MenuItem("Tools/멀티 테스트/VP 초기화 (Library/VP 지우기)")]
    static void ResetVp()
    {
        if (InVirtualPlayer) { Debug.LogWarning("[MPPM] 메인 에디터에서 눌러야 해"); return; }
        if (EditorApplication.isPlaying) { Debug.LogWarning("[MPPM] 플레이 끄고 해"); return; }

        if (!Directory.Exists(VpFolder))
        {
            Debug.Log("[MPPM] Library/VP 없음 — 지울 게 없어");
            return;
        }

        if (!EditorUtility.DisplayDialog("VP 초기화",
                "Multiplayer Play Mode 창에서 플레이어를 전부 끈 다음 눌러.\n\nLibrary/VP를 지우면 다음에 플레이어 창을 켤 때 처음처럼 다시 준비해(조금 오래 걸림).",
                "지우기", "취소"))
            return;

        AssetDatabase.SaveAssets();

        try
        {
            Directory.Delete(VpFolder, true);
            Debug.Log("[MPPM] Library/VP 지움 — 새로고침 후 플레이어 창 다시 켜기");
        }
        catch (IOException e)
        {
            Debug.LogError($"[MPPM] 못 지움 — 플레이어 창이 아직 켜져 있을 수 있어. 다 끄고 다시.\n{e.Message}");
        }
        catch (System.UnauthorizedAccessException e)
        {
            Debug.LogError($"[MPPM] 못 지움 — 파일이 잡혀 있음. 플레이어 창을 다 끄고 다시.\n{e.Message}");
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }
}
#endif
