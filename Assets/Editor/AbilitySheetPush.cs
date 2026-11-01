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
/// 능력(AbilityData)에 한글로 적어 둔 이름 · 설명을 구글 시트(Ability 탭)에 키 맞춰 올림.
///   Tools → 다국어 → 능력 → 시트 올리기   (AbilityData 인스펙터 '기본' 탭 버튼도 같음)
/// - 키: ability.{Id}.name · flavor · summary · charge · passive.{i}.name/desc/info/tip · skill.{i}.name/desc/info/tip
/// - Behaviour(능력 로직 에셋) 안의 '글 + 숨은 키' 짝도 → ability.{Id}.x.{경로}
///     짝 규칙: string 필드 X 옆에 [HideInInspector] string XKey (예: DescLine.Text/TextKey, 죽음의 기사 Name/NameKey · Label/LabelKey)
///     키가 비어 있으면 여기서 만들어 에셋에 저장 (올리기 성공 후). 한 번 정해진 키는 배열 순서를 바꿔도 그대로
/// - 시트에 없는 키 → 맨 아래 새 줄 (A 분류 · B 키 · C 한국어 + D~ 자동 번역 식)
/// - 이미 있는 키 → 한국어(C)만 바꿈 (켜 뒀을 때). 직접 써 둔 다른 언어 칸은 안 건드림
/// - 로그인은 LangTable의 구글 시트 연결(SheetsServiceProvider)을 그대로 씀. 풀려 있으면 다시 로그인
/// </summary>
public class AbilitySheetPush : EditorWindow
{
    const string Pref = "AbilitySheetPush.";
    const string Api = "https://sheets.googleapis.com/v4/spreadsheets/";

    // ══════════════════ 모으기 ══════════════════

    public struct Entry
    {
        public string Key, Korean;
        public AbilityData Source;
        /// <summary>키를 에셋에 새로 적어야 할 때 — 그 에셋 · 키 필드 경로 (아니면 null)</summary>
        public UnityEngine.Object KeyAsset;
        public string KeyPath;
    }

    static readonly Regex LooksLikeKey = new(@"^[a-z0-9_]+(\.[A-Za-z0-9_{}]+)+$");

    public static List<Entry> Collect(IEnumerable<AbilityData> abilities)
    {
        var list = new List<Entry>();
        var doneBehaviours = new HashSet<UnityEngine.Object>();
        foreach (var a in abilities)
        {
            if (a == null || a.Id == 0) continue;
            string p = $"ability.{a.Id}";
            void Add(string key, string ko)
            {
                if (string.IsNullOrWhiteSpace(ko)) return;
                ko = ko.Replace("\r\n", "\n").Trim();
                if (LooksLikeKey.IsMatch(ko)) return;      // 칸에 시트 키를 적어 둔 경우 — 한국어가 아님
                list.Add(new Entry { Key = key, Korean = ko, Source = a });
            }

            Add($"{p}.name", a.DisplayName);
            Add($"{p}.flavor", a.Flavor);
            Add($"{p}.summary", a.Summary);
            if (a.Charges != null && a.Charges.Length > 0) Add($"{p}.charge", a.Charges[0].DisplayName);

            if (a.Passives != null)
                for (int i = 0; i < a.Passives.Length; i++)
                {
                    var e = a.Passives[i];
                    if (e == null) continue;
                    Add($"{p}.passive.{i}.name", e.Name);
                    Add($"{p}.passive.{i}.desc", e.Description);
                    Add($"{p}.passive.{i}.info", e.DetailedInfo);
                    Add($"{p}.passive.{i}.tip", e.Tip);
                }

            if (a.Skills != null)
                for (int i = 0; i < a.Skills.Length; i++)
                {
                    var e = a.Skills[i];
                    if (e == null) continue;
                    Add($"{p}.skill.{i}.name", e.Name);
                    Add($"{p}.skill.{i}.desc", e.Description);
                    Add($"{p}.skill.{i}.info", e.DetailedInfo);
                    Add($"{p}.skill.{i}.tip", e.Tip);
                }

            if (a.Behaviour != null && doneBehaviours.Add(a.Behaviour))
                CollectPairs(a.Behaviour, $"{p}.x", a, list);
        }
        return list;
    }

    /// <summary>에셋 안의 모든 'X + XKey' 짝 (배열 · 중첩 클래스까지)</summary>
    static void CollectPairs(UnityEngine.Object asset, string prefix, AbilityData src, List<Entry> list)
    {
        var so = new SerializedObject(asset);
        var found = new List<(string path, string keyPath, string text, string key)>();

        var it = so.GetIterator();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = it.propertyType == SerializedPropertyType.Generic;      // 클래스 · 배열만 들어감 (글자 · 에셋 참조는 X)
            if (it.propertyType != SerializedPropertyType.String || it.name.EndsWith("Key")) continue;

            string path = it.propertyPath;
            int dot = path.LastIndexOf('.');
            string keyPath = (dot >= 0 ? path.Substring(0, dot + 1) : "") + it.name + "Key";
            var kp = so.FindProperty(keyPath);
            if (kp == null || kp.propertyType != SerializedPropertyType.String) continue;

            string text = (it.stringValue ?? "").Replace("\r\n", "\n").Trim();
            if (text.Length == 0 || LooksLikeKey.IsMatch(text)) continue;
            found.Add((path, keyPath, text, kp.stringValue));
        }

        // 이미 있는 키 먼저 자리 잡기 → 새 키가 겹치지 않게. 복제로 같은 키 · 다른 글이면 뒤의 것은 새 키
        var used = new Dictionary<string, string>();
        var final = new List<(string keyPath, string text, string key, bool assign)>();
        foreach (var f in found)
        {
            if (string.IsNullOrEmpty(f.key)) continue;
            if (used.TryGetValue(f.key, out var t) && t != f.text) continue;
            used[f.key] = f.text;
        }
        foreach (var f in found)
        {
            bool keep = !string.IsNullOrEmpty(f.key) && used.TryGetValue(f.key, out var t) && t == f.text;
            string key = f.key;
            if (!keep)
            {
                string baseKey = $"{prefix}.{f.path.Replace(".Array.data[", ".").Replace("]", "")}";
                key = baseKey;
                for (int n = 2; used.ContainsKey(key); n++) key = $"{baseKey}_{n}";
                used[key] = f.text;
            }
            final.Add((f.keyPath, f.text, key, !keep || f.key != key));
        }

        foreach (var f in final)
            list.Add(new Entry
            {
                Key = f.key, Korean = f.text, Source = src,
                KeyAsset = f.assign ? asset : null, KeyPath = f.assign ? f.keyPath : null,
            });
    }

    /// <summary>새로 만든 키를 에셋에 저장 — 시트에 올린 뒤</summary>
    static int ApplyKeys(IEnumerable<Entry> entries)
    {
        int n = 0;
        foreach (var g in entries.Where(e => e.KeyAsset != null && e.KeyPath != null).GroupBy(e => e.KeyAsset))
        {
            var so = new SerializedObject(g.Key);
            Undo.RecordObject(g.Key, "번역 키 채우기");
            foreach (var e in g)
            {
                var p = so.FindProperty(e.KeyPath);
                if (p == null) continue;
                p.stringValue = e.Key;
                n++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(g.Key);
        }
        if (n > 0) AssetDatabase.SaveAssets();
        return n;
    }

    static IEnumerable<AbilityData> AllAbilities() =>
        AssetDatabase.FindAssets("t:AbilityData")
            .Select(g => AssetDatabase.LoadAssetAtPath<AbilityData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null)
            .OrderBy(a => a.Id);

    // ══════════════════ 창 ══════════════════

    bool _main = true, _sub = true, _random = true;
    bool _overwrite = true, _formulas = true;
    string _tab = "Ability", _category = "Ability";
    AbilityData _only;
    Plan _plan;
    Vector2 _scroll;
    string _status;

    [MenuItem("Tools/다국어/능력 → 시트 올리기")]
    public static void Open() => Open(null);

    public static void Open(AbilityData only)
    {
        var w = GetWindow<AbilitySheetPush>("능력 → 시트");
        w.minSize = new Vector2(480f, 420f);
        w._only = only;
        w._plan = null;
        w._status = null;
    }

    /// <summary>AbilityDataEditor '기본' 탭에서 부름</summary>
    public static void DrawInspectorButtons(AbilityData data)
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField("다국어 (구글 시트)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("이 능력 → 시트", "이 능력의 이름 · 설명을 시트 Ability 탭에 올림 (먼저 미리보기)"), GUILayout.Height(22)))
                Open(data);
            if (GUILayout.Button(new GUIContent("모든 능력 → 시트", "메인 · 서브 · 랜덤 전부"), GUILayout.Height(22)))
                Open(null);
            if (GUILayout.Button(new GUIContent("시트 → Pull", "시트 내용을 LangTable로 가져오기"), GUILayout.Height(22)))
                SheetsAuthTools.PullAll();
        }
    }

    void OnEnable()
    {
        _main = EditorPrefs.GetBool(Pref + "main", true);
        _sub = EditorPrefs.GetBool(Pref + "sub", true);
        _random = EditorPrefs.GetBool(Pref + "random", true);
        _overwrite = EditorPrefs.GetBool(Pref + "overwrite", true);
        _formulas = EditorPrefs.GetBool(Pref + "formulas", true);
        _tab = EditorPrefs.GetString(Pref + "tab", "Ability");
        _category = EditorPrefs.GetString(Pref + "category", "Ability");
    }

    void SavePrefs()
    {
        EditorPrefs.SetBool(Pref + "main", _main);
        EditorPrefs.SetBool(Pref + "sub", _sub);
        EditorPrefs.SetBool(Pref + "random", _random);
        EditorPrefs.SetBool(Pref + "overwrite", _overwrite);
        EditorPrefs.SetBool(Pref + "formulas", _formulas);
        EditorPrefs.SetString(Pref + "tab", _tab);
        EditorPrefs.SetString(Pref + "category", _category);
    }

    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        _only = (AbilityData)EditorGUILayout.ObjectField(new GUIContent("이 능력만", "비우면 아래 종류 전부"), _only, typeof(AbilityData), false);
        using (new EditorGUI.DisabledScope(_only != null))
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("종류", GUILayout.Width(146));
            _main = GUILayout.Toggle(_main, "메인", "Button");
            _sub = GUILayout.Toggle(_sub, "서브", "Button");
            _random = GUILayout.Toggle(_random, "랜덤", "Button");
        }

        _tab = EditorGUILayout.TextField(new GUIContent("시트 탭", "올릴 탭 이름 (LangTable 탭은 식으로 합쳐 보여 주는 탭이라 X)"), _tab);
        _category = EditorGUILayout.TextField(new GUIContent("A열 분류", "새 줄의 A열 값"), _category);
        _overwrite = EditorGUILayout.ToggleLeft("이미 있는 키도 한국어(C열)를 에디터 값으로 바꾸기", _overwrite);
        _formulas = EditorGUILayout.ToggleLeft("새 줄의 다른 언어 칸에 구글 자동 번역 식 넣기", _formulas);

        if (EditorGUI.EndChangeCheck()) { SavePrefs(); _plan = null; }

        GUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("1. 미리보기 (시트 읽기)", GUILayout.Height(28))) Run(false);
            using (new EditorGUI.DisabledScope(_plan == null || (_plan.Added.Count == 0 && _plan.Changed.Count == 0 && _plan.PendingKeys == 0)))
                if (GUILayout.Button("2. 시트에 올리기", GUILayout.Height(28))) Run(true);
        }

        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);

        if (_plan == null) return;
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawList($"새로 추가 {_plan.Added.Count}", _plan.Added.Select(e => $"{e.Key}   {Short(e.Korean)}"));
        DrawList($"한국어 바뀜 {_plan.Changed.Count}" + (_overwrite ? "" : "  (끔 — 안 바꿈)"),
                 _plan.Changed.Select(c => $"{c.e.Key}   {Short(c.old)}  →  {Short(c.e.Korean)}"));
        GUILayout.Label($"같음 {_plan.Same}개", EditorStyles.miniLabel);
        if (_plan.PendingKeys > 0)
            GUILayout.Label($"에셋에 새로 적을 번역 키 {_plan.PendingKeys}개 (Behaviour 글 — 올리면 저장)", EditorStyles.miniLabel);
        if (_plan.Duplicates.Count > 0)
            DrawList($"시트에 같은 키가 여러 줄 {_plan.Duplicates.Count} (첫 줄만 바꿈)", _plan.Duplicates);
        EditorGUILayout.EndScrollView();
    }

    static void DrawList(string title, IEnumerable<string> lines)
    {
        GUILayout.Label(title, EditorStyles.boldLabel);
        int n = 0;
        foreach (var l in lines)
        {
            if (++n > 300) { GUILayout.Label("…", EditorStyles.miniLabel); break; }
            GUILayout.Label(l, EditorStyles.miniLabel);
        }
    }

    static string Short(string s)
    {
        s = (s ?? "").Replace("\n", " ⏎ ");
        return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
    }

    // ══════════════════ 계획 · 실행 ══════════════════

    class Plan
    {
        public List<Entry> Entries = new();
        public int PendingKeys;
        public List<Entry> Added = new();
        public List<(Entry e, int row, string old)> Changed = new();
        public int Same;
        public List<string> Duplicates = new();
        public int LastRow;
        public List<string> Header = new();
    }

    void Run(bool write)
    {
        try
        {
            var ctx = Connect();
            if (ctx == null) return;

            var abilities = _only != null ? new[] { _only } : AllAbilities().Where(a =>
                (a.Kind == AbilityKind.Main && _main) || (a.Kind == AbilityKind.Sub && _sub) || (a.Kind == AbilityKind.Random && _random));
            var entries = Collect(abilities);

            EditorUtility.DisplayProgressBar("능력 → 시트", "시트 읽는 중…", 0.3f);
            _plan = MakePlan(ctx, entries);
            _status = $"시트 '{_tab}' {_plan.LastRow}줄 읽음 — 새로 {_plan.Added.Count} · 바뀜 {_plan.Changed.Count} · 같음 {_plan.Same}";

            if (!write) return;

            if (!EditorUtility.DisplayDialog("시트에 올리기",
                    $"'{_tab}' 탭에\n새 줄 {_plan.Added.Count}개" + (_overwrite ? $"\n한국어 바꿈 {_plan.Changed.Count}개" : "") +
                    (_plan.PendingKeys > 0 ? $"\n에셋에 번역 키 {_plan.PendingKeys}개 저장" : "") + "\n\n올릴까요?", "올리기", "취소"))
                return;

            EditorUtility.DisplayProgressBar("능력 → 시트", "쓰는 중…", 0.6f);
            Write(ctx, _plan);
            int keys = ApplyKeys(_plan.Entries);
            _status = $"올림 — 새로 {_plan.Added.Count} · 바뀜 {(_overwrite ? _plan.Changed.Count : 0)} · 에셋 키 {keys}.  Unity로 가져오려면 'Tools → 다국어 → 시트에서 전부 Pull'";
            Debug.Log("[다국어] " + _status);
            _plan = null;
        }
        catch (Exception e)
        {
            _status = "실패: " + Root(e).Message;
            Debug.LogError("[다국어] 능력 → 시트 실패: " + Root(e));
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    Plan MakePlan(Ctx ctx, List<Entry> entries)
    {
        var plan = new Plan { Entries = entries, PendingKeys = entries.Count(e => e.KeyPath != null) };
        var header = GetValues(ctx, Q(_tab) + "!1:1");
        if (header.Count > 0) plan.Header = header[0];

        var rows = GetValues(ctx, Q(_tab) + "!A:C");
        plan.LastRow = rows.Count;

        var keyRow = new Dictionary<string, (int row, string ko)>();
        for (int r = 1; r < rows.Count; r++)        // 0 = 머리줄
        {
            var row = rows[r];
            string key = row.Count > 1 ? row[1]?.Trim() : null;
            if (string.IsNullOrEmpty(key)) continue;
            if (keyRow.ContainsKey(key)) { plan.Duplicates.Add($"{key} ({r + 1}행)"); continue; }
            keyRow[key] = (r + 1, row.Count > 2 ? row[2] : "");
        }

        var seen = new HashSet<string>();
        foreach (var e in entries)
        {
            if (!seen.Add(e.Key)) continue;
            if (keyRow.TryGetValue(e.Key, out var ex))
            {
                if (Norm(ex.ko) == Norm(e.Korean)) plan.Same++;
                else plan.Changed.Add((e, ex.row, ex.ko));
            }
            else plan.Added.Add(e);
        }
        return plan;
    }

    static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Trim();

    void Write(Ctx ctx, Plan plan)
    {
        var raw = new List<(string range, List<List<string>> values)>();

        if (_overwrite)
            foreach (var (e, row, _) in plan.Changed)
                raw.Add(($"{Q(_tab)}!C{row}", new List<List<string>> { new() { e.Korean } }));

        int start = plan.LastRow + 1;
        if (plan.Added.Count > 0)
        {
            int end = start + plan.Added.Count - 1;
            raw.Add(($"{Q(_tab)}!A{start}:C{end}",
                     plan.Added.Select(e => new List<string> { _category, e.Key, e.Korean }).ToList()));
        }

        if (raw.Count > 0) BatchUpdate(ctx, raw, "RAW");

        // 새 줄 — 다른 언어 칸에 자동 번역 식 (머리줄의 '(en)' '(ja-JP)' 같은 코드로)
        if (_formulas && plan.Added.Count > 0 && plan.Header.Count > 3)
        {
            var langs = plan.Header.Skip(3).Select(TranslateCode).ToList();
            int last = langs.FindLastIndex(l => l != null);
            if (last >= 0)
            {
                langs = langs.Take(last + 1).ToList();
                int end = start + plan.Added.Count - 1;
                var f = new List<List<string>>();
                for (int r = start; r <= end; r++)
                    f.Add(langs.Select(l => l == null ? "" : $"=IF($C{r}=\"\",\"\",GOOGLETRANSLATE($C{r}, \"ko\", \"{l}\"))").ToList());
                BatchUpdate(ctx, new() { ($"{Q(_tab)}!D{start}:{Col(3 + langs.Count - 1)}{end}", f) }, "USER_ENTERED");
            }
        }
    }

    /// <summary>"English(en)" → en, "Chinese (Simplified)(zh-Hans)" → zh-CN, "Spanish (Spain)(es-ES)" → es</summary>
    static string TranslateCode(string header)
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

    static string Col(int index)       // 0 = A
    {
        string s = "";
        for (index++; index > 0; index = (index - 1) / 26) s = (char)('A' + (index - 1) % 26) + s;
        return s;
    }

    static string Q(string tab) => "'" + tab.Replace("'", "''") + "'";

    // ══════════════════ 구글 시트 API (로그인은 Unity 프로바이더 것) ══════════════════

    class Ctx
    {
        public string SheetId;
        public SheetsServiceProvider Provider;
        public string Token;
    }

    Ctx Connect()
    {
        var col = LocalizationEditorSettings.GetStringTableCollection(LocUtil.TableName);
        var ext = col != null ? col.Extensions.OfType<GoogleSheetsExtension>().FirstOrDefault() : null;
        if (ext == null || ext.SheetsServiceProvider == null || string.IsNullOrEmpty(ext.SpreadsheetId))
        {
            _status = $"'{LocUtil.TableName}'에 구글 시트 연결(Google Sheets 확장 · 프로바이더 · 시트 ID)이 없음";
            return null;
        }

        var ctx = new Ctx { SheetId = ext.SpreadsheetId, Provider = ext.SheetsServiceProvider };
        EditorUtility.DisplayProgressBar("능력 → 시트", "구글 로그인 확인…", 0.1f);
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
        var auth = typeof(SheetsServiceProvider).GetMethod("AuthorizeOAuth", Type.EmptyTypes)
                   ?? throw new Exception("AuthorizeOAuth 없음 — Localization 패키지 버전 확인");
        object cred = auth.Invoke(p, null);                  // 저장된 토큰이 있으면 바로, 없으면 브라우저 로그인
        if (cred == null) throw new Exception("구글 로그인 실패 (프로바이더가 OAuth 방식인지 확인)");

        var m = cred.GetType().GetMethod("GetAccessTokenForRequestAsync", new[] { typeof(string), typeof(CancellationToken) })
                ?? throw new Exception("GetAccessTokenForRequestAsync 없음");
        // 메인 스레드 멈춤 방지 — 다른 스레드에서 기다림
        return Task.Run(() => (Task<string>)m.Invoke(cred, new object[] { null, CancellationToken.None })).GetAwaiter().GetResult();
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

    static List<List<string>> GetValues(Ctx ctx, string range)
    {
        string body = Send(ctx, HttpMethod.Get, $"{Api}{ctx.SheetId}/values/{Uri.EscapeDataString(range)}", null);
        var result = new List<List<string>>();
        if (MiniJson.Parse(body) is Dictionary<string, object> o && o.TryGetValue("values", out var v) && v is List<object> rows)
            foreach (var r in rows)
                result.Add(r is List<object> cells ? cells.Select(c => c?.ToString() ?? "").ToList() : new List<string>());
        return result;
    }

    static void BatchUpdate(Ctx ctx, List<(string range, List<List<string>> values)> data, string mode)
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

    static Exception Root(Exception e)
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
            return lit == "null" ? null : lit;      // 숫자 · true/false는 글자 그대로
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
