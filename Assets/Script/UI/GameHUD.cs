using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class GameHUD : MonoBehaviour
{
    private NetworkRunner _runner;
    private GUIStyle _tooltipStyle;
    [SerializeField] private bool showDebugKeys = true;

    private int _lastStones = -1;
    private int _stoneDelta;
    private float _stoneFlash;

    const float RefWidth = 1920f;
    const float RefHeight = 1080f;

    private float _scale = 1f;

    // ══════════════════ 진입점 ══════════════════

    void OnGUI()
    {
        if (_runner == null)
        {
            _runner = FindFirstObjectByType<NetworkRunner>();
            return;
        }

        var local = _runner.GetPlayerObject(_runner.LocalPlayer);
        if (local == null) return;
        if (!local.TryGetComponent<PlayerHealth>(out var hp)) return;

        // 기준 해상도(1920x1080)로 스케일
        _scale = Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);

        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                   new Vector3(_scale, _scale, 1f));

        DrawAll(local, hp);

        GUI.matrix = prevMatrix;
    }

    void DrawAll(NetworkObject local, PlayerHealth hp)
    {
        var ar = local.GetComponent<AbilityRuntime>();

        //DrawStatus(hp);
        //DrawCrosshair();

        if (ar != null)
        {
            //DrawCharge(ar);
            //DrawRandomAbility(ar);
            //DrawSkillBar(ar);
            //DrawDanger(ar);
        }

        //DrawScoreboard(_runner);
        DrawDebugKeys();
        //DrawRoundState();
        //DrawShop(local, local.GetComponent<PlayerShop>(), hp);
        //DrawInteract(local);
        DrawStoneGain(local.GetComponent<PlayerEnchant>());
    }

    /// <summary>스크린 좌표를 기준 해상도 좌표로</summary>
    Vector2 GuiMousePos()
    {
        return new Vector2(Input.mousePosition.x / _scale,
                           (Screen.height - Input.mousePosition.y) / _scale);
    }

    // ══════════════════ 상호작용 ══════════════════

    void DrawInteract(NetworkObject local)
    {
        if (!local.TryGetComponent<PlayerInteract>(out var pi)) return;

        var chest = pi.CurrentTarget;
        if (chest == null || chest.IsOpened) return;

        float cx = RefWidth / 2f;
        float y = RefHeight / 2f + 60f;

        GUI.Label(new Rect(cx - 100f, y, 200f, 24f), $"[E] 열기  ({chest.Tier})");

        var bar = new Rect(cx - 90f, y + 26f, 180f, 10f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Box(bar, "");
        GUI.color = new Color(1f, 0.85f, 0.3f);
        GUI.Box(new Rect(bar.x, bar.y, bar.width * chest.Progress, bar.height), "");
        GUI.color = Color.white;
    }

    // ══════════════════ 스톤 획득 ══════════════════

    void DrawStoneGain(PlayerEnchant en)
    {
        if (en == null) return;

        if (Event.current.type != EventType.Repaint)
        {
            if (_stoneFlash > 0f) DrawStoneLabel();
            return;
        }

        if (_lastStones >= 0 && en.Stones > _lastStones)
        {
            _stoneDelta = en.Stones - _lastStones;
            _stoneFlash = 1.5f;
        }
        _lastStones = en.Stones;

        if (_stoneFlash <= 0f) return;
        _stoneFlash -= Time.deltaTime;

        DrawStoneLabel();
    }

    void DrawStoneLabel()
    {
        GUI.color = new Color(0.6f, 0.9f, 1f, Mathf.Clamp01(_stoneFlash));
        GUI.Label(new Rect(RefWidth / 2f - 60f, RefHeight / 2f + 100f, 200f, 24f),
            $"인챈트 스톤 +{_stoneDelta}");
        GUI.color = Color.white;
    }

    // ══════════════════ 상점 ══════════════════

    void DrawShop(NetworkObject local, PlayerShop shop, PlayerHealth hp)
    {
        if (shop == null) return;

        var db = shop.Database;
        if (db == null) return;

        if (!shop.IsShopping)
        {
            bool blocked = hp.InCombat && !hp.IsDead;
            GUI.color = blocked ? new Color(1f, 0.5f, 0.5f) : Color.white;
            GUI.Label(new Rect(20f, RefHeight - 40f, 300f, 24f),
                blocked ? "전투 중 — 상점 이용 불가" : "[B] 상점");
            GUI.color = Color.white;
            return;
        }

        const float w = 420f;
        float listH = 44f + db.Count * 40f;
        float h = listH + 260f;
        float x = RefWidth / 2f - w / 2f;
        float y = 120f;

        GUI.color = new Color(0f, 0f, 0f, 0.9f);
        GUI.Box(new Rect(x, y, w, h), "");
        GUI.color = Color.white;

        GUI.Label(new Rect(x + 16f, y + 10f, w, 24f),
            "상점  —  [B] 닫기  ·  이동/공격 불가");

        for (int i = 0; i < db.Count; i++)
        {
            var item = db.GetByIndex(i);
            if (item == null) continue;

            float ry = y + 44f + i * 40f;
            int lv = shop.GetLevel(i);
            bool ok = shop.CanBuy(i, out int price, out string reason);

            GUI.Label(new Rect(x + 16f, ry, 200f, 22f),
                item.IsRepeatable
                    ? item.DisplayName
                    : $"{item.DisplayName}  Lv.{lv}/{item.MaxLevel}");

            GUI.Label(new Rect(x + 220f, ry, 120f, 22f), ok ? $"◆{price}" : reason);

            using (new GUIEnabledScope(ok))
            {
                if (GUI.Button(new Rect(x + w - 90f, ry - 2f, 70f, 26f), "구매"))
                    shop.RPC_Buy(i);
            }
        }

        float enchantY = y + listH + 16f;

        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        GUI.Box(new Rect(x + 16f, enchantY - 8f, w - 32f, 1f), "");
        GUI.color = Color.white;

        DrawEnchantSection(local, x, enchantY, w);

        DrawStatPanel(local);
    }

    void DrawEnchantSection(NetworkObject local, float x, float y, float w)
    {
        if (!local.TryGetComponent<PlayerEnchant>(out var en)) return;
        if (!local.TryGetComponent<PlayerEquipment>(out var eq)) return;

        var weapon = eq.GetWeapon();

        GUI.Label(new Rect(x + 16f, y, 300f, 22f), $"인챈트  ·  스톤 {en.Stones}개");

        if (weapon == null || !weapon.CanEnchant)
        {
            GUI.Label(new Rect(x + 16f, y + 24f, 300f, 22f), "이 무기는 인챈트 불가");
            return;
        }

        int stage = en.ActiveStage;
        int max = Mathf.Min(weapon.MaxEnchantStage, PlayerEnchant.MaxEnchants);

        GUI.Label(new Rect(x + 16f, y + 24f, 300f, 22f),
            $"{weapon.DisplayName}  {stage}/{max}단계");

        var db = en.Database;
        for (int i = 0; i < stage; i++)
        {
            int id = en.GetEnchantId(eq.ActiveWeaponSlot, i);
            var e = db?.Get(id);
            if (e == null) continue;

            GUI.color = e.TierColor;
            GUI.Label(new Rect(x + 30f, y + 48f + i * 20f, w - 60f, 20f), e.BuildLine());
            GUI.color = Color.white;
        }

        float by = y + 56f + stage * 20f;

        bool ok = en.CanEnchant(out string reason, out int cost);
        using (new GUIEnabledScope(ok))
        {
            if (GUI.Button(new Rect(x + 16f, by, 180f, 28f),
                ok ? $"인챈트 (스톤 {cost})" : reason))             
                en.RPC_Enchant();
        }
    }

    struct GUIEnabledScope : System.IDisposable
    {
        private readonly bool _prev;
        public GUIEnabledScope(bool on) { _prev = GUI.enabled; GUI.enabled = on; }
        public void Dispose() => GUI.enabled = _prev;
    }

    // ══════════════════ 라운드 ══════════════════

    void DrawRoundState()
    {
        var rm = RoundManager.Instance;
        if (rm == null) return;

        if (rm.GameOver)
        {
            var box = new Rect(RefWidth / 2f - 180f, RefHeight / 2f - 60f, 360f, 120f);

            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Box(box, "");
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 20f, box.y + 20f, box.width - 40f, 30f),
                $"Player {rm.Winner.PlayerId} 승리!");
            GUI.Label(new Rect(box.x + 20f, box.y + 55f, box.width - 40f, 30f),
                $"{rm.WinnerSoul} 소울");
            GUI.Label(new Rect(box.x + 20f, box.y + 85f, box.width - 40f, 30f),
                "곧 새 라운드가 시작됩니다");
        }
        else
        {
            GUI.Label(new Rect(RefWidth / 2f - 100f, 20f, 300f, 24f),
                $"목표 {rm.TargetSoul}  ·  1위 {rm.GetTopSoul()}");
        }
    }

    // ══════════════════ 디버그 ══════════════════

    void DrawDebugKeys()
    {
        if (!showDebugKeys) return;

        var box = new Rect(RefWidth - 210f, 20f, 200f, 170f);

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Box(box, "");
        GUI.color = new Color(1f, 1f, 1f, 0.7f);

        GUI.Label(new Rect(box.x + 8f, box.y + 6f, 190f, 160f),
            "F1  소울 +50\n" +
            "F2  소울 -25\n" +
            "F5  더미 소환\n" +
            "F6  더미 제거\n" +
            "F7  랜덤 능력 교체\n" +
            "F8  자해 사망\n" +
            "F9  체력 회복\n" +
            "Tab 순위표");

        GUI.color = Color.white;
    }

    // ══════════════════ 상태 ══════════════════

    void DrawStatus(PlayerHealth hp)
    {
        // ── HP 바 ──
        float maxHp = 100f;
        if (hp.TryGetComponent<PlayerStats>(out var st))
            maxHp = Mathf.Max(1f, st.Get(StatType.MaxHP));

        float ratio = Mathf.Clamp01(hp.HP / maxHp);

        var barBg = new Rect(20f, RefHeight - 90f, 280f, 22f);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Box(barBg, "");

        GUI.color = ratio > 0.5f ? new Color(0.3f, 0.85f, 0.4f)
                  : ratio > 0.25f ? new Color(0.95f, 0.8f, 0.2f)
                                  : new Color(0.9f, 0.25f, 0.2f);
        GUI.Box(new Rect(barBg.x, barBg.y, barBg.width * ratio, barBg.height), "");

        GUI.color = Color.white;
        GUI.Label(new Rect(barBg.x + 8f, barBg.y + 1f, 280f, 22f),
            $"{hp.HP} / {Mathf.RoundToInt(maxHp)}");

        // ── 소울 ──
        GUI.Label(new Rect(20f, RefHeight - 116f, 300f, 24f), $"◆ SOUL  {hp.Soul}");

        // ── 탄약 (왼쪽 아래) ──
        if (hp.TryGetComponent<PlayerEquipment>(out var eq))
        {
            var w = eq.GetWeapon();
            if (w != null && w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun)
            {
                GUI.color = eq.Ammo > 0 ? Color.white : new Color(1f, 0.35f, 0.3f);
                GUI.Label(new Rect(20f, RefHeight - 62f, 200f, 30f),
                    $"{eq.Ammo} / {w.MaxAmmo}");
                GUI.color = Color.white;
            }
        }

        // ── KDA (좌상단) ──
        GUI.Label(new Rect(20f, 20f, 300f, 24f),
            $"K {hp.Kills}  A {hp.Assists}  D {hp.Deaths}");

        if (hp.IsDead)
            GUI.Label(new Rect(RefWidth / 2f - 40f, RefHeight / 2f, 200f, 30f), "사망");
    }

    static readonly (StatType stat, string label, string fmt)[] StatRows =
{
    (StatType.MaxHP,             "최대 체력",   "0"),
    (StatType.AttackPower,       "공격력",      "0.#"),
    (StatType.Defense,           "방어력",      "0.#"),
    (StatType.MoveSpeed,         "이동 속도",   "0.##"),
    (StatType.AttackSpeed,       "공격 속도",   "0.##"),
    (StatType.CritChance,        "치명타 확률", "P1"),
    (StatType.CritDamage,        "치명타 피해", "P0"),
    (StatType.CooldownReduction, "쿨타임 감소", "P1"),
    (StatType.LifeSteal,         "흡혈",        "P1"),
    (StatType.DamageDealt,       "가하는 피해", "P0"),
    (StatType.DamageTaken,       "받는 피해",   "P0"),
    (StatType.MeleeDamage,       "근접 피해",   "P0"),
    (StatType.RangedDamage,      "원거리 피해", "P0"),
    (StatType.SkillDamage,       "스킬 피해",   "P0"),
    (StatType.UltimateDamage,    "궁극기 피해", "P0"),
};

    void DrawStatPanel(NetworkObject local)
    {
        if (!local.TryGetComponent<PlayerStats>(out var st)) return;

        const float w = 260f;
        float h = 40f + StatRows.Length * 20f;
        float x = 40f;
        float y = 120f;

        GUI.color = new Color(0f, 0f, 0f, 0.9f);
        GUI.Box(new Rect(x, y, w, h), "");
        GUI.color = Color.white;

        GUI.Label(new Rect(x + 12f, y + 8f, w, 22f), "현재 스탯");

        for (int i = 0; i < StatRows.Length; i++)
        {
            var (stat, label, fmt) = StatRows[i];
            float v = st.Get(stat);
            float ry = y + 32f + i * 20f;

            GUI.Label(new Rect(x + 12f, ry, 140f, 20f), label);

            // 배율 스탯은 1.0이 기준이라 색으로 구분
            bool isMul = stat == StatType.DamageDealt || stat == StatType.DamageTaken
                      || stat == StatType.MeleeDamage || stat == StatType.RangedDamage
                      || stat == StatType.SkillDamage || stat == StatType.UltimateDamage;

            if (isMul)
            {
                if (v > 1.001f) GUI.color = new Color(0.5f, 1f, 0.6f);
                else if (v < 0.999f) GUI.color = new Color(1f, 0.6f, 0.5f);
            }
            else if (v > 0f) GUI.color = new Color(0.8f, 0.9f, 1f);

            GUI.Label(new Rect(x + 150f, ry, 100f, 20f), v.ToString(fmt));
            GUI.color = Color.white;
        }
    }
    void DrawCrosshair()
    {
        GUI.Label(new Rect(RefWidth / 2f - 4f, RefHeight / 2f - 10f, 20f, 20f), "+");
    }

    void DrawCharge(AbilityRuntime ar)
    {
        var esa = ar.FindAbility<EmpoweredStrikeAbility>();
        if (esa == null) return;

        float r = esa.ChargeRatio;
        var bar = new Rect(RefWidth / 2f - 100f, RefHeight - 130f, 200f, 14f);

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Box(bar, "");

        GUI.color = esa.IsReady ? new Color(1f, 0.85f, 0.2f) : new Color(0.4f, 0.6f, 1f);
        GUI.Box(new Rect(bar.x, bar.y, bar.width * r, bar.height), "");

        GUI.color = Color.white;
        GUI.Label(new Rect(bar.x, bar.y - 18f, 200f, 18f),
            esa.IsReady ? "일격 준비!" : $"{r * 100f:F0}%");
    }

    void DrawRandomAbility(AbilityRuntime ar)
    {
        var rand = ar.GetRandomAbility();
        if (rand == null) return;

        GUI.Label(new Rect(20f, 70f, 400f, 30f), $"이번 생: {rand.DisplayName}");
    }

    // ══════════════════ 스킬바 ══════════════════

    void DrawSkillBar(AbilityRuntime ar)
    {
        if (_tooltipStyle == null)
        {
            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                richText = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(10, 10, 8, 8),
                fontSize = 12
            };
        }

        const float boxW = 60f;
        const float gap = 8f;

        int count = SkillSlotExt.Count;
        float total = count * boxW + (count - 1) * gap;
        float x = RefWidth / 2f - total / 2f;
        float y = RefHeight - 90f;

        Vector2 mouse = GuiMousePos();

        AbilityData.SkillDef hovered = null;
        Rect hoveredRect = default;

        for (int i = 0; i < count; i++)
        {
            var skill = ar.GetSlot(i);
            var slot = (SkillSlot)i;
            var box = new Rect(x + i * (boxW + gap), y, boxW, boxW);

            GUI.Box(box, "");
            GUI.Label(new Rect(box.x + 4f, box.y + 3f, boxW - 8f, 16f), slot.Label());

            if (skill == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                GUI.Label(new Rect(box.x + 24f, box.y + 22f, 20f, 20f), "－");
                GUI.color = Color.white;
                continue;
            }

            bool weaponOk = ar.CanUseWeapon(i);

            if (!weaponOk) GUI.color = new Color(1f, 0.5f, 0.4f, 0.7f);
            GUI.Label(new Rect(box.x + 4f, box.y + 20f, boxW - 8f, 34f), skill.Name);
            GUI.color = Color.white;

            float ratio = ar.GetCooldownRatio(i);
            if (ratio > 0f)
            {
                float remain = ar.GetCooldownRemain(i);

                var cdRect = new Rect(box.x, box.y + box.height * (1f - ratio),
                                      box.width, box.height * ratio);
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.Box(cdRect, "");
                GUI.color = Color.white;

                GUI.Label(new Rect(box.x + 18f, box.y + 38f, 40f, 18f), $"{remain:F1}");
            }

            if (skill.SoulCost > 0)
            {
                GUI.color = new Color(0.6f, 0.8f, 1f);
                GUI.Label(new Rect(box.x + 4f, box.y + boxW - 18f, boxW - 8f, 16f),
                    $"◆{skill.SoulCost}");
                GUI.color = Color.white;
            }

            if (box.Contains(mouse))
            {
                hovered = skill;
                hoveredRect = box;
            }
        }

        if (hovered != null) DrawTooltip(hovered, hoveredRect);
    }

    void DrawTooltip(AbilityData.SkillDef skill, Rect anchor)
    {
        string text = AbilityData.BuildTooltip(skill);

        const float w = 320f;
        float h = _tooltipStyle.CalcHeight(new GUIContent(text), w);

        float x = Mathf.Clamp(anchor.x + anchor.width / 2f - w / 2f, 10f, RefWidth - w - 10f);
        float y = anchor.y - h - 8f;

        GUI.Box(new Rect(x, y, w, h), text, _tooltipStyle);
    }

    void DrawDanger(AbilityRuntime ar)
    {
        var bomb = ar.FindAbility<AnthropophobiaAbility>();
        if (bomb == null || !bomb.IsCounting) return;

        float r = bomb.DangerRatio;

        // 화면 전체를 덮어야 하므로 스케일을 되돌려 그린다
        var prev = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;

        GUI.color = new Color(1f, 0f, 0f, r * 0.4f);
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), "");
        GUI.color = Color.white;

        GUI.matrix = prev;

        var box = new Rect(RefWidth / 2f - 100f, 140f, 200f, 30f);
        GUI.color = new Color(1f, 0.2f, 0.1f);
        GUI.Label(box, $"폭발까지 {(1f - r) * bomb.TriggerTime:F1}초!");
        GUI.color = Color.white;
    }

    // ══════════════════ 순위표 ══════════════════

    void DrawScoreboard(NetworkRunner runner)
    {
        if (!Input.GetKey(KeyCode.Tab)) return;

        var list = new List<(string name, int k, int a, int d, int soul, bool isLocal)>();

        foreach (var p in runner.ActivePlayers)
        {
            var obj = runner.GetPlayerObject(p);
            if (obj == null) continue;
            if (!obj.TryGetComponent<PlayerHealth>(out var h)) continue;

            list.Add(($"Player {p.PlayerId}", h.Kills, h.Assists, h.Deaths, h.Soul,
                      p == runner.LocalPlayer));
        }

        if (list.Count == 0) return;

        list.Sort((a, b) =>
        {
            int c = b.soul.CompareTo(a.soul);
            return c != 0 ? c : b.k.CompareTo(a.k);
        });

        const float w = 460f;
        const float rowH = 26f;
        const float headH = 30f;

        float x = RefWidth / 2f - w / 2f;
        float y = 90f;
        float h2 = headH + rowH * list.Count + 12f;

        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Box(new Rect(x, y, w, h2), "");
        GUI.color = Color.white;

        var head = new Rect(x + 12f, y + 6f, w - 24f, headH);
        GUI.Label(head, "#    이름              K     A     D    SOUL");

        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        GUI.Box(new Rect(x + 12f, y + headH, w - 24f, 1f), "");
        GUI.color = Color.white;

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var row = new Rect(x + 12f, y + headH + 4f + rowH * i, w - 24f, rowH);

            if (e.isLocal)
            {
                GUI.color = new Color(0.3f, 0.6f, 1f, 0.25f);
                GUI.Box(new Rect(x + 8f, row.y - 2f, w - 16f, rowH), "");
                GUI.color = Color.white;
            }

            GUI.Label(row,
                $"{i + 1,-4} {e.name,-16} {e.k,3}   {e.a,3}   {e.d,3}   {e.soul,4}");
        }
    }
}