using UnityEngine;
using TMPro;

/// <summary>순위표 한 줄</summary>
public class ScoreboardRow : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text killsText;
    [SerializeField] private TMP_Text assistsText;
    [SerializeField] private TMP_Text deathsText;
    [SerializeField] private TMP_Text soulText;
    [Tooltip("내 줄일 때만 켜지는 강조 배경")]
    [SerializeField] private GameObject localHighlight;

    [Tooltip("제단 칸 (비우면 안 보임)")]
    [SerializeField] private TMPro.TMP_Text altarText;

    public void SetAltars(int have, int need)
    {
        if (altarText == null) return;
        altarText.text = need > 0 ? $"{have}/{need}" : have.ToString();
        altarText.color = need > 0 && have >= need ? new Color(1f, 0.84f, 0.22f) : Color.white;   // 권한 얻으면 노랑
    }

    public void Bind(int rank, string playerName, int kills, int assists, int deaths, int soul, bool isLocal)
    {
        if (rankText != null) rankText.text = rank.ToString();
        if (nameText != null) nameText.text = playerName;
        if (killsText != null) killsText.text = kills.ToString();
        if (assistsText != null) assistsText.text = assists.ToString();
        if (deathsText != null) deathsText.text = deaths.ToString();
        if (soulText != null) soulText.text = soul.ToString();
        if (localHighlight != null) localHighlight.SetActive(isLocal);
    }
}
