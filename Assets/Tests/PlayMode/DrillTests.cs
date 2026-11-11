using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class DrillTests
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
    public IEnumerator DrillIsAvailableInBuildListAndCanBeBuilt()
    {
        buildController.SetBuildModeActive(true);
        buildController.SetSelectedType(BuildableType.Drill);
        Assert.That(buildController.SelectedType, Is.EqualTo(BuildableType.Drill));

        Vector3 drillPos = new Vector3(30f, 0f, 30f);
        var drill = buildController.BuildDrill(drillPos, Quaternion.identity);

        Assert.That(drill, Is.Not.Null, "Built drill should not be null.");
        Assert.That(drill.GetComponent<Drill>(), Is.Not.Null);
        Assert.That(drill.statusLight, Is.Not.Null, "Drill should have status light.");
        Assert.That(drill.OutputItem, Is.Not.Null, "Output item should be Iron Ore.");
        Assert.That(drill.OutputItem.DisplayName, Does.Contain("Ore"));

        yield return null;
    }

    [UnityTest]
    public IEnumerator DrillExtractsOreIntoBufferAndPlayerCollects()
    {
        Vector3 drillPos = new Vector3(35f, 0f, 35f);
        var drill = buildController.BuildDrill(drillPos, Quaternion.identity);
        drill.processingSeconds = 0.05f;

        // Allow drill to run a few cycles
        yield return new WaitForSeconds(0.2f);

        Assert.That(drill.BufferedOre, Is.GreaterThan(0), "Drill should have buffered extracted ore.");

        int initialPlayerOre = player.GetItemCount(drill.OutputItem);
        int buffered = drill.BufferedOre;

        drill.Interact(player);

        Assert.That(player.GetItemCount(drill.OutputItem), Is.EqualTo(initialPlayerOre + buffered),
            "Player should have collected all buffered ore from drill.");
        Assert.That(drill.BufferedOre, Is.Zero, "Drill buffer should be empty after collection.");
    }

    [UnityTest]
    public IEnumerator DrillOutputsDirectlyToConnectedConveyor()
    {
        Vector3 drillPos = new Vector3(45f, 0f, 45f);
        var drill = buildController.BuildDrill(drillPos, Quaternion.identity);
        drill.processingSeconds = 0.05f;

        Vector3 beltStart = drillPos + Vector3.forward * 1.8f;
        beltStart.y = 1.0f;
        Vector3 beltEnd = beltStart + Vector3.forward * 5f;

        var belt = buildController.BuildConveyorBelt(beltStart, beltEnd);
        drill.outputBelt = belt;
        belt.inputMachine = drill;

        yield return new WaitForSeconds(0.2f);

        Assert.That(drill.ProducedCount, Is.GreaterThan(0), "Drill should have produced ore onto conveyor.");
        Assert.That(belt.ItemCount, Is.GreaterThan(0), "Conveyor should contain items fed from the drill.");
    }
}
