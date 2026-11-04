using NUnit.Framework;
using UnityEngine;

public class ResourcePlanningTests
{
    private MapGenerationSettings settings;
    private TerrainData terrain;

    [SetUp]
    public void SetUp()
    {
        settings = ScriptableObject.CreateInstance<MapGenerationSettings>();
        settings.heightmapPower = 8;
        terrain = new TerrainData { heightmapResolution = settings.HeightmapResolution,
            size = new Vector3(settings.mapSize.x, settings.verticalSize, settings.mapSize.y) };
        terrain.SetHeights(0, 0, MapHeightGenerator.Generate(settings));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(terrain);
        Object.DestroyImmediate(settings);
    }

    [Test]
    public void DepositsAreRepeatableSeparatedAndOnSuitableGround()
    {
        var first = MapResourcePlanner.Generate(settings, terrain);
        var repeat = MapResourcePlanner.Generate(settings, terrain);
        Assert.That(first, Is.EqualTo(repeat));
        Assert.That(first.Count, Is.EqualTo(settings.resourceSpotCount));
        Assert.That(new Vector2(first[0].position.x, first[0].position.z), Is.EqualTo(settings.starterResourcePosition));
        for (int i = 0; i < first.Count; i++)
        {
            var point = new Vector2(first[i].position.x, first[i].position.z);
            Assert.That(MapResourcePlanner.IsSuitable(settings, terrain, point), Is.True);
            Assert.That(point.x, Is.InRange(settings.Origin.x + settings.resourceEdgeMargin,
                settings.Origin.x + settings.mapSize.x - settings.resourceEdgeMargin));
            if (i > 0)
                Assert.That(settings.DistanceFromClearing(point), Is.GreaterThanOrEqualTo(settings.clearingBlend));
            for (int j = 0; j < i; j++)
                Assert.That(Vector2.Distance(point, new Vector2(first[j].position.x, first[j].position.z)),
                    Is.GreaterThanOrEqualTo(settings.resourceSpacing));
        }
    }

    [Test]
    public void ImpossibleDensityTerminatesWithoutCrowdingAndZeroCountDisablesScatter()
    {
        settings.resourceSpacing = 2000f;
        settings.resourceSpotCount = 3;
        Assert.That(MapResourcePlanner.Generate(settings, terrain).Count, Is.EqualTo(1));
        settings.resourceSpotCount = 0;
        Assert.That(MapResourcePlanner.Generate(settings, terrain), Is.Empty);
    }
}
