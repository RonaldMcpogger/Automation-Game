using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "newResource", menuName = "Automation Game /Resource Data")]
public class Resource : ScriptableObject
{
    [Tooltip("Prefab spawned when this item leaves a factory. It must have FactoryItem and Rigidbody components.")]
    public GameObject objModel;
    [FormerlySerializedAs("name")]
    public string itemName;

    public Sprite ItemImage;

    public int maxStackSize = stackDefault; // maximum number of items that can be stacked in one inventory slot

    private const int stackDefault = 60;
    public string DisplayName => string.IsNullOrWhiteSpace(itemName) ? name : itemName;
}
