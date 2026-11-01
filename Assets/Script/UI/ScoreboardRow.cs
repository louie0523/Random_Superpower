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

    [Header("클래스 (메인 능력)")]
    [Tooltip("클래스 이름 칸 — 비우면 이름 옆에 작게 붙여서 보임")]
    [SerializeField] private TMP_Text classText;
    [Tooltip("클래스 아이콘 (없어도 됨)")]
    [SerializeField] private UnityEngine.UI.Image classIcon;

    /// <summary>클래스 — 아직 고르는 중이면 a = null ('-')</summary>
    public void SetClass(AbilityData a)
    {
        if (classIcon != null)
        {
            classIcon.sprite = a != null ? a.Icon : null;
            classIcon.enabled = classIcon.sprite != null;
        }

        string label = a != null ? AbilityUi.Name(a) : "-";
        if (classText != null)
        {
            classText.text = label;
            classText.color = a != null ? Brighten(AbilityUi.Accent(a)) : new Color(0.6f, 0.6f, 0.65f);
        }
        else if (nameText != null && a != null)
        {
            // 칸이 없으면 이름 뒤에 작게 — "닉네임  죽음의 기사"
            string hex = ColorUtility.ToHtmlStringRGB(Brighten(AbilityUi.Accent(a)));
            nameText.text += $"  <size=75%><color=#{hex}>{label}</color></size>";
        }
    }

    /// <summary>어두운 강조색은 글자로 안 보여서 조금 밝게</summary>
    static Color Brighten(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Min(s, 0.75f), Mathf.Max(v, 0.85f));
    }

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
