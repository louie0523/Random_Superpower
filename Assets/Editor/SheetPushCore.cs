#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.Google;
using UnityEngine;

/// <summary>
/// 구글 시트에 키 · 한국어 올리기 — 공통 (아이템 → 시트 등에서 씀).
/// - 로그인은 LangTable의 구글 시트 연결(SheetsServiceProvider)을 그대로 씀. 풀려 있으면 다시 로그인
/// - 여러 탭을 같이 읽어서, 키가 이미 어느 탭에 있으면 그 줄의 한국어(C)만 바꾸고, 없으면 지정한 탭 맨 아래에 새 줄
/// - 새 줄의 다른 언어 칸에는 구글 자동 번역 식 (머리줄 언어 코드로)
/// </summary>
public static class SheetPushCore
{
    const string Api = "https://sheets.googleapis.com/v4/spreadsheets/";

    /// <summary>칸에 한국어 대신 시트 키를 적어 둔 경우 (ability.6001.name · #weapon.1001.name 등)</summary>
    public static readonly Regex LooksLikeKey = new(@"^#?[a-z0-9_]+(\.[A-Za-z0-9_{}]+)+$");

    public static string Clean(string s) => (s ?? "").Replace("\r\n", "\n").Trim();

    public static bool IsText(string s)
    {
        s = Clean(s);
        return s.Length > 0 && !LooksLikeKey.IsMatch(s);
    }

    // ══════════════════ 계획 ══════════════════

    public class Entry
    {
        public string Key, Korean;
        public UnityEngine.Object Source;
        /// <summary>키를 에셋에 새로 적어야 할 때 — 그 에셋 · 키 필드 경로 (아니면 null)</summary>
        public UnityEngine.Object KeyAsset;
        public string KeyPath;
    }

    public class Found
    {
        public string Tab;
        public int Row;
        public string Korean;
    }

    public class Plan
    {
        public List<Entry> Entries = new();
        public List<Entry> Added = new();
        public List<(Entry e, Found at)> Changed = new();
        public int Same;
        public List<string> Duplicates = new();
        public int PendingKeys;
        public string Target;
        public int TargetNextRow;
        public List<string> TargetHeader = new();
        public Dictionary<string, int> RowsRead = new();
    }

    /// <summary>탭들을 읽어 무엇을 새로 넣고 무엇을 바꿀지 — 쓰지는 않음</summary>
    public static Plan MakePlan(Ctx ctx, List<Entry> entries, IEnumerable<string> tabs, string target)
    {
        var plan = new Plan { Entries = entries, Target = target, PendingKeys = entries.Count(e => e.KeyPath != null) };
        var where = new Dictionary<string, Found>();

        var all = tabs.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (!all.Contains(target)) all.Add(target);
        foreach (var tab in all)
        {
            List<List<string>> rows;
            try { rows = GetValues(ctx, Q(tab) + "!A:C"); }
            catch (Exception e) when (tab != target) { Debug.LogWarning($"[다국어] '{tab}' 탭을 못 읽음 — 건너뜀 ({Root(e).Message})"); continue; }
            plan.RowsRead[tab] = rows.Count;

            int lastKey = 1;
            for (int r = 1; r < rows.Count; r++)        // 0 = 머리줄
            {
                var row = rows[r];
                string key = row.Count > 1 ? row[1]?.Trim() : null;
                if (string.IsNullOrEmpty(key)) continue;
                lastKey = r + 1;
                if (where.TryGetValue(key, out var prev)) { plan.Duplicates.Add($"{key}  ({prev.Tab} {prev.Row}행 · {tab} {r + 1}행)"); continue; }
                where[key] = new Found { Tab = tab, Row = r + 1, Korean = row.Count > 2 ? row[2] : "" };
            }
            if (tab == target)
            {
                plan.TargetNextRow = lastKey + 1;
                var header = GetValues(ctx, Q(tab) + "!1:1");
                if (header.Count > 0) plan.TargetHeader = header[0];
            }
        }

        var seen = new HashSet<string>();
        foreach (var e in entries)
        {
            if (!seen.Add(e.Key)) continue;
            if (where.TryGetValue(e.Key, out var at))
            {
                if (Clean(at.Korean) == Clean(e.Korean)) plan.Same++;
                else plan.Changed.Add((e, at));
            }
            else plan.Added.Add(e);
        }
        return plan;
    }

    /// <summary>계획대로 쓰기 — category = 새 줄의 A열</summary>
    public static void Write(Ctx ctx, Plan plan, string category, bool overwrite, bool formulas)
    {
        var raw = new List<(string range, List<List<string>> values)>();

        if (overwrite)
            foreach (var (e, at) in plan.Changed)
                raw.Add(($"{Q(at.Tab)}!C{at.Row}", new List<List<string>> { new() { e.Korean } }));

        int start = plan.TargetNextRow;
        int end = start + plan.Added.Count - 1;
        if (plan.Added.Count > 0)
            raw.Add(($"{Q(plan.Target)}!A{start}:C{end}",
                     plan.Added.Select(e => new List<string> { category, e.Key, e.Korean }).ToList()));

        if (raw.Count > 0) BatchUpdate(ctx, raw, "RAW");

        if (formulas && plan.Added.Count > 0 && plan.TargetHeader.Count > 3)
        {
            var langs = plan.TargetHeader.Skip(3).Select(TranslateCode).ToList();
            int last = langs.FindLastIndex(l => l != null);
            if (last >= 0)
            {
                langs = langs.Take(last + 1).ToList();
                var f = new List<List<string>>();
                for (int r = start; r <= end; r++)
                    f.Add(langs.Select(l => l == null ? "" : $"=IF($C{r}=\"\",\"\",GOOGLETRANSLATE($C{r}, \"ko\", \"{l}\"))").ToList());
                BatchUpdate(ctx, new() { ($"{Q(plan.Target)}!D{start}:{Col(3 + langs.Count - 1)}{end}", f) }, "USER_ENTERED");
            }
        }
    }

    /// <summary>새로 만든 키를 에셋에 저장 — 시트에 올린 뒤</summary>
    public static int ApplyKeys(IEnumerable<Entry> entries)
    {
        int n = 0;
        foreach (var g in entries.Where(e => e.KeyAsset != null && e.KeyPath != null).GroupBy(e => e.KeyAsset))
        {
            var so = new SerializedObject(g.Key);
            Undo.RecordObject(g.Key, "번역 키 채우기");
            foreach (var e in g)
            {
                var p = so.FindProperty(e.KeyPath);
                if (p == null || p.propertyType != SerializedPropertyType.String) continue;
                p.stringValue = e.Key;
                n++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(g.Key);
        }
        if (n > 0) AssetDatabase.SaveAssets();
        return n;
    }

    /// <summary>"English(en)" → en, "Chinese (Simplified)(zh-Hans)" → zh-CN, "Spanish (Spain)(es-ES)" → es</summary>
    public static string TranslateCode(string header)
    {
        if (string.IsNullOrEmpty(header)) return null;
        var m = Regex.Match(header, @"\(([A-Za-z\-]+)\)\s*$");
        if (!m.Success) return null;
        string c = m.Groups[1].Value;
        if (c.StartsWith("ko")) return null;
        if (c == "zh-Hans" || c == "zh-CN") return "zh-CN";
        if (c == "zh-Hant" || c == "zh-TW") return "zh-TW";
        int dash = c.IndexOf('-');
        return dash > 0 ? c.Substring(0, dash) : c;
    }

    public static string Col(int index)       // 0 = A
    {
        string s = "";
        for (index++; index > 0; index = (index - 1) / 26) s = (char)('A' + (index - 1) % 26) + s;
        return s;
    }

    public static string Q(string tab) => "'" + tab.Replace("'", "''") + "'";

    // ══════════════════ 구글 시트 API (로그인은 Unity 프로바이더 것) ══════════════════

    public class Ctx
    {
        public string SheetId;
        public SheetsServiceProvider Provider;
        public string Token;
    }

    /// <summary>연결 — 실패하면 null, 이유는 error</summary>
    public static Ctx Connect(out string error)
    {
        error = null;
        var col = LocalizationEditorSettings.GetStringTableCollection(LocUtil.TableName);
        var ext = col != null ? col.Extensions.OfType<GoogleSheetsExtension>().FirstOrDefault() : null;
        if (ext == null || ext.SheetsServiceProvider == null || string.IsNullOrEmpty(ext.SpreadsheetId))
        {
            error = $"'{LocUtil.TableName}'에 구글 시트 연결(Google Sheets 확장 · 프로바이더 · 시트 ID)이 없음";
            return null;
        }

        var ctx = new Ctx { SheetId = ext.SpreadsheetId, Provider = ext.SheetsServiceProvider };
        try { ctx.Token = AccessToken(ctx.Provider); }
        catch (Exception e) when (IsExpired(e))
        {
            Debug.LogWarning("[다국어] 구글 로그인이 풀려 있음 → 다시 로그인");
            EditorUtility.ClearProgressBar();
            SheetsAuthTools.Relogin();
            ctx.Token = AccessToken(ctx.Provider);
        }
        return ctx;
    }

    /// <summary>프로바이더의 OAuth로 접근 토큰 받기 — 구글 DLL 타입은 리플렉션으로만 (어셈블리 참조 필요 없음)</summary>
    static string AccessToken(SheetsServiceProvider p)
    {
        object cred = Authorize(p);
        if (cred == null) throw new Exception("구글 로그인 실패 (프로바이더가 OAuth 방식인지 확인)");

        var m = cred.GetType().GetMethod("GetAccessTokenForRequestAsync", new[] { typeof(string), typeof(CancellationToken) })
                ?? throw new Exception("GetAccessTokenForRequestAsync 없음");
        return Task.Run(() => (Task<string>)m.Invoke(cred, new object[] { null, CancellationToken.None })).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 구글 로그인 → UserCredential(구글 타입이라 object로). 저장된 토큰이 살아 있으면 바로, 아니면 브라우저 로그인.
    /// 다른 스레드에서 돌리고 여기선 진행 창만 (취소 가능) — SheetsAuthTools 버전과 상관없이 쓰게 여기 둠
    /// </summary>
    static object Authorize(SheetsServiceProvider p, int timeoutSeconds = 180)
    {
        var t = typeof(SheetsServiceProvider);
        t.GetProperty("DataStore")?.GetValue(p);      // 토큰 저장소를 메인 스레드에서 먼저 만들어 둠

        var mAsync = t.GetMethod("AuthorizeOAuthAsync", new[] { typeof(CancellationToken) });
        if (mAsync == null)
        {
            var m = t.GetMethod("AuthorizeOAuth", Type.EmptyTypes) ?? throw new Exception("AuthorizeOAuth 없음 — Localization 패키지 버전 확인");
            return m.Invoke(p, null);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var task = Task.Run(async () =>
        {
            var inner = (Task)mAsync.Invoke(p, new object[] { cts.Token });
            await inner.ConfigureAwait(false);
            return inner.GetType().GetProperty("Result")?.GetValue(inner);
        });

        var start = DateTime.Now;
        try
        {
            while (!task.IsCompleted)
            {
                int sec = (int)(DateTime.Now - start).TotalSeconds;
                string msg = sec < 3 ? "저장된 로그인 확인 중…" : $"브라우저에서 구글 로그인 → (확인되지 않은 앱이면 '고급' → 이동) → 허용   ({sec}초)";
                if (EditorUtility.DisplayCancelableProgressBar("구글 로그인", msg, Mathf.Repeat(sec / 30f, 1f)))
                {
                    cts.Cancel();
                    break;
                }
                Thread.Sleep(100);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        try { return task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { throw new Exception("구글 로그인을 취소했거나 시간이 지남 — 다시 눌러서 브라우저 로그인을 끝까지 해 줘"); }
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static string Send(Ctx ctx, HttpMethod method, string url, string json)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ctx.Token);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = Task.Run(() => Http.SendAsync(req)).GetAwaiter().GetResult();
        string body = Task.Run(() => res.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode) throw new Exception($"시트 API {(int)res.StatusCode}: {body}");
        return body;
    }

    public static List<List<string>> GetValues(Ctx ctx, string range)
    {
        string body = Send(ctx, HttpMethod.Get, $"{Api}{ctx.SheetId}/values/{Uri.EscapeDataString(range)}", null);
        var result = new List<List<string>>();
        if (MiniJson.Parse(body) is Dictionary<string, object> o && o.TryGetValue("values", out var v) && v is List<object> rows)
            foreach (var r in rows)
                result.Add(r is List<object> cells ? cells.Select(c => c?.ToString() ?? "").ToList() : new List<string>());
        return result;
    }

    public static void BatchUpdate(Ctx ctx, List<(string range, List<List<string>> values)> data, string mode)
    {
        var sb = new StringBuilder();
        sb.Append("{\"valueInputOption\":\"").Append(mode).Append("\",\"data\":[");
        for (int i = 0; i < data.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"range\":").Append(MiniJson.Str(data[i].range)).Append(",\"values\":[");
            for (int r = 0; r < data[i].values.Count; r++)
            {
                if (r > 0) sb.Append(',');
                sb.Append('[').Append(string.Join(",", data[i].values[r].Select(MiniJson.Str))).Append(']');
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        Send(ctx, HttpMethod.Post, $"{Api}{ctx.SheetId}/values:batchUpdate", sb.ToString());
    }

    static bool IsExpired(Exception e)
    {
        for (; e != null; e = e.InnerException)
            if (e.Message != null && (e.Message.Contains("invalid_grant") || e.Message.Contains("expired or revoked"))) return true;
        return false;
    }

    public static Exception Root(Exception e)
    {
        while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null) e = e.InnerException;
        return e;
    }

    // ══════════════════ 작은 JSON (시트 응답 읽기 · 문자열 쓰기) ══════════════════

    static class MiniJson
    {
        public static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static object Parse(string s)
        {
            int i = 0;
            return Value(s, ref i);
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var o = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = String(s, ref i);
                    Ws(s, ref i); i++;      // :
                    o[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i++] == '}') return o;     // , 또는 }
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i++] == ']') return a;
                }
            }
            if (c == '"') return String(s, ref i);
            int st = i;
            while (i < s.Length && ",}] \n\r\t".IndexOf(s[i]) < 0) i++;
            string lit = s.Substring(st, i - st);
            return lit == "null" ? null : lit;
        }

        static string String(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;    // "
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }
}
#endif
