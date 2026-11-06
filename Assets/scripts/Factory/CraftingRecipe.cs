using System;
using UnityEngine;

[Serializable]
public struct CraftingIngredient
{
    public Resource item;
    [Min(1)] public int amount;
}

[CreateAssetMenu(fileName = "Recipe", menuName = "Automation Game/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    public string recipeName;
    public CraftingIngredient[] ingredients;
    public Resource outputItem;
    [Min(1)] public int outputAmount = 1;
    public string DisplayName => string.IsNullOrWhiteSpace(recipeName) ? name : recipeName;
    public bool IsValid
    {
        get
        {
            if (outputItem == null || outputAmount <= 0 || ingredients == null || ingredients.Length == 0) return false;
            foreach (var ingredient in ingredients)
                if (ingredient.item == null || ingredient.amount <= 0) return false;
            return true;
        }
    }
}
