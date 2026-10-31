using TMPro;
using UnityEngine;

/// <summary>
/// 설정 창 열고 닫기 · 키 바꾸기 받기. 씬에 없어도 저절로 생김.
/// 모양: Resources/UiPrefabSet의 '설정 창'을 켜면 그 프리팹, 아니면 기본 모양(코드로 만듦 — 같은 카툰 모양).
/// 열기: ESC 메뉴 → 설정 / 보이스 설정 키(O) → 채팅 · 보이스 탭
/// </summary>
public class SettingsWindow : MonoBehaviour
{
    static SettingsWindow _inst;

    public static bool IsOpen { get; private set; }

    /// <summary>창을 닫은 프레임 (같은 Esc로 ESC 메뉴가 또 반응하지 않게)</summary>
    public static int ClosedFrame { get; private set; } = -10;

    /// <summary>키 바꾸기 기다리는 줄</summary>
    public static SettingDef WaitingFor { get; private set; }

    SettingsView _view;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_inst != null) return;
        var go = new GameObject("SettingsWindow");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<SettingsWindow>();
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

    SettingsView View()
    {
        if (_view != null) return _view;

        var prefab = UiPrefabSet.SettingsPrefab;
        if (prefab != null) _view = Instantiate(prefab, transform);
        else
        {
            var go = new GameObject("SettingsView");
            go.SetActive(false);        // 다 만든 뒤 켜야 Awake가 줄들을 찾음
            go.transform.SetParent(transform, false);
            _view = SettingsUiBuilders.Settings(go, UiFont.Get());
            go.SetActive(true);
        }
        _view.Hide();
        return _view;
    }

    /// <summary>tab = SettingsRegistry 탭 id (null이면 마지막에 본 탭)</summary>
    public static void Open(string tab = null)
    {
        if (_inst == null) AutoCreate();
        IsOpen = true;
        WaitingFor = null;
        _inst.View().Show(tab);
    }

    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        WaitingFor = null;
        ClosedFrame = Time.frameCount;
        if (_inst != null && _inst._view != null) _inst._view.Hide();

        GameSettings.Save();
        VoiceSettings.Save();
        GameAudio.Save();
        EscMenu.RelockCursor();
    }

    static int _waitFrame;

    public static void StartKeyBind(SettingDef def)
    {
        if (def == null || def.Kind != SettingKind.Key) return;
        WaitingFor = def;
        _waitFrame = Time.frameCount;
    }

    void Update()
    {
        if (!IsOpen) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (WaitingFor != null)
        {
            // 마우스 버튼도 키로 (Event로는 안 잡힘) — 왼쪽은 칸을 누른 다음 프레임부터
            for (var k = KeyCode.Mouse0; k <= KeyCode.Mouse6; k++)
            {
                if (k == KeyCode.Mouse0 && Time.frameCount <= _waitFrame + 1) continue;
                if (Input.GetKeyDown(k)) { WaitingFor.SetK(k); WaitingFor = null; return; }
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void OnGUI()
    {
        if (!IsOpen || WaitingFor == null) return;
        var e = Event.current;
        if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;

        if (e.keyCode != KeyCode.Escape) WaitingFor.SetK(e.keyCode);
        WaitingFor = null;
        e.Use();
    }
}

/// <summary>코드로 만드는 화면의 글꼴 — UiPrefabSet 글꼴 → 씬의 아무 TMP 글꼴 → TMP 기본</summary>
public static class UiFont
{
    static TMP_FontAsset _font;

    public static TMP_FontAsset Get()
    {
        if (_font != null) return _font;
        var set = UiPrefabSet.Get();
        if (set != null && set.Font != null) return _font = set.Font;
        var any = Object.FindFirstObjectByType<TMP_Text>();
        if (any != null && any.font != null) return _font = any.font;
        return TMP_Settings.defaultFontAsset;
    }
}
