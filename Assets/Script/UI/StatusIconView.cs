using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>상태이상 아이콘 하나 — 아이콘, 남은 시간 원형 게이지, 남은 초, 중첩 수</summary>
public class StatusIconView : MonoBehaviour
{
    [SerializeField] private Image icon;
    [Tooltip("Image Type = Filled / Radial 360. 남은 시간 비율")]
    [SerializeField] private Image timerFill;
    [SerializeField] private TMP_Text timeText;
    [Tooltip("중독 중첩 수 (2 이상일 때만)")]
    [SerializeField] private TMP_Text stackText;

    private int _shownSec = -1;
    private int _shownStacks = -1;

    public void Setup(Sprite sprite, Color color)
    {
        if (icon == null) return;
        icon.sprite = sprite;
        icon.color = color;
        icon.enabled = sprite != null;
    }

    public void Set(float remain, float ratio, int stacks)
    {
        if (timerFill != null) timerFill.fillAmount = Mathf.Clamp01(ratio);

        if (timeText != null)
        {
            int sec = Mathf.CeilToInt(remain);
            if (sec != _shownSec)
            {
                _shownSec = sec;
                timeText.text = sec.ToString();
            }
        }

        if (stackText != null && stacks != _shownStacks)
        {
            _shownStacks = stacks;
            stackText.gameObject.SetActive(stacks > 1);
            if (stacks > 1) stackText.text = stacks.ToString();
        }
    }
}
