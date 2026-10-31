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

    // 키 글자는 설정의 '상호작용' 키를 따라감 (예전 keyName = "E" 칸은 없어짐)

    private PlayerInteract _interact;
    private LootChest _shown;
    private string _keyShown;

    public void Bind(PlayerInteract interact) => _interact = interact;

    void Start()
    {
        RefreshKey();
        actionLabel?.SetKey("ui.interact.open");
    }

    void Update()
    {
        var chest = _interact != null ? _interact.CurrentTarget : null;
        bool show = chest != null && !chest.IsOpened;

        if (root != null && root.activeSelf != show) root.SetActive(show);
        if (!show) { _shown = null; return; }

        RefreshKey();       // 게임 중에 키를 바꿔도 바로 반영

        // 대상이 바뀔 때만 등급 텍스트 갱신
        if (_shown != chest)
        {
            _shown = chest;
            tierLabel?.SetKey($"ui.chest.tier.{chest.Tier}");
        }

        if (progressFill != null) progressFill.fillAmount = chest.Progress;
    }

    void RefreshKey()
    {
        if (keyText == null) return;
        string k = PlayerInteract.KeyName;
        if (k == _keyShown) return;      // 바뀔 때만 글자 다시 씀
        _keyShown = k;
        keyText.text = k;
    }
}