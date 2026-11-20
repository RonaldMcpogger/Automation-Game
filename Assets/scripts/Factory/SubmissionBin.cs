using UnityEngine;

public class SubmissionBin : ConveyorReceiver
{
    [Tooltip("When assigned, only this item ScriptableObject is accepted.")]
    public Resource acceptedItem;
    [Min(1)] public int targetCount = 10;
    public TextMesh counterLabel;
    public int SubmittedCount { get; private set; }
    public int StoredCount { get; private set; }
    public bool GoalReached => SubmittedCount >= targetCount;

    private void Awake()
    {
        EnsureInteractable();
    }

    private void Start()
    {
        UpdateLabel();
    }

    public void EnsureInteractable()
    {
        if (GetComponent<SubmissionBinInteractable>() == null)
        {
            var interactable = gameObject.AddComponent<SubmissionBinInteractable>();
            interactable.bin = this;
        }
    }

    public override bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null || !body.TryGetComponent<FactoryItem>(out var item)
            || !item.TryMarkSubmitted(acceptedItem))
            return false;

        Resource res = item.itemData;
        if (res == null)
        {
            var smelterData = Resources.Load<FactoryData>("FactoryData/Smelter");
            if (smelterData != null) res = smelterData.outputItem;
        }

        var storage = CentralStorage.EnsureInstance();
        bool deposited = storage != null ? storage.TryDeposit(res, 1) : (Inventory.Instance != null && Inventory.Instance.TryAdd(res, 1));
        if (!deposited)
            return false;

        SubmittedCount++;
        StoredCount++;
        UpdateLabel();

        body.gameObject.SetActive(false);
        Destroy(body.gameObject);
        return true;
    }

    public void RegisterDeposit()
    {
        SubmittedCount++;
        StoredCount++;
        UpdateLabel();
    }

    public bool TakeIngot(FactoryPlayer player)
    {
        if (player == null)
            return false;

        var storage = CentralStorage.Instance;
        Resource itemToGive = acceptedItem;
        if (itemToGive == null)
        {
            var smelterData = Resources.Load<FactoryData>("FactoryData/Smelter");
            if (smelterData != null && smelterData.outputItem != null)
                itemToGive = smelterData.outputItem;
        }

        if (storage != null && itemToGive != null && storage.HasItem(itemToGive, 1))
        {
            if (player.TryCollect(itemToGive))
            {
                storage.TryRemove(itemToGive, 1);
                if (StoredCount > 0) StoredCount--;
                UpdateLabel();
                return true;
            }
        }
        else if (StoredCount > 0 && itemToGive != null && player.TryCollect(itemToGive))
        {
            StoredCount--;
            UpdateLabel();
            return true;
        }

        return false;
    }

    public void UpdateLabel()
    {
        if (counterLabel != null)
        {
            string filterText = acceptedItem != null ? acceptedItem.DisplayName : "ALL MATERIALS";
            counterLabel.text = $"DROP CHUTE\n[{filterText}]\n{SubmittedCount} Processed\n-> Central Storage";
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Also accepts a loose item that falls into the opening.
        TryReceive(other.attachedRigidbody);
    }
}

/// <summary>Allows the player to inspect the drop chute or manually deposit materials.</summary>
public class SubmissionBinInteractable : FactoryInteractable
{
    public SubmissionBin bin;

    private void Awake()
    {
        if (bin == null)
            bin = GetComponent<SubmissionBin>();
    }

    public override string GetPrompt(FactoryPlayer player)
    {
        if (bin == null)
            bin = GetComponent<SubmissionBin>();

        if (bin == null)
            return string.Empty;

        string itemName = bin.acceptedItem != null ? bin.acceptedItem.DisplayName : "materials";
        var carried = player != null && Inventory.Instance != null ? Inventory.Instance.CurrentResource : null;
        if (carried != null && (bin.acceptedItem == null || carried == bin.acceptedItem))
        {
            return $"Press [{{key}}] to deposit {carried.DisplayName} -> Central Storage ({bin.SubmittedCount} sent)";
        }

        return $"DROP CHUTE | Routes {itemName} to Central Storage ({bin.SubmittedCount} sent)";
    }

    public override void Interact(FactoryPlayer player)
    {
        if (bin == null)
            bin = GetComponent<SubmissionBin>();

        if (bin == null || player == null || Inventory.Instance == null)
            return;

        var toDeposit = bin.acceptedItem ?? Inventory.Instance.CurrentResource;
        if (toDeposit != null && Inventory.Instance.HasItem(toDeposit, 1))
        {
            var central = CentralStorage.EnsureInstance();
            bool stored = central != null ? central.TryDeposit(toDeposit, 1) : false;
            if (stored)
            {
                Inventory.Instance.TryRemove(toDeposit, 1);
                bin.RegisterDeposit();
            }
        }
    }
}
