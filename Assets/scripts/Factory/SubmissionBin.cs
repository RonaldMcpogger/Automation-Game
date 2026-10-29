using UnityEngine;

public class SubmissionBin : ConveyorReceiver
{
    [Min(1)] public int targetCount = 10;
    public TextMesh counterLabel;
    public int SubmittedCount { get; private set; }
    public bool GoalReached => SubmittedCount >= targetCount;

    public override bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null || !body.TryGetComponent<FactoryItem>(out var item)
            || !item.TryMarkSubmitted())
            return false;

        SubmittedCount++;
        if (counterLabel != null)
            counterLabel.text = $"SUBMISSION BIN\n{SubmittedCount} / {targetCount} INGOTS"
                + (GoalReached ? "\nGOAL COMPLETE" : "");
        body.gameObject.SetActive(false);
        Destroy(body.gameObject);
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Also accepts a loose ingot that falls into the opening.
        TryReceive(other.attachedRigidbody);
    }
}
