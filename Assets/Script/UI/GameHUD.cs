using Fusion;
using UnityEngine;

public class GameHUD : MonoBehaviour
{
    private NetworkRunner _runner;
    private GUIStyle _tooltipStyle;

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



        GUI.Label(new Rect(20, 20, 300, 30),
            $"HP: {hp.HP}   SOUL: {hp.Soul}");

        if (hp.IsDead)
            GUI.Label(new Rect(Screen.width / 2 - 40, Screen.height / 2, 200, 30), "사망");

        // 조준점
        GUI.Label(new Rect(Screen.width / 2 - 4, Screen.height / 2 - 10, 20, 20), "+");

        var ar = local.GetComponent<AbilityRuntime>();
        if (ar != null)
        {
            var esa = ar.FindAbility<EmpoweredStrikeAbility>();
            if (esa != null)
            {
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
        }

        float x = Screen.width / 2f - 130f;
        float y = Screen.height - 90f;

        var rand = ar?.GetRandomAbility();
        if (rand != null)
            GUI.Label(new Rect(20, 50, 400, 30), $"이번 생: {rand.DisplayName}");


        for (int i = 0; i < SkillSlotExt.Count; i++)
        {
            var skill = ar.GetSlot(i);
            var slot = (SkillSlot)i;
            var box = new Rect(x + i * 68f, y, 60f, 60f);

            GUI.Box(box, "");

            if (skill == null)
            {
                GUI.Label(new Rect(box.x + 4, box.y + 20, 52, 20), "－");
            }
            else
            {
                float ratio = ar.GetCooldownRatio(i);
                float remain = ar.GetCooldownRemain(i);

                GUI.Label(new Rect(box.x + 4, box.y + 4, 52, 18), slot.Label());
                GUI.Label(new Rect(box.x + 4, box.y + 22, 52, 18), skill.Name);

                if (ratio > 0f)
                {
                    var cdRect = new Rect(box.x, box.y + box.height * (1f - ratio),
                                          box.width, box.height * ratio);
                    GUI.color = new Color(0f, 0f, 0f, 0.6f);
                    GUI.Box(cdRect, "");
                    GUI.color = Color.white;
                    GUI.Label(new Rect(box.x + 18, box.y + 40, 40, 18), $"{remain:F1}");
                }
            }
        }





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

        float x = Screen.width / 2f - 130f;
        float y = Screen.height - 90f;

        AbilityData.SkillDef hovered = null;
        Rect hoveredRect = default;

        for (int i = 0; i < SkillSlotExt.Count; i++)
        {
            var skill = ar.GetSlot(i);
            var slot = (SkillSlot)i;
            var box = new Rect(x + i * 68f, y, 60f, 60f);

            GUI.Box(box, "");

            if (skill == null)
            {
                GUI.Label(new Rect(box.x + 22, box.y + 20, 52, 20), "－");
                continue;
            }

            float ratio = ar.GetCooldownRatio(i);
            float remain = ar.GetCooldownRemain(i);

            GUI.Label(new Rect(box.x + 4, box.y + 3, 52, 16), slot.Label());
            GUI.Label(new Rect(box.x + 4, box.y + 20, 52, 34), skill.Name);

            if (ratio > 0f)
            {
                var cdRect = new Rect(box.x, box.y + box.height * (1f - ratio),
                                      box.width, box.height * ratio);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.Box(cdRect, "");
                GUI.color = Color.white;
                GUI.Label(new Rect(box.x + 18, box.y + 38, 40, 18), $"{remain:F1}");
            }

            // 마우스 오버 감지
            Vector2 mouse = new Vector2(Input.mousePosition.x,
                                        Screen.height - Input.mousePosition.y);
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
        text += $"\n\n<color=#888>{AbilityData.DescribeRange(skill)}</color>";

        var content = new GUIContent(text);
        float width = 300f;
        float height = _tooltipStyle.CalcHeight(content, width);

        float tx = Mathf.Clamp(anchor.x + anchor.width * 0.5f - width * 0.5f,
                               10f, Screen.width - width - 10f);
        float ty = anchor.y - height - 8f;

        GUI.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        GUI.Box(new Rect(tx, ty, width, height), "");
        GUI.color = Color.white;

        GUI.Label(new Rect(tx, ty, width, height), content, _tooltipStyle);
    }


}