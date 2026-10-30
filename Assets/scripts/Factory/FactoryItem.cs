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
