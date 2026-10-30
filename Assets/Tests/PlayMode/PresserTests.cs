using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class PresserTests
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
    public IEnumerator PresserIsAvailableInBuildListAndCanBeBuilt()
    {
        buildController.SetBuildModeActive(true);
        buildController.SetSelectedType(BuildableType.Presser);
        Assert.That(buildController.SelectedType, Is.EqualTo(BuildableType.Presser));

        Vector3 presserPos = new Vector3(40f, 0f, 40f);
        var presser = buildController.BuildPresser(presserPos, Quaternion.identity);

        Assert.That(presser, Is.Not.Null, "Built presser should not be null.");
        Assert.That(presser.GetComponent<Presser>(), Is.Not.Null);
        Assert.That(presser.statusLight, Is.Not.Null, "Presser should have status light.");
        Assert.That(presser.InputItem, Is.Not.Null, "Input item should be Iron Ingot.");
        Assert.That(presser.InputItem.DisplayName, Does.Contain("Ingot"));
        Assert.That(presser.OutputItem, Is.Not.Null, "Output item should be Iron Plate.");
        Assert.That(presser.OutputItem.DisplayName, Does.Contain("Plate"));

        yield return null;
    }

    [UnityTest]
    public IEnumerator IngotContactWithPresserTransformsIntoIronPlate()
    {
        Vector3 presserPos = new Vector3(50f, 0f, 50f);
        var presser = buildController.BuildPresser(presserPos, Quaternion.identity);
        presser.processingSeconds = 0.05f;

        // Spawn an iron ingot
        GameObject ingotObj = null;
        if (presser.InputItem != null && presser.InputItem.objModel != null)
        {
            ingotObj = Object.Instantiate(presser.InputItem.objModel, presserPos + Vector3.up * 1f, Quaternion.identity);
        }
        else
        {
            ingotObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var fi = ingotObj.AddComponent<FactoryItem>();
            fi.material = FactoryItem.MaterialKind.IronIngot;
            ingotObj.AddComponent<Rigidbody>();
        }

        var factoryItem = ingotObj.GetComponent<FactoryItem>();
        if (factoryItem != null)
            factoryItem.itemData = presser.InputItem;

        // Contact via TryProcess
        bool accepted = presser.TryProcess(ingotObj);
        Assert.That(accepted, Is.True, "Presser should accept iron ingot contact.");

        // Wait for processing cycle
        yield return new WaitForSeconds(0.15f);

        Assert.That(presser.ProducedCount, Is.EqualTo(1), "Presser should have produced 1 plate.");

        // Verify plate exists in scene
        var items = Object.FindObjectsByType<FactoryItem>();
        bool foundPlate = false;
        foreach (var it in items)
        {
            if (it != null && (it.itemData == presser.OutputItem || it.name.Contains("Plate")))
            {
                foundPlate = true;
                break;
            }
        }
        Assert.That(foundPlate, Is.True, "Transformed iron plate should exist in scene.");
    }

    [UnityTest]
    public IEnumerator CarrierHeldIngotInteractionWithPresserProducesPlate()
    {
        Vector3 presserPos = new Vector3(60f, 0f, 60f);
        var presser = buildController.BuildPresser(presserPos, Quaternion.identity);
        presser.processingSeconds = 0.05f;

        var carrier = player.GetComponent<Carrier>();
        Assert.That(carrier, Is.Not.Null, "Carrier should exist on player.");

        // Create an ingot with Carriable and Interactable
        GameObject ingotObj;
        if (presser.InputItem != null && presser.InputItem.objModel != null)
            ingotObj = Object.Instantiate(presser.InputItem.objModel, player.transform.position + Vector3.forward * 2f, Quaternion.identity);
        else
            ingotObj = GameObject.CreatePrimitive(PrimitiveType.Cube);

        var carriable = ingotObj.GetComponent<Carriable>() ?? ingotObj.AddComponent<Carriable>();
        var interactable = ingotObj.GetComponent<Interactable>() ?? ingotObj.AddComponent<Interactable>();
        var rb = ingotObj.GetComponent<Rigidbody>() ?? ingotObj.AddComponent<Rigidbody>();
        var item = ingotObj.GetComponent<FactoryItem>() ?? ingotObj.AddComponent<FactoryItem>();
        item.itemData = presser.InputItem;

        // Carrier grabs the ingot
        bool grabbed = carrier.TryGrab(carriable);
        Assert.That(grabbed, Is.True, "Carrier should grab the iron ingot.");

        // Deliver to Presser via IHeldTargetReceiver
        bool received = presser.OnReceiveHeld(carrier, carriable);
        Assert.That(received, Is.True, "Presser should receive held iron ingot.");

        yield return new WaitForSeconds(0.15f);

        Assert.That(presser.ProducedCount, Is.EqualTo(1), "Presser should produce plate from held ingot.");
    }

    [UnityTest]
    public IEnumerator ConveyorTransportsIngotIntoPresserAndDeliversPlateToOutputBelt()
    {
        // 1. Build Presser
        Vector3 presserPos = new Vector3(70f, 0f, 70f);
        var presser = buildController.BuildPresser(presserPos, Quaternion.identity);
        presser.processingSeconds = 0.05f;

        // 2. Build input conveyor leading to presser
        Vector3 inPort = presser.inputPort != null ? presser.inputPort.position : presserPos - Vector3.forward * 1.8f;
        Vector3 inBeltStart = inPort - Vector3.forward * 6f;
        var inBelt = buildController.BuildConveyorBelt(inBeltStart, inPort);
        inBelt.speed = 100f;

        // 3. Build output conveyor leading away from presser
        Vector3 outPort = presser.outputPort != null ? presser.outputPort.position : presserPos + Vector3.forward * 1.8f;
        Vector3 outBeltEnd = outPort + Vector3.forward * 6f;
        var outBelt = buildController.BuildConveyorBelt(outPort, outBeltEnd);
        outBelt.speed = 100f;

        // Connect
        if (presser.receiver != null)
        {
            inBelt.outputReceiver = presser.receiver;
            presser.receiver.inputConveyor = inBelt;
        }
        presser.outputBelt = outBelt;
        outBelt.inputMachine = presser;

        // Spawn ingot on input conveyor
        GameObject ingotObj = Object.Instantiate(presser.InputItem.objModel, inBeltStart, Quaternion.identity);
        var ingotItem = ingotObj.GetComponent<FactoryItem>();
        ingotItem.itemData = presser.InputItem;
        var ingotBody = ingotObj.GetComponent<Rigidbody>();
        bool loaded = inBelt.TryLoadAtStart(ingotBody, 0.5f);
        Assert.That(loaded, Is.True, "Ingot should load onto input conveyor.");

        // Wait for transport and press
        yield return new WaitForSeconds(0.35f);

        Assert.That(presser.ProducedCount, Is.EqualTo(1), "Presser should produce 1 plate from conveyor.");
    }
}
