using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 격자 카드 하나 (무기 · 스킨 · 부위 · 도감) — 그림 · 이름 · 아래 글 · 등급 띠 · 잠김 · 표시(장착 등).
/// 쓰지 않는 칸은 비워 둬도 됨
/// </summary>
public class UiItemCard : MonoBehaviour
{
    public Button Button;
    public Image Face;
    public Image Icon;
    public RawImage Raw;
    public TMP_Text Title;
    public TMP_Text Sub;
    [Tooltip("등급 색 띠")]
    public Image Grade;
    [Tooltip("잠김 표시 (자물쇠 등)")]
    public GameObject Lock;
    [Tooltip("오른쪽 위 작은 표시 (장착 · 보유 등)")]
    public GameObject Badge;
    public TMP_Text BadgeText;
    [Tooltip("고른 카드 테두리")]
    public GameObject Selected;

    [System.NonSerialized] public int Id;
    [System.NonSerialized] public object Data;

    public void Set(string title, string sub, Sprite icon, Texture raw, Color? grade, bool locked, string badge)
    {
        if (Title != null) Title.text = title ?? "";
        if (Sub != null) { Sub.text = sub ?? ""; Sub.gameObject.SetActive(!string.IsNullOrEmpty(sub)); }
        if (Icon != null)
        {
            Icon.sprite = icon;
            Icon.enabled = icon != null && raw == null;
            Icon.color = locked ? new Color(0f, 0f, 0f, 0.85f) : Color.white;     // 잠김 = 검은 실루엣
        }
        if (Raw != null)
        {
            Raw.texture = raw;
            Raw.enabled = raw != null;
            Raw.color = locked ? new Color(0f, 0f, 0f, 0.85f) : Color.white;
        }
        if (Grade != null)
        {
            Grade.enabled = grade.HasValue;
            if (grade.HasValue) Grade.color = grade.Value;
        }
        if (Lock != null) Lock.SetActive(locked);
        bool hasBadge = !string.IsNullOrEmpty(badge);
        if (Badge != null) Badge.SetActive(hasBadge);
        if (BadgeText != null && hasBadge) BadgeText.text = badge;
    }

    public void SetSelected(bool on)
    {
        if (Selected != null && Selected.activeSelf != on) Selected.SetActive(on);
    }
}
