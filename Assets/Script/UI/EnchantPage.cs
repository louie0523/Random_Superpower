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

    [Header("목록 — 특수(위) · 일반(같은 것끼리 묶음)")]
    [SerializeField] private Transform rowParent;
    [SerializeField] private EnchantRowView rowPrefab;
    [SerializeField] private LocText emptyLabel;

    [Header("실행")]
    [SerializeField] private Button enchantButton;
    [SerializeField] private LocText enchantLabel;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text reasonText;

    [Header("특수 단계 선택")]
    [Tooltip("선택지가 있을 때만 켜짐. 이때 실행·지정 버튼은 숨겨짐")]
    [SerializeField] private GameObject specialChoiceRoot;
    [SerializeField] private Transform specialChoiceParent;
    [SerializeField] private SpecialChoiceView specialChoicePrefab;
    [SerializeField] private LocText specialChoiceTitle;
    [SerializeField] private Button normalInsteadButton;
    [SerializeField] private LocText normalInsteadLabel;

    [Header("대장장이 — 다음 인챈트 지정")]
    [Tooltip("대장장이일 때만 보임")]
    [SerializeField] private Button pickButton;
    [SerializeField] private LocText pickLabel;
    [Tooltip("지정 목록 창")]
    [SerializeField] private GameObject pickRoot;
    [SerializeField] private LocText pickTitle;
    [SerializeField] private Transform pickParent;
    [SerializeField] private PickEntryView pickEntryPrefab;
    [Tooltip("'특수 인챈트' / '일반 인챈트' 구역 제목")]
    [SerializeField] private TMP_Text pickHeaderPrefab;
    [SerializeField] private Button pickCloseButton;

    private PlayerEnchant _enchant;
    private PlayerEquipment _equip;
    private PlayerHealth _health;

    private readonly List<EnchantRowView> _rows = new();
    private readonly List<EnchantSummary.Group> _groups = new();
    private readonly List<SpecialChoiceView> _choices = new();
    private readonly List<GameObject> _pickItems = new();
    private readonly List<EnchantData> _normals = new();

    private int _shownStage = -1;
    private int _shownWeaponId = -1;
    private int _shownRollTick = -1;
    private int _shownPendingSig;
    private string _shownEnchantKey;

    private bool _pickOpen;
    private int _pickStage, _pickWeapon, _pickSoul;

    void Awake()
    {
        enchantButton?.onClick.AddListener(OnEnchant);
        normalInsteadButton?.onClick.AddListener(() => _enchant?.RPC_ChooseNormal());
        pickButton?.onClick.AddListener(OpenPick);
        pickCloseButton?.onClick.AddListener(ClosePick);

        stoneLabel?.SetKey("ui.enchant.stones");
        emptyLabel?.SetKey("ui.enchant.empty");
        specialChoiceTitle?.SetKey("ui.enchant.special_title");
        normalInsteadLabel?.SetKey("ui.enchant.normal_instead");
        pickLabel?.SetKey("ui.enchant.pick");
        pickTitle?.SetKey("ui.enchant.pick_title");

        if (specialChoiceRoot != null) specialChoiceRoot.SetActive(false);
        if (pickRoot != null) pickRoot.SetActive(false);
    }

    void OnDisable() => ClosePick();

    public void Bind(PlayerEnchant enchant, PlayerEquipment equip)
    {
        _enchant = enchant;
        _equip = equip;
        _health = enchant != null ? enchant.GetComponent<PlayerHealth>() : null;
        Invalidate();
    }

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
        _shownPendingSig = 0;
        _shownEnchantKey = null;
        ClosePick();
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
                ? LocUtil.GetOr($"weapon.{w.Id}.name", LocUtil.Resolve(w.DisplayName))
                : LocUtil.Get("ui.enchant.no_weapon");

        if (stageText != null)
        {
            int max = _enchant.GetMaxStage(w);
            bool show = max > 0;
            stageText.gameObject.SetActive(show);
            if (show) stageText.text = $"{stage} / {max}";
        }

        // ── 목록 ──
        if (stage != _shownStage || wid != _shownWeaponId || _enchant.LastRollTick != _shownRollTick)
        {
            _shownStage = stage;
            _shownWeaponId = wid;
            _shownRollTick = _enchant.LastRollTick;
            RebuildRows(slot, stage);
        }

        // ── 특수 선택 대기 ──
        bool pending = _enchant.HasPending(slot);

        if (specialChoiceRoot != null) specialChoiceRoot.SetActive(pending);
        if (enchantButton != null) enchantButton.gameObject.SetActive(!pending);
        if (pickButton != null) pickButton.gameObject.SetActive(!pending && _enchant.UnlimitedStage);

        if (pending)
        {
            ClosePick();

            int sig = PendingSig();
            if (sig != _shownPendingSig)
            {
                _shownPendingSig = sig;
                RebuildChoices(slot);
            }

            if (costText != null) costText.gameObject.SetActive(false);
            if (reasonText != null) reasonText.gameObject.SetActive(false);
            return;
        }
        _shownPendingSig = 0;

        // ── 실행 버튼 ──
        string key = _enchant.NextIsSpecial(slot) ? "ui.enchant.do_special" : "ui.enchant.do";
        if (key != _shownEnchantKey)
        {
            _shownEnchantKey = key;
            enchantLabel?.SetKey(key);
        }

        bool ok = _enchant.CanEnchant(out string reason, out int cost);

        if (enchantButton != null) enchantButton.interactable = ok;
        if (pickButton != null) pickButton.interactable = ok;

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

        // ── 지정 창 — 단계·무기가 바뀌거나 지정 불가면 닫고, 소울이 바뀌면 다시 그림 ──
        if (_pickOpen)
        {
            if (!ok || !_enchant.UnlimitedStage || stage != _pickStage || wid != _pickWeapon) ClosePick();
            else if (_health != null && _health.Soul != _pickSoul) BuildPickList();
        }
    }

    int PendingSig()
    {
        unchecked
        {
            int h = 17;
            for (int i = 0; i < PlayerEnchant.MaxChoices; i++) h = h * 31 + _enchant.GetPending(i);
            return h;
        }
    }

    // ══════════════════ 목록 ══════════════════

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

        EnchantSummary.Build(_enchant, slot, _groups);

        foreach (var g in _groups)
        {
            var row = Instantiate(rowPrefab, rowParent);
            row.Bind(g);
            _rows.Add(row);
        }
    }

    // ══════════════════ 특수 선택지 ══════════════════

    void RebuildChoices(int slot)
    {
        foreach (var c in _choices)
            if (c != null) Destroy(c.gameObject);
        _choices.Clear();

        if (specialChoicePrefab == null || specialChoiceParent == null) return;

        var db = _enchant.Database;
        if (db == null) return;

        for (int i = 0; i < PlayerEnchant.MaxChoices; i++)
        {
            int code = _enchant.GetPending(i);
            if (code == 0) continue;

            var data = db.GetSpecial(EnchantCode.SpecialId(code));
            if (data == null) continue;

            int tier = EnchantCode.Tier(code);
            bool upgrade = _enchant.OwnedTier(slot, data.Id) > 0;
            int index = i;

            var view = Instantiate(specialChoicePrefab, specialChoiceParent);
            view.Bind(data, tier, upgrade, () => _enchant.RPC_ChooseSpecial(index));
            _choices.Add(view);
        }
    }

    // ══════════════════ 대장장이 지정 ══════════════════

    void OpenPick()
    {
        if (pickRoot == null || _enchant == null || !_enchant.UnlimitedStage) return;

        _pickOpen = true;
        _pickStage = _enchant.GetStage(_equip.ActiveWeaponSlot);
        _pickWeapon = _equip.GetWeapon()?.Id ?? 0;

        BuildPickList();
        pickRoot.SetActive(true);
    }

    void ClosePick()
    {
        _pickOpen = false;
        ClearPickItems();
        if (pickRoot != null) pickRoot.SetActive(false);
    }

    void ClearPickItems()
    {
        foreach (var go in _pickItems)
            if (go != null) Destroy(go);
        _pickItems.Clear();
    }

    void BuildPickList()
    {
        ClearPickItems();
        if (pickEntryPrefab == null || pickParent == null) return;

        int slot = _equip.ActiveWeaponSlot;
        var t = _equip.GetWeapon()?.EnchantTable;
        if (t == null) return;

        int soul = _health != null ? _health.Soul : 0;
        _pickSoul = soul;

        // ── 특수 — 새 종류는 모든 등급, 가진 종류는 지금보다 높은 등급만 ──
        if (_enchant.NextIsSpecial(slot))
        {
            AddHeader("ui.enchant.pick_special");

            foreach (var c in t.SpecialCandidates)
            {
                var s = c?.Special;
                if (s == null || c.Weight <= 0f) continue;

                int owned = _enchant.OwnedTier(slot, s.Id);
                for (int tier = owned + 1; tier <= s.MaxTier; tier++)
                {
                    int code = EnchantCode.Make(s.Id, tier);
                    int cost = _enchant.GetPickSoulCost(slot, code);
                    if (cost < 0) continue;

                    AddEntry(s.Icon, s.Name(tier), s.Color, s.Describe(tier),
                             cost, soul >= cost, owned > 0, code);
                }
            }
        }

        // ── 일반 — 다음 단계 구간의 후보 ──
        t.GetNormalCandidates(_enchant.GetStage(slot) + 1, _normals);
        if (_normals.Count > 0)
        {
            AddHeader("ui.enchant.pick_normal");

            foreach (var d in _normals)
            {
                int cost = _enchant.GetPickSoulCost(slot, d.Id);
                if (cost < 0) continue;

                AddEntry(null, EnchantSummary.Name(d), d.TierColor, StatLine(d),
                         cost, soul >= cost, false, d.Id);
            }
        }
    }

    void AddHeader(string key)
    {
        if (pickHeaderPrefab == null) return;
        var h = Instantiate(pickHeaderPrefab, pickParent);
        h.text = LocUtil.Get(key);
        _pickItems.Add(h.gameObject);
    }

    void AddEntry(Sprite icon, string title, Color color, string desc,
                  int cost, bool affordable, bool upgrade, int code)
    {
        var view = Instantiate(pickEntryPrefab, pickParent);
        view.Bind(icon, title, color, desc, cost, affordable, upgrade, () =>
        {
            _enchant.RPC_PickEnchant(code);
            ClosePick();
        });
        _pickItems.Add(view.gameObject);
    }

    static string StatLine(EnchantData d)
    {
        if (d.Stats == null) return "";

        var sb = new System.Text.StringBuilder();
        foreach (var s in d.Stats)
        {
            if (sb.Length > 0) sb.Append("   ");
            sb.Append(LocUtil.Get($"stat.{s.Stat}")).Append(' ')
              .Append(StatText.Value(s.Stat, s.Type, s.Value));
        }
        return sb.ToString();
    }

    void OnEnchant() => _enchant?.RPC_Enchant();
}