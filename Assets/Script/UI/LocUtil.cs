using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public static class LocUtil
{
    public const string TableName = "LangTable";

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
}