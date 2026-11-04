using UnityEngine;

/// <summary>Marks real machine output so loose test cubes cannot score in the bin.</summary>
[RequireComponent(typeof(Rigidbody))]
public class FactoryItem : MonoBehaviour
{
    [Tooltip("The ScriptableObject definition for this physical item.")]
    public Resource itemData;

    // Kept so older scene objects and tests continue to work while they are migrated to itemData.
    public enum MaterialKind { IronOre, IronIngot }
    public MaterialKind material = MaterialKind.IronIngot;
    public bool Submitted { get; private set; }

    private Interactable interactable;

    /// <summary>Finds the optional E-interaction component used to put this world item in an inventory.</summary>
    private void Awake() => interactable = GetComponent<Interactable>();

    /// <summary>Listens for E interaction while this physical factory item is active in the world.</summary>
    private void OnEnable()
    {
        if (interactable != null)
            interactable.OnInteractEvent += TryStoreInInventory;
    }

    /// <summary>Removes the interaction listener before pooled or destroyed items are reused.</summary>
    private void OnDisable()
    {
        if (interactable != null)
            interactable.OnInteractEvent -= TryStoreInInventory;
    }

    /// <summary>
    /// Transfers this item to the active inventory on E. It stays in the world if the inventory
    /// is full or the item has no resource definition, so a pickup never silently deletes items.
    /// </summary>
    private void TryStoreInInventory(Interactor who)
    {
        if (Submitted || itemData == null || Inventory.Instance == null)
            return;
        if (TryGetComponent<Carriable>(out var carriable) && carriable.Holder != null)
            return;
        if (!Inventory.Instance.TryPickup(this))
            return;

        Destroy(gameObject);
    }

    public bool Matches(Resource requiredItem)
    {
        // A missing requirement means this is a legacy bin: preserve its old ingot-only rule.
        return requiredItem == null ? material == MaterialKind.IronIngot : itemData == requiredItem;
    }

    public bool TryMarkSubmitted(Resource requiredItem = null)
    {
        if (Submitted || !Matches(requiredItem))
            return false;
        Submitted = true;
        return true;
    }
}
