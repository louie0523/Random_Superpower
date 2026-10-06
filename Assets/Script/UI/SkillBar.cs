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
    private float _nameTimer;

    public void Bind(AbilityRuntime ar, PlayerHealth hp)
    {
        _ar = ar;
        _hp = hp;
        _lastRandomId = int.MinValue;
    }

    void Update()
    {
        if (_ar == null) return;

        foreach (var s in slots)
            if (s != null) s.Refresh(_ar, _hp);

        CheckRandomChanged();
    }

    /// <summary>랜덤 능력이 바뀌면 (리스폰 등) 이름을 잠깐 띄우고 칸을 튀게</summary>
    void CheckRandomChanged()
    {
        var rand = _ar.GetRandomAbility();
        int id = rand != null ? rand.Id : 0;

        if (id != _lastRandomId)
        {
            bool first = _lastRandomId == int.MinValue;
            _lastRandomId = id;

            if (randomNameText != null)
                randomNameText.text = rand != null
                    ? LocUtil.GetOr($"ability.{rand.Id}.name", LocUtil.Resolve(rand.DisplayName))
                    : "";

            _nameTimer = rand != null ? nameShowTime : 0f;

            if (!first)
                foreach (var s in slots)
                    if (s != null && s.Slot == SkillSlot.Random) s.Punch();
        }

        if (randomNameText != null)
        {
            _nameTimer -= Time.deltaTime;
            var c = randomNameText.color;
            c.a = Mathf.Clamp01(_nameTimer);
            randomNameText.color = c;
        }
    }
}