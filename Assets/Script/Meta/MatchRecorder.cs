using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 판 기록 · 보상 — 저절로 생김 (씬에 안 놓아도 됨).
///   게임 중: 내가 가진 메인 · 랜덤 능력을 도감에 '봤음'으로 (진화 · 변화도 직접 해 보면 열림)
///   판이 끝나면(RoundManager.GameOver): 등수(제단 수 → 소울) · 처치 · 소울 · 승리 → 기록 + SC · 티켓 (RewardRules)
/// 마지막 보상은 LastReward에 — 결과 화면이 보여 줄 수 있게
/// </summary>
public class MatchRecorder : MonoBehaviour
{
    public struct Reward { public int Rank, Players, SC, Tickets, Kills, Soul; public bool Win; }

    public static Reward? LastReward { get; private set; }
    public static event System.Action<Reward> Rewarded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<MatchRecorder>() != null) return;
        var go = new GameObject("MatchRecorder");
        DontDestroyOnLoad(go);
        go.AddComponent<MatchRecorder>();
    }

    float _next;
    bool _wasOver;
    int _main, _rand1, _rand2;
    readonly HashSet<int> _randomsThisRound = new();

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;

        var rm = RoundManager.Instance;
        if (rm == null || rm.Object == null || !rm.Object.IsValid) { _wasOver = false; return; }
        var runner = rm.Runner;
        if (runner == null) return;

        var me = runner.LocalPlayer;
        var obj = runner.GetPlayerObject(me);

        // 도감 · 이번 판 능력
        if (obj != null && obj.TryGetComponent<AbilityRuntime>(out var ar))
        {
            bool changed = false;
            var main = ar.GetMainAbility();
            if (main != null) { _main = main.Id; changed |= PlayerSave.See(main.Id); }
            var r1 = ar.GetRandomAbility();
            if (r1 != null) { _rand1 = r1.Id; _randomsThisRound.Add(r1.Id); changed |= PlayerSave.See(r1.Id); }
            var r2 = ar.GetRandomAbility2();
            if (r2 != null) { _rand2 = r2.Id; _randomsThisRound.Add(r2.Id); changed |= PlayerSave.See(r2.Id); }
            if (changed) PlayerSave.Commit();
        }

        bool over = rm.GameOver;
        if (over && !_wasOver) Finish(rm, runner, me, obj);
        if (!over && _wasOver) _randomsThisRound.Clear();
        _wasOver = over;
    }

    void Finish(RoundManager rm, NetworkRunner runner, PlayerRef me, NetworkObject obj)
    {
        var d = PlayerSave.Data;
        int roundKey = rm.RoundIndex * 100000 + Mathf.RoundToInt(rm.RoundStartTime) % 100000;
        if (d.LastRound == roundKey) return;          // 이미 기록함
        d.LastRound = roundKey;

        // 등수 — 제단 수 → 소울 (순위표와 같은 기준)
        int myAltars = PlayerAltars.CountOf(me), mySoul = 0, myKills = 0;
        if (obj != null && obj.TryGetComponent<PlayerHealth>(out var mh)) { mySoul = mh.Soul; myKills = mh.Kills; }
        int rank = 1, players = 0;
        foreach (var p in runner.ActivePlayers)
        {
            var o = runner.GetPlayerObject(p);
            if (o == null || !o.TryGetComponent<PlayerHealth>(out var h)) continue;
            players++;
            if (p == me) continue;
            int a = PlayerAltars.CountOf(p);
            if (a > myAltars || (a == myAltars && h.Soul > mySoul)) rank++;
        }
        bool win = rm.Winner == me;
        if (win) rank = 1;

        // 기록
        d.Matches++;
        if (win) d.Wins++;
        d.Kills += myKills;
        if (_main > 0) Add(PlayerSave.Record(d.Classes, _main), win, myKills, mySoul);
        foreach (var id in _randomsThisRound) Add(PlayerSave.Record(d.Randoms, id), win, myKills, mySoul);

        // 보상
        var rules = GameMeta.Get()?.Rewards;
        int sc = rules != null ? rules.SCFor(rank, myKills) : 0;
        int tk = rules != null ? rules.TicketsFor(rank) : 0;
        PlayerSave.AddSC(sc);
        PlayerSave.AddTickets(tk);
        PlayerSave.Commit();

        var rw = new Reward { Rank = rank, Players = players, SC = sc, Tickets = tk, Kills = myKills, Soul = mySoul, Win = win };
        LastReward = rw;
        Rewarded?.Invoke(rw);
        Debug.Log($"[기록] {rank}등 / {players}명 · 처치 {myKills} · 소울 {mySoul} → SC +{sc} · 티켓 +{tk}");
    }

    static void Add(AbilityRecord r, bool win, int kills, int soul)
    {
        r.Plays++;
        if (win) r.Wins++;
        r.Kills += kills;
        r.TotalSoul += soul;
        r.MaxSoul = Mathf.Max(r.MaxSoul, soul);
    }
}
