using UnityEngine;

/// <summary>Marks real machine output so loose test cubes cannot score in the bin.</summary>
[RequireComponent(typeof(Rigidbody))]
public class FactoryItem : MonoBehaviour
{
    public enum MaterialKind { IronOre, IronIngot }
    public MaterialKind material = MaterialKind.IronIngot;
    public bool Submitted { get; private set; }

    public bool TryMarkSubmitted()
    {
        if (Submitted || material != MaterialKind.IronIngot)
            return false;
        Submitted = true;
        return true;
    }
}
