using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Localization.Settings;

public class AbilityPanel : UIPanel
{
    [Header("헤더")]
    [SerializeField] private Image abilityIcon;
    [SerializeField] private TMP_Text abilityName;
    [SerializeField] private TMP_Text abilityFlavor;

    [Header("열 — 목록 컨테이너")]
    [SerializeField] private Transform passiveParent;
    [SerializeField] private Transform skillParent;
    [SerializeField] private Transform specialParent;
    [Tooltip("비워두면 게이지 항목이 패시브 열로 들어감")]
    [SerializeField] private Transform chargeParent;

    [Header("열 — 라벨")]
    [SerializeField] private LocText passiveLabel;
    [SerializeField] private LocText skillLabel;
    [SerializeField] private LocText specialLabel;

    [Header("열 — 비었을 때")]
    [SerializeField] private GameObject passiveEmpty;
    [SerializeField] private GameObject skillEmpty;
    [SerializeField] private GameObject specialEmpty;

    [Header("카드")]
    [SerializeField] private AbilityEntryView entryPrefab;

    [Header("출처 색")]
    [SerializeField] private Color mainAccent = new(0f, 0f, 0f, 0f);
    [SerializeField] private Color subAccent = new(0.38f, 0.75f, 1f, 1f);
    [SerializeField] private Color randomAccent = new(1f, 0.78f, 0.2f, 1f);

    [Header("분류 방식")]
    [Tooltip("켜면 서브·랜덤이 전부 특수 열로. 끄면 종류대로 나뉘고 색으로만 구분")]
    [SerializeField] private bool specialColumnBySource = true;

    [Header("닫기")]
    [SerializeField] private Button closeButton;
    [SerializeField] private LocText closeHint;

    private readonly List<AbilityEntryView> _entries = new();
    private AbilityRuntime _runtime;
    private AbilityEntryView _selectedEntry;

    private int _passiveCount, _skillCount, _specialCount;

    // ══════════════════ 생명주기 ══════════════════

    protected override void Awake()
    {
        base.Awake();

        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseAll());

        closeHint?.SetKey("ui.common.close_hint");
        passiveLabel?.SetKey("ui.ability.passive");
        skillLabel?.SetKey("ui.ability.skill");
        specialLabel?.SetKey("ui.ability.special");
    }

    void OnEnable() => LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    void OnLocaleChanged(UnityEngine.Localization.Locale _) { if (IsOpen) Rebuild(); }

    public void Bind(AbilityRuntime runtime)
    {
        _runtime = runtime;
        if (IsOpen) Rebuild();
    }

    protected override void OnOpened() => Rebuild();

    // ══════════════════ 재구성 ══════════════════

    void Rebuild()
    {
        UIManager.Instance?.Detail?.Hide();
        _selectedEntry = null;

        foreach (var e in _entries)
        {
            if (e == null) continue;
            e.gameObject.SetActive(false);
            Destroy(e.gameObject);
        }
        _entries.Clear();
        _passiveCount = _skillCount = _specialCount = 0;

        if (_runtime == null) { FillHeader(null); UpdateEmptyStates(); return; }

        var main = _runtime.GetMainAbility();
        FillHeader(main);

        AddFrom(main, mainAccent, false);
        AddFrom(_runtime.GetSubAbility(), subAccent, specialColumnBySource);
        AddFrom(_runtime.GetRandomAbility(), randomAccent, specialColumnBySource);

        AddCharges(main);
        UpdateEmptyStates();
    }

    void FillHeader(AbilityData data)
    {
        if (data == null)
        {
            if (abilityName != null) abilityName.text = LocUtil.Get("ui.ability.none");
            if (abilityFlavor != null) abilityFlavor.text = "";
            if (abilityIcon != null) abilityIcon.enabled = false;
            return;
        }

        if (abilityIcon != null)
        {
            abilityIcon.enabled = data.Icon != null;
            abilityIcon.sprite = data.Icon;
        }

        if (abilityName != null)
            abilityName.text = LocUtil.GetOr($"ability.{data.Id}.name",
                                             LocUtil.Resolve(data.DisplayName));

        if (abilityFlavor != null)
            abilityFlavor.text = LocUtil.GetOr($"ability.{data.Id}.flavor",
                                               LocUtil.Resolve(data.Flavor));
    }

    /// <param name="forceSpecial">true면 종류와 무관하게 특수 열로 보냄</param>
    void AddFrom(AbilityData data, Color accent, bool forceSpecial)
    {
        if (data == null) return;

        if (data.Passives != null)
            for (int i = 0; i < data.Passives.Length; i++)
                Place(forceSpecial ? Col.Special : Col.Passive,
                      BuildPassive(data, i, accent));

        if (data.Skills != null)
            for (int i = 0; i < data.Skills.Length; i++)
                Place(forceSpecial ? Col.Special : Col.Skill,
                      BuildSkill(data, i, accent));
    }

    void AddCharges(AbilityData data)
    {
        if (data?.Charges == null) return;

        foreach (var c in data.Charges)
        {
            var m = new AbilityEntryView.Model
            {
                SlotLabel = LocUtil.Get("ui.ability.charge"),
                Title = LocUtil.Resolve(c.DisplayName),
                Body = LocUtil.Get("ui.ability.charge_max", c.Max.ToString("0")),
                Accent = mainAccent,
            };

            if (chargeParent != null) Spawn(chargeParent, m);
            else Place(Col.Passive, m);
        }
    }

    // ══════════════════ 배치 ══════════════════

    enum Col { Passive, Skill, Special }

    void Place(Col col, AbilityEntryView.Model m)
    {
        switch (col)
        {
            case Col.Passive: Spawn(passiveParent, m); _passiveCount++; break;
            case Col.Skill: Spawn(skillParent, m); _skillCount++; break;
            case Col.Special: Spawn(specialParent, m); _specialCount++; break;
        }
    }

    void Spawn(Transform parent, AbilityEntryView.Model m)
    {
        if (entryPrefab == null || parent == null) return;

        var view = Instantiate(entryPrefab, parent);
        view.Bind(m);
        view.Clicked += OnEntryClicked;
        _entries.Add(view);
    }

    void UpdateEmptyStates()
    {
        if (passiveEmpty != null) passiveEmpty.SetActive(_passiveCount == 0);
        if (skillEmpty != null) skillEmpty.SetActive(_skillCount == 0);
        if (specialEmpty != null) specialEmpty.SetActive(_specialCount == 0);
    }

    // ══════════════════ 클릭 ══════════════════

    void OnEntryClicked(AbilityEntryView v)
    {
        _selectedEntry = v;

        foreach (var e in _entries)
            if (e != null) e.SetSelected(e == v);

        var d = v.Data;
        UIManager.Instance?.Detail?.Show(new DetailWindow.Content
        {
            Icon = d.Icon,
            Title = d.Title,
            Subtitle = d.SlotLabel,
            Body = d.Detail,
        });
    }

    // ══════════════════ 모델 만들기 ══════════════════

    int Rank => _runtime != null ? _runtime.SkillRank : 0;

    AbilityEntryView.Model BuildPassive(AbilityData data, int i, Color accent)
    {
        var p = data.Passives[i];
        string prefix = $"ability.{data.Id}.passive.{i}";
        object[] args = BuildArgs(p.Effects, Rank);

        return new AbilityEntryView.Model
        {
            Icon = p.Icon,
            SlotLabel = LocUtil.Get("ui.ability.passive"),
            Title = LocUtil.GetOr($"{prefix}.name", LocUtil.Resolve(p.Name)),
            Body = Format(LocUtil.GetOr($"{prefix}.desc",
                                             LocUtil.Resolve(p.Description)), args),
            Detail = BuildPassiveDetail(data, i, args),
            Accent = accent,
        };
    }

    AbilityEntryView.Model BuildSkill(AbilityData data, int i, Color accent)
    {
        var s = data.Skills[i];
        string prefix = $"ability.{data.Id}.skill.{i}";
        object[] args = BuildArgs(s.Effects, Rank);

        return new AbilityEntryView.Model
        {
            Icon = s.Icon,
            SlotLabel = s.Slot.Label(),
            Title = LocUtil.GetOr($"{prefix}.name", LocUtil.Resolve(s.Name)),
            Body = Format(LocUtil.GetOr($"{prefix}.desc",
                                             LocUtil.Resolve(s.Description)), args),
            Detail = BuildSkillDetail(data, i, args),
            Accent = accent,
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
        string info = LocUtil.GetOr($"{prefix}.info", LocUtil.Resolve(s.DetailedInfo));
        if (!string.IsNullOrWhiteSpace(info) && !info.StartsWith("#"))
        {
            sb.AppendLine();
            sb.AppendLine(Format(info, args));
        }

        // 팁
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