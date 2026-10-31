using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ESC 메뉴 (화면) — 계속하기 · 설정 · 방 나가기 · 게임 종료. 열고 닫기는 EscMenu가 함</summary>
public class EscMenuView : MonoBehaviour
{
    public GameObject Root;
    public TMP_Text Title;
    public Button ContinueButton;
    public Button SettingsButton;
    [Tooltip("방에 있을 때만 보임")]
    public Button LeaveButton;
    public Button QuitButton;

    void Awake()
    {
        if (ContinueButton != null) ContinueButton.onClick.AddListener(EscMenu.Close);
        if (SettingsButton != null) SettingsButton.onClick.AddListener(() => SettingsWindow.Open());
        if (LeaveButton != null) LeaveButton.onClick.AddListener(EscMenu.LeaveRoom);
        if (QuitButton != null) QuitButton.onClick.AddListener(EscMenu.QuitGame);
    }

    public void Show(bool inRoom)
    {
        GameUiKit.EnsureEventSystem();
        GameUiKit.Active(Root, true);
        GameUiKit.Active(LeaveButton, inRoom);
    }

    public void Hide() => GameUiKit.Active(Root, false);

    void Update()
    {
        if (Root != null && Root.activeSelf) GameUiKit.DropButtonFocus();
    }
}
