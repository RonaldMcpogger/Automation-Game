using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>Authors saved assets and scene references. Never runs when the game starts.</summary>
public static class CraftingSetup
{
    [InitializeOnLoadMethod]
    private static void AutoSync()
    {
        EditorApplication.delayCall += RequestSync;
    }

    private static void RequestSync()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            return;
        }

        try
        {
            SyncCraftingData();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CraftingSetup] AutoSync deferred: {ex.Message}");
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.delayCall += RequestSync;
        }
    }

    [MenuItem("Tools/Factory Crafting/Sync All Crafting and Machines")]
    public static void SyncCraftingData()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[CraftingSetup] Cannot sync crafting data while in Play Mode. Switch to Edit Mode first.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Recipes")) AssetDatabase.CreateFolder("Assets", "Recipes");
        if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects/Raw Resources"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects")) AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Raw Resources");
        }

        var ore = AssetDatabase.LoadAssetAtPath<Resource>("Assets/ScriptableObjects/Raw Resources/Iron Ore.asset");
        var ingot = AssetDatabase.LoadAssetAtPath<Resource>("Assets/ScriptableObjects/Raw Resources/Iron Ingot.asset");
        var plate = AssetDatabase.LoadAssetAtPath<Resource>("Assets/ScriptableObjects/Raw Resources/IronPlate.asset");

        var conveyorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Conveyor.prefab");
        var smelterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Factories/Smelter.prefab");
        var binPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Submission Bin.prefab");
        var presserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Factories/Presser.prefab");

        var conveyorItem = EnsureResource("Conveyor Belt", 100, conveyorPrefab);
        var smelterItem = EnsureResource("Smelter", 20, smelterPrefab);
        var binItem = EnsureResource("Submission Bin", 20, binPrefab);
        var presserItem = EnsureResource("Presser", 20, presserPrefab);

        // Update FactoryData costs
        var conveyorData = Resources.Load<FactoryData>("FactoryData/Conveyor");
        if (conveyorData != null)
        {
            conveyorData.buildCostItem = conveyorItem;
            conveyorData.buildCostAmount = 1;
            EditorUtility.SetDirty(conveyorData);
        }

        var smelterData = Resources.Load<FactoryData>("FactoryData/Smelter");
        if (smelterData != null)
        {
            smelterData.buildCostItem = smelterItem;
            smelterData.buildCostAmount = 1;
            EditorUtility.SetDirty(smelterData);
        }

        var binData = Resources.Load<FactoryData>("FactoryData/SubmissionBin");
        if (binData != null)
        {
            binData.buildCostItem = binItem;
            binData.buildCostAmount = 1;
            EditorUtility.SetDirty(binData);
        }

        var presserData = Resources.Load<FactoryData>("FactoryData/Presser");
        if (presserData != null)
        {
            presserData.buildCostItem = presserItem;
            presserData.buildCostAmount = 1;
            EditorUtility.SetDirty(presserData);
        }

        // Recipes (Option C durations: Ingot/Plate 0s for test compatibility, machines have durations)
        var ingotRecipe = RecipeWithDuration("Handcraft Iron Ingot", ore, 2, ingot, 1, 0f);
        var plateRecipe = RecipeWithDuration("Handcraft Iron Plate", ingot, 2, plate, 1, 0f);
        var conveyorRecipe = RecipeWithDuration("Craft Conveyor Belt", ingot, 1, conveyorItem, 2, 0.8f);
        var smelterRecipe = RecipeWithDuration("Craft Smelter", ingot, 4, smelterItem, 1, 2.5f);
        var binRecipe = RecipeWithDuration("Craft Submission Bin", ingot, 3, binItem, 1, 2.0f);
        var presserRecipe = RecipeWithDuration("Craft Presser", ingot, 4, presserItem, 1, 3.0f);

        var allRecipes = new[] { ingotRecipe, plateRecipe, conveyorRecipe, smelterRecipe, binRecipe, presserRecipe };

        // Update CraftingTabUI.prefab
        const string prefabPath = "Assets/prefabs/CraftingTabUI.prefab";
        if (File.Exists(prefabPath))
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var tab = root.GetComponent<CraftingTab>();
                var main = root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Craft List");
                var scroll = main.GetComponentInChildren<ScrollRect>(true);
                var content = scroll.content;

                var existingButtons = content.GetComponentsInChildren<Button>(true).ToList();
                while (existingButtons.Count < allRecipes.Length)
                {
                    var clone = Object.Instantiate(existingButtons[0], content);
                    clone.name = $"Recipe Button {existingButtons.Count + 1}";
                    existingButtons.Add(clone);
                }

                tab.entries = new CraftingTab.RecipeEntry[allRecipes.Length];
                for (int i = 0; i < allRecipes.Length; i++)
                {
                    tab.entries[i] = Entry(existingButtons[i], allRecipes[i]);
                }

                content.sizeDelta = new Vector2(0, allRecipes.Length * 96f + 20f);
                EditorUtility.SetDirty(tab);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Update MainScene.unity
        var currentScene = EditorSceneManager.GetActiveScene();
        if (!currentScene.isLoaded || currentScene.path != "Assets/Scenes/MainScene.unity")
        {
            currentScene = EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
        }

        if (currentScene.isLoaded)
        {
            var tabInScene = Object.FindAnyObjectByType<CraftingTab>(FindObjectsInactive.Include);
            if (tabInScene != null)
            {
                PrefabUtility.RevertPrefabInstance(tabInScene.gameObject, InteractionMode.AutomatedAction);
                var contentObj = tabInScene.GetComponentInChildren<ScrollRect>(true)?.content;
                if (contentObj != null)
                {
                    var buttonsInScene = contentObj.GetComponentsInChildren<Button>(true).ToList();
                    tabInScene.entries = new CraftingTab.RecipeEntry[allRecipes.Length];
                    for (int i = 0; i < allRecipes.Length && i < buttonsInScene.Count; i++)
                    {
                        tabInScene.entries[i] = Entry(buttonsInScene[i], allRecipes[i]);
                    }
                    contentObj.sizeDelta = new Vector2(0, allRecipes.Length * 96f + 20f);
                }
                EditorUtility.SetDirty(tabInScene);
                PrefabUtility.RecordPrefabInstancePropertyModifications(tabInScene);
            }
            EnsureUIInfrastructureInScene(currentScene);
            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);
        }

        EnsureUIInfrastructureInPlayerPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[CraftingSetup] Successfully synchronized all machine items, recipes, and UI prefab entries.");
    }

    private static Resource EnsureResource(string name, int maxStack, GameObject model = null)
    {
        string path = $"Assets/ScriptableObjects/Raw Resources/{name}.asset";
        var res = AssetDatabase.LoadAssetAtPath<Resource>(path);
        if (res != null) return res;
        res = ScriptableObject.CreateInstance<Resource>();
        res.itemName = name;
        res.maxStackSize = maxStack;
        res.objModel = model;
        AssetDatabase.CreateAsset(res, path);
        return res;
    }

    private static CraftingRecipe RecipeWithDuration(string name, Resource input, int amount, Resource output, int outputCount, float duration)
    {
        string path = $"Assets/Recipes/{name}.asset";
        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(path);
        if (recipe == null)
        {
            recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            AssetDatabase.CreateAsset(recipe, path);
        }
        recipe.recipeName = name.Replace("Craft ", "").Replace("Handcraft ", "");
        recipe.ingredients = new[] { new CraftingIngredient { item = input, amount = amount } };
        recipe.outputItem = output;
        recipe.outputAmount = outputCount;
        recipe.craftDuration = duration;
        EditorUtility.SetDirty(recipe);
        return recipe;
    }

    private static CraftingTab.RecipeEntry Entry(Button button, CraftingRecipe recipe)
    {
        button.name = recipe.DisplayName + " Recipe";
        button.onClick.RemoveAllListeners();
        StyleButton(button, recipe.DisplayName);
        return new CraftingTab.RecipeEntry { button = button, recipe = recipe, label = button.GetComponentInChildren<TMP_Text>(true) };
    }

    private static void StyleButton(Button button, string text)
    {
        var img = button.GetComponent<Image>();
        if (img != null) img.color = new Color(.18f, .38f, .48f);
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null) label = Label("Recipe Label", button.transform, 22);
        label.text = text;
        StyleText(label, 22);
        label.alignment = TextAlignmentOptions.Center;
        SetRect((RectTransform)label.transform, Vector2.zero, Vector2.one);
    }

    private static void StyleText(TMP_Text text, int size)
    {
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    private static TMP_Text Label(string name, Transform parent, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TMP_Text>();
        label.font = parent.GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;
        StyleText(label, size);
        SetRect((RectTransform)go.transform, Vector2.zero, Vector2.one);
        return label;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    public static void PolishSavedLayout()
    {
        SyncCraftingData();
    }

    public static void EnsureUIInfrastructureInScene(UnityEngine.SceneManagement.Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;

        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        foreach (var canvas in canvases)
        {
            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log($"[CraftingSetup] Added GraphicRaycaster to {canvas.gameObject.name} in scene {scene.name}");
            }
        }

        var es = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (es == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = go.GetComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            Debug.Log($"[CraftingSetup] Created EventSystem with InputSystemUIInputModule in scene {scene.name}");
        }
        else if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            var module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            Debug.Log($"[CraftingSetup] Added InputSystemUIInputModule to EventSystem in scene {scene.name}");
        }
    }

    public static void EnsureUIInfrastructureInPlayerPrefab()
    {
        string playerPrefabPath = "Assets/FPSCore/Runtime/Prefabs/Player.prefab";
        if (!File.Exists(playerPrefabPath)) return;

        using (var editScope = new PrefabUtility.EditPrefabContentsScope(playerPrefabPath))
        {
            var canvases = editScope.prefabContentsRoot.GetComponentsInChildren<Canvas>(true);
            foreach (var canvas in canvases)
            {
                if (canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
                    Debug.Log($"[CraftingSetup] Added GraphicRaycaster to {canvas.gameObject.name} in Player.prefab");
                }
            }
        }
    }
}
