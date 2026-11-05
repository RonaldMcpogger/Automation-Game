using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Authors a native terrain asset once; Play Mode loads the baked map without generating it.</summary>
public static class TerrainMapEditor
{
    private const string ScenePath = "Assets/Scenes/MainScene.unity";
    private const string Folder = "Assets/World";
    private const string DataPath = Folder + "/Generated Terrain.asset";

    [MenuItem("Tools/Factory Map/Upgrade to Large Landscape")]
    public static void UpgradeLargeLandscape()
    {
        var settings = AssetDatabase.LoadAssetAtPath<MapGenerationSettings>(Folder + "/Map Settings.asset");
        if (settings == null)
            throw new InvalidOperationException("Set up MainScene's map first.");
        Undo.RecordObject(settings, "Upgrade landscape settings");
        settings.mapSize = new Vector2(1024, 1024);
        settings.heightmapPower = 9;
        settings.verticalSize = 192f;
        settings.hillHeight = 20f;
        settings.noiseScale = 160f;
        settings.mountainCount = 7;
        settings.mountainHeight = 130f;
        settings.mountainRadius = 150f;
        settings.resourceSpotCount = 48;
        settings.resourceSpacing = 65f;
        EditorUtility.SetDirty(settings);
        SetUpMainScene();
    }

    [MenuItem("Tools/Factory Map/Set Up Main Scene")]
    public static void SetUpMainScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        var settings = AssetDatabase.LoadAssetAtPath<MapGenerationSettings>(Folder + "/Map Settings.asset");
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<MapGenerationSettings>();
            AssetDatabase.CreateAsset(settings, Folder + "/Map Settings.asset");
        }
        settings.Validate();
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
        if (data == null)
        {
            data = new TerrainData { name = "Generated Terrain" };
            AssetDatabase.CreateAsset(data, DataPath);
        }
        var map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        if (map == null)
        {
            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Generated Factory Map";
            map = terrainObject.AddComponent<TerrainMapGenerator>();
            Undo.RegisterCreatedObjectUndo(terrainObject, "Create factory map");
        }
        map.settings = settings;
        map.surfaceLayers = new[]
        {
            Layer("Grass", new Color(0.30f, 0.39f, 0.20f), 12f),
            Layer("Rock", new Color(0.43f, 0.39f, 0.32f), 8f)
        };
        map.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Terrain.mat");
        if (map.terrainMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null)
                throw new InvalidOperationException("The URP Terrain Lit shader is unavailable.");
            map.terrainMaterial = new Material(shader) { name = "Factory Terrain" };
            AssetDatabase.CreateAsset(map.terrainMaterial, Folder + "/Terrain.mat");
        }
        map.resourceSpotPrefab = CreateOreSpotPrefab();
        map.GenerateInto(data);
        // Preserve the old floor for comparison without double surfaces or colliders.
        var oldFloor = GameObject.Find("Land");
        if (oldFloor != null)
        {
            Undo.RecordObject(oldFloor, "Replace legacy ground");
            oldFloor.SetActive(false);
        }
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = map.gameObject;
        Debug.Log($"Factory map saved: seed {settings.seed}, {settings.mapSize.x} x {settings.mapSize.y}m, "
            + $"generator v{MapGenerationSettings.GeneratorVersion}. Factory clearing stays at Y=0.");
    }

    private static ResourceSpot CreateOreSpotPrefab()
    {
        string path = Folder + "/Iron Ore Deposit.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
            return prefab.GetComponent<ResourceSpot>();
        var ore = AssetDatabase.LoadAssetAtPath<Resource>("Assets/ScriptableObjects/Raw Resources/Iron Ore.asset");
        if (ore == null)
            throw new InvalidOperationException("The iron ore resource definition is missing.");
        var rock = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { name = "Ore Deposit Rock", color = new Color(0.20f, 0.23f, 0.25f), enableInstancing = true };
        var mineral = new Material(rock.shader)
            { name = "Iron Mineral", color = new Color(0.76f, 0.34f, 0.12f), enableInstancing = true };
        AssetDatabase.CreateAsset(rock, Folder + "/Ore Deposit Rock.mat");
        AssetDatabase.CreateAsset(mineral, Folder + "/Iron Mineral.mat");
        var mesh = new Mesh { name = "Faceted Ore Rock" };
        var points = new[] { new Vector3(0, 1.6f, 0), new Vector3(-0.9f, 0.4f, 0),
            new Vector3(0, 0.4f, 0.8f), new Vector3(0.9f, 0.4f, 0),
            new Vector3(0, 0.4f, -0.8f), new Vector3(0, -0.15f, 0) };
        var faces = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1,
            5, 2, 1, 5, 3, 2, 5, 4, 3, 5, 1, 4 };
        var vertices = new Vector3[faces.Length];
        var triangles = new int[faces.Length];
        for (int i = 0; i < faces.Length; i++) { vertices[i] = points[faces[i]]; triangles[i] = i; }
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Folder + "/Ore Rock.asset");
        var root = new GameObject("Iron Ore Deposit");
        var spot = root.AddComponent<ResourceSpot>();
        spot.resourceData = ore;
        Rock("Main Rock", Vector3.zero, new Vector3(1.1f, 1f, 1.1f), rock);
        Rock("Side Rock", new Vector3(-1f, 0, 0.5f), new Vector3(0.75f, 0.65f, 0.85f), rock);
        Rock("Side Rock", new Vector3(0.8f, 0, -0.7f), new Vector3(0.75f, 0.8f, 0.8f), rock);
        Rock("Iron Mineral", new Vector3(-0.6f, 0.5f, 0.3f), new Vector3(0.35f, 0.55f, 0.4f), mineral);
        Rock("Iron Mineral", new Vector3(0.5f, 0.7f, -0.3f), new Vector3(0.4f, 0.5f, 0.3f), mineral);
        prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<ResourceSpot>();

        void Rock(string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            part.GetComponent<MeshCollider>().sharedMesh = mesh;
        }
    }

    public static void Regenerate(TerrainMapGenerator map)
    {
        if (map.settings == null || map.Terrain.terrainData == null)
            throw new InvalidOperationException("Set up the map with Tools > Factory Map > Set Up Main Scene first.");
        map.settings.Validate();
        Undo.RegisterCompleteObjectUndo(map.Terrain.terrainData, "Regenerate factory terrain");
        Undo.RegisterFullObjectHierarchyUndo(map.gameObject, "Regenerate factory terrain");
        map.GenerateInto(map.Terrain.terrainData);
        EditorUtility.SetDirty(map.Terrain.terrainData);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        // Let the user save the scene normally; this button supports Undo and does not auto-save.
    }

    private static TerrainLayer Layer(string name, Color color, float tileSize)
    {
        string path = $"{Folder}/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer != null)
            return layer;
        string texturePath = $"{Folder}/{name}.png";
        var texture = new Texture2D(64, 64, TextureFormat.RGB24, false);
        for (int z = 0; z < 64; z++)
        for (int x = 0; x < 64; x++)
        {
            // Periodic detail tiles seamlessly and needs no external art or random state.
            float u = x / 64f * Mathf.PI * 2f, v = z / 64f * Mathf.PI * 2f;
            float detail = 0.9f + 0.05f * Mathf.Sin(u) * Mathf.Cos(v) + 0.04f * Mathf.Cos(2f * u + 3f * v);
            texture.SetPixel(x, z, color * detail);
        }
        texture.Apply();
        File.WriteAllBytes(texturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texturePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        layer = new TerrainLayer
        {
            name = name,
            diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath),
            tileSize = Vector2.one * tileSize,
            smoothness = 0.05f
        };
        AssetDatabase.CreateAsset(layer, path);
        return layer;
    }

    /// <summary>Batch entry point: render after Editor frames have initialized the render pipeline.</summary>
    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene(ScenePath);
        ShaderUtil.allowAsyncCompilation = false;
        int frames = 0;
        EditorApplication.CallbackFunction draw = null;
        draw = () =>
        {
            if (++frames < 12)
                return;
            EditorApplication.update -= draw;
            try
            {
                RenderPreview();
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        };
        EditorApplication.update += draw;
    }

    private static void RenderPreview()
    {
        var camera = new GameObject("Map Preview Camera").AddComponent<Camera>();
        var map = Object.FindAnyObjectByType<TerrainMapGenerator>();
        float size = Mathf.Max(map.settings.mapSize.x, map.settings.mapSize.y);
        Vector3 center = new Vector3(map.settings.mapCenter.x, 0, map.settings.mapCenter.y);
        camera.transform.position = center + new Vector3(-0.6f, 0.65f, 0.7f) * size;
        camera.transform.LookAt(center);
        camera.fieldOfView = 65f;
        camera.farClipPlane = size * 4f;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var target = new RenderTexture(1440, 900, 24);
        target.Create();
        var previous = RenderTexture.active;
        var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
            image.Apply();
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/terrain-preview.png", image.EncodeToPNG());
            Debug.Log("Terrain preview saved: Logs/terrain-preview.png");
        }
        finally
        {
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(camera.gameObject);
        }
    }
}

[CustomEditor(typeof(TerrainMapGenerator))]
public class TerrainMapGeneratorInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Regenerates this map's heightmap and surface paint from its settings. "
            + "Manual sculpting is replaced. Factory objects are not moved. Edit the settings asset, then regenerate and save the scene.", MessageType.Info);
        if (GUILayout.Button(Application.isPlaying ? "Regenerate Runtime Copy" : "Regenerate Terrain (Undo Supported)"))
        {
            var map = (TerrainMapGenerator)target;
            try
            {
                if (Application.isPlaying)
                    map.RegenerateRuntime();
                else
                    TerrainMapEditor.Regenerate(map);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, map);
            }
        }
        if (GUILayout.Button("New Random Seed and Regenerate"))
        {
            var map = (TerrainMapGenerator)target;
            if (Application.isPlaying)
                map.RegenerateRuntime(true);
            else if (map.settings != null)
            {
                map.settings.Validate();
                Undo.RecordObject(map.settings, "Choose new map seed");
                map.settings.seed = Guid.NewGuid().GetHashCode();
                EditorUtility.SetDirty(map.settings);
                TerrainMapEditor.Regenerate(map);
            }
        }
        var current = (TerrainMapGenerator)target;
        EditorGUILayout.LabelField("Generated seed", current.GeneratedSeed.ToString());
        EditorGUILayout.LabelField("Resource spots", current.GeneratedResources == null ? "0" : current.GeneratedResources.childCount.ToString());
    }
}
