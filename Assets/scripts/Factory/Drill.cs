using System;
using UnityEngine;

/// <summary>
/// Automatically extracts ore from a resource spot (or default ore) and
/// outputs directly to a connected conveyor belt or internal storage hopper.
/// </summary>
public class Drill : FactoryMachine
{
    [Header("Drill Components")]
    public Transform drillBit;
    public Transform outputPort;
    [Tooltip("Degrees per second the drill bit spins when actively mining.")]
    public float drillSpinSpeed = 360f;

    [Header("Resource Settings")]
    public Resource minedResource;
    public ResourceSpot targetSpot;
    public float spotCheckRadius = 4f;
    public bool autoMineWithoutSpot = true;

    private bool initialized;

    protected override void Awake()
    {
        base.Awake();
        InitReferences();
    }

    public void InitReferences()
    {
        if (initialized && drillBit != null && outputPort != null)
            return;

        if (factoryData == null)
            factoryData = Resources.Load<FactoryData>("FactoryData/Drill");
        ApplyFactoryData();

        if (drillBit == null)
            drillBit = transform.Find("DrillBit") ?? transform.Find("Cone");

        if (outputPort == null)
        {
            outputPort = transform.Find("Output");
            if (outputPort == null)
            {
                var outObj = new GameObject("Output");
                outObj.transform.SetParent(transform, false);
                outObj.transform.localPosition = new Vector3(0f, 0.83f, 1.2f);
                outputPort = outObj.transform;
            }
        }

        if (statusLight == null)
        {
            var lightObj = transform.Find("Status Light") ?? transform.Find("Drill Status Light");
            if (lightObj != null)
                statusLight = lightObj.GetComponent<Renderer>();
        }

        var interactable = GetComponent<Interactable>();
        if (interactable == null)
        {
            interactable = gameObject.AddComponent<Interactable>();
            interactable.SetInteractable(true);
        }

        DetectResourceSpot();
        initialized = true;
    }

    public void DetectResourceSpot()
    {
        // 1. Raycast down to check if placed directly on top of a ResourceSpot
        if (Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out var hit, 5f))
        {
            var spot = hit.collider.GetComponentInParent<ResourceSpot>();
            if (spot != null)
            {
                targetSpot = spot;
                minedResource = spot.resourceData;
                return;
            }
        }

        // 2. OverlapSphere to find closest ResourceSpot nearby
        Collider[] hits = Physics.OverlapSphere(transform.position, spotCheckRadius);
        float bestDist = float.MaxValue;
        ResourceSpot closest = null;
        foreach (var col in hits)
        {
            var spot = col.GetComponentInParent<ResourceSpot>();
            if (spot != null)
            {
                float dist = Vector3.Distance(transform.position, spot.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    closest = spot;
                }
            }
        }

        if (closest != null)
        {
            targetSpot = closest;
            minedResource = closest.resourceData;
            return;
        }

        // 3. Fallback to factoryData output item or standard iron ore
        if (minedResource == null)
        {
            if (factoryData != null && factoryData.outputItem != null)
                minedResource = factoryData.outputItem;
            else
            {
                var smelter = Resources.Load<FactoryData>("FactoryData/Smelter");
                minedResource = smelter != null ? smelter.inputItem : null;
            }
        }
    }

    public string ResourceName => minedResource != null ? minedResource.DisplayName : "Iron Ore";
    public override Resource OutputItem => minedResource != null ? minedResource : base.OutputItem;

    public override string Status
    {
        get
        {
            if (BufferedOre >= inputCapacity && (outputBelt == null || !outputBelt.CanLoadAtStart))
                return $"Storage full ({BufferedOre}/{inputCapacity}) | Belt blocked";
            if (IsProcessing)
                return $"Drilling {ResourceName} {Mathf.Clamp01(elapsed / processingSeconds):P0} | Stored: {BufferedOre}";
            return $"Drill ready | Stored: {BufferedOre}";
        }
    }

    public override string GetPrompt(FactoryPlayer player)
    {
        if (BufferedOre > 0)
            return $"Press [{{key}}] to collect {BufferedOre}x {ResourceName}";
        if (IsProcessing)
            return $"DRILL - Mining {ResourceName}... ({Mathf.Clamp01(elapsed / processingSeconds):P0})";
        return "DRILL - Storage full / Output blocked";
    }

    public override void Interact(FactoryPlayer player)
    {
        if (player == null || BufferedOre <= 0)
            return;

        Resource toGive = OutputItem;
        int toTake = BufferedOre;
        int collected = 0;
        for (int i = 0; i < toTake; i++)
        {
            if (player.TryCollect(toGive, 1))
                collected++;
            else
                break;
        }
        BufferedOre -= collected;
    }

    protected override void Update()
    {
        if (minedResource == null)
            DetectResourceSpot();

        bool beltCanAccept = outputBelt != null && outputBelt.CanLoadAtStart;
        bool bufferHasRoom = BufferedOre < inputCapacity;
        bool canMine = (targetSpot != null || autoMineWithoutSpot) && (beltCanAccept || bufferHasRoom);

        if (canMine)
        {
            IsProcessing = true;
            if (drillBit != null)
                drillBit.Rotate(Vector3.up, drillSpinSpeed * Time.deltaTime, Space.Self);

            elapsed += Time.deltaTime;
            if (elapsed >= processingSeconds)
            {
                elapsed = 0f;
                if (beltCanAccept && TryLoadBelt(outputBelt))
                {
                    ProducedCount++;
                }
                else if (bufferHasRoom)
                {
                    BufferedOre++;
                    ProducedCount++;
                }
            }
        }
        else
        {
            IsProcessing = false;
        }

        // Also unload any buffered ore if conveyor becomes ready
        if (BufferedOre > 0 && outputBelt != null && outputBelt.CanLoadAtStart)
        {
            if (TryLoadBelt(outputBelt))
                BufferedOre--;
        }

        if (statusLight != null)
        {
            statusLight.sharedMaterial = (BufferedOre >= inputCapacity && !beltCanAccept)
                ? blockedMaterial
                : IsProcessing ? workingMaterial : idleMaterial;
        }
    }

    private bool TryLoadBelt(Conveyor belt)
    {
        if (belt == null || !belt.CanLoadAtStart)
            return false;

        GameObject prefabObj = OutputPrefab != null ? OutputPrefab.gameObject
            : (OutputItem != null && OutputItem.objModel != null ? OutputItem.objModel : null);

        GameObject spawned;
        if (prefabObj != null)
        {
            spawned = Instantiate(prefabObj, belt.StartPosition, Quaternion.identity);
        }
        else
        {
            spawned = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.transform.localScale = Vector3.one * 0.4f;
        }

        var item = spawned.GetComponent<FactoryItem>() ?? spawned.AddComponent<FactoryItem>();
        item.itemData = OutputItem;
        item.material = FactoryItem.MaterialKind.IronOre;

        var body = spawned.GetComponent<Rigidbody>() ?? spawned.AddComponent<Rigidbody>();
        if (belt.TryLoadAtStart(body, beltSurfaceHeight))
        {
            return true;
        }
        else
        {
            spawned.SetActive(false);
            Destroy(spawned);
            return false;
        }
    }
}
