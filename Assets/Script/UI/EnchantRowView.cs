using UnityEngine;
using TMPro;

/// <summary>인챈트 페이지의 한 줄 — 일반은 개수·합산 스탯, 특수는 "둔화 II"와 효과 설명</summary>
public class EnchantRowView : MonoBehaviour
{
    [Tooltip("일반: ×N / 특수: ★")]
    [SerializeField] private TMP_Text indexText;
    [Tooltip("이름 — 인챈트 색으로 칠해짐")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private GameObject newMark;
    [Tooltip("특수 인챈트일 때만 켜짐 (테두리·배경 강조 등). 없어도 됨")]
    [SerializeField] private GameObject specialMark;

    public void Bind(EnchantSummary.Group g)
    {
        if (g == null || (g.Data == null && g.Special == null)) return;

        if (indexText != null) indexText.text = g.IsSpecial ? "★" : $"×{g.Count}";

        if (nameText != null)
        {
            nameText.text = EnchantSummary.Name(g);
            nameText.color = EnchantSummary.Color(g);
        }

        if (statsText != null) statsText.text = EnchantSummary.Detail(g);
        if (newMark != null) newMark.SetActive(g.IsNewest);
        if (specialMark != null) specialMark.SetActive(g.IsSpecial);
    }
}