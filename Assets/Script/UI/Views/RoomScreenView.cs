using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방 목록 · 대기실 프리팹 — 화면만 (연결 · 값 넣기는 RoomUI가 함).
/// 모양은 자유롭게 바꿔도 되고, 쓰지 않는 칸은 비워 둬도 됨 (그 기능만 안 보임).
/// </summary>
public class RoomScreenView : MonoBehaviour
{
    [Header("공통")]
    public GameObject Root;
    [Tooltip("알림 (가득 참 · 강퇴 등)")]
    public GameObject ToastBox;
    public TMP_Text ToastText;
    [Tooltip("'방 정보 받는 중' 같은 가운데 글")]
    public TMP_Text CenterText;

    [Header("방 목록")]
    public GameObject LobbyPanel;
    public TMP_Text LobbyStatus;
    public Button ReconnectButton;
    public TMP_InputField NickInput;
    public RectTransform RoomListContent;
    public RoomRowView RoomRowPrefab;
    public TMP_Text EmptyText;
    public TMP_InputField RoomNameInput;
    public UiOption HiddenOption;
    public Button CreateButton;
    public TMP_InputField CodeInput;
    public Button JoinCodeButton;

    [Header("대기실 — 명단")]
    public GameObject WaitingPanel;
    public TMP_Text RoomTitle;
    public TMP_Text CodeText;
    public Button CopyCodeButton;
    public TMP_Text VisibilityText;
    public TMP_Text CountText;
    public RectTransform PlayerListContent;
    public RoomPlayerRowView PlayerRowPrefab;
    [Tooltip("방장 = 게임 시작 / 나머지 = 준비")]
    public Button MainButton;
    public TMP_Text MainButtonLabel;
    public Image MainButtonFace;
    public TMP_Text ReasonText;
    public Button LeaveButton;

    [Header("대기실 — 설정")]
    public TMP_Text SettingsTitle;
    public StepperRowView MapRow;
    public StepperRowView MaxPlayersRow;
    public StepperRowView RoundTimeRow;
    public StepperRowView StartSoulRow;
    public StepperRowView ChestRow;
    public UiOption FallDeath;
    public UiOption FallReturn;
    public UiOption TeamOption;
    public UiOption HiddenSetting;

    [Header("대기실 — 제단")]
    public StepperRowView SubCostRow;
    public StepperRowView CentralCostRow;
    public UiOption NeedAllOption;
    public StepperRowView NeededRow;
    public StepperRowView ChannelRow;

    [Header("대기실 — 밴")]
    public Button ClassBanHeader;
    public TMP_Text ClassBanLabel;
    public Button ClassBanClear;
    public RectTransform ClassBanContent;
    public Button RandBanHeader;
    public TMP_Text RandBanLabel;
    public Button RandBanClear;
    public RectTransform RandBanContent;
    [Tooltip("밴 목록 한 칸 (체크)")]
    public UiOption BanTogglePrefab;

    [Header("대기실 — 프리셋")]
    public Button CopySettingsButton;
    public Button PasteButton;
    public TMP_InputField PresetNameInput;
    public Button SaveButton;
    public RectTransform PresetListContent;
    [Tooltip("프리셋 한 줄 — Label = 이름, Action = 불러오기")]
    public UiListRow PresetRowPrefab;
    public Button OpenFolderButton;

    [Header("색")]
    public Color HostBadge = new(1f, 0.831f, 0.278f, 1f);       // 노랑 (GameUiKit.Gold)
    public Color ReadyBadge = new(0.373f, 0.827f, 0.541f, 1f);  // 초록 (Ok)
    public Color WaitBadge = new(0.227f, 0.255f, 0.294f, 1f);   // 회색 (LineColor)
    public Color StartColor = new(0.306f, 0.839f, 0.745f, 1f);  // 민트
    public Color ReadyColor = new(0.373f, 0.827f, 0.541f, 1f);
    public Color UnreadyColor = new(0.169f, 0.196f, 0.235f, 1f);

    void Awake()
    {
        // 프리팹 미리보기 줄 지우기 (게임에선 코드가 다시 만듦)
        Clear<RoomRowView>(RoomListContent);
        Clear<RoomPlayerRowView>(PlayerListContent);
        Clear<UiOption>(ClassBanContent);
        Clear<UiOption>(RandBanContent);
        Clear<UiListRow>(PresetListContent);
    }

    static void Clear<T>(RectTransform parent) where T : Component
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).GetComponent<T>() != null) Destroy(parent.GetChild(i).gameObject);
    }
}
