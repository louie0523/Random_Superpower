using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class EnchantPage : MonoBehaviour
{
    [Header("무기")]
    [SerializeField] private Image weaponIcon;
    [SerializeField] private TMP_Text weaponName;
    [SerializeField] private TMP_Text stageText;

    [Header("스톤")]
    [SerializeField] private LocText stoneLabel;
    [SerializeField] private TMP_Text stoneValue;

    [Header("옵션 목록")]
    [SerializeField] private Transform slotParent;
    [SerializeField] private EnchantSlotView slotPrefab;

    [Header("버튼")]
    [SerializeField] private Button enchantButton;
    [SerializeField] private LocText enchantLabel;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text reasonText;

    private readonly List<EnchantSlotView> _slots = new();
    private PlayerEnchant _enchant;
    private PlayerEquipment _equip;
    private int _builtStage = -1;
    private int _builtWeaponId = -1;

    void Awake()
    {
        stoneLabel?.SetKey("ui.enchant.stones");
        enchantLabel?.SetKey("ui.enchant.button");
        enchantButton?.onClick.AddListener(() => _enchant?.RPC_Enchant());
    }

    public void Bind(PlayerEnchant enchant, PlayerEquipment equip)
    {
        _enchant = enchant;
        _equip = equip;
    }

    public void Refresh()
    {
        _builtStage = -1;       // 강제 재구성
        RefreshState();
    }

    public void RefreshState()
    {
        if (_enchant == null || _equip == null) return;

        var w = _equip.GetWeapon();

        // 헤더
        if (weaponIcon != null)
        {
            weaponIcon.enabled = w?.Icon != null;
            weaponIcon.sprite = w?.Icon;
        }

        if (weaponName != null)
            weaponName.text = w != null
                ? LocUtil.GetOr($"weapon.{w.Id}.name", w.DisplayName)
                : LocUtil.Get("ui.enchant.no_weapon");

        if (stoneValue != null) stoneValue.text = _enchant.Stones.ToString();

        int stage = _enchant.ActiveStage;
        int max = w != null ? Mathf.Min(w.MaxEnchantStage, PlayerEnchant.MaxEnchants) : 0;

        if (stageText != null) stageText.text = $"{stage} / {max}";

        // 슬롯 재구성 (무기나 단계가 바뀔 때만)
        int wid = w != null ? w.Id : 0;
        if (_builtStage != stage || _builtWeaponId != wid)
        {
            RebuildSlots(max, stage);
            _builtStage = stage;
            _builtWeaponId = wid;
        }

        // 버튼
        bool ok = _enchant.CanEnchant(out string reason, out int cost);

        if (enchantButton != null) enchantButton.interactable = ok;

        if (costText != null)
        {
            costText.gameObject.SetActive(ok);
            if (ok) costText.text = cost.ToString();
        }

        if (reasonText != null)
        {
            reasonText.gameObject.SetActive(!ok);
            if (!ok) reasonText.text = LocUtil.Get(reason);
        }
    }

    void RebuildSlots(int max, int stage)
    {
        foreach (var s in _slots) Destroy(s.gameObject);
        _slots.Clear();

        if (_enchant == null || _equip == null) return;

        int slot = _equip.ActiveWeaponSlot;
        var db = _enchant.Database;

        for (int i = 0; i < max; i++)
        {
            var view = Instantiate(slotPrefab, slotParent);

            int id = _enchant.GetEnchantId(slot, i);
            view.Bind(i + 1, id != 0 ? db?.Get(id) : null);

            _slots.Add(view);
        }
    }
}