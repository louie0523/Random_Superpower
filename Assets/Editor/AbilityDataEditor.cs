using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AbilityData 전용 인스펙터.
///  - 탭으로 나눔: 기본 / 패시브 / 스킬 / 로직·게이지 / 배정·진화·상점
///  - 효과 종류에 따라 필요한 칸만 보여주고, 각 칸이 무슨 값인지 한 줄 설명
///  - 레벨별 값은 Lv1 Lv2 … 칸으로, 최대 레벨과 칸 수가 안 맞으면 버튼 하나로 맞춤
///  - 설명 문장은 미리보기 레벨 기준으로 실제 인게임처럼 미리보기
///  - 지금 코드에서 동작하지 않는 조합은 경고
///  - 검사는 값이 바뀔 때만 다시 함 (매 프레임 X)
/// </summary>
[CustomEditor(typeof(AbilityData))]
public class AbilityDataEditor : Editor
{
    const string TabKey = "AbilityDataEditor.Tab";
    const string PreviewKey = "AbilityDataEditor.PreviewLevel";

    SerializedProperty _id, _displayName, _kind, _tier, _tags, _icon, _flavor;
    SerializedProperty _passives, _skills, _behaviour, _charges, _exclusiveShop;
    SerializedProperty _maxConcurrent, _excludeFromRoll, _rollWeight, _evolveToId, _evolveKillCount;

    Editor _behaviourEditor;
    bool _showBehaviour = true;
    bool _showIssues = true;
    bool _showRaw;

    readonly List<(MessageType type, string msg)> _issues = new();
    readonly List<string> _tmp = new();
    readonly StringBuilder _sb = new();
    bool _issuesDirty = true;

    static GUIStyle _preview, _hint, _token, _section, _entryTitle, _richFoldout, _lv, _lvUnused, _ok, _descArea;
    static readonly Regex VarToken = new(@"\{v(\d+)", RegexOptions.Compiled);

    SerializedObject _bSO;

    // ══════════════════ 생명주기 ══════════════════

    void OnEnable()
    {
        _id = serializedObject.FindProperty("Id");
        _displayName = serializedObject.FindProperty("DisplayName");
        _kind = serializedObject.FindProperty("Kind");
        _tier = serializedObject.FindProperty("Tier");
        _tags = serializedObject.FindProperty("Tags");
        _icon = serializedObject.FindProperty("Icon");
        _flavor = serializedObject.FindProperty("Flavor");
        _passives = serializedObject.FindProperty("Passives");
        _skills = serializedObject.FindProperty("Skills");
        _behaviour = serializedObject.FindProperty("Behaviour");
        _charges = serializedObject.FindProperty("Charges");
        _exclusiveShop = serializedObject.FindProperty("ExclusiveShopItems");
        _maxConcurrent = serializedObject.FindProperty("MaxConcurrent");
        _excludeFromRoll = serializedObject.FindProperty("ExcludeFromRoll");
        _rollWeight = serializedObject.FindProperty("RollWeight");
        _evolveToId = serializedObject.FindProperty("EvolveToId");
        _evolveKillCount = serializedObject.FindProperty("EvolveKillCount");

        Undo.undoRedoPerformed += OnUndo;
        _issuesDirty = true;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndo;
        if (_behaviourEditor != null) DestroyImmediate(_behaviourEditor);
    }

    void OnUndo()
    {
        _issuesDirty = true;
        Repaint();
    }

    AbilityKind Kind => (AbilityKind)_kind.enumValueIndex;

    // ══════════════════ 메인 ══════════════════

    public override void OnInspectorGUI()
    {
        EnsureStyles();
        var data = (AbilityData)target;
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();

        DrawTitle();
        DrawIssues(data);

        int tab = SessionState.GetInt(TabKey, 0);
        string[] tabs =
        {
            "기본",
            $"패시브 {_passives.arraySize}",
            $"스킬 {_skills.arraySize}",
            "로직 · 게이지",
            "배정 · 진화 · 상점",
        };

        GUILayout.Space(4);
        int newTab = GUILayout.Toolbar(tab, tabs, GUILayout.Height(24));
        if (newTab != tab)
        {
            SessionState.SetInt(TabKey, newTab);
            tab = newTab;
            GUI.FocusControl(null);
        }
        GUILayout.Space(6);

        switch (tab)
        {
            case 0: DrawBasicTab(); break;
            case 1: DrawEntriesTab(data, false); break;
            case 2: DrawEntriesTab(data, true); break;
            case 3: DrawLogicTab(); break;
            case 4: DrawRulesTab(); break;
        }

        if (EditorGUI.EndChangeCheck()) _issuesDirty = true;
        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(12);
        _showRaw = EditorGUILayout.Foldout(_showRaw, "원본 인스펙터 (디버그용)", true);
        if (_showRaw) DrawDefaultInspector();
    }

    /// <summary>배열 구조가 바뀌면 이번 프레임 그리기를 끊고 다음 프레임에 새로 그림</summary>
    void EndStructural()
    {
        serializedObject.ApplyModifiedProperties();
        _issuesDirty = true;
        GUIUtility.ExitGUI();
    }

    // ══════════════════ 머리 ══════════════════

    void DrawTitle()
    {
        EditorGUILayout.BeginHorizontal();

        _icon.objectReferenceValue = EditorGUILayout.ObjectField(
            _icon.objectReferenceValue, typeof(Sprite), false,
            GUILayout.Width(64), GUILayout.Height(64));

        EditorGUILayout.BeginVertical();
        EditorGUILayout.PropertyField(_displayName, new GUIContent("이름",
            "평문 또는 로컬라이제이션 키.\n시트에 ability.{Id}.name 이 있으면 그게 우선"));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(_id, new GUIContent("ID"));
        int expected = ExpectedBand(Kind);
        bool bandOk = _id.intValue / 1000 == expected;
        GUILayout.Label(bandOk ? $"✓ {expected}000번대" : $"<color=#E0A040>⚠ {expected}000번대여야 함</color>",
                        _hint, GUILayout.Width(110));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.PropertyField(_kind, new GUIContent("종류",
            "Main = 메인 능력 · Sub = 서브 능력(등급 있음) · Random = 사망마다 바뀌는 랜덤 능력"));
        if (Kind == AbilityKind.Sub)
            EditorGUILayout.PropertyField(_tier, new GUIContent("등급", "실버 60% · 골드 30% · 플래티넘 10%"));

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    static int ExpectedBand(AbilityKind k) => k switch
    {
        AbilityKind.Main => 6,
        AbilityKind.Sub => 7,
        AbilityKind.Random => 8,
        _ => -1,
    };

    // ══════════════════ 검사 ══════════════════

    void DrawIssues(AbilityData data)
    {
        if (_issuesDirty)
        {
            Validate(data);
            _issuesDirty = false;
        }

        if (_issues.Count == 0)
        {
            GUILayout.Label("✓ 문제 없음", _ok);
            return;
        }

        _showIssues = EditorGUILayout.Foldout(_showIssues, $"확인할 것 {_issues.Count}건", true);
        if (!_showIssues) return;

        foreach (var (type, msg) in _issues)
            EditorGUILayout.HelpBox(msg, type);
    }

    void Validate(AbilityData d)
    {
        _issues.Clear();

        // ── 기본 ──
        if (d.Id == 0)
            _issues.Add((MessageType.Error, "ID가 0입니다."));
        else if (d.Id / 1000 != ExpectedBand(d.Kind))
            _issues.Add((MessageType.Warning, $"ID {d.Id}가 {d.Kind}({ExpectedBand(d.Kind)}000번대)와 맞지 않습니다."));

        if (string.IsNullOrWhiteSpace(d.DisplayName))
            _issues.Add((MessageType.Warning, "이름이 비어 있습니다."));

        int pCount = d.Passives?.Length ?? 0;
        int sCount = d.Skills?.Length ?? 0;

        if (pCount + sCount == 0 && d.Behaviour == null)
            _issues.Add((MessageType.Error, "패시브·스킬·Behaviour가 모두 비어 있어 아무것도 하지 않습니다."));

        if (d.Kind != AbilityKind.Random && pCount + sCount > AbilityRuntime.EntriesPerAbility)
            _issues.Add((MessageType.Warning,
                $"패시브+스킬이 {pCount + sCount}개 — 레벨은 앞 {AbilityRuntime.EntriesPerAbility}개까지만 저장됩니다."));

        // ── 슬롯 ──
        if (d.Skills != null)
        {
            var seen = new HashSet<SkillSlot>();
            foreach (var s in d.Skills)
            {
                if (s == null) continue;

                if (!seen.Add(s.Slot))
                    _issues.Add((MessageType.Warning, $"슬롯 {s.Slot.Label()}에 스킬이 둘 이상 — 먼저 나온 것만 쓰입니다."));

                if (d.Kind == AbilityKind.Random && s.Slot != SkillSlot.Random)
                    _issues.Add((MessageType.Warning,
                        $"'{s.Name}': 랜덤 능력 스킬은 F(Random) 슬롯이어야 메인·서브 스킬과 겹치지 않습니다."));

                if (d.Kind != AbilityKind.Random && s.Slot == SkillSlot.Random)
                    _issues.Add((MessageType.Warning,
                        $"'{s.Name}': F(Random) 슬롯은 랜덤 능력 전용 — 랜덤 능력 스킬과 겹칩니다."));
            }
        }

        // ── 효과 · 칸 수 ──
        int fxWarn = 0, sizeWarn = 0;

        if (d.Passives != null)
            foreach (var p in d.Passives)
            {
                if (p == null) continue;
                sizeWarn += CountOversize(p.Effects, p.Vars, null, p.MaxLevel);
                fxWarn += CountEffectWarnings(p.Effects, false, false);
            }

        if (d.Skills != null)
            foreach (var s in d.Skills)
            {
                if (s == null) continue;
                sizeWarn += CountOversize(s.Effects, s.Vars, s.CooldownByLevel, s.MaxLevel);
                fxWarn += CountEffectWarnings(s.Effects, true, s.NeedsTarget);
            }

        if (fxWarn > 0)
            _issues.Add((MessageType.Warning, $"지금 코드에서 동작하지 않는 효과 설정 {fxWarn}건 — 각 효과 아래에 표시됩니다."));

        if (sizeWarn > 0)
            _issues.Add((MessageType.Info, $"최대 레벨보다 값 칸이 많은 곳 {sizeWarn}군데 — 넘는 칸은 안 쓰입니다."));

        // ── 게이지 ↔ Behaviour ──
        int? slot = BehaviourChargeSlot(d);
        if (slot.HasValue)
        {
            bool found = false;
            if (d.Charges != null)
                foreach (var c in d.Charges)
                    if (c.Slot == slot.Value) { found = true; break; }

            if (!found)
                _issues.Add((MessageType.Error,
                    $"Behaviour의 ChargeSlot({slot.Value})에 맞는 게이지 항목이 없습니다 → 게이지 UI가 안 보입니다. " +
                    "'로직 · 게이지' 탭에서 추가하세요."));
        }

        // ── 진화 ──
        if (d.EvolveToId != 0 && d.EvolveKillCount <= 0)
            _issues.Add((MessageType.Warning, "진화 대상이 있는데 필요 막타 수가 0입니다."));

        if (d.ExclusiveShopItems != null && d.ExclusiveShopItems.Length > PlayerShop.MaxExclusive)
            _issues.Add((MessageType.Warning,
                $"전용 상점 항목이 {d.ExclusiveShopItems.Length}개 — 앞 {PlayerShop.MaxExclusive}개까지만 표시됩니다."));
    }

    int CountEffectWarnings(AbilityData.EffectDef[] fx, bool isSkill, bool needsTarget)
    {
        if (fx == null) return 0;
        int n = 0;
        foreach (var e in fx)
        {
            if (e == null) continue;
            _tmp.Clear();
            CollectEffectWarnings(e.Effect, e.Target, e.Trigger, isSkill, needsTarget, _tmp);
            n += _tmp.Count;
        }
        return n;
    }

    static int CountOversize(AbilityData.EffectDef[] fx, AbilityData.RankVar[] vars, float[] cds, int max)
    {
        int n = 0;
        if (fx != null)
            foreach (var e in fx)
                if (e?.Values != null && e.Effect != EffectType.EnterState && e.Values.Length > max) n++;
        if (vars != null)
            foreach (var v in vars)
                if (v?.Values != null && v.Values.Length > max) n++;
        if (cds != null && cds.Length > max) n++;
        return n;
    }

    static int? BehaviourChargeSlot(AbilityData d)
    {
        if (d.Behaviour == null) return null;

        var so = new SerializedObject(d.Behaviour);
        try
        {
            var p = so.FindProperty("ChargeSlot");
            return p != null && p.propertyType == SerializedPropertyType.Integer ? p.intValue : null;
        }
        finally { so.Dispose(); }
    }

    // ══════════════════ 탭: 기본 ══════════════════

    void DrawBasicTab()
    {
        EditorGUILayout.PropertyField(_flavor, new GUIContent("플레이버",
            "능력 한 줄 소개. 시트에 ability.{Id}.flavor 가 있으면 그게 우선"));
        EditorGUILayout.PropertyField(_tags, new GUIContent("태그", "분류용. 다른 능력이 태그로 찾을 때 사용"), true);

        AbilityHudInspector.Draw(serializedObject, (AbilityData)target);     // 추가
    }

    // ══════════════════ 탭: 패시브 / 스킬 ══════════════════

    void DrawEntriesTab(AbilityData data, bool isSkill)
    {
        var list = isSkill ? _skills : _passives;

        // 미리보기 레벨
        int maxAll = 1;
        for (int i = 0; i < list.arraySize; i++)
            maxAll = Mathf.Max(maxAll, list.GetArrayElementAtIndex(i).FindPropertyRelative("MaxLevel").intValue);

        int preview = Mathf.Clamp(SessionState.GetInt(PreviewKey, 1), 1, maxAll);
        if (maxAll > 1 && Kind != AbilityKind.Random)
        {
            int np = EditorGUILayout.IntSlider(new GUIContent("미리보기 레벨",
                "설명 미리보기를 이 레벨 기준으로 보여줌"), preview, 1, maxAll);
            if (np != preview) { SessionState.SetInt(PreviewKey, np); preview = np; }
        }

        if (Kind == AbilityKind.Random)
            Hint("랜덤 능력은 레벨이 없습니다 — 모든 값은 Lv1([0])만 쓰입니다.");

        if (isSkill)
            Hint("슬롯: SHIFT = Skill1 · E = Skill2 · 우클릭 = Skill3 · Q = Ultimate · F = Random(랜덤 능력 전용)");

        GUILayout.Space(4);

        for (int i = 0; i < list.arraySize; i++)
        {
            var el = list.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (DrawItemHeader(list, i, EntryTitle(el, isSkill)))
            {
                GUILayout.Space(2);
                if (isSkill) DrawSkill(el, data, i, preview);
                else DrawPassive(el, data, i, preview);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        if (GUILayout.Button(isSkill ? "+ 스킬 추가" : "+ 패시브 추가", GUILayout.Height(24)))
        {
            AddEntry(list, isSkill);
            EndStructural();
        }
    }

    /// <summary>접기 + ▲▼ 복제 ✕. 구조가 바뀌면 여기서 그리기를 끊음</summary>
    bool DrawItemHeader(SerializedProperty list, int i, string title)
    {
        var el = list.GetArrayElementAtIndex(i);

        EditorGUILayout.BeginHorizontal();
        el.isExpanded = EditorGUILayout.Foldout(el.isExpanded, title, true, _entryTitle);
        GUILayout.FlexibleSpace();

        bool structural = false;

        using (new EditorGUI.DisabledScope(i == 0))
            if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22)))
            { list.MoveArrayElement(i, i - 1); structural = true; }

        using (new EditorGUI.DisabledScope(i == list.arraySize - 1))
            if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22)))
            { list.MoveArrayElement(i, i + 1); structural = true; }

        if (GUILayout.Button("복제", EditorStyles.miniButtonMid, GUILayout.Width(36)))
        { list.InsertArrayElementAtIndex(i); structural = true; }

        if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22)))
        {
            string plain = Regex.Replace(title, "<.*?>", "");
            if (EditorUtility.DisplayDialog("삭제", $"'{plain.Trim()}'을(를) 삭제할까요?", "삭제", "취소"))
            { list.DeleteArrayElementAtIndex(i); structural = true; }
        }

        EditorGUILayout.EndHorizontal();

        if (structural) EndStructural();
        return el.isExpanded;
    }

    string EntryTitle(SerializedProperty el, bool isSkill)
    {
        _sb.Clear();

        if (isSkill)
        {
            var slot = (SkillSlot)el.FindPropertyRelative("Slot").enumValueIndex;
            _sb.Append('[').Append(slot.Label()).Append("]  ");
        }

        string name = el.FindPropertyRelative("Name").stringValue;
        _sb.Append(string.IsNullOrWhiteSpace(name) ? "(이름 없음)" : name);

        _sb.Append("     <color=#888>");
        if (Kind != AbilityKind.Random)
            _sb.Append("최대 Lv").Append(el.FindPropertyRelative("MaxLevel").intValue).Append(" · ");

        if (isSkill)
        {
            var arr = el.FindPropertyRelative("CooldownByLevel");
            float cd = arr.arraySize > 0
                ? arr.GetArrayElementAtIndex(0).floatValue
                : el.FindPropertyRelative("Cooldown").floatValue;
            _sb.Append("쿨 ").Append(cd.ToString("0.#")).Append(arr.arraySize > 1 ? "s~ · " : "s · ");
        }

        _sb.Append("효과 ").Append(el.FindPropertyRelative("Effects").arraySize).Append("</color>");
        return _sb.ToString();
    }

    // ── 패시브 ──

    void DrawPassive(SerializedProperty el, AbilityData data, int index, int previewLevel)
    {
        var p = data.Passives != null && index < data.Passives.Length ? data.Passives[index] : null;
        int max = Mathf.Max(1, el.FindPropertyRelative("MaxLevel").intValue);
        int rank = Mathf.Clamp(previewLevel, 1, max) - 1;

        Section("표시");
        EditorGUILayout.PropertyField(el.FindPropertyRelative("Name"), new GUIContent("이름"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("Icon"), new GUIContent("아이콘"));
        DrawTokenHelp(el);
        DrawTextWithPreview(el.FindPropertyRelative("Description"), "설명", p?.Effects, p?.Vars, rank, max);
        DrawTextWithPreview(el.FindPropertyRelative("DetailedInfo"), "상세", p?.Effects, p?.Vars, rank, max);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("Tip"), new GUIContent("팁"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("DevNote"), new GUIContent("개발 메모", "인게임에 안 나옴"));

        if (Kind != AbilityKind.Random)
        {
            Section("레벨");
            DrawMaxLevel(el, false);
            DrawVars(el.FindPropertyRelative("Vars"), max);
        }
        else
        {
            Section("Vars");
            DrawVars(el.FindPropertyRelative("Vars"), 1);
        }

        DrawBehaviourParams(data, false, index, Kind != AbilityKind.Random ? max : 1);
        Section("효과");
        DrawEffects(el.FindPropertyRelative("Effects"), max, false, false);
    }

    // ── 스킬 ──

    void DrawSkill(SerializedProperty el, AbilityData data, int index, int previewLevel)
    {
        var s = data.Skills != null && index < data.Skills.Length ? data.Skills[index] : null;
        int max = Mathf.Max(1, el.FindPropertyRelative("MaxLevel").intValue);
        int rank = Mathf.Clamp(previewLevel, 1, max) - 1;

        Section("표시");
        EditorGUILayout.PropertyField(el.FindPropertyRelative("Name"), new GUIContent("이름"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("Icon"), new GUIContent("아이콘"));
        DrawTokenHelp(el);
        DrawTextWithPreview(el.FindPropertyRelative("Description"), "설명", s?.Effects, s?.Vars, rank, max);
        DrawTextWithPreview(el.FindPropertyRelative("DetailedInfo"), "상세", s?.Effects, s?.Vars, rank, max);
        EditorGUILayout.PropertyField(el.FindPropertyRelative("Tip"), new GUIContent("팁"));
        EditorGUILayout.PropertyField(el.FindPropertyRelative("DevNote"), new GUIContent("개발 메모", "인게임에 안 나옴"));

        Section(Kind != AbilityKind.Random ? "슬롯 · 레벨 · 쿨타임" : "슬롯 · 쿨타임");
        EditorGUILayout.PropertyField(el.FindPropertyRelative("Slot"), new GUIContent("슬롯"));
        if (Kind != AbilityKind.Random) DrawMaxLevel(el, true);
        DrawCooldown(el, Kind != AbilityKind.Random ? max : 1);
        var after = el.FindPropertyRelative("CooldownAfterEffect");
        EditorGUILayout.PropertyField(after, new GUIContent("효과 끝나고 쿨타임",
            "켜면 쓰는 동안 쿨이 멈추고, Behaviour가 효과를 끝낼 때부터 돎 (승마·변신·궁 등 지속형)"));
        if (after.boolValue && data.Behaviour == null)
            Warn("Behaviour가 없으면 끝을 알려줄 수 없어 안전장치 시간(60초) 뒤에 쿨이 돕니다.");
        DrawVars(el.FindPropertyRelative("Vars"), Kind != AbilityKind.Random ? max : 1);
        DrawBehaviourParams(data, true, index, Kind != AbilityKind.Random ? max : 1);

        Section("비용 · 조건");
        EditorGUILayout.PropertyField(el.FindPropertyRelative("SoulCost"), new GUIContent("소울 비용", "쓸 때마다 소모"));
        var ult = el.FindPropertyRelative("UltimateChargeCost");
        EditorGUILayout.PropertyField(ult, new GUIContent("궁극기 충전 비용"));
        if (ult.intValue > 0) Warn("아직 코드에서 검사하지 않습니다 (상세창 표시만 됨).");
        EditorGUILayout.PropertyField(el.FindPropertyRelative("RequiredWeapon"), new GUIContent("필요 무기",
            "이 무기를 들었을 때만 사용 가능. 여러 개 선택 가능. Nothing = 제한 없음"));

        Section("모션 · 타이밍");
        var motion = el.FindPropertyRelative("MotionData");
        EditorGUILayout.PropertyField(motion, new GUIContent("시전 모션", "비우면 즉시 발동"));
        if (motion.objectReferenceValue == null)
        {
            Hint("모션 없음 → 누르는 즉시 효과 발동 (타이밍 설정 무시)");
        }
        else
        {
            var timing = el.FindPropertyRelative("EffectTiming");
            EditorGUILayout.PropertyField(timing, new GUIContent("효과 타이밍"));
            Hint(TimingDesc((AbilityData.SkillEffectTiming)timing.enumValueIndex));
        }

        Section("판정");
        var needs = el.FindPropertyRelative("NeedsTarget");
        EditorGUILayout.PropertyField(needs, new GUIContent("대상 필요", "적을 찾아서 맞히는 스킬이면 체크 (버프·대시는 해제)"));
        if (needs.boolValue)
        {
            var mode = el.FindPropertyRelative("HitMode");
            EditorGUILayout.PropertyField(mode, new GUIContent("판정 모양", "ForwardArc = 정면 부채꼴 · ForwardSphere = 주변 전체"));
            EditorGUILayout.PropertyField(el.FindPropertyRelative("Range"), new GUIContent("사거리 (m)"));
            if ((WeaponHitMode)mode.enumValueIndex == WeaponHitMode.ForwardArc)
                EditorGUILayout.PropertyField(el.FindPropertyRelative("HitAngle"), new GUIContent("각도 (°)", "부채꼴 전체 각도"));
            EditorGUILayout.PropertyField(el.FindPropertyRelative("HitHeight"), new GUIContent("높이 허용 (m)", "위아래로 이만큼 차이까지 맞음"));
            var maxT = el.FindPropertyRelative("MaxTargets");
            EditorGUILayout.PropertyField(maxT, new GUIContent("최대 대상"));
            if (maxT.intValue > 1) Warn("지금 코드는 가장 가까운 1명에게만 적용합니다.");
            Hint("못 맞히면 효과 전체가 발동하지 않음 (쿨타임·소울은 소모)");
            EditorGUILayout.PropertyField(el.FindPropertyRelative("AppliesWeaponEnchants"),
            new GUIContent("무기 인챈트 적용", "맞힌 대상에게 들고 있는 무기의 특수 인챈트 효과"));
        }
        else
        {
            Hint("대상 없음 → 효과의 대상은 Self만 동작 (버프·대시 등)");
        }

        Section("효과");
        DrawEffects(el.FindPropertyRelative("Effects"), max, true, needs.boolValue);
    }

    void DrawMaxLevel(SerializedProperty el, bool isSkill)
    {
        var p = el.FindPropertyRelative("MaxLevel");
        EditorGUILayout.IntSlider(p, 1, 10, new GUIContent("최대 레벨", "1이면 강화 불가. 카드의 점 개수"));
        if (p.intValue < 1) p.intValue = 1;

        if (isSkill && (SkillSlot)el.FindPropertyRelative("Slot").enumValueIndex == SkillSlot.Ultimate && p.intValue == 5)
            Hint("궁극기는 보통 3");
    }

    void DrawCooldown(SerializedProperty el, int max)
    {
        var cd = el.FindPropertyRelative("Cooldown");
        var arr = el.FindPropertyRelative("CooldownByLevel");

        if (arr.arraySize == 0)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(cd, new GUIContent("쿨타임 (초)"));
            using (new EditorGUI.DisabledScope(max <= 1))
                if (GUILayout.Button(new GUIContent("레벨별로", "레벨마다 다른 쿨타임"), EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    arr.arraySize = max;
                    for (int i = 0; i < max; i++) arr.GetArrayElementAtIndex(i).floatValue = cd.floatValue;
                }
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            DrawLevelValues(arr, max, "쿨타임 (초)", "레벨별 쿨타임. 쿨감 스탯은 여기에 곱해짐");
            if (GUILayout.Button("단일 값으로 되돌리기", EditorStyles.miniButton, GUILayout.Width(130)))
            {
                cd.floatValue = arr.GetArrayElementAtIndex(0).floatValue;
                arr.arraySize = 0;
            }
        }
    }

    // ── 설명 · 미리보기 ──

    void DrawTokenHelp(SerializedProperty el)
    {
        var fx = el.FindPropertyRelative("Effects");
        var vars = el.FindPropertyRelative("Vars");

        _sb.Clear();
        _sb.Append("설명에 쓸 수 있는 값:  ");

        if (fx.arraySize == 0 && vars.arraySize == 0)
            _sb.Append("없음 (효과나 Vars를 추가하면 생김)");

        for (int i = 0; i < fx.arraySize; i++)
        {
            var eff = (EffectType)fx.GetArrayElementAtIndex(i).FindPropertyRelative("Effect").enumValueIndex;
            _sb.Append("<b>{").Append(i).Append("}</b> ").Append(ValueLabel(eff)).Append("    ");
        }

        for (int i = 0; i < vars.arraySize; i++)
        {
            string n = vars.GetArrayElementAtIndex(i).FindPropertyRelative("Name").stringValue;
            _sb.Append("<b>{v").Append(i).Append("}</b> ").Append(string.IsNullOrEmpty(n) ? "(이름 없음)" : n).Append("    ");
        }

        if (fx.arraySize + vars.arraySize > 0)
            _sb.Append("\n뒤에 :all 을 붙이면 모든 레벨 (예: {0:all} → 20 / 30 / 40)");

        Hint(_sb.ToString());
    }

    /// <summary>설명 입력 칸 — 글 길이만큼 늘어남 (최소 minLines줄, 스크롤 없음)</summary>
    void DrawDescField(SerializedProperty prop, string label, int minLines = 4)
    {
        EditorGUILayout.LabelField(new GUIContent(label, prop.tooltip), EditorStyles.boldLabel);

        string text = prop.stringValue ?? "";
        // 끝에 빈 줄 하나를 더해서 높이를 잼 → 타이핑 중 칸이 덜컥거리지 않음
        var measure = new GUIContent(text + "\n ");
        float minH = _descArea.lineHeight * minLines + _descArea.padding.vertical;
        Rect r = GUILayoutUtility.GetRect(measure, _descArea, GUILayout.MinHeight(minH), GUILayout.ExpandWidth(true));

        EditorGUI.BeginProperty(r, GUIContent.none, prop);
        EditorGUI.BeginChangeCheck();
        string nv = EditorGUI.TextArea(r, text, _descArea);
        if (EditorGUI.EndChangeCheck()) prop.stringValue = nv;
        EditorGUI.EndProperty();
    }

    void DrawTextWithPreview(SerializedProperty prop, string label,
                             AbilityData.EffectDef[] fx, AbilityData.RankVar[] vars, int rank, int max)
    {
        DrawDescField(prop, label);

        string raw = prop.stringValue;
        if (string.IsNullOrWhiteSpace(raw) || raw.IndexOf('{') < 0) return;

        var owner = (AbilityData)target;
        raw = AbilityDescTokens.Apply(raw, (AbilityData)target, vars, rank, max);
        if (raw.IndexOf('{') < 0) { GUILayout.Label(raw, _preview); return; }

        int e = fx?.Length ?? 0;
        int v = vars?.Length ?? 0;

        // 서식이 깨졌으면 미리보기 대신 이유를 보여줌 (콘솔 도배 방지)
        if (!CheckFormat(raw, e, v))
        {
            Warn($"서식 오류 — {{0}}~{{{Mathf.Max(0, e - 1)}}}(효과 {e}개), {{v0}}~(Vars {v}개)만 쓸 수 있고 중괄호 짝이 맞아야 합니다.");
            return;
        }

        GUILayout.Label(AbilityData.FormatRanked(raw, fx, vars, rank, max), _preview);
    }

    static bool CheckFormat(string raw, int effectCount, int varCount)
    {
        string s = VarToken.Replace(raw, m => "{" + (effectCount + int.Parse(m.Groups[1].Value)));
        var dummy = new object[effectCount + varCount];
        for (int i = 0; i < dummy.Length; i++) dummy[i] = 0f;

        try { string.Format(s, dummy); return true; }
        catch (System.FormatException) { return false; }
    }

    // ── 레벨별 값 칸 ──

    void DrawLevelValues(SerializedProperty arr, int max, string label, string tooltip = null)
    {
        if (arr.arraySize == 0)
        {
            arr.arraySize = 1;
            arr.GetArrayElementAtIndex(0).floatValue = 0f;
        }
        int n = arr.arraySize;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent(label, tooltip));

        for (int i = 0; i < n; i++)
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(34));
            bool unused = i >= max;
            string head = n == 1 && max > 1 ? "전 레벨" : $"Lv{i + 1}";
            GUILayout.Label(head, unused ? _lvUnused : _lv);

            var el = arr.GetArrayElementAtIndex(i);
            el.floatValue = EditorGUILayout.FloatField(el.floatValue);
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();

        // 칸 수 안내 + 맞추기
        string note = null, btn = null;
        int target = n;

        if (max > 1)
        {
            if (n == 1) { note = "모든 레벨 같은 값"; btn = "레벨별로"; target = max; }
            else if (n < max) { note = $"Lv{n + 1}~{max}은 마지막 값 반복"; btn = "칸 맞추기"; target = max; }
            else if (n > max) { note = $"<color=#E0A040>Lv{max + 1}~ 값은 안 쓰임</color>"; btn = "잘라내기"; target = max; }
        }
        else if (n > 1)
        {
            note = "<color=#E0A040>최대 레벨 1 → 첫 값만 쓰임</color>"; btn = "잘라내기"; target = 1;
        }

        if (note == null) return;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUIUtility.labelWidth);
        GUILayout.Label(note, _hint);
        if (GUILayout.Button(btn, EditorStyles.miniButton, GUILayout.Width(64)))
            arr.arraySize = target;     // 늘릴 땐 마지막 값이 복사됨
        EditorGUILayout.EndHorizontal();
    }

    // ── Vars ──

    void DrawVars(SerializedProperty vars, int max)
    {
        GUILayout.Label(new GUIContent("Vars — Behaviour 코드가 읽는 수치",
            "Behaviour에서 Var(항목번호, Var번호, 기본값)으로 읽음. 설명에서는 {v0} {v1}"), EditorStyles.miniBoldLabel);

        for (int i = 0; i < vars.arraySize; i++)
        {
            var v = vars.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{{v{i}}}", _token, GUILayout.Width(36));

            var name = v.FindPropertyRelative("Name");
            name.stringValue = EditorGUILayout.TextField(name.stringValue);

            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                vars.DeleteArrayElementAtIndex(i);
                EndStructural();
            }
            EditorGUILayout.EndHorizontal();

            DrawLevelValues(v.FindPropertyRelative("Values"), max, "값");
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("+ Var", EditorStyles.miniButton, GUILayout.Width(60)))
        {
            vars.arraySize++;
            var v = vars.GetArrayElementAtIndex(vars.arraySize - 1);
            v.FindPropertyRelative("Name").stringValue = "";
            var vals = v.FindPropertyRelative("Values");
            vals.arraySize = Mathf.Max(1, max);
            for (int k = 0; k < vals.arraySize; k++) vals.GetArrayElementAtIndex(k).floatValue = 0f;
            EndStructural();
        }
    }

    /// <summary>Behaviour의 [ForSkill]/[ForPassive] LevelValue 중 이 항목에 묶인 것들을 레벨 칸으로</summary>
    void DrawBehaviourParams(AbilityData data, bool skill, int index, int max)
    {
        var b = data.Behaviour;
        if (b == null) return;

        if (_bSO == null || _bSO.targetObject != b) _bSO = new SerializedObject(b);
        _bSO.Update();

        bool header = false;
        foreach (var f in b.GetType().GetFields(LevelBinding.Flags))
        {
            if (!LevelBinding.TryGet(f, b, out bool isSkill, out int idx, out string label)) continue;
            if (isSkill != skill || idx != index) continue;

            var prop = _bSO.FindProperty(f.Name);
            if (prop == null) continue;

            if (!header)
            {
                Section($"Behaviour 수치 — {b.name}");
                Hint("레벨별 값 · 이 Behaviour 에셋을 쓰는 모든 능력에 같이 적용됩니다.\n" +
                     "설명에서 {b:필드} (현재 레벨) · {b:필드:all} (전 레벨) · {b:필드*100} (곱해서 0.9 → 90)");
                header = true;
            }

            var values = prop.FindPropertyRelative("Values");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("{b:" + f.Name + "}", _token);
                GUILayout.FlexibleSpace();

                // 설명용 토큰 복사
                if (GUILayout.Button(new GUIContent("복사 ▾", "설명에 붙여넣을 토큰을 클립보드로"), EditorStyles.miniButton, GUILayout.Width(52)))
                    ShowTokenMenu(f.Name, values);

                // 단일 ↔ 레벨별
                if (values.arraySize > 1)
                {
                    if (GUILayout.Button(new GUIContent("하나로", "Lv1 값 하나만 남김 → 모든 레벨 같은 값"), EditorStyles.miniButton, GUILayout.Width(48)))
                        values.arraySize = 1;
                }
                else if (max > 1)
                {
                    if (GUILayout.Button(new GUIContent("레벨별로", "최대 레벨만큼 칸을 늘림 (지금 값 복사)"), EditorStyles.miniButton, GUILayout.Width(58)))
                        values.arraySize = max;
                }
            }
            DrawLevelValues(values, max, LevelBinding.Nice(f, label));
            EditorGUILayout.EndVertical();
        }

        if (_bSO.ApplyModifiedProperties()) Repaint();
    }

    /// <summary>토큰 복사 메뉴 — 값이 0~1 비율이면 ×100 버전을 위에</summary>
    void ShowTokenMenu(string field, SerializedProperty values)
    {
        bool ratio = true;
        for (int i = 0; i < values.arraySize; i++)
        {
            float v = values.GetArrayElementAtIndex(i).floatValue;
            if (v < 0f || v > 1f) { ratio = false; break; }
        }

        var menu = new GenericMenu();
        void Add(string text, string token) =>
            menu.AddItem(new GUIContent($"{text}    {token}"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = token;
                EditorWindow.focusedWindow?.ShowNotification(new GUIContent($"복사됨: {token}"), 1.2);
            });

        if (ratio)
        {
            Add("×100 (0.9 → 90)", $"{{b:{field}*100}}");
            Add("×100 전 레벨", $"{{b:{field}*100:all}}");
            menu.AddSeparator("");
        }
        Add("현재 레벨", $"{{b:{field}}}");
        Add("전 레벨", $"{{b:{field}:all}}");
        menu.AddSeparator("");
        Add("피해 계수로 (공격력 × 이 값 %)", $"{{dmg:{field}}}");
        menu.ShowAsContext();
    }

    // ── 효과 ──

    void DrawEffects(SerializedProperty effects, int max, bool isSkill, bool needsTarget)
    {
        if (isSkill)
            Hint("스킬 효과는 사용 시(모션이 있으면 타이밍에 맞춰) 한 번 발동합니다. 발동 시점(Trigger)은 무시됩니다.");

        int levels = Kind != AbilityKind.Random ? max : 1;

        for (int i = 0; i < effects.arraySize; i++)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawEffect(effects, i, levels, isSkill, needsTarget);
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("+ 효과 추가", EditorStyles.miniButton, GUILayout.Width(90)))
        {
            AddEffect(effects, isSkill, levels);
            EndStructural();
        }
    }

    void DrawEffect(SerializedProperty list, int i, int levels, bool isSkill, bool needsTarget)
    {
        var e = list.GetArrayElementAtIndex(i);
        var effP = e.FindPropertyRelative("Effect");
        var tgtP = e.FindPropertyRelative("Target");
        var trgP = e.FindPropertyRelative("Trigger");

        // ── 머리: {번호} 효과 → 대상   ▲▼✕ ──
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"{{{i}}}", _token, GUILayout.Width(28));
        EditorGUILayout.PropertyField(effP, GUIContent.none, GUILayout.MinWidth(90));
        GUILayout.Label("→", GUILayout.Width(14));
        EditorGUILayout.PropertyField(tgtP, GUIContent.none, GUILayout.MinWidth(90));
        GUILayout.FlexibleSpace();

        bool structural = false;
        using (new EditorGUI.DisabledScope(i == 0))
            if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22)))
            { list.MoveArrayElement(i, i - 1); structural = true; }
        using (new EditorGUI.DisabledScope(i == list.arraySize - 1))
            if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22)))
            { list.MoveArrayElement(i, i + 1); structural = true; }
        if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22)))
        { list.DeleteArrayElementAtIndex(i); structural = true; }

        EditorGUILayout.EndHorizontal();
        if (structural) EndStructural();

        var eff = (EffectType)effP.enumValueIndex;
        var tgt = (TargetType)tgtP.enumValueIndex;
        var trg = (TriggerType)trgP.enumValueIndex;

        Hint(EffectDesc(eff) + "  ·  대상: " + TargetDesc(tgt));

        // ── 발동 시점 (패시브만) ──
        if (!isSkill)
        {
            EditorGUILayout.PropertyField(trgP, new GUIContent("발동 시점"));
            Hint(TriggerDesc(trg));
        }

        // ── 효과별 칸 ──
        var vals = e.FindPropertyRelative("Values");
        var dur = e.FindPropertyRelative("Duration");

        switch (eff)
        {
            case EffectType.StatMod:
                {
                    var statP = e.FindPropertyRelative("Stat");
                    var modP = e.FindPropertyRelative("ModType");

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(statP, new GUIContent("스탯"));
                    EditorGUILayout.PropertyField(modP, GUIContent.none, GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                    Hint(StatHint((StatType)statP.enumValueIndex, (ModType)modP.enumValueIndex));

                    var stat = (StatType)statP.enumValueIndex;
                    bool isMul = stat == StatType.DamageDealt || stat == StatType.DamageTaken ||
                                 stat == StatType.MeleeDamage || stat == StatType.RangedDamage ||
                                 stat == StatType.SkillDamage || stat == StatType.UltimateDamage;
                    if (isMul && (ModType)modP.enumValueIndex == ModType.Flat)
                        for (int k = 0; k < vals.arraySize; k++)
                            if (Mathf.Abs(vals.GetArrayElementAtIndex(k).floatValue) >= 1f)
                            { Warn("1 이상이면 배율이 2배 넘게 오릅니다 — 0.25 = +25%p 단위가 맞나요?"); break; }

                    DrawLevelValues(vals, levels, "수치");

                    bool alwaysOn = !isSkill && trg == TriggerType.Passive;
                    if (!alwaysOn)
                    {
                        EditorGUILayout.PropertyField(dur, new GUIContent("지속 (초)", "0이면 영구"));
                        if (dur.floatValue <= 0f)
                            Warn("지속 0 = 영구 — 발동할 때마다 계속 쌓이고 사라지지 않습니다 (라운드 초기화 전까지).");
                    }
                    break;
                }

            case EffectType.EnterState:
                {
                    if (vals.arraySize == 0) vals.arraySize = 1;
                    var v0 = vals.GetArrayElementAtIndex(0);
                    v0.floatValue = EditorGUILayout.IntField(new GUIContent("상태 ID",
                        "Behaviour의 StateId와 같은 번호"), Mathf.RoundToInt(v0.floatValue));

                    if (vals.arraySize > 1)
                    {
                        EditorGUILayout.BeginHorizontal();
                        Hint("<color=#E0A040>상태 ID는 첫 값만 쓰입니다</color>");
                        if (GUILayout.Button("정리", EditorStyles.miniButton, GUILayout.Width(40))) vals.arraySize = 1;
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.PropertyField(dur, new GUIContent("지속 (초)"));
                    break;
                }

            case EffectType.AddCharge:
                EditorGUILayout.PropertyField(e.FindPropertyRelative("ChargeSlot"), new GUIContent("게이지 슬롯"));
                DrawLevelValues(vals, levels, "증가량");
                break;

            case EffectType.Dash:
                DrawLevelValues(vals, levels, "거리 (m)");
                EditorGUILayout.PropertyField(dur, new GUIContent("시간 (초)", "0이면 0.2초"));
                break;

            case EffectType.Slow:
                DrawLevelValues(vals, levels, "감소율 (%)");
                EditorGUILayout.PropertyField(dur, new GUIContent("지속 (초)"));
                break;

            case EffectType.ApplyStatus:
                {
                    var stP = e.FindPropertyRelative("Status");
                    EditorGUILayout.PropertyField(stP, new GUIContent("상태이상"));
                    var type = (StatusType)stP.enumValueIndex;

                    if (type is StatusType.Seal or StatusType.Stun or StatusType.Evade or StatusType.Ghost or StatusType.Root)
                        Hint("수치 없음 — 지속시간만 씀");
                    else DrawLevelValues(vals, levels, StatusValueLabel(type));

                    EditorGUILayout.PropertyField(dur, new GUIContent("지속 (초)"));
                    if (dur.floatValue <= 0f) Warn("지속 0이면 걸리지 않습니다.");
                    break;
                }

            case EffectType.AddAlter:
            case EffectType.RemoveAlter:
                EditorGUILayout.PropertyField(e.FindPropertyRelative("Alter"), new GUIContent("변화"));
                if (e.FindPropertyRelative("Alter").objectReferenceValue == null) Warn("변화 에셋을 넣으세요.");
                break;

            default:
                DrawLevelValues(vals, levels, ValueLabel(eff));
                if (IsUnimplemented(eff))
                    EditorGUILayout.PropertyField(dur, new GUIContent("지속 (초)"));
                break;
        }

        // ── 동작하지 않는 조합 ──
        _tmp.Clear();
        CollectEffectWarnings(eff, tgt, trg, isSkill, needsTarget, _tmp);
        foreach (var w in _tmp) EditorGUILayout.HelpBox(w, MessageType.Warning);

        DrawEffectAdvanced(e, trg, isSkill);
    }

    static string StatusValueLabel(StatusType t) => t switch
    {


        StatusType.Slow => "이속 감소 (%)",
        StatusType.Frost => "공속 감소 (%)",
        StatusType.Burn => "초당 최대체력 %",
        StatusType.DeepWound => "회복 감소 (%)",
        StatusType.Blind => "보이는 반경 (m, 최소 1)",
        StatusType.Poison => "중첩당 초당 최대체력 %",
        StatusType.ArmorBreak => "방어력 감소 (%)",
        _ => "값",
    };

    void DrawEffectAdvanced(SerializedProperty e, TriggerType trg, bool isSkill)
    {
        var chance = e.FindPropertyRelative("Chance");
        var req = e.FindPropertyRelative("RequireStateId");
        var cost = e.FindPropertyRelative("ChargeCost");
        var costSlot = e.FindPropertyRelative("CostChargeSlot");
        var ptc = e.FindPropertyRelative("PerTargetCooldown");
        var interval = e.FindPropertyRelative("Interval");

        _sb.Clear();
        if (chance.floatValue < 1f) _sb.Append("확률 ").Append(Mathf.RoundToInt(chance.floatValue * 100f)).Append("%  ");
        if (req.intValue != 0) _sb.Append("상태 ").Append(req.intValue).Append(" 필요  ");
        if (cost.floatValue > 0f) _sb.Append("게이지 -").Append(cost.floatValue.ToString("0.#")).Append("  ");
        if (ptc.floatValue > 0f) _sb.Append("대상별 ").Append(ptc.floatValue.ToString("0.#")).Append("초  ");
        string summary = _sb.Length > 0 ? _sb.ToString() : "기본값";

        // 접힘 상태는 Chance 속성의 isExpanded에 저장 (효과마다 따로 기억)
        chance.isExpanded = EditorGUILayout.Foldout(chance.isExpanded,
            $"조건 · 비용 · 확률   <color=#888>{summary}</color>", true, _richFoldout);
        if (!chance.isExpanded) return;


        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(e.FindPropertyRelative("RequireWeapon"),
            new GUIContent("필요 무기", "이 무기를 들고 있을 때만 발동 (패시브 스탯 포함)"));
        EditorGUILayout.Slider(chance, 0f, 1f, new GUIContent("발동 확률", "1 = 항상"));
        EditorGUILayout.PropertyField(req, new GUIContent("필요 상태 ID", "이 상태일 때만 발동. 0 = 조건 없음"));
        EditorGUILayout.PropertyField(cost, new GUIContent("게이지 소모", "모든 조건 통과 후 마지막에 소모"));
        if (cost.floatValue > 0f)
            EditorGUILayout.PropertyField(costSlot, new GUIContent("소모할 게이지 슬롯"));
        EditorGUILayout.PropertyField(ptc, new GUIContent("대상별 재발동 (초)", "같은 대상에게 이 시간 안엔 다시 안 걸림"));
        if (!isSkill && trg == TriggerType.Periodic)
            EditorGUILayout.PropertyField(interval, new GUIContent("주기 (초)"));
        EditorGUI.indentLevel--;
    }

    // ══════════════════ 탭: 로직 · 게이지 ══════════════════

    void DrawLogicTab()
    {
        Section("Behaviour (전용 로직)");
        EditorGUILayout.PropertyField(_behaviour, new GUIContent("Behaviour 에셋", "단순 능력은 비워두기"));

        var b = _behaviour.objectReferenceValue;
        if (b == null)
        {
            Hint("비어 있음 — 패시브·스킬 데이터만으로 동작합니다.");
        }
        else
        {
            _showBehaviour = EditorGUILayout.Foldout(_showBehaviour,
                "Behaviour 설정 펼치기  (※ 이 에셋을 쓰는 모든 능력에 적용)", true);

            if (_showBehaviour)
            {
                CreateCachedEditor(b, null, ref _behaviourEditor);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _behaviourEditor.OnInspectorGUI();
                EditorGUILayout.EndVertical();
            }
        }

        Section("게이지");
        Hint("화면의 게이지 UI가 이 목록을 보고 그립니다. 슬롯 번호는 Behaviour의 ChargeSlot과 같아야 합니다.\n" +
             "실제 최대치는 Behaviour가 정하고, 여기 Max는 표시용입니다.");

        for (int i = 0; i < _charges.arraySize; i++)
        {
            var c = _charges.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"게이지 {i}", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                _charges.DeleteArrayElementAtIndex(i);
                EndStructural();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(c.FindPropertyRelative("Slot"), new GUIContent("슬롯"));
            EditorGUILayout.PropertyField(c.FindPropertyRelative("DisplayName"), new GUIContent("이름", "평문 또는 로컬라이제이션 키"));
            EditorGUILayout.PropertyField(c.FindPropertyRelative("BarColor"), new GUIContent("바 색"));
            EditorGUILayout.PropertyField(c.FindPropertyRelative("Max"), new GUIContent("최대 (표시용)"));
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("+ 게이지 추가", EditorStyles.miniButton, GUILayout.Width(100)))
        {
            _charges.arraySize++;
            var c = _charges.GetArrayElementAtIndex(_charges.arraySize - 1);
            c.FindPropertyRelative("Slot").intValue = _charges.arraySize - 1;
            c.FindPropertyRelative("DisplayName").stringValue = "";
            c.FindPropertyRelative("BarColor").colorValue = Color.white;
            c.FindPropertyRelative("Max").floatValue = 100f;
            EndStructural();
        }
    }

    // ══════════════════ 탭: 배정 · 진화 · 상점 ══════════════════

    void DrawRulesTab()
    {
        Section("배정");
        EditorGUILayout.PropertyField(_excludeFromRoll, new GUIContent("추첨 제외", "진화 전용 능력 등 — 처음 배정될 때 안 나옴"));

        using (new EditorGUI.DisabledScope(_excludeFromRoll.boolValue))
        {
            EditorGUILayout.PropertyField(_rollWeight, new GUIContent("추첨 가중치", "클수록 자주 나옴"));
            EditorGUILayout.PropertyField(_maxConcurrent, new GUIContent("동시 인원 제한", "한 게임에 이 능력을 가질 수 있는 인원. 0 = 무제한"));
        }

        if (Kind != AbilityKind.Random)
            Hint("가중치·인원 제한은 지금 랜덤 능력 추첨에만 적용됩니다 (메인·서브는 균등 추첨).");

        Section("진화");
        EditorGUILayout.PropertyField(_evolveToId, new GUIContent("진화 대상 ID", "0 = 진화 없음"));
        if (_evolveToId.intValue != 0)
        {
            EditorGUILayout.PropertyField(_evolveKillCount, new GUIContent("필요 막타 수"));
            Hint("진화 대상 능력은 '추첨 제외'를 켜두세요. 진화는 랜덤 능력에서만 동작합니다.");
        }

        Section("전용 상점");
        EditorGUILayout.PropertyField(_exclusiveShop, new GUIContent("전용 상점 항목",
            "이 능력이 메인일 때만 상점 '전용' 탭에 표시"), true);
        Hint($"메인 능력일 때만 표시 · 최대 {PlayerShop.MaxExclusive}개");
    }

    // ══════════════════ 추가 ══════════════════

    void AddEntry(SerializedProperty list, bool isSkill)
    {
        list.arraySize++;
        var el = list.GetArrayElementAtIndex(list.arraySize - 1);

        el.FindPropertyRelative("Name").stringValue = isSkill ? "새 스킬" : "새 패시브";
        el.FindPropertyRelative("Icon").objectReferenceValue = null;
        el.FindPropertyRelative("Description").stringValue = "";
        el.FindPropertyRelative("DetailedInfo").stringValue = "";
        el.FindPropertyRelative("Tip").stringValue = "";
        el.FindPropertyRelative("DevNote").stringValue = "";
        el.FindPropertyRelative("MaxLevel").intValue = isSkill ? 5 : 3;
        el.FindPropertyRelative("Vars").arraySize = 0;
        el.FindPropertyRelative("Effects").arraySize = 0;

        if (isSkill)
        {
            el.FindPropertyRelative("Slot").enumValueIndex = (int)NextFreeSlot();
            el.FindPropertyRelative("CooldownByLevel").arraySize = 0;
            el.FindPropertyRelative("Cooldown").floatValue = 8f;
            el.FindPropertyRelative("SoulCost").intValue = 0;
            el.FindPropertyRelative("UltimateChargeCost").intValue = 0;
            el.FindPropertyRelative("RequiredWeapon").intValue = 0;
            el.FindPropertyRelative("MotionData").objectReferenceValue = null;
            el.FindPropertyRelative("EffectTiming").enumValueIndex = 0;
            el.FindPropertyRelative("NeedsTarget").boolValue = false;
            el.FindPropertyRelative("HitMode").enumValueIndex = 0;
            el.FindPropertyRelative("Range").floatValue = 6f;
            el.FindPropertyRelative("HitAngle").floatValue = 60f;
            el.FindPropertyRelative("HitHeight").floatValue = 1.5f;
            el.FindPropertyRelative("MaxTargets").intValue = 1;
        }

        el.isExpanded = true;
    }

    SkillSlot NextFreeSlot()
    {
        if (Kind == AbilityKind.Random) return SkillSlot.Random;

        var used = new HashSet<int>();
        for (int i = 0; i < _skills.arraySize - 1; i++)    // 방금 추가한 칸 제외
            used.Add(_skills.GetArrayElementAtIndex(i).FindPropertyRelative("Slot").enumValueIndex);

        foreach (var s in new[] { SkillSlot.Skill1, SkillSlot.Skill2, SkillSlot.Skill3, SkillSlot.Ultimate })
            if (!used.Contains((int)s)) return s;

        return SkillSlot.Skill1;
    }

    static void AddEffect(SerializedProperty list, bool isSkill, int levels)
    {
        list.arraySize++;
        var e = list.GetArrayElementAtIndex(list.arraySize - 1);

        e.FindPropertyRelative("Trigger").enumValueIndex = (int)(isSkill ? TriggerType.OnSkillUse : TriggerType.Passive);
        e.FindPropertyRelative("Effect").enumValueIndex = (int)(isSkill ? EffectType.Damage : EffectType.StatMod);
        e.FindPropertyRelative("Target").enumValueIndex = (int)(isSkill ? TargetType.Victim : TargetType.Self);
        e.FindPropertyRelative("RequireStateId").intValue = 0;
        e.FindPropertyRelative("ChargeSlot").intValue = 0;
        e.FindPropertyRelative("CostChargeSlot").intValue = 0;
        e.FindPropertyRelative("ChargeCost").floatValue = 0f;
        e.FindPropertyRelative("Duration").floatValue = 0f;
        e.FindPropertyRelative("Chance").floatValue = 1f;
        e.FindPropertyRelative("Stat").enumValueIndex = 0;
        e.FindPropertyRelative("ModType").enumValueIndex = 0;
        e.FindPropertyRelative("PerTargetCooldown").floatValue = 0f;
        e.FindPropertyRelative("Interval").floatValue = 1f;

        var vals = e.FindPropertyRelative("Values");
        vals.arraySize = Mathf.Max(1, levels);
        for (int i = 0; i < vals.arraySize; i++) vals.GetArrayElementAtIndex(i).floatValue = 0f;
    }

    // ══════════════════ 설명 문구 ══════════════════
    // 지금 런타임 코드(AbilityRuntime.Execute / FireTrigger) 기준

    static void CollectEffectWarnings(EffectType eff, TargetType tgt, TriggerType trg,
                                      bool isSkill, bool needsTarget, List<string> outList)
    {
        if (IsUnimplemented(eff))
            outList.Add($"{eff}는 아직 구현되지 않아 아무 일도 일어나지 않습니다.");

        if (tgt == TargetType.AreaAroundSelf || tgt == TargetType.MarkedTarget)
            outList.Add($"대상 {tgt}는 아직 구현되지 않아 발동하지 않습니다.");

        if ((eff == EffectType.EnterState || eff == EffectType.Dash) && tgt != TargetType.Self)
            outList.Add($"{eff}는 항상 자신에게 적용됩니다. 대상을 Self로 두세요 (다른 대상이 없으면 발동 안 함).");

        if (isSkill)
        {
            if (!needsTarget && tgt != TargetType.Self)
                outList.Add("'대상 필요'가 꺼져 있어 맞힐 대상이 없습니다 → Self 외에는 발동하지 않습니다.");
            return;
        }

        if (trg == TriggerType.Passive && eff != EffectType.StatMod)
            outList.Add("발동 시점이 Passive면 StatMod(상시 스탯)만 동작합니다.");

        if (trg == TriggerType.Periodic || trg == TriggerType.OnSoulGain || trg == TriggerType.OnStateActive)
            outList.Add($"{trg} 시점은 아직 발동시키는 코드가 없습니다.");

        bool givesTarget = trg == TriggerType.OnHit || trg == TriggerType.OnHitTaken;
        if (tgt != TargetType.Self && !givesTarget && trg != TriggerType.Passive)
            outList.Add($"{trg} 시점은 대상 정보를 넘기지 않습니다 → Self만 동작합니다.");
    }

    static bool IsUnimplemented(EffectType e) =>
        e == EffectType.Blind || e == EffectType.Projectile || e == EffectType.MarkTarget;

    static string EffectDesc(EffectType e) => e switch
    {
        EffectType.StatMod => "스탯 변경",
        EffectType.Damage => "피해 — 스킬 피해 배율·치명타 적용 (궁극기 슬롯이면 궁극기 피해)",
        EffectType.Heal => "회복",
        EffectType.Seal => "능력 봉인 — 값 = 봉인 시간(초)",
        EffectType.GrantSoul => "소울 지급",
        EffectType.AddCharge => "게이지 증가 — 남에게 걸면 그 사람 게이지가 오름",
        EffectType.EnterState => "상태 진입 — Behaviour가 상태 ID로 반응",
        EffectType.Dash => "정면 수평 돌진",
        EffectType.Slow => "둔화 — 이동속도 감소",
        EffectType.ApplyStatus => "상태이상 부여 — 같은 종류는 높은 수치 우선",
        EffectType.AddAlter => "변화 붙이기 — 파츠 + 스탯. 같은 그룹은 교체",
        EffectType.RemoveAlter => "변화 떼기",

        _ => "<color=#E0A040>미구현</color>",
    };

    static string ValueLabel(EffectType e) => e switch
    {
        EffectType.StatMod => "스탯 수치",
        EffectType.Damage => "피해량",
        EffectType.Heal => "회복량",
        EffectType.Seal => "봉인 (초)",
        EffectType.GrantSoul => "소울",
        EffectType.AddCharge => "게이지 증가량",
        EffectType.EnterState => "상태 ID",
        EffectType.Dash => "거리 (m)",
        _ => "값",
    };

    static string TargetDesc(TargetType t) => t switch
    {
        TargetType.Self => "나",
        TargetType.Victim => "내가 때린 사람 / 스킬에 맞은 사람",
        TargetType.Attacker => "나를 때린 사람",
        TargetType.Killer => "나를 죽인 사람",
        TargetType.AimTarget => "스킬에 맞은 사람",
        _ => "<color=#E0A040>미구현</color>",
    };

    static string TriggerDesc(TriggerType t) => t switch
    {
        TriggerType.Passive => "항상 — StatMod만 동작 (능력을 가진 동안 상시 스탯)",
        TriggerType.OnSkillUse => "아무 스킬이나 썼을 때 · 대상 정보 없음",
        TriggerType.OnHit => "내가 때렸을 때 · 대상 = 맞은 사람(Victim)",
        TriggerType.OnHitTaken => "내가 맞았을 때 · 대상 = 때린 사람(Attacker)",
        TriggerType.OnKill => "막타를 쳤을 때 · 대상 정보 없음",
        TriggerType.OnDeath => "내가 죽었을 때 · 대상 정보 없음",
        TriggerType.OnRespawn => "부활했을 때",
        TriggerType.OnMove => "이동 중 1초마다",
        _ => "<color=#E0A040>아직 발동시키는 코드 없음</color>",
    };

    static string TimingDesc(AbilityData.SkillEffectTiming t) => t switch
    {
        AbilityData.SkillEffectTiming.OnHitFrame => "모션의 판정 구간에 발동 (공격형)",
        AbilityData.SkillEffectTiming.OnCastEnd => "모션이 끝난 뒤 발동 (버프형)",
        _ => "누르는 즉시 발동, 모션은 연출만",
    };

    /// <summary>PlayerStats.Recalculate의 실제 계산 기준</summary>
    static string StatHint(StatType s, ModType m)
    {
        bool multiplier = s == StatType.DamageDealt || s == StatType.DamageTaken ||
                          s == StatType.MeleeDamage || s == StatType.RangedDamage ||
                          s == StatType.SkillDamage || s == StatType.UltimateDamage;

        bool ratio = s == StatType.CritChance || s == StatType.CritDamage ||
                     s == StatType.CooldownReduction || s == StatType.LifeSteal;

        if (multiplier)
            return m == ModType.Flat
                ? "배율 스탯 · Flat = 실제 배율(기본 1.0)에 더함 — 0.25 = +25%p"
                : "배율 스탯 · Percent = 곱연산 — 20 = ×1.2";

        if (ratio)
            return m == ModType.Flat
                ? "비율 스탯 · 0.1 = 10%"
                : "<color=#E0A040>비율 스탯은 Percent가 무시됩니다 → Flat 사용 (0.1 = 10%)</color>";

        return m == ModType.Flat ? "실수치 — 25 = +25" : "퍼센트 — 25 = +25%";
    }

    // ══════════════════ 공용 ══════════════════

    void Section(string title)
    {
        GUILayout.Space(6);
        GUILayout.Label(title, _section);
    }

    static void Hint(string text) => GUILayout.Label(text, _hint);
    static void Warn(string text) => GUILayout.Label($"<color=#E0A040>⚠ {text}</color>", _hint);

    static void EnsureStyles()
    {
        if (_preview != null) return;

        _preview = new GUIStyle(EditorStyles.helpBox)
        {
            richText = true,
            wordWrap = true,
            fontSize = 12,
            padding = new RectOffset(8, 8, 6, 6),
        };

        _hint = new GUIStyle(EditorStyles.miniLabel) { richText = true, wordWrap = true };
        _token = new GUIStyle(EditorStyles.miniBoldLabel) { richText = true };
        _section = new GUIStyle(EditorStyles.boldLabel);
        _entryTitle = new GUIStyle(EditorStyles.foldout) { richText = true, fontStyle = FontStyle.Bold };
        _richFoldout = new GUIStyle(EditorStyles.foldout) { richText = true };
        _lv = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        _lvUnused = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        _lvUnused.normal.textColor = new Color(1f, 0.6f, 0.2f);
        _ok = new GUIStyle(EditorStyles.miniLabel);
        _ok.normal.textColor = new Color(0.4f, 0.8f, 0.4f);
        _descArea = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true,
            fontSize = 12,
            padding = new RectOffset(6, 6, 5, 5),
        };
    }
}
