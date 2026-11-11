using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.TestTools;

public class BuildModeTests
{
    private FactoryPlayer player;
    private BuildController buildController;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;
        player = Object.FindAnyObjectByType<FactoryPlayer>();
        Assert.That(player, Is.Not.Null, "FactoryPlayer should be in the scene.");
        buildController = player.GetComponent<BuildController>();
        Assert.That(buildController, Is.Not.Null, "BuildController should be on the player.");
    }

    [UnityTest]
    public IEnumerator BuildModeCanBeToggled()
    {
        Assert.That(buildController.IsBuildModeActive, Is.False, "Build mode should start inactive.");

        buildController.SetBuildModeActive(true);
        Assert.That(buildController.IsBuildModeActive, Is.True, "Build mode should be active.");

        yield return null;

        buildController.SetBuildModeActive(false);
        Assert.That(buildController.IsBuildModeActive, Is.False, "Build mode should be inactive after toggle.");
    }

    [UnityTest]
    public IEnumerator SwitchingBuildTypesUpdatesSelection()
    {
        buildController.SetBuildModeActive(true);

        buildController.SetSelectedType(BuildableType.Smelter);
        Assert.That(buildController.SelectedType, Is.EqualTo(BuildableType.Smelter));

        buildController.SetSelectedType(BuildableType.SubmissionBin);
        Assert.That(buildController.SelectedType, Is.EqualTo(BuildableType.SubmissionBin));

        buildController.SetSelectedType(BuildableType.ResourceSpot);
        Assert.That(buildController.SelectedType, Is.EqualTo(BuildableType.ResourceSpot));

        buildController.SetSelectedType(BuildableType.ConveyorBelt);
        Assert.That(buildController.SelectedType, Is.EqualTo(BuildableType.ConveyorBelt));

        yield return null;
    }

    [UnityTest]
    public IEnumerator TwoPointConveyorBuildsBelt()
    {
        Vector3 start = new Vector3(0f, 1f, 0f);
        Vector3 end = new Vector3(0f, 1f, 10f);

        var conveyor = buildController.BuildConveyorBelt(start, end);
        Assert.That(conveyor, Is.Not.Null, "Built conveyor should not be null.");

        var container = conveyor.GetComponent<SplineContainer>();
        Assert.That(container, Is.Not.Null, "Conveyor must have SplineContainer.");
        Assert.That(container.Spline.Count, Is.EqualTo(2), "Spline should have start and end knots.");

        var meshFilter = conveyor.GetComponent<MeshFilter>();
        Assert.That(meshFilter, Is.Not.Null, "Conveyor must have a MeshFilter.");
        Assert.That(meshFilter.sharedMesh, Is.Not.Null, "Conveyor must generate its square extrusion mesh.");

        var meshCollider = conveyor.GetComponent<MeshCollider>();
        Assert.That(meshCollider, Is.Not.Null, "Conveyor must have MeshCollider.");
        Assert.That(meshCollider.sharedMesh, Is.Not.Null, "MeshCollider must have sharedMesh.");

        Assert.That(conveyor.speed, Is.GreaterThan(0f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator BuildCreatesFunctionalObjects()
    {
        Vector3 spotPos = new Vector3(15f, 0f, 15f);
        Vector3 smelterPos = new Vector3(20f, 0f, 20f);
        Vector3 binPos = new Vector3(20f, 0f, 30f);

        var spot = buildController.BuildResourceSpot(spotPos, Quaternion.identity);
        Assert.That(spot, Is.Not.Null);
        Assert.That(spot.GetComponent<Collider>(), Is.Not.Null);

        var smelter = buildController.BuildSmelter(smelterPos, Quaternion.identity);
        Assert.That(smelter, Is.Not.Null);
        Assert.That(smelter.GetComponent<Collider>(), Is.Not.Null);
        Assert.That(smelter.statusLight, Is.Not.Null);

        var bin = buildController.BuildSubmissionBin(binPos, Quaternion.identity);
        Assert.That(bin, Is.Not.Null);
        Assert.That(bin.GetComponent<BoxCollider>(), Is.Not.Null);
        Assert.That(bin.GetComponent<BoxCollider>().isTrigger, Is.True);

        yield return null;
    }

    [UnityTest]
    public IEnumerator BuiltBeltTransportsCargoFromSmelterToBin()
    {
        // 1. Build Smelter
        Vector3 smelterPos = new Vector3(30f, 0f, 0f);
        var smelter = buildController.BuildSmelter(smelterPos, Quaternion.identity);
        smelter.processingSeconds = 0.05f;

        // 2. Build Belt starting in front of Smelter
        Vector3 beltStart = smelterPos + Vector3.forward * 1.8f + Vector3.up * 1.0f;
        Vector3 beltEnd = beltStart + Vector3.forward * 8f;
        var belt = buildController.BuildConveyorBelt(beltStart, beltEnd);
        belt.speed = 100f;

        // 3. Build Submission Bin at belt end
        Vector3 binPos = new Vector3(beltEnd.x, 0f, beltEnd.z);
        var bin = buildController.BuildSubmissionBin(binPos, Quaternion.identity);

        // Verify auto-connections
        Assert.That(smelter.outputBelt, Is.EqualTo(belt), "Smelter should auto-connect to newly built belt.");
        Assert.That(belt.outputReceiver, Is.EqualTo(bin), "Belt should auto-connect to newly built bin.");

        // Load ore and smelt
        player.TryCollect(smelter.InputItem);
        smelter.LoadFrom(player);

        float deadline = Time.realtimeSinceStartup + 3f;
        while (bin.SubmittedCount < 1 && Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.That(bin.SubmittedCount, Is.EqualTo(1), "Delivered ingot should be accepted by bin.");
        Assert.That(smelter.ProducedCount, Is.EqualTo(1), "Smelter should have produced 1 ingot.");
    }

    [UnityTest]
    public IEnumerator StandaloneBeltEjectsCargoBeyondItsOpenEnd()
    {
        Vector3 start = new Vector3(50f, 1f, 0f);
        Vector3 end = start + Vector3.forward * 4f;
        var belt = buildController.BuildConveyorBelt(start, end);
        belt.speed = 20f;

        var cargo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cargo.transform.position = start;
        var body = cargo.AddComponent<Rigidbody>();
        body.useGravity = false;

        Assert.That(belt.HasOutputConnection, Is.False);
        Assert.That(belt.TryLoadAtStart(body, 0.5f), Is.True);

        yield return new WaitForSeconds(0.5f);

        Assert.That(belt.ItemCount, Is.Zero, "Cargo should detach from an open belt end.");
        Assert.That(body.transform.position.z, Is.GreaterThan(end.z + 0.45f),
            "Cargo should clear the conveyor end cap rather than reattaching to it.");
        Object.Destroy(cargo);
    }

    [UnityTest]
    public IEnumerator CatalogSelectionStartsPlacement()
    {
        // Open the same catalog flow used by B, then exercise the actual scene buttons.
        typeof(BuildController).GetMethod("OpenBuildTab", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic).Invoke(buildController, null);
        yield return null;
        var catalog = Object.FindAnyObjectByType<BuildTabCatalog>(FindObjectsInactive.Include);
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.IsVisible, Is.True);
        Assert.That(InputManager.Instance.GameplayEnabled, Is.False);
        var buttons = catalog.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        var smelterEntry = System.Array.Find(buttons, x => x.name == "Smelter");
        Assert.That(smelterEntry, Is.Not.Null);
        smelterEntry.onClick.Invoke();
        Assert.That(buildController.SelectedType, Is.EqualTo(BuildableType.Smelter));
        var buildButton = (UnityEngine.UI.Button)typeof(BuildTabCatalog).GetField("buildButton",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(catalog);
        Assert.That(buildButton.interactable, Is.True, "The scene's free smelter is available without resources.");
        buildButton.onClick.Invoke();
        yield return null;
        Assert.That(buildController.IsBuildModeActive, Is.True);
        Assert.That(catalog.IsVisible, Is.False);
        Assert.That(InputManager.Instance.GameplayEnabled, Is.True);
        Assert.That(InputManager.Instance.CursorLocked, Is.True);
        var placeFactory = typeof(BuildController).GetMethod("BuildSelectedFactory",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var placed = (GameObject)placeFactory.Invoke(buildController, new object[] { new Vector3(25, 0, 25) });
        var machine = placed.GetComponent<FactoryMachine>();
        var receiver = placed.transform.Find("Input").GetComponent<MachineReceiver>();
        Assert.That(machine.InputItem, Is.SameAs(player.DefaultOreResource));
        Assert.That(receiver, Is.Not.Null, "A catalog-placed smelter must expose its conveyor input.");
        Assert.That(receiver.machine, Is.SameAs(machine));
        var unrelatedBody = new GameObject("Unrelated Rigidbody").AddComponent<Rigidbody>();
        Assert.That(receiver.TryReceive(unrelatedBody), Is.False, "The smelter must leave non-resource objects alone.");
        Assert.That(machine.BufferedOre, Is.Zero);
        buildController.SetBuildModeActive(false);
    }
    [UnityTest]
    public IEnumerator ConveyorWidthCanBeCustomizedAndCycled()
    {
        // Test cycling presets
        buildController.conveyorRadius = 0.5f;
        buildController.CycleConveyorWidth();
        Assert.That(buildController.conveyorRadius, Is.EqualTo(0.75f));

        buildController.CycleConveyorWidth();
        Assert.That(buildController.conveyorRadius, Is.EqualTo(0.35f));

        buildController.CycleConveyorWidth();
        Assert.That(buildController.conveyorRadius, Is.EqualTo(0.50f));

        // Test custom thickness building
        buildController.conveyorRadius = 0.35f;
        var slimBelt = buildController.BuildConveyorBelt(new Vector3(0, 1, 0), new Vector3(0, 1, 5));
        var mesh = slimBelt.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(mesh.bounds.size.x, Is.EqualTo(0.7f).Within(0.02f),
            "The generated slim belt must be 0.7 metres wide.");

        yield return null;
    }
    [UnityTest]
    public IEnumerator BuildMenuExcludesResourceSpots()
    {
        Assert.That(buildController.buildableFactories, Is.Not.Null);
        Assert.That(buildController.buildableFactories.Length, Is.GreaterThanOrEqualTo(4));

        foreach (var factory in buildController.buildableFactories)
        {
            Assert.That(factory.kind, Is.Not.EqualTo(FactoryKind.ResourceSpot),
                "ResourceSpot must not appear in the buildable catalog / build menu.");
        }

        var kinds = System.Array.ConvertAll(buildController.buildableFactories, f => f.kind);
        Assert.That(kinds, Does.Contain(FactoryKind.Conveyor));
        Assert.That(kinds, Does.Contain(FactoryKind.Machine));
        Assert.That(kinds, Does.Contain(FactoryKind.SubmissionBin));
        Assert.That(kinds, Does.Contain(FactoryKind.Presser));

        yield return null;
    }
}
