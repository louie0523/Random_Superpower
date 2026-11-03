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
    [SerializeField] private ShopPanel shopPanel;
    [SerializeField] private InventoryPanel inventoryPanel;

    [Header("공용 창")]
    [SerializeField] private DetailWindow detailWindow;

    [Header("상태이상")]
    [SerializeField] private StatusScreenEffects statusEffects;
    [SerializeField] private StatusIconBar statusIcons;


    public DetailWindow Detail => detailWindow;

    [Header("툴팁")]
    [SerializeField] private TooltipView tooltip;
    public TooltipView Tooltip => tooltip;

    // 단축키(능력 창 · 인벤 · 상점)는 설정 > 조작에서 — KeyBinds

    private PlayerShop _shop;

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


    /// <summary>패널이 열려 있어 게임 입력을 막아야 하는가</summary>
    public static bool InputBlocked
    {
        get
        {
            var ui = Instance;
            return ui != null && ui.AnyPanelOpen || DevConsole.IsOpen || ClassPickPanel.IsShowing
                   || CommsHud.ChatOpen || PlayerPickPanel.IsShowing || VoiceSettingsPanel.IsOpen
                   || EscMenu.IsOpen || SettingsWindow.IsOpen || BlackMarketWindow.IsOpen;
        }
    }


    // ══════════════════ 생명주기 ══════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // v2 화면 — 씬 UI 캔버스 · UI 카메라를 알려 주고 (UiCanvasFollow) 새 HUD · 창으로 연결을 바꿈
        var sceneCanvas = hudRoot != null ? hudRoot.GetComponentInParent<Canvas>(true) : GetComponentInParent<Canvas>(true);
        if (sceneCanvas != null) UiCanvasFollow.Reference = sceneCanvas.rootCanvas;
        if (uiCamera != null) UiCanvasFollow.UiCamera = uiCamera;
        UseHudV2();
        UsePanelsV2();

        if (abilityPanel != null) _panels.Add(abilityPanel);
        if (shopPanel != null) _panels.Add(shopPanel);
        if (inventoryPanel != null) _panels.Add(inventoryPanel);

        AttachUICamera(bootCamera);
        if (bootCamera != null) bootCamera.enabled = true;

        ShowHud(false);
    }

    /// <summary>예전 HUD 묶음 + v2 HUD 같이 켜고 끔</summary>
    void ShowHud(bool on)
    {
        if (hudRoot != null) hudRoot.SetActive(on);
        if (_hudV2 != null)
        {
            _hudV2.SetActive(on);
            if (on && _hudV2.TryGetComponent<UiCanvasFollow>(out var f)) f.Apply();     // 카메라가 바뀌었을 수 있음
        }
    }

    GameObject _hudV2;
    GameObject _panelsV2;

    /// <summary>
    /// 새 화면은 씬 맨 위(루트)에 따로 띄움 — 씬 캔버스 안에 넣으면 스크린 카메라 모드에서 크기 · 레이어가 어긋남.
    /// UIManager가 DontDestroyOnLoad면 같이
    /// </summary>
    GameObject Spawn(GameObject prefab, bool matchCamera)
    {
        var inst = Instantiate(prefab);
        inst.name = prefab.name;
        if (gameObject.scene.buildIndex == -1) DontDestroyOnLoad(inst);
        if (inst.GetComponent<Canvas>() != null && inst.GetComponent<UiCanvasFollow>() == null)
            inst.AddComponent<UiCanvasFollow>().matchCamera = matchCamera;     // 예전에 만든 프리팹이어도
        return inst;
    }

    /// <summary>씬에 직접 놓아 둔 v2 화면 (프리팹을 끌어다 놓은 것) — 있으면 새로 안 띄우고 이걸 씀</summary>
    static GameObject FindPlaced<T>() where T : Component
    {
        foreach (var f in FindObjectsByType<UiCanvasFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (f.GetComponentInChildren<T>(true) != null) return f.gameObject;
        return null;
    }

    /// <summary>씬에 놓인 것 → 그것, 없으면 UiPrefabSet 프리팹 (Use 켜져 있을 때) 띄우기</summary>
    GameObject Acquire<T>(GameObject prefab, bool matchCamera, string label) where T : Component
    {
        var placed = FindPlaced<T>();
        if (placed != null)
        {
            if (!placed.activeSelf) placed.SetActive(true);
            Debug.Log($"[UI] {label}: 씬에 놓인 '{placed.name}' 사용 (새로 안 띄움)", placed);
            return placed;
        }
        if (prefab == null) return null;
        var inst = Spawn(prefab, matchCamera);
        Debug.Log($"[UI] {label}: '{prefab.name}' 띄움", inst);
        return inst;
    }

    // 남겨 둘 씬 조각 — 이걸 품은 오브젝트는 통째로 끄지 않음
    List<Component> Keep() => new() { interactPrompt, chargeGauge, statusIcons, statusEffects, tooltip, detailWindow, abilityPanel, shopPanel, inventoryPanel, inspectorBar, ammoDisplay, skillBar };

    /// <summary>
    /// v2 HUD — 씬에 놓여 있거나 UiPrefabSet의 'Use Hud'가 켜져 있으면 그걸로 연결을 바꿈.
    /// 씬의 예전 HUD(같은 역할)는 숨김 — 다른 HUD 조각을 품고 있는 오브젝트면 컴포넌트만 끔
    /// </summary>
    void UseHudV2()
    {
        var inst = Acquire<SkillBar>(UiPrefabSet.HudPrefab, true, "HUD v2");
        if (inst == null) return;
        _hudV2 = inst;

        var newBar = inst.GetComponentInChildren<InspectorBar>(true);
        var newAmmo = inst.GetComponentInChildren<AmmoDisplay>(true);
        var newSkill = inst.GetComponentInChildren<SkillBar>(true);
        var newPrompt = inst.GetComponentInChildren<InteractPrompt>(true);
        var newCharge = inst.GetComponentInChildren<ChargeGauge>(true);
        var newIcons = inst.GetComponentInChildren<StatusIconBar>(true);
        var newFx = inst.GetComponentInChildren<StatusScreenEffects>(true);
        var keep = Keep();
        keep.Remove(inspectorBar); keep.Remove(ammoDisplay); keep.Remove(skillBar);
        keep.Remove(interactPrompt); keep.Remove(chargeGauge); keep.Remove(statusIcons); keep.Remove(statusEffects);

        inspectorBar = Swap(inspectorBar, newBar, keep, inst);
        ammoDisplay = Swap(ammoDisplay, newAmmo, keep, inst);
        skillBar = Swap(skillBar, newSkill, keep, inst);
        interactPrompt = Swap(interactPrompt, newPrompt, keep, inst);
        chargeGauge = Swap(chargeGauge, newCharge, keep, inst);
        statusIcons = Swap(statusIcons, newIcons, keep, inst);
        statusEffects = Swap(statusEffects, newFx, keep, inst);

        Debug.Log($"[UI] HUD v2 연결 — 체력 {Has(newBar)} · 스킬 {Has(newSkill)} · 탄약 {Has(newAmmo)} · 상호작용 {Has(newPrompt)} · 게이지 {Has(newCharge)} · 상태이상 {Has(newIcons)}/{Has(newFx)}", inst);

        // 씬에 따로 있는 목표 · 랜덤 능력 알림 — 새 HUD 것만 남김
        foreach (var r in FindObjectsByType<RoundStatusView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!r.transform.IsChildOf(inst.transform)) Hide(r, keep);
        foreach (var r in FindObjectsByType<RandomAbilityHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!r.transform.IsChildOf(inst.transform)) Hide(r, keep);
    }

    /// <summary>
    /// v2 창 (능력 · 상세 · 상점 · 인벤토리 · 툴팁 · 순위표) — 씬에 놓여 있거나 'Use Panels'가 켜져 있으면 연결을 바꿈
    /// </summary>
    void UsePanelsV2()
    {
        var inst = Acquire<AbilityPanel>(UiPrefabSet.PanelsPrefab, false, "창 v2");
        if (inst == null) return;
        _panelsV2 = inst;

        var newAbility = inst.GetComponentInChildren<AbilityPanel>(true);
        var newShop = inst.GetComponentInChildren<ShopPanel>(true);
        var newInv = inst.GetComponentInChildren<InventoryPanel>(true);
        var newDetail = inst.GetComponentInChildren<DetailWindow>(true);
        var newTip = inst.GetComponentInChildren<TooltipView>(true);
        var keep = Keep();
        keep.Remove(abilityPanel); keep.Remove(shopPanel); keep.Remove(inventoryPanel); keep.Remove(detailWindow); keep.Remove(tooltip);

        abilityPanel = Swap(abilityPanel, newAbility, keep, inst);
        shopPanel = Swap(shopPanel, newShop, keep, inst);
        inventoryPanel = Swap(inventoryPanel, newInv, keep, inst);
        detailWindow = Swap(detailWindow, newDetail, keep, inst);
        tooltip = Swap(tooltip, newTip, keep, inst);

        foreach (var r in FindObjectsByType<ScoreboardView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!r.transform.IsChildOf(inst.transform)) Hide(r, keep);

        Debug.Log($"[UI] 창 v2 연결 — 능력 {Has(newAbility)} · 상점 {Has(newShop)} · 인벤 {Has(newInv)} · 상세 {Has(newDetail)} · 툴팁 {Has(newTip)}", inst);
    }

    static string Has(UnityEngine.Object o) => o != null ? "O" : "X";

    static T Swap<T>(T old, T now, List<Component> keep, GameObject newRoot) where T : Component
    {
        if (now == null) return old;
        if (old != null && old != now && !old.transform.IsChildOf(newRoot.transform)) Hide(old, keep);
        return now;
    }

    static void Hide(Component old, List<Component> keep)
    {
        if (old == null) return;
        foreach (var k in keep)
            if (k != null && k.transform.IsChildOf(old.transform))
            {
                if (old is UnityEngine.Behaviour b) b.enabled = false;      // 다른 조각을 품고 있음 — 컴포넌트만
                Debug.Log($"[UI] 예전 HUD '{old.name}'는 다른 HUD 조각을 품고 있어서 컴포넌트만 껐어 — 남는 그림은 직접 지워 줘", old);
                return;
            }
        old.gameObject.SetActive(false);
    }

    void Update()
    {
        if (DevConsole.IsOpen || ClassPickPanel.IsShowing || CommsHud.ChatOpen || PlayerPickPanel.IsShowing || VoiceSettingsPanel.IsOpen
            || EscMenu.IsOpen || SettingsWindow.IsOpen) return;
        if (KeyBinds.Down(GameAction.AbilityPanel)) ToggleAbility();
        if (KeyBinds.Down(GameAction.Inventory)) ToggleInventory();

        if (KeyBinds.Down(GameAction.Shop) && (_shop == null || _shop.CanOpenShop))
            ToggleShop();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (detailWindow != null && detailWindow.IsOpen) detailWindow.Hide();
            else if (AnyPanelOpen) CloseAll();
        }
    }

    // ══════════════════ 패널 ══════════════════

    public void ToggleAbility() => TogglePanel(abilityPanel);
    public void ToggleShop() => TogglePanel(shopPanel);
    public void ToggleInventory() => TogglePanel(inventoryPanel);

    public void TogglePanel(UIPanel target)
    {
        if (target == null) return;

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
        detailWindow?.Hide();
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
        DamageParticleManager.Instance?.SetCamera(playerCam);
        if (bootCamera != null) bootCamera.enabled = false;
        ShowHud(true);
        HudPortrait.SetSubject(player.transform);     // 왼쪽 아래 얼굴 칸

        var hp = player.GetComponent<PlayerHealth>();
        var stats = player.GetComponent<PlayerStats>();
        inspectorBar?.Bind(hp, stats, TargetUtil.NameOf(player));     // 방 닉네임 (없으면 Player N)
        ammoDisplay?.Bind(player.GetComponent<PlayerEquipment>());
        interactPrompt?.Bind(player.GetComponent<PlayerInteract>());

        var ability = player.GetComponent<AbilityRuntime>();
        skillBar?.Bind(ability, hp);
        chargeGauge?.Bind(ability);
        abilityPanel?.Bind(ability);


        inventoryPanel?.Bind(
        player.GetComponent<PlayerEquipment>(),
        player.GetComponent<PlayerEnchant>());

        _shop = player.GetComponent<PlayerShop>();
        shopPanel?.Bind(
            player.GetComponent<PlayerShop>(),
            hp,
            ability,
            player.GetComponent<PlayerEquipment>(),
            player.GetComponent<PlayerEnchant>());

        var status = player.GetComponent<PlayerStatus>();
        statusEffects?.Bind(status);
        statusIcons?.Bind(status);


    }

    public void UnbindLocalPlayer()
    {
        HudPortrait.SetSubject(null);
        CloseAll();
        AttachUICamera(bootCamera);
        if (bootCamera != null) bootCamera.enabled = true;
        ShowHud(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_hudV2 != null) Destroy(_hudV2);
        if (_panelsV2 != null) Destroy(_panelsV2);
    }
}