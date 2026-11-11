using System.Collections;
using System.Linq;
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
    public IEnumerator CopperAndTinGatherAsSeparateResources()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;

        var map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        var deposits = map.GeneratedResources.GetComponentsInChildren<ResourceSpot>();
        var player = Object.FindAnyObjectByType<FactoryPlayer>();
        var copper = deposits.First(spot => spot.ResourceName == "Copper Ore");
        var tin = deposits.First(spot => spot.ResourceName == "Tin Ore");
        Assert.That(deposits.Count(spot => spot.resourceData == copper.resourceData),
            Is.GreaterThan(deposits.Count(spot => spot.resourceData == tin.resourceData)));

        foreach (var spot in new[] { copper, tin })
        {
            spot.Interact(player);
            Assert.That(Inventory.Instance.GetItemCount(spot.resourceData), Is.EqualTo(1));
            var looseOre = spot.resourceData.objModel.GetComponent<FactoryItem>();
            Assert.That(looseOre.itemData, Is.SameAs(spot.resourceData));
            Assert.That(looseOre.GetComponent<Rigidbody>(), Is.Not.Null);
        }

        var builder = player.GetComponent<BuildController>();
        var smelter = builder.BuildSmelter(new Vector3(25, 0, 25), Quaternion.identity);
        Assert.That(smelter.LoadFrom(player), Is.Zero, "The iron recipe should only consume iron ore.");
        Assert.That(Inventory.Instance.TotalCount, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator RepeatedRegenerationReplacesOnlyGeneratedResources()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;
        var map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        var builder = Object.FindAnyObjectByType<BuildController>();
        var placedSpot = builder.BuildResourceSpot(Vector3.zero, Quaternion.identity);
        var savedLayout = ReadLayout();
        int seed = map.settings.seed;
        map.RegenerateRuntime();
        yield return null;
        Assert.That(map.GeneratedResources.childCount, Is.EqualTo(map.settings.resourceSpotCount));
        Assert.That(ReadLayout(), Is.EqualTo(savedLayout), "The seed must reproduce both deposit locations and ore types.");
        Assert.That(placedSpot, Is.Not.Null);
        Assert.That(placedSpot.gameObject.activeInHierarchy, Is.True);
        map.RegenerateRuntime(true);
        yield return null;
        Assert.That(map.settings.seed, Is.EqualTo(seed), "A random runtime map must not overwrite the settings asset.");
        Assert.That(map.GeneratedSeed, Is.Not.EqualTo(seed));
        Assert.That(map.transform.Find("Map Bounds").childCount, Is.EqualTo(4));
        Assert.That(map.GeneratedResources.childCount, Is.EqualTo(map.settings.resourceSpotCount));

        (Vector3 position, Resource ore)[] ReadLayout() => map.GeneratedResources.Cast<Transform>()
            .Select(deposit => (deposit.position, deposit.GetComponent<ResourceSpot>().resourceData)).ToArray();
    }
}
