using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Authors saved assets and scene references. Never runs when the game starts.</summary>
public static class CraftingSetup
{
    // Batch visual check of the saved controls, without saving preview state into the scene.
    public static void CapturePreview()
    {
        PolishSavedLayout();
        EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
        ShaderUtil.allowAsyncCompilation = false;
        int frames = 0;
        EditorApplication.CallbackFunction draw = null;
        draw = () =>
        {
            if (++frames < 12) return;
            EditorApplication.update -= draw;
            try
            {
                var tab = Object.FindAnyObjectByType<CraftingTab>();
                foreach (var group in Object.FindObjectsByType<CanvasGroup>(FindObjectsSortMode.None)) group.alpha = 0;
                tab.panel.alpha = 1;
                tab.craftButton.interactable = true;
                tab.requirements.text = "2 Iron Ore  (Have 6)\n\nProduces 1 Iron Ingot";
                tab.status.text = "Ready to craft. Output goes to your inventory.";
                var camera = new GameObject("Crafting Preview Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.04f, .06f, .08f);
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;
                camera.transform.position = new Vector3(-7, 2, -6);
                camera.transform.LookAt(new Vector3(-10, 1, -1));
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var canvas = tab.GetComponentInParent<Canvas>().rootCanvas;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                var target = new RenderTexture(1440, 900, 24);
                target.Create();
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes("Logs/crafting-preview.png", image.EncodeToPNG());
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(camera.gameObject);
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        };
        EditorApplication.update += draw;
    }

    [MenuItem("Tools/Factory Crafting/Set Up Main Scene")]
    public static void SetUpMainScene()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
        var smelter = Resources.Load<FactoryData>("FactoryData/Smelter");
        var presser = Resources.Load<FactoryData>("FactoryData/Presser");
        if (smelter == null || presser == null) throw new InvalidOperationException("Missing factory definitions.");
        if (!AssetDatabase.IsValidFolder("Assets/Recipes")) AssetDatabase.CreateFolder("Assets", "Recipes");
        var ingot = Recipe("Handcraft Iron Ingot", smelter.inputItem, 2, smelter.outputItem);
        var plate = Recipe("Handcraft Iron Plate", presser.inputItem, 2, presser.outputItem);

        var root = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(t => t.name == "Crafting Tab").gameObject;
        EnsureInventory(root.GetComponentInParent<Canvas>().transform);
        var existingTab = root.GetComponent<CraftingTab>();
        if (existingTab != null)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Crafting is already set up. Edit the saved prefabs and recipe assets directly.");
            return;
        }
        var catalog = root.GetComponent<BuildTabCatalog>();
        var serialized = new SerializedObject(catalog);
        var content = (RectTransform)serialized.FindProperty("content").objectReferenceValue;
        var craft = (Button)serialized.FindProperty("buildButton").objectReferenceValue;
        var name = (TMP_Text)serialized.FindProperty("FactoryNameDisplay").objectReferenceValue;
        var entryButtons = content.GetComponentsInChildren<Button>(true);
        if (entryButtons.Length < 2) throw new InvalidOperationException("The authored crafting layout needs two recipe entries.");
        Object.DestroyImmediate(catalog);

        // Preserve the teammate's layout objects and style them in the editor.
        var main = root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Craft List");
        SetRect(main, new Vector2(.14f, .18f), new Vector2(.86f, .82f));
        var fade = (RectTransform)main.parent;
        SetRect(fade, Vector2.zero, Vector2.one);
        fade.GetComponent<Image>().color = new Color(.025f, .035f, .05f, .85f);
        main.GetComponent<Image>().color = new Color(.08f, .11f, .15f, 1f);
        var scroll = content.GetComponentInParent<ScrollRect>();
        SetRect((RectTransform)scroll.transform, new Vector2(.04f, .16f), new Vector2(.47f, .82f));
        var grid = content.GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 1;
        grid.cellSize = new Vector2(315f, 84f);
        grid.spacing = new Vector2(0, 12);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.childAlignment = TextAnchor.UpperCenter;
        SetRect(content, new Vector2(0, 1), Vector2.one);
        content.pivot = new Vector2(.5f, 1);
        content.sizeDelta = new Vector2(0, 208);
        scroll.horizontal = false;
        SetRect((RectTransform)name.transform, new Vector2(.53f, .75f), new Vector2(.96f, .86f));
        StyleText(name, 26);
        var requirementPanel = root.GetComponentsInChildren<RectTransform>(true)
            .Single(t => t.name.StartsWith("Craft Req", StringComparison.Ordinal));
        requirementPanel.name = "Recipe Requirements";
        requirementPanel.SetParent(main, false);
        SetRect(requirementPanel, new Vector2(.53f, .33f), new Vector2(.96f, .72f));
        requirementPanel.GetComponent<Image>().color = new Color(.12f, .16f, .21f);
        var requirements = Label("Ingredients", requirementPanel, 21);
        requirements.alignment = TextAlignmentOptions.TopLeft;
        requirements.margin = new Vector4(14, 12, 14, 12);
        SetRect((RectTransform)craft.transform, new Vector2(.53f, .16f), new Vector2(.96f, .29f));
        StyleButton(craft, "CRAFT ONE");
        var status = Label("Crafting Status", main, 17);
        SetRect((RectTransform)status.transform, new Vector2(.53f, .02f), new Vector2(.96f, .14f));
        var title = Label("Crafting Title", main, 30);
        title.text = "CRAFTING TABLE";
        SetRect((RectTransform)title.transform, new Vector2(.04f, .86f), new Vector2(.78f, .98f));
        var close = Object.Instantiate(craft, main);
        close.name = "Close Crafting";
        close.onClick.RemoveAllListeners();
        SetRect((RectTransform)close.transform, new Vector2(.84f, .88f), new Vector2(.97f, .97f));
        StyleButton(close, "Close");

        var tab = root.AddComponent<CraftingTab>();
        tab.panel = root.GetComponent<CanvasGroup>();
        tab.entries = new[] { Entry(entryButtons[0], ingot), Entry(entryButtons[1], plate) };
        tab.recipeName = name;
        tab.requirements = requirements;
        tab.status = status;
        tab.craftButton = craft;
        tab.closeButton = close;
        tab.panel.alpha = 0;
        tab.panel.interactable = tab.panel.blocksRaycasts = false;
        name.text = ingot.DisplayName;
        requirements.text = "2 Iron Ore\n\nProduces 1 Iron Ingot";
        status.text = "Uses resources in your inventory.";
        craft.interactable = false;

        Transform parent = root.transform.parent;
        root.transform.SetParent(null, false);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, "Assets/prefabs/CraftingTabUI.prefab", InteractionMode.AutomatedAction);
        root.transform.SetParent(parent, false);
        SetRect((RectTransform)root.transform, Vector2.zero, Vector2.one);
        root.transform.SetAsLastSibling();

        var tablePrefab = CreateTablePrefab();
        var table = (GameObject)PrefabUtility.InstantiatePrefab(tablePrefab, scene);
        table.transform.position = new Vector3(-10, 0, -1);
        table.name = "Starter Crafting Table";
        PrefabUtility.RecordPrefabInstancePropertyModifications(table.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Saved crafting panel, workbench and two recipe assets in MainScene.");
    }

    public static void PolishSavedLayout()
    {
        const string path = "Assets/prefabs/CraftingTabUI.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var tab = root.GetComponent<CraftingTab>();
            var main = root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Craft List");
            var area = root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Recipe Requirements");
            area.SetParent(main, false);
            SetRect(area, new Vector2(.53f, .33f), new Vector2(.96f, .72f));
            StyleText(tab.requirements, 21);
            tab.requirements.alignment = TextAlignmentOptions.TopLeft;
            tab.requirements.text = "2 Iron Ore\n\nProduces 1 Iron Ingot";
            foreach (var entry in tab.entries)
            {
                StyleButton(entry.button, entry.recipe.DisplayName);
                entry.label = entry.button.GetComponentInChildren<TMP_Text>(true);
            }
            var scroll = main.GetComponentInChildren<ScrollRect>(true);
            scroll.GetComponent<Image>().color = new Color(.10f, .14f, .19f);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void EnsureInventory(Transform canvas)
    {
        var inventory = Object.FindAnyObjectByType<Inventory>(FindObjectsInactive.Include);
        if (inventory == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InventoryTabUI.prefab");
            if (prefab == null) throw new InvalidOperationException("InventoryTabUI prefab is missing.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas);
            inventory = instance.GetComponent<Inventory>();
            SetRect((RectTransform)instance.transform, Vector2.zero, Vector2.one);
        }
        inventory.gameObject.SetActive(true);
        inventory.enabled = true;
        var group = inventory.GetComponent<CanvasGroup>();
        group.alpha = 0;
        group.interactable = group.blocksRaycasts = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(inventory.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(group);
    }

    private static CraftingRecipe Recipe(string name, Resource input, int amount, Resource output)
    {
        string path = $"Assets/Recipes/{name}.asset";
        var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(path);
        if (recipe != null) return recipe;
        recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
        recipe.recipeName = name.Replace("Handcraft ", "");
        recipe.ingredients = new[] { new CraftingIngredient { item = input, amount = amount } };
        recipe.outputItem = output;
        recipe.outputAmount = 1;
        if (!recipe.IsValid) throw new InvalidOperationException("Recipe resources are missing.");
        AssetDatabase.CreateAsset(recipe, path);
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
        button.GetComponent<Image>().color = new Color(.18f, .38f, .48f);
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

    private static GameObject CreateTablePrefab()
    {
        const string path = "Assets/prefabs/Crafting Table.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) return prefab;
        var root = new GameObject("Crafting Table", typeof(CraftingTable));
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.17f, .26f, .31f) };
        AssetDatabase.CreateAsset(material, "Assets/prefabs/Crafting Table.mat");
        Part(root.transform, "Worktop", new Vector3(0, 1, 0), new Vector3(2.2f, .22f, 1.2f), material);
        foreach (float x in new[] { -.85f, .85f })
            foreach (float z in new[] { -.4f, .4f })
                Part(root.transform, "Leg", new Vector3(x, .45f, z), new Vector3(.18f, .9f, .18f), material);
        var sign = new GameObject("Crafting Table Sign", typeof(TextMesh));
        sign.transform.SetParent(root.transform, false);
        sign.transform.localPosition = new Vector3(0, 1.65f, 0);
        var text = sign.GetComponent<TextMesh>();
        text.text = "CRAFTING TABLE\n[E]";
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 48;
        text.characterSize = .055f;
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }
}
