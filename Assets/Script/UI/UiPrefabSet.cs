using UnityEngine;

/// <summary>
/// 화면마다 '코드 기본 모양' / '내 프리팹' 고르기. Resources 폴더 안에 이름 그대로 'UiPrefabSet'으로 둘 것.
/// Tools → UI → 방 · 대상 선택 · 보이스 설정 프리팹 만들기를 누르면 저절로 만들어지고 예시 프리팹이 들어감.
/// 켜기(Use) 칸을 켜야 프리팹을 씀 — 끄면 예전 코드 모양.
/// </summary>
[CreateAssetMenu(menuName = "Game/UI/UI Prefab Set", fileName = "UiPrefabSet")]
public class UiPrefabSet : ScriptableObject
{
    [Header("대상 선택 창 (저주 인형 · 데스 노트)")]
    [Tooltip("켜면 아래 프리팹, 끄면 코드 기본 모양")]
    public bool UsePlayerPick;
    public PlayerPickView PlayerPick;

    [Header("보이스 설정 창 (O)")]
    [Tooltip("켜면 아래 프리팹, 끄면 코드 기본 모양 (왼쪽 아래 마이크 상태는 계속 코드)")]
    public bool UseVoiceSettings;
    public VoiceSettingsView VoiceSettings;

    [Header("방 목록 · 대기실")]
    [Tooltip("켜면 아래 프리팹, 끄면 코드 기본 모양")]
    public bool UseRoom;
    public RoomScreenView Room;

    [Header("설정 창 · ESC 메뉴 (끄면 같은 카툰 모양을 코드로 만들어 씀)")]
    public bool UseSettings;
    public SettingsView Settings;
    public bool UseEscMenu;
    public EscMenuView EscMenu;

    [Header("게임 HUD v2 (소울 · 체력 · 스킬 칸 · 탄약 · 목표 · 랜덤 능력 알림)")]
    [Tooltip("켜면 이 HUD를 띄우고 씬의 예전 HUD(같은 역할)는 숨김. 조준점 · 상호작용 안내 · 충전 게이지 · 상태이상 아이콘은 씬 것 그대로")]
    public bool UseHud;
    public GameObject Hud;
    [Tooltip("HUD를 항상 Overlay로 (끄면 UIManager의 UI 카메라로 Screen Space - Camera)")]
    public bool ForceOverlay;

    [Header("게임 창 v2 (능력 창 F1 · 스킬 상세 · 상점 · 인벤토리 · 순위표 Tab · 툴팁)")]
    [Tooltip("켜면 이 창들을 띄우고 씬의 예전 능력 창 · 상점 · 인벤토리 · 상세 창 · 툴팁 · 순위표는 숨김. 프리팹을 씬에 직접 놓아도 됨 (그걸 씀)")]
    public bool UsePanels;
    public GameObject Panels;

    [Header("글꼴")]
    [Tooltip("코드로 만드는 화면(설정 창 · ESC 메뉴)의 글꼴 — 한글 TMP 글꼴")]
    public TMPro.TMP_FontAsset Font;
    [Tooltip("제목 글꼴 (예: Black Han Sans 한글 TMP) — 비우면 위 글꼴. 창 제목 · 작은 제목에 씀")]
    public TMPro.TMP_FontAsset DisplayFont;

    public static UiPrefabSet Get() => Resources.Load<UiPrefabSet>("UiPrefabSet");

    public static SettingsView SettingsPrefab
    {
        get { var s = Get(); return s != null && s.UseSettings ? s.Settings : null; }
    }

    public static EscMenuView EscMenuPrefab
    {
        get { var s = Get(); return s != null && s.UseEscMenu ? s.EscMenu : null; }
    }

    public static PlayerPickView PickPrefab
    {
        get { var s = Get(); return s != null && s.UsePlayerPick ? s.PlayerPick : null; }
    }

    public static VoiceSettingsView VoicePrefab
    {
        get { var s = Get(); return s != null && s.UseVoiceSettings ? s.VoiceSettings : null; }
    }

    public static GameObject HudPrefab
    {
        get { var s = Get(); return s != null && s.UseHud ? s.Hud : null; }
    }

    public static GameObject PanelsPrefab
    {
        get { var s = Get(); return s != null && s.UsePanels ? s.Panels : null; }
    }

    public static RoomScreenView RoomPrefab
    {
        get { var s = Get(); return s != null && s.UseRoom ? s.Room : null; }
    }
}
