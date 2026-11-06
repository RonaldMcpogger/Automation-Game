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
    public bool IsCrafting => isCrafting;
    public float CraftProgress => isCrafting && targetDuration > 0f ? Mathf.Clamp01(craftTimer / targetDuration) : 0f;

    private FactoryPlayer player;
    private Inventory inventory;
    private string resultMessage;
    private bool isCrafting;
    private float craftTimer;
    private float targetDuration;

    private void Awake()
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].button != null && entries[i].label == null)
                    entries[i].label = entries[i].button.GetComponentInChildren<TMP_Text>(true);
            }
        }
        for (int i = 0; entries != null && i < entries.Length; i++)
        {
            int index = i;
            if (entries[i] != null && entries[i].button != null) entries[i].button.onClick.AddListener(() => Select(index));
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
        isCrafting = false;
        craftTimer = 0f;
        SelectedRecipe = entries != null && entries.Length > 0 ? entries[0].recipe : null;
        SetVisible(true);
        InputManager.Instance?.EnterUIMode(this);
        Refresh();
    }

    public void Close()
    {
        if (isCrafting) CancelCraft();
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
        {
            if (isCrafting) CancelCraft();
            Close();
            return;
        }

        if (isCrafting)
        {
            craftTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(craftTimer / Mathf.Max(0.01f, targetDuration));
            int pct = Mathf.RoundToInt(progress * 100f);
            if (status != null)
                status.text = $"Crafting {SelectedRecipe?.DisplayName}... {pct}%";

            if (craftTimer >= targetDuration)
            {
                CompleteCraft();
            }
        }
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
        if (isCrafting) CancelCraft();
        SelectedRecipe = entries[index].recipe;
        resultMessage = string.Empty;
        Refresh();
    }

    public void CraftSelected()
    {
        if (!IsOpen || isCrafting || player == null || ActiveTable == null || !ActiveTable.isActiveAndEnabled
            || !WithinReach(ActiveTable, player) || inventory == null) { Close(); return; }

        if (!inventory.CanCraft(SelectedRecipe, out string reason))
        {
            resultMessage = reason;
            Refresh();
            return;
        }

        if (SelectedRecipe.craftDuration > 0f)
        {
            isCrafting = true;
            craftTimer = 0f;
            targetDuration = SelectedRecipe.craftDuration;
            resultMessage = string.Empty;
            Refresh();
        }
        else
        {
            CompleteCraft();
        }
    }

    public void CompleteCraft()
    {
        isCrafting = false;
        craftTimer = 0f;
        if (inventory != null && SelectedRecipe != null)
        {
            resultMessage = inventory.TryCraft(SelectedRecipe, out string reason)
                ? $"Crafted {SelectedRecipe.outputAmount} {SelectedRecipe.outputItem.DisplayName}."
                : reason;
        }
        else
        {
            resultMessage = "Inventory unavailable.";
        }
        Refresh();
    }

    public void CancelCraft()
    {
        isCrafting = false;
        craftTimer = 0f;
        resultMessage = "Crafting cancelled.";
        Refresh();
    }

    private void Refresh()
    {
        foreach (var entry in entries ?? Array.Empty<RecipeEntry>())
        {
            if (entry == null || entry.recipe == null) continue;
            var lbl = entry.label ?? entry.button?.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null)
                lbl.text = $"{entry.recipe.DisplayName}\n<size=70%>Makes {entry.recipe.outputAmount}</size>";
        }
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
        if (craftButton != null)
        {
            craftButton.interactable = IsOpen && canCraft && !isCrafting;
            var btnText = craftButton.GetComponentInChildren<TMP_Text>();
            if (btnText != null)
                btnText.text = isCrafting ? "CRAFTING..." : "CRAFT ONE";
        }
        if (status != null)
        {
            if (isCrafting)
            {
                int pct = Mathf.RoundToInt((craftTimer / Mathf.Max(0.01f, targetDuration)) * 100f);
                status.text = $"Crafting {SelectedRecipe?.DisplayName}... {pct}%";
            }
            else
            {
                string durationNotice = SelectedRecipe != null && SelectedRecipe.craftDuration > 0f
                    ? $" (Takes {SelectedRecipe.craftDuration:F1}s)" : "";
                status.text = !string.IsNullOrEmpty(resultMessage) ? resultMessage
                    : canCraft ? $"Ready to craft{durationNotice}. Output goes to your inventory." : reason;
            }
        }
    }

    private void OnDisable() => Close();
}
