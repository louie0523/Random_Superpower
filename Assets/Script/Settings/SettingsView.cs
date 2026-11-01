using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class SettingsTab
{
    [Tooltip("SettingsRegistry 탭 id (general · graphics · audio · controls · crosshair · voice · hud)")]
    public string Id;
    public UiOption Button;
    public GameObject Page;
}

/// <summary>
/// 설정 창 (화면). 안의 SettingRow들을 Key로 찾아 저절로 연결함 — 프리팹에서 줄을 옮기거나 지워도 됨.
/// 열고 닫기는 SettingsWindow가 함.
/// </summary>
public class SettingsView : MonoBehaviour
{
    public GameObject Root;
    public TMP_Text Title;
    public List<SettingsTab> Tabs = new();
    public Button CloseButton;
    [Tooltip("조준점 편집 (조준점 탭 안 — 없어도 됨)")]
    public CrosshairEditorView Crosshair;

    readonly List<SettingRow> _rows = new();
    readonly Dictionary<SettingRow, GameObject> _rowPage = new();
    string _tab;
    readonly Dictionary<SettingsTab, string> _tabFallback = new();
    int _locVer = -1;

    public string CurrentTab => _tab;

    void Awake()
    {
        // 예전에 만든 프리팹에도 번역 · 글자 맞춤이 되게 (이미 있으면 건드리지 않음)
        SetLocText.Ensure(Title, "set.ui.title");
        SetLocText.Ensure(CloseButton, "set.ui.close");
        if (Crosshair != null) Crosshair.EnsureLoc();
        UiTextFit.Apply(transform);

        foreach (var r in GetComponentsInChildren<SettingRow>(true))
        {
            var def = SettingsRegistry.Get(r.Key);
            if (def == null)
            {
                Debug.LogWarning($"[Settings] '{r.Key}' 설정이 없어 — 그 줄은 숨김", r);
                r.gameObject.SetActive(false);
                continue;
            }
            r.Bind(def);
            _rows.Add(r);
            foreach (var t in Tabs)
                if (t.Page != null && r.transform.IsChildOf(t.Page.transform)) _rowPage[r] = t.Page;
        }

        foreach (var t in Tabs)
        {
            var id = t.Id;
            if (t.Button != null && t.Button.Button != null) t.Button.Button.onClick.AddListener(() => ShowTab(id));
            if (t.Button != null && t.Button.Label != null) _tabFallback[t] = t.Button.Label.text;   // 번역 없을 때 보일 글자
        }

        if (CloseButton != null) CloseButton.onClick.AddListener(SettingsWindow.Close);
    }

    public void Show(string tab)
    {
        GameUiKit.EnsureEventSystem();
        GameUiKit.Active(Root, true);
        ShowTab(!string.IsNullOrEmpty(tab) ? tab : _tab ?? (Tabs.Count > 0 ? Tabs[0].Id : null));
    }

    public void Hide() => GameUiKit.Active(Root, false);

    public void ShowTab(string id)
    {
        if (Tabs.Find(t => t.Id == id) == null && Tabs.Count > 0) id = Tabs[0].Id;
        _tab = id;
        foreach (var t in Tabs)
        {
            bool on = t.Id == id;
            if (t.Button != null) t.Button.SetOn(on);
            GameUiKit.Active(t.Page, on);
        }
    }

    void Update()
    {
        if (Root == null || !Root.activeSelf) return;
        GameUiKit.DropButtonFocus();

        // 탭 이름 번역 (set.tab.{id}) — 언어가 바뀔 때만
        int ver = SetLoc.Version;
        if (ver != _locVer)
        {
            _locVer = ver;
            foreach (var t in Tabs)
                if (t.Button != null && _tabFallback.TryGetValue(t, out var fb))
                    t.Button.SetText(SetLoc.T("set.tab." + t.Id, fb));
        }

        foreach (var r in _rows)
        {
            if (_rowPage.TryGetValue(r, out var page) && page != null && !page.activeSelf) continue;   // 안 보이는 탭은 건너뜀
            r.Refresh();
        }

        if (Crosshair != null && Crosshair.gameObject.activeInHierarchy) Crosshair.Tick();
    }
}
