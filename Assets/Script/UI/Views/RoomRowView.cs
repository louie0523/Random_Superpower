using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>방 목록 한 줄</summary>
public class RoomRowView : MonoBehaviour
{
    public Image Face;
    public TMP_Text Name;
    public TMP_Text Count;
    public TMP_Text Lock;
    public TMP_Text Map;
    public TMP_Text State;
    public Button Join;
    public TMP_Text JoinLabel;

    /// <summary>이 줄의 방 코드 (코드가 넣음)</summary>
    [System.NonSerialized] public string Code;
}
