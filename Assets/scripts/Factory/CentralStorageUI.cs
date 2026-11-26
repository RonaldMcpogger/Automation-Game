using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Management UI for Central Storage.
/// Loads from the CentralStorageUI prefab (Resources/UI/CentralStorageUI) or binds a scene instance,
/// with a runtime fallback if the prefab has not been generated yet.
/// </summary>
public class CentralStorageUI : MonoBehaviour
{
    public const string PrefabResourcePath = "UI/CentralStorageUI";

    public static CentralStorageUI Instance { get; private set; }

    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private Text capacityText;
    [SerializeField] private Image capacityFillImage;
    [SerializeField] private Button closeButton;

    [Header("Columns")]
    [SerializeField] private Text inventoryHeaderText;
    [SerializeField] private Transform inventoryContent;
    [SerializeField] private Button depositAllButton;
    [SerializeField] private Text storageHeaderText;
    [SerializeField] private Transform storageContent;

    [Header("Templates & Footer")]
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private Text statusText;

    public CentralStorage ActiveStorage { get; private set; }
    public bool IsOpen { get; private set; }
    public string StatusMessage { get; private set; } = string.Empty;

    private Font uiFont;
    private FactoryPlayer player;
    private Inventory boundInventory;
    private bool buttonsWired;

    private static readonly Color BackdropColor = new(0.02f, 0.03f, 0.05f, 0.55f);
    private static readonly Color WindowBgColor = new(0.08f, 0.10f, 0.14f, 0.97f);
    private static readonly Color PanelBgColor = new(0.11f, 0.14f, 0.19f, 0.95f);
    private static readonly Color RowBgColor = new(0.15f, 0.19f, 0.26f, 0.95f);
    private static readonly Color BorderColor = new(0.24f, 0.45f, 0.68f, 0.85f);
    private static readonly Color AccentBlue = new(0.20f, 0.48f, 0.78f, 1f);
    private static readonly Color AccentGreen = new(0.18f, 0.54f, 0.34f, 1f);
    private static readonly Color AccentAmber = new(0.68f, 0.46f, 0.16f, 1f);
    private static readonly Color CloseRed = new(0.62f, 0.22f, 0.22f, 1f);
    private static readonly Color MutedTextColor = new(0.72f, 0.78f, 0.86f, 1f);

    public static CentralStorageUI EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        var existing = FindAnyObjectByType<CentralStorageUI>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.InitializeIfNeeded();
            return existing;
        }

        var prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab != null)
        {
            var instanceGO = Instantiate(prefab);
            instanceGO.name = prefab.name;
            if (!instanceGO.TryGetComponent<CentralStorageUI>(out var prefabUI))
                prefabUI = instanceGO.AddComponent<CentralStorageUI>();
            prefabUI.InitializeIfNeeded();
            return prefabUI;
        }

        var canvasGO = new GameObject("Central Storage UI");
        var ui = canvasGO.AddComponent<CentralStorageUI>();
        ui.BuildDefaultHierarchy();
        ui.InitializeIfNeeded();
        return ui;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializeIfNeeded();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        UnbindEvents();
        if (Instance == this)
            Instance = null;
    }

    private void OnDisable()
    {
        if (IsOpen)
            Close();
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        if (ActiveStorage == null || !ActiveStorage.isActiveAndEnabled
            || (player != null && !WithinReach(ActiveStorage, player)))
        {
            Close();
            return;
        }

        bool cancelPressed = (InputManager.Instance != null && InputManager.Instance.UICancelPressed)
            || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);

        if (cancelPressed)
            Close();
    }

    public void Open(CentralStorage targetStorage, FactoryPlayer user = null)
    {
        if (targetStorage == null || !targetStorage.isActiveAndEnabled)
            return;

        if (user != null && !WithinReach(targetStorage, user))
            return;

        InitializeIfNeeded();

        // Close competing menus so UIMode ownership stays clean.
        Inventory.Instance?.SetOpen(false);
        FindAnyObjectByType<CraftingTab>(FindObjectsInactive.Include)?.Close();
        MachineInterface.Instance?.Close();
        if (user != null)
        {
            var build = user.GetComponent<BuildController>();
            if (build != null)
            {
                if (build.IsBuildModeActive) build.SetBuildModeActive(false);
                build.CloseBuildTab();
            }
        }

        UnbindEvents();

        ActiveStorage = targetStorage;
        player = user;
        boundInventory = Inventory.Instance;

        ActiveStorage.Changed += Refresh;
        if (boundInventory != null)
            boundInventory.Changed += Refresh;

        StatusMessage = "Select a resource to transfer between your bag and Central Storage.";
        SetVisible(true);
        InputManager.Instance?.EnterUIMode(this);
        Refresh();
    }

    public void Close()
    {
        UnbindEvents();
        ActiveStorage = null;
        player = null;
        SetVisible(false);
        InputManager.Instance?.ExitUIMode(this);
    }

    public void DepositResource(Resource res, int amount)
    {
        if (!IsOpen || ActiveStorage == null || boundInventory == null || res == null || amount <= 0)
            return;

        int moved = ActiveStorage.DepositFromInventory(boundInventory, res, amount);
        if (moved > 0)
        {
            StatusMessage = $"Deposited {moved} {res.DisplayName} into Central Storage.";
        }
        else if (ActiveStorage.TotalStoredCount >= ActiveStorage.totalCapacity)
        {
            StatusMessage = "Central Storage is full.";
        }
        else
        {
            StatusMessage = $"No {res.DisplayName} available in your bag.";
        }
        Refresh();
    }

    public void WithdrawResource(Resource res, int amount)
    {
        if (!IsOpen || ActiveStorage == null || boundInventory == null || res == null || amount <= 0)
            return;

        int moved = ActiveStorage.WithdrawToInventory(boundInventory, res, amount);
        if (moved > 0)
        {
            StatusMessage = $"Withdrew {moved} {res.DisplayName} to your bag.";
        }
        else if (boundInventory.GetAvailableRoom(res) <= 0)
        {
            StatusMessage = "Your bag is full.";
        }
        else
        {
            StatusMessage = $"No {res.DisplayName} in Central Storage.";
        }
        Refresh();
    }

    public void DepositAll()
    {
        if (!IsOpen || ActiveStorage == null || boundInventory == null)
            return;

        int moved = ActiveStorage.DepositAllFromInventory(boundInventory);
        if (moved > 0)
            StatusMessage = $"Deposited {moved} items into Central Storage.";
        else if (boundInventory.TotalCount == 0)
            StatusMessage = "Your bag is already empty.";
        else
            StatusMessage = "Central Storage is full.";

        Refresh();
    }

    public void Refresh()
    {
        if (ActiveStorage == null)
            return;

        int stored = ActiveStorage.TotalStoredCount;
        int cap = Mathf.Max(1, ActiveStorage.totalCapacity);
        if (capacityText != null)
            capacityText.text = $"Capacity: {stored} / {cap}";
        if (capacityFillImage != null)
            capacityFillImage.fillAmount = Mathf.Clamp01((float)stored / cap);

        if (inventoryHeaderText != null)
        {
            int bagCount = boundInventory != null ? boundInventory.TotalCount : 0;
            int bagCap = boundInventory != null ? boundInventory.itemCapacity : 0;
            inventoryHeaderText.text = $"PLAYER BAG ({bagCount} / {bagCap})";
        }

        if (storageHeaderText != null)
            storageHeaderText.text = $"CENTRAL STORAGE ({stored} / {cap})";

        if (depositAllButton != null)
            depositAllButton.interactable = boundInventory != null && boundInventory.TotalCount > 0 && stored < cap;

        if (statusText != null)
            statusText.text = StatusMessage;

        RebuildInventoryRows();
        RebuildStorageRows();
    }

    private void InitializeIfNeeded()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (inventoryContent == null || storageContent == null || rowTemplate == null)
            BuildDefaultHierarchy();

        if (rowTemplate != null && rowTemplate.activeSelf)
            rowTemplate.SetActive(false);

        if (uiFont == null)
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        if (!buttonsWired)
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (depositAllButton != null)
                depositAllButton.onClick.AddListener(DepositAll);
            buttonsWired = true;
        }
    }

    private void RebuildInventoryRows()
    {
        ClearChildren(inventoryContent);
        if (inventoryContent == null)
            return;

        if (boundInventory == null)
        {
            CreateEmptyRow(inventoryContent, "Player bag unavailable.");
            return;
        }

        var items = SortEntries(boundInventory.GetAllItemCounts());
        if (items.Count == 0)
        {
            CreateEmptyRow(inventoryContent, "Your bag is empty.");
            return;
        }

        int storageFree = ActiveStorage != null ? Mathf.Max(0, ActiveStorage.totalCapacity - ActiveStorage.TotalStoredCount) : 0;

        foreach (var kvp in items)
        {
            Resource res = kvp.Key;
            int count = kvp.Value;
            int stackSize = Mathf.Max(1, res.maxStackSize);
            bool canStore = storageFree > 0 && count > 0;

            CreateResourceRow(
                inventoryContent,
                res,
                count,
                new[]
                {
                    ("+1", AccentGreen, canStore, (Action)(() => DepositResource(res, 1))),
                    ($"+{Mathf.Min(stackSize, count)}", AccentBlue, canStore, (Action)(() => DepositResource(res, stackSize))),
                    ("All", AccentAmber, canStore, (Action)(() => DepositResource(res, count)))
                });
        }
    }

    private void RebuildStorageRows()
    {
        ClearChildren(storageContent);
        if (storageContent == null || ActiveStorage == null)
            return;

        var items = SortEntries(ActiveStorage.GetAllStored());
        if (items.Count == 0)
        {
            CreateEmptyRow(storageContent, "Central Storage is empty.\nDeliver items via Conveyors, Chutes, or deposit from your bag.");
            return;
        }

        foreach (var kvp in items)
        {
            Resource res = kvp.Key;
            int count = kvp.Value;
            int stackSize = Mathf.Max(1, res.maxStackSize);
            int bagRoom = boundInventory != null ? boundInventory.GetAvailableRoom(res) : 0;
            bool canTake = bagRoom > 0 && count > 0;

            CreateResourceRow(
                storageContent,
                res,
                count,
                new[]
                {
                    ("Take 1", AccentGreen, canTake, (Action)(() => WithdrawResource(res, 1))),
                    ($"Take {Mathf.Min(stackSize, count)}", AccentBlue, canTake, (Action)(() => WithdrawResource(res, stackSize))),
                    ("Fill Bag", AccentAmber, canTake, (Action)(() => WithdrawResource(res, bagRoom)))
                });
        }
    }

    private static List<KeyValuePair<Resource, int>> SortEntries(IReadOnlyDictionary<Resource, int> source)
    {
        var list = new List<KeyValuePair<Resource, int>>();
        if (source == null) return list;

        foreach (var kvp in source)
        {
            if (kvp.Key != null && kvp.Value > 0)
                list.Add(kvp);
        }

        list.Sort((a, b) => string.Compare(a.Key.DisplayName, b.Key.DisplayName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static bool WithinReach(CentralStorage storage, FactoryPlayer user)
    {
        if (storage == null || user == null)
            return false;

        var col = storage.GetComponentInChildren<Collider>();
        Vector3 origin = user.viewCamera != null ? user.viewCamera.transform.position : user.transform.position;
        Vector3 target = col != null ? col.ClosestPoint(origin) : storage.transform.position;
        return Vector3.Distance(origin, target) <= user.interactionRange;
    }

    private void UnbindEvents()
    {
        if (ActiveStorage != null)
            ActiveStorage.Changed -= Refresh;
        if (boundInventory != null)
            boundInventory.Changed -= Refresh;
        boundInventory = null;
    }

    private void SetVisible(bool visible)
    {
        IsOpen = visible;
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private void CreateResourceRow(
        Transform parent,
        Resource res,
        int count,
        (string label, Color color, bool interactable, Action onClick)[] actions)
    {
        if (rowTemplate == null) return;

        var row = Instantiate(rowTemplate, parent);
        row.name = $"Row_{res.DisplayName}";
        row.SetActive(true);

        var icon = row.transform.Find("Icon")?.GetComponent<Image>();
        if (icon != null)
        {
            bool hasIcon = res.ItemImage != null;
            icon.sprite = res.ItemImage;
            icon.gameObject.SetActive(hasIcon);
        }

        var nameText = row.transform.Find("Name")?.GetComponent<Text>();
        if (nameText != null)
            nameText.text = res.DisplayName;

        var countText = row.transform.Find("Count")?.GetComponent<Text>();
        if (countText != null)
            countText.text = $"x{count}";

        for (int i = 0; i < 3; i++)
        {
            var btnTransform = row.transform.Find($"Action{i + 1}");
            if (btnTransform == null) continue;

            bool hasAction = i < actions.Length;
            btnTransform.gameObject.SetActive(hasAction);
            if (!hasAction) continue;

            var act = actions[i];
            var btn = btnTransform.GetComponent<Button>();
            var img = btnTransform.GetComponent<Image>();
            var lbl = btnTransform.GetComponentInChildren<Text>(true);

            if (img != null) img.color = act.color;
            if (lbl != null) lbl.text = act.label;
            if (btn != null)
            {
                btn.interactable = act.interactable;
                btn.onClick.RemoveAllListeners();
                if (act.onClick != null)
                    btn.onClick.AddListener(() => act.onClick());
            }
        }
    }

    private void CreateEmptyRow(Transform parent, string message)
    {
        var row = CreateUIObject("EmptyRow", parent, typeof(LayoutElement));
        row.GetComponent<LayoutElement>().preferredHeight = 64f;
        var text = CreateText("Message", row.transform, message, 14, FontStyle.Italic, TextAnchor.MiddleCenter, MutedTextColor);
        StretchFull(text.GetComponent<RectTransform>());
    }

    /// <summary>
    /// Populates the default UI hierarchy and binds all serialized fields.
    /// Used both by the Editor prefab generator and as a runtime fallback if the prefab is missing.
    /// </summary>
    public void BuildDefaultHierarchy()
    {
        if (!TryGetComponent<Canvas>(out var canvas))
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55;

        if (!TryGetComponent<CanvasScaler>(out var scaler))
            scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (!TryGetComponent<GraphicRaycaster>(out _))
            gameObject.AddComponent<GraphicRaycaster>();

        if (!TryGetComponent<CanvasGroup>(out canvasGroup))
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Clear any partial children before rebuilding
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        // Fullscreen dim backdrop
        var backdrop = CreateUIObject("Backdrop", transform, typeof(Image));
        StretchFull(backdrop.GetComponent<RectTransform>());
        backdrop.GetComponent<Image>().color = BackdropColor;

        // Centered window
        var window = CreateUIObject("Window", backdrop.transform, typeof(Image), typeof(Outline));
        var winRT = window.GetComponent<RectTransform>();
        winRT.anchorMin = winRT.anchorMax = new Vector2(0.5f, 0.5f);
        winRT.pivot = new Vector2(0.5f, 0.5f);
        winRT.sizeDelta = new Vector2(1020f, 650f);
        window.GetComponent<Image>().color = WindowBgColor;
        var winOutline = window.GetComponent<Outline>();
        winOutline.effectColor = BorderColor;
        winOutline.effectDistance = new Vector2(2f, -2f);

        // Header bar
        var header = CreateUIObject("Header", window.transform, typeof(Image));
        var headerRT = header.GetComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0f, 1f);
        headerRT.anchorMax = new Vector2(1f, 1f);
        headerRT.pivot = new Vector2(0.5f, 1f);
        headerRT.anchoredPosition = Vector2.zero;
        headerRT.sizeDelta = new Vector2(0f, 70f);
        header.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.98f);

        var title = CreateText("Title", header.transform, "CENTRAL STORAGE", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 0f);
        titleRT.anchorMax = new Vector2(0.45f, 1f);
        titleRT.offsetMin = new Vector2(24f, 0f);
        titleRT.offsetMax = Vector2.zero;

        // Capacity label + bar
        capacityText = CreateText("CapacityText", header.transform, "Capacity: 0 / 9999", 15, FontStyle.Bold, TextAnchor.MiddleRight, MutedTextColor);
        var capRT = capacityText.GetComponent<RectTransform>();
        capRT.anchorMin = new Vector2(0.45f, 0.45f);
        capRT.anchorMax = new Vector2(0.84f, 0.92f);
        capRT.offsetMin = Vector2.zero;
        capRT.offsetMax = new Vector2(-12f, 0f);

        var barBg = CreateUIObject("CapacityBarBg", header.transform, typeof(Image));
        var barBgRT = barBg.GetComponent<RectTransform>();
        barBgRT.anchorMin = new Vector2(0.50f, 0.18f);
        barBgRT.anchorMax = new Vector2(0.84f, 0.36f);
        barBgRT.offsetMin = Vector2.zero;
        barBgRT.offsetMax = new Vector2(-12f, 0f);
        barBg.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 1f);

        var barFill = CreateUIObject("CapacityBarFill", barBg.transform, typeof(Image));
        StretchFull(barFill.GetComponent<RectTransform>());
        capacityFillImage = barFill.GetComponent<Image>();
        capacityFillImage.color = AccentBlue;
        capacityFillImage.type = Image.Type.Filled;
        capacityFillImage.fillMethod = Image.FillMethod.Horizontal;
        capacityFillImage.fillAmount = 0f;
        capacityFillImage.raycastTarget = false;

        closeButton = CreateButton("CloseButton", header.transform, "CLOSE [ESC]", CloseRed, 14);
        var closeRT = closeButton.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(0.86f, 0.20f);
        closeRT.anchorMax = new Vector2(0.98f, 0.80f);
        closeRT.offsetMin = Vector2.zero;
        closeRT.offsetMax = Vector2.zero;

        // Body columns container
        var body = CreateUIObject("Body", window.transform);
        var bodyRT = body.GetComponent<RectTransform>();
        bodyRT.anchorMin = Vector2.zero;
        bodyRT.anchorMax = Vector2.one;
        bodyRT.offsetMin = new Vector2(18f, 54f);
        bodyRT.offsetMax = new Vector2(-18f, -82f);

        // Left Column: Player Bag
        CreateColumn(body.transform, "InventoryColumn", new Vector2(0f, 0f), new Vector2(0.49f, 1f),
            "PLAYER BAG", out inventoryHeaderText, out inventoryContent, includeDepositAll: true);

        // Right Column: Central Storage
        CreateColumn(body.transform, "StorageColumn", new Vector2(0.51f, 0f), new Vector2(1f, 1f),
            "CENTRAL STORAGE", out storageHeaderText, out storageContent, includeDepositAll: false);

        // Footer status bar
        var footer = CreateUIObject("Footer", window.transform, typeof(Image));
        var footerRT = footer.GetComponent<RectTransform>();
        footerRT.anchorMin = new Vector2(0f, 0f);
        footerRT.anchorMax = new Vector2(1f, 0f);
        footerRT.pivot = new Vector2(0.5f, 0f);
        footerRT.sizeDelta = new Vector2(0f, 44f);
        footer.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.98f);

        statusText = CreateText("StatusText", footer.transform, string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, MutedTextColor);
        var statusRT = statusText.GetComponent<RectTransform>();
        StretchFull(statusRT);
        statusRT.offsetMin = new Vector2(20f, 0f);
        statusRT.offsetMax = new Vector2(-20f, 0f);

        // Row template (stored inactive inside window so it can be edited visually in the prefab)
        rowTemplate = BuildRowTemplate(window.transform);
        rowTemplate.SetActive(false);
    }

    private GameObject BuildRowTemplate(Transform parent)
    {
        var row = CreateUIObject("RowTemplate", parent, typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.GetComponent<Image>().color = RowBgColor;

        var le = row.GetComponent<LayoutElement>();
        le.preferredHeight = 46f;

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.padding = new RectOffset(10, 10, 6, 6);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var iconGO = CreateUIObject("Icon", row.transform, typeof(Image), typeof(LayoutElement));
        var iconImg = iconGO.GetComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        var iconLE = iconGO.GetComponent<LayoutElement>();
        iconLE.preferredWidth = 28f;
        iconLE.preferredHeight = 28f;

        var nameText = CreateText("Name", row.transform, "Resource Name", 14, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        var nameLE = nameText.gameObject.AddComponent<LayoutElement>();
        nameLE.flexibleWidth = 1f;

        var countText = CreateText("Count", row.transform, "x0", 14, FontStyle.Bold, TextAnchor.MiddleRight, new Color(0.95f, 0.85f, 0.45f, 1f));
        var countLE = countText.gameObject.AddComponent<LayoutElement>();
        countLE.preferredWidth = 64f;

        Color[] defaultColors = { AccentGreen, AccentBlue, AccentAmber };
        for (int i = 0; i < 3; i++)
        {
            var btn = CreateButton($"Action{i + 1}", row.transform, $"Act {i + 1}", defaultColors[i], 12);
            var btnLE = btn.gameObject.AddComponent<LayoutElement>();
            btnLE.preferredWidth = 68f;
            btnLE.preferredHeight = 30f;
        }

        return row;
    }

    private void CreateColumn(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        string initialTitle, out Text headerLabel, out Transform contentTransform, bool includeDepositAll)
    {
        var col = CreateUIObject(name, parent, typeof(Image), typeof(Outline));
        var colRT = col.GetComponent<RectTransform>();
        colRT.anchorMin = anchorMin;
        colRT.anchorMax = anchorMax;
        colRT.offsetMin = Vector2.zero;
        colRT.offsetMax = Vector2.zero;
        col.GetComponent<Image>().color = PanelBgColor;
        var colOutline = col.GetComponent<Outline>();
        colOutline.effectColor = new Color(0.2f, 0.3f, 0.45f, 0.6f);
        colOutline.effectDistance = new Vector2(1f, -1f);

        headerLabel = CreateText("ColumnHeader", col.transform, initialTitle, 17, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        var hdrRT = headerLabel.GetComponent<RectTransform>();
        hdrRT.anchorMin = new Vector2(0f, 1f);
        hdrRT.anchorMax = new Vector2(1f, 1f);
        hdrRT.pivot = new Vector2(0.5f, 1f);
        hdrRT.anchoredPosition = Vector2.zero;
        hdrRT.sizeDelta = new Vector2(0f, 40f);
        hdrRT.offsetMin = new Vector2(14f, hdrRT.offsetMin.y);
        hdrRT.offsetMax = new Vector2(-14f, hdrRT.offsetMax.y);

        float bottomOffset = includeDepositAll ? 50f : 10f;

        var scrollGO = CreateUIObject("ScrollView", col.transform, typeof(ScrollRect), typeof(RectMask2D));
        var scrollRT = scrollGO.GetComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero;
        scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = new Vector2(10f, bottomOffset);
        scrollRT.offsetMax = new Vector2(-10f, -44f);

        var contentGO = CreateUIObject("Content", scrollGO.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = Vector2.zero;

        var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(2, 2, 2, 2);

        var fitter = contentGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGO.GetComponent<ScrollRect>();
        scroll.content = contentRT;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 28f;

        if (includeDepositAll)
        {
            depositAllButton = CreateButton("DepositAllButton", col.transform, "DEPOSIT ALL TO STORAGE", AccentBlue, 14);
            var btnRT = depositAllButton.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0f, 0f);
            btnRT.anchorMax = new Vector2(1f, 0f);
            btnRT.pivot = new Vector2(0.5f, 0f);
            btnRT.anchoredPosition = new Vector2(0f, 8f);
            btnRT.sizeDelta = new Vector2(-20f, 36f);
        }

        contentTransform = contentGO.transform;
    }

    private Button CreateButton(string name, Transform parent, string label, Color bgColor, int fontSize)
    {
        var go = CreateUIObject(name, parent, typeof(Image), typeof(Button));
        var img = go.GetComponent<Image>();
        img.color = bgColor;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        var txt = CreateText("Label", go.transform, label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        StretchFull(txt.GetComponent<RectTransform>());
        return btn;
    }

    private Text CreateText(string name, Transform parent, string content, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        var go = CreateUIObject(name, parent, typeof(Text));
        var txt = go.GetComponent<Text>();
        txt.font = uiFont;
        txt.text = content;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = alignment;
        txt.color = color;
        txt.raycastTarget = false;
        return txt;
    }

    private static GameObject CreateUIObject(string name, Transform parent, params Type[] components)
    {
        var allTypes = new Type[components.Length + 1];
        allTypes[0] = typeof(RectTransform);
        Array.Copy(components, 0, allTypes, 1, components.Length);
        var go = new GameObject(name, allTypes);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }
    }
}
