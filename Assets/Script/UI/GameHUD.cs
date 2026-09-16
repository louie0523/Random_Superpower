using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class GameHUD : MonoBehaviour
{
    private NetworkRunner _runner;
    private GUIStyle _tooltipStyle;
    [SerializeField] private bool showDebugKeys = true;

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

        var ar = local.GetComponent<AbilityRuntime>();

        DrawStatus(hp);
        DrawCrosshair();

        if (ar != null)
        {
            DrawCharge(ar);
            DrawRandomAbility(ar);
            DrawSkillBar(ar);
            DrawDanger(ar);
        }

        DrawScoreboard(_runner);
        DrawDebugKeys();
        DrawRoundState();
    }

    void DrawRoundState()
    {
        var rm = RoundManager.Instance;
        if (rm == null) return;

        if (rm.GameOver)
        {
            var box = new Rect(Screen.width / 2f - 180f, Screen.height / 2f - 60f, 360f, 120f);

            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Box(box, "");
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x, box.y + 20f, box.width, 30f),
                $"Player {rm.Winner.PlayerId} 승리!");
            GUI.Label(new Rect(box.x, box.y + 55f, box.width, 30f),
                $"{rm.WinnerSoul} 소울");
            GUI.Label(new Rect(box.x, box.y + 85f, box.width, 30f),
                "곧 새 라운드가 시작됩니다");
        }
        else
        {
            // 목표 진행도
            GUI.Label(new Rect(Screen.width / 2f - 60f, 20f, 200f, 24f),
                $"목표 {rm.TargetSoul}  ·  1위 {rm.GetTopSoul()}");
        }
    }

    void DrawDebugKeys()
    {
        if (!showDebugKeys) return;

        var box = new Rect(Screen.width - 210, 20, 200, 110);

        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Box(box, "");
        GUI.color = new Color(1f, 1f, 1f, 0.7f);

        GUI.Label(new Rect(box.x + 8, box.y + 6, 190, 100),
            "F5  더미 소환\n" +
            "F6  더미 제거\n" +
            "F7  랜덤 능력 교체\n" +
            "F8  자해 사망\n" +
            "F9  체력 회복\n" +
            "Tab 순위표");

        GUI.color = Color.white;
    }



    void DrawStatus(PlayerHealth hp)
    {
        GUI.Label(new Rect(20, 20, 300, 30), $"HP: {hp.HP}   SOUL: {hp.Soul}");
        GUI.Label(new Rect(20, 40, 300, 30), $"K {hp.Kills}  A {hp.Assists}  D {hp.Deaths}");

        // 탄약
        if (hp.TryGetComponent<PlayerEquipment>(out var eq))
        {
            var w = eq.GetWeapon();
            if (w != null && w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun)
            {
                GUI.color = eq.Ammo > 0 ? Color.white : new Color(1f, 0.3f, 0.3f);
                GUI.Label(new Rect(Screen.width - 140, Screen.height - 60, 120, 30),
                    $"{eq.Ammo} / {w.MaxAmmo}");
                GUI.color = Color.white;
            }
        }

        if (hp.IsDead)
            GUI.Label(new Rect(Screen.width / 2 - 40, Screen.height / 2, 200, 30), "사망");
    }

    void DrawCrosshair()
    {
        GUI.Label(new Rect(Screen.width / 2 - 4, Screen.height / 2 - 10, 20, 20), "+");
    }

    void DrawCharge(AbilityRuntime ar)
    {
        var esa = ar.FindAbility<EmpoweredStrikeAbility>();
        if (esa == null) return;

        float r = esa.ChargeRatio;
        var bar = new Rect(Screen.width / 2f - 100f, Screen.height - 130f, 200f, 14f);

        GUI.color = new Color(0, 0, 0, 0.6f);
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

        GUI.Label(new Rect(20, 70, 400, 30), $"이번 생: {rand.DisplayName}");
    }


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
        float x = Screen.width / 2f - total / 2f;
        float y = Screen.height - 90f;

        Vector2 mouse = new Vector2(Input.mousePosition.x,
                                    Screen.height - Input.mousePosition.y);

        AbilityData.SkillDef hovered = null;
        Rect hoveredRect = default;

        for (int i = 0; i < count; i++)
        {
            var skill = ar.GetSlot(i);
            var slot = (SkillSlot)i;
            var box = new Rect(x + i * (boxW + gap), y, boxW, boxW);

            GUI.Box(box, "");

            // 키 라벨은 항상 표시
            GUI.Label(new Rect(box.x + 4, box.y + 3, boxW - 8, 16), slot.Label());

            if (skill == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                GUI.Label(new Rect(box.x + 24, box.y + 22, 20, 20), "－");
                GUI.color = Color.white;
                continue;
            }

            GUI.Label(new Rect(box.x + 4, box.y + 20, boxW - 8, 34), skill.Name);

            // 쿨타임 오버레이
            float ratio = ar.GetCooldownRatio(i);
            if (ratio > 0f)
            {
                float remain = ar.GetCooldownRemain(i);

                var cdRect = new Rect(box.x, box.y + box.height * (1f - ratio),
                                      box.width, box.height * ratio);
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.Box(cdRect, "");
                GUI.color = Color.white;

                GUI.Label(new Rect(box.x + 18, box.y + 38, 40, 18), $"{remain:F1}");
            }

            // 소울 부족 표시
            if (skill.SoulCost > 0)
            {
                GUI.color = new Color(0.6f, 0.8f, 1f);
                GUI.Label(new Rect(box.x + 4, box.y + boxW - 18, boxW - 8, 16),
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

        float x = Mathf.Clamp(anchor.x + anchor.width / 2f - w / 2f, 10f, Screen.width - w - 10f);
        float y = anchor.y - h - 8f;

        GUI.Box(new Rect(x, y, w, h), text, _tooltipStyle);
    }

    void DrawDanger(AbilityRuntime ar)
    {
        var bomb = ar.FindAbility<AnthropophobiaAbility>();
        if (bomb == null || !bomb.IsCounting) return;

        float r = bomb.DangerRatio;

        // 화면 테두리 붉게
        GUI.color = new Color(1f, 0f, 0f, r * 0.4f);
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
        GUI.color = Color.white;

        var box = new Rect(Screen.width / 2f - 100f, 140f, 200f, 30f);
        GUI.color = new Color(1f, 0.2f, 0.1f);
        GUI.Label(box, $"폭발까지 {(1f - r) * bomb.TriggerTime:F1}초!");
        GUI.color = Color.white;
    }

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

        // K 내림차순, 동점이면 소울 내림차순
        list.Sort((a, b) =>
        {
            int c = b.k.CompareTo(a.k);
            return c != 0 ? c : b.soul.CompareTo(a.soul);
        });

        const float w = 460f;
        const float rowH = 26f;
        const float headH = 30f;

        float x = Screen.width / 2f - w / 2f;
        float y = 90f;
        float h2 = headH + rowH * list.Count + 12f;

        // 배경
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Box(new Rect(x, y, w, h2), "");
        GUI.color = Color.white;

        // 헤더
        var head = new Rect(x + 12, y + 6, w - 24, headH);
        GUI.Label(head, "#    이름              K     A     D    SOUL");

        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        GUI.Box(new Rect(x + 12, y + headH, w - 24, 1), "");
        GUI.color = Color.white;

        // 행
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            var row = new Rect(x + 12, y + headH + 4 + rowH * i, w - 24, rowH);

            if (e.isLocal)
            {
                GUI.color = new Color(0.3f, 0.6f, 1f, 0.25f);
                GUI.Box(new Rect(x + 8, row.y - 2, w - 16, rowH), "");
                GUI.color = Color.white;
            }

            GUI.Label(row,
                $"{i + 1,-4} {e.name,-16} {e.k,3}   {e.a,3}   {e.d,3}   {e.soul,4}");
        }
    }


}