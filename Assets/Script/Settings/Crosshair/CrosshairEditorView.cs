using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조준점 편집 (설정 창 조준점 탭 위쪽) — 무기 종류 고르기 · 미리보기 · 코드 복사/붙여넣기 · 초기화 · 모두에 복사.
/// 세부 값(색 · 선 · 점 …)은 같은 탭의 SettingRow(xh.*)들이 바꿈.
/// </summary>
public class CrosshairEditorView : MonoBehaviour
{
    [Header("무기 종류")]
    public UiOption GunOption;
    public UiOption BowOption;
    public UiOption MeleeOption;
    [Tooltip("'모든 무기에 같은 조준점'일 때 보이는 안내 (없어도 됨)")]
    public TMP_Text SharedNote;

    [Header("미리보기")]
    public CrosshairGraphic Preview;
    public Image PreviewBack;
    [Tooltip("누르면 미리보기 배경 바꾸기")]
    public Button BackButton;
    [Tooltip("켜면 미리보기에서 벌어짐 움직임을 보여 줌")]
    public UiOption AnimateOption;

    [Header("코드")]
    public Button CopyButton;
    public Button PasteButton;
    public Button ResetButton;
    public Button CopyToAllButton;
    public TMP_Text Status;

    static readonly Color[] Backs =
    {
        new(0.36f, 0.42f, 0.36f), new(0.75f, 0.82f, 0.9f), new(0.12f, 0.12f, 0.14f), new(0.92f, 0.9f, 0.84f),
    };

    int _back;
    bool _animate = true;
    float _statusUntil;

    void Awake()
    {
        Kind(GunOption, CrosshairKind.Gun);
        Kind(BowOption, CrosshairKind.Bow);
        Kind(MeleeOption, CrosshairKind.Melee);

        if (BackButton != null) BackButton.onClick.AddListener(() => _back = (_back + 1) % Backs.Length);
        if (AnimateOption != null && AnimateOption.Button != null) AnimateOption.Button.onClick.AddListener(() => _animate = !_animate);

        if (CopyButton != null) CopyButton.onClick.AddListener(() =>
        {
            GUIUtility.systemCopyBuffer = CrosshairSettings.Edit.ToCode();
            Say("코드를 클립보드에 복사함");
        });
        if (PasteButton != null) PasteButton.onClick.AddListener(() =>
        {
            if (CrosshairStyle.TryParse(GUIUtility.systemCopyBuffer, out var s))
            {
                CrosshairSettings.Set(CrosshairSettings.ShareAll ? CrosshairKind.Gun : CrosshairSettings.Editing, s);
                Say("붙여 넣음");
            }
            else Say("클립보드에 조준점 코드가 없어");
        });
        if (ResetButton != null) ResetButton.onClick.AddListener(() => { CrosshairSettings.ResetEdit(); Say("기본값으로 돌림"); });
        if (CopyToAllButton != null) CopyToAllButton.onClick.AddListener(() => { CrosshairSettings.CopyEditToAll(); Say("다른 무기에도 똑같이 넣음"); });
    }

    void Kind(UiOption o, CrosshairKind k)
    {
        if (o != null && o.Button != null) o.Button.onClick.AddListener(() => CrosshairSettings.Editing = k);
    }

    void Say(string msg)
    {
        GameUiKit.Set(Status, msg);
        _statusUntil = Time.unscaledTime + 3f;
    }

    /// <summary>설정 창이 매 프레임 부름</summary>
    public void Tick()
    {
        bool share = CrosshairSettings.ShareAll;
        var ed = CrosshairSettings.Editing;
        Opt(GunOption, ed == CrosshairKind.Gun || share, !share);
        Opt(BowOption, ed == CrosshairKind.Bow && !share, !share);
        Opt(MeleeOption, ed == CrosshairKind.Melee && !share, !share);
        if (SharedNote != null) GameUiKit.Active(SharedNote, share);
        GameUiKit.Active(CopyToAllButton, !share);

        if (AnimateOption != null) AnimateOption.SetOn(_animate);
        if (PreviewBack != null) PreviewBack.color = Backs[_back];

        if (Preview != null)
        {
            float t = Time.unscaledTime;
            float move = _animate ? (Mathf.Sin(t * 1.3f) * 0.5f + 0.5f) * 10f : 0f;
            float fire = _animate ? Mathf.Max(0f, Mathf.Sin(t * 3.1f)) * 8f : 0f;
            Preview.Draw(CrosshairSettings.Edit, move, fire);
        }

        if (Status != null && Time.unscaledTime > _statusUntil) GameUiKit.Set(Status, "");
    }

    static void Opt(UiOption o, bool on, bool interact)
    {
        if (o == null) return;
        o.SetOn(on);
        GameUiKit.Interact(o.Button, interact);
    }
}
