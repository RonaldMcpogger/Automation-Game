using UnityEngine;

/// <summary>
/// A detached transform used to carry one stored item without putting its Rigidbody below the
/// Carry Box Rigidbody. Carriable calls this message just before it grabs a stored item.
/// </summary>
public sealed class StoredItemAnchor : MonoBehaviour
{
    private ItemContainer owner;

    public void Initialize(ItemContainer container) => owner = container;

    public void Clear(ItemContainer container)
    {
        if (owner == container)
            owner = null;
    }

    public void ReleaseStoredItem(GameObject item) => owner?.ReleaseItem(item);
}
