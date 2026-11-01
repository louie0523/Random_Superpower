using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// 설정 창 · ESC 메뉴 · 키 이름 번역. LangTable에서 찾고, 키가 없으면(또는 아직 로딩 중이면) 코드에 적힌 한국어 그대로.
/// → 시트에 키를 안 넣어도 안 깨짐. 넣는 순간 그 언어로 바뀜.
/// 키 규칙: set.{설정키} · set.{설정키}.note · set.{설정키}.opt{번호} · set.{설정키}.btn · set.tab.{탭} · set.ui.* · set.xh.* · set.esc.* · key.name.{KeyCode}
/// </summary>
public static class SetLoc
{
    public const string Table = "LangTable";

    static string _code;
    static StringTable _table;
    static int _ver;
    static readonly Dictionary<string, string> _cache = new();

    /// <summary>언어가 바뀌거나 표가 로딩되면 +1 — 화면 글자를 다시 쓸 때 비교용</summary>
    public static int Version { get { Check(); return _ver; } }

    static void Check()
    {
        string code;
        try
        {
            if (!LocalizationSettings.InitializationOperation.IsDone) return;
            var loc = LocalizationSettings.SelectedLocale;
            code = loc != null ? loc.Identifier.Code : null;
        }
        catch { return; }

        if (code != _code)
        {
            _code = code;
            _table = null;
            _cache.Clear();
            _ver++;
        }

        if (_table == null && code != null)
        {
            try
            {
                var op = LocalizationSettings.StringDatabase.GetTableAsync(Table);
                if (op.IsDone && op.Result != null)
                {
                    _table = op.Result;
                    _cache.Clear();
                    _ver++;
                }
            }
            catch { }
        }
    }

    /// <summary>번역 — 없으면 fallback (코드의 한국어)</summary>
    public static string T(string key, string fallback)
    {
        Check();
        if (string.IsNullOrEmpty(key) || _table == null) return fallback;     // 로딩 중엔 저장 안 함
        if (_cache.TryGetValue(key, out var s)) return s;

        string v = null;
        try
        {
            var e = _table.GetEntry(key);
            if (e != null) v = e.Value;      // 원문 그대로 ({0}은 F에서 채움)
        }
        catch { }
        if (string.IsNullOrEmpty(v)) v = fallback;
        _cache[key] = v;
        return v;
    }

    /// <summary>번역 + {0} 채우기</summary>
    public static string F(string key, string fallback, params object[] args)
    {
        string fmt = T(key, fallback);
        try { return string.Format(fmt, args); }
        catch { return string.Format(fallback, args); }
    }

    public static string On => T("set.ui.on", "켜짐");
    public static string Off => T("set.ui.off", "꺼짐");
}
