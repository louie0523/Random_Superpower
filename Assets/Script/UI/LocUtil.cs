using UnityEngine.Localization.Settings;

public static class LocUtil
{
    public const string DefaultTable = "LangTable";

    public static string Get(string key) => Get(DefaultTable, key);

    public static string Get(string table, string key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        var op = LocalizationSettings.StringDatabase
            .GetLocalizedStringAsync(table, key);

        if (!op.IsDone) op.WaitForCompletion();

        string s = op.Result;
        return string.IsNullOrEmpty(s) ? $"#{key}" : s;
    }

    public static string Get(string key, params object[] args)
    {
        string s = Get(key);
        if (args == null || args.Length == 0) return s;
        try { return string.Format(s, args); }
        catch { return s; }
    }

    /// <summary>키가 없으면 폴백 문자열</summary>
    public static string GetOr(string key, string fallback)
        => string.IsNullOrEmpty(key) ? fallback : Get(key);
}