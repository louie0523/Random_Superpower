using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class AbilityPanel : UIPanel
{
    [Header("탭")]
    [SerializeField] private Button mainTabButton;
    [SerializeField] private Button randomTabButton;
    [SerializeField] private LocText mainTabLabel;
    [SerializeField] private LocText randomTabLabel;

    [Header("헤더")]
    [SerializeField] private Image abilityIcon;
    [SerializeField] private TMP_Text abilityName;
    [SerializeField] private TMP_Text abilityFlavor;

    [Header("목록")]
    [SerializeField] private Transform entryParent;
    [SerializeField] private AbilityEntryView entryPrefab;

    [Header("닫기")]
    [SerializeField] private Button closeButton;
    [SerializeField] private LocText closeHint;

    private readonly List<AbilityEntryView> _entries = new();
    private AbilityRuntime _runtime;
    private bool _showingMain = true;

    protected override void Awake()
    {
        base.Awake();

        mainTabButton?.onClick.AddListener(() => { _showingMain = true; Rebuild(); });
        randomTabButton?.onClick.AddListener(() => { _showingMain = false; Rebuild(); });
        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseAll());

        mainTabLabel?.SetKey("ui.ability.tab_main");
        randomTabLabel?.SetKey("ui.ability.tab_random");
        closeHint?.SetKey("ui.common.close_hint");
    }

    public void Bind(AbilityRuntime runtime) => _runtime = runtime;

    protected override void OnOpened()
    {
        _showingMain = true;
        Rebuild();
    }

    void Rebuild()
    {
        foreach (var e in _entries) Destroy(e.gameObject);
        _entries.Clear();

        if (_runtime == null) return;

        var data = _showingMain ? _runtime.GetMainAbility() : _runtime.GetRandomAbility();

        if (data == null)
        {
            if (abilityName != null)
                abilityName.text = LocUtil.Get("ui.ability.none");
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

        if (abilityName != null)
            abilityName.text = LocUtil.GetOr($"ability.{data.Id}.name", data.DisplayName);

        if (abilityFlavor != null)
            abilityFlavor.text = LocUtil.GetOr($"ability.{data.Id}.flavor", data.Flavor);

        // 패시브
        if (data.Passives != null)
            for (int i = 0; i < data.Passives.Length; i++)
                AddEntry(BuildPassive(data, i));

        // 스킬
        if (data.Skills != null)
            for (int i = 0; i < data.Skills.Length; i++)
                AddEntry(BuildSkill(data, i));
    }

    void AddEntry(AbilityEntryView.Model m)
    {
        var view = Instantiate(entryPrefab, entryParent);
        view.Bind(m);
        _entries.Add(view);
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
            Title = LocUtil.GetOr($"{prefix}.name", p.Name),
            Body = Format(LocUtil.GetOr($"{prefix}.desc", p.Description), args),
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
            Title = LocUtil.GetOr($"{prefix}.name", s.Name),
            Body = Format(LocUtil.GetOr($"{prefix}.desc", s.Description), args),
            Detail = BuildSkillDetail(data, i, args),
        };
    }

    static object[] BuildArgs(AbilityData.EffectDef[] effects, int rank)
    {
        if (effects == null) return new object[0];

        var args = new object[effects.Length];
        for (int i = 0; i < effects.Length; i++)
            args[i] = effects[i].GetValue(rank);
        return args;
    }

    static string Format(string s, object[] args)
    {
        if (string.IsNullOrEmpty(s) || args == null || args.Length == 0) return s;
        try { return string.Format(s, args); }
        catch { return s; }
    }

    string BuildSkillDetail(AbilityData data, int i, object[] args)
    {
        var s = data.Skills[i];
        string prefix = $"ability.{data.Id}.skill.{i}";
        var sb = new System.Text.StringBuilder();

        // 코스트 / 쿨타임
        if (s.Cooldown > 0f)
            sb.AppendLine(LocUtil.Get("ui.ability.cooldown", s.Cooldown.ToString("0.#")));

        if (s.SoulCost > 0)
            sb.AppendLine(LocUtil.Get("ui.ability.soul_cost", s.SoulCost));

        if (s.UltimateChargeCost > 0)
            sb.AppendLine(LocUtil.Get("ui.ability.ult_cost", s.UltimateChargeCost));

        // 판정 범위
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

        // 무기 조건
        if (s.RequiredWeapon != WeaponRequirement.None)
            sb.AppendLine(LocUtil.Get("ui.ability.weapon_req",
                LocUtil.Get($"ui.weapon_req.{s.RequiredWeapon}")));

        // 상세 설명
        string info = LocUtil.GetOr($"{prefix}.info", s.DetailedInfo);
        if (!string.IsNullOrWhiteSpace(info) && !info.StartsWith("#"))
        {
            sb.AppendLine();
            sb.AppendLine(Format(info, args));
        }

        // 팁
        string tip = LocUtil.GetOr($"{prefix}.tip", s.Tip);
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
        string info = LocUtil.GetOr($"ability.{data.Id}.passive.{i}.info", p.DetailedInfo);

        if (string.IsNullOrWhiteSpace(info) || info.StartsWith("#")) return null;
        return Format(info, args);
    }
}