using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>키를 코드로 바꿀 수 있는 로컬라이즈 텍스트</summary>
[RequireComponent(typeof(TMP_Text))]
public class LocText : MonoBehaviour
{
    [SerializeField] private string tableName = "LangTable";
    [SerializeField] private string entryKey;

    private TMP_Text _text;
    private object[] _args;

    void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        Refresh();
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    void OnLocaleChanged(Locale _) => Refresh();

    public void SetKey(string key, params object[] args)
    {
        entryKey = key;
        _args = (args != null && args.Length > 0) ? args : null;
        Refresh();
    }

    /// <summary>번역 없이 그대로 표시 (닉네임 등)</summary>
    public void SetRaw(string raw)
    {
        entryKey = null;
        _args = null;
        if (_text == null) _text = GetComponent<TMP_Text>();
        _text.text = raw;
    }

    public void Refresh()
    {
        if (_text == null) _text = GetComponent<TMP_Text>();
        if (string.IsNullOrEmpty(entryKey)) return;

        string s = LocUtil.Get(tableName, entryKey);

        if (_args != null)
        {
            try { s = string.Format(s, _args); }
            catch { }
        }

        _text.text = s;
    }
}