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

        // A separate stream keeps mountain layout independent of octave count.
        var mountainRandom = new System.Random(unchecked(settings.seed ^ 0x5a174d31));
        var mountains = new Vector4[settings.mountainCount];
        for (int i = 0; i < mountains.Length; i++)
        {
            Vector2 center = settings.mapCenter;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                center = settings.mapCenter + new Vector2(
                    ((float)mountainRandom.NextDouble() - 0.5f) * settings.mapSize.x * 0.8f,
                    ((float)mountainRandom.NextDouble() - 0.5f) * settings.mapSize.y * 0.8f);
                if (settings.DistanceFromClearing(center) > settings.clearingBlend + 30f)
                    break;
            }
            mountains[i] = new Vector4(center.x, center.y,
                settings.mountainRadius * Mathf.Lerp(0.8f, 1.25f, (float)mountainRandom.NextDouble()),
                Mathf.Lerp(0.8f, 1f, (float)mountainRandom.NextDouble()));
        }

        Vector3 origin = settings.Origin;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            float worldX = origin.x + settings.mapSize.x * x / (resolution - 1f);
            float worldZ = origin.z + settings.mapSize.y * z / (resolution - 1f);
            float distance = settings.DistanceFromClearing(new Vector2(worldX, worldZ));
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

            float mountain = 0f;
            foreach (var region in mountains)
            {
                float dx = (worldX - region.x) / region.z;
                float dz = (worldZ - region.y) / region.z;
                float radiusSquared = dx * dx + dz * dz;
                if (radiusSquared >= 1f)
                    continue;
                float envelope = 1f - radiusSquared;
                envelope *= envelope;
                float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(
                    worldX / 55f + offsets[0].x, worldZ / 55f + offsets[0].y) - 1f);
                // Max rather than sum prevents overlapping regions exceeding the configured height.
                mountain = Mathf.Max(mountain, envelope * Mathf.Lerp(0.55f, 1f, ridge) * region.w);
            }
            float elevation = (Mathf.Clamp01(noise / weight) * settings.hillHeight
                + mountain * settings.mountainHeight) * blend;
            // Native Terrain indexes height arrays [z,x]. A flat sample maps back to world Y=0.
            heights[z, x] = (settings.depthBelowGround + elevation) / settings.verticalSize;
        }
        return heights;
    }
}
