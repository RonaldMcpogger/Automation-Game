using UnityEngine;

/// <summary>Pure height generation: no scene objects, assets, or Unity global random state.</summary>
public static class MapHeightGenerator
{
    public static float[,] Generate(MapGenerationSettings settings)
    {
        settings.Validate();
        int resolution = settings.HeightmapResolution;
        var heights = new float[resolution, resolution];
        var random = new System.Random(settings.seed);
        var offsets = new Vector2[settings.octaves];
        for (int octave = 0; octave < offsets.Length; octave++)
            offsets[octave] = new Vector2(random.Next(-10000, 10000), random.Next(-10000, 10000));

        Vector3 origin = settings.Origin;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            float worldX = origin.x + settings.mapSize.x * x / (resolution - 1f);
            float worldZ = origin.z + settings.mapSize.y * z / (resolution - 1f);
            float distance = Mathf.Max(
                Mathf.Abs(worldX - settings.clearingCenter.x) - settings.clearingSize.x * 0.5f,
                Mathf.Abs(worldZ - settings.clearingCenter.y) - settings.clearingSize.y * 0.5f);
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / settings.clearingBlend));
            float amplitude = 1f, frequency = 1f, noise = 0f, weight = 0f;
            for (int octave = 0; octave < offsets.Length; octave++)
            {
                noise += Mathf.PerlinNoise(worldX / settings.noiseScale * frequency + offsets[octave].x,
                    worldZ / settings.noiseScale * frequency + offsets[octave].y) * amplitude;
                weight += amplitude;
                amplitude *= settings.persistence;
                frequency *= 2f;
            }

            float elevation = Mathf.Clamp01(noise / weight) * settings.hillHeight * blend;
            // Native Terrain indexes height arrays [z,x]. A flat sample maps back to world Y=0.
            heights[z, x] = (settings.depthBelowGround + elevation) / settings.verticalSize;
        }
        return heights;
    }
}
