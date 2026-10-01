using UnityEngine;
using TMPro;

public class AmmoDisplay : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text currentText;
    [SerializeField] private TMP_Text maxText;

    [Header("색")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color lowColor = new(1f, 0.75f, 0.2f);
    [SerializeField] private Color emptyColor = new(1f, 0.3f, 0.3f);
    [Tooltip("이 비율 이하면 경고색")]
    [SerializeField, Range(0f, 1f)] private float lowRatio = 0.25f;

    private PlayerEquipment _equip;

    public void Bind(PlayerEquipment equip) => _equip = equip;

    void Update()
    {
        var w = _equip != null ? _equip.GetWeapon() : null;

        bool isGun = w != null
                  && w.Class == WeaponClass.Ranged
                  && w.RangedKind == RangedType.Gun;

        if (root != null && root.activeSelf != isGun) root.SetActive(isGun);
        if (!isGun) return;

        int cur = _equip.Ammo;
        int max = Mathf.Max(1, w.MaxAmmo);

        if (currentText != null)
        {
            currentText.text = cur.ToString();
            currentText.color = cur <= 0 ? emptyColor
                              : cur <= max * lowRatio ? lowColor
                              : normalColor;
        }

        if (maxText != null) maxText.text = $"/ {max}";
    }
}