using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EnchantPage : MonoBehaviour
{
    [Header("스톤")]
    [SerializeField] private LocText stoneLabel;
    [SerializeField] private TMP_Text stoneValue;

    [Header("대상 무기")]
    [SerializeField] private Image weaponIcon;
    [SerializeField] private TMP_Text weaponName;
    [SerializeField] private TMP_Text stageText;

    [Header("목록")]
    [SerializeField] private Transform rowParent;
    [SerializeField] private EnchantRowView rowPrefab;
    [SerializeField] private LocText emptyLabel;

    [Header("실행")]
    [SerializeField] private Button enchantButton;
    [SerializeField] private LocText enchantLabel;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text reasonText;

    private PlayerEnchant _enchant;
    private PlayerEquipment _equip;

    private readonly List<EnchantRowView> _rows = new();

    // 목록을 매 프레임 다시 만들지 않기 위한 캐시
    private int _shownStage = -1;
    private int _shownWeaponId = -1;
    private int _shownRollTick = -1;

    void Awake()
    {
        enchantButton?.onClick.AddListener(OnEnchant);

        stoneLabel?.SetKey("ui.enchant.stones");
        enchantLabel?.SetKey("ui.enchant.do");
        emptyLabel?.SetKey("ui.enchant.empty");
    }

    public void Bind(PlayerEnchant enchant, PlayerEquipment equip)
    {
        _enchant = enchant;
        _equip = equip;
        Invalidate();
    }

    /// <summary>탭 진입 시 — 강제로 다시 그림</summary>
    public void Refresh()
    {
        Invalidate();
        RefreshState();
    }

    void Invalidate()
    {
        _shownStage = -1;
        _shownWeaponId = -1;
        _shownRollTick = -1;
    }

    /// <summary>매 프레임 — ShopPanel.Update 가 호출</summary>
    public void RefreshState()
    {
        if (_enchant == null || _equip == null) return;

        if (stoneValue != null) stoneValue.text = _enchant.Stones.ToString();

        var w = _equip.GetWeapon();
        int slot = _equip.ActiveWeaponSlot;
        int stage = _enchant.GetStage(slot);
        int wid = w != null ? w.Id : 0;

        // ── 대상 무기 ──
        if (weaponIcon != null)
        {
            weaponIcon.enabled = w != null && w.Icon != null;
            if (w != null) weaponIcon.sprite = w.Icon;
        }

        if (weaponName != null)
            weaponName.text = w != null
                ? LocUtil.GetOr($"weapon.{w.Id}.name", w.DisplayName)
                : LocUtil.Get("ui.enchant.no_weapon");

        if (stageText != null)
        {
            bool show = w != null && w.CanEnchant;
            stageText.gameObject.SetActive(show);
            if (show)
            {
                int max = Mathf.Min(w.MaxEnchantStage, PlayerEnchant.MaxEnchants);
                stageText.text = $"{stage} / {max}";
            }
        }

        // ── 목록 (바뀔 때만) ──
        if (stage != _shownStage || wid != _shownWeaponId
            || _enchant.LastRollTick != _shownRollTick)
        {
            _shownStage = stage;
            _shownWeaponId = wid;
            _shownRollTick = _enchant.LastRollTick;

            RebuildRows(slot, stage);
        }

        // ── 버튼 ──
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

    void RebuildRows(int slot, int stage)
    {
        foreach (var r in _rows)
        {
            if (r == null) continue;
            r.gameObject.SetActive(false);
            Destroy(r.gameObject);
        }
        _rows.Clear();

        if (emptyLabel != null) emptyLabel.gameObject.SetActive(stage == 0);
        if (rowPrefab == null || rowParent == null) return;

        var db = _enchant.Database;
        int newest = _enchant.LastRolled;

        for (int i = 0; i < stage; i++)
        {
            int id = _enchant.GetEnchantId(slot, i);
            if (id == 0) continue;

            var data = db != null ? db.Get(id) : null;
            if (data == null) continue;

            var row = Instantiate(rowPrefab, rowParent);
            row.Bind(i, data, id == newest && i == stage - 1);
            _rows.Add(row);
        }
    }

    void OnEnchant() => _enchant?.RPC_Enchant();
}