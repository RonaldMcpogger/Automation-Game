using UnityEngine;

/// <summary>One inexhaustible ore spot for the weekly prototype.</summary>
public class ResourceSpot : FactoryInteractable
{
    [Min(0.05f)] public float gatherInterval = 0.4f;
    private float nextGatherTime;

    public override bool HoldToInteract => true;

    public override string GetPrompt(FactoryPlayer player) => player.OreCount >= player.oreCapacity
        ? "Ore pouch full - load the smelter"
        : "Hold [{key}] to gather iron ore";

    public override void Interact(FactoryPlayer player)
    {
        if (player == null || !isActiveAndEnabled || Time.time < nextGatherTime)
            return;
        if (player.TryCollectOre())
            nextGatherTime = Time.time + gatherInterval;
    }
}
