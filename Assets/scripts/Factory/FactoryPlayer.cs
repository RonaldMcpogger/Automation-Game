using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A resource pouch, station interaction, HUD, and crafting inventory on the FPS player.</summary>
public class FactoryPlayer : MonoBehaviour
{
    public Camera viewCamera;
    public FactoryMachine machine;
    public SubmissionBin submissionBin;
    public Text objectiveText;
    public Text inventoryText;
    public Text machineText;
    public Text promptText;
    public Text resourceBarText;
    [Min(1)] public int oreCapacity = 20;
    [Min(0.5f)] public float interactionRange = 4f;

    public int OreCount { get; private set; }
    public Resource CarriedItem { get; private set; }

    private readonly Dictionary<Resource, int> inventory = new();
    private GameObject runtimeHudCanvas;

    public int TotalCarriedCount
    {
        get
        {
            int total = 0;
            foreach (var kvp in inventory)
                total += kvp.Value;
            return total > 0 ? total : OreCount;
        }
    }

    public Resource DefaultOreResource
    {
        get
        {
            var smelter = Resources.Load<FactoryData>("FactoryData/Smelter");
            return smelter != null ? smelter.inputItem : null;
        }
    }

    public Resource DefaultIngotResource
    {
        get
        {
            var smelter = Resources.Load<FactoryData>("FactoryData/Smelter");
            return smelter != null ? smelter.outputItem : null;
        }
    }

    public Resource DefaultPlateResource
    {
        get
        {
            var presser = Resources.Load<FactoryData>("FactoryData/Presser");
            return presser != null ? presser.outputItem : null;
        }
    }

    private void Awake()
    {
        if (!TryGetComponent<BuildController>(out _))
            gameObject.AddComponent<BuildController>();

        if (viewCamera == null)
            viewCamera = GetComponentInChildren<Camera>(true) ?? Camera.main;

        if (machine == null)
            machine = FindAnyObjectByType<FactoryMachine>();
        if (submissionBin == null)
            submissionBin = FindAnyObjectByType<SubmissionBin>();

        EnsureHUD();
    }

    private void Start()
    {
        EnsureHUD();
    }

    public void EnsureHUD()
    {
        if (promptText == null || objectiveText == null || inventoryText == null)
        {
            var allTexts = Resources.FindObjectsOfTypeAll<Text>();
            foreach (var t in allTexts)
            {
                if (t.name == "Interaction Prompt" || t.name == "Prompt")
                {
                    if (promptText == null) promptText = t;
                }
                else if (t.name == "Machine Status" || t.name == "Machine")
                {
                    if (machineText == null) machineText = t;
                }
                else if (t.name == "Objective")
                {
                    if (objectiveText == null) objectiveText = t;
                }
                else if (t.name == "Ore Pouch" || t.name == "Inventory")
                {
                    if (inventoryText == null) inventoryText = t;
                }
            }
        }

        bool needsRuntimeHud = resourceBarText == null || promptText == null || !promptText.gameObject.activeInHierarchy;
        if (needsRuntimeHud && runtimeHudCanvas == null)
        {
            BuildRuntimeHUD();
        }
    }

    private void BuildRuntimeHUD()
    {
        runtimeHudCanvas = new GameObject("Automation Factory HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = runtimeHudCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;

        var scaler = runtimeHudCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 1. Hotbar Resource Bar (Anchored bottom-center, exactly centered horizontally, right above the hotbar at Y = 96)
        var barPanel = CreateHudPanel("Hotbar Resource Bar", runtimeHudCanvas.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 96f), new Vector2(360f, 32f),
            new Color(0.10f, 0.12f, 0.16f, 0.90f));
        resourceBarText = CreateHudText("Resource Bar Text", barPanel.transform, font, 14, TextAnchor.MiddleCenter, Color.white);
        resourceBarText.fontStyle = FontStyle.Bold;

        // 2. Interaction & Build Prompt (Anchored bottom-center, exactly centered horizontally, above the resource bar at Y = 136)
        var promptPanel = CreateHudPanel("Prompt Panel", runtimeHudCanvas.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 136f), new Vector2(560f, 34f),
            new Color(0.06f, 0.08f, 0.12f, 0.85f));
        promptText = CreateHudText("Prompt Text", promptPanel.transform, font, 14, TextAnchor.MiddleCenter, Color.white);
    }

    private GameObject CreateHudPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color bgColor)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var img = panel.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = false;
        return panel;
    }

    private Text CreateHudText(string name, Transform parent, Font font, int fontSize, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10f, 4f);
        rt.offsetMax = new Vector2(-10f, -4f);

        var txt = go.GetComponent<Text>();
        txt.font = font;
        txt.fontSize = fontSize;
        txt.alignment = alignment;
        txt.color = color;
        txt.supportRichText = true;
        txt.raycastTarget = false;
        return txt;
    }

    public int GetItemCount(Resource item)
    {
        if (item == null)
            item = CarriedItem ?? DefaultOreResource;

        if (item != null && inventory.TryGetValue(item, out int count))
            return count;

        if (item != null && item == DefaultOreResource && OreCount > 0 && !inventory.ContainsKey(item))
            return OreCount;

        return item == null ? OreCount : (CarriedItem == item ? OreCount : 0);
    }

    public bool HasItem(Resource item, int amount)
    {
        if (amount <= 0) return true;
        return GetItemCount(item) >= amount;
    }

    public bool TryCollectOre()
    {
        return TryCollect(null, 1);
    }

    public bool TryCollect(Resource item)
    {
        return TryCollect(item, 1);
    }

    public bool TryCollect(Resource item, int amount)
    {
        if (amount <= 0)
            return true;

        if (item == null)
            item = CarriedItem ?? DefaultOreResource;

        if (TotalCarriedCount + amount > oreCapacity)
            return false;

        if (item == null)
        {
            OreCount += amount;
            return true;
        }

        if (!inventory.ContainsKey(item))
            inventory[item] = 0;

        inventory[item] += amount;
        CarriedItem = item;
        OreCount = inventory[item];
        return true;
    }

    public int TakeOre(int requested)
    {
        return TakeItem(null, requested);
    }

    public int TakeItem(Resource item, int requested)
    {
        if (requested <= 0)
            return 0;

        if (item == null)
            item = CarriedItem ?? DefaultOreResource;

        if (item == null)
        {
            int takenLegacy = Mathf.Clamp(requested, 0, OreCount);
            OreCount -= takenLegacy;
            return takenLegacy;
        }

        if (!inventory.TryGetValue(item, out int currentCount) || currentCount <= 0)
        {
            if ((CarriedItem == item || item == DefaultOreResource) && OreCount > 0)
            {
                int takenFallback = Mathf.Clamp(requested, 0, OreCount);
                OreCount -= takenFallback;
                if (OreCount == 0 && CarriedItem == item) CarriedItem = null;
                return takenFallback;
            }
            return 0;
        }

        int taken = Mathf.Clamp(requested, 0, currentCount);
        int remaining = currentCount - taken;

        if (remaining > 0)
        {
            inventory[item] = remaining;
            if (CarriedItem == item)
                OreCount = remaining;
        }
        else
        {
            inventory.Remove(item);
            if (CarriedItem == item)
            {
                CarriedItem = null;
                OreCount = 0;
                foreach (var kvp in inventory)
                {
                    CarriedItem = kvp.Key;
                    OreCount = kvp.Value;
                    break;
                }
            }
        }

        return taken;
    }

    public bool ConsumeItem(Resource item, int amount)
    {
        if (!HasItem(item, amount))
            return false;
        return TakeItem(item, amount) == amount;
    }

    private void Update()
    {
        var input = InputManager.Instance;
        FactoryInteractable station = null;
        if (viewCamera != null && input != null && input.GameplayEnabled && input.CursorLocked)
        {
            var ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            if (Physics.Raycast(ray, out var hit, interactionRange, ~0, QueryTriggerInteraction.Ignore))
                station = hit.collider.GetComponentInParent<FactoryInteractable>();

            if (station != null && station.isActiveAndEnabled
                && (station.HoldToInteract ? input.InteractHeld : input.InteractPressed))
                station.Interact(this);
        }

        int ore = GetItemCount(DefaultOreResource);
        int ingots = GetItemCount(DefaultIngotResource);
        int plates = GetItemCount(DefaultPlateResource);

        if (resourceBarText != null)
        {
            resourceBarText.text = $"<color=#E09550>ORE: {ore}</color>    |    <color=#F0D060>INGOTS: {ingots}</color>    |    <color=#A0D0F0>PLATES: {plates}</color>";
        }

        if (objectiveText != null)
        {
            if (submissionBin != null)
            {
                objectiveText.text = submissionBin.GoalReached
                    ? "<b>FIRST FACTORY COMPLETE!</b>\nKeep producing, or build a Presser to make plates."
                    : $"<b>OBJECTIVE</b>  |  Submit {submissionBin.targetCount} Iron Ingots\n"
                        + $"Delivered: {submissionBin.SubmittedCount} / {submissionBin.targetCount}"
                        + (submissionBin.StoredCount > 0 ? $" ({submissionBin.StoredCount} in bin)" : "");
            }
            else
            {
                objectiveText.text = "<b>OBJECTIVE</b>\nBuild a Smelter & Submission Bin [B]";
            }
        }

        if (inventoryText != null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<b>POUCH ({TotalCarriedCount} / {oreCapacity})</b>");
            sb.AppendLine($" • Iron Ore: {ore}");
            sb.AppendLine($" • Iron Ingot: {ingots}");
            sb.AppendLine($" • Iron Plate: {plates}");
            inventoryText.text = sb.ToString().TrimEnd();
        }

        if (machineText != null && machine != null)
            machineText.text = "SMELTER  |  " + machine.Status;

        if (promptText != null)
        {
            var build = GetComponent<BuildController>();
            if (build != null && build.IsBuildModeActive)
            {
                var data = build.SelectedFactoryData;
                bool canAfford = build.CanAfford(build.SelectedType, out string affordMsg);
                if (!canAfford)
                {
                    promptText.text = $"[B] Exit  |  <color=#FF5555>CANNOT AFFORD: {affordMsg}</color>";
                }
                else
                {
                    string costStr = data != null && data.HasBuildCost ? $" (Cost: {data.GetCostText()})" : "";
                    promptText.text = $"[B] Exit Build Mode  |  [LMB] Place {data?.DisplayName}{costStr}  |  [R] Rotate";
                }
            }
            else
            {
                string key = input != null ? input.GetLabel(PromptAction.Interact) : "E";
                promptText.text = input != null && !input.CursorLocked
                    ? "Click to resume  |  WASD move  |  Shift sprint  |  Space jump"
                    : station != null && station.isActiveAndEnabled
                        ? station.GetPrompt(this).Replace("{key}", key ?? "E")
                        : "WASD move  |  [B] Build Mode  |  Shift sprint  |  Space jump";
            }
        }
    }
}
