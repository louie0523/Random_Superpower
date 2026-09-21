using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InspectorBar : MonoBehaviour
{
    [Header("정보")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private Image portrait;

    [Header("체력")]
    [SerializeField] private Image hpFill;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private Gradient hpGradient;

    [Header("소울")]
    [SerializeField] private TMP_Text soulText;
    [SerializeField] private LocText soulLabel;

    [Header("상태")]
    [SerializeField] private GameObject combatIcon;
    [SerializeField] private LocText combatLabel;

    private PlayerHealth _hp;
    private PlayerStats _stats;

    void Start()
    {
        soulLabel?.SetKey("ui.hud.soul");
        combatLabel?.SetKey("ui.hud.in_combat");
    }

    public void Bind(PlayerHealth hp, PlayerStats stats, string nickname)
    {
        _hp = hp;
        _stats = stats;
        if (nicknameText != null) nicknameText.text = nickname;   // 닉네임은 번역 안 함
    }

    void Update()
    {
        if (_hp == null) return;

        float max = _stats != null ? Mathf.Max(1f, _stats.Get(StatType.MaxHP)) : 100f;
        float ratio = Mathf.Clamp01(_hp.HP / max);

        if (hpFill != null)
        {
            hpFill.fillAmount = ratio;
            if (hpGradient != null) hpFill.color = hpGradient.Evaluate(ratio);
        }

        if (hpText != null)
            hpText.text = $"{_hp.HP} / {Mathf.RoundToInt(max)}";

        if (soulText != null) soulText.text = _hp.Soul.ToString();

        if (combatIcon != null)
            combatIcon.SetActive(_hp.InCombat && !_hp.IsDead);
    }
}