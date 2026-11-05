using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Stores picked-up resources in fixed, stack-limited slots and controls the inventory UI.</summary>
public class Inventory : MonoBehaviour
{
    /// <summary>The active player inventory, used by world pickups.</summary>
    public static Inventory Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private CanvasGroup InventoryGroup; // inventory canvas group
    [SerializeField] public GameObject[] InventorySlotList = new GameObject[30]; // inventory list
    [SerializeField] private GameObject InventorySlotPrefab; // prefab for the slots
    [SerializeField] private Button dropButton; // drop button

    [Header("World Drop")] // reference
    [Tooltip("Optional orign reference point for drops, this is so inventory knows which place at a set distance can it drop the item")]
    [SerializeField] private Transform dropOrigin;
    [SerializeField, Min(0.5f)] private float dropDistance = 2f;

    private readonly List<Slot> slots = new();
    private InputAction inventoryOpenAction;
    private int selectedSlot = -1;
    private bool isOpen;

    private sealed class Slot
    {
        public Resource item; // itemdata
        public int amount;
        public Button button;
        public Image icon;
        public TMP_Text itemName;
        public TMP_Text amountLabel;
    }

    /// <summary>Registers the one inventory that world pickups can use.</summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("More than one Inventory exists; disabling the duplicate.", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    /// <summary>Builds slot views, wires the Drop button, and starts with the panel hidden.</summary>
    private void Start()
    {
        BuildSlotViews();


        FindAndWireDropButton();

        SetOpen(false);

        RefreshUI();
    }

    /// <summary>Toggles the inventory with InventoryOpen (I), or closes it with Escape.</summary>
    private void Update()
    {
        var input = InputManager.Instance;
        if (input == null)
            return;

        inventoryOpenAction ??= input.GetExtraAction("InventoryOpen", "<Keyboard>/i");
        if (inventoryOpenAction != null && inventoryOpenAction.WasPressedThisFrame())
            SetOpen(!isOpen);
        else if (isOpen && input.UICancelPressed)
            SetOpen(false);
    }

    /// <summary>Releases UI mode if this component is disabled while its panel is open.</summary>
    private void OnDisable()
    {
        if (Instance == this)
            InputManager.Instance?.ExitUIMode(this);
    }

    /// <summary>Clears the world-pickup reference when this inventory is destroyed.</summary>
    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Adds items by filling partial matching stacks first, then empty slots.
    /// Returns false if the inventory lacks room for every requested item.
    /// </summary>
    public bool TryAdd(Resource item, int amount = 1)
    {
        if (item == null || amount <= 0)
            return false;

        int remaining = amount;
        int stackLimit = Mathf.Max(1, item.maxStackSize);
        foreach (var slot in slots)
        {
            if (slot.item != item || slot.amount >= stackLimit)
                continue;
            int added = Mathf.Min(stackLimit - slot.amount, remaining);
            slot.amount += added;
            remaining -= added;
            if (remaining == 0)
            {
                RefreshUI();
                return true;
            }
        }

        foreach (var slot in slots)
        {
            if (slot.item != null)
                continue;
            int added = Mathf.Min(stackLimit, remaining);
            slot.item = item;
            slot.amount = added;
            remaining -= added;
            if (remaining == 0)
            {
                RefreshUI();
                return true;
            }
        }

        RefreshUI();
        return false;
    }

    /// <summary>Attempts to transfer a FactoryItem from the world; failed transfers leave it in place.</summary>
    public bool TryPickup(FactoryItem worldItem) => worldItem != null && TryAdd(worldItem.itemData);

    /// <summary>Compatibility entry point for UI events and scripts adding one resource.</summary>
    public void AddItem(Resource item) => TryAdd(item);

    /// <summary>Drops one selected item into the world instead of deleting it.</summary>
    public void DropItem()
    {
        if (selectedSlot < 0 || selectedSlot >= slots.Count)
            return;
        var slot = slots[selectedSlot];
        if (slot.item == null || slot.amount <= 0 || slot.item.objModel == null)
            return;

        Transform origin = GetDropOrigin();
        if (origin == null)
        {
            Debug.LogWarning("Inventory cannot drop without a player camera or drop origin.", this);
            return;
        }

        var dropped = Instantiate(slot.item.objModel, origin.position + origin.forward * dropDistance, Quaternion.identity);
        if (dropped.TryGetComponent<FactoryItem>(out var factoryItem))
            factoryItem.itemData = slot.item;

        if (--slot.amount == 0)
        {
            slot.item = null;
            selectedSlot = -1;
        }
        RefreshUI();
    }

    /// <summary>Returns true when the inventory contains at least one resource with this name.</summary>
    public bool SearchItem(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return false;
        foreach (var slot in slots)
            if (slot.item != null && slot.amount > 0 && slot.item.DisplayName == itemName)
                return true;
        return false;
    }

    /// <summary>Selects the clicked occupied slot for subsequent Drop button presses.</summary>
    private void SelectSlot(int index)
    {
        if (index >= 0 && index < slots.Count && slots[index].item != null)
            selectedSlot = index;
        RefreshUI();
    }

    /// <summary>Shows the panel and enters UI mode, or hides it and restores gameplay input.</summary>
    private void SetOpen(bool open)
    {
        isOpen = open;
        if (InventoryGroup != null)
        {
            InventoryGroup.alpha = open ? 1f : 0f;//fade 
            InventoryGroup.interactable = open;
            InventoryGroup.blocksRaycasts = open;
        }

        if (open)
            InputManager.Instance?.EnterUIMode(this);
        else
            InputManager.Instance?.ExitUIMode(this);
    }

    /// <summary>Uses the existing slot as the first view, then clones it to fill the fixed capacity.</summary>
    private void BuildSlotViews()
    {
        if (InventorySlotPrefab == null || InventorySlotList == null || InventorySlotList.Length == 0)
        {
            Debug.LogError("Inventory needs an Item Slot prefab and at least one slot.", this);
            return;
        }
        Transform container= InventoryGroup.transform;
        if (InventorySlotPrefab.transform.parent != null)
        {
            container = InventorySlotPrefab.transform.parent;
        }
        
        for (int i = 0; i < InventorySlotList.Length; i++)
        {
            // view means the slot prefab

            GameObject view = InventorySlotPrefab; 

            if (i != 0)
            {
                view = Instantiate(InventorySlotPrefab, container);
            }


            view.name = $"Item Slot {i + 1}";
            view.SetActive(true);
            InventorySlotList[i] = view;

            var slot = new Slot { button = view.GetComponent<Button>() };
            var icon = view.transform.Find("Item Image");
            var itemName = view.transform.Find("Item Name");
            var amount = view.transform.Find("Amount Stack");
            slot.icon = icon != null ? icon.GetComponent<Image>() : null;

            slot.itemName = itemName != null ? itemName.GetComponent<TMP_Text>() : null;
            slot.amountLabel = amount != null ? amount.GetComponent<TMP_Text>() : null;

            int index = i;
            if (slot.button != null)
            {
                slot.button.onClick.RemoveAllListeners();
                slot.button.onClick.AddListener(() => SelectSlot(index));
            }
            slots.Add(slot);
        }
    }

    /// <summary>Finds and wires the existing Drop button if it was not assigned in the inspector.</summary>
    private void FindAndWireDropButton()
    {
        if (dropButton == null && InventoryGroup != null)
            foreach (var button in InventoryGroup.GetComponentsInChildren<Button>(true))
                if (button.name == "Drop")
                {
                    dropButton = button;
                    break;
                }

        if (dropButton != null)
        {
            dropButton.onClick.RemoveAllListeners();
            dropButton.onClick.AddListener(DropItem);
        }
    }

    /// <summary>Renders item icon, name, amount, selection state, and Drop availability for every slot.</summary>
    private void RefreshUI()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            bool occupied = slot.item != null && slot.amount > 0;
            if (slot.icon != null)
            {
                slot.icon.sprite = occupied ? slot.item.ItemImage : null;
                slot.icon.enabled = occupied && slot.item.ItemImage != null;
            }
            if (slot.itemName != null)
                slot.itemName.text = occupied ? slot.item.DisplayName : string.Empty;
            if (slot.amountLabel != null)
                slot.amountLabel.text = occupied ? slot.amount.ToString() : string.Empty;
            if (slot.button != null)
            {
                var colors = slot.button.colors;
                colors.normalColor = i == selectedSlot ? new Color(1f, 0.85f, 0.25f) : Color.white;
                slot.button.colors = colors;
            }
        }

        if (dropButton != null)
            dropButton.interactable = selectedSlot >= 0 && selectedSlot < slots.Count && slots[selectedSlot].item != null;
    }

    /// <summary>Uses the assigned drop origin or the active FPS camera as the world spawn point.</summary>
    private Transform GetDropOrigin()
    {
        if (dropOrigin != null)
            return dropOrigin;
        var player = FindAnyObjectByType<FirstPersonController>();
        return player != null ? player.GetComponentInChildren<Camera>(true)?.transform : Camera.main?.transform;
    }


    //test code for resource getting 

    public int GetItemCount(Resource item) // check number of item
    {
        if (item == null)
            return 0;

        int total = 0;
        foreach (var slot in slots)
        {
            if (slot.item == item)
                total += slot.amount;
        }
        return total;
    }

    public bool HasItem(Resource item, int amount) // check if has resource in inventory
    {
        return amount <= 0 || GetItemCount(item) >= amount;
    }

    public bool TryRemove(Resource item, int amount) // bool remove from inventory
    {
        if (item == null || amount <= 0 || !HasItem(item, amount))
            return false;

        int remaining = amount; // total left

        foreach (var slot in slots)
        {
            if (slot.item != item)
                continue;

            int removed = Mathf.Min(slot.amount, remaining);
            slot.amount -= removed;
            remaining -= removed;

            if (slot.amount == 0)
                slot.item = null;

            if (remaining == 0)
                break;
        }

        RefreshUI();
        return true;
    }
}
