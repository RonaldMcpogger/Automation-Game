using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A clean vertical sidebar HUD overlay for Build Mode, positioned directly on the side of the screen.
/// Displays available buildings, selection slots, snap feedback, and dynamic keybind instructions.
/// </summary>
public class BuildModeUI : MonoBehaviour
{
    private Text headerText;
    private Text subheaderText;
    private Image[] slotBackgrounds;
    private Text[] slotLabels;
    private Text snapStatusText;
    private Text promptText;
    private GameObject crosshairDot;

    private readonly Color activeSlotColor = new Color(0.18f, 0.45f, 0.7f, 0.95f);
    private readonly Color inactiveSlotColor = new Color(0.12f, 0.16f, 0.2f, 0.7f);
    private readonly Color activeTextColor = Color.white;
    private readonly Color inactiveTextColor = new Color(0.75f, 0.8f, 0.85f);
    private readonly Color snapHighlightColor = new Color(1f, 0.88f, 0.25f); // Bright Gold
    private readonly Color normalStatusColor = new Color(0.6f, 0.8f, 1f);

    private static readonly string[] SlotNames =
    {
        "[1] Conveyor Belt",
        "[2] Smelter",
        "[3] Submission Bin",
        "[4] Resource Spot"
    };

    /// <summary>
    /// Creates and attaches the vertical sidebar BuildModeUI directly to the screen Canvas.
    /// </summary>
    public static BuildModeUI Create(Transform parent, Action<int> onSlotClicked, bool onRightSide = true)
    {
        // Clean up any stale existing BuildModeUI or BuildCrosshair objects in the scene
        foreach (var old in FindObjectsByType<BuildModeUI>())
        {
            if (old != null && old.gameObject != null)
                DestroyImmediate(old.gameObject);
        }

        var staleCrosshair = GameObject.Find("BuildCrosshair");
        if (staleCrosshair != null)
            DestroyImmediate(staleCrosshair);

        Canvas canvas = null;
        if (parent != null)
            canvas = parent.GetComponentInParent<Canvas>();

        if (canvas == null)
            canvas = FindAnyObjectByType<Canvas>();

        if (canvas == null)
        {
            var canvasObj = new GameObject("Build Mode Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Center Aiming Crosshair Dot (Direct child of Canvas)
        var crosshair = new GameObject("BuildCrosshair", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        crosshair.transform.SetParent(canvas.transform, false);
        var crosshairRect = crosshair.GetComponent<RectTransform>();
        crosshairRect.anchorMin = crosshairRect.anchorMax = new Vector2(0.5f, 0.5f);
        crosshairRect.pivot = new Vector2(0.5f, 0.5f);
        crosshairRect.sizeDelta = new Vector2(6f, 6f);
        crosshairRect.anchoredPosition = Vector2.zero;
        crosshairRect.localScale = Vector3.one;
        var dotImage = crosshair.GetComponent<Image>();
        dotImage.color = new Color(1f, 1f, 1f, 0.85f);
        dotImage.raycastTarget = false;

        // Vertical Sidebar Panel directly parented to Canvas
        var panelObj = new GameObject("BuildModeSidebar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(BuildModeUI));
        panelObj.transform.SetParent(canvas.transform, false);
        var panelRect = panelObj.GetComponent<RectTransform>();

        if (onRightSide)
        {
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.sizeDelta = new Vector2(250f, 440f);
            panelRect.anchoredPosition = new Vector2(-24f, 0f);
        }
        else
        {
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 0.5f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.sizeDelta = new Vector2(250f, 440f);
            panelRect.anchoredPosition = new Vector2(24f, 0f);
        }

        panelRect.localScale = Vector3.one;

        var panelImage = panelObj.GetComponent<Image>();
        panelImage.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);

        var panelOutline = panelObj.GetComponent<Outline>();
        panelOutline.effectColor = new Color(0.2f, 0.35f, 0.55f, 0.7f);
        panelOutline.effectDistance = new Vector2(2f, -2f);

        var buildUI = panelObj.GetComponent<BuildModeUI>();
        buildUI.crosshairDot = crosshair;
        buildUI.BuildChildren(font, onSlotClicked);
        buildUI.SetVisible(false);
        return buildUI;
    }

    private void BuildChildren(Font font, Action<int> onSlotClicked)
    {
        // Header Title
        var headerObj = new GameObject("Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
        headerObj.transform.SetParent(transform, false);
        var headerRect = headerObj.GetComponent<RectTransform>();
        headerRect.anchorMin = headerRect.anchorMax = new Vector2(0.5f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(230f, 32f);
        headerRect.anchoredPosition = new Vector2(0f, -12f);
        headerRect.localScale = Vector3.one;

        headerText = headerObj.GetComponent<Text>();
        headerText.font = font;
        headerText.fontSize = 20;
        headerText.fontStyle = FontStyle.Bold;
        headerText.alignment = TextAnchor.MiddleCenter;
        headerText.color = new Color(1f, 0.82f, 0.15f);
        headerText.text = "BUILD MODE";
        headerText.raycastTarget = false;

        // Subheader
        var subheaderObj = new GameObject("Subheader", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        subheaderObj.transform.SetParent(transform, false);
        var subheaderRect = subheaderObj.GetComponent<RectTransform>();
        subheaderRect.anchorMin = subheaderRect.anchorMax = new Vector2(0.5f, 1f);
        subheaderRect.pivot = new Vector2(0.5f, 1f);
        subheaderRect.sizeDelta = new Vector2(230f, 20f);
        subheaderRect.anchoredPosition = new Vector2(0f, -40f);
        subheaderRect.localScale = Vector3.one;

        subheaderText = subheaderObj.GetComponent<Text>();
        subheaderText.font = font;
        subheaderText.fontSize = 12;
        subheaderText.alignment = TextAnchor.MiddleCenter;
        subheaderText.color = new Color(0.65f, 0.7f, 0.75f);
        subheaderText.text = "Select item or press [1-4]";
        subheaderText.raycastTarget = false;

        // Vertical Slots Container
        var slotsContainer = new GameObject("SlotsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
        slotsContainer.transform.SetParent(transform, false);
        var slotsRect = slotsContainer.GetComponent<RectTransform>();
        slotsRect.anchorMin = slotsRect.anchorMax = new Vector2(0.5f, 1f);
        slotsRect.pivot = new Vector2(0.5f, 1f);
        slotsRect.sizeDelta = new Vector2(230f, 180f);
        slotsRect.anchoredPosition = new Vector2(0f, -66f);
        slotsRect.localScale = Vector3.one;

        var layout = slotsContainer.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        slotBackgrounds = new Image[SlotNames.Length];
        slotLabels = new Text[SlotNames.Length];

        for (int i = 0; i < SlotNames.Length; i++)
        {
            int slotIndex = i;
            var slotObj = new GameObject($"Slot_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            slotObj.transform.SetParent(slotsContainer.transform, false);

            var le = slotObj.GetComponent<LayoutElement>();
            le.minHeight = 36f;
            le.preferredHeight = 38f;

            var slotImg = slotObj.GetComponent<Image>();
            slotImg.color = inactiveSlotColor;
            slotBackgrounds[i] = slotImg;

            var btn = slotObj.GetComponent<Button>();
            btn.targetGraphic = slotImg;
            btn.onClick.AddListener(() => onSlotClicked?.Invoke(slotIndex));

            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObj.transform.SetParent(slotObj.transform, false);
            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
            labelRect.localScale = Vector3.one;

            var label = labelObj.GetComponent<Text>();
            label.font = font;
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = inactiveTextColor;
            label.text = SlotNames[i];
            label.raycastTarget = false;
            slotLabels[i] = label;
        }

        // Instructions Box at bottom of the panel
        var infoBox = new GameObject("InfoBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        infoBox.transform.SetParent(transform, false);
        var infoRect = infoBox.GetComponent<RectTransform>();
        infoRect.anchorMin = infoRect.anchorMax = new Vector2(0.5f, 0f);
        infoRect.pivot = new Vector2(0.5f, 0f);
        infoRect.sizeDelta = new Vector2(230f, 172f);
        infoRect.anchoredPosition = new Vector2(0f, 12f);
        infoRect.localScale = Vector3.one;

        var infoImg = infoBox.GetComponent<Image>();
        infoImg.color = new Color(0.03f, 0.05f, 0.07f, 0.8f);

        // Snap Status Text
        var snapObj = new GameObject("SnapStatus", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        snapObj.transform.SetParent(infoBox.transform, false);
        var snapRect = snapObj.GetComponent<RectTransform>();
        snapRect.anchorMin = snapRect.anchorMax = new Vector2(0.5f, 1f);
        snapRect.pivot = new Vector2(0.5f, 1f);
        snapRect.sizeDelta = new Vector2(216f, 28f);
        snapRect.anchoredPosition = new Vector2(0f, -6f);
        snapRect.localScale = Vector3.one;

        snapStatusText = snapObj.GetComponent<Text>();
        snapStatusText.font = font;
        snapStatusText.fontSize = 13;
        snapStatusText.fontStyle = FontStyle.Bold;
        snapStatusText.alignment = TextAnchor.MiddleCenter;
        snapStatusText.color = normalStatusColor;
        snapStatusText.text = "Aim at ground";
        snapStatusText.raycastTarget = false;

        // Prompt & Keybind Instructions
        var promptObj = new GameObject("PromptText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        promptObj.transform.SetParent(infoBox.transform, false);
        var promptRect = promptObj.GetComponent<RectTransform>();
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.sizeDelta = new Vector2(216f, 130f);
        promptRect.anchoredPosition = new Vector2(0f, 6f);
        promptRect.localScale = Vector3.one;

        promptText = promptObj.GetComponent<Text>();
        promptText.font = font;
        promptText.fontSize = 12;
        promptText.lineSpacing = 1.15f;
        promptText.alignment = TextAnchor.UpperLeft;
        promptText.color = new Color(0.85f, 0.9f, 0.95f);
        promptText.raycastTarget = false;
    }

    /// <summary>
    /// Shows or hides the Build Mode UI.
    /// </summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
        if (crosshairDot != null)
            crosshairDot.SetActive(visible);
    }

    private void OnDestroy()
    {
        if (crosshairDot != null)
            Destroy(crosshairDot);
    }

    /// <summary>
    /// Updates slot highlights, snap status, and dynamic instruction text.
    /// </summary>
    public void UpdateDisplay(int selectedIndex, float rotation, bool conveyorHasStart, string snapTargetName = null, float conveyorRadius = 0.5f)
    {
        for (int i = 0; i < slotBackgrounds.Length; i++)
        {
            bool isSelected = i == selectedIndex;
            if (slotBackgrounds[i] != null)
                slotBackgrounds[i].color = isSelected ? activeSlotColor : inactiveSlotColor;
            if (slotLabels[i] != null)
            {
                slotLabels[i].color = isSelected ? activeTextColor : inactiveTextColor;
                slotLabels[i].fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        var im = InputManager.Instance;
        string placeKey = im != null ? im.GetLabel("BuildPlace") ?? "LMB" : "LMB";
        string cancelKey = im != null ? im.GetLabel("BuildCancel") ?? "RMB" : "RMB";
        string rotateKey = im != null ? im.GetLabel("BuildRotate") ?? "R" : "R";
        string widthKey = im != null ? im.GetLabel("BuildWidth") ?? "T" : "T";
        string exitKey = im != null ? im.GetLabel("BuildMode") ?? "B" : "B";
        string widthStr = $"{conveyorRadius * 2f:0.0}m";

        if (snapStatusText != null)
        {
            if (!string.IsNullOrEmpty(snapTargetName))
            {
                snapStatusText.color = snapHighlightColor;
                snapStatusText.text = $"⚡ Snapped: {snapTargetName}";
            }
            else
            {
                snapStatusText.color = normalStatusColor;
                snapStatusText.text = selectedIndex == 0
                    ? (conveyorHasStart ? "Select End Point" : "Select Start Point")
                    : "Placing Building";
            }
        }

        if (promptText != null)
        {
            if (selectedIndex == 0) // Conveyor Belt
            {
                if (!conveyorHasStart)
                {
                    promptText.text =
                        $"[{placeKey}] Set Start Point\n" +
                        $"• Snap: Smelter / Belt\n" +
                        $"[{widthKey}] Width: {widthStr}\n" +
                        $"[{cancelKey}] Cancel\n" +
                        $"[1-4] or Scroll: Switch\n" +
                        $"[{exitKey}] Exit Build Mode";
                }
                else
                {
                    promptText.text =
                        $"[{placeKey}] Set End Point & Build\n" +
                        $"• Snap: Bin / Belt\n" +
                        $"[{widthKey}] Width: {widthStr}\n" +
                        $"[{cancelKey}] Unpin Start\n" +
                        $"[1-4] Switch Building\n" +
                        $"[{exitKey}] Exit Build Mode";
                }
            }
            else // Regular Buildings
            {
                promptText.text =
                    $"[{placeKey}] Place Building\n" +
                    $"[{rotateKey}] Rotate: {(int)rotation}°\n" +
                    $"• Auto-connects to nearby\n" +
                    $"  belts automatically\n" +
                    $"[1-4] or Scroll: Switch\n" +
                    $"[{exitKey}] Exit Build Mode";
            }
        }
    }
}
