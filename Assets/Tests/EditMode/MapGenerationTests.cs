using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class MapGenerationTests
{
    private MapGenerationSettings settings;

    [SetUp]
    public void SetUp()
    {
        settings = ScriptableObject.CreateInstance<MapGenerationSettings>();
        settings.heightmapPower = 6;
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(settings);

    [Test]
    public void SameSeedReproducesMapAndDifferentSeedChangesHills()
    {
        var first = MapHeightGenerator.Generate(settings);
        var repeat = MapHeightGenerator.Generate(settings);
        Assert.That(repeat, Is.EqualTo(first));
        settings.seed++;
        var changed = MapHeightGenerator.Generate(settings);
        Assert.That(changed, Is.Not.EqualTo(first));
    }

    [Test]
    public void ClearingRemainsAtZeroAndEntireHeightmapFitsTerrainRange()
    {
        var heights = MapHeightGenerator.Generate(settings);
        int resolution = settings.HeightmapResolution;
        float highest = 0f;
        for (int z = 0; z < resolution; z++)
        for (int x = 0; x < resolution; x++)
        {
            float height = heights[z, x];
            Assert.That(height, Is.InRange(0f, 1f));
            float worldHeight = settings.Origin.y + height * settings.verticalSize;
            highest = Mathf.Max(highest, worldHeight);
            float worldX = settings.Origin.x + x * settings.mapSize.x / (resolution - 1f);
            float worldZ = settings.Origin.z + z * settings.mapSize.y / (resolution - 1f);
            if (Mathf.Abs(worldX - settings.clearingCenter.x) <= settings.clearingSize.x / 2f
                && Mathf.Abs(worldZ - settings.clearingCenter.y) <= settings.clearingSize.y / 2f)
                Assert.That(worldHeight, Is.EqualTo(0f).Within(0.0001f));
        }
        Assert.That(highest, Is.GreaterThan(1f), "The surrounding map must actually have hills.");
        Assert.That(highest, Is.LessThanOrEqualTo(settings.hillHeight));
    }

    [Test]
    public void GenerationDoesNotChangeGameplayRandomSequence()
    {
        var original = Random.state;
        try
        {
            Random.InitState(42);
            var before = Random.state;
            MapHeightGenerator.Generate(settings);
            Assert.That(Random.state, Is.EqualTo(before));
        }
        finally
        {
            Random.state = original;
        }
    }

    [Test]
    public void InvalidMapSettingsAreRejected()
    {
        settings.hillHeight = settings.verticalSize + 1f;
        Assert.Throws<ArgumentException>(() => MapHeightGenerator.Generate(settings));
        settings.hillHeight = 14f;
        settings.clearingCenter = settings.mapSize;
        Assert.Throws<ArgumentException>(() => MapHeightGenerator.Generate(settings));
        settings.clearingCenter = Vector2.zero;
        settings.noiseScale = float.NaN;
        Assert.Throws<ArgumentException>(() => MapHeightGenerator.Generate(settings));
    }
}
