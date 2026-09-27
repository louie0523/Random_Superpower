using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Localization.Settings;

public enum AbilityTab { Main, Sub, Random }

[System.Serializable]
class TabEntry
{
    public AbilityTab Tab;
    public Button Button;
    public LocText Label;
    public string LabelKey;      // 예: ui.ability.tab_main
}

public class AbilityPanel : UIPanel
{
    [Header("탭")]
    [SerializeField] private List<TabEntry> tabs = new();

    private AbilityTab _tab = AbilityTab.Main;

    [Header("헤더")]
    [SerializeField] private Image abilityIcon;
    [SerializeField] private TMP_Text abilityName;
    [SerializeField] private TMP_Text abilityFlavor;

    [Header("목록")]
    [SerializeField] private Transform entryParent;
    [SerializeField] private AbilityEntryView entryPrefab;
    [SerializeField] private ScrollRect scroll;          // 없으면 비워두세요

    [Header("닫기")]
    [SerializeField] private Button closeButton;
    [SerializeField] private LocText closeHint;

    private readonly List<AbilityEntryView> _entries = new();
    private AbilityEntryView _expanded;
    private AbilityRuntime _runtime;
    private bool _showingMain = true;

    protected override void Awake()
    {
        base.Awake();

        foreach (var t in tabs)
        {
            var captured = t.Tab;                       // 클로저 캡처 주의
            t.Button?.onClick.AddListener(() => SelectTab(captured));
            if (!string.IsNullOrEmpty(t.LabelKey)) t.Label?.SetKey(t.LabelKey);
        }

        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseAll());
        closeHint?.SetKey("ui.common.close_hint");
    }

    void OnEnable() => LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    void OnLocaleChanged(UnityEngine.Localization.Locale _) { if (IsOpen) Rebuild(); }

    public void Bind(AbilityRuntime runtime)
    {
        _runtime = runtime;
        if (IsOpen) Rebuild();               // ②
    }

    protected override void OnOpened() => SelectTab(AbilityTab.Main);

    void SelectTab(AbilityTab tab)
    {
        _tab = tab;
        UpdateTabVisual();
        Rebuild();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    void UpdateTabVisual()
    {
        foreach (var t in tabs)
            if (t.Button != null) t.Button.interactable = t.Tab != _tab;
    }

    AbilityData GetTabData() => _tab switch
    {
        AbilityTab.Main => _runtime.GetMainAbility(),
        AbilityTab.Sub => _runtime.GetSubAbility(),
        AbilityTab.Random => _runtime.GetRandomAbility(),
        _ => null,
    };

    void Rebuild()
    {
        _expanded = null;
        foreach (var e in _entries)
        {
            if (e == null) continue;
            e.gameObject.SetActive(false);   // ⑤ 같은 프레임에 즉시 사라지게
            Destroy(e.gameObject);
        }
        _entries.Clear();

        if (_runtime == null) return;

        var data = GetTabData();

        if (data == null)
        {
            if (abilityName != null) abilityName.text = LocUtil.Get("ui.ability.none");
            if (abilityFlavor != null) abilityFlavor.text = "";
            if (abilityIcon != null) abilityIcon.enabled = false;
            return;
        }

        // 헤더
        if (abilityIcon != null)
        {
            abilityIcon.enabled = data.Icon != null;
            abilityIcon.sprite = data.Icon;
        }
        if (abilityName != null)                                          // ③
            abilityName.text = LocUtil.GetOr($"ability.{data.Id}.name",
                                             LocUtil.Resolve(data.DisplayName));
        if (abilityFlavor != null)
            abilityFlavor.text = LocUtil.GetOr($"ability.{data.Id}.flavor",
                                               LocUtil.Resolve(data.Flavor));

        // 패시브
        if (data.Passives != null)
            for (int i = 0; i < data.Passives.Length; i++)
                AddEntry(BuildPassive(data, i));

        // 스킬
        if (data.Skills != null)
            for (int i = 0; i < data.Skills.Length; i++)
                AddEntry(BuildSkill(data, i));

        // 게이지
        if (data.Charges != null)
            foreach (var c in data.Charges)
                AddEntry(new AbilityEntryView.Model
                {
                    SlotLabel = LocUtil.Get("ui.ability.charge"),
                    Title = LocUtil.Resolve(c.DisplayName),
                    Body = LocUtil.Get("ui.ability.charge_max", c.Max.ToString("0")),
                });
    }

    void AddEntry(AbilityEntryView.Model m)
    {
        if (entryPrefab == null || entryParent == null) return;

        var view = Instantiate(entryPrefab, entryParent);
        view.Bind(m);
        view.Clicked += OnEntryClicked;
        _entries.Add(view);
    }

    void OnEntryClicked(AbilityEntryView v)
    {
        bool open = !v.IsExpanded;

        if (_expanded != null && _expanded != v)
            _expanded.SetExpanded(false);      // 기존 거 접기

        v.SetExpanded(open);
        _expanded = open ? v : null;
    }

    // ── 모델 만들기 ──

    AbilityEntryView.Model BuildPassive(AbilityData data, int i)
    {
        var p = data.Passives[i];
        string prefix = $"ability.{data.Id}.passive.{i}";

        int rank = _runtime != null ? _runtime.SkillRank : 0;
        object[] args = BuildArgs(p.Effects, rank);

        return new AbilityEntryView.Model
        {
            Icon = p.Icon,
            SlotLabel = LocUtil.Get("ui.ability.passive"),
            Title = LocUtil.GetOr($"{prefix}.name", LocUtil.Resolve(p.Name)),        // ④
            Body = Format(LocUtil.GetOr($"{prefix}.desc",
                                             LocUtil.Resolve(p.Description)), args),
            Detail = BuildPassiveDetail(data, i, args),
        };
    }

    AbilityEntryView.Model BuildSkill(AbilityData data, int i)
    {
        var s = data.Skills[i];
        string prefix = $"ability.{data.Id}.skill.{i}";

        int rank = _runtime != null ? _runtime.SkillRank : 0;
        object[] args = BuildArgs(s.Effects, rank);

        return new AbilityEntryView.Model
        {
            Icon = s.Icon,
            SlotLabel = s.Slot.Label(),
            Title = LocUtil.GetOr($"{prefix}.name", LocUtil.Resolve(s.Name)),
            Body = Format(LocUtil.GetOr($"{prefix}.desc",
                                             LocUtil.Resolve(s.Description)), args),
            Detail = BuildSkillDetail(data, i, args),
        };
    }

    static object[] BuildArgs(AbilityData.EffectDef[] effects, int rank)
    {
        if (effects == null) return System.Array.Empty<object>();

        var args = new object[effects.Length];
        for (int i = 0; i < effects.Length; i++)
            args[i] = effects[i].GetValue(rank);
        return args;
    }

    static string Format(string s, object[] args)
    {
        if (string.IsNullOrEmpty(s) || args == null || args.Length == 0) return s;
        try { return string.Format(s, args); }
        catch (System.FormatException e)
        {
#if UNITY_EDITOR
            // 번역문의 {N} 이 인자 개수를 넘으면 조용히 원문이 나옴 — 에디터에선 알려줌
            Debug.LogWarning($"[AbilityPanel] 서식 인자 불일치 ({args.Length}개): \"{s}\" — {e.Message}");
#endif
            return s;
        }
    }

    string BuildSkillDetail(AbilityData data, int i, object[] args)
    {
        var s = data.Skills[i];
        string prefix = $"ability.{data.Id}.skill.{i}";
        var sb = new System.Text.StringBuilder();

        if (s.Cooldown > 0f)
            sb.AppendLine(LocUtil.Get("ui.ability.cooldown", s.Cooldown.ToString("0.#")));

        if (s.SoulCost > 0)
            sb.AppendLine(LocUtil.Get("ui.ability.soul_cost", s.SoulCost));

        if (s.UltimateChargeCost > 0)
            sb.AppendLine(LocUtil.Get("ui.ability.ult_cost", s.UltimateChargeCost));

        if (s.NeedsTarget)
        {
            string shape = s.HitMode == WeaponHitMode.ForwardArc
                ? LocUtil.Get("ui.ability.shape_arc", s.HitAngle.ToString("0"))
                : LocUtil.Get("ui.ability.shape_sphere");

            sb.AppendLine(LocUtil.Get("ui.ability.range_line",
                shape, s.Range.ToString("0.#"), s.MaxTargets));
        }
        else
        {
            sb.AppendLine(LocUtil.Get("ui.ability.self_target"));
        }

        if (s.RequiredWeapon != WeaponRequirement.None)
            sb.AppendLine(LocUtil.Get("ui.ability.weapon_req",
                LocUtil.Get($"ui.weapon_req.{s.RequiredWeapon}")));

        string info = LocUtil.GetOr($"{prefix}.info", LocUtil.Resolve(s.DetailedInfo));
        if (!string.IsNullOrWhiteSpace(info) && !info.StartsWith("#"))
        {
            sb.AppendLine();
            sb.AppendLine(Format(info, args));
        }

        string tip = LocUtil.GetOr($"{prefix}.tip", LocUtil.Resolve(s.Tip));
        if (!string.IsNullOrWhiteSpace(tip) && !tip.StartsWith("#"))
        {
            sb.AppendLine();
            sb.AppendLine(LocUtil.Get("ui.ability.tip_prefix") + " " + Format(tip, args));
        }

        return sb.ToString().TrimEnd();
    }

    string BuildPassiveDetail(AbilityData data, int i, object[] args)
    {
        var p = data.Passives[i];
        string info = LocUtil.GetOr($"ability.{data.Id}.passive.{i}.info",
                                    LocUtil.Resolve(p.DetailedInfo));

        if (string.IsNullOrWhiteSpace(info) || info.StartsWith("#")) return null;
        return Format(info, args);
    }
}