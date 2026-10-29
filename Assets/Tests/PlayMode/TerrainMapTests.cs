using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class TerrainMapTests
{
    private TerrainMapGenerator map;

    [UnitySetUp]
    public IEnumerator LoadMap()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;
        map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        Assert.That(map, Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator SavedMapHasWalkableClearingAndPreservesFactory()
    {
        var terrain = map.Terrain;
        Assert.That(Object.FindObjectsByType<Terrain>().Length, Is.EqualTo(1));
        Assert.That(terrain.terrainData.heightmapResolution, Is.EqualTo(257));
        Assert.That(map.GetComponent<TerrainCollider>().terrainData, Is.SameAs(terrain.terrainData));
        var legacy = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Single(x => x.name == "Land");
        Assert.That(legacy.gameObject.activeSelf, Is.False, "The old ground is preserved without a duplicate collider.");
        var demoRoot = Object.FindAnyObjectByType<FactoryPlayer>().machine.transform.parent;
        var stations = demoRoot.GetComponentsInChildren<FactoryInteractable>(true);
        Assert.That(stations.Length, Is.EqualTo(2), "Both preserved demo stations must be checked, even while inactive.");
        foreach (var factory in stations)
        {
            Assert.That(map.Contains(factory.transform.position), Is.True);
            Assert.That(terrain.SampleHeight(factory.transform.position) + terrain.transform.position.y,
                Is.EqualTo(0f).Within(0.01f), "Existing factory stations must keep their flat ground.");
        }
        Assert.That(Physics.Raycast(new Vector3(-50, 30, 20), Vector3.down, out var hit, 40f), Is.True);
        Assert.That(hit.collider, Is.TypeOf<TerrainCollider>());
        Assert.That(hit.point.y, Is.EqualTo(0f).Within(0.01f));
        yield return new WaitForSeconds(0.1f);
        Assert.That(Object.FindAnyObjectByType<FactoryPlayer>().transform.position.y, Is.GreaterThan(0.5f));
    }

    [UnityTest]
    public IEnumerator ElevatedGroundSupportsBuildingAndHeightRelativeSnapping()
    {
        Assert.That(Physics.Raycast(new Vector3(80, 40, 60), Vector3.down, out var hit, 50f), Is.True);
        Assert.That(hit.collider, Is.TypeOf<TerrainCollider>());
        Assert.That(hit.point.y, Is.GreaterThan(0.5f));
        var builder = Object.FindAnyObjectByType<BuildController>();
        var machine = builder.BuildSmelter(hit.point, Quaternion.identity);
        var port = hit.point + Vector3.forward * 1.8f + Vector3.up * builder.conveyorHeightOffset;
        builder.SetSelectedType(BuildableType.ConveyorBelt);
        Assert.That(builder.TryGetSnapTarget(true, port, out var snap), Is.True);
        Assert.That(snap.machine, Is.SameAs(machine));
        Assert.That(snap.position.y, Is.EqualTo(port.y).Within(0.01f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator FiniteMapHasBoundaryCollidersAndCorrectWorldBounds()
    {
        Assert.That(map.Contains(Vector3.zero), Is.True);
        Assert.That(map.Contains(new Vector3(200, 0, 0)), Is.False);
        var boundaries = map.transform.Find("Map Bounds").GetComponentsInChildren<BoxCollider>();
        Assert.That(boundaries.Length, Is.EqualTo(4));
        Assert.That(Physics.Raycast(new Vector3(-126, 20, 0), Vector3.left, out var hit, 5f, 1 << 2), Is.True);
        Assert.That(hit.collider.name, Is.EqualTo("West"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator RuntimeRegenerationLeavesBakedMapAndSettingsUntouched()
    {
        var baked = map.Terrain.terrainData;
        float bakedCorner = baked.GetHeight(0, 0);
        var originalSettings = map.settings;
        var runtimeSettings = Object.Instantiate(originalSettings);
        try
        {
            runtimeSettings.seed++;
            map.settings = runtimeSettings;
            map.RegenerateRuntime();
            Assert.That(map.Terrain.terrainData, Is.Not.SameAs(baked));
            Assert.That(baked.GetHeight(0, 0), Is.EqualTo(bakedCorner));
            Assert.That(originalSettings.seed, Is.Not.EqualTo(runtimeSettings.seed));
        }
        finally
        {
            map.settings = originalSettings;
            Object.Destroy(runtimeSettings);
        }
        yield return null;
    }
}
