using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 능력 설명의 확장 토큰. FormatPassive / FormatSkill 이 먼저 이걸 돌린 뒤 {0} {v0}을 처리함.
///
///   {b:필드}                  Behaviour 수치 (현재 레벨)
///   {b:필드:all}              전 레벨 — 400 / 425 / 450 (현재 굵게)
///   {b:필드:pct}              ×100 해서 표시 (0.5 → 50)
///   {b:필드*100}              곱해서 표시 (0.9 → 90) — {b:필드*100:all} 처럼 뒤에 서식도 됨. 나누기는 /N
///   {b:필드:all|pct|0.#}      섞어 쓰기 ( | 로 구분, 숫자 서식도 가능)
///
///   {dmg:계수}                공격력 × 계수% → 카드 "<b>230</b>" · 상세 창 "<b>230</b> (공격력 150%)"
///   {dmg:계수+고정}           공격력 × 계수% + 고정 → 카드 "<b>250</b>" · 상세 창 "<b>250</b> (공격력 150% + 20)"
///   {dmg:계수+고정:n}         항상 숫자만 / :full 항상 공식까지 / :f 공식만 / :all 전 레벨 숫자
///       계수·고정 자리에는 Behaviour 필드 이름, v0 v1 (이 항목의 Vars), 숫자(20) 모두 가능
///       게임 중엔 내 공격력으로 계산, 에디터에선 공식만 표시. 치명타·피해 증가 스탯은 미포함
///
///   {x:키}                    Behaviour가 지금 상태에 맞춰 넣는 글 (DescToken). 비어 있으면 그 줄째 사라짐
///       예) 죽음의 기사 — {x:name} {x:tier} {x:slash} {x:mount} {x:ult} {x:ghost}
/// </summary>
public static class AbilityDescTokens
{
    /// <summary>내 플레이어 — AbilityRuntime.Spawned에서 등록</summary>
    public static AbilityRuntime Local;

    /// <summary>true인 동안 {dmg:}에 (공격력 N% + M) 괄호까지 — 상세 창용 텍스트를 만들 때 켬</summary>
    public static bool Detailed;

    const string Dim = "#8A8A8A";
    const string DefaultNum = "0.##";

    static readonly Regex XToken = new(@"(\r?\n)?\{x:(\w+)\}", RegexOptions.Compiled);
    static readonly Regex DmgToken = new(@"\{dmg:([\w.+\s]+?)(?::(\w+))?\}", RegexOptions.Compiled);
    // {b:필드} · {b:필드*100} · {b:필드/10:all} — 이름 뒤 *N /N 으로 곱하기·나누기
    static readonly Regex BToken = new(@"\{b:(\w+)\s*(?:([*/])\s*([\d.]+))?(?::([^}]*))?\}", RegexOptions.Compiled);

    static string AtkLabel => LocUtil.GetOr("ui.ability.atk", "공격력");

    // ══════════════════ 진입점 ══════════════════

    public static string Apply(string text, AbilityData owner, AbilityData.RankVar[] vars, int rank, int maxLevel)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;

        var live = FindLive(owner);
        AbilityBehaviour b = live != null ? live : (owner != null ? owner.Behaviour : null);
        int levels = Mathf.Max(1, maxLevel);

        // 1) {x:} — 먼저 넣어야 그 안의 {b:} {dmg:}도 처리됨
        // 넣은 글 안에 또 {x:}가 있을 수 있어서 몇 번 반복 (예: 등급 글 안의 {x:chargedist})
        for (int pass = 0; pass < 3 && text.Contains("{x:"); pass++)
            text = XToken.Replace(text, m =>
            {
                string key = m.Groups[2].Value;
                string s = live != null ? live.DescToken(key, rank) : null;
                if (s == null && !Application.isPlaying) s = $"<color={Dim}>[x:{key}]</color>";
                return string.IsNullOrEmpty(s) ? "" : m.Groups[1].Value + s;
            });

        // 2) {dmg:}
        if (text.Contains("{dmg:"))
            text = DmgToken.Replace(text, m => Damage(m, b, live, vars, rank, levels));

        // 3) {b:}
        if (text.Contains("{b:"))
            text = BToken.Replace(text, m =>
            {
                string name = m.Groups[1].Value;
                if (!TryValues(name, b, vars, out var v)) return Missing("b:" + name);

                if (m.Groups[2].Success &&
                    float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float k))
                {
                    float mul = m.Groups[2].Value == "*" ? k : (Mathf.Approximately(k, 0f) ? 1f : 1f / k);
                    var scaled = new float[v.Length];
                    for (int i = 0; i < v.Length; i++) scaled[i] = v[i] * mul;
                    v = scaled;
                }

                return Levels(v, rank, levels, m.Groups[4].Success ? m.Groups[4].Value : null);
            });

        return text;
    }

    static AbilityBehaviour FindLive(AbilityData owner)
    {
        if (owner == null || Local == null) return null;
        var list = Local.Behaviours;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].SourceData == owner) return list[i];
        return null;
    }

    // ══════════════════ {dmg:} ══════════════════

    static string Damage(Match m, AbilityBehaviour b, AbilityBehaviour live, AbilityData.RankVar[] vars, int rank, int levels)
    {
        string[] terms = m.Groups[1].Value.Split('+');
        string mode = m.Groups[2].Success ? m.Groups[2].Value : "";

        string coefName = terms[0].Trim();
        if (!TryValues(coefName, b, vars, out var coefV)) return Missing("dmg:" + coefName);

        var flats = new List<float[]>();
        for (int i = 1; i < terms.Length; i++)
        {
            string t = terms[i].Trim();
            if (t.Length == 0) continue;
            if (!TryValues(t, b, vars, out var fv)) return Missing("dmg:" + t);
            flats.Add(fv);
        }

        var coef = new float[levels];
        var flat = new float[levels];
        bool anyFlat = false;
        for (int r = 0; r < levels; r++)
        {
            float mul = live != null ? live.DescDamageMul(coefName, r) : 1f;
            coef[r] = At(coefV, r) * mul;
            foreach (var f in flats) flat[r] += At(f, r);
            if (!Mathf.Approximately(flat[r], 0f)) anyFlat = true;
        }

        string sel = mode == "all" ? "all" : null;
        string formula = $"{AtkLabel} {Levels(coef, rank, levels, sel)}%" +
                         (anyFlat ? $" + {Levels(flat, rank, levels, sel)}" : "");

        var stats = Local != null ? Local.Stats : null;
        if (stats == null || mode == "f") return formula;     // 에디터 · 로비 → 공식만

        float atk = stats.Get(StatType.AttackPower);
        var dmg = new float[levels];
        for (int r = 0; r < levels; r++) dmg[r] = atk * coef[r] * 0.01f + flat[r];

        string num = mode == "all"
            ? Levels(dmg, rank, levels, "all|0")
            : "<b>" + At(dmg, rank).ToString("0", CultureInfo.InvariantCulture) + "</b>";

        bool withFormula = mode == "full" || (mode == "" && Detailed);
        return withFormula ? $"{num}<color={Dim}> ({formula})</color>" : num;
    }

    // ══════════════════ 값 찾기 ══════════════════

    /// <summary>숫자 · v0(Vars) · Behaviour 필드(LevelValue / float / int)</summary>
    static bool TryValues(string name, AbilityBehaviour b, AbilityData.RankVar[] vars, out float[] values)
    {
        values = null;

        if (float.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out float lit))
        {
            values = new[] { lit };
            return true;
        }

        if (vars != null && name.Length > 1 && name[0] == 'v' && int.TryParse(name.Substring(1), out int vi))
        {
            if (vi >= 0 && vi < vars.Length && vars[vi] != null) values = vars[vi].Values;
            return values != null;
        }

        var f = b != null ? b.GetType().GetField(name, LevelBinding.Flags) : null;
        object raw = f != null ? f.GetValue(b) : null;
        values = raw switch
        {
            LevelValue lv => lv.Values,
            float x => new[] { x },
            int i => new[] { (float)i },
            _ => null,
        };
        return values != null;
    }

    static float At(float[] v, int r) =>
        v == null || v.Length == 0 ? 0f : v[Mathf.Clamp(r, 0, v.Length - 1)];

    static string Missing(string what) => $"<color=#E05050>[{what}?]</color>";

    // ══════════════════ 서식 ══════════════════

    /// <summary>fmt = "all" / "pct" / 숫자 서식("0.#")을 | 로 섞어서</summary>
    public static string Levels(float[] v, int rank, int levels, string fmt)
    {
        bool all = false;
        float scale = 1f;
        string num = DefaultNum;

        if (!string.IsNullOrEmpty(fmt))
            foreach (var part in fmt.Split('|'))
            {
                string p = part.Trim();
                if (p == "all") all = true;
                else if (p == "pct") scale = 100f;
                else if (p.Length > 0) num = p;
            }

        string F(int r) => (At(v, r) * scale).ToString(num, CultureInfo.InvariantCulture);

        if (!all) return F(rank);

        levels = Mathf.Max(1, levels);
        bool same = true;
        for (int i = 1; i < levels; i++)
            if (!Mathf.Approximately(At(v, i), At(v, 0))) { same = false; break; }
        if (same) return F(0);

        var sb = new StringBuilder();
        for (int i = 0; i < levels; i++)
        {
            if (i > 0) sb.Append(" / ");
            if (i == rank) sb.Append("<b>").Append(F(i)).Append("</b>");
            else sb.Append("<color=").Append(Dim).Append('>').Append(F(i)).Append("</color>");
        }
        return sb.ToString();
    }
}

/// <summary>설명용 키-글 한 줄. Behaviour가 {x:키}에 넣을 글을 인스펙터에서 적을 때</summary>
[System.Serializable]
public class DescLine
{
    public string Key = "slash";
    [TextArea(2, 12)] public string Text;

    public DescLine() { }
    public DescLine(string key, string text) { Key = key; Text = text; }

    public static string Find(DescLine[] lines, string key)
    {
        if (lines == null) return null;
        foreach (var l in lines)
            if (l != null && l.Key == key && !string.IsNullOrWhiteSpace(l.Text))
                return LocUtil.Resolve(l.Text);
        return null;
    }
}