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
    public IEnumerator SwitchingBuildableTypesUpdatesSelectedType()
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
    public IEnumerator TwoPointConveyorCreationGeneratesValidBelt()
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
    public IEnumerator BuildingSmelterBinAndResourceSpotCreatesFunctionalObjects()
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
        player.TryCollectOre();
        smelter.LoadFrom(player);

        yield return new WaitForSeconds(0.4f);

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
    public IEnumerator BuildModeUIAnchoredToScreenSideWhenActive()
    {
        buildController.SetBuildModeActive(true);
        yield return null;

        var ui = Object.FindAnyObjectByType<BuildModeUI>(FindObjectsInactive.Include);
        Assert.That(ui, Is.Not.Null, "BuildModeUI should exist.");
        Assert.That(ui.gameObject.activeSelf, Is.True, "BuildModeUI should be visible when build mode is active.");

        var rect = ui.GetComponent<RectTransform>();
        Assert.That(rect, Is.Not.Null, "BuildModeUI must have a RectTransform.");

        // When sidebarOnRight is true, anchors should be pinned to the right edge (x = 1)
        Assert.That(rect.anchorMin.x, Is.EqualTo(1f), "Sidebar anchorMin.x should be 1.0 (right edge).");
        Assert.That(rect.anchorMax.x, Is.EqualTo(1f), "Sidebar anchorMax.x should be 1.0 (right edge).");
        Assert.That(rect.pivot.x, Is.EqualTo(1f), "Sidebar pivot.x should be 1.0 (right-aligned).");
        Assert.That(rect.anchoredPosition.x, Is.EqualTo(-24f), "Sidebar should be offset 24px inward from right edge.");

        // Check center crosshair exists and is at screen center (0.5, 0.5)
        var crosshair = GameObject.Find("BuildCrosshair");
        Assert.That(crosshair, Is.Not.Null, "BuildCrosshair should exist.");
        Assert.That(crosshair.activeSelf, Is.True, "BuildCrosshair should be visible.");
        var crosshairRect = crosshair.GetComponent<RectTransform>();
        Assert.That(crosshairRect.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)), "Crosshair must be centered.");
        Assert.That(crosshairRect.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)), "Crosshair must be centered.");

        // Deactivating build mode hides both
        buildController.SetBuildModeActive(false);
        yield return null;

        Assert.That(ui.gameObject.activeSelf, Is.False, "BuildModeUI should be hidden when build mode is inactive.");
        Assert.That(crosshair.activeSelf, Is.False, "BuildCrosshair should be hidden when build mode is inactive.");
    }

    [UnityTest]
    public IEnumerator ConveyorWidthCanBeCustomizedAndCycled()
    {
        // Default radius should be 0.5f (thinner than original 0.83f)
        Assert.That(buildController.conveyorRadius, Is.EqualTo(0.5f));

        // Test cycling presets
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
}
