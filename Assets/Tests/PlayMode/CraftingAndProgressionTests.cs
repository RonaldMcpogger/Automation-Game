using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class CraftingAndProgressionTests
{
    private FactoryPlayer player;
    private BuildController buildController;
    private ResourceSpot spot;
    private FactoryMachine machine;
    private SubmissionBin bin;
    private Conveyor belt;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;

        player = Object.FindAnyObjectByType<FactoryPlayer>();
        Assert.That(player, Is.Not.Null, "FactoryPlayer should be present.");

        buildController = player.GetComponent<BuildController>();
        Assert.That(buildController, Is.Not.Null, "BuildController should be present.");

        // Enable First Factory demo for testing stations
        var demo = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Single(x => x.name == "First Factory");
        demo.gameObject.SetActive(true);
        yield return null;

        spot = demo.GetComponentInChildren<ResourceSpot>();
        machine = demo.GetComponentInChildren<FactoryMachine>();
        bin = demo.GetComponentInChildren<SubmissionBin>();
        player.machine = machine;
        player.submissionBin = bin;
        belt = machine.outputBelt;
        machine.processingSeconds = 0.05f;
        belt.speed = 100f;
    }

    [UnityTest]
    public IEnumerator SmelterBuildCostRequiresOreAndDeductsUponCrafting()
    {
        Assert.That(player.OreCount, Is.Zero);

        // Cannot afford smelter initially
        bool canAfford = buildController.CanAfford(BuildableType.Smelter, out string reason);
        Assert.That(canAfford, Is.False, "Player should not afford Smelter with 0 ore.");
        Assert.That(reason, Does.Contain("Requires"));

        // Gather 5 ore from spot
        spot.gatherInterval = 0.01f;
        for (int i = 0; i < 5; i++)
        {
            spot.Interact(player);
            yield return new WaitForSeconds(0.02f);
        }

        Assert.That(player.OreCount, Is.EqualTo(5));
        canAfford = buildController.CanAfford(BuildableType.Smelter, out _);
        Assert.That(canAfford, Is.True, "Player should afford Smelter with 5 ore.");

        // Deduct cost
        buildController.DeductBuildCost(BuildableType.Smelter);
        Assert.That(player.OreCount, Is.Zero, "Deducting cost should consume 5 ore.");
    }

    [UnityTest]
    public IEnumerator SubmissionBinReceivesIngotAndAllowsPlayerToTakeIt()
    {
        Assert.That(bin.SubmittedCount, Is.Zero);
        Assert.That(bin.StoredCount, Is.Zero);

        // Load 1 ore into machine and let it reach bin
        spot.gatherInterval = 0.01f;
        spot.Interact(player);
        yield return new WaitForSeconds(0.02f);
        machine.LoadFrom(player);

        // Wait for machine and belt to deliver ingot
        float timeout = Time.time + 3f;
        while (bin.SubmittedCount == 0 && Time.time < timeout)
            yield return null;

        Assert.That(bin.SubmittedCount, Is.EqualTo(1), "Submission bin should register submitted ingot.");
        Assert.That(bin.StoredCount, Is.EqualTo(1), "Submission bin should store 1 ingot available for withdrawal.");

        // Player takes ingot from bin
        bool taken = bin.TakeIngot(player);
        Assert.That(taken, Is.True, "Player should successfully take ingot from bin.");
        Assert.That(bin.StoredCount, Is.Zero, "Stored count should decrement.");
        Assert.That(bin.SubmittedCount, Is.EqualTo(1), "Submitted milestone progress should remain 1.");
        Assert.That(player.CarriedItem, Is.Not.Null);
        Assert.That(player.CarriedItem.DisplayName, Does.Contain("Ingot"));
        Assert.That(player.OreCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator EndToEndProgressionFlow_Gather_Smelt_Bin_TakeIngots_CraftPresser()
    {
        // 1. Gather ore
        spot.gatherInterval = 0.01f;
        for (int i = 0; i < 4; i++)
        {
            spot.Interact(player);
            yield return new WaitForSeconds(0.02f);
        }

        // 2. Feed ore into smelter
        machine.LoadFrom(player);

        // Wait for all 4 ingots to reach submission bin
        float timeout = Time.time + 5f;
        while (bin.SubmittedCount < 4 && Time.time < timeout)
            yield return null;

        Assert.That(bin.SubmittedCount, Is.EqualTo(4));
        Assert.That(bin.StoredCount, Is.EqualTo(4));

        // 3. Player cannot afford Presser yet (has 0 ingots in inventory)
        Assert.That(buildController.CanAfford(BuildableType.Presser, out _), Is.False);

        // 4. Player takes 4 ingots from submission bin
        for (int i = 0; i < 4; i++)
        {
            bool taken = bin.TakeIngot(player);
            Assert.That(taken, Is.True);
        }

        Assert.That(bin.StoredCount, Is.Zero);
        Assert.That(player.GetItemCount(machine.OutputItem), Is.EqualTo(4));

        // 5. Now player CAN afford Presser
        Assert.That(buildController.CanAfford(BuildableType.Presser, out _), Is.True);

        // 6. Build and place Presser
        Vector3 presserPos = new Vector3(30f, 0f, 30f);
        var presser = buildController.BuildPresser(presserPos, Quaternion.identity);
        buildController.DeductBuildCost(BuildableType.Presser);

        Assert.That(presser, Is.Not.Null);
        Assert.That(player.GetItemCount(machine.OutputItem), Is.Zero, "Presser cost should be consumed.");
    }

    [UnityTest]
    public IEnumerator HotbarResourceBarIsVisibleAndReflectsInventory()
    {
        Assert.That(player.resourceBarText, Is.Not.Null, "ResourceBarText should be present directly above hotbar.");
        Assert.That(player.promptText, Is.Not.Null, "PromptText should be present.");

        // Gather 3 ore
        spot.gatherInterval = 0.01f;
        for (int i = 0; i < 3; i++)
        {
            spot.Interact(player);
            yield return new WaitForSeconds(0.02f);
        }

        // Wait a frame for UI update
        yield return null;

        Assert.That(player.resourceBarText.text, Does.Contain("ORE: 3"));
    }
}
