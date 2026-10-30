using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Populates the scene's BuildTab with the project's FactoryData definitions.</summary>
public class BuildTabCatalog : MonoBehaviour
{
    private CanvasGroup canvasGroup;
    private RectTransform content;
    private Action<int> onSelected;
    private Action onBuildRequested;
    private Button buildButton;
    private Image selectedEntry;
    private readonly Color normalColor = new(0.12f, 0.16f, 0.2f, 0.95f);
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

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        selectedEntry = null;
        if (buildButton != null)
            buildButton.interactable = false;

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
        Initialize();
        if (canvasGroup == null)
            return;

        gameObject.SetActive(true);
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private void Initialize()
    {
        canvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
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
    }

    private void CreateEntry(FactoryData definition, int index)
    {
        var entry = new GameObject(definition.DisplayName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        entry.transform.SetParent(content, false);
        var element = entry.GetComponent<LayoutElement>();
        element.preferredHeight = 72f;

        var image = entry.GetComponent<Image>();
        image.color = normalColor;
        var button = entry.GetComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = hoverColor;
        colors.pressedColor = hoverColor;
        button.colors = colors;
        button.onClick.AddListener(() => SelectEntry(image, index));

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(entry.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 6f);
        labelRect.offsetMax = new Vector2(-16f, -6f);
        var label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = string.IsNullOrWhiteSpace(definition.description)
            ? definition.DisplayName
            : $"{definition.DisplayName}\n<size=12>{definition.description}</size>";
    }

    private void SelectEntry(Image entry, int index)
    {
        if (selectedEntry != null)
            selectedEntry.color = normalColor;
        selectedEntry = entry;
        selectedEntry.color = hoverColor;
        if (buildButton != null)
            buildButton.interactable = true;
        onSelected?.Invoke(index);
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
        buildButton.onClick.AddListener(() => onBuildRequested?.Invoke());

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 18;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "BUILD";
        label.raycastTarget = false;
    }
}
