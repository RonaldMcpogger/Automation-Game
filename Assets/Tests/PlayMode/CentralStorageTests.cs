using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class CentralStorageTests
{
    private GameObject storageObj;
    private CentralStorage storage;
    private Resource ironIngot;

    [SetUp]
    public void SetUp()
    {
        storageObj = new GameObject("Test Central Storage");
        storage = storageObj.AddComponent<CentralStorage>();
        storage.totalCapacity = 100;

        ironIngot = ScriptableObject.CreateInstance<Resource>();
        ironIngot.name = "Iron Ingot";
        ironIngot.itemName = "Iron Ingot";
        ironIngot.maxStackSize = 20;
    }

    [TearDown]
    public void TearDown()
    {
        if (storageObj != null)
            Object.DestroyImmediate(storageObj);
        if (ironIngot != null)
            Object.DestroyImmediate(ironIngot);
    }

    [Test]
    public void TryDepositAndWithdrawUpdatesCounts()
    {
        Assert.That(storage.GetItemCount(ironIngot), Is.Zero);

        bool deposited = storage.TryDeposit(ironIngot, 15);
        Assert.That(deposited, Is.True);
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(15));
        Assert.That(storage.TotalStoredCount, Is.EqualTo(15));

        bool withdrawn = storage.TryWithdraw(ironIngot, 5);
        Assert.That(withdrawn, Is.True);
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(10));

        bool overdraw = storage.TryWithdraw(ironIngot, 50);
        Assert.That(overdraw, Is.False);
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(10));
    }

    [Test]
    public void RespectsCapacity()
    {
        storage.totalCapacity = 10;
        Assert.That(storage.TryDeposit(ironIngot, 8), Is.True);
        Assert.That(storage.TryDeposit(ironIngot, 5), Is.False, "Cannot exceed totalCapacity");
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(8));
    }

    [UnityTest]
    public IEnumerator SubmissionBinRoutesDeliveredCargoToCentralStorage()
    {
        var binObj = new GameObject("Test Submission Bin");
        var bin = binObj.AddComponent<SubmissionBin>();
        bin.acceptedItem = ironIngot;

        var cargo = new GameObject("Cargo");
        var rb = cargo.AddComponent<Rigidbody>();
        var item = cargo.AddComponent<FactoryItem>();
        item.itemData = ironIngot;

        Assert.That(bin.TryReceive(rb), Is.True);
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(1));
        Assert.That(bin.SubmittedCount, Is.EqualTo(1));

        yield return null;
        Object.Destroy(binObj);
    }
}
