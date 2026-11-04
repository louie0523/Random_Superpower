using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = GameUiKit;

/// <summary>
/// 게임 HUD v2 (스티커 카툰) — 'UI 프리팹 만들기 창'의 '게임 HUD'가 이걸로 프리팹을 만듦.
///   왼쪽 위: 소울 + 랜덤 능력 알림 (RandomAbilityHud)
///   가운데 위: 목표 · 결과 (RoundStatusView)
///   왼쪽 아래: 얼굴 스티커 · 이름 · 쉴드 · 체력 · 전투 중 (InspectorBar)
///   가운데 아래: 스킬 칸 6개 — 육각 스티커 + 키 이름표 (SkillBar · SkillSlotView)
///   오른쪽 아래: 탄약 (AmmoDisplay)
/// 기존 컴포넌트를 그대로 쓰고 모양만 새로 — 연결은 UIManager가 UiPrefabSet의 'Use Hud'를 보고 알아서.
/// 조준점 · 상호작용 안내 · 충전 게이지 · 상태이상 아이콘은 지금 씬 것을 그대로 씀.
/// 색은 UiTheme, 제목 글꼴은 UiPrefabSet.DisplayFont.
/// </summary>
public static class HudUiBuilder
{
    /// <summary>에디터가 넘겨주는 스티커 그림 (없으면 코드 육각형)</summary>
    public struct Icons
    {
        public Sprite Soul;       // 소울 (노랑 해골 스티커)
        public Sprite Portrait;   // 내 얼굴 칸 (어두운 해골 스티커)
    }

    /// <summary>게임 HUD — v2 모양 그대로 (메뉴 창이 v7이어도 HUD는 안 바뀜)</summary>
    public static GameObject Build(GameObject host, TMP_FontAsset font, Icons icons)
    {
        using (K.Classic()) return BuildHud(host, font, icons);
    }

    static GameObject BuildHud(GameObject host, TMP_FontAsset font, Icons icons)
    {
        var display = K.Display(font);
        var root = K.MakeScreen(host, 100, null, true);     // 씬 UI가 스크린 카메라 모드면 같이
        var gr = host.GetComponent<GraphicRaycaster>();
        if (gr != null) Object.DestroyImmediate(gr);        // HUD는 클릭 안 받음
        host.AddComponent<HudScaler>();                      // 설정 'HUD 크기'

        var tpl = K.Rect("_Templates", root);
        K.Fill(tpl);

        BuildScreenFx(root);                                // 맨 뒤 — 화상 · 중독 가장자리 등
        BuildPlayerInfo(root, font, display, icons);
        BuildRandomHud(root, font);
        BuildRound(root, font, display);
        BuildSkills(root, font, display);
        BuildAmmo(root, font, display);
        BuildInteract(root, font, display);
        BuildCharge(root, font);
        BuildStatusIcons(root, tpl, font, display);
        BuildVoice(root);
        BuildChat(root, tpl, font);

        tpl.SetAsLastSibling();
        tpl.gameObject.SetActive(false);                    // 틀 묶음만 끔 (틀은 켜진 채)
        return host;
    }

    // ══════════════════ 소울 · 체력 (InspectorBar) ══════════════════

    static void BuildPlayerInfo(RectTransform root, TMP_FontAsset font, TMP_FontAsset display, Icons icons)
    {
        var pi = K.Rect("PlayerInfo", root);
        K.Fill(pi);
        var bar = pi.gameObject.AddComponent<InspectorBar>();

        // ── 왼쪽 위: 소울 ──
        var soul = Corner(pi, "Soul", new Vector2(0f, 1f), new Vector2(32f, -22f), new Vector2(360f, 72f));
        var soulIcon = Sticker(soul, "Icon", icons.Soul, K.Gold, 64f, -5f);
        Place(soulIcon, 0f, 0f, 64f, 64f, new Vector2(0f, 0.5f));
        var soulNum = Shadowed(K.Text("Value", soul, display, 50f, K.Light, TextAlignmentOptions.MidlineLeft));
        Place(soulNum.rectTransform, 76f, 6f, 260f, 56f, new Vector2(0f, 0.5f));
        soulNum.text = "0";
        var soulLb = Shadowed(K.Text("Label", soul, font, 15f, K.Gold, TextAlignmentOptions.MidlineLeft, true));
        Place(soulLb.rectTransform, 78f, -24f, 200f, 20f, new Vector2(0f, 0.5f));
        soulLb.text = "소울";
        var soulLoc = soulLb.gameObject.AddComponent<LocText>();

        // ── 왼쪽 아래: 얼굴 · 이름 · 쉴드 · 체력 ──
        var hp = Corner(pi, "Health", new Vector2(0f, 0f), new Vector2(32f, 28f), new Vector2(420f, 92f));
        var face = Sticker(hp, "Portrait", icons.Portrait, K.Raised, 72f, 4f);
        Place(face, 0f, 0f, 72f, 72f, new Vector2(0f, 0f));
        var portraitImg = face.GetComponentInChildren<Image>();

        // 내 캐릭터 얼굴 (작은 카메라 그림) — 머리를 찾으면 스티커 위에 육각으로 덮음 (HudPortrait)
        var live = K.Rect("Live", face);
        K.Fill(live, 6f);
        var liveMask = live.gameObject.AddComponent<Image>();
        liveMask.sprite = K.Hex;
        liveMask.raycastTarget = false;
        live.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var shot = K.Rect("Shot", live);
        K.Fill(shot);
        var raw = shot.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        var portrait = face.gameObject.AddComponent<HudPortrait>();
        portrait.Target = raw;
        portrait.LiveRoot = live.gameObject;
        portrait.Background = new Color(K.Field.r, K.Field.g, K.Field.b, 1f);
        live.gameObject.SetActive(false);

        var nick = Shadowed(K.Text("Name", hp, font, 17f, K.Light, TextAlignmentOptions.BottomLeft, true));
        Place(nick.rectTransform, 86f, 70f, 240f, 22f, new Vector2(0f, 0f));
        nick.text = "Player";

        // 전투 중 표시
        var combat = K.Rect("InCombat", hp);
        Place(combat, 270f, 70f, 96f, 22f, new Vector2(0f, 0f));
        var cbg = K.Img("Back", combat, K.Cut, new Color(K.Danger.r, K.Danger.g, K.Danger.b, 0.9f));
        K.Fill(cbg.rectTransform);
        var ct = K.Text("Text", combat, font, 13f, K.TextOn(K.Danger), TextAlignmentOptions.Center, true);
        K.Fill(ct.rectTransform);
        ct.text = "전투 중";
        var combatLoc = ct.gameObject.AddComponent<LocText>();
        combat.gameObject.SetActive(false);

        // 쉴드 (이름 아래 얇은 줄 — InspectorBar가 Regen · Soul · Temp 조각을 채움)
        var sh = K.Rect("ShieldBar", hp);
        Place(sh, 86f, 56f, 300f, 7f, new Vector2(0f, 0f));
        var shBg = sh.gameObject.AddComponent<Image>();
        shBg.sprite = K.Slant;
        shBg.color = K.HudPanel;
        shBg.raycastTarget = false;
        foreach (var (n, c) in new[] { ("Regen", UiTheme.Current.ShieldRegen), ("Soul", UiTheme.Current.ShieldSoul), ("Temp", UiTheme.Current.ShieldTemp) })
        {
            var seg = K.Img(n, sh, K.Slant, c);
            seg.rectTransform.anchorMin = Vector2.zero;
            seg.rectTransform.anchorMax = new Vector2(0f, 1f);
            seg.rectTransform.offsetMin = seg.rectTransform.offsetMax = Vector2.zero;
        }
        sh.gameObject.SetActive(false);

        // 체력 막대 (기울어진 막대, Filled)
        var hb = K.Rect("HpBar", hp);
        Place(hb, 86f, 26f, 300f, 24f, new Vector2(0f, 0f));
        var hbBg = K.Img("Back", hb, K.Slant, K.HudPanel);
        K.Fill(hbBg.rectTransform);
        var fill = K.Img("Fill", hb, K.Slant, K.Danger);
        K.Fill(fill.rectTransform, 2f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;

        var hpText = Shadowed(K.Text("Value", hp, display, 22f, K.Light, TextAlignmentOptions.TopLeft));
        Place(hpText.rectTransform, 92f, 0f, 300f, 26f, new Vector2(0f, 0f));
        hpText.text = "1000 / 1000";
        hpText.richText = true;

        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.18f, 0.27f), 0f), new GradientColorKey(K.Danger, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        Wire(bar, "nicknameText", nick);
        Wire(bar, "portrait", portraitImg);
        Wire(bar, "hpFill", fill);
        Wire(bar, "hpText", hpText);
        Wire(bar, "hpGradient", grad);
        Wire(bar, "shieldBar", sh);
        Wire(bar, "shieldRegenColor", UiTheme.Current.ShieldRegen);
        Wire(bar, "shieldSoulColor", UiTheme.Current.ShieldSoul);
        Wire(bar, "shieldTempColor", UiTheme.Current.ShieldTemp);
        Wire(bar, "soulText", soulNum);
        Wire(bar, "soulLabel", soulLoc);
        Wire(bar, "combatIcon", combat.gameObject);
        Wire(bar, "combatLabel", combatLoc);
    }

    // ══════════════════ 랜덤 능력 알림 ══════════════════

    const float Rw = 680f;      // 랜덤 능력 줄 폭 — 이름 · 요약이 안 잘리게 넓게 (요약은 줄바꿈)

    static void BuildRandomHud(RectTransform root, TMP_FontAsset font)
    {
        var rt = K.Rect("RandomAbilityHud", root);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(32f, -104f);
        rt.sizeDelta = new Vector2(Rw, 0f);
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 6f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var f = rt.gameObject.AddComponent<ContentSizeFitter>();
        f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var hud = rt.gameObject.AddComponent<RandomAbilityHud>();
        Wire(hud, "font", font);
        Wire(hud, "width", Rw);
        Wire(hud, "summarySize", 17f);
        Wire(hud, "background", K.HudPanel);
        Wire(hud, "rowParent", rt);
    }

    // ══════════════════ 목표 · 결과 ══════════════════

    static void BuildRound(RectTransform root, TMP_FontAsset font, TMP_FontAsset display)
    {
        var r = K.Rect("Round", root);
        K.Fill(r);
        var view = r.gameObject.AddComponent<RoundStatusView>();

        // 가운데 위 목표 칩
        var chip = K.Rect("Goal", r);
        chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0.5f, 1f);
        chip.anchoredPosition = new Vector2(0f, -20f);
        chip.sizeDelta = new Vector2(520f, 46f);
        chip.localRotation = K.Tilt(-1.2f);
        var cbg = K.Img("Back", chip, K.Cut, K.HudPanel);
        K.Fill(cbg.rectTransform);
        var goal = K.Text("Text", chip, display, 24f, K.Light, TextAlignmentOptions.Center);
        K.Fill(goal.rectTransform, 6f);
        goal.text = "목표";

        // 결과 (가운데)
        var res = K.Rect("Result", r);
        K.Center(res, 0f, 120f, 760f, 260f);
        var face = K.Sticker(res, K.Paper, 0f, 14f);
        var v = face.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(30, 30, 26, 26);
        v.spacing = 8f;
        v.childAlignment = TextAnchor.MiddleCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var win = K.Text("Winner", face.transform, display, 56f, K.Gold, TextAlignmentOptions.Center);
        K.Size(win, -1f, 74f);
        win.text = "승자";
        var ws = K.Text("WinnerSoul", face.transform, font, 26f, K.Ink, TextAlignmentOptions.Center, true);
        K.Size(ws, -1f, 36f);
        ws.text = "소울";
        var rs = K.Text("Restart", face.transform, font, 20f, K.Muted, TextAlignmentOptions.Center);
        K.Size(rs, -1f, 30f);
        rs.text = "다시 시작까지";
        res.gameObject.SetActive(false);

        Wire(view, "goalText", goal);
        Wire(view, "resultRoot", res.gameObject);
        Wire(view, "winnerText", win);
        Wire(view, "winnerSoulText", ws);
        Wire(view, "restartText", rs);
    }

    // ══════════════════ 스킬 칸 ══════════════════

    static void BuildSkills(RectTransform root, TMP_FontAsset font, TMP_FontAsset display)
    {
        var rt = K.Rect("SkillBar", root);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 24f);
        rt.sizeDelta = new Vector2(760f, 140f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.LowerCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var bar = rt.gameObject.AddComponent<SkillBar>();
        var slots = new SkillSlotView[]
        {
            Slot(rt, font, display, SkillSlot.Skill1, 72f, K.Light),
            Slot(rt, font, display, SkillSlot.Skill2, 72f, K.Light),
            Slot(rt, font, display, SkillSlot.Skill3, 72f, K.Light),
            Slot(rt, font, display, SkillSlot.Ultimate, 96f, K.Mint),
            Slot(rt, font, display, SkillSlot.Random, 72f, K.Gold),
            Slot(rt, font, display, SkillSlot.Random2, 72f, K.Gold),
        };
        Wire(bar, "slots", slots);
        slots[5].gameObject.SetActive(false);     // 두 번째 랜덤(조커)은 생기면 SkillBar가 켬

        // 랜덤 능력이 바뀌면 잠깐 뜨는 이름
        var rn = Shadowed(K.Text("RandomName", root, display, 26f, K.Gold, TextAlignmentOptions.Center));
        rn.rectTransform.anchorMin = rn.rectTransform.anchorMax = rn.rectTransform.pivot = new Vector2(0.5f, 0f);
        rn.rectTransform.anchoredPosition = new Vector2(0f, 214f);
        rn.rectTransform.sizeDelta = new Vector2(800f, 36f);
        rn.text = "";
        Wire(bar, "randomNameText", rn);
    }

    static SkillSlotView Slot(Transform parent, TMP_FontAsset font, TMP_FontAsset display, SkillSlot slot, float s, Color rimColor)
    {
        var rt = K.Rect("Slot_" + slot, parent);
        K.Size(rt, s * 1.12f, s + 30f);
        var view = rt.gameObject.AddComponent<SkillSlotView>();
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // 육각 스티커 (튀는 연출 대상)
        var punch = K.Rect("Sticker", rt);
        punch.anchorMin = punch.anchorMax = punch.pivot = new Vector2(0.5f, 1f);
        punch.anchoredPosition = Vector2.zero;
        punch.sizeDelta = new Vector2(s * 1.12f, s);
        float tilt = slot switch
        {
            SkillSlot.Skill1 => -3f, SkillSlot.Skill2 => 2f, SkillSlot.Skill3 => -2f,
            SkillSlot.Ultimate => -4f, SkillSlot.Random => 5f, _ => 3f,
        };
        punch.localRotation = K.Tilt(tilt);

        var glow = K.Img("ReadyGlow", punch, K.Hex, new Color(K.Mint.r, K.Mint.g, K.Mint.b, 0.85f));
        K.Fill(glow.rectTransform, -5f);
        glow.gameObject.SetActive(false);

        var rim = K.Img("Rim", punch, K.Hex, rimColor);
        K.Fill(rim.rectTransform);
        var face = K.Img("Face", rim.transform, K.Hex, K.Raised);
        K.Fill(face.rectTransform, s * 0.06f);

        var empty = K.Text("Empty", face.transform, font, s * 0.3f, K.Muted, TextAlignmentOptions.Center, true);
        K.Fill(empty.rectTransform);
        empty.text = "–";
        empty.gameObject.SetActive(false);

        // 아이콘이 없는 스킬은 키 글자를 크게 (UiShowWhenOff)
        var letter = K.Text("Letter", face.transform, display, s * 0.36f, K.Light, TextAlignmentOptions.Center);
        K.Fill(letter.rectTransform, s * 0.12f);
        letter.text = slot.Label();
        letter.enableAutoSizing = true;
        letter.fontSizeMin = s * 0.16f;
        letter.fontSizeMax = s * 0.36f;

        var icon = K.Img("Icon", face.transform, null, Color.white);
        K.Fill(icon.rectTransform, s * 0.16f);
        icon.preserveAspect = true;
        icon.enabled = false;

        var showLetter = rt.gameObject.AddComponent<UiShowWhenOff>();
        showLetter.Watch = icon;
        showLetter.Also = empty.gameObject;
        showLetter.Target = letter.gameObject;

        var active = K.Img("Active", face.transform, K.Hex, new Color(K.Mint.r, K.Mint.g, K.Mint.b, 0.3f));
        K.Fill(active.rectTransform);
        active.gameObject.SetActive(false);

        var cd = K.Img("Cooldown", face.transform, K.Hex, new Color(0f, 0f, 0f, 0.66f));
        K.Fill(cd.rectTransform);
        cd.type = Image.Type.Filled;
        cd.fillMethod = Image.FillMethod.Radial360;
        cd.fillOrigin = (int)Image.Origin360.Top;
        cd.fillClockwise = false;
        cd.fillAmount = 0f;

        var blocked = K.Img("Blocked", face.transform, K.Hex, new Color(K.Danger.r, K.Danger.g, K.Danger.b, 0.45f));
        K.Fill(blocked.rectTransform);
        var bx = K.Text("X", blocked.transform, display, s * 0.5f, K.Light, TextAlignmentOptions.Center);
        K.Fill(bx.rectTransform);
        bx.text = "×";
        blocked.gameObject.SetActive(false);

        var cdText = Shadowed(K.Text("CooldownText", face.transform, display, s * 0.4f, K.Light, TextAlignmentOptions.Center));
        K.Fill(cdText.rectTransform);
        cdText.text = "";
        cdText.gameObject.SetActive(false);

        // 무기 조건 안 맞음 — 작은 빨간 칩
        var locked = K.Rect("Locked", punch);
        locked.anchorMin = locked.anchorMax = locked.pivot = new Vector2(0.5f, 0.5f);
        locked.anchoredPosition = new Vector2(0f, -s * 0.05f);
        locked.sizeDelta = new Vector2(s * 0.8f, 20f);
        var lbg = K.Img("Back", locked, K.Cut, K.Danger);
        K.Fill(lbg.rectTransform);
        var lt = K.Text("Text", locked, font, 12f, K.TextOn(K.Danger), TextAlignmentOptions.Center, true);
        K.Fill(lt.rectTransform);
        lt.text = "무기";
        locked.gameObject.SetActive(false);

        // 소울 비용 (오른쪽 위 노란 숫자)
        var cost = Shadowed(K.Text("SoulCost", punch, display, 18f, K.Gold, TextAlignmentOptions.TopRight));
        cost.rectTransform.anchorMin = cost.rectTransform.anchorMax = cost.rectTransform.pivot = new Vector2(1f, 1f);
        cost.rectTransform.anchoredPosition = new Vector2(2f, 4f);
        cost.rectTransform.sizeDelta = new Vector2(50f, 22f);
        cost.text = "";
        cost.gameObject.SetActive(false);

        // 키 이름표 (아래)
        var key = K.Rect("Key", rt);
        key.anchorMin = key.anchorMax = key.pivot = new Vector2(0.5f, 0f);
        key.anchoredPosition = new Vector2(0f, 2f);
        key.sizeDelta = new Vector2(slot == SkillSlot.Ultimate ? 70f : 60f, 22f);
        var kbg = K.Img("Back", key, K.Cut, new Color(K.Dark.r, K.Dark.g, K.Dark.b, 0.85f));
        K.Fill(kbg.rectTransform);
        var kt = K.Text("Text", key, font, 13f, rimColor == K.Light ? K.Light : rimColor, TextAlignmentOptions.Center, true);
        K.Fill(kt.rectTransform, 2f);
        kt.text = slot.Label();
        kt.enableAutoSizing = true;
        kt.fontSizeMin = 9f;
        kt.fontSizeMax = 13f;

        Wire(view, "slot", slot);
        Wire(view, "icon", icon);
        Wire(view, "keyText", kt);
        Wire(view, "group", group);
        Wire(view, "cooldownFill", cd);
        Wire(view, "cooldownText", cdText);
        Wire(view, "readyGlow", glow.gameObject);
        Wire(view, "lockedMark", locked.gameObject);
        Wire(view, "soulCostText", cost);
        Wire(view, "emptyRoot", empty.gameObject);
        Wire(view, "activeMark", active.gameObject);
        Wire(view, "blockedMark", blocked.gameObject);
        Wire(view, "punchTarget", punch);
        Wire(view, "hideWhenEmpty", true);
        return view;
    }

    // ══════════════════ 상태이상 화면 연출 · 아이콘 ══════════════════

    static void BuildScreenFx(RectTransform root)
    {
        var fx = K.Rect("ScreenFx", root);
        K.Fill(fx);
        fx.gameObject.AddComponent<StatusScreenEffects>();   // 레이어(RawImage)는 실행할 때 스스로 만듦
    }

    /// <summary>상태이상 색 — 아이콘 그림이 없으면 이 색 육각 + 이름 두 글자</summary>
    static Color StatusColor(StatusType t) => t switch
    {
        StatusType.Slow => new Color(0.45f, 0.75f, 1f),
        StatusType.Burn => new Color(1f, 0.5f, 0.15f),
        StatusType.Frost => new Color(0.6f, 0.88f, 1f),
        StatusType.Seal => new Color(0.68f, 0.45f, 1f),
        StatusType.Stun => new Color(1f, 0.85f, 0.25f),
        StatusType.DeepWound => new Color(0.8f, 0.15f, 0.2f),
        StatusType.Poison => new Color(0.45f, 0.9f, 0.3f),
        StatusType.Evade => new Color(0.9f, 0.95f, 1f),
        StatusType.ArmorBreak => new Color(0.85f, 0.55f, 0.35f),
        StatusType.Ghost => new Color(0.75f, 0.8f, 0.9f),
        StatusType.Blind => new Color(0.4f, 0.4f, 0.48f),
        StatusType.Root => new Color(0.6f, 0.45f, 0.3f),
        StatusType.Invisible => new Color(0.8f, 0.9f, 0.95f),
        StatusType.Undead => new Color(0.55f, 0.5f, 0.7f),
        StatusType.Curse => new Color(0.9f, 0.3f, 0.75f),
        StatusType.Weaken => new Color(1f, 0.55f, 0.65f),
        _ => K.Light,
    };

    /// <summary>체력 위 상태이상 아이콘 줄 — 남은 시간 = 흰 테두리가 줄어듦, 오른쪽 아래 초, 오른쪽 위 중첩</summary>
    static void BuildStatusIcons(RectTransform root, RectTransform tpl, TMP_FontAsset font, TMP_FontAsset display)
    {
        var row = Corner(root, "StatusIcons", new Vector2(0f, 0f), new Vector2(118f, 128f), new Vector2(420f, 48f));
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        var bar = row.gameObject.AddComponent<StatusIconBar>();

        // 아이콘 하나 (틀)
        var it = K.Rect("StatusIcon", tpl);
        it.sizeDelta = new Vector2(48f, 44f);
        var back = K.Img("Back", it, K.Hex, K.HudPanel);
        K.Fill(back.rectTransform);
        var timer = K.Img("Timer", it, K.Hex, K.Light);
        K.Fill(timer.rectTransform);
        timer.type = Image.Type.Filled;
        timer.fillMethod = Image.FillMethod.Radial360;
        timer.fillOrigin = (int)Image.Origin360.Top;
        timer.fillClockwise = false;
        var face = K.Img("Face", it, K.Hex, K.Field);
        K.Fill(face.rectTransform, 3f);
        var nm = K.Text("Name", face.transform, font, 13f, K.Light, TextAlignmentOptions.Center, true);
        K.Fill(nm.rectTransform, 2f);
        nm.text = "화상";
        var icon = K.Img("Icon", face.transform, null, Color.white);
        K.Fill(icon.rectTransform, 5f);
        icon.preserveAspect = true;
        var time = Shadowed(K.Text("Time", it, display, 15f, K.Light, TextAlignmentOptions.BottomRight));
        Place(time.rectTransform, 4f, -6f, 30f, 18f, new Vector2(1f, 0f));
        time.text = "3";
        var stack = Shadowed(K.Text("Stack", it, display, 15f, K.Gold, TextAlignmentOptions.TopRight));
        Place(stack.rectTransform, 4f, 6f, 30f, 18f, new Vector2(1f, 1f));
        stack.text = "2";
        stack.gameObject.SetActive(false);

        var view = it.gameObject.AddComponent<StatusIconView>();
        Wire(view, "icon", icon);
        Wire(view, "timerFill", timer);
        Wire(view, "timeText", time);
        Wire(view, "stackText", stack);
        Wire(view, "nameText", nm);

        // 상태이상마다 한 칸 — 그림은 비어 있음 (프리팹에서 Icons 칸에 넣으면 그 그림)
        var types = (StatusType[])System.Enum.GetValues(typeof(StatusType));
        var defs = new StatusIconBar.IconDef[types.Length];
        for (int i = 0; i < types.Length; i++)
            defs[i] = new StatusIconBar.IconDef { Type = types[i], Icon = null, Color = StatusColor(types[i]) };

        Wire(bar, "parent", row);
        Wire(bar, "iconPrefab", view);
        Wire(bar, "icons", defs);
    }

    // ══════════════════ 마이크 상태 · 채팅 (왼쪽 아래 — 체력 · 상태이상 위로 쌓임) ══════════════════

    /// <summary>마이크 · 말하는 사람 — VoiceStatusHud (모양은 그대로, HUD 안이면 혼자 생긴 건 숨음)</summary>
    static void BuildVoice(RectTransform root)
    {
        var v = K.Rect("Voice", root);
        K.Fill(v);
        var hud = v.gameObject.AddComponent<VoiceStatusHud>();
        Wire(hud, "margin", new Vector2(32f, 186f));       // 상태이상 아이콘 줄 위
    }

    /// <summary>채팅창 — ChatView (열면 배경 · 입력 줄, 닫혀 있으면 새 줄만 잠깐)</summary>
    static void BuildChat(RectTransform root, RectTransform tpl, TMP_FontAsset font)
    {
        var holder = K.Rect("Chat", root);
        K.Fill(holder);
        var view = holder.gameObject.AddComponent<ChatView>();

        var box = Corner(holder, "Box", new Vector2(0f, 0f), new Vector2(32f, 300f), new Vector2(600f, 430f));

        var back = K.Img("OpenBack", box, K.Cut, new Color(K.HudPanel.r, K.HudPanel.g, K.HudPanel.b, 0.55f));
        K.Fill(back.rectTransform, -8f);
        back.gameObject.SetActive(false);

        var lines = K.Rect("Lines", box);
        lines.anchorMin = new Vector2(0f, 0f);
        lines.anchorMax = new Vector2(1f, 1f);
        lines.offsetMin = new Vector2(0f, 52f);
        lines.offsetMax = Vector2.zero;
        var vl = lines.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.LowerLeft;
        vl.spacing = 2f;
        vl.padding = new RectOffset(4, 4, 0, 4);
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        var line = Shadowed(K.Text("ChatLine", tpl, font, 20f, K.Light, TextAlignmentOptions.TopLeft));
        line.textWrappingMode = TextWrappingModes.Normal;
        line.overflowMode = TextOverflowModes.Overflow;
        line.richText = true;
        line.text = "<color=#ffffff>[경기]</color> <b>정루이</b>: 안녕";

        var row = K.Rect("InputRow", box);
        row.anchorMin = new Vector2(0f, 0f);
        row.anchorMax = new Vector2(1f, 0f);
        row.pivot = new Vector2(0.5f, 0f);
        row.offsetMin = Vector2.zero;
        row.offsetMax = new Vector2(0f, 44f);
        var ch = Shadowed(K.Text("Channel", row, font, 19f, K.Light, TextAlignmentOptions.MidlineLeft, true));
        ch.rectTransform.anchorMin = new Vector2(0f, 0f);
        ch.rectTransform.anchorMax = new Vector2(0f, 1f);
        ch.rectTransform.pivot = new Vector2(0f, 0.5f);
        ch.rectTransform.offsetMin = new Vector2(4f, 0f);
        ch.rectTransform.offsetMax = new Vector2(84f, 0f);
        ch.richText = true;
        ch.text = "[경기]";
        var input = K.Input("Input", row, font, 20f, "메시지 입력…", PlayerComms.MaxChatLength);
        var irt = (RectTransform)input.transform;
        irt.anchorMin = new Vector2(0f, 0f);
        irt.anchorMax = new Vector2(1f, 1f);
        irt.offsetMin = new Vector2(88f, 0f);
        irt.offsetMax = Vector2.zero;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;
        if (input.placeholder is TMP_Text ph) SetLocText.Add(ph, "ui.chat.placeholder");
        row.gameObject.SetActive(false);

        Wire(view, "lineParent", lines);
        Wire(view, "lineTemplate", line);
        Wire(view, "openBack", back.gameObject);
        Wire(view, "inputRow", row.gameObject);
        Wire(view, "channelText", ch);
        Wire(view, "input", input);
    }

    // ══════════════════ 상호작용 안내 · 충전 게이지 ══════════════════

    /// <summary>조준점 아래 — [E] 열기 · 상자 등급 · 여는 중 막대</summary>
    static void BuildInteract(RectTransform root, TMP_FontAsset font, TMP_FontAsset display)
    {
        var holder = K.Rect("Interact", root);
        K.Fill(holder);
        var ip = holder.gameObject.AddComponent<InteractPrompt>();

        var box = K.Rect("Box", holder);
        K.Center(box, 0f, -130f, 340f, 64f);
        var bg = K.Img("Back", box, K.Cut, K.HudPanel);
        K.Fill(bg.rectTransform);

        var key = K.Rect("Key", box);
        Place(key, 10f, 0f, 50f, 46f, new Vector2(0f, 0.5f));
        var kRim = K.Img("Rim", key, K.Hex, K.Light);
        K.Fill(kRim.rectTransform);
        var kFace = K.Img("Face", kRim.transform, K.Hex, K.Field);
        K.Fill(kFace.rectTransform, 3f);
        var kt = K.Text("Text", kFace.transform, display, 22f, K.Light, TextAlignmentOptions.Center);
        K.Fill(kt.rectTransform, 2f);
        kt.enableAutoSizing = true;
        kt.fontSizeMin = 10f;
        kt.fontSizeMax = 22f;
        kt.text = "E";

        var act = K.Text("Action", box, font, 22f, K.Light, TextAlignmentOptions.BottomLeft, true);
        Place(act.rectTransform, 72f, 4f, 250f, 28f, new Vector2(0f, 0.5f));
        act.rectTransform.pivot = new Vector2(0f, 0f);
        act.text = "열기";
        var tier = K.Text("Tier", box, font, 16f, K.Gold, TextAlignmentOptions.TopLeft, true);
        Place(tier.rectTransform, 72f, -2f, 250f, 22f, new Vector2(0f, 0.5f));
        tier.rectTransform.pivot = new Vector2(0f, 1f);
        tier.text = "희귀 상자";

        var pb = K.Rect("Progress", box);
        pb.anchorMin = new Vector2(0f, 0f);
        pb.anchorMax = new Vector2(1f, 0f);
        pb.pivot = new Vector2(0.5f, 1f);
        pb.anchoredPosition = new Vector2(0f, -6f);
        pb.sizeDelta = new Vector2(-20f, 8f);
        var pbg = K.Img("Back", pb, K.Slant, K.HudPanel);
        K.Fill(pbg.rectTransform);
        var pf = K.Img("Fill", pb, K.Slant, K.Mint);
        K.Fill(pf.rectTransform);
        pf.type = Image.Type.Filled;
        pf.fillMethod = Image.FillMethod.Horizontal;
        pf.fillAmount = 0.4f;
        box.gameObject.SetActive(false);

        Wire(ip, "root", box.gameObject);
        Wire(ip, "keyText", kt);
        Wire(ip, "actionLabel", act.gameObject.AddComponent<LocText>());
        Wire(ip, "tierLabel", tier.gameObject.AddComponent<LocText>());
        Wire(ip, "progressFill", pf);
    }

    /// <summary>스킬 칸 위 — 메인 능력 게이지 (이름 · % · 기울어진 막대)</summary>
    static void BuildCharge(RectTransform root, TMP_FontAsset font)
    {
        var holder = K.Rect("Charge", root);
        K.Fill(holder);
        var cg = holder.gameObject.AddComponent<ChargeGauge>();

        var box = K.Rect("Box", holder);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0f);
        box.anchoredPosition = new Vector2(0f, 172f);
        box.sizeDelta = new Vector2(360f, 38f);

        var label = Shadowed(K.Text("Label", box, font, 16f, K.Light, TextAlignmentOptions.BottomLeft, true));
        Place(label.rectTransform, 0f, 14f, 260f, 22f, new Vector2(0f, 0f));
        label.text = "게이지";
        var val = Shadowed(K.Text("Value", box, font, 16f, K.Light, TextAlignmentOptions.BottomRight, true));
        Place(val.rectTransform, 0f, 14f, 100f, 22f, new Vector2(1f, 0f));
        val.text = "60%";

        var bar = K.Rect("Bar", box);
        Place(bar, 0f, 0f, 360f, 12f, new Vector2(0.5f, 0f));
        var bbg = K.Img("Back", bar, K.Slant, K.HudPanel);
        K.Fill(bbg.rectTransform);
        var fill = K.Img("Fill", bar, K.Slant, K.Gold);
        K.Fill(fill.rectTransform, 1f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0.6f;
        box.gameObject.SetActive(false);

        Wire(cg, "root", box.gameObject);
        Wire(cg, "fill", fill);
        Wire(cg, "label", label);
        Wire(cg, "valueText", val);
        Wire(cg, "readyColor", K.Gold);
        Wire(cg, "disabledColor", K.LineColor);
    }

    // ══════════════════ 탄약 ══════════════════

    static void BuildAmmo(RectTransform root, TMP_FontAsset font, TMP_FontAsset display)
    {
        var holder = K.Rect("Ammo", root);
        K.Fill(holder);
        var ammo = holder.gameObject.AddComponent<AmmoDisplay>();

        var box = Corner(holder, "Box", new Vector2(1f, 0f), new Vector2(-36f, 26f), new Vector2(320f, 90f));
        var cur = Shadowed(K.Text("Current", box, display, 72f, K.Light, TextAlignmentOptions.BottomRight));
        Place(cur.rectTransform, -96f, 0f, 220f, 86f, new Vector2(1f, 0f));
        cur.text = "60";
        var max = Shadowed(K.Text("Max", box, display, 28f, K.Muted, TextAlignmentOptions.BottomLeft));
        Place(max.rectTransform, -2f, 8f, 92f, 36f, new Vector2(1f, 0f));
        max.text = "/ 60";

        Wire(ammo, "root", box.gameObject);
        Wire(ammo, "currentText", cur);
        Wire(ammo, "maxText", max);
        Wire(ammo, "normalColor", K.Light);
        Wire(ammo, "lowColor", K.Gold);
        Wire(ammo, "emptyColor", K.Danger);
    }

    // ══════════════════ 도우미 ══════════════════

    /// <summary>화면 모서리에 붙는 칸 — anchor = 붙을 모서리 (0,1 = 왼쪽 위)</summary>
    static RectTransform Corner(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = K.Rect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>pivot 기준으로 부모 안에 놓기</summary>
    static void Place(RectTransform rt, float x, float y, float w, float h, Vector2 anchor)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>스티커 — 그림이 있으면 그 그림, 없으면 흰 테두리 육각형 + 면 색 (살짝 기울임)</summary>
    static RectTransform Sticker(Transform parent, string name, Sprite sprite, Color face, float size, float tilt)
    {
        var rt = K.Rect(name, parent);
        rt.sizeDelta = new Vector2(size, size);
        rt.localRotation = K.Tilt(tilt);
        if (sprite != null)
        {
            var img = K.Img("Image", rt, sprite, Color.white);
            K.Fill(img.rectTransform);
            img.preserveAspect = true;
        }
        else
        {
            var rim = K.Img("Rim", rt, K.Hex, K.Light);
            K.Fill(rim.rectTransform, size * 0.06f);
            var f = K.Img("Image", rim.transform, K.Hex, face);
            K.Fill(f.rectTransform, size * 0.06f);
        }
        return rt;
    }

    /// <summary>게임 화면 위에서도 읽히게 글자 밑에 옅은 그림자</summary>
    static TextMeshProUGUI Shadowed(TextMeshProUGUI t)
    {
        t.gameObject.AddComponent<UiTextOutline>();      // 실행할 때 TMP 외곽선
        return t;
    }

    /// <summary>컴포넌트의 [SerializeField] private 칸에 값 넣기 (프리팹 만들 때만)</summary>
    static void Wire(Object target, string field, object value)
    {
        var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null)
        {
            Debug.LogWarning($"[HUD] {target.GetType().Name}에 '{field}' 칸이 없음 — 건너뜀");
            return;
        }
        if (value is SkillSlotView[] arr && f.FieldType == typeof(SkillSlotView[])) { f.SetValue(target, arr); return; }
        f.SetValue(target, value);
    }
}
