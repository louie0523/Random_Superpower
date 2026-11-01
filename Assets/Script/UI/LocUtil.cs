using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>화면에 번역 글자를 그리는 UI가 구현 — 언어가 바뀌면 불림 (글자를 다시 만들 것)</summary>
public interface ILocRefresh
{
    void OnLocaleChanged();
}

public static class LocUtil
{
    public const string TableName = "LangTable";

    // ══════════════════ 언어 바뀜 알림 ══════════════════

    /// <summary>언어가 바뀔 때마다 +1 — UI가 '바뀌었나' 비교(서명 · 해시)에 섞어 쓰면 저절로 다시 그림</summary>
    public static int Version { get; private set; }

    /// <summary>언어가 바뀐 뒤 (새 언어 표가 준비된 뒤) 한 번</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocale;
        LocalizationSettings.SelectedLocaleChanged += OnLocale;
    }

    static void OnLocale(Locale _)
    {
        // 새 언어 표를 먼저 불러온 뒤 알림 — 안 그러면 UI가 예전 언어 글자를 다시 읽음
        var op = LocalizationSettings.StringDatabase.GetTableAsync(TableName);
        if (op.IsDone) Raise();
        else op.Completed += _ => Raise();
    }

    static void Raise()
    {
        Version++;
        try { Changed?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }

        // 씬의 ILocRefresh 전부 (꺼진 것 포함)
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (mb is ILocRefresh r)
            {
                try { r.OnLocaleChanged(); }
                catch (Exception e) { Debug.LogException(e, mb); }
            }
    }

    static StringTable Table
    {
        get
        {
            var db = LocalizationSettings.StringDatabase;
            return db != null ? db.GetTable(TableName) : null;
        }
    }

    /// <summary>테이블에 엔트리가 실제로 있을 때만 true.
    /// Unity 는 키가 없어도 "No translation found..." 문자열을 돌려주므로
    /// 값이 아니라 엔트리 존재로 판단해야 함.</summary>
    public static bool TryGet(string key, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(key)) return false;

        var entry = Table?.GetEntry(key);
        if (entry == null) return false;

        value = entry.GetLocalizedString();
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>없으면 키를 그대로 반환 — 누락이 화면에서 바로 보이게</summary>
    public static string Get(string key)
        => TryGet(key, out var v) ? v : key;

    /// <summary>{0} 자리표시자에 값을 채워서 반환</summary>
    public static string Get(string key, params object[] args)
    {
        string s = Get(key);
        if (args == null || args.Length == 0) return s;

        try { return string.Format(s, args); }
        catch (System.FormatException e)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[LocUtil] 서식 불일치 — key={key}, args={args.Length}개: {e.Message}");
#endif
            return s;
        }
    }

    public static string GetOr(string key, string fallback)
        => TryGet(key, out var v) ? v : fallback;

    /// <summary>키처럼 생긴 문자열만 번역 시도. 평문이면 그대로 반환.
    /// 에셋 필드에 키와 원문이 섞여 있는 과도기용.</summary>
    public static string Resolve(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        bool looksLikeKey = s.IndexOf(' ') < 0 && s.IndexOf('.') > 0;
        return looksLikeKey ? GetOr(s, s) : s;
    }

    /// <summary>
    /// 숨은 번역 키 + 인스펙터 한국어 짝 — 키가 시트에 있으면 번역, 없으면 한국어(또는 키처럼 생긴 글은 Resolve).
    /// 짝 규칙: 글 필드 X 옆에 [HideInInspector] string XKey — '능력 → 시트'가 키를 채우고 시트에 올림
    /// </summary>
    public static string Pick(string key, string text)
        => TryGet(key, out var v) ? v : Resolve(text);
}
