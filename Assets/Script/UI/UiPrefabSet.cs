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

    [Header("글꼴")]
    [Tooltip("코드로 만드는 화면(설정 창 · ESC 메뉴)의 글꼴 — 한글 TMP 글꼴")]
    public TMPro.TMP_FontAsset Font;

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

    public static RoomScreenView RoomPrefab
    {
        get { var s = Get(); return s != null && s.UseRoom ? s.Room : null; }
    }
}
