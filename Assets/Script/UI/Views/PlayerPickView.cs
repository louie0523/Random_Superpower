using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 대상 선택 창 프리팹 (저주 인형 · 데스 노트). CommsHud가 열고 닫음 — 여기엔 화면만.
/// 줄은 RowPrefab(UiListRow)으로 만듦: Label = 이름, Sub = (나) · (사망), Button = 줄 전체.
/// </summary>
public class PlayerPickView : MonoBehaviour
{
    public GameObject Root;
    public TMP_Text Title;
    public RectTransform ListContent;
    public UiListRow RowPrefab;
    [Tooltip("고를 사람이 없을 때 (없어도 됨)")]
    public TMP_Text EmptyText;
    public Button CancelButton;

    [Header("줄 색")]
    public Color RowColor = Color.white;
    public Color SelfColor = new(1f, 0.95f, 0.78f, 1f);
    public Color DeadColor = new(0.85f, 0.85f, 0.87f, 1f);

    public struct Entry
    {
        public PlayerHealth Target;
        public string Name;
        public bool Self;
        public bool Dead;
    }

    readonly List<UiListRow> _rows = new();
    readonly List<PlayerHealth> _targets = new();
    Action<PlayerHealth> _onPick;
    Action _onCancel;

    void Awake()
    {
        if (CancelButton != null) CancelButton.onClick.AddListener(() => { var c = _onCancel; c?.Invoke(); });
        if (ListContent != null)
            for (int i = ListContent.childCount - 1; i >= 0; i--)          // 미리보기 줄 지우기
                if (ListContent.GetChild(i).GetComponent<UiListRow>() != null)
                    Destroy(ListContent.GetChild(i).gameObject);
    }

    public void Show(string title, Action<PlayerHealth> onPick, Action onCancel)
    {
        _onPick = onPick;
        _onCancel = onCancel;
        GameUiKit.Set(Title, title);
        GameUiKit.EnsureEventSystem();
        GameUiKit.Active(Root, true);
    }

    public void Hide()
    {
        _onPick = null;
        _onCancel = null;
        GameUiKit.Active(Root, false);
    }

    public void SetEntries(List<Entry> list)
    {
        while (_rows.Count < list.Count)
        {
            var r = Instantiate(RowPrefab, ListContent);
            int idx = _rows.Count;
            if (r.Button != null) r.Button.onClick.AddListener(() => OnRow(idx));
            if (r.Action != null) r.Action.onClick.AddListener(() => OnRow(idx));
            _rows.Add(r);
        }

        _targets.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool used = i < list.Count;
            GameUiKit.Active(row, used);
            if (!used) continue;

            var e = list[i];
            _targets.Add(e.Target);
            GameUiKit.Set(row.Label, e.Name);
            GameUiKit.Set(row.Sub, e.Dead ? TargetUtil.Text("ui.pick.dead", "(사망)")
                                  : e.Self ? TargetUtil.Text("ui.pick.self", "(나)").Trim() : "");
            if (row.Face != null) row.Face.color = e.Dead ? DeadColor : e.Self ? SelfColor : RowColor;
            GameUiKit.Interact(row.Button, !e.Dead);
            GameUiKit.Interact(row.Action, !e.Dead);
        }

        if (EmptyText != null) GameUiKit.Active(EmptyText, list.Count == 0);
    }

    void OnRow(int i)
    {
        if (i < 0 || i >= _targets.Count || _targets[i] == null) return;
        var p = _onPick;
        p?.Invoke(_targets[i]);
    }

    void Update()
    {
        if (Root != null && Root.activeSelf) GameUiKit.DropButtonFocus();
    }
}
