using System;
using UnityEngine;

/// <summary>One native Unity Terrain. Baked in the scene; runtime regeneration uses a private copy.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
public class TerrainMapGenerator : MonoBehaviour
{
    [Serializable]
    public struct DepositVariant
    {
        public ResourceSpot prefab;
        [Range(1, 100)] public int weight;
    }

    public MapGenerationSettings settings;
    [Tooltip("Grass first, rock second. Both are authored project assets.")]
    public TerrainLayer[] surfaceLayers;
    public Material terrainMaterial;
    public bool generateOnAwake;
    [Header("Generated Resource Spots")]
    [Tooltip("This deposit always appears in the starter clearing.")]
    public ResourceSpot resourceSpotPrefab;
    [Range(1, 100)] public int defaultDepositWeight = 3;
    [Tooltip("Alternative deposits and their relative shares outside the starter clearing.")]
    public DepositVariant[] additionalDeposits;
    [SerializeField] private Transform generatedResources;
    [SerializeField] private int generatedSeed;
    private TerrainData runtimeData;

    public Terrain Terrain => GetComponent<Terrain>();
    public Transform GeneratedResources => generatedResources;
    public int GeneratedSeed => generatedSeed;

    private void Awake()
    {
        if (generateOnAwake || Terrain.terrainData == null)
            RegenerateRuntime();
    }

    /// <summary>Does not modify the saved terrain asset, even when called in Editor Play Mode.</summary>
    public void RegenerateRuntime(bool newRandomSeed = false)
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Use the map inspector's Regenerate button outside Play Mode.");
        var data = new TerrainData { name = "Generated Map (runtime)" };
        var originalSettings = settings;
        try
        {
            if (newRandomSeed)
            {
                settings = Instantiate(originalSettings);
                settings.seed = Guid.NewGuid().GetHashCode();
            }
            GenerateInto(data);
        }
        catch
        {
            Destroy(data);
            throw;
        }
        finally
        {
            if (settings != originalSettings)
            {
                Destroy(settings);
                settings = originalSettings;
            }
        }
        var previous = runtimeData;
        runtimeData = data;
        if (previous != null)
            Destroy(previous);
    }

    /// <summary>Editor tooling supplies the owned terrain asset; runtime supplies a new instance.</summary>
    public void GenerateInto(TerrainData data)
    {
        if (settings == null || data == null)
            throw new InvalidOperationException("Assign map settings and a destination TerrainData.");
        ValidateDepositPrefabs();
        // Validate before touching the previous map, so invalid settings leave it intact.
        float[,] heights = MapHeightGenerator.Generate(settings);
        data.heightmapResolution = settings.HeightmapResolution;
        data.size = new Vector3(settings.mapSize.x, settings.verticalSize, settings.mapSize.y);
        data.SetHeights(0, 0, heights);
        if (surfaceLayers != null && surfaceLayers.Length == 2 && surfaceLayers[0] != null && surfaceLayers[1] != null)
        {
            data.terrainLayers = surfaceLayers;
            int resolution = settings.mapSize.x > 256f || settings.mapSize.y > 256f ? 256 : 128;
            data.alphamapResolution = resolution;
            var weights = new float[resolution, resolution, 2];
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (resolution - 1f), v = z / (resolution - 1f);
                float rock = Mathf.Max(Mathf.InverseLerp(18f, 38f, data.GetSteepness(u, v)),
                    Mathf.InverseLerp(45f, 100f, data.GetInterpolatedHeight(u, v) + settings.Origin.y));
                weights[z, x, 0] = 1f - rock;
                weights[z, x, 1] = rock;
            }
            data.SetAlphamaps(0, 0, weights);
        }

        transform.SetPositionAndRotation(settings.Origin, Quaternion.identity);
        transform.localScale = Vector3.one;
        Terrain.terrainData = data;
        GetComponent<TerrainCollider>().terrainData = data;
        Terrain.materialTemplate = terrainMaterial;
        Terrain.drawInstanced = true;
        Terrain.heightmapPixelError = 5f;
        Terrain.basemapDistance = 500f;
        Terrain.Flush();
        ConfigureBoundaries(data.size);
        generatedSeed = settings.seed;
        ScatterResources(data);
    }

    /// <summary>Refreshes deposits using the existing baked terrain and its seed.</summary>
    public void RegenerateResourceSpots()
    {
        var data = Terrain.terrainData;
        if (settings == null || data == null || generatedSeed != settings.seed
            || transform.position != settings.Origin
            || data.size != new Vector3(settings.mapSize.x, settings.verticalSize, settings.mapSize.y))
            throw new InvalidOperationException("Regenerate the terrain before refreshing deposits for these map settings.");

        ValidateDepositPrefabs();
        ScatterResources(data);
    }

    private void ValidateDepositPrefabs()
    {
        if (settings.resourceSpotCount == 0)
            return;
        if (resourceSpotPrefab == null)
            throw new InvalidOperationException("Assign a resource spot prefab before generating this map.");
        if (additionalDeposits == null || additionalDeposits.Length == 0)
            return;
        if (defaultDepositWeight < 1 || defaultDepositWeight > 100)
            throw new InvalidOperationException("Use a deposit weight between 1 and 100.");

        foreach (var variant in additionalDeposits)
            if (variant.prefab == null || variant.prefab.resourceData == null || variant.weight < 1 || variant.weight > 100)
                throw new InvalidOperationException("Each deposit variant needs a prefab with a resource and a weight between 1 and 100.");
    }

    private ResourceSpot[] ChooseDepositPrefabs(int count)
    {
        var chosen = new ResourceSpot[count];
        if (count == 0)
            return chosen;

        chosen[0] = resourceSpotPrefab;
        int variantCount = additionalDeposits?.Length ?? 0;
        if (variantCount == 0)
        {
            Array.Fill(chosen, resourceSpotPrefab);
            return chosen;
        }

        var weights = new int[variantCount + 1];
        weights[0] = defaultDepositWeight;
        int totalWeight = defaultDepositWeight;
        for (int i = 0; i < variantCount; i++)
        {
            weights[i + 1] = additionalDeposits[i].weight;
            totalWeight += additionalDeposits[i].weight;
        }

        // Allocate in proportion to the weights, then shuffle assignments across suitable sites.
        var credits = new int[weights.Length];
        for (int slot = 1; slot < count; slot++)
        {
            int best = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                credits[i] += weights[i];
                if (credits[i] > credits[best])
                    best = i;
            }
            credits[best] -= totalWeight;
            chosen[slot] = best == 0 ? resourceSpotPrefab : additionalDeposits[best - 1].prefab;
        }

        // Keep assignment independent of the random stream that places the rocks.
        var random = new System.Random(unchecked(settings.seed ^ 0x4e1dc36a));
        for (int i = count - 1; i > 1; i--)
        {
            int other = random.Next(1, i + 1);
            (chosen[i], chosen[other]) = (chosen[other], chosen[i]);
        }
        return chosen;
    }

    private void ScatterResources(TerrainData data)
    {
        // Only replace this generator's own group; player buildings and the demo are untouched.
        var placements = MapResourcePlanner.Generate(settings, data);
        var prefabs = ChooseDepositPrefabs(placements.Count);
        if (generatedResources != null)
        {
            generatedResources.gameObject.SetActive(false);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.DestroyObjectImmediate(generatedResources.gameObject);
            else
#endif
                Destroy(generatedResources.gameObject);
        }
        generatedResources = new GameObject("Generated Resource Spots").transform;
        generatedResources.SetParent(transform, false);
        for (int i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            ResourceSpot spot;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                spot = ((GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefabs[i].gameObject,
                    generatedResources)).GetComponent<ResourceSpot>();
            else
#endif
                spot = Instantiate(prefabs[i], generatedResources);
            spot.transform.SetPositionAndRotation(placement.position, Quaternion.Euler(0, placement.yaw, 0));
            spot.transform.localScale = Vector3.one * placement.scale;
            spot.name = i == 0 ? $"Starter {spot.ResourceName}" : $"{spot.ResourceName} Deposit";
            // Seat each base rock on the terrain; the mineral pieces stay attached to the central rock.
            foreach (Transform part in spot.transform)
            {
                if (!part.name.Contains("Rock"))
                    continue;
                Vector3 position = part.position;
                position.y = Terrain.SampleHeight(position) + transform.position.y + 0.03f;
                part.position = position;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(part);
#endif
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(spot.transform);
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(spot.gameObject);
            }
#endif
        }
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.Undo.RegisterCreatedObjectUndo(generatedResources.gameObject, "Scatter map resources");
#endif
        if (placements.Count < settings.resourceSpotCount)
            Debug.LogWarning($"Placed {placements.Count}/{settings.resourceSpotCount} deposits. "
                + "Suitable accessible ground was exhausted; reduce spacing or the requested count.", this);
    }

    private void ConfigureBoundaries(Vector3 size)
    {
        var root = transform.Find("Map Bounds");
        if (root == null)
        {
            root = new GameObject("Map Bounds").transform;
            root.SetParent(transform, false);
        }
        // These only block walking off the finite map; they are excluded from building raycasts.
        SetBoundary(root, "West", new Vector3(-0.5f, size.y * 0.5f, size.z * 0.5f), new Vector3(1f, size.y, size.z + 2f));
        SetBoundary(root, "East", new Vector3(size.x + 0.5f, size.y * 0.5f, size.z * 0.5f), new Vector3(1f, size.y, size.z + 2f));
        SetBoundary(root, "South", new Vector3(size.x * 0.5f, size.y * 0.5f, -0.5f), new Vector3(size.x + 2f, size.y, 1f));
        SetBoundary(root, "North", new Vector3(size.x * 0.5f, size.y * 0.5f, size.z + 0.5f), new Vector3(size.x + 2f, size.y, 1f));
    }

    private static void SetBoundary(Transform root, string name, Vector3 position, Vector3 size)
    {
        var wall = root.Find(name);
        if (wall == null)
        {
            wall = new GameObject(name, typeof(BoxCollider)).transform;
            wall.SetParent(root, false);
        }
        wall.gameObject.layer = 2; // Unity's built-in Ignore Raycast layer.
        wall.localPosition = position;
        wall.GetComponent<BoxCollider>().size = size;
    }

    public bool Contains(Vector3 worldPosition)
    {
        var data = Terrain.terrainData;
        if (data == null)
            return false;
        Vector3 local = worldPosition - transform.position;
        return local.x >= 0f && local.z >= 0f && local.x <= data.size.x && local.z <= data.size.z;
    }

    private void OnDestroy()
    {
        if (runtimeData != null)
            Destroy(runtimeData);
    }

    private void OnDrawGizmosSelected()
    {
        if (settings == null)
            return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(new Vector3(settings.clearingCenter.x, 0f, settings.clearingCenter.y),
            new Vector3(settings.clearingSize.x, 0.1f, settings.clearingSize.y));
    }
}
