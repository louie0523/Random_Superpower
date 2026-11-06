using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ShopPanel : UIPanel, ILocRefresh
{
    public enum Tab { Main, Enchant, Special, Limited }

    [Header("공통")]
    [SerializeField] private TMP_Text soulValue;
    [SerializeField] private LocText soulLabel;
    [SerializeField] private LocText titleLabel;
    [SerializeField] private Button closeButton;

    [Header("탭 버튼")]
    [SerializeField] private Button mainTab;
    [SerializeField] private Button enchantTab;
    [SerializeField] private Button specialTab;
    [SerializeField] private LocText mainTabLabel;
    [SerializeField] private LocText enchantTabLabel;
    [SerializeField] private LocText specialTabLabel;

    [Header("탭 내용")]
    [SerializeField] private GameObject mainPage;
    [SerializeField] private GameObject enchantPage;
    [SerializeField] private GameObject specialPage;

    [Header("메인 상점")]
    [SerializeField] private Transform itemParent;
    [SerializeField] private ShopItemView itemPrefab;

    [Header("인챈트")]
    [SerializeField] private EnchantPage enchantView;

    [Header("특수 상점")]
    [SerializeField] private Transform specialParent;
    [SerializeField] private ShopItemView specialItemPrefab;
    [SerializeField] private LocText specialEmptyLabel;

    [Header("한정판 — 몇 분마다 바뀌는 무기 · 방어구 (비우면 '특수' 탭을 복사해 저절로 만듦)")]
    [SerializeField] private Button limitedTab;
    [SerializeField] private GameObject limitedPage;
    [SerializeField] private GearOfferPage limitedView;

    private readonly List<ShopItemView> _mainViews = new();
    private readonly List<ShopItemView> _specialViews = new();

    private PlayerShop _shop;
    private PlayerHealth _health;
    private AbilityRuntime _ability;
    private Tab _tab = Tab.Main;

    protected override void Awake()
    {
        base.Awake();

        mainTab?.onClick.AddListener(() => SetTab(Tab.Main));
        enchantTab?.onClick.AddListener(() => SetTab(Tab.Enchant));
        specialTab?.onClick.AddListener(() => SetTab(Tab.Special));
        EnsureLimited();
        limitedTab?.onClick.AddListener(() => SetTab(Tab.Limited));
        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseAll());

        titleLabel?.SetKey("ui.shop.title");
        soulLabel?.SetKey("ui.hud.soul");
        mainTabLabel?.SetKey("ui.shop.tab_main");
        enchantTabLabel?.SetKey("ui.shop.tab_enchant");
        specialTabLabel?.SetKey("ui.shop.tab_special");
        specialEmptyLabel?.SetKey("ui.shop.special_empty");


    }

    public void Bind(PlayerShop shop, PlayerHealth health, AbilityRuntime ability,
                     PlayerEquipment equip, PlayerEnchant enchant)
    {
        _shop = shop;
        _health = health;
        _ability = ability;
        enchantView?.Bind(enchant, equip);
    }

    protected override void OnOpened()
    {
        _shop?.SetShopping(true);
        SetTab(Tab.Main);
    }

    protected override void OnClosed()
    {
        _shop?.SetShopping(false);
    }

    void SetTab(Tab t)
    {
        _tab = t;
            
        mainPage?.SetActive(t == Tab.Main);
        enchantPage?.SetActive(t == Tab.Enchant);
        specialPage?.SetActive(t == Tab.Special);
        limitedPage?.SetActive(t == Tab.Limited);

        // 선택된 탭은 누를 수 없게 해서 상태를 드러냄
        if (mainTab != null) mainTab.interactable = t != Tab.Main;
        if (enchantTab != null) enchantTab.interactable = t != Tab.Enchant;
        if (specialTab != null) specialTab.interactable = t != Tab.Special;
        if (limitedTab != null) limitedTab.interactable = t != Tab.Limited;

        if (t == Tab.Main) RebuildMain();
        else if (t == Tab.Special) RebuildSpecial();
        else if (t == Tab.Limited) RebuildLimited();
        else enchantView?.Refresh();
    }

    /// <summary>언어 바뀜 — 열려 있으면 물품 이름 · 설명 다시 (닫혀 있으면 열 때 다시 만듦)</summary>
    public void OnLocaleChanged()
    {
        if (!IsOpen) return;
        RebuildMain();
        RebuildSpecial();
        enchantView?.Refresh();
    }

    void Update()
    {
        if (!IsOpen) return;

        if (_shop != null && /*!_shop.IsShopping &&*/ !_shop.CanOpenShop)
        {
            UIManager.Instance?.CloseAll();
            return;
        }

        if (soulValue != null && _health != null)
            soulValue.text = _health.Soul.ToString();

        // 구매 가능 여부가 실시간으로 바뀌므로 갱신
        foreach (var v in _mainViews) v.RefreshState();
        foreach (var v in _specialViews) v.RefreshState();

        if (_tab == Tab.Enchant) enchantView?.RefreshState();
        if (_tab == Tab.Limited) UpdateLimited();
    }

    // ══════════════════ 한정판 ══════════════════

    int _limitedRot = -1;

    /// <summary>예전 프리팹 — '특수' 탭 버튼 · 페이지 자리를 복사해 한정판 탭을 만듦</summary>
    void EnsureLimited()
    {
        if (limitedTab == null && specialTab != null)
        {
            limitedTab = Instantiate(specialTab, specialTab.transform.parent);
            limitedTab.name = "LimitedTab";
            limitedTab.onClick.RemoveAllListeners();
            limitedTab.transform.SetSiblingIndex(specialTab.transform.GetSiblingIndex() + 1);
            var t = limitedTab.GetComponentInChildren<TMP_Text>(true);
            if (t != null)
            {
                var lt = t.GetComponent<LocText>();
                if (lt != null) Destroy(lt);
                t.text = "한정판!";
                SetLocText.Add(t, "ui.shop.tab_limited");
            }
        }
        if (limitedView == null)
        {
            var parent = specialPage != null ? specialPage.transform.parent : transform;
            var page = new GameObject("LimitedPage", typeof(RectTransform));
            page.transform.SetParent(parent, false);
            var rt = (RectTransform)page.transform;
            if (specialPage != null)
            {
                var sp = (RectTransform)specialPage.transform;
                rt.anchorMin = sp.anchorMin;
                rt.anchorMax = sp.anchorMax;
                rt.offsetMin = sp.offsetMin;
                rt.offsetMax = sp.offsetMax;
            }
            else
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            limitedPage = page;
            limitedView = GearOfferBuilder.Build(rt, UiFont.Get());
            page.SetActive(false);
        }
        if (limitedView != null)
        {
            limitedView.CanBuy = o =>
            {
                if (_shop != null && !_shop.CanOpenShop) return (false, SetLoc.T("market.reason.combat", "전투 중에는 거래할 수 없습니다"));
                if (LimitedShop.Sold(o.Market)) return (false, SetLoc.T("shop.limited.sold", "이번 한정판에서 이미 구매했습니다"));
                bool ok = GearPurchase.CanBuy(o.Item, o.Price, out var why);
                return (ok, why);
            };
            limitedView.Buy = o =>
            {
                var src = o.Market;
                bool ok = LimitedShop.Buy(src, out var msg);
                o.Sold = LimitedShop.Sold(src);
                return (ok, msg);
            };
        }
    }

    void RebuildLimited()
    {
        if (limitedView == null) return;
        _limitedRot = LimitedShop.Rotation;
        var list = new List<GearOfferPage.Offer>();
        foreach (var o in LimitedShop.Current())
            list.Add(GearOfferPage.Offer.From(o, LimitedShop.Sold(o)));
        limitedView.SetOffers(list);
        UpdateLimited();
    }

    void UpdateLimited()
    {
        if (limitedView == null) return;
        if (LimitedShop.Rotation != _limitedRot) { RebuildLimited(); return; }     // 바뀌는 시간 — 새 물건
        int left = Mathf.CeilToInt(LimitedShop.SecondsLeft);
        limitedView.SetHeader(SetLoc.F("ui.shop.limited_left", "한정판! — 다음 교체까지 {0}:{1:00}", left / 60, left % 60));
        limitedView.Refresh();
    }

    void RebuildMain()
    {
        foreach (var v in _mainViews) Destroy(v.gameObject);
        _mainViews.Clear();

        var db = _shop?.Database;
        if (db == null) return;

        for (int i = 0; i < db.Count; i++)
        {
            var item = db.GetByIndex(i);
            if (item == null) continue;

            var view = Instantiate(itemPrefab, itemParent);
            view.Bind(_shop, i, false);
            _mainViews.Add(view);
        }
    }

    void RebuildSpecial()
    {
        foreach (var v in _specialViews) Destroy(v.gameObject);
        _specialViews.Clear();

        var items = _shop?.GetExclusiveItems();
        bool empty = items == null || items.Length == 0;    

        if (specialEmptyLabel != null)
            specialEmptyLabel.gameObject.SetActive(empty);

        if (empty) return;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null) continue;

            var view = Instantiate(specialItemPrefab, specialParent);
            view.Bind(_shop, i, true);
            _specialViews.Add(view);
        }
    }
}