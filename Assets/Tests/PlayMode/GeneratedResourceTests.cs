using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class GeneratedResourceTests
{
    [UnityTest]
    public IEnumerator StarterDepositSuppliesTheSameOreThatTheSmelterAccepts()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;
        var map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        Assert.That(map.GeneratedResources.childCount, Is.EqualTo(map.settings.resourceSpotCount));
        var starter = map.GeneratedResources.GetChild(0).GetComponent<ResourceSpot>();
        Assert.That(starter.resourceData, Is.Not.Null);
        var player = Object.FindAnyObjectByType<FactoryPlayer>();
        starter.Interact(player);
        Assert.That(player.OreCount, Is.EqualTo(1));
        Assert.That(player.CarriedItem, Is.SameAs(starter.resourceData));
        var builder = player.GetComponent<BuildController>();
        var smelter = builder.BuildSmelter(starter.transform.position + Vector3.right * 5f, Quaternion.identity);
        Assert.That(smelter.LoadFrom(player), Is.EqualTo(1));
        Assert.That(player.OreCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator RepeatedRegenerationReplacesOnlyGeneratedResources()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;
        var map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        var builder = Object.FindAnyObjectByType<BuildController>();
        var placedSpot = builder.BuildResourceSpot(Vector3.zero, Quaternion.identity);
        Vector3 firstPosition = map.GeneratedResources.GetChild(0).position;
        int seed = map.settings.seed;
        map.RegenerateRuntime();
        yield return null;
        Assert.That(map.GeneratedResources.childCount, Is.EqualTo(map.settings.resourceSpotCount));
        Assert.That(map.GeneratedResources.GetChild(0).position, Is.EqualTo(firstPosition));
        Assert.That(placedSpot, Is.Not.Null);
        Assert.That(placedSpot.gameObject.activeInHierarchy, Is.True);
        map.RegenerateRuntime(true);
        yield return null;
        Assert.That(map.settings.seed, Is.EqualTo(seed), "A random runtime map must not overwrite the settings asset.");
        Assert.That(map.GeneratedSeed, Is.Not.EqualTo(seed));
        Assert.That(map.transform.Find("Map Bounds").childCount, Is.EqualTo(4));
        Assert.That(map.GeneratedResources.childCount, Is.EqualTo(map.settings.resourceSpotCount));
    }
}
