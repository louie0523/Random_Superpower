using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 칠하기 도구 막대 — 붓(스프레이 · 마커 · 지우개) · 물감 색 · 크기 · 보정 4개 · 되돌리기 · 다시 · 지우기 · 완료.
/// 고른 붓은 Brush에 · 버튼은 이벤트로. 마지막 설정은 기억함 (PlayerPrefs)
/// PaintToolbarBuilder가 만듦 (프리팹에서 마음대로 옮겨도 됨)
/// </summary>
public class PaintToolbarView : MonoBehaviour
{
    [Header("붓")]
    public UiOption Spray;
    public UiOption Marker;
    public UiOption Eraser;

    [Header("색 (Swatches 순서 = Colors 순서)")]
    public Button[] Swatches;
    [Tooltip("고른 색 표시 (색 버튼마다 하나 — 비워도 됨)")]
    public GameObject[] SwatchRings;
    public Color[] Colors;

    [Header("크기")]
    public Slider Size;

    [Header("보정")]
    public UiOption Stabilize;
    public UiOption Drips;
    public UiOption Outline;
    public UiOption Snap;

    [Header("버튼")]
    public Button Undo;
    public Button Redo;
    public Button Clear;
    public TMP_Text ClearLabel;
    public Button Done;

    public PaintBrush Brush = new();

    public event Action UndoClicked, RedoClicked, ClearClicked, DoneClicked;

    const string PrefKey = "paint.brush";
    float _clearArmed = -10f;
    string _clearText;

    void Awake()
    {
        Load();
        Wire(Spray, () => SetKind(BrushKind.Spray));
        Wire(Marker, () => SetKind(BrushKind.Marker));
        Wire(Eraser, () => SetKind(BrushKind.Eraser));
        Wire(Stabilize, () => { Brush.Stabilize = !Brush.Stabilize; Changed(); });
        Wire(Drips, () => { Brush.Drips = !Brush.Drips; Changed(); });
        Wire(Outline, () => { Brush.Outline = !Brush.Outline; Changed(); });
        Wire(Snap, () => { Brush.Snap = !Brush.Snap; Changed(); });

        if (Swatches != null)
            for (int i = 0; i < Swatches.Length; i++)
            {
                int k = i;
                if (Swatches[i] != null) Swatches[i].onClick.AddListener(() => SetColor(k));
            }

        if (Size != null)
        {
            Size.minValue = 0f;
            Size.maxValue = 1f;
            Size.SetValueWithoutNotify(Brush.Size);
            Size.onValueChanged.AddListener(v => { Brush.Size = v; Changed(); });
        }

        if (Undo != null) Undo.onClick.AddListener(() => UndoClicked?.Invoke());
        if (Redo != null) Redo.onClick.AddListener(() => RedoClicked?.Invoke());
        if (Done != null) Done.onClick.AddListener(() => DoneClicked?.Invoke());
        if (Clear != null)
        {
            if (ClearLabel != null) _clearText = ClearLabel.text;
            Clear.onClick.AddListener(OnClear);
        }
        Paint();
    }

    void Update()
    {
        if (_clearArmed > 0f && Time.unscaledTime - _clearArmed > 2.5f) Disarm();
    }

    static void Wire(UiOption o, Action a)
    {
        if (o != null && o.Button != null) o.Button.onClick.AddListener(() => a());
    }

    /// <summary>지우기 — 한 번 더 눌러야 (실수 방지)</summary>
    void OnClear()
    {
        if (_clearArmed < 0f)
        {
            _clearArmed = Time.unscaledTime;
            if (ClearLabel != null) ClearLabel.text = "정말 지울까?";
            return;
        }
        Disarm();
        ClearClicked?.Invoke();
    }

    void Disarm()
    {
        _clearArmed = -10f;
        if (ClearLabel != null && _clearText != null) ClearLabel.text = _clearText;
    }

    public void SetKind(BrushKind k)
    {
        Brush.Kind = k;
        Changed();
    }

    public void SetColor(int i)
    {
        if (Colors == null || i < 0 || i >= Colors.Length) return;
        Brush.Color = Colors[i];
        if (Brush.IsEraser) Brush.Kind = BrushKind.Spray;
        Changed();
    }

    /// <summary>크기 키 ([ ]) 등에서</summary>
    public void Nudge(float delta)
    {
        Brush.Size = Mathf.Clamp01(Brush.Size + delta);
        if (Size != null) Size.SetValueWithoutNotify(Brush.Size);
        Changed();
    }

    void Changed()
    {
        Paint();
        Save();
    }

    /// <summary>버튼 모양을 지금 붓에 맞춤</summary>
    public void Paint()
    {
        Spray?.SetOn(Brush.Kind == BrushKind.Spray);
        Marker?.SetOn(Brush.Kind == BrushKind.Marker);
        Eraser?.SetOn(Brush.Kind == BrushKind.Eraser);
        Stabilize?.SetOn(Brush.Stabilize);
        Drips?.SetOn(Brush.Drips);
        Outline?.SetOn(Brush.Outline);
        Snap?.SetOn(Brush.Snap);
        if (SwatchRings != null && Colors != null)
            for (int i = 0; i < SwatchRings.Length; i++)
                if (SwatchRings[i] != null)
                    SwatchRings[i].SetActive(i < Colors.Length && !Brush.IsEraser && Close(Colors[i], Brush.Color));
    }

    static bool Close(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    void Save()
    {
        try { PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(Brush)); }
        catch { /* 저장 못 해도 됨 */ }
    }

    void Load()
    {
        try
        {
            string s = PlayerPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(s)) JsonUtility.FromJsonOverwrite(s, Brush);
        }
        catch { /* 처음 */ }
        if (Colors == null || Colors.Length == 0) Colors = PaintBrush.Palette;
    }
}
