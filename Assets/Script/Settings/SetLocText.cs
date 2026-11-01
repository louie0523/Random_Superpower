using TMPro;
using UnityEngine;

/// <summary>
/// 글자 하나를 번역 키에 묶음 — 설정 창 · ESC 메뉴의 고정 글자(제목 · 버튼)에 붙어 있음.
/// 프리팹에서 다른 TMP 글자에 붙여도 됨: Key만 넣으면 지금 글자가 Fallback(번역 없을 때)이 됨.
/// </summary>
[DisallowMultipleComponent]
public class SetLocText : MonoBehaviour
{
    [Tooltip("LangTable 키 (예: set.ui.title)")]
    public string Key;
    [Tooltip("번역이 없을 때 보일 글자 — 비우면 처음 글자")]
    [TextArea] public string Fallback;

    TMP_Text _t;
    int _ver = -1;

    public static SetLocText Add(TMP_Text t, string key)
    {
        if (t == null) return null;
        var c = t.GetComponent<SetLocText>();
        if (c == null) c = t.gameObject.AddComponent<SetLocText>();
        c.Key = key;
        c.Fallback = t.text;
        c._t = t;
        return c;
    }

    /// <summary>번역 키가 아직 없는 글자에만 붙임 (프리팹에서 직접 다른 키를 넣어 뒀으면 그대로)</summary>
    public static void Ensure(TMP_Text t, string key)
    {
        if (t == null || t.GetComponent<SetLocText>() != null) return;
        Add(t, key);
    }

    /// <summary>버튼 · 고르기 칸 안의 글자에</summary>
    public static void Ensure(Component c, string key)
    {
        if (c == null) return;
        var t = c is UiOption o && o.Label != null ? o.Label : c.GetComponentInChildren<TMP_Text>(true);
        Ensure(t, key);
    }

    void Awake()
    {
        if (_t == null) _t = GetComponent<TMP_Text>();
        if (string.IsNullOrEmpty(Fallback) && _t != null) Fallback = _t.text;
    }

    void OnEnable() { _ver = -1; Apply(); }
    void Update() => Apply();

    void Apply()
    {
        if (_t == null) return;
        int v = SetLoc.Version;
        if (v == _ver) return;
        _ver = v;
        _t.text = SetLoc.T(Key, Fallback);
    }
}
