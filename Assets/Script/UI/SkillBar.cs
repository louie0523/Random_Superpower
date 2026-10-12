using UnityEngine;
using TMPro;

public class SkillBar : MonoBehaviour
{
    [SerializeField] private SkillSlotView[] slots;

    [Header("랜덤 능력 이름")]
    [SerializeField] private TMP_Text randomNameText;
    [SerializeField] private float nameShowTime = 3f;

    private AbilityRuntime _ar;
    private PlayerHealth _hp;
    private int _lastRandomId = int.MinValue;
    private int _lastRandomId2 = int.MinValue;
    private float _nameTimer;

    public void Bind(AbilityRuntime ar, PlayerHealth hp)
    {
        _ar = ar;
        _hp = hp;
        _lastRandomId = int.MinValue;
        _lastRandomId2 = int.MinValue;
    }

    void Update()
    {
        if (_ar == null) return;

        foreach (var s in slots)
            if (s != null) s.Refresh(_ar, _hp);

        CheckRandomChanged();
    }

    /// <summary>랜덤 능력(F · V)이 바뀌면 (리스폰 등) 이름을 잠깐 띄우고 그 칸을 튀게</summary>
    void CheckRandomChanged()
    {
        bool c1 = Track(_ar.GetRandomAbility(), ref _lastRandomId, SkillSlot.Random, out var r1);
        bool c2 = Track(_ar.GetRandomAbility2(), ref _lastRandomId2, SkillSlot.Random2, out var r2);

        // 이름 — 바뀐 것만 (둘 다 바뀌면 "F 이름 · V 이름")
        if ((c1 || c2) && randomNameText != null)
        {
            string n1 = c1 ? Name(r1) : "";
            string n2 = c2 ? Name(r2) : "";
            randomNameText.text = n1.Length > 0 && n2.Length > 0 ? $"{n1} · {n2}" : n1 + n2;
            _nameTimer = randomNameText.text.Length > 0 ? nameShowTime : 0f;
        }

        if (randomNameText != null)
        {
            _nameTimer -= Time.deltaTime;
            var c = randomNameText.color;
            c.a = Mathf.Clamp01(_nameTimer);
            randomNameText.color = c;
        }
    }

    /// <summary>바뀌었으면 true (처음 한 번은 이름만 띄우고 튀지는 않음)</summary>
    bool Track(AbilityData rand, ref int last, SkillSlot slot, out AbilityData shown)
    {
        shown = rand;
        int id = rand != null ? rand.Id : 0;
        if (id == last) return false;

        bool first = last == int.MinValue;
        last = id;

        if (!first)
            foreach (var s in slots)
                if (s != null && s.Slot == slot) s.Punch();

        return rand != null;
    }

    static string Name(AbilityData a) =>
        a == null ? "" : LocUtil.GetOr($"ability.{a.Id}.name", a.DisplayName);
}