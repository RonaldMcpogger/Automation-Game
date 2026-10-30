using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "newResource", menuName = "Automation Game /Resource Data")]
public class Resource : ScriptableObject
{
    [Tooltip("Prefab spawned when this item leaves a factory. It must have FactoryItem and Rigidbody components.")]
    public GameObject objModel;
    [FormerlySerializedAs("name")]
    public string itemName;
    [Range(1, 4)] public int Tier;

    public string DisplayName => string.IsNullOrWhiteSpace(itemName) ? name : itemName;
}
