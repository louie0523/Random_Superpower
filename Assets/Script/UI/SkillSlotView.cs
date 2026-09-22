using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillSlotView : MonoBehaviour
{
    [Header("담당 슬롯")]
    [SerializeField] private SkillSlot slot = SkillSlot.Skill1;

    [Header("표시")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text keyText;
    [SerializeField] private CanvasGroup group;

    [Header("쿨타임")]
    [SerializeField] private Image cooldownFill;       // Filled / Radial 360, 검정 반투명
    [SerializeField] private TMP_Text cooldownText;

    [Header("상태")]
    [SerializeField] private GameObject readyGlow;     // 사용 가능 강조 테두리
    [SerializeField] private GameObject lockedMark;    // 무기 조건 불충족
    [SerializeField] private TMP_Text soulCostText;
    [SerializeField] private GameObject emptyRoot;     // 스킬 없음

    [Header("연출")]
    [SerializeField] private RectTransform punchTarget;
    [SerializeField] private float punchScale = 0.18f;
    [SerializeField] private float punchDecay = 8f;
    [SerializeField, Range(0f, 1f)] private float unusableAlpha = 0.45f;

    public SkillSlot Slot => slot;

    private bool _wasReady;
    private float _punch;

    void Start()
    {
        if (keyText != null) keyText.text = slot.Label();
    }

    void Update()
    {
        if (punchTarget == null) return;
        _punch = Mathf.MoveTowards(_punch, 0f, Time.deltaTime * punchDecay * Mathf.Max(0.01f, _punch));
        punchTarget.localScale = Vector3.one * (1f + _punch);
    }

    /// <summary>튀는 연출 (쿨 끝날 때, 랜덤 능력 바뀔 때)</summary>
    public void Punch() => _punch = punchScale;

    public void Refresh(AbilityRuntime ar, PlayerHealth hp)
    {
        int i = (int)slot;
        var skill = ar.GetSlot(i);

        // ── 스킬 없음 ──
        if (skill == null)
        {
            // 랜덤 칸은 스킬이 없어도 능력 자체를 패시브로 표시
            var rand = slot == SkillSlot.Random ? ar.GetRandomAbility() : null;
            if (rand != null) { ShowPassive(rand.Icon); return; }

            ShowEmpty();
            return;
        }

        if (emptyRoot != null) emptyRoot.SetActive(false);

        if (icon != null)
        {
            icon.enabled = skill.Icon != null;
            icon.sprite = skill.Icon;
        }

        if (keyText != null) keyText.gameObject.SetActive(true);

        // ── 쿨타임 ──
        float ratio = ar.GetCooldownRatio(i);
        float remain = ar.GetCooldownRemain(i);
        bool cooling = ratio > 0.001f;

        if (cooldownFill != null) cooldownFill.fillAmount = ratio;

        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(cooling);
            if (cooling)
                cooldownText.text = remain >= 1f ? Mathf.CeilToInt(remain).ToString()
                                                 : remain.ToString("0.0");
        }

        // ── 조건 ──
        bool weaponOk = ar.CanUseWeapon(i);
        bool soulOk = skill.SoulCost <= 0 || (hp != null && hp.Soul >= skill.SoulCost);
        bool usable = !cooling && weaponOk && soulOk;

        if (lockedMark != null) lockedMark.SetActive(!weaponOk);

        if (soulCostText != null)
        {
            soulCostText.gameObject.SetActive(skill.SoulCost > 0);
            if (skill.SoulCost > 0)
            {
                soulCostText.text = skill.SoulCost.ToString();
                soulCostText.color = soulOk ? Color.white : new Color(1f, 0.4f, 0.35f);
            }
        }

        if (readyGlow != null) readyGlow.SetActive(usable);
        if (group != null) group.alpha = (usable || cooling) ? 1f : unusableAlpha;

        // 쿨이 막 끝났을 때만 튐
        if (usable && !_wasReady) Punch();
        _wasReady = usable;
    }

    void ShowPassive(Sprite passiveIcon)
    {
        if (emptyRoot != null) emptyRoot.SetActive(false);

        if (icon != null)
        {
            icon.enabled = passiveIcon != null;
            icon.sprite = passiveIcon;
        }

        if (keyText != null) keyText.gameObject.SetActive(false);   // 누를 키 없음
        if (cooldownFill != null) cooldownFill.fillAmount = 0f;
        if (cooldownText != null) cooldownText.gameObject.SetActive(false);
        if (lockedMark != null) lockedMark.SetActive(false);
        if (soulCostText != null) soulCostText.gameObject.SetActive(false);
        if (readyGlow != null) readyGlow.SetActive(false);
        if (group != null) group.alpha = 1f;

        _wasReady = false;
    }

    void ShowEmpty()
    {
        if (emptyRoot != null) emptyRoot.SetActive(true);
        if (icon != null) icon.enabled = false;
        if (cooldownFill != null) cooldownFill.fillAmount = 0f;
        if (cooldownText != null) cooldownText.gameObject.SetActive(false);
        if (lockedMark != null) lockedMark.SetActive(false);
        if (soulCostText != null) soulCostText.gameObject.SetActive(false);
        if (readyGlow != null) readyGlow.SetActive(false);
        if (group != null) group.alpha = unusableAlpha;

        _wasReady = false;
    }
}