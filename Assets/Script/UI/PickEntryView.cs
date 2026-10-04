using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>대장장이 '다음 인챈트 지정' 목록의 한 줄 — 이름 · 설명 · 소울 비용</summary>
public class PickEntryView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descText;
    [Tooltip("소울 비용 숫자")]
    [SerializeField] private TMP_Text costText;
    [Tooltip("이미 가진 특수 인챈트의 등급 올리기일 때만 켜짐")]
    [SerializeField] private GameObject upgradeMark;

    [SerializeField] private Color costOk = Color.white;
    [SerializeField] private Color costLack = new(1f, 0.4f, 0.35f);

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
    }

    public void Bind(Sprite iconSprite, string title, Color titleColor, string desc,
                     int soulCost, bool affordable, bool isUpgrade, System.Action onClick)
    {
        if (icon != null)
        {
            icon.enabled = iconSprite != null;
            icon.sprite = iconSprite;
        }

        if (nameText != null)
        {
            nameText.text = title;
            nameText.color = titleColor;
        }

        if (descText != null)
        {
            descText.text = desc;
            descText.gameObject.SetActive(!string.IsNullOrWhiteSpace(desc));
        }

        if (costText != null)
        {
            costText.text = soulCost.ToString();
            costText.color = affordable ? costOk : costLack;
        }

        if (upgradeMark != null) upgradeMark.SetActive(isUpgrade);

        if (button != null)
        {
            button.interactable = affordable;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
