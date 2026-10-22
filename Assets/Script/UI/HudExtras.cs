using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>랜덤 능력 HUD 아래에 붙는 추가 줄 하나 (복수 시간 · 데스 노트 등)</summary>
public struct HudExtraRow
{
    /// <summary>줄 구분 번호 — 바뀌면 새 줄로 보고 '톡' 튐. 능력 Id와 겹치지 않게 음수</summary>
    public int Key;
    /// <summary>아이콘 · 색을 빌려 올 능력 (비우면 기본 색)</summary>
    public AbilityData Style;
    public string Name;
    /// <summary>"NO. 12 - 1" 같은 번호 칸 (비우면 안 보임)</summary>
    public string Number;
    public string Summary;
}

/// <summary>
/// 랜덤 능력 HUD의 추가 줄 — 능력이 바뀌어도 남는 효과(복수 시간 · 데스 노트 예약)를 내 화면에 보여 줌.
///   Add(제공자) / Remove(제공자) — 제공자가 false를 돌려주면 그 프레임엔 안 보임
///   AddTimed(...) — N초 동안만 보이는 줄 (끝나면 알아서 빠짐)
/// 내 화면 전용 (네트워크 아님).
/// </summary>
public static class HudExtras
{
    public delegate bool Provider(out HudExtraRow row);

    static readonly List<Provider> _list = new();
    static int _nextKey = -20000;

    public static void Add(Provider p)
    {
        if (p != null && !_list.Contains(p)) _list.Add(p);
    }

    public static void Remove(Provider p)
    {
        if (p != null) _list.Remove(p);
    }

    public static void Collect(List<HudExtraRow> into)
    {
        if (_list.Count == 0) return;
        var copy = _list.ToArray();         // 제공자가 도는 중에 스스로 빠질 수 있음
        foreach (var p in copy)
        {
            try
            {
                if (p(out var r)) into.Add(r);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _list.Remove(p);
            }
        }
    }

    /// <summary>
    /// seconds초 동안 보이는 줄. summary(남은 초)가 null을 돌려주면 그때 끝남 (대상이 먼저 죽음 등)
    /// </summary>
    public static void AddTimed(AbilityData style, string name, string number, float seconds, Func<float, string> summary)
    {
        float until = Time.time + Mathf.Max(0f, seconds);
        int key = _nextKey--;
        Provider p = null;
        p = (out HudExtraRow row) =>
        {
            row = default;
            float left = until - Time.time;
            string s = left > 0f && summary != null ? summary(left) : null;
            if (s == null) { _list.Remove(p); return false; }
            row = new HudExtraRow { Key = key, Style = style, Name = name, Number = number, Summary = s };
            return true;
        };
        Add(p);
    }

    /// <summary>"NO. 12 - 1" — 그 능력 번호에 단계를 붙인 글 (진화 번호와 같은 모양)</summary>
    public static string Number(AbilityData a, int stage)
    {
        if (a == null) return "";
        AbilityUi.GetNumber(a, null, out int n, out _);
        string fmt = stage > 0
            ? LocUtil.GetOr("ui.ability.number_evolved", "NO. {0} - {1}")
            : LocUtil.GetOr("ui.ability.number", "NO. {0}");
        return fmt.Replace("{0}", n.ToString()).Replace("{1}", stage.ToString());
    }
}
