using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>대기실 명단 한 줄 — 표시(방장 · 준비 · 대기) · 이름 · 강퇴</summary>
public class RoomPlayerRowView : MonoBehaviour
{
    public Image Face;
    [Tooltip("방장 · 준비 · 대기 표시 바탕 (색이 바뀜)")]
    public Image Badge;
    public TMP_Text BadgeText;
    public TMP_Text Name;
    public Button Kick;

    /// <summary>이 줄의 사람 (코드가 넣음)</summary>
    [System.NonSerialized] public PlayerRef Ref;
}
