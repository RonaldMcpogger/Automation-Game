using UnityEngine;

/// <summary>One ore becomes one ingot. A blocked output retains its finished ingot.</summary>
public class FactoryMachine : FactoryInteractable
{
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

    public int BufferedOre { get; private set; }
    public bool IsProcessing { get; private set; }
    public bool OutputReady { get; private set; }
    public int ProducedCount { get; private set; }
    private float elapsed;

    public string Status => outputBelt == null || ingotPrefab == null ? "Output not configured"
        : OutputReady ? $"Output blocked - waiting | Ore queued: {BufferedOre}"
        : IsProcessing ? $"Smelting {Mathf.Clamp01(elapsed / processingSeconds):P0} | Ore queued: {BufferedOre}"
        : $"Waiting for ore | Ore queued: {BufferedOre}";

    public override string GetPrompt(FactoryPlayer player) => player.OreCount == 0
        ? "SMELTER - gather iron ore at the resource spot"
        : BufferedOre >= inputCapacity ? "SMELTER - input full"
        : "Press [{key}] to load ore into the smelter";

    public override void Interact(FactoryPlayer player) => LoadFrom(player);

    public int LoadFrom(FactoryPlayer player)
    {
        if (!isActiveAndEnabled || player == null)
            return 0;
        int loaded = player.TakeOre(Mathf.Max(0, inputCapacity - BufferedOre));
        BufferedOre += loaded;
        return loaded;
    }

    private void Update()
    {
        if (outputBelt == null || ingotPrefab == null)
            return;

        if (OutputReady)
        {
            if (outputBelt.CanLoadAtStart)
            {
                var item = Instantiate(ingotPrefab, outputBelt.StartPosition, Quaternion.identity);
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
