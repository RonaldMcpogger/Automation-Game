using UnityEngine;

/// <summary>One ore becomes one ingot. A blocked output retains its finished ingot.</summary>
public class FactoryMachine : FactoryInteractable
{
    [Tooltip("Recipe and presentation data for this machine.")]
    public FactoryData factoryData;
    public Conveyor outputBelt;
    public FactoryItem ingotPrefab;
    public Renderer statusLight;
    public Material idleMaterial;
    public Material workingMaterial;
    public Material blockedMaterial;
    [Min(1)] public int inputCapacity = 10;
    [Min(0.05f)] public float processingSeconds = 2f;
    [Tooltip("Height of the existing belt's surface above its spline center.")]
    public float beltSurfaceHeight = 0.83f;

    public int BufferedOre { get; protected set; }
    public bool IsProcessing { get; protected set; }
    public bool OutputReady { get; protected set; }
    public int ProducedCount { get; protected set; }
    protected float elapsed;

    protected virtual void Awake()
    {
        ApplyFactoryData();
        EnsureReceiver();
    }

    public void EnsureReceiver()
    {
        if (InputItem == null)
            return;
        Transform inputPort = transform.Find("Input");
        GameObject target = inputPort != null ? inputPort.gameObject : gameObject;
        if (target.GetComponent<ConveyorReceiver>() == null)
        {
            var mr = target.AddComponent<MachineReceiver>();
            mr.machine = this;
        }
    }

    public virtual void ApplyFactoryData()
    {
        if (factoryData == null)
            return;
        inputCapacity = factoryData.inputCapacity;
        processingSeconds = factoryData.processingSeconds;
    }

    public virtual Resource InputItem => factoryData != null ? factoryData.inputItem : null;
    public virtual Resource OutputItem => factoryData != null ? factoryData.outputItem : null;
    public virtual FactoryItem OutputPrefab => OutputItem != null && OutputItem.objModel != null
        ? OutputItem.objModel.GetComponent<FactoryItem>() : ingotPrefab;

    public virtual string Status => outputBelt == null || OutputPrefab == null ? "Output not configured"
        : OutputReady ? $"Output blocked - waiting | Ore queued: {BufferedOre}"
        : IsProcessing ? $"Smelting {Mathf.Clamp01(elapsed / processingSeconds):P0} | Ore queued: {BufferedOre}"
        : $"Waiting for ore | Ore queued: {BufferedOre}";

    // / <summary>Returns the prompt to show on the UI for this machine.</summary>
    public override string GetPrompt(FactoryPlayer player) => player.GetItemCount(InputItem) == 0
        ? $"{factoryData?.DisplayName ?? "SMELTER"} - gather {InputItem?.DisplayName ?? "iron ore"} at the resource spot"
        : BufferedOre >= inputCapacity ? "SMELTER - input full"
        : "Press [{key}] to load ore into the smelter";

    public override void Interact(FactoryPlayer player) => LoadFrom(player);

    public virtual int LoadFrom(FactoryPlayer player)
    {
        if (!isActiveAndEnabled || player == null)
            return 0;
        int loaded = player.TakeItem(InputItem, Mathf.Max(0, inputCapacity - BufferedOre));
        BufferedOre += loaded;
        return loaded;
    }

    public virtual bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null)
            return false;
        if (BufferedOre >= inputCapacity)
            return false;

        if (!body.TryGetComponent<FactoryItem>(out var item)
            || (InputItem != null && !item.Matches(InputItem)))
            return false;

        BufferedOre++;
        body.gameObject.SetActive(false);
        Destroy(body.gameObject);
        return true;
    }

    protected virtual void Update()
    {
        FactoryItem outputPrefab = OutputPrefab;
        if (outputBelt == null || outputPrefab == null)
            return;

        if (OutputReady)
        {
            if (outputBelt.CanLoadAtStart)
            {
                var item = Instantiate(outputPrefab, outputBelt.StartPosition, Quaternion.identity);
                item.itemData = OutputItem;
                var body = item.GetComponent<Rigidbody>();
                if (outputBelt.TryLoadAtStart(body, beltSurfaceHeight))
                {
                    OutputReady = false;
                    ProducedCount++;
                }
                else
                {
                    item.gameObject.SetActive(false);
                    Destroy(item.gameObject);
                }
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
}

/// <summary>Allows any FactoryMachine to receive inputs from conveyor belts.</summary>
public class MachineReceiver : ConveyorReceiver
{
    public FactoryMachine machine;

    private void Awake()
    {
        if (machine == null)
            machine = GetComponentInParent<FactoryMachine>();
    }

    public override bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null)
            return false;
        if (machine == null)
            machine = GetComponentInParent<FactoryMachine>();
        return machine != null && machine.TryReceive(body);
    }
}
