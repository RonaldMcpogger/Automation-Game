using System;
using UnityEngine;

/// <summary>Version-one finite map. All distances are metres in world space.</summary>
[CreateAssetMenu(fileName = "Map Settings", menuName = "Automation Game/Map Settings")]
public class MapGenerationSettings : ScriptableObject
{
    public const int GeneratorVersion = 1;

    [Header("Map")]
    public int seed = 104729;
    public Vector2 mapCenter = Vector2.zero;
    public Vector2 mapSize = new Vector2(256f, 256f);
    [Tooltip("Heightmap resolution is 2^power + 1. Eight gives 257 samples per side.")]
    [Range(5, 9)] public int heightmapPower = 8;
    [Min(1f)] public float verticalSize = 32f;
    [Tooltip("Heightmap space below world Y=0. The factory floor remains exactly at Y=0.")]
    [Min(0f)] public float depthBelowGround = 8f;

    [Header("Protected Factory Clearing")]
    public Vector2 clearingCenter = new Vector2(-8f, 0f);
    public Vector2 clearingSize = new Vector2(112f, 96f);
    [Min(1f)] public float clearingBlend = 24f;

    [Header("Hills")]
    [Min(0f)] public float hillHeight = 14f;
    [Min(1f)] public float noiseScale = 80f;
    [Range(1, 5)] public int octaves = 3;
    [Range(0.1f, 0.9f)] public float persistence = 0.5f;

    public int HeightmapResolution => (1 << heightmapPower) + 1;
    public Vector3 Origin => new Vector3(mapCenter.x - mapSize.x * 0.5f,
        -depthBelowGround, mapCenter.y - mapSize.y * 0.5f);

    public void Validate()
    {
        if (!Finite(mapCenter.x, mapCenter.y, mapSize.x, mapSize.y, verticalSize,
                depthBelowGround, clearingCenter.x, clearingCenter.y, clearingSize.x,
                clearingSize.y, clearingBlend, hillHeight, noiseScale, persistence))
            throw new ArgumentException("Map settings must contain finite numbers.");
        if (mapSize.x < 64f || mapSize.y < 64f || mapSize.x > 2048f || mapSize.y > 2048f
            || heightmapPower < 5 || heightmapPower > 9)
            throw new ArgumentException("Use a 64-2048 metre map and heightmap power 5-9.");
        if (depthBelowGround < 0f || hillHeight < 0f || verticalSize <= 0f
            || depthBelowGround + hillHeight > verticalSize)
            throw new ArgumentException("The heightmap must fit the clearing and the entire hill height.");
        if (clearingSize.x <= 0f || clearingSize.y <= 0f || clearingBlend < 1f
            || Mathf.Abs(clearingCenter.x - mapCenter.x) + clearingSize.x * 0.5f + clearingBlend > mapSize.x * 0.5f
            || Mathf.Abs(clearingCenter.y - mapCenter.y) + clearingSize.y * 0.5f + clearingBlend > mapSize.y * 0.5f)
            throw new ArgumentException("The clearing and its transition must fit inside the map.");
        if (noiseScale < 1f || octaves < 1 || octaves > 5 || persistence < 0.1f || persistence > 0.9f)
            throw new ArgumentException("Noise settings are outside their supported range.");
    }

    private static bool Finite(params float[] values)
    {
        foreach (float value in values)
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;
        return true;
    }
}
