using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>특수 단계 선택지 하나 — 클릭하면 그 특수 인챈트로 확정</summary>
public class SpecialChoiceView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [Tooltip("\"둔화 II\" — 인챈트 색")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descText;
    [Tooltip("이미 가진 종류의 등급 올리기일 때만 켜짐")]
    [SerializeField] private GameObject upgradeMark;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
    }

    public void Bind(SpecialEnchantData data, int tier, bool isUpgrade, System.Action onClick)
    {
        if (data == null) return;

        if (icon != null)
        {
            icon.enabled = data.Icon != null;
            icon.sprite = data.Icon;
        }

        if (nameText != null)
        {
            nameText.text = data.Name(tier);
            nameText.color = data.Color;
        }

        if (descText != null) descText.text = data.Describe(tier);
        if (upgradeMark != null) upgradeMark.SetActive(isUpgrade);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
