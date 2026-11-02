using UnityEngine;

/// <summary>
/// ESC 메뉴 — 다른 창(채팅 · 인벤 · 상점 · 선택 창 · 콘솔 등)이 안 열려 있을 때 Esc.
/// 게임 중에도 게임은 안 멈춤 (멀티) — 내 입력만 막힘 (UIManager.InputBlocked에 연결).
/// 모양: Resources/UiPrefabSet의 'ESC 메뉴'를 켜면 그 프리팹, 아니면 기본 모양. 씬에 없어도 저절로 생김.
/// </summary>
public class EscMenu : MonoBehaviour
{
    static EscMenu _inst;
    public static bool IsOpen { get; private set; }

    EscMenuView _view;
    bool _othersOpenLastFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_inst != null) return;
        var go = new GameObject("EscMenu");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<EscMenu>();
    }

    void Awake()
    {
        if (_inst != null && _inst != this) { Destroy(gameObject); return; }
        _inst = this;
    }

    void OnDestroy()
    {
        if (_inst == this) { _inst = null; IsOpen = false; }
    }

    /// <summary>Esc를 먼저 쓰는 다른 창들</summary>
    static bool OthersOpen
    {
        get
        {
            if (CommsHud.ChatOpen || DevConsole.IsOpen || PlayerPickPanel.IsShowing ||
                VoiceSettingsPanel.IsOpen || ClassPickPanel.IsShowing || BlackMarketWindow.IsOpen) return true;
            var ui = UIManager.Instance;
            return ui != null && ui.AnyPanelOpen;
        }
    }

    EscMenuView View()
    {
        if (_view != null) return _view;
        var prefab = UiPrefabSet.EscMenuPrefab;
        if (prefab != null) _view = Instantiate(prefab, transform);
        else
        {
            var go = new GameObject("EscMenuView");
            go.SetActive(false);        // 다 만든 뒤 켜야 Awake가 버튼을 찾음
            go.transform.SetParent(transform, false);
            _view = SettingsUiBuilders.EscMenu(go, UiFont.Get());
            go.SetActive(true);
        }
        _view.Hide();
        return _view;
    }

    static bool InRoom =>
        RoomLauncher.Instance != null && RoomLauncher.Instance.State == RoomLauncher.LinkState.InRoom;

    public static void Open()
    {
        if (_inst == null) AutoCreate();
        IsOpen = true;
        _inst.View().Show(InRoom);
    }

    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (_inst != null && _inst._view != null) _inst._view.Hide();
        RelockCursor();
    }

    public static void LeaveRoom()
    {
        Close();
        SettingsWindow.Close();
        if (RoomLauncher.Instance != null) RoomLauncher.Instance.Leave();
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>창을 닫은 뒤 — 게임 중(내 캐릭터 · 관전)이고 다른 창이 없으면 커서 잠금</summary>
    public static void RelockCursor()
    {
        if (IsOpen || SettingsWindow.IsOpen || OthersOpen) return;
        bool playing = PlayerAttack.Local != null || Spectator.Active;
        bool lobbyUi = RoomLauncher.Instance != null && !playing;
        if (!playing || lobbyUi) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (IsOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        bool others = OthersOpen;
        bool esc = Input.GetKeyDown(KeyCode.Escape);

        // 다른 창이 이번 · 지난 프레임에 열려 있었으면 그 창이 Esc를 씀 · 설정 창이 방금 닫혔어도
        if (esc && !others && !_othersOpenLastFrame && !SettingsWindow.IsOpen && SettingsWindow.ClosedFrame != Time.frameCount)
        {
            if (IsOpen) Close();
            else Open();
        }

        _othersOpenLastFrame = others;
    }
}
