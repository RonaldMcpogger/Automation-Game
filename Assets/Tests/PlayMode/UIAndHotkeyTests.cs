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
}
