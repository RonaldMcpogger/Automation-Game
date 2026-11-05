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

        SubmittedCount++;
        StoredCount++;
        UpdateLabel();

        body.gameObject.SetActive(false);
        Destroy(body.gameObject);
        return true;
    }

    public bool TakeIngot(FactoryPlayer player)
    {
        if (StoredCount <= 0 || player == null)
            return false;

        Resource itemToGive = acceptedItem;
        if (itemToGive == null)
        {
            var smelterData = Resources.Load<FactoryData>("FactoryData/Smelter");
            if (smelterData != null && smelterData.outputItem != null)
                itemToGive = smelterData.outputItem;
        }

        if (player.TryCollect(itemToGive))
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
            counterLabel.text = $"SUBMISSION BIN\n{SubmittedCount} / {targetCount} INGOTS"
                + (StoredCount > 0 ? $"\n({StoredCount} READY)" : "")
                + (GoalReached ? "\nGOAL COMPLETE" : "");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Also accepts a loose ingot that falls into the opening.
        TryReceive(other.attachedRigidbody);
    }
}

/// <summary>Allows the player to retrieve processed ingots directly from the submission bin.</summary>
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

        string itemName = bin.acceptedItem != null ? bin.acceptedItem.DisplayName : "Iron Ingot";
        if (bin.StoredCount <= 0)
            return $"SUBMISSION BIN | {bin.SubmittedCount} / {bin.targetCount} Submitted (0 in bin)";

        return $"Press [{{key}}] to take {itemName} ({bin.StoredCount} in bin)";
    }

    public override void Interact(FactoryPlayer player)
    {
        if (bin == null)
            bin = GetComponent<SubmissionBin>();

        if (bin != null && player != null)
            bin.TakeIngot(player);
    }
}
