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

    [Header("v7 카드 (없어도 됨)")]
    [Tooltip("인형 셔츠 — 사람마다 색이 다름")]
    public Image Shirt;
    [Tooltip("나일 때 켜지는 민트 테두리")]
    public GameObject MeRing;
    [Tooltip("카드 바탕 색 (사람마다 돌아가며)")]
    public Color[] CardBacks;
    [Tooltip("셔츠 색 (사람마다 돌아가며)")]
    public Color[] CardShirts;

    /// <summary>v7 카드 모양 — colorKey(PlayerId)로 색 고르기 · 나 표시. 칸이 없으면 아무것도 안 함</summary>
    public void Look(int colorKey, bool me)
    {
        int k = colorKey < 0 ? -colorKey : colorKey;
        if (Face != null && CardBacks != null && CardBacks.Length > 0) Face.color = CardBacks[k % CardBacks.Length];
        if (Shirt != null && CardShirts != null && CardShirts.Length > 0) Shirt.color = CardShirts[k % CardShirts.Length];
        if (MeRing != null && MeRing.activeSelf != me) MeRing.SetActive(me);
    }

    /// <summary>이 줄의 사람 (코드가 넣음)</summary>
    [System.NonSerialized] public PlayerRef Ref;
}
