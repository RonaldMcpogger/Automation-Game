using UnityEngine;

/// <summary>One inexhaustible ore spot for the weekly prototype.</summary>
///  Resource spot to farm manually, or drill factory
public class ResourceSpot : FactoryInteractable
{
    [Tooltip("The item definition gathered from this spot.")]
    public Resource resourceData; // resource to give or generate
    [Min(0.05f)] public float gatherInterval = 0.4f;
    private float nextGatherTime;

    public override bool HoldToInteract => true;

    private void Awake()
    {
        EnsureResourceData();
    }

    private void EnsureResourceData()
    {
        if (resourceData == null)
        {
            var smelter = Resources.Load<FactoryData>("FactoryData/Smelter");
            if (smelter != null && smelter.inputItem != null)
                resourceData = smelter.inputItem;
            else
            {
                var spotData = Resources.Load<FactoryData>("FactoryData/IronOreSpot");
                if (spotData != null && spotData.outputItem != null)
                    resourceData = spotData.outputItem;
            }
        }
    }

    public string ResourceName
    {
        get
        {
            EnsureResourceData();
            return resourceData != null ? resourceData.DisplayName : "Iron Ore";
        }
    }

    public override string GetPrompt(FactoryPlayer player) // write prompt to show on ui 
    {
        if (player == null)
            return string.Empty;

        return player.TotalCarriedCount >= player.oreCapacity
            ? "Ore pouch full - load the smelter [E] or build [B]"
            : $"Hold [{{key}}] to gather {ResourceName}";
    }

    public override void Interact(FactoryPlayer player)
    {
        if (player == null || !isActiveAndEnabled || Time.time < nextGatherTime)
            return;

        EnsureResourceData();
        if (Inventory.Instance != null && Inventory.Instance.TryAdd(resourceData))
        {
            Debug.Log("picked up+" + resourceData.DisplayName);
            if (player.TryCollect(resourceData)) // remove in the future as more items are added
                nextGatherTime = Time.time + gatherInterval;
        }
    }
}
