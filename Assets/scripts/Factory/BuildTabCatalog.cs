using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Populates the scene's BuildTab with the project's FactoryData definitions.</summary>
public class BuildTabCatalog : MonoBehaviour
{
    [SerializeField]
    private CanvasGroup canvasGroup;
    [SerializeField]
    private RectTransform content;
    [SerializeField]
    private Action<int> onSelected;
    [SerializeField]
    private Action onBuildRequested;
    [SerializeField]
    private Button buildButton;
    [SerializeField]
    private Image selectedEntry;

    //generatedEntries is used to keep track of the entries created at runtime so they can be destroyed when the catalog is updated.
    private readonly System.Collections.Generic.List<GameObject> generatedEntries = new();
    private readonly System.Collections.Generic.Dictionary<Image, Color> entryNormalColors = new();
    private bool buildButtonConfigured;
    [SerializeField]
    private TextMeshProUGUI FactoryNameDisplay;

    [SerializeField]
    private GameObject DataEntryPrefab;

    private readonly Color hoverColor = new(0.18f, 0.45f, 0.7f, 0.95f);

    
    public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.01f;

    public static BuildTabCatalog FindOrCreate(Action<int> onSelected, Action onBuildRequested)
    {
        BuildTabCatalog catalog = FindAnyObjectByType<BuildTabCatalog>(FindObjectsInactive.Include);
        if (catalog == null)
        {
            RectTransform tab = null;
            foreach (var rect in FindObjectsByType<RectTransform>(FindObjectsInactive.Include))
            {
                if (rect.name == "BuildTab")
                {
                    tab = rect;
                    break;
                }
            }

            if (tab == null)
            {
                Debug.LogWarning("BuildTabCatalog could not find the scene object named BuildTab.");
                return null;
            }

            catalog = tab.gameObject.AddComponent<BuildTabCatalog>();
        }

        catalog.onSelected = onSelected;
        catalog.onBuildRequested = onBuildRequested;
        catalog.Initialize();
        return catalog;
    }

    public void SetDefinitions(FactoryData[] definitions)
    {
        Initialize();
        if (content == null)
            return;

        ClearGeneratedEntries();

        selectedEntry = null;
        entryNormalColors.Clear();
        if (buildButton != null)
            buildButton.interactable = false;
        if (FactoryNameDisplay != null)
            FactoryNameDisplay.text = string.Empty;

        if (definitions == null)
            return;

        for (int i = 0; i < definitions.Length; i++)
        {
            FactoryData definition = definitions[i];
            if (definition == null)
                continue;
            CreateEntry(definition, i);
        }

        if (content.TryGetComponent<GridLayoutGroup>(out var grid))
        {
            int columns = Mathf.Max(1, grid.constraintCount);
            int rows = Mathf.CeilToInt(definitions.Length / (float)columns);
            float height = grid.padding.top + grid.padding.bottom
                + rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }

    public void SetVisible(bool visible)
    {
        // Hiding also happens during scene teardown. Never create UI/input objects on that path.
        if (visible)
            Initialize();
        else
            canvasGroup ??= GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            return;

        if (visible)
            gameObject.SetActive(true);
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private void Initialize()
    {
        EnsureUiInput();
        canvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        if (buildButton != null)
            ConfigureBuildButton();

        if (content != null)
            return;

        var scroll = GetComponentInChildren<ScrollRect>(true);
        content = scroll != null ? scroll.content : null;
        if (content == null)
        {
            var list = new GameObject("Factory List", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            list.transform.SetParent(transform, false);
            content = list.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0.1f, 0.1f);
            content.anchorMax = new Vector2(0.9f, 0.9f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = list.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            var fitter = list.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        if (buildButton == null)
            CreateBuildButton();
        ConfigureBuildButton();
    }

    // The catalog creates UGUI Buttons at runtime, so its canvas must raycast and
    // the scene needs an EventSystem that understands the Input System.
    private void EnsureUiInput()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        EventSystem eventSystem = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (eventSystem == null)
        {
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private void CreateEntry(FactoryData definition, int index)
    {
        GameObject entry;
        if (DataEntryPrefab != null)
        {
            entry = Instantiate(DataEntryPrefab, content);
        }
        else
        {
            // Keep a basic fallback for scenes that have not assigned the template yet.
            entry = new GameObject("Building Selectable", typeof(RectTransform), typeof(Image), typeof(Button));
            entry.transform.SetParent(content, false);
        }

        generatedEntries.Add(entry);
        entry.name = definition.DisplayName;
        entry.SetActive(true);

        var image = entry.GetComponent<Image>();
        var button = entry.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"Build entry template '{DataEntryPrefab.name}' needs a Button component.");
            return;
        }

        if (image != null && button.targetGraphic == null)
            button.targetGraphic = image;
        if (image != null)
            entryNormalColors[image] = image.color;

        button.onClick.AddListener(() => SelectEntry(image, index, definition));

        TextMeshProUGUI label = entry.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            string cost = definition.GetCostText();
            label.text = string.IsNullOrEmpty(cost) || cost == "Free"
                ? definition.DisplayName
                : $"{definition.DisplayName}\n<size=75%><color=#D0D0D0>({cost})</color></size>";
        }
    }

    private void SelectEntry(Image entry, int index, FactoryData definition)
    {
        if (selectedEntry != null && entryNormalColors.TryGetValue(selectedEntry, out Color normalColor))
            selectedEntry.color = normalColor;
        selectedEntry = entry;
        if (selectedEntry != null)
            selectedEntry.color = hoverColor;

        var builder = FindAnyObjectByType<BuildController>();
        bool canAfford = builder != null && builder.CanAfford(definition, out _);

        if (buildButton != null)
        {
            buildButton.interactable = canAfford;
            var btnText = buildButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
            {
                btnText.text = canAfford ? "BUILD" : "LOCKED (NEED RESOURCES)";
            }
        }

        if (FactoryNameDisplay != null)
        {
            string cost = definition != null ? definition.GetCostText() : "Free";
            string costColor = canAfford ? "#77DD77" : "#FF6666";
            int current = Inventory.Instance != null && definition != null && definition.BuildCostResource != null
                ? Inventory.Instance.GetItemCount(definition.BuildCostResource) : 0;
            string countInfo = definition != null && definition.HasBuildCost ? $" (You have: {current})" : "";
            FactoryNameDisplay.text = $"{definition.DisplayName}\n<size=65%><color={costColor}>Cost: {cost}{countInfo}</color></size>";
        }
        onSelected?.Invoke(index);
    }

    private void ClearGeneratedEntries()
    {
        foreach (GameObject entry in generatedEntries)
            if (entry != null)
                Destroy(entry);

        generatedEntries.Clear();
    }

    private void CreateBuildButton()
    {
        var buttonObject = new GameObject("Build", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(transform, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(180f, 42f);
        rect.anchoredPosition = new Vector2(0f, 12f);
        var image = buttonObject.GetComponent<Image>();
        image.color = hoverColor;
        buildButton = buttonObject.GetComponent<Button>();
        buildButton.targetGraphic = image;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.fontSize = 18;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.text = "BUILD";
        label.raycastTarget = false;
    }

    // Scene-authored buttons need the same callback as buttons created at runtime.
    private void ConfigureBuildButton()
    {
        if (buildButton == null || buildButtonConfigured)
            return;

        buildButton.onClick.AddListener(() => onBuildRequested?.Invoke());
        buildButtonConfigured = true;
    }
}
