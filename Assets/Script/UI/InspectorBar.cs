using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InspectorBar : MonoBehaviour
{
    [Header("정보")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private Image portrait;

    [Header("체력")]
    [SerializeField] private Image hpFill;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private Gradient hpGradient;

    [Header("쉴드 — 비우면 체력바 바로 위에 저절로 만듦")]
    [Tooltip("쉴드 칸 (안에 재생 · 소울 · 소모품 조각이 생김)")]
    [SerializeField] private RectTransform shieldBar;
    [SerializeField] private float shieldBarHeight = 6f;
    [SerializeField] private Color shieldRegenColor = new(0.96f, 0.97f, 1f, 0.95f);   // 하얀색
    [SerializeField] private Color shieldSoulColor = new(0.55f, 0.82f, 1f, 0.95f);    // 하늘색
    [SerializeField] private Color shieldTempColor = new(0.33f, 0.58f, 1f, 0.95f);    // 파란색

    private RectTransform _segRegen, _segSoul, _segTemp;

    [Header("소울")]
    [SerializeField] private TMP_Text soulText;
    [SerializeField] private LocText soulLabel;

    [Header("상태")]
    [SerializeField] private GameObject combatIcon;
    [SerializeField] private LocText combatLabel;

    private PlayerHealth _hp;
    private PlayerStats _stats;

    void Start()
    {
        soulLabel?.SetKey("ui.hud.soul");
        combatLabel?.SetKey("ui.hud.in_combat");
    }

    public void Bind(PlayerHealth hp, PlayerStats stats, string nickname)
    {
        _hp = hp;
        _stats = stats;
        if (nicknameText != null) nicknameText.text = nickname;   // 닉네임은 번역 안 함
    }

    void Update()
    {
        if (_hp == null) return;

        float max = _stats != null ? Mathf.Max(1f, _stats.Get(StatType.MaxHP)) : 100f;
        float ratio = Mathf.Clamp01(_hp.HP / max);

        if (hpFill != null)
        {
            hpFill.fillAmount = ratio;
            if (hpGradient != null) hpFill.color = hpGradient.Evaluate(ratio);
        }

        int shield = _hp.ShieldTotal;
        if (hpText != null)
            hpText.text = shield > 0
                ? $"{_hp.HP} / {Mathf.RoundToInt(max)}  <color=#bfe3ff>+{shield}</color>"
                : $"{_hp.HP} / {Mathf.RoundToInt(max)}";

        DrawShield(max);

        if (soulText != null) soulText.text = _hp.Soul.ToString();

        if (combatIcon != null)
            combatIcon.SetActive(_hp.InCombat && !_hp.IsDead);
    }

    // ══════════════════ 쉴드 ══════════════════

    void DrawShield(float maxHp)
    {
        bool any = _hp.ShieldTotal > 0 || _hp.ShieldRegenMax > 0 || _hp.ShieldSoulMax > 0;
        if (shieldBar == null && (!any || !BuildShieldBar())) return;
        if (shieldBar.gameObject.activeSelf != any) shieldBar.gameObject.SetActive(any);
        if (!any) return;
        if (_segRegen == null) FindSegments();

        // 칸 전체 = 최대 체력 (쉴드가 더 많으면 쉴드 합)
        float full = Mathf.Max(maxHp, _hp.ShieldTotal, 1f);
        float x = 0f;
        x = Seg(_segRegen, x, _hp.ShieldRegen / full);
        x = Seg(_segSoul, x, _hp.ShieldSoul / full);
        Seg(_segTemp, x, _hp.ShieldTemp / full);

        // 소울 충전 중 — 깜빡임
        if (_segSoul != null && _segSoul.TryGetComponent<Image>(out var si))
        {
            var c = shieldSoulColor;
            if (_hp.ShieldCharging) c.a *= 0.55f + 0.45f * Mathf.PingPong(Time.time * 4f, 1f);
            si.color = c;
        }
    }

    static float Seg(RectTransform r, float from, float w)
    {
        if (r == null) return from;
        float to = Mathf.Clamp01(from + Mathf.Max(0f, w));
        r.anchorMin = new Vector2(from, 0f);
        r.anchorMax = new Vector2(to, 1f);
        return to;
    }

    bool BuildShieldBar()
    {
        if (hpFill == null) return false;
        var parent = hpFill.rectTransform.parent as RectTransform;
        if (parent == null) return false;

        var go = new GameObject("ShieldBar", typeof(RectTransform), typeof(Image));
        shieldBar = (RectTransform)go.transform;
        shieldBar.SetParent(parent, false);
        shieldBar.anchorMin = new Vector2(0f, 1f);
        shieldBar.anchorMax = new Vector2(1f, 1f);
        shieldBar.pivot = new Vector2(0f, 0f);
        shieldBar.anchoredPosition = new Vector2(0f, 2f);
        shieldBar.sizeDelta = new Vector2(0f, shieldBarHeight);
        var bg = go.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.35f);
        bg.raycastTarget = false;

        _segRegen = MakeSeg("Regen", shieldRegenColor);
        _segSoul = MakeSeg("Soul", shieldSoulColor);
        _segTemp = MakeSeg("Temp", shieldTempColor);
        return true;
    }

    RectTransform MakeSeg(string n, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform), typeof(Image));
        var r = (RectTransform)go.transform;
        r.SetParent(shieldBar, false);
        r.offsetMin = r.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return r;
    }

    /// <summary>프리팹에 직접 만든 쉴드 칸 — 자식 이름 Regen · Soul · Temp (없으면 만듦)</summary>
    void FindSegments()
    {
        _segRegen = shieldBar.Find("Regen") as RectTransform ?? MakeSeg("Regen", shieldRegenColor);
        _segSoul = shieldBar.Find("Soul") as RectTransform ?? MakeSeg("Soul", shieldSoulColor);
        _segTemp = shieldBar.Find("Temp") as RectTransform ?? MakeSeg("Temp", shieldTempColor);
    }
}