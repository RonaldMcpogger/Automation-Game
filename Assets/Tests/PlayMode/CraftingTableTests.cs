using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class CraftingTableTests
{
    private Inventory inventory;
    private FactoryPlayer player;
    private CraftingTab tab;
    private CraftingTable table;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;
        inventory = Inventory.Instance;
        player = Object.FindAnyObjectByType<FactoryPlayer>();
        tab = Object.FindAnyObjectByType<CraftingTab>();
        table = Object.FindAnyObjectByType<CraftingTable>();
        Assert.That(inventory, Is.Not.Null);
        Assert.That(tab, Is.Not.Null);
        Assert.That(table, Is.Not.Null);
        Assert.That(tab.IsOpen, Is.False);
        foreach (var entry in tab.entries)
        {
            Assert.That(entry.label, Is.Not.SameAs(tab.requirements));
            Assert.That(tab.requirements.transform.IsChildOf(entry.button.transform), Is.False,
                "The ingredient details belong to the panel, outside the selectable recipe buttons.");
        }
    }

    [UnityTest]
    public IEnumerator GatheringBuildingAndMachineLoadingUseTheSameInventory()
    {
        var map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        var spot = map.GeneratedResources.GetChild(0).GetComponent<ResourceSpot>();
        spot.Interact(player);
        spot.Interact(player); // A second call during the cooldown must not duplicate ore.
        Assert.That(inventory.GetItemCount(spot.resourceData), Is.EqualTo(1));
        Assert.That(player.GetItemCount(spot.resourceData), Is.EqualTo(1));
        Assert.That(player.TryCollect(spot.resourceData, 5), Is.True);
        var builder = player.GetComponent<BuildController>();
        builder.DeductBuildCost(BuildableType.Smelter);
        Assert.That(inventory.GetItemCount(spot.resourceData), Is.EqualTo(1));
        Assert.That(player.OreCount, Is.EqualTo(1));
        var machine = builder.BuildSmelter(new Vector3(25, 0, 25), Quaternion.identity);
        Assert.That(machine.LoadFrom(player), Is.EqualTo(1));
        Assert.That(inventory.GetItemCount(spot.resourceData), Is.Zero);
        yield return null;
    }

    [UnityTest]
    public IEnumerator AuthoredRecipeButtonsCraftIntoInventoryAndCloseRestoresInput()
    {
        MoveToTable();
        int childCount = tab.GetComponentsInChildren<Transform>(true).Length;
        table.Interact(player);
        Assert.That(tab.IsOpen, Is.True);
        Assert.That(InputManager.Instance.GameplayEnabled, Is.False);
        Assert.That(tab.craftButton.interactable, Is.False);
        var ingot = tab.entries[0].recipe;
        Assert.That(inventory.TryAdd(ingot.ingredients[0].item, 4), Is.True);
        Assert.That(tab.craftButton.interactable, Is.True, "Inventory changes must immediately update affordability.");
        tab.craftButton.onClick.Invoke();
        tab.craftButton.onClick.Invoke();
        Assert.That(inventory.GetItemCount(ingot.ingredients[0].item), Is.Zero);
        Assert.That(inventory.GetItemCount(ingot.outputItem), Is.EqualTo(2));
        Assert.That(player.GetItemCount(ingot.outputItem), Is.EqualTo(2));
        tab.entries[1].button.onClick.Invoke();
        var plate = tab.entries[1].recipe;
        tab.craftButton.onClick.Invoke();
        Assert.That(inventory.GetItemCount(ingot.outputItem), Is.Zero);
        Assert.That(inventory.GetItemCount(plate.outputItem), Is.EqualTo(1));
        tab.closeButton.onClick.Invoke();
        Assert.That(tab.IsOpen, Is.False);
        Assert.That(InputManager.Instance.GameplayEnabled, Is.True);
        Assert.That(tab.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(childCount),
            "Opening and crafting must reuse authored controls.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator MissingIngredientsAndFullOutputLeaveInventoryUnchanged()
    {
        var recipe = tab.entries[0].recipe;
        Assert.That(inventory.TryCraft(recipe, out _), Is.False);
        Assert.That(inventory.TotalCount, Is.Zero);
        var tooLarge = ScriptableObject.CreateInstance<CraftingRecipe>();
        tooLarge.ingredients = new[] { new CraftingIngredient { item = recipe.ingredients[0].item, amount = 1 } };
        tooLarge.outputItem = recipe.outputItem;
        tooLarge.outputAmount = 2;
        Assert.That(inventory.TryAdd(recipe.ingredients[0].item, inventory.itemCapacity), Is.True);
        Assert.That(inventory.TryCraft(tooLarge, out string reason), Is.False);
        Assert.That(reason, Does.Contain("full"));
        Assert.That(inventory.TotalCount, Is.EqualTo(inventory.itemCapacity));
        Assert.That(inventory.GetItemCount(recipe.outputItem), Is.Zero);
        // Input removal frees capacity before output is checked.
        Assert.That(inventory.TryCraft(recipe, out _), Is.True);
        Assert.That(inventory.TotalCount, Is.EqualTo(inventory.itemCapacity - 1));
        Object.Destroy(tooLarge);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FailedBulkPickupNeverPartiallyAddsItems()
    {
        var item = tab.entries[0].recipe.ingredients[0].item;
        inventory.TryAdd(item, inventory.itemCapacity - 1);
        Assert.That(inventory.TryAdd(item, 2), Is.False);
        Assert.That(inventory.GetItemCount(item), Is.EqualTo(inventory.itemCapacity - 1));
        yield return null;
    }

    [UnityTest]
    public IEnumerator TableRangeAndDisableReleaseCraftingInput()
    {
        player.transform.position = table.transform.position + Vector3.right * 50;
        table.Interact(player);
        Assert.That(tab.IsOpen, Is.False);
        MoveToTable();
        table.Interact(player);
        Assert.That(tab.IsOpen, Is.True);
        player.transform.position += Vector3.right * 50;
        yield return null;
        Assert.That(tab.IsOpen, Is.False);
        Assert.That(InputManager.Instance.GameplayEnabled, Is.True);
        MoveToTable();
        table.Interact(player);
        table.gameObject.SetActive(false);
        Assert.That(tab.IsOpen, Is.False);
        Assert.That(InputManager.Instance.GameplayEnabled, Is.True);
    }

    private void MoveToTable() => player.transform.position = table.transform.position + new Vector3(0, 1.5f, -2);

    [UnityTest]
    public IEnumerator RepeatedIngredientEntriesRequireTheirCombinedAmount()
    {
        var original = tab.entries[0].recipe;
        var recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
        var input = original.ingredients[0].item;
        recipe.ingredients = new[] {
            new CraftingIngredient { item = input, amount = 3 },
            new CraftingIngredient { item = input, amount = 3 }
        };
        recipe.outputItem = original.outputItem;
        inventory.TryAdd(input, 5);
        Assert.That(inventory.TryCraft(recipe, out _), Is.False);
        Assert.That(inventory.GetItemCount(input), Is.EqualTo(5));
        inventory.TryAdd(input);
        Assert.That(inventory.TryCraft(recipe, out _), Is.True);
        Assert.That(inventory.GetItemCount(input), Is.Zero);
        Assert.That(inventory.GetItemCount(recipe.outputItem), Is.EqualTo(1));
        Object.Destroy(recipe);
        yield return null;
    }
}
