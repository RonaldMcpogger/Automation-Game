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
        var binPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Factories/Submission Bin.prefab");
        var presserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Factories/Presser.prefab");
        var drillPrefab = EnsureDrillPrefab();

        var conveyorItem = EnsureResource("Conveyor Belt", 100, conveyorPrefab);
        var smelterItem = EnsureResource("Smelter", 20, smelterPrefab);
        var binItem = EnsureResource("Submission Bin", 20, binPrefab);
        var presserItem = EnsureResource("Presser", 20, presserPrefab);
        var drillItem = EnsureResource("Drill", 20, drillPrefab);

        // Update FactoryData costs
        var conveyorData = LoadFactoryData("Conveyor");
        if (conveyorData != null)
        {
            conveyorData.buildCostItem = conveyorItem;
            conveyorData.buildCostAmount = 1;
            EditorUtility.SetDirty(conveyorData);
        }

        var smelterData = LoadFactoryData("Smelter");
        if (smelterData != null)
        {
            smelterData.buildCostItem = smelterItem;
            smelterData.buildCostAmount = 1;
            EditorUtility.SetDirty(smelterData);
        }

        var binData = LoadFactoryData("SubmissionBin");
        if (binData != null)
        {
            binData.buildCostItem = binItem;
            binData.buildCostAmount = 1;
            EditorUtility.SetDirty(binData);
        }

        var presserData = LoadFactoryData("Presser");
        if (presserData != null)
        {
            presserData.buildCostItem = presserItem;
            presserData.buildCostAmount = 1;
            EditorUtility.SetDirty(presserData);
        }

        var drillData = LoadFactoryData("Drill");
        if (drillData == null)
        {
            drillData = ScriptableObject.CreateInstance<FactoryData>();
            drillData.factoryName = "Drill";
            drillData.kind = FactoryKind.Drill;
            drillData.description = "Auto-gathers ore from resource spots and outputs to conveyors.";
            drillData.outputItem = ore;
            drillData.buildCostItem = drillItem;
            drillData.buildCostAmount = 1;
            drillData.processingSeconds = 2.0f;
            drillData.inputCapacity = 10;
            drillData.factoryPrefab = drillPrefab;
            AssetDatabase.CreateAsset(drillData, "Assets/Resources/FactoryData/Drill.asset");
        }
        else
        {
            drillData.buildCostItem = drillItem;
            drillData.buildCostAmount = 1;
            drillData.outputItem = ore;
            drillData.factoryPrefab = drillPrefab;
            EditorUtility.SetDirty(drillData);
        }

        // Recipes (Option C durations: Ingot/Plate 0s for test compatibility, machines have durations)
        var ingotRecipe = RecipeWithDuration("Handcraft Iron Ingot", ore, 2, ingot, 1, 0f);
        var plateRecipe = RecipeWithDuration("Handcraft Iron Plate", ingot, 2, plate, 1, 0f);
        var conveyorRecipe = RecipeWithDuration("Craft Conveyor Belt", ingot, 1, conveyorItem, 2, 0.8f);
        var smelterRecipe = RecipeWithDuration("Craft Smelter", ingot, 4, smelterItem, 1, 2.5f);
        var binRecipe = RecipeWithDuration("Craft Submission Bin", ingot, 3, binItem, 1, 2.0f);
        var presserRecipe = RecipeWithDuration("Craft Presser", ingot, 4, presserItem, 1, 3.0f);
        var drillRecipe = RecipeWithDuration("Craft Drill", ingot, 4, drillItem, 1, 2.5f);

        var allRecipes = new[] { ingotRecipe, plateRecipe, conveyorRecipe, smelterRecipe, binRecipe, presserRecipe, drillRecipe };

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
            EnsureUIInfrastructureInScene(currentScene);

            var player = Object.FindAnyObjectByType<FactoryPlayer>();
            if (player != null)
            {
                var build = player.GetComponent<BuildController>();
                if (build != null)
                {
                    var tableData = LoadFactoryData("CraftingTable");
                    build.buildableFactories = new[] { conveyorData, smelterData, binData, presserData, drillData, tableData }
                        .Where(d => d != null).ToArray();
                    EditorUtility.SetDirty(build);
                }
            }

            var orePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Resources/Iron Ore.prefab");
            if (orePrefab != null && orePrefab.GetComponent<FactoryItem>() == null)
            {
                var item = orePrefab.AddComponent<FactoryItem>();
                item.itemData = ore;
                item.material = FactoryItem.MaterialKind.IronOre;
                EditorUtility.SetDirty(orePrefab);
            }
            var oreReplPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Resources/iron Ore replacement.prefab");
            if (oreReplPrefab != null && oreReplPrefab.GetComponent<FactoryItem>() == null)
            {
                var item = oreReplPrefab.AddComponent<FactoryItem>();
                item.itemData = ore;
                item.material = FactoryItem.MaterialKind.IronOre;
                EditorUtility.SetDirty(oreReplPrefab);
            }

            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);
        }

        EnsureUIInfrastructureInPlayerPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[CraftingSetup] Successfully synchronized all machine items, recipes, and UI prefab entries.");
    }

    private static FactoryData LoadFactoryData(string name)
    {
        return AssetDatabase.LoadAssetAtPath<FactoryData>($"Assets/Resources/FactoryData/{name}.asset");
    }

    public static GameObject EnsureDrillPrefab()
    {
        string prefabPath = "Assets/prefabs/Factories/Drill.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder("Assets/prefabs/Factories"))
            AssetDatabase.CreateFolder("Assets/prefabs", "Factories");

        string conePath = "Assets/FactoryPrototype/Cone.asset";
        Mesh coneMesh = AssetDatabase.LoadAssetAtPath<Mesh>(conePath);
        if (coneMesh == null)
        {
            coneMesh = CreateConeMesh(0.6f, 0.9f, 16);
            AssetDatabase.CreateAsset(coneMesh, conePath);
        }

        var machineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Machine.mat");
        var yellowMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Safety Yellow.mat");
        var greenMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Working.mat");
        var redMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/FactoryPrototype/Blocked.mat");

        var root = new GameObject("Drill");
        var drill = root.AddComponent<Drill>();
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(2.2f, 2.4f, 2.2f);
        boxCol.center = new Vector3(0f, 1.2f, 0f);

        // MotorBody (Cube)
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "MotorBody";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        body.transform.localScale = new Vector3(1.8f, 1.2f, 1.8f);
        if (body.TryGetComponent<Renderer>(out var bodyRend) && machineMat != null)
            bodyRend.sharedMaterial = machineMat;
        Object.DestroyImmediate(body.GetComponent<Collider>());

        // DrillBit (Cone pointing down)
        var bitObj = new GameObject("DrillBit");
        bitObj.transform.SetParent(root.transform, false);
        bitObj.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        bitObj.transform.localRotation = Quaternion.identity;
        bitObj.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);
        var mf = bitObj.AddComponent<MeshFilter>();
        mf.sharedMesh = coneMesh;
        var mr = bitObj.AddComponent<MeshRenderer>();
        if (machineMat != null) mr.sharedMaterial = machineMat;

        // Status Light (Cube)
        var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lamp.name = "Status Light";
        lamp.transform.SetParent(root.transform, false);
        lamp.transform.localPosition = new Vector3(0f, 1.9f, 0.92f);
        lamp.transform.localScale = new Vector3(0.5f, 0.15f, 0.08f);
        Object.DestroyImmediate(lamp.GetComponent<Collider>());
        if (lamp.TryGetComponent<Renderer>(out var lampRend) && yellowMat != null)
            lampRend.sharedMaterial = yellowMat;

        // Output Port (Empty Transform)
        var output = new GameObject("Output");
        output.transform.SetParent(root.transform, false);
        output.transform.localPosition = new Vector3(0f, 0.83f, 1.2f);

        // Sign
        var signObj = new GameObject("DRILL Sign");
        signObj.transform.SetParent(root.transform, false);
        signObj.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        var textMesh = signObj.AddComponent<TextMesh>();
        textMesh.text = "DRILL\nAuto-mines ore\nPress E to collect";
        textMesh.fontSize = 24;
        textMesh.characterSize = 0.08f;
        textMesh.alignment = TextAlignment.Center;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.color = Color.white;

        drill.drillBit = bitObj.transform;
        drill.OutputPort = output.transform;
        drill.statusLight = lamp.GetComponent<Renderer>();
        drill.idleMaterial = yellowMat;
        drill.workingMaterial = greenMat;
        drill.blockedMaterial = redMat;
        drill.processingSeconds = 2.0f;
        drill.inputCapacity = 10;
        drill.beltSurfaceHeight = 0.83f;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    public static Mesh CreateConeMesh(float radius = 0.5f, float height = 0.8f, int segments = 16)
    {
        Mesh mesh = new Mesh { name = "Cone" };
        var vertices = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();

        Vector3 tip = new Vector3(0, -height, 0);
        Vector3 baseCenter = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = (float)i / segments * Mathf.PI * 2f;
            float angle2 = (float)(i + 1) / segments * Mathf.PI * 2f;

            Vector3 p1 = new Vector3(Mathf.Cos(angle1) * radius, 0, Mathf.Sin(angle1) * radius);
            Vector3 p2 = new Vector3(Mathf.Cos(angle2) * radius, 0, Mathf.Sin(angle2) * radius);

            int vIndex = vertices.Count;
            vertices.Add(tip);
            vertices.Add(p1);
            vertices.Add(p2);

            Vector3 normal = Vector3.Cross(p2 - tip, p1 - tip).normalized;
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            triangles.Add(vIndex);
            triangles.Add(vIndex + 1);
            triangles.Add(vIndex + 2);
        }

        int baseCenterIdx = vertices.Count;
        vertices.Add(baseCenter);
        normals.Add(Vector3.up);

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            vertices.Add(new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            normals.Add(Vector3.up);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            triangles.Add(baseCenterIdx);
            triangles.Add(baseCenterIdx + 1 + next);
            triangles.Add(baseCenterIdx + 1 + i);
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
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
        if (recipe != null)
            return recipe;

        recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
        recipe.recipeName = name.Replace("Craft ", "").Replace("Handcraft ", "");
        recipe.ingredients = new[] { new CraftingIngredient { item = input, amount = amount } };
        recipe.outputItem = output;
        recipe.outputAmount = outputCount;
        recipe.craftDuration = duration;
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
