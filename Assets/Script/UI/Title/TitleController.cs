using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 타이틀 화면 — 게임 시작(대기실 씬) · 무기 보기 · 스킨/뽑기 · 도감 · 프로필 · 꾸미기 · 설정 · 종료.
/// 화면(TitleScreenView)이 자식에 없으면 Prefab을 띄우고, 그것도 없으면 코드로 만듦.
/// Esc = 열린 창 닫기
/// </summary>
public class TitleController : MonoBehaviour
{
    public TitleScreenView View;
    [Tooltip("비우면 코드로 만듦 (Tools → UI → 타이틀 씬 만들기가 프리팹을 만들어 연결)")]
    public TitleScreenView Prefab;
    [Tooltip("코드로 만들 때 글꼴 (비우면 UiPrefabSet의 글꼴)")]
    public TMP_FontAsset Font;

    static bool _toastShown;
    TitlePanel _open;

    void Awake()
    {
        if (View == null) View = GetComponentInChildren<TitleScreenView>(true);
        if (View == null && Prefab != null) View = Instantiate(Prefab, transform);
        if (View == null)
        {
            var font = Font != null ? Font : UiPrefabSet.Get() != null ? UiPrefabSet.Get().Font : null;
            var go = new GameObject("TitleScreen");
            go.transform.SetParent(transform, false);
            View = TitleUiBuilder.Build(go, font);
        }
        GameUiKit.EnsureEventSystem();
        Wire();
    }

    void OnEnable() => PlayerSave.Changed += Paint;
    void OnDisable() => PlayerSave.Changed -= Paint;

    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;

        var meta = GameMeta.Get();
        if (View.Logo != null && meta != null && !string.IsNullOrEmpty(meta.GameTitle)) View.Logo.text = meta.GameTitle;
        if (View.Version != null) View.Version.text = "v" + Application.version;
        if (View.Nick != null) View.Nick.SetTextWithoutNotify(RoomLauncher.Nick);
        Paint();
        ShowToast();
        if (meta == null) Debug.LogWarning("[타이틀] Resources/GameMeta가 없어 — Create → Game/Meta/Game Meta 로 만들어 Resources 폴더에");
    }

    void Wire()
    {
        var v = View;
        On(v.Play, () =>
        {
            var meta = GameMeta.Get();
            string scene = meta != null ? meta.LobbyScene : "Lobby";
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else Debug.LogWarning($"[타이틀] '{scene}' 씬이 Build Settings에 없어");
        });
        On(v.Weapons, () => Open(v.WeaponPanel));
        On(v.Skins, () => Open(v.SkinPanel));
        On(v.Codex, () => Open(v.CodexPanel));
        On(v.Profile, () => Open(v.ProfilePanel));
        On(v.Customize, () => Open(v.CustomizePanel));
        On(v.Paint, StartPaint);
        if (v.PaintBar != null) v.PaintBar.gameObject.SetActive(false);
        On(v.Settings, () => SettingsWindow.Open());
        On(v.Quit, () =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });

        if (v.Nick != null)
            v.Nick.onEndEdit.AddListener(s =>
            {
                s = (s ?? "").Trim();
                if (s.Length == 0) { v.Nick.SetTextWithoutNotify(RoomLauncher.Nick); return; }
                RoomLauncher.Nick = s;
            });

        if (v.WeaponPanel != null) v.WeaponPanel.OpenSkins = id =>
        {
            v.WeaponPanel.Hide();
            if (v.SkinPanel == null) return;
            _open = v.SkinPanel;
            if (v.Menu != null) v.Menu.SetActive(false);
            v.SkinPanel.ShowWeapon(id);
        };

        foreach (var p in new TitlePanel[] { v.WeaponPanel, v.SkinPanel, v.CodexPanel, v.ProfilePanel, v.CustomizePanel })
        {
            if (p == null) continue;
            if (p.Frame == null) p.Frame = p.GetComponent<TitlePanelFrame>();
            p.Frame.gameObject.SetActive(false);
            var panel = p;
            p.Frame.Closed += () => { if (_open == panel) Closed(); };
        }
        if (v.Toast != null) v.Toast.SetActive(false);
    }

    static void On(UnityEngine.UI.Button b, UnityEngine.Events.UnityAction a)
    {
        if (b != null) b.onClick.AddListener(a);
    }

    void Open(TitlePanel p)
    {
        if (p == null) return;
        if (_open != null && _open != p) _open.Hide();
        _open = p;
        if (View.Menu != null) View.Menu.SetActive(false);
        p.Show();
        GameUiKit.DropButtonFocus();
    }

    void Closed()
    {
        _open = null;
        if (View.Menu != null) View.Menu.SetActive(true);
        GameUiKit.DropButtonFocus();
    }

    // ══════════════════ 벽 칠하기 ══════════════════

    void StartPaint()
    {
        if (View.PaintBar == null) { Debug.LogWarning("[타이틀] 칠하기 도구 막대(PaintBar)가 없어 — 타이틀 화면 프리팹 다시 만들기"); return; }
        var mode = TitlePaintMode.Instance != null ? TitlePaintMode.Instance : FindFirstObjectByType<TitlePaintMode>();
        if (mode == null) mode = gameObject.AddComponent<TitlePaintMode>();
        if (_open != null) _open.Hide();
        SetPaintHidden(true);
        mode.Enter(View.PaintBar, () => SetPaintHidden(false));
        GameUiKit.DropButtonFocus();
    }

    void SetPaintHidden(bool hide)
    {
        if (View.HideWhilePainting != null)
            foreach (var g in View.HideWhilePainting) if (g != null) g.SetActive(!hide);
        if (!hide && View.Menu != null) View.Menu.SetActive(_open == null);
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (PaintMode.On)
        {
            if (TitlePaintMode.Instance != null) TitlePaintMode.Instance.Exit();
            return;
        }
        if (SettingsWindow.IsOpen || SettingsWindow.ClosedFrame == Time.frameCount) return;     // 설정 창이 스스로 닫음
        if (_open != null) _open.Hide();
    }

    void Paint()
    {
        if (View == null) return;
        if (View.SC != null) View.SC.text = $"{PlayerSave.SC:N0} SC";
        if (View.Tickets != null) View.Tickets.text = $"티켓 {PlayerSave.Tickets}";
    }

    void ShowToast()
    {
        if (_toastShown || View.Toast == null || MatchRecorder.LastReward is not { } r) return;
        _toastShown = true;
        if (View.ToastText != null)
            View.ToastText.text = $"지난 판 {r.Rank}등" + (r.Win ? " · 승리!" : "") + $"  +{r.SC} SC" + (r.Tickets > 0 ? $" · 티켓 +{r.Tickets}" : "");
        StartCoroutine(Toast());
    }

    IEnumerator Toast()
    {
        View.Toast.SetActive(true);
        yield return new WaitForSecondsRealtime(5f);
        View.Toast.SetActive(false);
    }
}
