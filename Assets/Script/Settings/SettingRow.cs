using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 한 줄 (화면). Key = SettingsRegistry의 키 — 이것만 맞으면 프리팹에서 자유롭게 옮기고 꾸며도 됨.
/// 종류에 맞는 칸만 있으면 됨: 켜기/끄기 = Toggle, 숫자 = Slider, 고르기 = Prev · Next, 키 = KeyButton.
/// </summary>
public class SettingRow : MonoBehaviour
{
    [Tooltip("SettingsRegistry 키 (예: audio.master, xh.innerLen)")]
    public string Key;

    public TMP_Text Label;
    [Tooltip("작은 설명 (없어도 됨)")]
    public TMP_Text Note;
    [Tooltip("지금 값 글자 (없어도 됨)")]
    public TMP_Text Value;

    [Header("켜기 / 끄기")]
    public UiOption Toggle;

    [Header("숫자")]
    public Slider Slider;

    [Header("고르기")]
    public Button Prev;
    public Button Next;

    [Header("키")]
    public Button KeyButton;
    public TMP_Text KeyLabel;

    public SettingDef Def { get; private set; }
    bool _wired;

    public void Bind(SettingDef def)
    {
        Def = def;
        if (def == null || _wired) return;
        _wired = true;

        if (Toggle != null && Toggle.Button != null)
            Toggle.Button.onClick.AddListener(() => { if (Def.SetB != null) Def.SetB(!Def.GetB()); });

        if (Slider != null)
        {
            Slider.minValue = def.Min;
            Slider.maxValue = def.Max;
            Slider.wholeNumbers = def.Whole;
            Slider.onValueChanged.AddListener(v => { if (Def.SetF != null) Def.SetF(v); });
        }

        if (Prev != null) Prev.onClick.AddListener(() => Step(-1));
        if (Next != null) Next.onClick.AddListener(() => Step(1));
        if (KeyButton != null) KeyButton.onClick.AddListener(() =>
        {
            if (Def.Kind == SettingKind.Action) { if (Def.Run != null) Def.Run(); }
            else SettingsWindow.StartKeyBind(Def);
        });
    }

    void Step(int d)
    {
        if (Def.Options == null || Def.SetI == null) return;
        var o = Def.Options();
        if (o == null || o.Length == 0) return;
        int i = Def.GetI();
        i = i < 0 ? 0 : ((i + d) % o.Length + o.Length) % o.Length;
        Def.SetI(i);
    }

    /// <summary>값 다시 읽기 — 창이 열려 있는 동안 계속 부름</summary>
    public void Refresh()
    {
        if (Def == null) return;

        bool visible = Def.Visible == null || Def.Visible();
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        if (!visible) return;

        string label = Def.LabelText;
        GameUiKit.Set(Label, Def.Pending ? $"{label}  <size=70%><color=#9a9aa5>({SetLoc.T("set.ui.pending", "준비 중")})</color></size>" : label);
        if (Note != null)
        {
            string note = Def.NoteText;
            bool has = !string.IsNullOrEmpty(note);
            GameUiKit.Active(Note, has);
            if (has) GameUiKit.Set(Note, note);
        }

        switch (Def.Kind)
        {
            case SettingKind.Toggle:
                if (Toggle != null)
                {
                    bool on = Def.GetB();
                    Toggle.SetOn(on);
                    Toggle.SetText(on ? SetLoc.On : SetLoc.Off);
                }
                break;
            case SettingKind.Slider:
                if (Slider != null) Slider.SetValueWithoutNotify(Def.GetF());
                break;
            case SettingKind.Key:
                GameUiKit.Set(KeyLabel, SettingsWindow.WaitingFor == Def ? SetLoc.T("set.ui.press_key", "키를 누르세요… (Esc 취소)") : KeyBinds.Name(Def.GetK()));
                break;
            case SettingKind.Action:
                GameUiKit.Set(KeyLabel, Def.ButtonLabel());
                break;
        }

        GameUiKit.Set(Value, Def.ValueText());
    }
}
