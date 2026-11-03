using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 채팅창 (HUD v2 안 · uGUI) — 켜져 있으면 CommsHud가 예전 IMGUI 채팅 대신 이걸 씀.
/// 열기 · 보내기 · 채널 바꾸기 키는 그대로 CommsHud (Enter · Esc · Tab). 여기는 그리기 + 입력 칸만.
/// 닫혀 있을 때: 새 줄이 잠깐 보였다 사라짐 (설정 '채팅창 보이기'를 끄면 열었을 때만)
/// </summary>
public class ChatView : MonoBehaviour
{
    /// <summary>지금 쓰는 채팅창 (HUD가 꺼져 있으면 null → 예전 IMGUI)</summary>
    public static ChatView Active { get; private set; }

    [Tooltip("줄이 쌓이는 곳 (Vertical Layout Group, 아래 기준)")]
    [SerializeField] private RectTransform lineParent;
    [Tooltip("한 줄 틀 (꺼진 묶음 아래, 켜진 채)")]
    [SerializeField] private TMP_Text lineTemplate;
    [Tooltip("열었을 때만 보이는 배경")]
    [SerializeField] private GameObject openBack;
    [Tooltip("입력 줄 (열었을 때만)")]
    [SerializeField] private GameObject inputRow;
    [SerializeField] private TMP_Text channelText;
    [SerializeField] private TMP_InputField input;

    private readonly List<TMP_Text> _pool = new();
    private int _sig = -1;

    /// <summary>입력한 글 (조합 중인 글자는 CommsHud가 붙임)</summary>
    public string Text => input != null ? input.text : "";

    void OnEnable()
    {
        Active = this;
        bool open = CommsHud.ChatOpen;
        SetOpen(open);
        _sig = -1;
    }

    void OnDisable()
    {
        if (Active == this) Active = null;
    }

    public void Open(string channelTag)
    {
        SetChannel(channelTag);
        SetOpen(true);
        if (input != null)
        {
            input.text = "";
            input.ActivateInputField();
            input.Select();
        }
        _sig = -1;
    }

    public void Close()
    {
        if (input != null)
        {
            input.text = "";
            input.DeactivateInputField();
        }
        SetOpen(false);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        _sig = -1;
    }

    public void SetChannel(string tag)
    {
        if (channelText != null) channelText.text = tag;
    }

    void SetOpen(bool on)
    {
        if (openBack != null && openBack.activeSelf != on) openBack.SetActive(on);
        if (inputRow != null && inputRow.activeSelf != on) inputRow.SetActive(on);
    }

    void LateUpdate()
    {
        bool open = CommsHud.ChatOpen;
        SetOpen(open);

        // 입력 칸이 포커스를 잃으면 (다른 곳 클릭 등) 다시
        if (open && input != null && !input.isFocused) input.ActivateInputField();

        var hud = CommsHud.Instance;
        float fade = hud != null ? hud.ChatFadeTime : 8f;
        int max = open ? (hud != null ? hud.ChatOpenLines : 14) : (hud != null ? hud.ChatShownLines : 8);

        var lines = ChatLog.Lines;
        float now = Time.unscaledTime;
        bool visible = open || GameSettings.ChatVisible;

        // 보일 줄 — 최근 것부터 max개 (닫혀 있으면 fade 안의 것만)
        int start = lines.Count;
        int shown = 0;
        if (visible)
            for (int i = lines.Count - 1; i >= 0 && shown < max; i--)
            {
                if (!open && now - lines[i].Time > fade) break;
                start = i;
                shown++;
            }

        int sig = lines.Count * 31 + shown * 7 + (open ? 1 : 0);
        if (sig != _sig)
        {
            _sig = sig;
            for (int k = 0; k < shown; k++)
            {
                var t = Line(k);
                string s = CommsHud.FormatLine(lines[start + k]);
                if (t.text != s) t.text = s;
            }
            for (int k = shown; k < _pool.Count; k++)
                if (_pool[k].gameObject.activeSelf) _pool[k].gameObject.SetActive(false);
        }

        // 닫혀 있을 땐 오래된 줄부터 흐려짐
        for (int k = 0; k < shown; k++)
        {
            float a = open ? 1f : Mathf.Clamp01((fade - (now - lines[start + k].Time)) / 1.5f);
            if (!Mathf.Approximately(_pool[k].alpha, a)) _pool[k].alpha = a;
        }
    }

    TMP_Text Line(int i)
    {
        while (_pool.Count <= i)
        {
            var t = Instantiate(lineTemplate, lineParent);
            t.name = "Line";
            _pool.Add(t);
        }
        var l = _pool[i];
        if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        l.transform.SetSiblingIndex(i);
        return l;
    }
}
