using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점 '한정판!' — MarketRules.Shop 규칙으로 몇 분마다 바뀌는 강화 무기 · 특수 인챈트 방어구 (소울 정가).
/// 같은 판 · 같은 시간대면 모두 같은 상품 (판 시작 시각 · 방 이름 · 몇 번째 교체로 정한 씨앗). 상품마다 한 사람당 한 번
/// </summary>
public static class LimitedShop
{
    static int _builtKey = int.MinValue;
    static readonly List<MarketOffer> _offers = new();
    static readonly HashSet<string> _bought = new();

    static float Period => MarketRules.Get().ShopRotateMinutes * 60f;

    /// <summary>판이 시작된 뒤 흐른 초 (모두 같음) — 판 밖이면 내 시간</summary>
    public static float Elapsed
    {
        get
        {
            var rm = RoundManager.Instance;
            if (rm != null && rm.Object != null && rm.Object.IsValid) return Mathf.Max(0f, rm.Runner.SimulationTime - rm.RoundStartTime);
            return Time.time;
        }
    }

    /// <summary>방 이름 + 판 시작 시각 (판마다 · 방마다 다르게)</summary>
    public static int RoundSeed
    {
        get
        {
            var rm = RoundManager.Instance;
            bool ok = rm != null && rm.Object != null && rm.Object.IsValid;
            string s = ok && rm.Runner.SessionInfo != null ? rm.Runner.SessionInfo.Name : "";
            unchecked
            {
                int h = 17;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h * 31 + (ok ? Mathf.RoundToInt(rm.RoundStartTime * 10f) : 0);
            }
        }
    }

    public static int Rotation => Mathf.FloorToInt(Elapsed / Period);
    public static float SecondsLeft => Period - (Elapsed % Period);

    static int Key => unchecked(RoundSeed * 7919 + Rotation * 104729);

    public static IReadOnlyList<MarketOffer> Current()
    {
        int key = Key;
        if (key == _builtKey && _offers.Count > 0) return _offers;
        _builtKey = key;
        _offers.Clear();
        float minutes = Rotation * Period / 60f;          // 이번 교체가 시작된 시각 기준 (중간에 열어도 같은 상품)
        _offers.AddRange(MarketStock.Generate(MarketRules.Get().Shop, key, minutes, false));
        if (_offers.Count == 0) _builtKey = int.MinValue;   // 아직 내 캐릭터가 없음 — 다음에 다시
        return _offers;
    }

    static string BoughtKey(MarketOffer o) => $"{_builtKey}:{o.Item.Id}:{o.Stage}";

    public static bool Sold(MarketOffer o) => o != null && o.Item != null && _bought.Contains(BoughtKey(o));

    public static bool Buy(MarketOffer o, out string reason)
    {
        reason = "";
        if (o == null || o.Item == null) return false;
        if (Sold(o)) { reason = SetLoc.T("shop.limited.sold", "이번 한정판에서 이미 구매했습니다"); return false; }
        if (!GearPurchase.Buy(o, out reason)) return false;
        _bought.Add(BoughtKey(o));
        return true;
    }
}
