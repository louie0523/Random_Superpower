using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>목록 한 줄 — 이름 · 보조 글 · (줄 전체 버튼) · (오른쪽 버튼). 쓰지 않는 칸은 비워 둬도 됨</summary>
public class UiListRow : MonoBehaviour
{
    [Tooltip("줄 전체를 누르는 버튼 (없어도 됨)")]
    public Button Button;
    public Image Face;
    public TMP_Text Label;
    public TMP_Text Sub;
    [Tooltip("오른쪽 버튼 (없어도 됨)")]
    public Button Action;
    public TMP_Text ActionLabel;
}
