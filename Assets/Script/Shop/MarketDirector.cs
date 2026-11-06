using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 암시장 글로벌 이벤트 — 맵의 암시장(BlackMarket) 중 한 곳만 열림.
///   누군가 하나라도 사면 그 암시장은 닫힘 → 7~11분 뒤 무작위 암시장 하나가 열림 → 반복 (분은 MarketRules)
///   상품 5칸: 같은 씨앗으로 모두가 똑같이 계산 (B급부터 · 1~20강 · 할인) · 가끔 행방불명 무기 한 칸
///   행방불명: 낙사 구역(낙사 높이 아래)에 떨어진 무기는 사라지고 여기 목록에 쌓임 (최근 8개)
/// 맵에 하나 — RoundManager가 붙은 네트워크 오브젝트에 같이 붙이면 됨 (없으면 암시장은 예전처럼 늘 열림)
/// </summary>
public class MarketDirector : NetworkBehaviour
{
    public const int LostMax = 8;
    public const int LostStages = 20;

    public static MarketDirector Instance { get; private set; }

    [Networked] int OpenPlus1 { get; set; }          // 열린 암시장 (정렬 순서 + 1 · 0 = 닫힘)
    [Networked] public int Serial { get; private set; }  // 열릴 때마다 +1
    [Networked] float OpenedAt { get; set; }
    [Networked] float NextOpenAt { get; set; }
    [Networked] int Seed { get; set; }
    [Networked] int StockLostItem { get; set; }
    [Networked, Capacity(LostStages)] NetworkArray<int> StockLostCodes => default;

    [Networked] int LostCount { get; set; }
    [Networked, Capacity(LostMax)] NetworkArray<int> LostItems => default;
    [Networked, Capacity(LostMax * LostStages)] NetworkArray<int> LostCodes => default;

    int _seenSerial = -1;
    int _seenOpen = -1;
    int _builtSerial = -1;
    int _closedLocally = -1;
    int _lastPick = -1;
    readonly List<MarketOffer> _stock = new();

    public override void Spawned()
    {
        Instance = this;
        if (HasStateAuthority && Serial == 0 && NextOpenAt <= 0f)
        {
            var r = MarketRules.Get();
            NextOpenAt = Runner.SimulationTime + Random.Range(r.FirstOpenMinutes.x, Mathf.Max(r.FirstOpenMinutes.x, r.FirstOpenMinutes.y)) * 60f;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    // ══════════════════ 열고 닫기 (권한자) ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (OpenPlus1 != 0 || Runner.SimulationTime < NextOpenAt) return;

        var markets = BlackMarket.Sorted();
        if (markets.Count == 0) return;

        int pick = Random.Range(0, markets.Count);
        if (markets.Count > 1 && pick == _lastPick) pick = (pick + 1 + Random.Range(0, markets.Count - 1)) % markets.Count;   // 되도록 다른 곳

        // 행방불명 한 칸
        StockLostItem = 0;
        for (int i = 0; i < LostStages; i++) StockLostCodes.Set(i, 0);
        var r = MarketRules.Get();
        if (LostCount > 0 && Random.value < r.LostChance)
        {
            int idx = Random.Range(0, LostCount);
            StockLostItem = LostItems[idx];
            for (int i = 0; i < LostStages; i++) StockLostCodes.Set(i, LostCodes[idx * LostStages + i]);
            RemoveLost(idx);
        }

        Seed = Random.Range(int.MinValue, int.MaxValue);
        OpenedAt = Runner.SimulationTime;
        Serial++;
        OpenPlus1 = pick + 1;
        _lastPick = pick;
    }

    /// <summary>누가 샀음 → 그 암시장 닫고 다음 여는 시각</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Bought(int serial)
    {
        if (serial != Serial || OpenPlus1 == 0) return;
        OpenPlus1 = 0;
        var r = MarketRules.Get();
        NextOpenAt = Runner.SimulationTime + Random.Range(r.ReopenMinutes.x, Mathf.Max(r.ReopenMinutes.x, r.ReopenMinutes.y)) * 60f;
    }

    /// <summary>낙사 구역에 떨어진 무기 — 행방불명 목록에 (인챈트는 '1,2,3' 글자로)</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddLost(int itemId, string codes)
    {
        if (itemId == 0) return;
        if (LostCount >= LostMax) RemoveLost(0);       // 가장 오래된 것부터 밀어냄
        int idx = LostCount;
        LostItems.Set(idx, itemId);
        var parts = string.IsNullOrEmpty(codes) ? new string[0] : codes.Split(',');
        for (int i = 0; i < LostStages; i++)
            LostCodes.Set(idx * LostStages + i, i < parts.Length && int.TryParse(parts[i], out var c) ? c : 0);
        LostCount = idx + 1;
    }

    void RemoveLost(int idx)
    {
        for (int i = idx; i < LostCount - 1; i++)
        {
            LostItems.Set(i, LostItems[i + 1]);
            for (int s = 0; s < LostStages; s++) LostCodes.Set(i * LostStages + s, LostCodes[(i + 1) * LostStages + s]);
        }
        LostCount = Mathf.Max(0, LostCount - 1);
        LostItems.Set(LostCount, 0);
    }

    /// <summary>무기를 행방불명으로 (아무 컴퓨터에서나)</summary>
    public static void ReportLost(int itemId, int[] codes)
    {
        var d = Instance;
        if (d == null || d.Object == null || !d.Object.IsValid || itemId == 0) return;
        var sb = new System.Text.StringBuilder();
        if (codes != null)
            for (int i = 0; i < codes.Length && i < LostStages; i++)
            {
                if (codes[i] == 0) break;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(codes[i]);
            }
        d.RPC_AddLost(itemId, sb.ToString());
    }

    // ══════════════════ 내 화면 ══════════════════

    /// <summary>지금 열린 암시장 (없으면 null)</summary>
    public BlackMarket OpenMarket
    {
        get
        {
            if (Object == null || !Object.IsValid || OpenPlus1 == 0 || _closedLocally == Serial) return null;
            var m = BlackMarket.Sorted();
            int i = OpenPlus1 - 1;
            return i >= 0 && i < m.Count ? m[i] : null;
        }
    }

    public bool IsOpen(BlackMarket m) => m != null && OpenMarket == m;

    /// <summary>다음에 열릴 때까지 (닫혀 있을 때)</summary>
    public float SecondsToOpen => Object != null && Object.IsValid && OpenPlus1 == 0 ? Mathf.Max(0f, NextOpenAt - Runner.SimulationTime) : 0f;

    /// <summary>지금 상품 — 모두 같은 계산</summary>
    public IReadOnlyList<MarketOffer> Stock()
    {
        if (_builtSerial == Serial && _stock.Count > 0) return _stock;
        _stock.Clear();
        if (Object == null || !Object.IsValid || OpenPlus1 == 0) return _stock;

        int[] lost = null;
        if (StockLostItem != 0)
        {
            var l = new List<int>();
            for (int i = 0; i < LostStages; i++) { int c = StockLostCodes[i]; if (c == 0) break; l.Add(c); }
            lost = l.ToArray();
        }
        var rm = RoundManager.Instance;
        float minutes = rm != null && rm.Object != null && rm.Object.IsValid ? Mathf.Max(0f, OpenedAt - rm.RoundStartTime) / 60f : 0f;
        var r = MarketRules.Get();
        _stock.AddRange(MarketStock.Generate(r.Market, Seed, minutes, true, StockLostItem, lost, r.LostExtraDiscount));
        _builtSerial = _stock.Count > 0 ? Serial : -1;
        return _stock;
    }

    /// <summary>이 컴퓨터에서 샀음 — 바로 닫힌 걸로 보고 권한자에게 알림</summary>
    public void MarkBought()
    {
        _closedLocally = Serial;
        RPC_Bought(Serial);
    }

    public override void Render()
    {
        // 열림 · 닫힘 알림 (모두에게)
        int open = OpenPlus1;
        if (_seenSerial < 0) { _seenSerial = Serial; _seenOpen = open; return; }
        if (Serial != _seenSerial && open != 0)
        {
            var m = OpenMarket;
            AltarHud.Announce(SetLoc.F("market.event.open", "암시장이 열렸습니다 — {0}", m != null ? m.Name : ""));
        }
        else if (_seenOpen != 0 && open == 0 && _closedLocally != Serial)     // 내가 산 거면 알림 생략
            AltarHud.Toast(SetLoc.T("market.event.closed", "누군가 거래를 마쳐 암시장이 닫혔습니다"));
        _seenOpen = open;
        _seenSerial = Serial;
    }
}
