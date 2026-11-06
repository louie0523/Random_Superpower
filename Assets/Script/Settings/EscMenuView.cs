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
    [Tooltip("타이틀 화면으로 (방 · 연결을 끊고) — 비워 두면 '게임 종료' 버튼을 복사해 저절로 만듦")]
    public Button TitleButton;
    public Button QuitButton;

    void Awake()
    {
        // 예전에 만든 프리팹에도 번역 · 글자 맞춤이 되게
        SetLocText.Ensure(Title, "set.esc.title");
        SetLocText.Ensure(ContinueButton, "set.esc.continue");
        SetLocText.Ensure(SettingsButton, "set.esc.settings");
        SetLocText.Ensure(LeaveButton, "set.esc.leave");
        SetLocText.Ensure(QuitButton, "set.esc.quit");
        if (TitleButton == null && QuitButton != null) TitleButton = MakeTitleButton();
        else SetLocText.Ensure(TitleButton, "set.esc.to_title");
        UiTextFit.Apply(transform);

        if (ContinueButton != null) ContinueButton.onClick.AddListener(EscMenu.Close);
        if (SettingsButton != null) SettingsButton.onClick.AddListener(() => SettingsWindow.Open());
        if (LeaveButton != null) LeaveButton.onClick.AddListener(EscMenu.LeaveRoom);
        if (QuitButton != null) QuitButton.onClick.AddListener(EscMenu.QuitGame);
        if (TitleButton != null) TitleButton.onClick.AddListener(EscMenu.ToTitle);
    }

    public void Show(bool inRoom)
    {
        GameUiKit.EnsureEventSystem();
        GameUiKit.Active(Root, true);
        GameUiKit.Active(LeaveButton, inRoom);
        GameUiKit.Active(TitleButton, EscMenu.TitleAvailable);
    }

    /// <summary>예전 프리팹 — '게임 종료' 버튼을 복사해서 바로 위에 '타이틀로'</summary>
    Button MakeTitleButton()
    {
        var b = Instantiate(QuitButton, QuitButton.transform.parent);
        b.name = "ToTitle";
        b.onClick.RemoveAllListeners();
        b.transform.SetSiblingIndex(QuitButton.transform.GetSiblingIndex());
        var t = b.GetComponentInChildren<TMP_Text>(true);
        if (t != null)
        {
            var old = t.GetComponent<SetLocText>();
            if (old != null) DestroyImmediate(old);
            t.text = "타이틀로";
            SetLocText.Add(t, "set.esc.to_title");
        }
        return b;
    }

    public void Hide() => GameUiKit.Active(Root, false);

    void Update()
    {
        if (Root != null && Root.activeSelf) GameUiKit.DropButtonFocus();
    }
}
