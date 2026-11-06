using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 암시장 — 맵의 NPC · 장소에 붙임 (네트워크 오브젝트 아님, 그냥 씬 오브젝트). 일반 상점(B)과 따로.
/// 무기 · 방어구를 정해진 가격(소울)에 팖. 가까이 가면 '[R] 암시장' → 창.
///   물건: Stock에 적은 것 (Price 0 = 등급별 가격). Stock이 비면 데이터베이스의 무기 · 방어구 전부(특수 제외)
///   방어구에 Enchant를 넣으면 그 특수 인챈트가 붙은 채로 팖 (+ EnchantPrice)
///   무기 칸이 꽉 차 있으면 든 무기를 바닥에 내려놓고 바꿈 · 방어구는 끼고 있던 걸 바닥에
/// 구매는 내 캐릭터 권한자(내 컴퓨터)에서 바로 — 재고가 없어서(무제한) 서버 쪽 상태가 필요 없음
/// </summary>
public class BlackMarket : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public EquipmentData Item;
        [Tooltip("가격 (소울). 0 = 등급별 가격")]
        [Min(0)] public int Price;
        [Tooltip("방어구만 — 이 특수 인챈트가 붙은 채로")]
        public ArmorEnchantData Enchant;
    }

    [Header("이름")]
    [Tooltip("번역 키 — 비우면 아래 이름")]
    [SerializeField] private string nameKey = "market.black.name";
    [SerializeField] private string displayName = "암시장";

    [Header("이용")]
    [Tooltip("이 거리 안이면 이용 가능 (m)")]
    [SerializeField] private float radius = 3f;
    [Tooltip("전투 중엔 못 씀 (일반 상점과 같게)")]
    [SerializeField] private bool outOfCombatOnly = true;

    [Header("물건")]
    [SerializeField] private Entry[] stock;
    [Tooltip("Stock이 빌 때 — 이 데이터베이스의 무기 · 방어구 전부 (특수 제외)")]
    [SerializeField] private EquipmentDatabase database;

    [Header("가격")]
    [Tooltip("Price가 0일 때 — 등급 C · B · A · S")]
    [SerializeField] private int[] gradePrice = { 40, 90, 170, 320 };
    [Tooltip("특수 인챈트가 붙은 방어구 추가 가격")]
    [SerializeField] private int enchantPrice = 60;

    static readonly List<BlackMarket> _all = new();
    public static IReadOnlyList<BlackMarket> All => _all;

    List<Entry> _auto;

    public float Radius => radius;
    public string Name => LocUtil.GetOr(nameKey, displayName);

    /// <summary>모든 컴퓨터에서 같은 순서 (위치 순) — MarketDirector가 번호로 고름</summary>
    public static List<BlackMarket> Sorted()
    {
        var l = new List<BlackMarket>(_all);
        l.RemoveAll(m => m == null);
        l.Sort((a, b) =>
        {
            var p = a.transform.position;
            var q = b.transform.position;
            int c = Mathf.RoundToInt(p.x * 10f).CompareTo(Mathf.RoundToInt(q.x * 10f));
            if (c == 0) c = Mathf.RoundToInt(p.z * 10f).CompareTo(Mathf.RoundToInt(q.z * 10f));
            if (c == 0) c = Mathf.RoundToInt(p.y * 10f).CompareTo(Mathf.RoundToInt(q.y * 10f));
            return c;
        });
        return l;
    }

    /// <summary>글로벌 이벤트로 도는 중 (맵에 MarketDirector) — 아니면 예전처럼 늘 열림 · Stock 판매</summary>
    public static bool Global => MarketDirector.Instance != null && MarketDirector.Instance.Object != null && MarketDirector.Instance.Object.IsValid;

    /// <summary>지금 이 암시장이 열려 있나</summary>
    public bool IsOpenNow => !Global || MarketDirector.Instance.IsOpen(this);

    void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
    void OnDisable() => _all.Remove(this);

    // ══════════════════ 물건 · 가격 ══════════════════

    public IReadOnlyList<Entry> Items()
    {
        if (stock != null && stock.Length > 0) return stock;
        if (_auto != null) return _auto;

        _auto = new List<Entry>();
        var db = database != null ? database : FindDatabase();
        if (db != null && db.All != null)
            foreach (var d in db.All)
                if (d != null && d.IsListed && !d.IsSpecial && !d.IsUnarmed && (d.IsWeapon || d.IsArmorPiece))
                    _auto.Add(new Entry { Item = d });
        _auto.Sort((a, b) => a.Item.IsWeapon != b.Item.IsWeapon ? (a.Item.IsWeapon ? -1 : 1)
                                : a.Item.Grade != b.Item.Grade ? a.Item.Grade.CompareTo(b.Item.Grade)
                                : a.Item.Id.CompareTo(b.Item.Id));
        return _auto;
    }

    static EquipmentDatabase FindDatabase()
    {
        var me = PlayerAttack.Local;
        return me != null && me.TryGetComponent<PlayerEquipment>(out var eq) ? eq.Database : null;
    }

    public int PriceOf(Entry e)
    {
        if (e == null || e.Item == null) return 0;
        int p = e.Price > 0 ? e.Price
              : gradePrice != null && gradePrice.Length > 0 ? gradePrice[Mathf.Clamp((int)e.Item.Grade, 0, gradePrice.Length - 1)] : 50;
        if (e.Enchant != null && e.Item.IsArmorPiece) p += enchantPrice;
        return Mathf.Max(0, p);
    }

    // ══════════════════ 이용 (내 화면) ══════════════════

    /// <summary>내 캐릭터가 이용할 수 있나 — reason은 번역된 이유</summary>
    public bool CanUse(out string reason)
    {
        reason = null;
        var me = PlayerAttack.Local;
        if (me == null || !me.TryGetComponent<PlayerHealth>(out var hp)) { reason = ""; return false; }
        if (hp.IsDead) { reason = SetLoc.T("market.reason.dead", "죽은 상태에서는 살 수 없습니다"); return false; }
        if (!IsOpenNow) { reason = SetLoc.T("market.reason.closed", "암시장이 닫혔습니다"); return false; }
        if (Vector3.Distance(me.transform.position, transform.position) > radius + 0.75f) { reason = SetLoc.T("market.reason.far", "너무 멉니다"); return false; }
        if (outOfCombatOnly && hp.InCombat) { reason = SetLoc.T("market.reason.combat", "전투 중에는 거래할 수 없습니다"); return false; }
        return true;
    }

    public bool CanBuy(Entry e, out string reason)
    {
        if (!CanUse(out reason)) return false;
        var me = PlayerAttack.Local;
        var hp = me.GetComponent<PlayerHealth>();
        var eq = me.GetComponent<PlayerEquipment>();
        if (eq == null || e?.Item == null) { reason = ""; return false; }
        if (eq.Locked) { reason = SetLoc.T("market.reason.locked", "변신 중에는 장비를 바꿀 수 없습니다"); return false; }
        if (hp.Soul < PriceOf(e)) { reason = SetLoc.T("market.reason.soul", "소울이 부족합니다"); return false; }
        return true;
    }

    /// <summary>사기 — 내 캐릭터 권한자(이 컴퓨터)에서 바로</summary>
    public bool Buy(Entry e, out string reason)
    {
        if (!CanBuy(e, out reason)) return false;
        var me = PlayerAttack.Local;
        var hp = me.GetComponent<PlayerHealth>();
        var eq = me.GetComponent<PlayerEquipment>();
        int price = PriceOf(e);
        var d = e.Item;

        bool ok;
        if (d.IsWeapon)
        {
            if (eq.HasEmptyWeaponSlot()) ok = eq.PickUpWeapon(d.Id, out _);
            else { eq.SwapWithPickup(d.Id); ok = true; }           // 든 무기는 바닥에
        }
        else ok = eq.EquipArmor(d.Id, e.Enchant != null ? e.Enchant.Id : 0, true);

        if (!ok) { reason = SetLoc.T("market.reason.fail", "지금은 살 수 없습니다"); return false; }

        hp.SpendSoul(price);
        reason = SetLoc.F("market.bought", "{0}을(를) 구매했습니다", d.GetName());
        return true;
    }

    void Update()
    {
        if (BlackMarketWindow.IsOpen || UIManager.InputBlocked) return;
        var me = PlayerAttack.Local;
        if (me == null) return;
        if (Vector3.Distance(me.transform.position, transform.position) > radius) return;
        if (me.TryGetComponent<PlayerHealth>(out var hp) && hp.IsDead) return;
        if (!IsOpenNow)
        {
            AltarHud.Prompt(SetLoc.T("market.black.closed_prompt", "암시장 — 지금은 닫혀 있습니다"));
            return;
        }

        AltarHud.Prompt(SetLoc.F("market.black.prompt", "[{0}] {1}", KeyBinds.Name(GameAction.Interact), Name));
        if (KeyBinds.Down(GameAction.Interact)) BlackMarketWindow.Open(this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.3f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
