using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class FirstFactoryTests
{
    private FactoryPlayer player;
    private ResourceSpot spot;
    private FactoryMachine machine;
    private SubmissionBin bin;
    private Conveyor belt;

    [UnitySetUp]
    public IEnumerator LoadFactory()
    {
        yield return SceneManager.LoadSceneAsync("MainScene");
        yield return null;
        player = Object.FindAnyObjectByType<FactoryPlayer>();
        spot = Object.FindAnyObjectByType<ResourceSpot>();
        machine = Object.FindAnyObjectByType<FactoryMachine>();
        bin = Object.FindAnyObjectByType<SubmissionBin>();
        belt = machine.outputBelt;
        machine.processingSeconds = 0.05f;
        belt.speed = 120f;
        Assert.That(Object.FindObjectsByType<FirstPersonController>().Length, Is.EqualTo(1));
        Assert.That(Object.FindObjectsByType<ResourceSpot>().Length, Is.EqualTo(1));
        Assert.That(Object.FindObjectsByType<FactoryMachine>().Length, Is.EqualTo(1));
        Assert.That(Object.FindObjectsByType<SubmissionBin>().Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator GatherSmeltTransportAndSubmitTenIngots()
    {
        spot.gatherInterval = 0.05f;
        for (int i = 0; i < 10; i++)
        {
            spot.Interact(player);
            yield return new WaitForSeconds(0.06f);
        }
        Assert.That(player.OreCount, Is.EqualTo(10));
        Assert.That(player.TryCollectOre(), Is.False, "The pouch must respect its capacity.");
        Assert.That(machine.LoadFrom(player), Is.EqualTo(10));
        Assert.That(player.OreCount, Is.Zero);
        yield return WaitForDeliveries(10);
        Assert.That(bin.GoalReached, Is.True);
        Assert.That(machine.ProducedCount, Is.EqualTo(10));
        Assert.That(machine.BufferedOre, Is.Zero);
        Assert.That(belt.ItemCount, Is.Zero);
        Assert.That(player.objectiveText.text, Does.Contain("COMPLETE"));
    }

    [UnityTest]
    public IEnumerator DisabledBeltRetainsFinishedOutputUntilItResumes()
    {
        belt.enabled = false;
        player.TryCollectOre();
        machine.LoadFrom(player);
        yield return new WaitForSeconds(0.25f);
        Assert.That(machine.OutputReady, Is.True);
        Assert.That(machine.ProducedCount, Is.Zero);
        Assert.That(bin.SubmittedCount, Is.Zero);
        belt.enabled = true;
        yield return WaitForDeliveries(1);
        Assert.That(machine.ProducedCount, Is.EqualTo(1));
        Assert.That(machine.OutputReady, Is.False);
    }

    [UnityTest]
    public IEnumerator UnavailableBinQueuesCargoThenAcceptsItExactlyOnce()
    {
        bin.enabled = false;
        player.TryCollectOre();
        player.TryCollectOre();
        machine.LoadFrom(player);
        yield return new WaitForSeconds(1.5f);
        Assert.That(machine.ProducedCount, Is.EqualTo(2));
        Assert.That(belt.ItemCount, Is.EqualTo(2));
        Assert.That(bin.SubmittedCount, Is.Zero);
        var items = Object.FindObjectsByType<FactoryItem>();
        Assert.That(items.Length, Is.EqualTo(2));
        Assert.That(Vector3.Distance(items[0].transform.position, items[1].transform.position),
            Is.GreaterThan(0.6f), "Queued ingots must remain separated.");
        bin.enabled = true;
        yield return WaitForDeliveries(2);
        yield return new WaitForSeconds(0.1f);
        Assert.That(bin.SubmittedCount, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator BinRejectsTestCubesRawOreAndDuplicateSubmissions()
    {
        var cargo = new GameObject("Submission validation cargo");
        cargo.transform.position = Vector3.up * 100f;
        var body = cargo.AddComponent<Rigidbody>();
        body.useGravity = false;
        Assert.That(bin.TryReceive(body), Is.False, "Unmarked test objects must not score.");
        var item = cargo.AddComponent<FactoryItem>();
        item.material = FactoryItem.MaterialKind.IronOre;
        Assert.That(bin.TryReceive(body), Is.False, "Raw ore must be processed first.");
        item.material = FactoryItem.MaterialKind.IronIngot;
        Assert.That(bin.TryReceive(body), Is.True);
        Assert.That(bin.TryReceive(body), Is.False, "Trigger and endpoint must not count the same ingot twice.");
        Assert.That(bin.SubmittedCount, Is.EqualTo(1));
        yield return null;
    }

    [UnityTest]
    public IEnumerator ResourceSpotRespectsGatherCooldown()
    {
        spot.Interact(player);
        spot.Interact(player);
        Assert.That(player.OreCount, Is.EqualTo(1));
        yield return new WaitForSeconds(spot.gatherInterval + 0.02f);
        spot.Interact(player);
        Assert.That(player.OreCount, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator FullMachineDoesNotConsumeMoreOre()
    {
        for (int i = 0; i < 10; i++)
            player.TryCollectOre();
        Assert.That(machine.LoadFrom(player), Is.EqualTo(10));
        player.TryCollectOre();
        Assert.That(machine.LoadFrom(player), Is.Zero);
        Assert.That(player.OreCount, Is.EqualTo(1));
        Assert.That(machine.BufferedOre, Is.EqualTo(10));
        yield return null;
    }

    private IEnumerator WaitForDeliveries(int count)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (bin.SubmittedCount < count && Time.realtimeSinceStartup < deadline)
            yield return null;
        yield return null;
        Assert.That(bin.SubmittedCount, Is.EqualTo(count), "The saved scene must deliver every produced ingot.");
    }
}
