using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InteractPrompt : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text keyText;
    [SerializeField] private LocText actionLabel;
    [SerializeField] private LocText tierLabel;
    [SerializeField] private Image progressFill;      // Filled

    [SerializeField] private string keyName = "E";

    private PlayerInteract _interact;
    private LootChest _shown;

    public void Bind(PlayerInteract interact) => _interact = interact;

    void Start()
    {
        if (keyText != null) keyText.text = keyName;
        actionLabel?.SetKey("ui.interact.open");
    }

    void Update()
    {
        var chest = _interact != null ? _interact.CurrentTarget : null;
        bool show = chest != null && !chest.IsOpened;

        if (root != null && root.activeSelf != show) root.SetActive(show);
        if (!show) { _shown = null; return; }

        // 대상이 바뀔 때만 등급 텍스트 갱신
        if (_shown != chest)
        {
            _shown = chest;
            tierLabel?.SetKey($"ui.chest.tier.{chest.Tier}");
        }

        if (progressFill != null) progressFill.fillAmount = chest.Progress;
    }
}