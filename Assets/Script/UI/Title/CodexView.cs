using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 도감 — 메인 · 보조 · 랜덤 능력. 게임에서 한 번이라도 가져 본 능력이 열림 (MatchRecorder가 기록).
/// 못 만난 진화형은 검은 실루엣 + ??? + "○○을 진화시켜 보세요"
/// </summary>
public class CodexView : TitlePanel
{
    static readonly string[] TabNames = { "메인", "보조", "랜덤" };

    int _tab;
    AbilityData _sel;
    readonly List<AbilityData> _list = new();
    readonly Dictionary<int, AbilityData> _evolvedFrom = new();

    static AbilityDatabase Db => Meta != null ? Meta.Abilities : null;

    public override void Show()
    {
        Frame.Open();
        BuildEvolveMap();
        Frame.Tabs(TabNames, _tab, i => { _tab = i; _sel = null; Refresh(); });
        for (int i = 0; i < TabNames.Length; i++) Frame.SetTabText(i, $"{TabNames[i]} {Count(i)}");
        Refresh();
    }

    AbilityData[] Source(int tab)
    {
        var db = Db;
        if (db == null) return new AbilityData[0];
        var a = tab switch { 0 => db.MainAbilities, 1 => db.SubAbilities, _ => db.RandomAbilities };
        return a ?? new AbilityData[0];
    }

    string Count(int tab)
    {
        var src = Source(tab).Where(a => a != null).ToList();
        return $"{src.Count(a => PlayerSave.Seen(a.Id))}/{src.Count}";
    }

    void BuildEvolveMap()
    {
        _evolvedFrom.Clear();
        var db = Db;
        if (db == null || db.All == null) return;
        foreach (var a in db.All)
            if (a != null && a.EvolveToId != 0 && !_evolvedFrom.ContainsKey(a.EvolveToId)) _evolvedFrom[a.EvolveToId] = a;
    }

    bool IsEvolution(AbilityData a) => _evolvedFrom.ContainsKey(a.Id);

    protected override void Refresh()
    {
        _list.Clear();
        _list.AddRange(Source(_tab).Where(a => a != null));
        Frame.SetEmpty(Db == null ? "능력 목록이 비어 있습니다" : "아직 없습니다");

        var cards = Frame.Layout(_list.Count, c => Pick(c.Data as AbilityData));
        for (int i = 0; i < _list.Count; i++)
        {
            var a = _list[i];
            bool seen = PlayerSave.Seen(a.Id);
            bool evo = IsEvolution(a);
            cards[i].Data = a;
            cards[i].Id = a.Id;
            string title = seen || !evo ? AbilityUi.Name(a) : "???";
            string sub = seen ? AbilityUi.NumberText(a, Db) : evo ? "진화" : "아직 만나지 못했습니다";
            cards[i].Set(title, sub, a.Icon, null, seen ? a.UiAccent : (Color?)null, !seen, null);
        }

        if (_sel == null || !_list.Contains(_sel)) _sel = _list.Count > 0 ? _list[0] : null;
        Pick(_sel);
    }

    void Pick(AbilityData a)
    {
        _sel = a;
        Frame.Clear();
        foreach (var c in Frame.Cards) c.SetSelected(c.gameObject.activeSelf && a != null && c.Data == (object)a);
        if (a == null) return;

        bool seen = PlayerSave.Seen(a.Id);
        bool evo = IsEvolution(a);
        Frame.ShowIcon(a.Icon, !seen);
        if (seen) Frame.SetPaint(a.UiAccent);        // 능력 색 스프레이 · 그림은 흰색

        if (!seen && evo)
        {
            var from = _evolvedFrom[a.Id];
            string fromName = AbilityUi.Name(from);
            TitlePanelFrame.Set(Frame.Name, "???");
            TitlePanelFrame.Set(Frame.Sub, "진화형");
            Frame.SetBody($"<b>{fromName}</b>{Josa(fromName, "을", "를")} 진화시켜 보십시오" +
                (from.EvolveKillCount > 0 ? $"\n<color=#9A93A8>처치 {from.EvolveKillCount}번</color>" : ""));
            return;
        }

        TitlePanelFrame.Set(Frame.Name, AbilityUi.Name(a));
        TitlePanelFrame.Set(Frame.Sub, AbilityUi.NumberText(a, Db) + (seen ? "" : " · 아직 만나지 못했습니다"));

        var sb = new StringBuilder();
        string sum = AbilityUi.Codex(a);         // 도감 설명 (게임 중 숫자 칸은 뺌)
        if (!string.IsNullOrWhiteSpace(sum)) sb.AppendLine(sum);
        if (seen)
        {
            string det = AbilityUi.StripLive(AbilityUi.Details(a));
            if (!string.IsNullOrWhiteSpace(det)) sb.AppendLine().Append(det.TrimEnd()).AppendLine();
            string fl = AbilityUi.StripLive(AbilityUi.Flavor(a));
            if (!string.IsNullOrWhiteSpace(fl) && fl != sum) sb.AppendLine().Append("<i><color=#9A93A8>").Append(fl).AppendLine("</color></i>");

            var rec = Find(a);
            if (rec != null && rec.Plays > 0)
                sb.AppendLine().Append($"<b>{rec.Plays}판</b> · 승 {rec.Wins} · 승률 {Pct(rec.WinRate)} · 처치 {rec.Kills}");
            if (a.EvolveToId != 0 && Db != null && Db.Get(a.EvolveToId) is { } to)
                sb.AppendLine().Append("<color=#9A93A8>진화 → ")
                  .Append(PlayerSave.Seen(to.Id) ? AbilityUi.Name(to) : "???").Append("</color>");
        }
        Frame.SetBody(sb.ToString().TrimEnd());
    }

    static AbilityRecord Find(AbilityData a)
    {
        var list = a.Kind == AbilityKind.Random ? PlayerSave.Data.Randoms : a.Kind == AbilityKind.Main ? PlayerSave.Data.Classes : null;
        return list?.FirstOrDefault(r => r.AbilityId == a.Id);
    }
}
