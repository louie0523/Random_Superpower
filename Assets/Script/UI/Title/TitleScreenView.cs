using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>타이틀 화면 연결 (TitleUiBuilder가 만듦 · 프리팹에서 마음대로 옮겨도 됨)</summary>
public class TitleScreenView : MonoBehaviour
{
    [Header("로고")]
    public TMP_Text Logo;
    public TMP_Text Version;

    [Header("위 오른쪽")]
    public TMP_Text SC;
    public TMP_Text Tickets;
    public TMP_InputField Nick;

    [Header("메뉴 (창이 열리면 숨김)")]
    public GameObject Menu;
    public Button Play;
    public Button Weapons;
    public Button Skins;
    public Button Codex;
    public Button Profile;
    public Button Customize;
    public Button Settings;
    public Button Quit;

    [Header("지난 판 보상 알림")]
    public GameObject Toast;
    public TMP_Text ToastText;

    [Header("창")]
    public WeaponGalleryView WeaponPanel;
    public SkinShopView SkinPanel;
    public CodexView CodexPanel;
    public ProfileView ProfilePanel;
    public CustomizeView CustomizePanel;
}
