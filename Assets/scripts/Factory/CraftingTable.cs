using UnityEngine;

/// <summary>A placed workbench opens the scene-authored crafting panel.</summary>
public class CraftingTable : FactoryInteractable
{
    public override string GetPrompt(FactoryPlayer player) => "Press [{key}] to use crafting table";

    public override void Interact(FactoryPlayer player)
    {
        if (player == null || !isActiveAndEnabled) return;
        var tab = FindAnyObjectByType<CraftingTab>();
        if (tab != null) tab.Open(this, player);
    }

    private void OnDisable()
    {
        var tab = FindAnyObjectByType<CraftingTab>();
        if (tab != null && tab.ActiveTable == this) tab.Close();
    }
}
