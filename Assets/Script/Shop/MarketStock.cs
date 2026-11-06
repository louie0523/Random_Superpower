using System.Collections.Generic;
using UnityEngine;

/// <summary>상점 한정판 · 암시장 상품 하나 — 강화된 무기 / 특수 인챈트 방어구</summary>
public class MarketOffer
{
    public EquipmentData Item;
    /// <summary>무기 인챈트 (단계 순서대로 · 일반 = EnchantData.Id · 특수 = EnchantCode)</summary>
    public int[] Codes = new int[0];
    /// <summary>방어구 특수 인챈트</summary>
    public ArmorEnchantData ArmorEnchant;
    public int Specials;
    public int FullPrice;
    public float Discount;
    public int Price;
    /// <summary>낙사 구역에 떨어져 사라졌던 무기 (행방불명)</summary>
    public bool Lost;

    public int Stage => Codes != null ? Codes.Length : 0;
}

/// <summary>
/// 상품 만들기 — 같은 씨앗(seed) · 같은 시간이면 누가 계산해도 같은 상품 (네트워크로 상품을 안 보내도 모두 같게).
/// 강화는 그 무기의 인챈트 표대로: 특수 단계면 무작위 특수 인챈트, 아니면 일반 인챈트
/// </summary>
public static class MarketStock
{
    public static EquipmentDatabase Equipment =>
        PlayerAttack.Local != null && PlayerAttack.Local.TryGetComponent<PlayerEquipment>(out var eq) ? eq.Database : null;

    public static PlayerEnchant LocalEnchant =>
        PlayerAttack.Local != null && PlayerAttack.Local.TryGetComponent<PlayerEnchant>(out var en) ? en : null;

    /// <param name="minutes">판 시작부터 흐른 분 (등급 확률)</param>
    /// <param name="marketOnly">암시장 전용 무기도 넣나 (암시장만 true)</param>
    /// <param name="lostItem">행방불명 무기 (0 = 없음) — 마지막 칸에</param>
    public static List<MarketOffer> Generate(MarketRules.OfferRules r, int seed, float minutes, bool marketOnly,
                                             int lostItem = 0, int[] lostCodes = null, float lostExtra = 0f)
    {
        var list = new List<MarketOffer>();
        var db = Equipment;
        if (db == null || db.All == null || r == null) return list;

        var weapons = new List<EquipmentData>();
        var armors = new List<EquipmentData>();
        foreach (var d in db.All)
        {
            if (d == null || !d.IsListed || d.IsSpecial || d.IsUnarmed) continue;
            if (d.BlackMarketOnly && !marketOnly) continue;
            if (d.IsWeapon) weapons.Add(d);
            else if (d.IsArmorPiece) armors.Add(d);
        }
        weapons.Sort((a, b) => a.Id.CompareTo(b.Id));
        armors.Sort((a, b) => a.Id.CompareTo(b.Id));

        var keep = Random.state;
        Random.InitState(seed);
        try
        {
            var en = LocalEnchant;
            var enchDb = en != null ? en.Database : null;
            float k = Mathf.Clamp01(minutes / Mathf.Max(0.1f, r.LateMinutes));
            int count = Mathf.Max(1, r.Count);
            bool lost = lostItem != 0 && db.Get(lostItem) != null;
            int normal = lost ? count - 1 : count;
            int armorN = 0, sN = 0;

            for (int i = 0; i < normal; i++)
            {
                bool armor = armors.Count > 0 && armorN < r.MaxArmor && (weapons.Count == 0 || Random.value < r.ArmorChance);
                var pool = armor ? armors : weapons;
                if (pool.Count == 0) break;

                var grade = PickGrade(r, pool, k, sN);
                if (grade < 0) break;
                var cands = pool.FindAll(d => (int)d.Grade == grade);
                var item = cands[Random.Range(0, cands.Count)];
                pool.Remove(item);                         // 같은 물건 두 번 안 나오게
                if (armor) armorN++;
                if (item.Grade == ItemGrade.S) sN++;

                var o = new MarketOffer { Item = item };
                if (item.IsWeapon)
                {
                    int want = Random.Range(r.MinStage, r.MaxStage + 1);
                    o.Codes = RollCodes(item, Mathf.Min(want, item.MaxEnchantStage), en);
                }
                else if (Random.value < r.ArmorEnchantChance && ArmorEnchantDatabase.Default != null)
                {
                    int id = ArmorEnchantDatabase.Default.Roll(item);        // 방어구 후보 배열 우선
                    o.ArmorEnchant = id != 0 ? ArmorEnchantDatabase.Default.Get(id) : null;
                }
                Price(r, o, enchDb, RollDiscount(r));
                list.Add(o);
            }

            if (lost)
            {
                var o = new MarketOffer { Item = db.Get(lostItem), Codes = lostCodes ?? new int[0], Lost = true };
                Price(r, o, enchDb, Mathf.Min(0.95f, RollDiscount(r) + lostExtra));
                list.Add(o);
            }
        }
        finally { Random.state = keep; }
        return list;
    }

    /// <summary>등급 — 판 시간에 따라 처음 → 후반 확률 · 최소 등급 · S 개수 제한 · 이 칸에 물건이 있는 등급만</summary>
    static int PickGrade(MarketRules.OfferRules r, List<EquipmentData> pool, float k, int sCount)
    {
        var w = new float[4];
        float sum = 0f;
        for (int g = 0; g < 4; g++)
        {
            if (g < (int)r.MinGrade) continue;
            if (g == (int)ItemGrade.S && r.MaxS > 0 && sCount >= r.MaxS) continue;
            if (!pool.Exists(d => (int)d.Grade == g)) continue;
            float e = r.GradeWeightEarly != null && g < r.GradeWeightEarly.Length ? r.GradeWeightEarly[g] : 1f;
            float l = r.GradeWeightLate != null && g < r.GradeWeightLate.Length ? r.GradeWeightLate[g] : e;
            w[g] = Mathf.Max(0f, Mathf.Lerp(e, l, k));
            sum += w[g];
        }
        if (sum <= 0f)
        {
            // 확률이 전부 0이면 가능한 등급 아무거나 (물건이 있는데 안 나오는 일 없게)
            for (int g = 3; g >= (int)r.MinGrade; g--)
                if (pool.Exists(d => (int)d.Grade == g) && !(g == 3 && r.MaxS > 0 && sCount >= r.MaxS)) return g;
            return -1;
        }
        float x = Random.value * sum;
        for (int g = 0; g < 4; g++)
        {
            if (w[g] <= 0f) continue;
            x -= w[g];
            if (x <= 0f) return g;
        }
        for (int g = 3; g >= 0; g--) if (w[g] > 0f) return g;
        return -1;
    }

    /// <summary>n강까지 인챈트 — 특수 단계면 붙을 수 있는 특수 인챈트 중 무작위 (이미 있으면 다음 등급), 아니면 일반</summary>
    public static int[] RollCodes(EquipmentData d, int n, PlayerEnchant en)
    {
        var t = d != null ? d.EnchantTable : null;
        if (t == null || n <= 0) return new int[0];
        var rules = en != null ? en.Rules : null;
        var codes = new List<int>(n);
        var owned = new Dictionary<int, int>();

        for (int s = 1; s <= n; s++)
        {
            int code = 0;
            if (t.IsSpecialStage(s, false, 0) && t.SpecialCandidates != null)
            {
                float sum = 0f;
                foreach (var c in t.SpecialCandidates)
                    if (Eligible(c, s, owned)) sum += c.Weight;
                if (sum > 0f)
                {
                    float x = Random.value * sum;
                    foreach (var c in t.SpecialCandidates)
                    {
                        if (!Eligible(c, s, owned)) continue;
                        x -= c.Weight;
                        if (x > 0f) continue;
                        owned.TryGetValue(c.Special.Id, out int have);
                        int tier = have > 0 ? have + 1 : (rules != null ? rules.RollTier(c.Special.MaxTier) : 1);
                        owned[c.Special.Id] = tier;
                        code = EnchantCode.Make(c.Special.Id, tier);
                        break;
                    }
                }
            }
            if (code == 0)
            {
                var e = t.Roll(s);
                if (e != null) code = e.Id;
            }
            if (code != 0) codes.Add(code);
        }
        return codes.ToArray();
    }

    static bool Eligible(SpecialCandidate c, int stage, Dictionary<int, int> owned)
    {
        if (c?.Special == null || c.Weight <= 0f || c.Special.MaxTier <= 0 || !c.AvailableAt(stage)) return false;
        owned.TryGetValue(c.Special.Id, out int have);
        return have < c.Special.MaxTier;
    }

    static float RollDiscount(MarketRules.OfferRules r)
    {
        if (r.Discounts == null || r.Discounts.Length == 0) return 0f;
        float sum = 0f;
        foreach (var b in r.Discounts) if (b != null) sum += Mathf.Max(0f, b.Weight);
        if (sum <= 0f) return 0f;
        float x = Random.value * sum;
        foreach (var b in r.Discounts)
        {
            if (b == null) continue;
            x -= Mathf.Max(0f, b.Weight);
            if (x <= 0f) return Mathf.Round(Random.Range(b.Min, Mathf.Max(b.Min, b.Max)) * 100f) / 100f;
        }
        return 0f;
    }

    /// <summary>가격 = (등급 가격 + 추가 가격) × (1 + 강화% × 강화) × (1 + 특수% × 특수 개수) × (1 − 할인)</summary>
    static void Price(MarketRules.OfferRules r, MarketOffer o, EnchantDatabase enchDb, float discount)
    {
        var d = o.Item;
        var specials = new HashSet<int>();
        foreach (var c in o.Codes) if (EnchantCode.IsSpecial(c)) specials.Add(EnchantCode.SpecialId(c));
        o.Specials = specials.Count + (o.ArmorEnchant != null ? 1 : 0);

        int g = Mathf.Clamp((int)d.Grade, 0, 3);
        float basePrice = (r.GradePrice != null && r.GradePrice.Length > 0 ? r.GradePrice[Mathf.Min(g, r.GradePrice.Length - 1)] : 100) + d.MarketExtraPrice;
        float full = basePrice * (1f + r.PerStage * o.Stage) * (1f + r.PerSpecial * o.Specials);
        o.FullPrice = Mathf.Max(1, Mathf.RoundToInt(full));
        o.Discount = Mathf.Clamp(discount, 0f, 0.95f);
        o.Price = Mathf.Max(1, Mathf.RoundToInt(full * (1f - o.Discount)));
    }
}
