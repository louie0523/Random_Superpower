using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 프로필 — 요약(판 · 승 · 처치 · 도감) / 클래스별 · 랜덤 능력별 기록 (판 수 · 승률 · 처치 · 소울)
/// </summary>
public class ProfileView : TitlePanel
{
    static readonly string[] TabNames = { "요약", "클래스", "랜덤 능력" };

    int _tab;
    AbilityRecord _sel;
    readonly List<AbilityRecord> _list = new();

    static AbilityDatabase Db => Meta != null ? Meta.Abilities : null;

    public override void Show()
    {
        Frame.Open();
        Frame.Tabs(TabNames, _tab, i => { _tab = i; _sel = null; Refresh(); });
        Refresh();
    }

    protected override void OnSave() => Refresh();

    protected override void Refresh()
    {
        _list.Clear();
        if (_tab > 0)
        {
            var src = _tab == 1 ? PlayerSave.Data.Classes : PlayerSave.Data.Randoms;
            _list.AddRange(src.Where(r => r != null && r.Plays > 0).OrderByDescending(r => r.Plays).ThenByDescending(r => r.WinRate));
        }
        Frame.SetEmpty(_tab == 0 ? "" : "아직 기록이 없어요 — 한 판 해 보세요");

        var cards = Frame.Layout(_list.Count, c => Pick(c.Data as AbilityRecord));
        for (int i = 0; i < _list.Count; i++)
        {
            var r = _list[i];
            var a = Db != null ? Db.Get(r.AbilityId) : null;
            cards[i].Data = r;
            cards[i].Id = r.AbilityId;
            cards[i].Set(a != null ? AbilityUi.Name(a) : "#" + r.AbilityId, $"{r.Plays}판 · 승률 {Pct(r.WinRate)}",
                         a != null ? a.Icon : null, null, a != null ? a.UiAccent : (Color?)null, false,
                         r.Wins > 0 ? $"{r.Wins}승" : null);
        }

        if (_tab == 0) { Summary(); return; }
        if (_sel == null || !_list.Contains(_sel)) _sel = _list.Count > 0 ? _list[0] : null;
        Pick(_sel);
    }

    void Summary()
    {
        Frame.Clear();
        var d = PlayerSave.Data;
        Frame.ShowTexture(CharacterPhoto.Get(PlayerSave.Look.PartsCode, CharacterPhoto.Frame.Bust, 384, 384));
        TitlePanelFrame.Set(Frame.Name, RoomLauncher.Nick);
        TitlePanelFrame.Set(Frame.Sub, $"{d.SC:N0} SC · 티켓 {d.Tickets}");

        var sb = new StringBuilder();
        float wr = d.Matches > 0 ? d.Wins / (float)d.Matches : 0f;
        sb.AppendLine($"<b>{d.Matches}판</b> · {d.Wins}승 · 승률 {Pct(wr)}");
        sb.AppendLine($"처치 {d.Kills} · 판당 {(d.Matches > 0 ? d.Kills / (float)d.Matches : 0f):0.#}");

        var db = Db;
        if (db != null && db.All != null)
        {
            int all = db.All.Count(a => a != null), seen = db.All.Count(a => a != null && PlayerSave.Seen(a.Id));
            sb.AppendLine($"도감 {seen}/{all}");
        }

        var best = d.Classes.Where(r => r.Plays > 0).OrderByDescending(r => r.Plays).FirstOrDefault();
        if (best != null && db != null && db.Get(best.AbilityId) is { } ba)
            sb.AppendLine($"많이 한 클래스  <b>{AbilityUi.Name(ba)}</b> ({best.Plays}판)");

        if (MatchRecorder.LastReward is { } rw)
            sb.AppendLine().Append($"<color=#9A93A8>지난 판 {rw.Rank}등 / {rw.Players}명 · +{rw.SC} SC" +
                                   (rw.Tickets > 0 ? $" · 티켓 +{rw.Tickets}" : "") + "</color>");
        Frame.SetBody(sb.ToString().TrimEnd());
    }

    void Pick(AbilityRecord r)
    {
        _sel = r;
        Frame.Clear();
        foreach (var c in Frame.Cards) c.SetSelected(c.gameObject.activeSelf && r != null && c.Data == (object)r);
        if (r == null) return;

        var a = Db != null ? Db.Get(r.AbilityId) : null;
        Frame.ShowIcon(a != null ? a.Icon : null, false);
        TitlePanelFrame.Set(Frame.Name, a != null ? AbilityUi.Name(a) : "#" + r.AbilityId);
        TitlePanelFrame.Set(Frame.Sub, a != null ? AbilityUi.NumberText(a, Db) : "");
        Frame.SetBody(
            $"<b>{r.Plays}판</b> · {r.Wins}승 · 승률 {Pct(r.WinRate)}\n" +
            $"처치 {r.Kills} · 판당 {(r.Plays > 0 ? r.Kills / (float)r.Plays : 0f):0.#}\n" +
            $"소울 평균 {r.AvgSoul:0} · 최고 {r.MaxSoul}");
    }
}
