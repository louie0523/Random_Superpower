#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.Google;
using UnityEngine;

/// <summary>
/// Tools → 다국어
///   시트에서 전부 Pull        — 구글 시트 연결이 있는 테이블을 한 번에 Pull. 로그인이 풀렸으면(invalid_grant) 저절로 다시 로그인하고 한 번 더 시도
///   구글 다시 로그인           — 저장된 토큰을 지우고 브라우저 로그인 (Pull이 invalid_grant로 실패할 때)
/// 근본 해결은 구글 클라우드에서 OAuth 앱을 '프로덕션'으로 바꾸는 것 (문서 참고) — 그러면 일주일마다 안 풀림.
/// </summary>
public static class SheetsAuthTools
{
    const string TokenFolder = "Library/Google";     // SheetsServiceProvider 기본 토큰 저장 위치

    [MenuItem("Tools/다국어/시트에서 전부 Pull")]
    public static void PullAll() => PullAll(true);

    [MenuItem("Tools/다국어/구글 다시 로그인")]
    public static void Relogin()
    {
        var providers = AssetDatabase.FindAssets("t:SheetsServiceProvider")
            .Select(g => AssetDatabase.LoadAssetAtPath<SheetsServiceProvider>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null).ToArray();
        if (providers.Length == 0)
        {
            Debug.LogWarning("[다국어] SheetsServiceProvider 에셋이 없음");
            return;
        }

        // 1. 저장된 (만료된) 토큰 지우기
        if (Directory.Exists(TokenFolder))
        {
            Directory.Delete(TokenFolder, true);
            Debug.Log($"[다국어] 저장된 구글 토큰 지움 ({TokenFolder})");
        }

        // 2. 메모리에 들고 있던 연결도 버리고 새로 로그인 (브라우저가 열림 — 로그인 · 허용 누르면 끝)
        foreach (var p in providers)
        {
            ForgetCachedService(p);
            if (!EditorUtility.DisplayDialog("구글 다시 로그인",
                    $"'{p.name}'로 브라우저 로그인을 엽니다.\n로그인 → (확인되지 않은 앱이면 '고급' → 이동) → 허용", "열기", "건너뛰기"))
                continue;
            try
            {
                // 반환 타입(UserCredential)이 구글 DLL이라 리플렉션으로 부름 — 어셈블리 참조 안 걸어도 됨
                var m = typeof(SheetsServiceProvider).GetMethod("AuthorizeOAuth", Type.EmptyTypes);
                if (m == null) { Debug.LogWarning("[다국어] AuthorizeOAuth를 못 찾음 — 프로바이더 인스펙터의 Authorize 버튼을 눌러 줘"); continue; }
                m.Invoke(p, null);
                Debug.Log($"[다국어] '{p.name}' 로그인 완료");
            }
            catch (Exception e)
            {
                Debug.LogError($"[다국어] '{p.name}' 로그인 실패: {Root(e).Message}");
            }
        }
    }

    static void PullAll(bool retryOnExpired)
    {
        int done = 0, fail = 0;
        foreach (var col in LocalizationEditorSettings.GetStringTableCollections())
        {
            foreach (var ext in col.Extensions.OfType<GoogleSheetsExtension>())
            {
                if (ext.SheetsServiceProvider == null || string.IsNullOrEmpty(ext.SpreadsheetId)) continue;
                try
                {
                    var gs = new GoogleSheets(ext.SheetsServiceProvider) { SpreadSheetId = ext.SpreadsheetId };
                    gs.PullIntoStringTableCollection(ext.SheetId, col, ext.Columns, ext.RemoveMissingPulledKeys, null, true);
                    done++;
                    Debug.Log($"[다국어] Pull 완료: {col.TableCollectionName}");
                }
                catch (Exception e)
                {
                    if (retryOnExpired && IsExpired(e))
                    {
                        Debug.LogWarning("[다국어] 구글 로그인이 풀려 있음 (invalid_grant) → 다시 로그인 후 한 번 더");
                        Relogin();
                        PullAll(false);
                        return;
                    }
                    fail++;
                    Debug.LogError($"[다국어] Pull 실패: {col.TableCollectionName} — {Root(e).Message}");
                }
            }
        }
        if (done + fail == 0) Debug.LogWarning("[다국어] 구글 시트가 연결된 테이블이 없음");
        else AssetDatabase.SaveAssets();
    }

    static bool IsExpired(Exception e)
    {
        for (; e != null; e = e.InnerException)
            if (e.Message != null && (e.Message.Contains("invalid_grant") || e.Message.Contains("expired or revoked"))) return true;
        return false;
    }

    static Exception Root(Exception e)
    {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        return e;
    }

    /// <summary>프로바이더가 메모리에 들고 있는 SheetsService(만료된 토큰)를 버림 — 다음 사용 때 새로 로그인</summary>
    static void ForgetCachedService(SheetsServiceProvider p)
    {
        for (var t = p.GetType(); t != null && t != typeof(ScriptableObject); t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                if (f.FieldType.Name == "SheetsService" || f.FieldType.Name == "UserCredential")
                    f.SetValue(p, null);
    }
}
#endif
