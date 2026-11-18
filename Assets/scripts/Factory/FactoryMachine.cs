using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class FactorySlot
{
    public Resource item;
    [Min(0)] public int amount;
    public bool IsEmpty => item == null || amount <= 0;
}

/// <summary>Base class for machines that craft the recipes in their FactoryData.</summary>
public class FactoryMachine : FactoryInteractable
{
    [Tooltip("Recipe and presentation data for this machine.")]
    public FactoryData factoryData;

    [Header("Storage / Processing Settings")]
    [Tooltip("Maximum number of items held by one input slot.")]
    [Min(1)] public int inputCapacity = 10;
    [Tooltip("Maximum number of items held by one output slot.")]
    [Min(1)] public int outputCapacity = 10;

    // These lists are machine data. Future UI reads them; it does not store items itself.
    [SerializeField] private List<FactorySlot> inputSlots = new();
    [SerializeField] private List<FactorySlot> outputSlots = new();
    public IReadOnlyList<FactorySlot> InputSlots => inputSlots;
    public IReadOnlyList<FactorySlot> OutputSlots => outputSlots;

    [Header("Conveyor Ports")]
    [SerializeField] protected Transform inputPort;
    [SerializeField] protected Transform outputPort;
    public Conveyor outputBelt;
    public FactoryItem ingotPrefab;
    public Renderer statusLight;
    public Material idleMaterial;
    public Material workingMaterial;
    public Material blockedMaterial;

    [Min(0.05f)] public float processingSeconds = 2f;
    public float beltSurfaceHeight = 0.83f;

    // Retained for older machines and their subclasses.
    public int BufferedOre { get; protected set; }
    public bool IsProcessing { get; protected set; }
    public bool OutputReady { get; protected set; }
    public int ProducedCount { get; protected set; }
    protected float elapsed;
    protected CraftingRecipe currentRecipe;

    protected bool UsesRecipeSystem => factoryData != null
        && factoryData.ValidCraftables != null
        && factoryData.ValidCraftables.Count > 0;

    /// <summary>True while this machine is allowed to process recipes and move conveyor output.</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>Allows this machine to process again after it has been stopped from its menu.</summary>
    public virtual void StartMachine() => IsRunning = true;

    /// <summary>Pauses this machine without removing its inputs, outputs, or in-progress recipe time.</summary>
    public virtual void StopMachine() => IsRunning = false;

    protected virtual void Awake()
    {
        ResolvePorts();
        ApplyFactoryData();
        EnsureReceiver();
    }

    /// <summary>Finds the optional child transforms used as conveyor input and output ports.</summary>
    public void ResolvePorts()
    {
        if (inputPort == null) inputPort = transform.Find("Input");
        if (outputPort == null) outputPort = transform.Find("Output");
    }

    public Transform OutputPort { get => outputPort; set => outputPort = value; }
    public Transform InputPort { get => inputPort; set => inputPort = value; }

    /// <summary>Adds the component that lets a connected conveyor deliver items to this machine.</summary>
    public void EnsureReceiver()
    {
        if (InputItem == null && !UsesRecipeSystem) return;

        ResolvePorts();
        GameObject target = inputPort != null ? inputPort.gameObject : gameObject;
        if (target.GetComponent<ConveyorReceiver>() == null)
        {
            var receiver = target.AddComponent<MachineReceiver>();
            receiver.machine = this;
        }
    }

    /// <summary>Copies storage and processing settings from this machine's FactoryData asset.</summary>
    public virtual void ApplyFactoryData()
    {
        if (factoryData == null) return;

        inputCapacity = factoryData.inputCapacity;
        outputCapacity = factoryData.outputCapacity;
        processingSeconds = factoryData.processingSeconds;
        EnsureSlotCount(inputSlots, factoryData.inputSlotCount);
        EnsureSlotCount(outputSlots, factoryData.outputSlotCount);
    }

    /// <summary>Adds empty storage slots until the list reaches the requested machine capacity.</summary>
    private static void EnsureSlotCount(List<FactorySlot> slots, int count)
    {
        while (slots.Count < Mathf.Max(1, count))
            slots.Add(new FactorySlot());
    }

    // Used by older factory assets. Recipe machines use CraftingRecipe input/output data.
    public virtual Resource InputItem => factoryData != null ? factoryData.inputItem : null;
    public virtual Resource OutputItem => factoryData != null ? factoryData.outputItem : null;
    public virtual FactoryItem OutputPrefab => OutputItem != null && OutputItem.objModel != null
        ? OutputItem.objModel.GetComponent<FactoryItem>() : ingotPrefab;

    public virtual string Status
    {
        get
        {
            if (!IsRunning) return "Stopped";

            if (UsesRecipeSystem)
            {
                if (OutputReady) return "Output stored - waiting for conveyor";
                if (IsProcessing) return $"Crafting {Mathf.Clamp01(elapsed / Mathf.Max(0.01f, currentRecipe.craftDuration)):P0}";
                return "Waiting for ingredients";
            }

            return outputBelt == null || OutputPrefab == null ? "Output not configured"
                : OutputReady ? $"Output blocked - waiting | Ore queued: {BufferedOre}"
                : IsProcessing ? $"Smelting {Mathf.Clamp01(elapsed / processingSeconds):P0} | Ore queued: {BufferedOre}"
                : $"Waiting for ore | Ore queued: {BufferedOre}";
        }
    }

    public override string GetPrompt(FactoryPlayer player)
    {
        if (UsesRecipeSystem)
            return IsInputStorageFull() ? "MACHINE - input storage full"
                : "Press [{key}] to load an ingredient";

        return player.GetItemCount(InputItem) == 0
            ? $"{factoryData?.DisplayName ?? "SMELTER"} - gather {InputItem?.DisplayName ?? "iron ore"} at the resource spot"
            : BufferedOre >= inputCapacity ? "SMELTER - input full"
            : "Press [{key}] to load ore into the smelter";
    }

    public override void Interact(FactoryPlayer player) => LoadFrom(player);

    /// <summary>Moves the player's selected valid ingredient into this machine's input storage.</summary>
    public virtual int LoadFrom(FactoryPlayer player)
    {
        if (!isActiveAndEnabled || player == null) return 0;

        if (!UsesRecipeSystem)
        {
            int loaded = player.TakeItem(InputItem, Mathf.Max(0, inputCapacity - BufferedOre));
            BufferedOre += loaded;
            return loaded;
        }

        Resource item = player.CarriedItem;
        if (!IsValidIngredient(item)) item = InputItem;
        if (!IsValidIngredient(item)) item = GetFirstIngredient();

        int added = 0;
        while (player.GetItemCount(item) > 0 && TryAddInput(item, 1))
        {
            if (player.TakeItem(item, 1) != 1) break;
            added++;
        }
        BufferedOre = TotalItems(inputSlots);
        return added;
    }

    /// <summary>Accepts a conveyor item only when it is a recipe ingredient and storage has room.</summary>
    public virtual bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null) return false;
        if (!body.TryGetComponent<FactoryItem>(out FactoryItem item)) return false;

        if (UsesRecipeSystem)
        {
            if (!TryAddInput(item.itemData, 1)) return false;
            Destroy(item.gameObject);
            BufferedOre = TotalItems(inputSlots);
            return true;
        }

        if (BufferedOre >= inputCapacity || (InputItem != null && !item.Matches(InputItem)))
            return false;

        BufferedOre++;
        Destroy(item.gameObject);
        return true;
    }

    /// <summary>Chooses either recipe processing or the legacy one-input processing path each frame.</summary>
    protected virtual void Update()
    {
        if (!IsRunning) return;
        if (UsesRecipeSystem) UpdateRecipeMachine();
        else UpdateLegacyMachine();
    }

    /// <summary>Sends buffered output, advances the active recipe, and starts the next matching recipe.</summary>
    private void UpdateRecipeMachine()
    {
        TrySendOutputToBelt();

        if (IsProcessing)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= currentRecipe.craftDuration)
            {
                TryAddToSlots(outputSlots, currentRecipe.outputItem, currentRecipe.outputAmount, outputCapacity);
                IsProcessing = false;
                currentRecipe = null;
            }
        }
        else
        {
            CraftingRecipe recipe = FindCraftableRecipe();
            if (recipe != null && CanAddToSlots(outputSlots, recipe.outputItem, recipe.outputAmount, outputCapacity))
            {
                RemoveIngredients(recipe);
                currentRecipe = recipe;
                elapsed = 0f;
                IsProcessing = true;
            }
        }

        BufferedOre = TotalItems(inputSlots);
        OutputReady = HasStoredOutput();
        if (statusLight != null)
            statusLight.sharedMaterial = OutputReady ? blockedMaterial
                : IsProcessing ? workingMaterial : idleMaterial;
    }

    // Original one-input machine behaviour, kept for assets without recipe entries.
    /// <summary>Runs the original fixed-input smelter behavior for FactoryData with no recipe list.</summary>
    private void UpdateLegacyMachine()
    {
        FactoryItem outputPrefab = OutputPrefab;
        if (outputBelt == null || outputPrefab == null) return;

        if (OutputReady)
        {
            if (outputBelt.CanLoadAtStart)
            {
                FactoryItem item = Instantiate(outputPrefab, outputBelt.StartPosition, Quaternion.identity);
                item.itemData = OutputItem;
                Rigidbody body = item.GetComponent<Rigidbody>();
                if (body != null && outputBelt.TryLoadAtStart(body, beltSurfaceHeight))
                {
                    OutputReady = false;
                    ProducedCount++;
                }
                else Destroy(item.gameObject);
            }
        }
        else if (IsProcessing)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= processingSeconds)
            {
                IsProcessing = false;
                OutputReady = true;
            }
        }
        else if (BufferedOre > 0)
        {
            BufferedOre--;
            elapsed = 0f;
            IsProcessing = true;
        }

        if (statusLight != null)
            statusLight.sharedMaterial = OutputReady ? blockedMaterial
                : IsProcessing ? workingMaterial : idleMaterial;
    }

    /// <summary>Adds a valid recipe ingredient to input slots without accepting unrelated resources.</summary>
    public bool TryAddInput(Resource item, int amount = 1)
    {
        return IsValidIngredient(item) && TryAddToSlots(inputSlots, item, amount, inputCapacity);
    }

    /// <summary>Checks whether at least one recipe assigned to this machine uses the resource.</summary>
    private bool IsValidIngredient(Resource item)
    {
        if (item == null || factoryData == null || factoryData.ValidCraftables == null) return false;

        foreach (CraftingRecipe recipe in factoryData.ValidCraftables)
        {
            if (recipe == null || !recipe.IsValid) continue;
            foreach (CraftingIngredient ingredient in recipe.ingredients)
                if (ingredient.item == item) return true;
        }
        return false;
    }

    /// <summary>Gets a fallback ingredient for player interaction when no carried item is valid.</summary>
    private Resource GetFirstIngredient()
    {
        foreach (CraftingRecipe recipe in factoryData.ValidCraftables)
            if (recipe != null && recipe.ingredients != null && recipe.ingredients.Length > 0)
                return recipe.ingredients[0].item;
        return null;
    }

    /// <summary>Returns the first assigned recipe whose complete ingredient requirement is in input storage.</summary>
    private CraftingRecipe FindCraftableRecipe()
    {
        foreach (CraftingRecipe recipe in factoryData.ValidCraftables)
        {
            if (recipe == null || !recipe.IsValid) continue;

            bool hasIngredients = true;
            foreach (CraftingIngredient ingredient in recipe.ingredients)
            {
                int required = 0;
                foreach (CraftingIngredient other in recipe.ingredients)
                    if (other.item == ingredient.item) required += other.amount;

                if (GetInputAmount(ingredient.item) < required)
                {
                    hasIngredients = false;
                    break;
                }
            }
            if (hasIngredients) return recipe;
        }
        return null;
    }

    /// <summary>Counts one resource across all input slots.</summary>
    private int GetInputAmount(Resource item)
    {
        int total = 0;
        foreach (FactorySlot slot in inputSlots)
            if (slot.item == item) total += slot.amount;
        return total;
    }

    /// <summary>Consumes a recipe's ingredients after the machine has confirmed they are all available.</summary>
    private void RemoveIngredients(CraftingRecipe recipe)
    {
        foreach (CraftingIngredient ingredient in recipe.ingredients)
        {
            int remaining = ingredient.amount;
            foreach (FactorySlot slot in inputSlots)
            {
                if (slot.item != ingredient.item) continue;

                int removed = Mathf.Min(slot.amount, remaining);
                slot.amount -= removed;
                remaining -= removed;
                if (slot.amount == 0) slot.item = null;
                if (remaining == 0) break;
            }
        }
    }

    /// <summary>Checks whether a whole item amount fits before changing any stack.</summary>
    private static bool CanAddToSlots(List<FactorySlot> slots, Resource item, int amount, int slotCapacity)
    {
        if (item == null || amount <= 0) return false;

        int room = 0;
        foreach (FactorySlot slot in slots)
        {
            if (slot.item == item) room += Mathf.Max(0, slotCapacity - slot.amount);
            else if (slot.IsEmpty) room += slotCapacity;
        }
        return room >= amount;
    }

    /// <summary>Fills matching stacks first, then empty slots, after confirming the whole amount fits.</summary>
    private static bool TryAddToSlots(List<FactorySlot> slots, Resource item, int amount, int slotCapacity)
    {
        if (!CanAddToSlots(slots, item, amount, slotCapacity)) return false;

        int remaining = amount;
        foreach (FactorySlot slot in slots)
        {
            if (slot.item != item || slot.amount >= slotCapacity) continue;
            int added = Mathf.Min(slotCapacity - slot.amount, remaining);
            slot.amount += added;
            remaining -= added;
            if (remaining == 0) return true;
        }

        foreach (FactorySlot slot in slots)
        {
            if (!slot.IsEmpty) continue;
            int added = Mathf.Min(slotCapacity, remaining);
            slot.item = item;
            slot.amount = added;
            remaining -= added;
            if (remaining == 0) return true;
        }
        return false;
    }

    /// <summary>Reports whether every input slot has reached its stack capacity.</summary>
    private bool IsInputStorageFull()
    {
        foreach (FactorySlot slot in inputSlots)
            if (slot.IsEmpty || slot.amount < inputCapacity) return false;
        return true;
    }

    /// <summary>Returns the combined amount stored in a set of slots.</summary>
    private static int TotalItems(List<FactorySlot> slots)
    {
        int total = 0;
        foreach (FactorySlot slot in slots) total += slot.amount;
        return total;
    }

    /// <summary>Reports whether at least one finished item is waiting in output storage.</summary>
    private bool HasStoredOutput()
    {
        foreach (FactorySlot slot in outputSlots)
            if (!slot.IsEmpty) return true;
        return false;
    }

    /// <summary>Spawns and sends one buffered output item when the output conveyor has room.</summary>
    private void TrySendOutputToBelt()
    {
        if (outputBelt == null || !outputBelt.CanLoadAtStart) return;

        foreach (FactorySlot slot in outputSlots)
        {
            if (slot.IsEmpty || slot.item.objModel == null) continue;

            FactoryItem prefab = slot.item.objModel.GetComponent<FactoryItem>();
            if (prefab == null) continue;

            FactoryItem item = Instantiate(prefab, outputBelt.StartPosition, Quaternion.identity);
            item.itemData = slot.item;
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body != null && outputBelt.TryLoadAtStart(body, beltSurfaceHeight))
            {
                slot.amount--;
                if (slot.amount == 0) slot.item = null;
                ProducedCount++;
            }
            else Destroy(item.gameObject);
            return; // Load one item per frame.
        }
    }
}

/// <summary>Allows any FactoryMachine to receive inputs from conveyor belts.</summary>
public class MachineReceiver : ConveyorReceiver
{
    public FactoryMachine machine;

    private void Awake()
    {
        if (machine == null) machine = GetComponentInParent<FactoryMachine>();
    }

    public override bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null) return false;
        if (machine == null) machine = GetComponentInParent<FactoryMachine>();
        return machine != null && machine.TryReceive(body);
    }
}
