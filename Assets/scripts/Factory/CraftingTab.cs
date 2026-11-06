using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Only binds existing scene/prefab controls. No UI objects are created at runtime.</summary>
public class CraftingTab : MonoBehaviour
{
    [Serializable]
    public class RecipeEntry
    {
        public CraftingRecipe recipe;
        public Button button;
        public TMP_Text label;
    }

    public CanvasGroup panel;
    public RecipeEntry[] entries;
    public TMP_Text recipeName;
    public TMP_Text requirements;
    public TMP_Text status;
    public Button craftButton;
    public Button closeButton;
    public CraftingTable ActiveTable { get; private set; }
    public bool IsOpen { get; private set; }
    public CraftingRecipe SelectedRecipe { get; private set; }
    private FactoryPlayer player;
    private Inventory inventory;
    private string resultMessage;

    private void Awake()
    {
        for (int i = 0; entries != null && i < entries.Length; i++)
        {
            int index = i;
            if (entries[i].button != null) entries[i].button.onClick.AddListener(() => Select(index));
        }
        if (craftButton != null) craftButton.onClick.AddListener(CraftSelected);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        SetVisible(false);
    }

    public void Open(CraftingTable table, FactoryPlayer user)
    {
        if (table == null || !table.isActiveAndEnabled || user == null || panel == null
            || Inventory.Instance == null || !WithinReach(table, user)) return;
        var build = user.GetComponent<BuildController>();
        if (build != null && build.IsBuildModeActive) build.SetBuildModeActive(false);
        Inventory.Instance.SetOpen(false);
        Close();
        ActiveTable = table;
        player = user;
        inventory = Inventory.Instance;
        inventory.Changed += Refresh;
        resultMessage = string.Empty;
        SelectedRecipe = entries != null && entries.Length > 0 ? entries[0].recipe : null;
        SetVisible(true);
        InputManager.Instance?.EnterUIMode(this);
        Refresh();
    }

    public void Close()
    {
        if (inventory != null) inventory.Changed -= Refresh;
        inventory = null;
        ActiveTable = null;
        player = null;
        SetVisible(false);
        InputManager.Instance?.ExitUIMode(this);
    }

    private void SetVisible(bool visible)
    {
        IsOpen = visible;
        if (panel == null) return;
        panel.alpha = visible ? 1f : 0f;
        panel.interactable = panel.blocksRaycasts = visible;
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (ActiveTable == null || !ActiveTable.isActiveAndEnabled || player == null
            || !WithinReach(ActiveTable, player) || (InputManager.Instance != null && InputManager.Instance.UICancelPressed))
            Close();
    }

    private static bool WithinReach(CraftingTable table, FactoryPlayer user)
    {
        var collider = table.GetComponentInChildren<Collider>();
        Vector3 origin = user.viewCamera != null ? user.viewCamera.transform.position : user.transform.position;
        Vector3 target = collider != null ? collider.ClosestPoint(origin) : table.transform.position;
        return Vector3.Distance(origin, target) <= user.interactionRange;
    }

    public void Select(int index)
    {
        if (entries == null || index < 0 || index >= entries.Length) return;
        SelectedRecipe = entries[index].recipe;
        resultMessage = string.Empty;
        Refresh();
    }

    public void CraftSelected()
    {
        if (!IsOpen || player == null || ActiveTable == null || !ActiveTable.isActiveAndEnabled
            || !WithinReach(ActiveTable, player) || inventory == null) { Close(); return; }
        resultMessage = inventory.TryCraft(SelectedRecipe, out string reason)
            ? $"Crafted {SelectedRecipe.outputAmount} {SelectedRecipe.outputItem.DisplayName}." : reason;
        Refresh();
    }

    private void Refresh()
    {
        foreach (var entry in entries ?? Array.Empty<RecipeEntry>())
            if (entry.label != null && entry.recipe != null)
                entry.label.text = $"{entry.recipe.DisplayName}\n<size=70%>Makes {entry.recipe.outputAmount}</size>";
        if (recipeName != null) recipeName.text = SelectedRecipe != null ? SelectedRecipe.DisplayName : "Select a recipe";
        var text = new StringBuilder();
        if (SelectedRecipe != null && SelectedRecipe.IsValid)
        {
            foreach (var ingredient in SelectedRecipe.ingredients)
                text.AppendLine($"{ingredient.amount} {ingredient.item.DisplayName}  (Have {inventory?.GetItemCount(ingredient.item) ?? 0})");
            text.AppendLine($"\nProduces {SelectedRecipe.outputAmount} {SelectedRecipe.outputItem.DisplayName}");
        }
        if (requirements != null) requirements.text = text.ToString();
        string reason = "Inventory unavailable.";
        bool canCraft = inventory != null && inventory.CanCraft(SelectedRecipe, out reason);
        if (craftButton != null) craftButton.interactable = IsOpen && canCraft;
        if (status != null) status.text = !string.IsNullOrEmpty(resultMessage) ? resultMessage
            : canCraft ? "Ready to craft. Output goes to your inventory." : reason;
    }

    private void OnDisable() => Close();
}
