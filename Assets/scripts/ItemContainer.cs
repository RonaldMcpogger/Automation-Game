using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Packs Items-tagged rigidbodies into a carry volume.
///
/// Stored items are intentionally never parented below the Carry Box Rigidbody. Nested moving
/// rigidbodies have ambiguous physics ownership and cause the clipping/launch bugs this component
/// is designed to prevent. Instead, each stored body is kinematic and follows a cached carry-space
/// pose during the physics step.
///
/// Storage lifecycle:
/// 1. An Items-tagged collider enters CarrySpace and calls TryStoreColliderItem.
/// 2. The component verifies that the item is not held, has a root Rigidbody, has a free cell,
///    and can fit after a uniform scale reduction.
/// 3. StoreItem saves the item's original transform/physics settings, resets its rotation to
///    world (0, 0, 0), makes the body kinematic, and ignores contacts with the Carry Box only.
/// 4. ArrangeStoredItems assigns a grid cell, calculates the item's one-time fit scale, centres
///    its collider in that cell, and caches the final pose relative to the carry area.
/// 5. FixedUpdate and LateUpdate rebuild each cached pose from the moving carry area so contents
///    move and rotate with the Carry Box without becoming Rigidbody children of it.
/// 6. When Carriable grabs an item, StoredItemAnchor calls ReleaseItem. The original scale and
///    Rigidbody settings are restored, the item is removed from the grid, and remaining contents
///    are repositioned without being rescaled.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ItemContainer : MonoBehaviour
{
    [SerializeField, Tooltip("Trigger volume that defines the usable carry space.")]
    private BoxCollider carrySpace;

    [Header("Grid Layout")]
    [SerializeField, Tooltip("World-space size reserved for one stored item.")]
    private Vector3 cellSize = new Vector3(0.3f, 0.08f, 0.3f);

    [SerializeField, Range(0.05f, 1f), Tooltip("Items will never be shrunk below this fraction of their original scale.")]
    [FormerlySerializedAs("SizeMultiplier")]
    private float sizeMultiplier = 0.2f;

    [SerializeField, Min(0f), Tooltip("Empty space left around every grid cell so stored colliders do not touch.")]
    private float cellPadding = 0.005f;

    [Header("Debug")]
    [SerializeField, Tooltip("Logs why an entering collider was accepted or rejected. Turn this off after setup.")]
    private bool logStorageChecks;

    [SerializeField, Tooltip("Draw the carry-space bounds and storage cells when this object is selected.")]
    private bool drawGridGizmo = true;

    [SerializeField, Min(1), Tooltip("Safety limit for Scene-view gizmo lines on very small cells.")]
    private int maxGizmoCells = 256;

    private readonly Dictionary<GameObject, StoredItem> storedItems = new();
    private readonly List<GameObject> storedOrder = new();
    private Collider[] containerColliders;

    /// <summary>All state needed to restore one item exactly after it leaves this container.</summary>
    private sealed class StoredItem
    {
        public Transform originalParent;
        public Rigidbody body;
        public bool wasKinematic;
        public bool usedGravity;
        public bool detectedCollisions;
        public RigidbodyConstraints constraints;
        public RigidbodyInterpolation interpolation;
        public CollisionDetectionMode collisionDetection;
        public Collider[] colliders;
        public Vector3 originalLocalScale;
        public Vector3 originalPosition;
        public Quaternion originalRotation;
        public float storedScale;
        public Vector3 carryLocalPosition;
        public Quaternion carryLocalRotation;
        public bool hasStoredPose;
    }

    /// <summary>Initialises the trigger volume and caches every physical collider belonging to the box.</summary>
    private void Awake()
    {
        carrySpace ??= GetComponent<BoxCollider>();
        if (carrySpace == null)
            carrySpace = gameObject.AddComponent<BoxCollider>();
        carrySpace.isTrigger = true;

        Rigidbody containerBody = GetComponentInParent<Rigidbody>();
        containerColliders = containerBody != null
            ? containerBody.GetComponentsInChildren<Collider>(true)
            : transform.root.GetComponentsInChildren<Collider>(true);
    }

    /// <summary>Releases every item if this component or its Carry Box is disabled/destroyed.</summary>
    private void OnDisable()
    {
        // Never leave invisible kinematic items behind when a box is disabled or destroyed.
        var items = new List<GameObject>(storedItems.Keys);
        foreach (GameObject item in items)
            ReleaseItemInternal(item, false);
    }

    /// <summary>Updates stored Rigidbody poses before the next physics simulation step.</summary>
    private void FixedUpdate()
    {
        RemoveDestroyedItems();
        foreach (GameObject item in storedOrder)
            if (item != null && storedItems.TryGetValue(item, out StoredItem state))
                SyncStoredPose(state, false);
    }

    /// <summary>Applies one final pose update after normal frame movement to remove visible carry lag.</summary>
    private void LateUpdate()
    {
        // Carriable moves its Rigidbody after some FixedUpdate callbacks. A final direct pose
        // write keeps the contents visually and physically locked to the box in that same frame.
        foreach (GameObject item in storedOrder)
            if (item != null && storedItems.TryGetValue(item, out StoredItem state))
                SyncStoredPose(state, true);
    }

    /// <summary>Starts storage validation when a collider first enters the carry-area trigger.</summary>
    private void OnTriggerEnter(Collider other) => TryStoreColliderItem(other, true);

    /// <summary>Retries validation while an item settles through the carry-area trigger.</summary>
    // A valid item can first touch the rim before it settles inside the trigger.
    private void OnTriggerStay(Collider other) => TryStoreColliderItem(other, false);

    /// <summary>Resolves an entered collider to its Items-tagged root and performs all safe-to-store checks.</summary>
    private void TryStoreColliderItem(Collider other, bool logResult)
    {
        GameObject item = FindTaggedItemRoot(other);
        if (item == null)
        {
            if (logResult && logStorageChecks)
                Debug.Log($"{name}: entered collider {other.name} does not have an Items-tagged parent.", this);
            return;
        }

        if (storedItems.ContainsKey(item) || IsBeingHeld(item))
            return;

        if (!item.TryGetComponent(out Rigidbody body))
        {
            if (logResult && logStorageChecks)
                Debug.LogWarning($"{name}: {item.name} needs a Rigidbody on its Items-tagged root to be stored.", this);
            return;
        }

        if (!CanFitCell(item))
        {
            if (logResult && logStorageChecks)
                Debug.LogWarning($"{name}: {item.name} cannot fit a grid cell. Increase Cell Size or lower Size Multiplier.", this);
            return;
        }

        StoreItem(item, body);
        if (logResult && logStorageChecks && storedItems.ContainsKey(item))
            Debug.Log($"{name}: stored {item.name}.", this);
    }

    /// <summary>Prevents a held or externally claimed resource from being re-stored by this trigger.</summary>
    private static bool IsBeingHeld(GameObject item)
    {
        Carriable carriable = item.GetComponent<Carriable>();
        return carriable != null && (carriable.IsHeld || carriable.IsClaimed);
    }

    /// <summary>Walks upward from a hit collider and returns the first parent tagged <c>Items</c>.</summary>
    private static GameObject FindTaggedItemRoot(Collider other)
    {
        if (other == null)
            return null;

        for (Transform current = other.transform; current != null; current = current.parent)
            if (current.CompareTag("Items"))
                return current.gameObject;
        return null;
    }

    /// <summary>Returns true when this container has locked the supplied item.</summary>
    public bool Contains(GameObject item) => item != null && storedItems.ContainsKey(item);

    /// <summary>Saves original state, applies stable stored physics, and assigns the item its first grid cell.</summary>
    private void StoreItem(GameObject item, Rigidbody body)
    {
        var state = new StoredItem
        {
            originalParent = item.transform.parent,
            body = body,
            wasKinematic = body.isKinematic,
            usedGravity = body.useGravity,
            detectedCollisions = body.detectCollisions,
            constraints = body.constraints,
            interpolation = body.interpolation,
            collisionDetection = body.collisionDetectionMode,
            colliders = item.GetComponentsInChildren<Collider>(),
            originalLocalScale = item.transform.localScale,
            originalPosition = body.position,
            originalRotation = body.rotation,
        };

        // Carriable uses this hook to release a directly grabbed stored item without creating an
        // Assembly-CSharp dependency in FPSCore and without needing transform parenting.
        StoredItemAnchor anchor = item.GetComponent<StoredItemAnchor>();
        if (anchor == null)
            anchor = item.AddComponent<StoredItemAnchor>();
        anchor.Initialize(this);

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.useGravity = false;
        // detectCollisions must stay on: the Interactor raycast needs these colliders to remain
        // queryable. Ignore only the Carry Box colliders so stored items cannot push its walls.
        body.detectCollisions = state.detectedCollisions;
        body.constraints = state.constraints;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        // All resources begin aligned in the carry cells. The cached local rotation below then
        // makes them rotate with the Carry Box after they have been stored.
        body.rotation = Quaternion.identity;
        IgnoreContainerCollisions(state.colliders, true);

        storedItems.Add(item, state);
        storedOrder.Add(item);

        // No SetParent call: the item stays outside the Carry Box Rigidbody hierarchy.
        if (!ArrangeStoredItems())
            ReleaseItemInternal(item, true);
    }

    /// <summary>Restores one stored item's transform scale and original Rigidbody settings.</summary>
    public bool ReleaseItem(GameObject item) => ReleaseItemInternal(item, true);

    /// <summary>Message entry point used by StoredItemAnchor and legacy SendMessage callers.</summary>
    public void ReleaseStoredItem(GameObject item) => ReleaseItem(item);

    /// <summary>Restores one item and optionally compacts the remaining grid cells.</summary>
    private bool ReleaseItemInternal(GameObject item, bool relayoutRemainingItems)
    {
        if (item == null || !storedItems.TryGetValue(item, out StoredItem state))
            return false;

        // A grab may happen between FixedUpdate calls, so release at the exact current box pose.
        // If packing failed before a pose was assigned, put the item back where it entered.
        if (state.hasStoredPose)
            SyncStoredPose(state, true);
        else if (state.body != null)
        {
            state.body.position = state.originalPosition;
            state.body.rotation = state.originalRotation;
        }
        if (item.transform.parent != state.originalParent)
            item.transform.SetParent(state.originalParent, true);
        item.transform.localScale = state.originalLocalScale;

        IgnoreContainerCollisions(state.colliders, false);
        if (state.body != null)
        {
            state.body.linearVelocity = Vector3.zero;
            state.body.angularVelocity = Vector3.zero;
            state.body.isKinematic = state.wasKinematic;
            state.body.useGravity = state.usedGravity;
            state.body.detectCollisions = state.detectedCollisions;
            state.body.constraints = state.constraints;
            state.body.interpolation = state.interpolation;
            state.body.collisionDetectionMode = state.collisionDetection;
            state.body.WakeUp();
        }

        if (item.TryGetComponent(out StoredItemAnchor anchor))
            anchor.Clear(this);

        storedItems.Remove(item);
        storedOrder.Remove(item);
        if (relayoutRemainingItems && isActiveAndEnabled)
            ArrangeStoredItems();
        return true;
    }

    /// <summary>
    /// Converts a cached carry-local pose into world space and writes it to the stored Rigidbody.
    /// Both paths use direct writes so Rigidbody constraints cannot leave an item behind.
    /// </summary>
    private void SyncStoredPose(StoredItem state, bool immediate)
    {
        if (state.body == null || carrySpace == null)
            return;

        Vector3 position = carrySpace.transform.TransformPoint(state.carryLocalPosition);
        Quaternion rotation = carrySpace.transform.rotation * state.carryLocalRotation;
        if (immediate)
        {
            state.body.position = position;
            state.body.rotation = rotation;
        }
        else
        {
            // Rigidbody.position bypasses position constraints. This matters for resources whose
            // prefab has frozen axes, and avoids a one-physics-step lag behind a carried box.
            state.body.position = position;
            state.body.rotation = rotation;
        }
    }

    /// <summary>
    /// Places every stored item in X, then Z, then Y order and caches each pose in carry-space
    /// coordinates. An item's scale is calculated only the first time it is stored.
    /// </summary>
    private bool ArrangeStoredItems()
    {
        if (carrySpace == null)
            return false;

        Vector3 interior = cellSize - Vector3.one * (cellPadding * 2f);
        if (interior.x <= 0f || interior.y <= 0f || interior.z <= 0f)
            return false;

        if (!TryGetGrid(out Vector3 localCellSize, out int xCount, out int yCount, out int zCount))
            return false;
        int capacity = xCount * yCount * zCount;
        if (xCount <= 0 || yCount <= 0 || zCount <= 0 || storedOrder.Count > capacity)
            return false;

        for (int index = 0; index < storedOrder.Count; index++)
        {
            GameObject item = storedOrder[index];
            if (item == null || !storedItems.TryGetValue(item, out StoredItem state) || state.body == null)
                return false;

            // Determine a single scalar once, on first storage. Later grid layouts only move the
            // item; they never resize existing contents when a different item is added.
            if (state.storedScale <= 0f)
            {
                item.transform.localScale = state.originalLocalScale;
                Bounds originalBounds = GetItemBounds(item.GetComponentsInChildren<Collider>());
                state.storedScale = GetFittingScale(originalBounds.size, interior);
                if (state.storedScale < sizeMultiplier)
                    return false;
            }
            item.transform.localScale = state.originalLocalScale * state.storedScale;

            int x = index % xCount;
            int z = (index / xCount) % zCount;
            int y = index / (xCount * zCount);
            Vector3 cellCenter = carrySpace.transform.TransformPoint(
                carrySpace.center - Vector3.Scale(carrySpace.size, Vector3.one * 0.5f)
                + Vector3.Scale(localCellSize, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f))
            );

            // Resources can have offset pivots, so centre their actual collider bounds.
            Bounds scaledBounds = GetItemBounds(item.GetComponentsInChildren<Collider>());
            if (scaledBounds.size == Vector3.zero)
                return false;
            state.body.position += cellCenter - scaledBounds.center;
            state.carryLocalPosition = carrySpace.transform.InverseTransformPoint(state.body.position);
            state.carryLocalRotation = Quaternion.Inverse(carrySpace.transform.rotation) * state.body.rotation;
            state.hasStoredPose = true;
        }
        return true;
    }

    /// <summary>Checks capacity and verifies that an item can fit a padded cell above the scale limit.</summary>
    private bool CanFitCell(GameObject item)
    {
        if (item == null || carrySpace == null)
            return false;

        Vector3 interior = cellSize - Vector3.one * (cellPadding * 2f);
        if (interior.x <= 0f || interior.y <= 0f || interior.z <= 0f
            || !TryGetGrid(out _, out int xCount, out int yCount, out int zCount))
            return false;
        if (storedOrder.Count >= xCount * yCount * zCount)
            return false;

        Bounds itemBounds = GetItemBounds(item.GetComponentsInChildren<Collider>());
        return GetFittingScale(itemBounds.size, interior) >= sizeMultiplier;
    }

    /// <summary>
    /// Converts world-space cell dimensions to the carry collider's local axes and counts complete
    /// cells. This keeps the grid valid when the Carry Box is scaled or rotating after a throw.
    /// </summary>
    private bool TryGetGrid(out Vector3 localCellSize, out int xCount, out int yCount, out int zCount)
    {
        Vector3 scale = carrySpace.transform.lossyScale;
        scale.x = Mathf.Abs(scale.x);
        scale.y = Mathf.Abs(scale.y);
        scale.z = Mathf.Abs(scale.z);
        if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
        {
            localCellSize = default;
            xCount = yCount = zCount = 0;
            return false;
        }

        Vector3 worldSize = Vector3.Scale(carrySpace.size, scale);
        xCount = Mathf.FloorToInt(worldSize.x / cellSize.x);
        yCount = Mathf.FloorToInt(worldSize.y / cellSize.y);
        zCount = Mathf.FloorToInt(worldSize.z / cellSize.z);
        localCellSize = new Vector3(cellSize.x / scale.x, cellSize.y / scale.y, cellSize.z / scale.z);
        return xCount > 0 && yCount > 0 && zCount > 0;
    }

    /// <summary>Returns the largest uniform scale from 0 to 1 that fits an item's bounds into a cell.</summary>
    private static float GetFittingScale(Vector3 size, Vector3 available)
    {
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            return 0f;
        return Mathf.Min(1f, available.x / size.x, available.y / size.y, available.z / size.z);
    }

    /// <summary>Combines enabled solid colliders into one world-space box used for sizing and centring.</summary>
    private static Bounds GetItemBounds(Collider[] colliders)
    {
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;
            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        return bounds;
    }

    /// <summary>Enables/disables contacts with the Carry Box only; item colliders remain raycastable.</summary>
    private void IgnoreContainerCollisions(Collider[] itemColliders, bool ignore)
    {
        if (itemColliders == null || containerColliders == null)
            return;

        foreach (Collider itemCollider in itemColliders)
            foreach (Collider containerCollider in containerColliders)
                if (itemCollider != null && containerCollider != null && itemCollider != containerCollider)
                    Physics.IgnoreCollision(itemCollider, containerCollider, ignore);
    }

    /// <summary>Removes destroyed Unity objects from bookkeeping before their poses are updated.</summary>
    private void RemoveDestroyedItems()
    {
        var destroyedItems = new List<GameObject>();
        foreach (GameObject item in storedItems.Keys)
            if (item == null)
                destroyedItems.Add(item);
        foreach (GameObject item in destroyedItems)
            storedItems.Remove(item);

        for (int index = storedOrder.Count - 1; index >= 0; index--)
        {
            GameObject item = storedOrder[index];
            if (item != null)
                continue;
            storedOrder.RemoveAt(index);
        }
    }

    /// <summary>Clamps Inspector values and makes sure the chosen carry-space collider is a trigger.</summary>
    private void OnValidate()
    {
        carrySpace ??= GetComponent<BoxCollider>();
        if (carrySpace != null)
            carrySpace.isTrigger = true;
        cellSize.x = Mathf.Max(0.001f, cellSize.x);
        cellSize.y = Mathf.Max(0.001f, cellSize.y);
        cellSize.z = Mathf.Max(0.001f, cellSize.z);
        sizeMultiplier = Mathf.Clamp(sizeMultiplier, 0.05f, 1f);
        maxGizmoCells = Mathf.Max(1, maxGizmoCells);
    }

    /// <summary>Draws the oriented carry volume and padded grid cells in the Scene view.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawGridGizmo)
            return;

        BoxCollider spaceCollider = carrySpace != null ? carrySpace : GetComponent<BoxCollider>();
        if (spaceCollider == null)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = spaceCollider.transform.localToWorldMatrix;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(spaceCollider.center, spaceCollider.size);

        if (!TryGetGrid(out Vector3 localCellSize, out int xCount, out int yCount, out int zCount))
        {
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
            return;
        }
        int total = xCount * yCount * zCount;
        if (xCount <= 0 || yCount <= 0 || zCount <= 0 || total > maxGizmoCells)
        {
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
            return;
        }

        Gizmos.color = new Color(0f, 0.85f, 1f, 0.75f);
        Vector3 localPadding = new Vector3(
            cellPadding / Mathf.Abs(spaceCollider.transform.lossyScale.x),
            cellPadding / Mathf.Abs(spaceCollider.transform.lossyScale.y),
            cellPadding / Mathf.Abs(spaceCollider.transform.lossyScale.z)
        );
        Vector3 visibleCellSize = Vector3.Max(Vector3.one * 0.001f, localCellSize - localPadding * 2f);
        for (int y = 0; y < yCount; y++)
            for (int z = 0; z < zCount; z++)
                for (int x = 0; x < xCount; x++)
                {
                    Vector3 center = spaceCollider.center - Vector3.Scale(spaceCollider.size, Vector3.one * 0.5f)
                        + Vector3.Scale(localCellSize, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)
                    );
                    Gizmos.DrawWireCube(center, visibleCellSize);
                }
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
