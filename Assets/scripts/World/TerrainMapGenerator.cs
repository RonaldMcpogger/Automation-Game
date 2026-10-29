using System;
using UnityEngine;

/// <summary>One native Unity Terrain. Baked in the scene; runtime regeneration uses a private copy.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
public class TerrainMapGenerator : MonoBehaviour
{
    public MapGenerationSettings settings;
    [Tooltip("Grass first, rock second. Both are authored project assets.")]
    public TerrainLayer[] surfaceLayers;
    public Material terrainMaterial;
    public bool generateOnAwake;
    private TerrainData runtimeData;

    public Terrain Terrain => GetComponent<Terrain>();

    private void Awake()
    {
        if (generateOnAwake || Terrain.terrainData == null)
            RegenerateRuntime();
    }

    /// <summary>Does not modify the saved terrain asset, even when called in Editor Play Mode.</summary>
    public void RegenerateRuntime()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Use the map inspector's Regenerate button outside Play Mode.");
        var data = new TerrainData { name = "Generated Map (runtime)" };
        try
        {
            GenerateInto(data);
        }
        catch
        {
            Destroy(data);
            throw;
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
        // Validate before touching the previous map, so invalid settings leave it intact.
        float[,] heights = MapHeightGenerator.Generate(settings);
        data.heightmapResolution = settings.HeightmapResolution;
        data.size = new Vector3(settings.mapSize.x, settings.verticalSize, settings.mapSize.y);
        data.SetHeights(0, 0, heights);
        if (surfaceLayers != null && surfaceLayers.Length == 2 && surfaceLayers[0] != null && surfaceLayers[1] != null)
        {
            data.terrainLayers = surfaceLayers;
            data.alphamapResolution = 128;
            var weights = new float[128, 128, 2];
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
            {
                float rock = Mathf.InverseLerp(14f, 32f, data.GetSteepness(x / 127f, z / 127f));
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
