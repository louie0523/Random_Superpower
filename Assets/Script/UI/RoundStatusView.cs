using UnityEngine;
using TMPro;

/// <summary>
/// 라운드 상태 — 진행 중엔 상단 "목표 · 1위", 끝나면 결과 패널(승자 · 소울 · 재시작까지 초).
/// GameHUD의 OnGUI 라운드 표시 대체
/// </summary>
public class RoundStatusView : MonoBehaviour
{
    [Header("진행 중")]
    [SerializeField] private TMP_Text goalText;

    [Header("결과")]
    [SerializeField] private GameObject resultRoot;
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private TMP_Text winnerSoulText;
    [SerializeField] private TMP_Text restartText;

    [SerializeField] private float refreshInterval = 0.2f;

    private float _next;

    void Awake()
    {
        if (resultRoot != null) resultRoot.SetActive(false);
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + refreshInterval;

        var rm = RoundManager.Instance;
        bool valid = rm != null && rm.Object != null && rm.Object.IsValid;
        bool over = valid && rm.GameOver;

        if (goalText != null) goalText.gameObject.SetActive(valid && !over);
        if (resultRoot != null && resultRoot.activeSelf != over) resultRoot.SetActive(over);

        if (!valid) return;

        if (over)
        {
            if (winnerText != null) winnerText.text = LocUtil.Get("ui.round.winner", rm.Winner.PlayerId);
            if (winnerSoulText != null) winnerSoulText.text = LocUtil.Get("ui.round.winner_soul", rm.WinnerSoul);
            if (restartText != null)
                restartText.text = LocUtil.Get("ui.round.restart_in", Mathf.CeilToInt(rm.RestartRemaining));
        }
        else if (goalText != null)
        {
            goalText.text = LocUtil.Get("ui.round.goal", rm.GetTopAltars(), rm.GetTopSoul());
        }
    }
}
