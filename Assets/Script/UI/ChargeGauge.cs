using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChargeGauge : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Image fill;               // Filled / Horizontal
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text valueText;

    [Tooltip("메인 능력 Charges 배열의 몇 번째를 표시할지")]
    [SerializeField, Range(0, 3)] private int chargeIndex = 0;

    [SerializeField] private Color readyColor = new(1f, 0.61f, 0.1f);
    [SerializeField] private Color disabledColor = new(0.5f, 0.5f, 0.5f);

    private AbilityRuntime _ar;
    private int _cachedMainId = int.MinValue;
    private string _cachedLocale;

    public void Bind(AbilityRuntime ar)
    {
        _ar = ar;
        _cachedMainId = int.MinValue;
    }

    void Update()
    {
        var main = _ar != null ? _ar.GetMainAbility() : null;
        bool has = main != null && main.Charges != null && chargeIndex < main.Charges.Length;

        if (root != null && root.activeSelf != has) root.SetActive(has);
        if (!has) return;

        var info = main.Charges[chargeIndex];

        // 라벨은 능력이나 언어가 바뀔 때만 갱신 (매 프레임 번역 조회 금지)
        string locale = UnityEngine.Localization.Settings.LocalizationSettings
                            .SelectedLocale?.Identifier.Code;
        if (main.Id != _cachedMainId || locale != _cachedLocale)
        {
            _cachedMainId = main.Id;
            _cachedLocale = locale;
            if (label != null)
                label.text = LocUtil.GetOr($"ability.{main.Id}.charge.{info.Slot}", info.DisplayName);
        }

        float ratio = _ar.GetChargeRatio(info.Slot);
        bool full = ratio >= 0.999f;

        // 일격의 대가: 근접 무기가 아니면 회색
        var esa = _ar.FindAbility<EmpoweredStrikeAbility>();
        bool usable = esa == null || esa.CanUseNow;

        if (fill != null)
        {
            fill.fillAmount = ratio;
            fill.color = !usable ? disabledColor : full ? readyColor : info.BarColor;
        }

        if (valueText != null) valueText.text = $"{Mathf.FloorToInt(ratio * 100f)}%";
    }
}