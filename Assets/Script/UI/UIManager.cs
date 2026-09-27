using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("카메라")]
    [SerializeField] private Camera bootCamera;
    [SerializeField] private Camera uiCamera;

    [Header("HUD")]
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private InspectorBar inspectorBar;
    [SerializeField] private AmmoDisplay ammoDisplay;
    [SerializeField] private InteractPrompt interactPrompt;
    [SerializeField] private SkillBar skillBar;
    [SerializeField] private ChargeGauge chargeGauge;

    [Header("패널 (없으면 비워둬도 됨)")]
    [SerializeField] private AbilityPanel abilityPanel;
    [SerializeField] private UIPanel shopPanel;
    [SerializeField] private UIPanel inventoryPanel;

    [Header("툴팁")]
    [SerializeField] private TooltipView tooltip;
    public TooltipView Tooltip => tooltip;


    private readonly List<UIPanel> _panels = new();
    private Camera _currentBase;

    /// <summary>패널이 하나라도 열려 있으면 true — 입력 차단용</summary>
    public bool AnyPanelOpen
    {
        get
        {
            foreach (var p in _panels)
                if (p != null && p.IsOpen) return true;
            return false;
        }
    }

    // ══════════════════ 생명주기 ══════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (abilityPanel != null) _panels.Add(abilityPanel);
        if (shopPanel != null) _panels.Add(shopPanel);
        if (inventoryPanel != null) _panels.Add(inventoryPanel);

        AttachUICamera(bootCamera);
        if (bootCamera != null) bootCamera.enabled = true;

        if (hudRoot != null) hudRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ══════════════════ 패널 ══════════════════

    public void ToggleAbility() => TogglePanel(abilityPanel);
    public void ToggleShop() => TogglePanel(shopPanel);
    public void ToggleInventory() => TogglePanel(inventoryPanel);

    public void TogglePanel(UIPanel target)
    {
        if (target == null) return;       // 아직 안 만든 패널은 무시

        bool wasOpen = target.IsOpen;
        CloseAllInternal();

        if (!wasOpen) target.Open();
        UpdateCursor();
    }

    public void CloseAll()
    {
        CloseAllInternal();
        UpdateCursor();
    }

    void CloseAllInternal()
    {
        foreach (var p in _panels) p?.Close();
        tooltip?.Hide();
    }

    void UpdateCursor()
    {
        bool open = AnyPanelOpen;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    // ══════════════════ 카메라 ══════════════════

    void AttachUICamera(Camera baseCam)
    {
        if (uiCamera == null || baseCam == null) return;

        if (_currentBase != null && _currentBase != baseCam)
            _currentBase.GetUniversalAdditionalCameraData().cameraStack.Remove(uiCamera);

        var data = baseCam.GetUniversalAdditionalCameraData();
        if (!data.cameraStack.Contains(uiCamera))
            data.cameraStack.Add(uiCamera);

        _currentBase = baseCam;
    }

    // ══════════════════ 플레이어 ══════════════════

    public void BindLocalPlayer(NetworkObject player, Camera playerCam)
    {
        if (playerCam == null)
        {
            Debug.LogWarning("[UI] 플레이어 카메라가 없습니다");
            return;
        }

        AttachUICamera(playerCam);
        if (bootCamera != null) bootCamera.enabled = false;
        if (hudRoot != null) hudRoot.SetActive(true);

        var hp = player.GetComponent<PlayerHealth>();
        var stats = player.GetComponent<PlayerStats>();
        inspectorBar?.Bind(hp, stats, $"Player {player.InputAuthority.PlayerId}");
        ammoDisplay?.Bind(player.GetComponent<PlayerEquipment>());
        interactPrompt?.Bind(player.GetComponent<PlayerInteract>());

        var ability = player.GetComponent<AbilityRuntime>();
        skillBar?.Bind(ability, hp);
        chargeGauge?.Bind(ability);
        abilityPanel?.Bind(ability);

    }

    public void UnbindLocalPlayer()
    {
        CloseAll();
        AttachUICamera(bootCamera);
        if (bootCamera != null) bootCamera.enabled = true;
        if (hudRoot != null) hudRoot.SetActive(false);
    }
}