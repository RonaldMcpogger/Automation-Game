using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class UIAndHotkeyTests
{
    private FactoryPlayer player;
    private Inventory inventory;
    private BuildController buildController;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;

        player = Object.FindAnyObjectByType<FactoryPlayer>();
        Assert.That(player, Is.Not.Null, "FactoryPlayer should be in MainScene.");

        inventory = Inventory.Instance;
        Assert.That(inventory, Is.Not.Null, "Inventory.Instance should be present.");

        buildController = player.GetComponent<BuildController>();
        Assert.That(buildController, Is.Not.Null, "BuildController should be on the player.");
    }

    [UnityTest]
    public IEnumerator EventSystemAndGraphicRaycastersExistInMainScene()
    {
        var es = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
        Assert.That(es, Is.Not.Null, "An EventSystem must exist in the scene.");
        Assert.That(Object.FindObjectsByType<EventSystem>().Length, Is.EqualTo(1),
            "The UI bootstrap must reuse the scene EventSystem.");

        var uiModule = es.GetComponent<InputSystemUIInputModule>();
        Assert.That(uiModule, Is.Not.Null, "EventSystem must have an InputSystemUIInputModule.");

        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        Assert.That(canvases.Length, Is.GreaterThan(0), "At least one Canvas must exist.");

        foreach (var canvas in canvases)
        {
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            Assert.That(raycaster, Is.Not.Null, $"Canvas '{canvas.gameObject.name}' must have a GraphicRaycaster component.");
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator InventoryCanBeToggledAndControlsUIMode()
    {
        Assert.That(InputManager.Instance, Is.Not.Null);

        inventory.SetOpen(true);
        yield return null;
        Assert.That(InputManager.Instance.UIMode, Is.True, "InputManager should be in UIMode when inventory is open.");

        inventory.SetOpen(false);
        yield return null;
        Assert.That(InputManager.Instance.UIMode, Is.False, "InputManager should not be in UIMode when inventory is closed.");
    }

    [UnityTest]
    public IEnumerator BuildTabCatalogCanBeOpenedAndClosed()
    {
        var catalog = Object.FindAnyObjectByType<BuildTabCatalog>(FindObjectsInactive.Include);
        Assert.That(catalog, Is.Not.Null, "BuildTabCatalog should exist in MainScene.");
        Assert.That(catalog.gameObject.activeInHierarchy, Is.True, "BuildTab GameObject should start active in hierarchy.");
        Assert.That(catalog.IsVisible, Is.False, "BuildTabCatalog should start hidden with alpha 0.");

        catalog.SetVisible(true);
        yield return null;
        Assert.That(catalog.IsVisible, Is.True, "BuildTabCatalog should be visible.");

        buildController.CloseBuildTab();
        yield return null;
        Assert.That(catalog.IsVisible, Is.False, "CloseBuildTab should hide BuildTabCatalog.");
    }

    [UnityTest]
    public IEnumerator BuildModeUIOpensAndClosesViaController()
    {
        var catalog = Object.FindAnyObjectByType<BuildTabCatalog>(FindObjectsInactive.Include);
        Assert.That(catalog, Is.Not.Null);

        // Simulate opening build tab
        catalog.SetVisible(true);
        InputManager.Instance?.EnterUIMode(buildController);
        yield return null;

        Assert.That(catalog.IsVisible, Is.True, "Catalog should be visible.");
        Assert.That(InputManager.Instance.UIMode, Is.True, "InputManager should be in UIMode.");

        // Simulate closing via buildController
        buildController.CloseBuildTab();
        yield return null;

        Assert.That(catalog.IsVisible, Is.False, "Catalog should be hidden.");
        Assert.That(InputManager.Instance.UIMode, Is.False, "InputManager should exit UIMode.");
    }

    [UnityTest]
    public IEnumerator MachineButtonsControlOnlyTheSelectedMachine()
    {
        var menu = MachineInterface.EnsureInstance();
        Assert.That(menu, Is.Not.Null, "MainScene should contain the machine menu.");
        var background = menu.transform.Find("fade/MachineMenuBackground");
        Assert.That(background, Is.Not.Null);
        var start = background.Find("Start").GetComponent<Button>();
        var stop = background.Find("Stop").GetComponent<Button>();
        var close = background.Find("Close").GetComponent<Button>();
        Assert.That(start.interactable, Is.False);
        Assert.That(stop.interactable, Is.False);

        var first = new GameObject("First menu test machine").AddComponent<FactoryMachine>();
        var second = new GameObject("Second menu test machine").AddComponent<FactoryMachine>();
        try
        {
            menu.Open(first);
            Assert.That(start.interactable, Is.False, "Running machines cannot be started again.");
            Assert.That(stop.interactable, Is.True);
            stop.onClick.Invoke();
            Assert.That(first.IsRunning, Is.False);
            Assert.That(second.IsRunning, Is.True, "The other machine must keep running.");
            Assert.That(start.interactable, Is.True);
            Assert.That(stop.interactable, Is.False);

            menu.Open(second);
            Assert.That(start.interactable, Is.False, "Switching machines must refresh the controls.");
            Assert.That(stop.interactable, Is.True);
            stop.onClick.Invoke();
            start.onClick.Invoke();
            Assert.That(second.IsRunning, Is.True);
            Assert.That(first.IsRunning, Is.False, "Start must use the current selection.");

            menu.Open(first);
            Assert.That(start.interactable, Is.True, "Reopening preserves a machine's stopped state.");
            start.onClick.Invoke();
            Assert.That(first.IsRunning, Is.True);
            close.onClick.Invoke();
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(InputManager.Instance.UIMode, Is.False);
            Assert.That(start.interactable, Is.False);
            Assert.That(stop.interactable, Is.False);
        }
        finally
        {
            menu.Close();
            Object.Destroy(first.gameObject);
            Object.Destroy(second.gameObject);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator MachineButtonsPauseAndResumeAnInProgressRecipe()
    {
        var menu = MachineInterface.EnsureInstance();
        Assert.That(menu, Is.Not.Null);
        var background = menu.transform.Find("fade/MachineMenuBackground");
        var start = background.Find("Start").GetComponent<Button>();
        var stop = background.Find("Stop").GetComponent<Button>();
        var data = Object.Instantiate(Resources.Load<FactoryData>("FactoryData/Smelter"));
        var recipe = Object.Instantiate(data.ValidCraftables[0]);
        recipe.craftDuration = 2f;
        data.ValidCraftables = new System.Collections.Generic.List<CraftingRecipe> { recipe };
        var machine = new GameObject("Recipe pause test machine").AddComponent<FactoryMachine>();
        machine.factoryData = data;
        machine.ApplyFactoryData();
        try
        {
            foreach (var ingredient in recipe.ingredients)
                Assert.That(machine.TryAddInput(ingredient.item, ingredient.amount), Is.True);
            menu.Open(machine);
            yield return null;
            yield return new WaitForSeconds(1f);
            Assert.That(machine.IsProcessing, Is.True);

            stop.onClick.Invoke();
            yield return new WaitForSeconds(2.2f);
            Assert.That(machine.IsRunning, Is.False);
            Assert.That(machine.IsProcessing, Is.True, "Stopping preserves the active recipe.");
            Assert.That(machine.OutputSlots[0].IsEmpty, Is.True, "Stopped machines must not finish crafting.");

            start.onClick.Invoke();
            yield return new WaitForSeconds(1.1f);
            Assert.That(machine.IsProcessing, Is.False, "Start resumes the remaining time instead of restarting.");
            Assert.That(machine.OutputSlots[0].item, Is.EqualTo(recipe.outputItem));
            Assert.That(machine.OutputSlots[0].amount, Is.EqualTo(recipe.outputAmount));
        }
        finally
        {
            menu.Close();
            Object.Destroy(machine.gameObject);
            Object.Destroy(data);
            Object.Destroy(recipe);
        }
    }
}
