using System.Collections.Generic;
using UnityEngine;

/// <summary>Keeps item physics stable while an item is fully inside this carry space.</summary>
[RequireComponent(typeof(BoxCollider))]
public class ItemContainer : MonoBehaviour
{
    [SerializeField, Tooltip("Trigger volume that defines the usable carry space.")]
    private BoxCollider carrySpace;

    [Header("Temporary Grid Layout")]
    [SerializeField, Tooltip("Maximum world-space size reserved for one stored item.")]
    private Vector3 cellSize = new Vector3(0.3f, 0.08f, 0.3f);

    [SerializeField, Range(0.05f, 1f), Tooltip("Items will never be shrunk below this fraction of their original scale.")]
    private float SizeMultiplier = 0.2f;

    [SerializeField, Min(0f), Tooltip("Empty space left around every grid cell so stored colliders do not touch.")]
    private float cellPadding = 0.005f;

    [Header("Debug")]
    [SerializeField, Tooltip("Logs why an entering collider was accepted or rejected. Turn this off after setup.")]
    private bool logStorageChecks = true;

    [SerializeField, Tooltip("Draw the carry-space bounds and storage cells when this object is selected.")]
    private bool drawGridGizmo = true;

    [SerializeField, Min(1), Tooltip("Safety limit for Scene-view gizmo lines on very small cells.")]
    private int maxGizmoCells = 256;


    private readonly Dictionary<GameObject, StoredItem> storedItems = new();
    // Dictionary = quick lookup; list = consistent placement order in the grid.
    private readonly List<GameObject> storedOrder = new();
    private Collider[] containerColliders;

    private sealed class StoredItem // a stored item class used for the item storage
    {
        public Transform originalParent;
        public Rigidbody body;
        public bool wasKinematic, usedGravity;
        public bool detectedCollisions;
        public RigidbodyConstraints constraints;
        public RigidbodyInterpolation interpolation;
        public CollisionDetectionMode collisionDetection;
        public Collider[] colliders;
        public Vector3 originalLocalScale;
    }

    private void Awake()
    {
        // Keep the collider assigned in the Inspector. This is important when the carry trigger
        // is a child object rather than the first BoxCollider on this GameObject.
        carrySpace ??= GetComponent<BoxCollider>();
        if (carrySpace == null)
            carrySpace = gameObject.AddComponent<BoxCollider>();
        carrySpace.isTrigger = true;
        // The trigger is a child of the Carry Box, but the box's solid walls are siblings under
        // the same Rigidbody. Gather the whole Rigidbody hierarchy, not just this trigger child.
        Rigidbody carryBody = GetComponentInParent<Rigidbody>();
        containerColliders = carryBody != null
            ? carryBody.GetComponentsInChildren<Collider>(true)
            : transform.root.GetComponentsInChildren<Collider>(true);
    }

    private void OnDisable()
    {
        var items = new List<GameObject>(storedItems.Keys);
        foreach (var item in items)
            ReleaseItem(item);
    }

    private void OnTriggerEnter(Collider other) => TryStoreColliderItem(other, true);

    // An item can enter before all its bounds fit. Keep checking until it is fully placed inside.
    private void OnTriggerStay(Collider other) => TryStoreColliderItem(other, false);

    private void OnTriggerExit(Collider other)
    {
        GameObject item = FindTaggedItemRoot(other);
        if (item != null && storedItems.ContainsKey(item) && !IsFullyInside(item))
            ReleaseItem(item);
    }

    private void TryStoreColliderItem(Collider other, bool logResult)
    {
        GameObject item = FindTaggedItemRoot(other);
        // This deliberately does NOT require the item to fit at its original size. If its bounds
        // can fit after shrinking no lower than SizeMultiplier, layout will move it into a cell.
        if (item == null)
        {
            if (logResult && logStorageChecks)
                Debug.Log($"{name}: entered collider {other.name} does not have an Items-tagged parent.", this);
            return;
        }
        if (storedItems.ContainsKey(item))
            return;
        if (!CanFitCell(item))
        {
            if (logResult && logStorageChecks)
                Debug.LogWarning(
                    $"{name}: detected tagged item {item.name}, but it cannot fit a grid cell. "
                    + "Increase Cell Size or lower Size Multiplier.", this
                );
            return;
        }
        StoreItem(item);
        if (logResult && logStorageChecks)
            Debug.Log($"{name}: stored {item.name}.", this);
    }

    private GameObject FindTaggedItemRoot(Collider other)
    {
        if (other == null) return null;
        for (Transform current = other.transform; current != null; current = current.parent)
            if (current.CompareTag("Items")) return current.gameObject;
        return null;
    }

    /// <summary>True only when every enabled collider on the item fits inside the carry-space bounds.</summary>
    public bool IsFullyInside(GameObject item)
    {
        if (item == null || carrySpace == null) return false;
        Bounds space = carrySpace.bounds;
        Collider[] itemColliders = item.GetComponentsInChildren<Collider>();
        if (itemColliders.Length == 0) return false;

        foreach (Collider itemCollider in itemColliders)
        {
            if (itemCollider == null || !itemCollider.enabled || itemCollider == carrySpace) continue;
            Bounds bounds = itemCollider.bounds;
            if (!space.Contains(bounds.min) || !space.Contains(bounds.max)) return false;
        }
        return true;
    }

    private void StoreItem(GameObject item)
    {
        Rigidbody body = item.GetComponent<Rigidbody>();
        var state = new StoredItem
        {
            originalParent = item.transform.parent,
            body = body,
            colliders = item.GetComponentsInChildren<Collider>(),
            originalLocalScale = item.transform.localScale,
        };

        if (body != null)
        {
            state.wasKinematic = body.isKinematic;
            state.usedGravity = body.useGravity;
            state.detectedCollisions = body.detectCollisions;
            state.constraints = body.constraints;
            state.interpolation = body.interpolation;
            state.collisionDetection = body.collisionDetectionMode;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            // Keep Collider components enabled for the Interactor raycast, but prevent these
            // stored colliders from producing any physical contacts that could push the box.
            body.detectCollisions = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        // Parent it to the container so it cannot lag behind when the player carries the container.
        item.transform.SetParent(transform, true);
        IgnoreContainerCollisions(state.colliders, true);
        storedItems.Add(item, state);
        storedOrder.Add(item);

        // A new item may change the position/scale of earlier items, so lay out all stored items.
        // If the grid is full, ReleaseItem puts this new item back exactly as it was.
        if (!ArrangeStoredItems())
            ReleaseItem(item);
    }

    /// <summary>Restores physics for one stored item; called automatically before a grab.</summary>
    public bool ReleaseItem(GameObject item)
    {
        if (item == null || !storedItems.TryGetValue(item, out StoredItem state)) return false;
        IgnoreContainerCollisions(state.colliders, false);
        item.transform.SetParent(state.originalParent, true);
        item.transform.localScale = state.originalLocalScale;

        if (state.body != null)
        {
            state.body.isKinematic = state.wasKinematic;
            state.body.useGravity = state.usedGravity;
            state.body.detectCollisions = state.detectedCollisions;
            state.body.constraints = state.constraints;
            state.body.interpolation = state.interpolation;
            state.body.collisionDetectionMode = state.collisionDetection;
            state.body.WakeUp();
        }
        storedItems.Remove(item);
        storedOrder.Remove(item);

        // Close the empty slot after this item is grabbed or otherwise removed.
        if (isActiveAndEnabled)
            ArrangeStoredItems();
        return true;
    }

    // Called by Carriable through SendMessage so FPSCore does not need a reference to this assembly.
    public void ReleaseStoredItem(GameObject item) => ReleaseItem(item);

    /// <summary>
    /// Current first version of packing: every item occupies one grid cell. An item is uniformly
    /// shrunk only enough to fit that cell, then centered using its collider bounds. This makes a
    /// safe base for a later variable-size packing / rotation / item-priority system.
    /// </summary>
    private bool ArrangeStoredItems()
    {
        if (carrySpace == null)
            return false;

        Bounds space = carrySpace.bounds;
        Vector3 interior = cellSize - Vector3.one * (cellPadding * 2f);
        if (interior.x <= 0f || interior.y <= 0f || interior.z <= 0f)
            return false;

        // Floor means the final partial row/column is never used, so every cell stays in bounds.
        int xCount = Mathf.FloorToInt(space.size.x / cellSize.x);
        int yCount = Mathf.FloorToInt(space.size.y / cellSize.y);
        int zCount = Mathf.FloorToInt(space.size.z / cellSize.z);
        int capacity = xCount * yCount * zCount;
        if (xCount <= 0 || yCount <= 0 || zCount <= 0 || storedOrder.Count > capacity)
            return false;

        for (int index = 0; index < storedOrder.Count; index++)
        {
            GameObject item = storedOrder[index];
            if (item == null || !storedItems.TryGetValue(item, out StoredItem state))
                return false;

            // Reset first. Without this, each re-layout would shrink an already-shrunk item again.
            item.transform.localScale = state.originalLocalScale;
            Bounds originalBounds = GetItemBounds(state.colliders);
            float scale = GetCheckFittingScale(originalBounds.size, interior);
            if (scale < SizeMultiplier)
                return false;
            item.transform.localScale = state.originalLocalScale * scale;

            // Fill X first, then Z, then stack upward on Y.
            int x = index % xCount;
            int z = (index / xCount) % zCount;
            int y = index / (xCount * zCount);
            Vector3 cellCenter = space.min + new Vector3(
                (x + 0.5f) * cellSize.x,
                (y + 0.5f) * cellSize.y,
                (z + 0.5f) * cellSize.z
            );

            // Models often have an offset pivot, so center the real collider bounds, not the pivot.
            Bounds scaledBounds = GetItemBounds(state.colliders);
            item.transform.position += cellCenter - scaledBounds.center;
            if (!IsFullyInside(item))
                return false;
        }
        return true;
    }

    private bool CanFitCell(GameObject item)
    {
        if (item == null || carrySpace == null)
            return false;
        Vector3 interior = cellSize - Vector3.one * (cellPadding * 2f);
        if (interior.x <= 0f || interior.y <= 0f || interior.z <= 0f)
            return false;

        Bounds space = carrySpace.bounds;
        int xCount = Mathf.FloorToInt(space.size.x / cellSize.x);
        int yCount = Mathf.FloorToInt(space.size.y / cellSize.y);
        int zCount = Mathf.FloorToInt(space.size.z / cellSize.z);
        if (xCount <= 0 || yCount <= 0 || zCount <= 0 || storedOrder.Count >= xCount * yCount * zCount)
            return false;

        Bounds itemBounds = GetItemBounds(item.GetComponentsInChildren<Collider>());
        return GetCheckFittingScale(itemBounds.size, interior) >= SizeMultiplier;
    }

    // Uniform scaling preserves the object's proportions. 1 means its current size already fits.
    private static float GetCheckFittingScale(Vector3 size, Vector3 available)
    {
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            return 0f;
        return Mathf.Min(1f, available.x / size.x, available.y / size.y, available.z / size.z);
    }

    private static Bounds GetItemBounds(Collider[] colliders)
    {
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled)
                continue;
            if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        return bounds;
    }

    private void IgnoreContainerCollisions(Collider[] itemColliders, bool ignore)
    {
        if (itemColliders == null || containerColliders == null)
            return;
        foreach (Collider itemCollider in itemColliders)
            foreach (Collider containerCollider in containerColliders)
                if (itemCollider != null && containerCollider != null && itemCollider != containerCollider)
                    Physics.IgnoreCollision(itemCollider, containerCollider, ignore);
    }

    private void OnValidate() // validation check
    {
        carrySpace ??= GetComponent<BoxCollider>();
        if (carrySpace != null) carrySpace.isTrigger = true;
        cellSize.x = Mathf.Max(0.001f, cellSize.x);
        cellSize.y = Mathf.Max(0.001f, cellSize.y);
        cellSize.z = Mathf.Max(0.001f, cellSize.z);
        SizeMultiplier = Mathf.Clamp(SizeMultiplier, 0.05f, 1f);
        maxGizmoCells = Mathf.Max(1, maxGizmoCells);
    }

    // This uses carrySpace.bounds and cellSize, exactly like ArrangeStoredItems, so the cyan
    // wire cubes shown in the Scene view match the locations used by the storage layout.
    private void OnDrawGizmosSelected()
    {
        if (!drawGridGizmo)
            return;

        BoxCollider spaceCollider = carrySpace != null ? carrySpace : GetComponent<BoxCollider>();
        if (spaceCollider == null)
            return;

        Bounds space = spaceCollider.bounds;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(space.center, space.size);

        if (cellSize.x <= 0f || cellSize.y <= 0f || cellSize.z <= 0f)
            return;

        int xCount = Mathf.FloorToInt(space.size.x / cellSize.x);
        int yCount = Mathf.FloorToInt(space.size.y / cellSize.y);
        int zCount = Mathf.FloorToInt(space.size.z / cellSize.z);
        int total = xCount * yCount * zCount;
        if (xCount <= 0 || yCount <= 0 || zCount <= 0)
            return;

        // Avoid filling the editor with thousands of wireframes if a cell size is tiny.
        if (total > maxGizmoCells)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(space.center, space.size);
            return;
        }

        Gizmos.color = new Color(0f, 0.85f, 1f, 0.75f);
        Vector3 visibleCellSize = Vector3.Max(
            Vector3.one * 0.001f,
            cellSize - Vector3.one * (cellPadding * 2f)
        );
        for (int y = 0; y < yCount; y++)
            for (int z = 0; z < zCount; z++)
                for (int x = 0; x < xCount; x++)
                {
                    Vector3 center = space.min + new Vector3(
                        (x + 0.5f) * cellSize.x,
                        (y + 0.5f) * cellSize.y,
                        (z + 0.5f) * cellSize.z
                    );
                    Gizmos.DrawWireCube(center, visibleCellSize);
                }
    }
}
