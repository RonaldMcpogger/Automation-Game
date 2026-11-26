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

    [UnityTest]
    public IEnumerator CentralStorageUIManagesResourcesWithInventory()
    {
        var invObj = new GameObject("Test Inventory");
        var inventory = invObj.AddComponent<Inventory>();
        inventory.itemCapacity = 30;

        var copperOre = ScriptableObject.CreateInstance<Resource>();
        copperOre.name = "Copper Ore";
        copperOre.itemName = "Copper Ore";
        copperOre.maxStackSize = 10;

        inventory.TryAdd(ironIngot, 12);
        inventory.TryAdd(copperOre, 5);
        storage.TryDeposit(ironIngot, 8);

        var ui = CentralStorageUI.EnsureInstance();
        Assert.That(ui, Is.Not.Null);
        Assert.That(ui.IsOpen, Is.False);

        ui.Open(storage);
        yield return null;

        Assert.That(ui.IsOpen, Is.True);
        Assert.That(ui.ActiveStorage, Is.EqualTo(storage));
        Assert.That(InputManager.Instance.UIMode, Is.True);

        // Deposit 4 iron ingots from inventory to central storage
        ui.DepositResource(ironIngot, 4);
        Assert.That(inventory.GetItemCount(ironIngot), Is.EqualTo(8));
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(12));

        // Withdraw 6 iron ingots from central storage to inventory
        ui.WithdrawResource(ironIngot, 6);
        Assert.That(inventory.GetItemCount(ironIngot), Is.EqualTo(14));
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(6));

        // Deposit all remaining inventory items
        ui.DepositAll();
        Assert.That(inventory.TotalCount, Is.Zero);
        Assert.That(storage.GetItemCount(ironIngot), Is.EqualTo(20));
        Assert.That(storage.GetItemCount(copperOre), Is.EqualTo(5));

        ui.Close();
        yield return null;
        Assert.That(ui.IsOpen, Is.False);
        Assert.That(InputManager.Instance.UIMode, Is.False);

        Object.Destroy(ui.gameObject);
        Object.Destroy(invObj);
        Object.DestroyImmediate(copperOre);
    }
}
