using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 무기 슬롯의 인챈트를 정리 — 특수 인챈트는 종류별 최고 등급으로, 일반 인챈트는 같은 것끼리 개수·합산 스탯으로.
/// 인챈트 페이지, 인벤토리 툴팁이 같이 씀.
/// </summary>
public static class EnchantSummary
{
    public struct StatSum
    {
        public StatType Stat;
        public ModType Type;
        public float Value;
    }

    public class Group
    {
        // 일반
        public EnchantData Data;
        public int Count;
        public readonly List<StatSum> Stats = new();

        // 특수
        public SpecialEnchantData Special;
        public int Tier;

        public bool IsSpecial => Special != null;
        public bool IsNewest;
    }

    /// <summary>특수(앞) → 일반(뒤), 각각 처음 나온 순서</summary>
    public static List<Group> Build(PlayerEnchant en, int slot, List<Group> into = null)
    {
        var list = into ?? new List<Group>();
        list.Clear();

        if (en == null || en.Database == null) return list;

        var db = en.Database;
        int stage = en.GetStage(slot);
        int specialCount = 0;

        for (int i = 0; i < stage; i++)
        {
            int code = en.GetEnchantId(slot, i);

            if (EnchantCode.IsSpecial(code))
            {
                var s = db.GetSpecial(EnchantCode.SpecialId(code));
                if (s == null) continue;

                int tier = EnchantCode.Tier(code);
                Group g = null;
                for (int k = 0; k < specialCount; k++)
                    if (list[k].Special == s) { g = list[k]; break; }

                if (g == null)
                {
                    g = new Group { Special = s, Tier = tier };
                    list.Insert(specialCount++, g);
                }
                else if (tier > g.Tier) g.Tier = tier;
            }
            else
            {
                var d = db.Get(code);
                if (d == null) continue;

                Group g = null;
                for (int k = specialCount; k < list.Count; k++)
                    if (list[k].Data == d) { g = list[k]; break; }

                if (g == null)
                {
                    g = new Group { Data = d };
                    list.Add(g);
                }
                g.Count++;
            }
        }

        int lastCode = stage > 0 ? en.GetEnchantId(slot, stage - 1) : 0;
        bool fresh = lastCode != 0 && lastCode == en.LastRolled;

        foreach (var g in list)
        {
            if (!g.IsSpecial) Sum(g);

            g.IsNewest = fresh && (g.IsSpecial
                ? EnchantCode.IsSpecial(lastCode) && EnchantCode.SpecialId(lastCode) == g.Special.Id
                : !EnchantCode.IsSpecial(lastCode) && g.Data.Id == lastCode);
        }

        return list;
    }

    static void Sum(Group g)
    {
        g.Stats.Clear();
        if (g.Data.Stats == null) return;

        foreach (var s in g.Data.Stats)
        {
            float total = StatText.IsMultiplier(s.Stat) && s.Type == ModType.Percent
                ? (Mathf.Pow(1f + s.Value * 0.01f, g.Count) - 1f) * 100f
                : s.Value * g.Count;

            int idx = -1;
            for (int i = 0; i < g.Stats.Count; i++)
                if (g.Stats[i].Stat == s.Stat && g.Stats[i].Type == s.Type) { idx = i; break; }

            if (idx >= 0)
            {
                var t = g.Stats[idx];
                t.Value += total;
                g.Stats[idx] = t;
            }
            else g.Stats.Add(new StatSum { Stat = s.Stat, Type = s.Type, Value = total });
        }
    }

    // ══════════════════ 표시 ══════════════════

    public static string Name(EnchantData d) =>
        LocUtil.GetOr($"enchant.{d.Id}.name", LocUtil.Resolve(d.DisplayName));

    public static string ColoredName(EnchantData d) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(d.TierColor)}>{Name(d)}</color>";

    /// <summary>묶음 이름 — 일반은 이름, 특수는 "둔화 II"</summary>
    public static string Name(Group g) => g.IsSpecial ? g.Special.Name(g.Tier) : Name(g.Data);

    public static Color Color(Group g) => g.IsSpecial ? g.Special.Color : g.Data.TierColor;

    public static string ColoredName(Group g) =>
        g.IsSpecial ? g.Special.ColoredName(g.Tier) : ColoredName(g.Data);

    /// <summary>일반은 합산 스탯, 특수는 등급 효과 설명</summary>
    public static string Detail(Group g, string separator = "   ") =>
        g.IsSpecial ? g.Special.Describe(g.Tier) : StatLine(g, separator);

    /// <summary>툴팁 한 줄 — "  날카로움 ×12   공격력 +36"  /  "  둔화 II   타격 시 둔화 23% (2초)"</summary>
    public static string Line(Group g)
    {
        var sb = new StringBuilder("  ");
        sb.Append(ColoredName(g));
        if (!g.IsSpecial) sb.Append(" ×").Append(g.Count);
        sb.Append("   ").Append(Detail(g, "  "));
        return sb.ToString();
    }

    public static string StatLine(Group g, string separator = "   ")
    {
        var sb = new StringBuilder();
        foreach (var s in g.Stats)
        {
            if (sb.Length > 0) sb.Append(separator);
            sb.Append(LocUtil.Get($"stat.{s.Stat}")).Append(' ')
              .Append(StatText.Value(s.Stat, s.Type, s.Value));
        }
        return sb.ToString();
    }
}