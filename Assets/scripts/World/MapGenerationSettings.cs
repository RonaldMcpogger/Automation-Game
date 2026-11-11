using System;
using UnityEngine;

/// <summary>Seeded finite landscape and resource placement. All distances are world metres.</summary>
[CreateAssetMenu(fileName = "Map Settings", menuName = "Automation Game/Map Settings")]
public class MapGenerationSettings : ScriptableObject
{
    public const int GeneratorVersion = 3;

    [Header("Map")]
    public int seed = 104729;
    public Vector2 mapCenter = Vector2.zero;
    public Vector2 mapSize = new Vector2(1024f, 1024f);
    [Tooltip("Heightmap resolution is 2^power + 1. Eight gives 257 samples per side.")]
    [Range(5, 10)] public int heightmapPower = 9;
    [Min(1f)] public float verticalSize = 192f;
    [Tooltip("Heightmap space below world Y=0. The factory floor remains exactly at Y=0.")]
    [Min(0f)] public float depthBelowGround = 8f;

    [Header("Protected Factory Clearing")]
    public Vector2 clearingCenter = new Vector2(-8f, 0f);
    public Vector2 clearingSize = new Vector2(112f, 96f);
    [Min(1f)] public float clearingBlend = 24f;

    [Header("Hills")]
    [Min(0f)] public float hillHeight = 20f;
    [Min(1f)] public float noiseScale = 160f;
    [Range(1, 5)] public int octaves = 3;
    [Range(0.1f, 0.9f)] public float persistence = 0.5f;

    [Header("Mountain Regions")]
    [Range(0, 16)] public int mountainCount = 7;
    [Min(0f)] public float mountainHeight = 130f;
    [Min(20f)] public float mountainRadius = 150f;

    [Header("Resource Spots")]
    [Range(0, 256)] public int resourceSpotCount = 48;
    [Min(5f)] public float resourceSpacing = 65f;
    [Range(0f, 30f)] public float resourceMaxSlope = 16f;
    [Min(0f)] public float resourceMaxElevation = 45f;
    [Min(2f)] public float resourceEdgeMargin = 24f;
    [Tooltip("One guaranteed deposit inside the clearing, close to the existing player spawn.")]
    public Vector2 starterResourcePosition = new Vector2(-42f, 12f);

    public float DistanceFromClearing(Vector2 point) => Mathf.Max(
        Mathf.Abs(point.x - clearingCenter.x) - clearingSize.x * 0.5f,
        Mathf.Abs(point.y - clearingCenter.y) - clearingSize.y * 0.5f);

    public int HeightmapResolution => (1 << heightmapPower) + 1;
    public Vector3 Origin => new Vector3(mapCenter.x - mapSize.x * 0.5f,
        -depthBelowGround, mapCenter.y - mapSize.y * 0.5f);

    public void Validate()
    {
        if (!Finite(mapCenter.x, mapCenter.y, mapSize.x, mapSize.y, verticalSize,
                depthBelowGround, clearingCenter.x, clearingCenter.y, clearingSize.x,
                clearingSize.y, clearingBlend, hillHeight, noiseScale, persistence,
                mountainHeight, mountainRadius, resourceSpacing, resourceMaxSlope,
                resourceMaxElevation, resourceEdgeMargin, starterResourcePosition.x, starterResourcePosition.y))
            throw new ArgumentException("Map settings must contain finite numbers.");
        if (mapSize.x < 64f || mapSize.y < 64f || mapSize.x > 2048f || mapSize.y > 2048f
            || heightmapPower < 5 || heightmapPower > 10)
            throw new ArgumentException("Use a 64-2048 metre map and heightmap power 5-10.");
        if (depthBelowGround < 0f || hillHeight < 0f || verticalSize <= 0f
            || mountainHeight < 0f || depthBelowGround + hillHeight + (mountainCount > 0 ? mountainHeight : 0f) > verticalSize)
            throw new ArgumentException("The heightmap must fit the clearing and the entire hill height.");
        if (clearingSize.x <= 0f || clearingSize.y <= 0f || clearingBlend < 1f
            || Mathf.Abs(clearingCenter.x - mapCenter.x) + clearingSize.x * 0.5f + clearingBlend > mapSize.x * 0.5f
            || Mathf.Abs(clearingCenter.y - mapCenter.y) + clearingSize.y * 0.5f + clearingBlend > mapSize.y * 0.5f)
            throw new ArgumentException("The clearing and its transition must fit inside the map.");
        if (noiseScale < 1f || octaves < 1 || octaves > 5 || persistence < 0.1f || persistence > 0.9f)
            throw new ArgumentException("Noise settings are outside their supported range.");
        if (mountainCount < 0 || mountainCount > 16 || mountainRadius < 20f)
            throw new ArgumentException("Use 0-16 mountain regions with a radius of at least 20 metres.");
        if (resourceSpotCount < 0 || resourceSpotCount > 256 || resourceSpacing < 5f
            || resourceMaxSlope < 0f || resourceMaxSlope > 30f || resourceMaxElevation < 0f
            || resourceEdgeMargin < 2f || resourceEdgeMargin * 2f >= Mathf.Min(mapSize.x, mapSize.y))
            throw new ArgumentException("Resource placement settings are outside their supported range.");
        if (resourceSpotCount > 0 && DistanceFromClearing(starterResourcePosition) > -4f)
            throw new ArgumentException("Keep the starter resource at least four metres inside the factory clearing.");
    }

    private static bool Finite(params float[] values)
    {
        foreach (float value in values)
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;
        return true;
    }
}
