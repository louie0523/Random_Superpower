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

    [Header("레벨")]
    [Tooltip("남은 스킬 포인트 표시. 0이면 숨김")]
    [SerializeField] private TMP_Text pointsText;

    [Header("열 — 목록 컨테이너")]
    [SerializeField] private Transform passiveParent;
    [SerializeField] private Transform skillParent;

    [Header("열 — 라벨")]
    [SerializeField] private LocText passiveLabel;
    [SerializeField] private LocText skillLabel;

    [Header("열 — 비었을 때")]
    [SerializeField] private GameObject passiveEmpty;
    [SerializeField] private GameObject skillEmpty;

    [Header("카드")]
    [SerializeField] private AbilityEntryView entryPrefab;

    [Header("출처 색 (카드 왼쪽 띠)")]
    [SerializeField] private Color mainAccent = new(0f, 0f, 0f, 0f);
    [SerializeField] private Color subAccent = new(0.38f, 0.75f, 1f, 1f);
    [SerializeField] private Color randomAccent = new(1f, 0.78f, 0.2f, 1f);

    [Header("닫기")]
    [SerializeField] private Button closeButton;
    [SerializeField] private LocText closeHint;

    private (int, int, int, int) _shownIds;

    /// <summary>카드 하나가 어떤 항목인지</summary>
    struct Card
    {
        public AbilityEntryView View;
        public AbilityData Data;
        public int Owner;       // AbilityRuntime.OwnerMain / OwnerSub, 레벨 없음(랜덤)은 -1
        public bool IsSkill;
        public int Index;       // Passives 또는 Skills 배열 안의 번호
        public int Local;       // 레벨 저장용 항목 번호 (패시브 먼저, 이어서 스킬)
        public Color Accent;
    }

    private readonly List<Card> _cards = new();
    private AbilityRuntime _runtime;
    private AbilityEntryView _selectedEntry;

    private int _passiveCount, _skillCount;
    private int _rankSig;

    // ══════════════════ 생명주기 ══════════════════

    protected override void Awake()
    {
        base.Awake();

        closeButton?.onClick.AddListener(() => UIManager.Instance?.CloseAll());

        closeHint?.SetKey("ui.common.close_hint");
        passiveLabel?.SetKey("ui.ability.passive");
        skillLabel?.SetKey("ui.ability.skill");
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

    void Update()
    {
        if (!IsOpen || _runtime == null) return;

        // 능력 자체가 바뀌었으면 (사망 리롤·진화) 전부 다시
        var ids = (_runtime.MainAbilityId, _runtime.SubAbilityId, _runtime.RandomAbilityId, _runtime.RandomAbilityId2);
        if (ids != _shownIds) { Rebuild(); return; }

        // 레벨·포인트만 바뀌었으면 카드 내용만 갱신 (선택·상세창 유지)
        int sig = ComputeRankSig();
        if (sig != _rankSig)
        {
            _rankSig = sig;
            RefreshCards();
        }
    }

    // ══════════════════ 재구성 ══════════════════

    void Rebuild()
    {
        UIManager.Instance?.Detail?.Hide();
        _selectedEntry = null;

        foreach (var c in _cards)
        {
            if (c.View == null) continue;
            c.View.gameObject.SetActive(false);
            Destroy(c.View.gameObject);
        }
        _cards.Clear();
        _passiveCount = _skillCount = 0;

        if (_runtime == null)
        {
            FillHeader(null);
            UpdateEmptyStates();
            RefreshPoints();
            return;
        }

        _shownIds = (_runtime.MainAbilityId, _runtime.SubAbilityId, _runtime.RandomAbilityId, _runtime.RandomAbilityId2);

        var main = _runtime.GetMainAbility();
        FillHeader(main);

        // 정렬 = 추가 순서. 각 열 안에서 메인 → 서브 → 랜덤(항상 맨 아래)
        AddFrom(main, mainAccent, AbilityRuntime.OwnerMain);
        AddFrom(_runtime.GetSubAbility(), subAccent, AbilityRuntime.OwnerSub);
        AddFrom(_runtime.GetRandomAbility(), randomAccent, -1);
        AddFrom(_runtime.GetRandomAbility2(), randomAccent, -1);

        _rankSig = ComputeRankSig();
        UpdateEmptyStates();
        RefreshPoints();
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

    /// <summary>패시브는 패시브 열, 스킬은 스킬 열. 출처는 카드 띠 색으로만 구분</summary>
    void AddFrom(AbilityData data, Color accent, int owner)
    {
        if (data == null) return;

        int p = data.Passives?.Length ?? 0;

        for (int i = 0; i < p; i++)
            Spawn(passiveParent, new Card
            {
                Data = data,
                Owner = owner,
                IsSkill = false,
                Index = i,
                Local = i,
                Accent = accent,
            }, ref _passiveCount);

        if (data.Skills != null)
            for (int i = 0; i < data.Skills.Length; i++)
                Spawn(skillParent, new Card
                {
                    Data = data,
                    Owner = owner,
                    IsSkill = true,
                    Index = i,
                    Local = p + i,
                    Accent = accent,
                }, ref _skillCount);
    }

    // ══════════════════ 배치 ══════════════════

    void Spawn(Transform parent, Card c, ref int count)
    {
        if (entryPrefab == null || parent == null) return;

        c.View = Instantiate(entryPrefab, parent);
        if (!c.View.gameObject.activeSelf) c.View.gameObject.SetActive(true);   // 프리팹이 꺼진 채 저장돼 있어도

        c.View.Clicked += OnEntryClicked;
        c.View.UpgradeClicked += OnUpgradeClicked;
        _cards.Add(c);          // ★ Bind 전에 등록
        count++;

        c.View.Bind(BuildModel(c));
    }

    void UpdateEmptyStates()
    {
        if (passiveEmpty != null) passiveEmpty.SetActive(_passiveCount == 0);
        if (skillEmpty != null) skillEmpty.SetActive(_skillCount == 0);
    }

    // ══════════════════ 레벨 갱신 ══════════════════

    int ComputeRankSig()
    {
        if (_runtime == null) return 0;

        unchecked
        {
            int h = _runtime.SkillPoints * 397;
            for (int i = 0; i < _runtime.EntryRanks.Length; i++)
                h = h * 31 + _runtime.EntryRanks[i];
            return h;
        }
    }

    void RefreshCards()
    {
        var detail = UIManager.Instance?.Detail;

        foreach (var c in _cards)
        {
            if (c.View == null) continue;

            bool sel = c.View == _selectedEntry;
            c.View.Bind(BuildModel(c));
            c.View.SetSelected(sel);

            // 보고 있던 상세창도 새 수치로
            if (sel && detail != null && detail.IsOpen) ShowDetail(c.View);
        }

        RefreshPoints();
    }

    void RefreshPoints()
    {
        if (pointsText == null) return;

        int pts = _runtime != null ? _runtime.SkillPoints : 0;
        pointsText.gameObject.SetActive(pts > 0);
        if (pts > 0) pointsText.text = LocUtil.Get("ui.ability.points", pts);
    }

    // ══════════════════ 클릭 ══════════════════

    void OnEntryClicked(AbilityEntryView v)
    {
        _selectedEntry = v;

        foreach (var c in _cards)
            if (c.View != null) c.View.SetSelected(c.View == v);

        ShowDetail(v);
    }

    void ShowDetail(AbilityEntryView v)
    {
        var d = v.Data;
        UIManager.Instance?.Detail?.Show(new DetailWindow.Content
        {
            Icon = d.Icon,
            Title = d.Title,
            Subtitle = d.SlotLabel,
            Description = string.IsNullOrEmpty(d.BodyFull) ? d.Body : d.BodyFull,
            Body = d.Detail,    // 레벨·쿨타임·사거리·팁 등 추가 정보
        });
    }

    void OnUpgradeClicked(AbilityEntryView v)
    {
        if (_runtime == null) return;

        foreach (var c in _cards)
        {
            if (c.View != v) continue;
            _runtime.RequestUpgrade(c.Owner, c.Local);
            return;
        }
    }

    // ══════════════════ 모델 만들기 ══════════════════

    AbilityEntryView.Model BuildModel(Card c)
    {
        int rank = 0, max = 1;
        if (c.Owner >= 0 && _runtime != null)
        {
            rank = _runtime.GetEntryRank(c.Owner, c.Local);
            max = _runtime.GetEntryMaxLevel(c.Owner, c.Local);
        }
        bool ranked = c.Owner >= 0 && max > 1;

        var m = new AbilityEntryView.Model
        {
            Accent = c.Accent,
            ShowRank = ranked,
            Level = rank + 1,
            MaxLevel = max,
            CanUpgrade = ranked && _runtime.CanUpgrade(c.Owner, c.Local),
        };

        var data = c.Data;

        if (c.IsSkill)
        {
            var s = data.Skills[c.Index];
            string prefix = $"ability.{data.Id}.skill.{c.Index}";

            m.Icon = s.Icon;
            m.SlotLabel = s.Slot.Label();
            m.Title = LocUtil.GetOr($"{prefix}.name", LocUtil.Resolve(s.Name));
            string desc = LocUtil.GetOr($"{prefix}.desc", LocUtil.Resolve(s.Description));
            m.Body = AbilityData.FormatSkill(s, desc, rank);

            AbilityDescTokens.Detailed = true;
            try
            {
                m.BodyFull = AbilityData.FormatSkill(s, desc, rank);
                m.Detail = BuildSkillDetail(s, prefix, rank, max, ranked);
            }
            finally { AbilityDescTokens.Detailed = false; }
        }
        else
        {
            var p = data.Passives[c.Index];
            string prefix = $"ability.{data.Id}.passive.{c.Index}";

            m.Icon = p.Icon;
            m.SlotLabel = LocUtil.Get("ui.ability.passive");
            m.Title = LocUtil.GetOr($"{prefix}.name", LocUtil.Resolve(p.Name));
            string desc = LocUtil.GetOr($"{prefix}.desc", LocUtil.Resolve(p.Description));
            m.Body = AbilityData.FormatPassive(p, desc, rank);

            AbilityDescTokens.Detailed = true;
            try
            {
                m.BodyFull = AbilityData.FormatPassive(p, desc, rank);
                m.Detail = BuildPassiveDetail(p, prefix, rank, max, ranked);
            }
            finally { AbilityDescTokens.Detailed = false; }
        }

        return m;
    }

    string BuildSkillDetail(AbilityData.SkillDef s, string prefix, int rank, int max, bool ranked)
    {
        var sb = new System.Text.StringBuilder();

        if (ranked)
            sb.AppendLine(LocUtil.Get("ui.ability.level", rank + 1, max));

        // 코스트 / 쿨타임
        bool hasLevels = s.CooldownByLevel != null && s.CooldownByLevel.Length > 0;
        if (s.GetCooldown(rank) > 0f)
        {
            string cd = hasLevels
                ? AbilityData.FormatLevels(s.CooldownByLevel, rank, max)
                : s.Cooldown.ToString("0.#");
            sb.AppendLine(LocUtil.Get("ui.ability.cooldown", cd));
        }

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
            sb.AppendLine(AbilityData.FormatSkill(s, info, rank));
        }

        // 팁
        string tip = LocUtil.GetOr($"{prefix}.tip", LocUtil.Resolve(s.Tip));
        if (!string.IsNullOrWhiteSpace(tip) && !tip.StartsWith("#"))
        {
            sb.AppendLine();
            sb.AppendLine(LocUtil.Get("ui.ability.tip_prefix") + " " + AbilityData.FormatSkill(s, tip, rank));
        }

        return sb.ToString().TrimEnd();
    }

    string BuildPassiveDetail(AbilityData.PassiveDef p, string prefix, int rank, int max, bool ranked)
    {
        var sb = new System.Text.StringBuilder();

        if (ranked)
            sb.AppendLine(LocUtil.Get("ui.ability.level", rank + 1, max));

        string info = LocUtil.GetOr($"{prefix}.info", LocUtil.Resolve(p.DetailedInfo));
        if (!string.IsNullOrWhiteSpace(info) && !info.StartsWith("#"))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine(AbilityData.FormatPassive(p, info, rank));
        }

        string s = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}