using UnityEngine;

/// <summary>The three stations in the first factory loop share the player's E interaction.</summary>
/// 

/// this is an abstract class / interface 
public abstract class FactoryInteractable : MonoBehaviour
{
    public virtual bool HoldToInteract => false;
    public abstract string GetPrompt(FactoryPlayer player);
    public abstract void Interact(FactoryPlayer player);
}
