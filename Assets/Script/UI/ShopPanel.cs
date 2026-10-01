using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ShopPanel : UIPanel
{
    public enum Tab { Main, Enchant, Special }

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

        // 선택된 탭은 누를 수 없게 해서 상태를 드러냄
        if (mainTab != null) mainTab.interactable = t != Tab.Main;
        if (enchantTab != null) enchantTab.interactable = t != Tab.Enchant;
        if (specialTab != null) specialTab.interactable = t != Tab.Special;

        if (t == Tab.Main) RebuildMain();
        else if (t == Tab.Special) RebuildSpecial();
        else enchantView?.Refresh();
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