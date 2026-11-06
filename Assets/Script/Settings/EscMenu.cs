using UnityEngine;
using UnityEngine.SceneManagement;

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

    /// <summary>타이틀 씬이 빌드에 있고 지금 타이틀이 아님</summary>
    public static bool TitleAvailable
    {
        get
        {
            string s = TitleScene;
            return !string.IsNullOrEmpty(s) && SceneManager.GetActiveScene().name != s && Application.CanStreamedLevelBeLoaded(s);
        }
    }

    static string TitleScene
    {
        get
        {
            var m = GameMeta.Get();
            return m != null && !string.IsNullOrEmpty(m.TitleScene) ? m.TitleScene : "Title";
        }
    }

    /// <summary>타이틀로 — 방에 있으면 나가고 연결을 끊은 뒤 타이틀 씬</summary>
    public static void ToTitle()
    {
        Close();
        SettingsWindow.Close();
        string scene = TitleScene;
        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogWarning($"[ESC] 타이틀 씬 '{scene}'이 Build Settings에 없습니다");
            return;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (RoomLauncher.Instance != null) RoomLauncher.Instance.QuitToTitle(scene);
        else SceneManager.LoadScene(scene);
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
        // 타이틀 씬은 자기 메뉴가 있어서 ESC 메뉴를 안 띄움
        bool inTitle = SceneManager.GetActiveScene().name == TitleScene;
        if (esc && !inTitle && !others && !_othersOpenLastFrame && !SettingsWindow.IsOpen && SettingsWindow.ClosedFrame != Time.frameCount)
        {
            if (IsOpen) Close();
            else Open();
        }

        _othersOpenLastFrame = others;
    }
}
