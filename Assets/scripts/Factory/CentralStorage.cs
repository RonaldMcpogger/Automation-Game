using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// A central depository building that stores automated resources in a shared pool.
/// Conveyor belts can deliver to it directly, or Submission Bins / Drop Chutes can route items into it remotely.
/// Crafting and Building systems pull from this pool in addition to the player's personal bag.
/// </summary>
public class CentralStorage : ConveyorReceiver
{
    public static CentralStorage Instance { get; private set; }

    [Header("Capacity")]
    [Tooltip("Maximum total items this central storage can hold.")]
    [Min(10)] public int totalCapacity = 9999;

    [Header("Display")]
    public TextMesh counterLabel;
    public TMP_Text tmpLabel;

    private readonly Dictionary<Resource, int> storedItems = new();

    public event Action Changed;

    public int TotalStoredCount
    {
        get
        {
            int sum = 0;
            foreach (var kvp in storedItems)
                sum += kvp.Value;
            return sum;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicate CentralStorage found; disabling duplicate on " + gameObject.name, this);
            enabled = false;
            return;
        }
        Instance = this;
        EnsureInteractable();
    }

    private void Start()
    {
        EnsureVisualDisplay();
        UpdateDisplay();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitCentralStorageOnLoad()
    {
        EnsureInstance();
    }

    public static CentralStorage EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        Instance = FindAnyObjectByType<CentralStorage>();
        if (Instance != null)
            return Instance;

        var prefab = Resources.Load<GameObject>("Factories/Central Storage");
        if (prefab != null)
        {
            var go = Instantiate(prefab);
            go.name = "Central Storage";
            return go.GetComponent<CentralStorage>();
        }

        var fallbackGO = CreateDefaultVisuals();
        return fallbackGO.GetComponent<CentralStorage>();
    }

    public void EnsureInteractable()
    {
        if (GetComponent<CentralStorageInteractable>() == null)
        {
            var interactable = gameObject.AddComponent<CentralStorageInteractable>();
            interactable.storage = this;
        }
    }

    private void EnsureVisualDisplay()
    {
        if (counterLabel == null && tmpLabel == null)
        {
            counterLabel = GetComponentInChildren<TextMesh>();
            if (counterLabel == null)
            {
                var labelGO = new GameObject("Storage Counter Label");
                labelGO.transform.SetParent(transform, false);
                labelGO.transform.localPosition = new Vector3(0f, 2.6f, 0f);
                counterLabel = labelGO.AddComponent<TextMesh>();
                counterLabel.fontSize = 28;
                counterLabel.characterSize = 0.08f;
                counterLabel.anchor = TextAnchor.MiddleCenter;
                counterLabel.alignment = TextAlignment.Center;
                counterLabel.color = new Color(0.3f, 0.9f, 1f, 1f);
            }
        }
    }

    public int GetItemCount(Resource res)
    {
        if (res == null) return 0;
        return storedItems.TryGetValue(res, out int count) ? count : 0;
    }

    public bool HasItem(Resource res, int amount = 1)
    {
        if (res == null || amount <= 0) return true;
        return GetItemCount(res) >= amount;
    }

    public bool TryDeposit(Resource res, int amount = 1)
    {
        if (res == null || amount <= 0) return false;
        if (TotalStoredCount + amount > totalCapacity) return false;

        storedItems.TryGetValue(res, out int count);
        storedItems[res] = count + amount;

        UpdateDisplay();
        Changed?.Invoke();
        return true;
    }

    public bool TryWithdraw(Resource res, int amount = 1)
    {
        return TryRemove(res, amount);
    }

    public bool TryRemove(Resource res, int amount = 1)
    {
        if (res == null || amount <= 0) return true;
        if (!storedItems.TryGetValue(res, out int current) || current < amount)
            return false;

        current -= amount;
        if (current <= 0)
            storedItems.Remove(res);
        else
            storedItems[res] = current;

        UpdateDisplay();
        Changed?.Invoke();
        return true;
    }

    public IReadOnlyDictionary<Resource, int> GetAllStored() => storedItems;

    public override bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null || !body.TryGetComponent<FactoryItem>(out var item))
            return false;

        Resource res = item.itemData;
        if (res == null)
        {
            if (item.material == FactoryItem.MaterialKind.IronIngot)
            {
                var smelterData = Resources.Load<FactoryData>("FactoryData/Smelter");
                res = smelterData != null ? smelterData.outputItem : null;
            }
        }

        if (res == null || !TryDeposit(res, 1))
            return false;

        body.gameObject.SetActive(false);
        Destroy(body.gameObject);
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryReceive(other.attachedRigidbody);
    }

    public void UpdateDisplay()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CENTRAL STORAGE ===");
        if (storedItems.Count == 0)
        {
            sb.AppendLine("(Storage Empty)");
            sb.AppendLine("Conveyors & Chutes feed here");
        }
        else
        {
            foreach (var kvp in storedItems)
            {
                string name = kvp.Key != null ? kvp.Key.DisplayName : "Unknown";
                sb.AppendLine($"• {name}: {kvp.Value}");
            }
            sb.AppendLine($"Total: {TotalStoredCount} / {totalCapacity}");
        }

        string text = sb.ToString().TrimEnd();
        if (counterLabel != null) counterLabel.text = text;
        if (tmpLabel != null) tmpLabel.text = text;
    }

    public int DepositFromInventory(Inventory inventory, Resource res, int requestedAmount)
    {
        if (inventory == null || res == null || requestedAmount <= 0)
            return 0;

        int inInventory = inventory.GetItemCount(res);
        int freeSpace = Mathf.Max(0, totalCapacity - TotalStoredCount);
        int toMove = Mathf.Min(requestedAmount, inInventory, freeSpace);
        if (toMove <= 0)
            return 0;

        if (!inventory.TryRemove(res, toMove))
            return 0;

        if (!TryDeposit(res, toMove))
        {
            inventory.TryAdd(res, toMove);
            return 0;
        }

        return toMove;
    }

    public int WithdrawToInventory(Inventory inventory, Resource res, int requestedAmount)
    {
        if (inventory == null || res == null || requestedAmount <= 0)
            return 0;

        int inStorage = GetItemCount(res);
        int roomInBag = inventory.GetAvailableRoom(res);
        int toMove = Mathf.Min(requestedAmount, inStorage, roomInBag);
        if (toMove <= 0)
            return 0;

        if (!TryRemove(res, toMove))
            return 0;

        if (!inventory.TryAdd(res, toMove))
        {
            TryDeposit(res, toMove);
            return 0;
        }

        return toMove;
    }

    public int DepositAllFromInventory(Inventory inventory)
    {
        if (inventory == null)
            return 0;

        int totalMoved = 0;
        foreach (var kvp in inventory.GetAllItemCounts())
        {
            if (TotalStoredCount >= totalCapacity)
                break;

            totalMoved += DepositFromInventory(inventory, kvp.Key, kvp.Value);
        }
        return totalMoved;
    }

    public bool WithdrawToPlayer(FactoryPlayer player)
    {
        if (player == null || Inventory.Instance == null || storedItems.Count == 0)
            return false;

        foreach (var kvp in new List<KeyValuePair<Resource, int>>(storedItems))
        {
            Resource res = kvp.Key;
            int countInStorage = kvp.Value;
            if (res == null || countInStorage <= 0) continue;

            if (Inventory.Instance.CanAdd(res, 1))
            {
                int maxStack = Mathf.Max(1, res.maxStackSize);
                int toTake = Mathf.Min(maxStack, countInStorage);
                int taken = 0;
                for (int i = 0; i < toTake; i++)
                {
                    if (Inventory.Instance.TryAdd(res, 1))
                        taken++;
                    else
                        break;
                }

                if (taken > 0)
                {
                    TryRemove(res, taken);
                    return true;
                }
            }
        }

        return false;
    }

    private static GameObject CreateDefaultVisuals()
    {
        var root = new GameObject("Central Storage");
        root.transform.position = new Vector3(-34f, 0f, -1f);

        // Base platform
        var baseObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseObj.name = "Storage Base";
        baseObj.transform.SetParent(root.transform, false);
        baseObj.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        baseObj.transform.localScale = new Vector3(3.5f, 0.5f, 3.5f);

        // Silo body
        var siloObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        siloObj.name = "Silo Tank";
        siloObj.transform.SetParent(root.transform, false);
        siloObj.transform.localPosition = new Vector3(0f, 2f, 0f);
        siloObj.transform.localScale = new Vector3(2.6f, 1.5f, 2.6f);

        // Roof cone / cap
        var roofObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        roofObj.name = "Silo Cap";
        roofObj.transform.SetParent(root.transform, false);
        roofObj.transform.localPosition = new Vector3(0f, 3.6f, 0f);
        roofObj.transform.localScale = new Vector3(2.8f, 0.2f, 2.8f);

        // Intake funnel / chute opening
        var chuteObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chuteObj.name = "Intake Chute";
        chuteObj.transform.SetParent(root.transform, false);
        chuteObj.transform.localPosition = new Vector3(0f, 1.2f, 1.5f);
        chuteObj.transform.localScale = new Vector3(1.2f, 0.8f, 0.8f);

        // Boundary trigger for loose items or conveyor receiver
        var triggerObj = new GameObject("BoundaryTrigger");
        triggerObj.transform.SetParent(root.transform, false);
        triggerObj.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        var triggerCol = triggerObj.AddComponent<BoxCollider>();
        triggerCol.size = new Vector3(3.8f, 3f, 3.8f);
        triggerCol.isTrigger = true;

        var storage = root.AddComponent<CentralStorage>();
        storage.EnsureVisualDisplay();
        storage.EnsureInteractable();

        return root;
    }
}

/// <summary>Allows the player to withdraw resources from central storage.</summary>
public class CentralStorageInteractable : FactoryInteractable
{
    public CentralStorage storage;

    private void Awake()
    {
        if (storage == null)
            storage = GetComponent<CentralStorage>();
    }

    public override string GetPrompt(FactoryPlayer player)
    {
        if (storage == null) storage = GetComponent<CentralStorage>();
        if (storage == null) return string.Empty;

        if (storage.TotalStoredCount <= 0)
            return $"Press [{{key}}] to open Central Storage (Empty | Cap {storage.totalCapacity})";

        return $"Press [{{key}}] to manage Central Storage ({storage.TotalStoredCount} / {storage.totalCapacity})";
    }

    public override void Interact(FactoryPlayer player)
    {
        if (storage == null) storage = GetComponent<CentralStorage>();
        if (storage != null)
        {
            CentralStorageUI.EnsureInstance().Open(storage, player);
        }
    }
}
