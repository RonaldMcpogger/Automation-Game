using System;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Authors the weekly prototype into MainScene, using its existing player and belt.</summary>
public static class FactoryPrototypeSetup
{
    private const string ScenePath = "Assets/Scenes/MainScene.unity";
    private const string ArtPath = "Assets/FactoryPrototype";

    /// <summary>Review the authored layout and HUD without changing the saved scene.</summary>
    public static void CapturePreview()
    {
        bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        EditorSceneManager.OpenScene(ScenePath);
        var player = Object.FindAnyObjectByType<FactoryPlayer>();
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.CopyFrom(player.viewCamera);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        camera.fieldOfView = 65f;
        camera.transform.position = new Vector3(-31.4f, 2f, 4.05f);
        camera.transform.LookAt(new Vector3(-30.5f, 1.5f, -3.5f));
        var hud = player.objectiveText.canvas;
        hud.renderMode = RenderMode.ScreenSpaceCamera;
        hud.worldCamera = camera;
        hud.planeDistance = 0.5f;
        Canvas.ForceUpdateCanvases();

        var target = new RenderTexture(1280, 720, 24);
        target.Create();
        RenderPipeline.SubmitRenderRequest(camera,
            new RenderPipeline.StandardRequest { destination = target });
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes("Logs/first-factory-preview.png", image.EncodeToPNG());
        RenderTexture.active = previous;
        target.Release();
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(camera.gameObject);
        ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
        Debug.Log("Factory preview saved to Logs/first-factory-preview.png (scene not saved).");
    }

    [MenuItem("Tools/Factory Prototype/Set Up Main Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        if (GameObject.Find("First Factory") != null)
            throw new InvalidOperationException("MainScene is already configured. Edit its First Factory objects directly.");

        Directory.CreateDirectory(ArtPath);
        AssetDatabase.Refresh();
        var ore = MakeMaterial("Iron Ore", new Color(0.48f, 0.24f, 0.12f));
        var shell = MakeMaterial("Machine", new Color(0.12f, 0.23f, 0.29f));
        var metal = MakeMaterial("Iron Ingot", new Color(0.65f, 0.76f, 0.8f));
        var dark = MakeMaterial("Bin", new Color(0.12f, 0.15f, 0.18f));
        var yellow = MakeMaterial("Safety Yellow", new Color(1f, 0.65f, 0.08f));
        var green = MakeMaterial("Working", new Color(0.2f, 0.9f, 0.4f));
        var red = MakeMaterial("Blocked", new Color(0.95f, 0.25f, 0.15f));

        var root = new GameObject("First Factory").transform;
        var experiments = new GameObject("Conveyor Experiments (inactive)").transform;
        var belt = Object.FindObjectsByType<Conveyor>()
            .Single(x => !x.ConveyerSpline.Spline.Closed);
        foreach (var other in Object.FindObjectsByType<Conveyor>())
            if (other != belt)
                other.transform.SetParent(experiments, true);

        belt.name = "Conveyor - Smelter to Bin";
        belt.transform.SetParent(root, true);
        belt.transform.position = new Vector3(belt.transform.position.x, 1f, belt.transform.position.z);
        // Keep the existing curved route, level its working surface, and give the bin room
        // away from the inlet (the original two endpoints were almost on top of each other).
        var spline = belt.ConveyerSpline.Spline;
        for (int i = 0; i < spline.Count; i++)
        {
            var knot = spline[i];
            knot.Position.y = 0f;
            var forward = math.mul(knot.Rotation, new float3(0, 0, 1));
            forward.y = 0f;
            knot.Rotation = quaternion.LookRotationSafe(forward, new float3(0, 1, 0));
            if (i == spline.Count - 1)
                knot.Position = new float3(-10f, 0f, -2f);
            spline[i] = knot;
        }
        belt.speed = 5f;
        belt.surfOffset = 0.03f;
        belt.SearchRes = 16;
        belt.minimumSpacing = 0.8f;
        belt.GetComponent<Renderer>().sharedMaterial = shell;
        belt.GetComponent<SplineExtrude>().Rebuild();
        Vector3 start = belt.ConveyerSpline.EvaluatePosition(0f);
        Vector3 end = belt.ConveyerSpline.EvaluatePosition(1f);
        Vector3 forwardDirection = ((Vector3)belt.ConveyerSpline.EvaluateTangent(0f)).normalized;

        var spotObject = ReuseCube("Cube (1)", "Iron Ore Spot",
            new Vector3(start.x - 5f, 0.55f, start.z + 4f), new Vector3(2f, 1.1f, 2f), ore, root);
        spotObject.AddComponent<ResourceSpot>();
        Sign("IRON ORE\nHold E to gather", spotObject.transform.position + Vector3.up * 1.7f, root, yellow.color);

        Vector3 machinePosition = start - forwardDirection * 1.8f;
        machinePosition.y = 1.2f;
        var machineObject = ReuseCube("Cube", "Smelter", machinePosition,
            new Vector3(2f, 2.4f, 2f), shell, root);
        var machine = machineObject.AddComponent<FactoryMachine>();
        machine.outputBelt = belt;
        machine.idleMaterial = yellow;
        machine.workingMaterial = green;
        machine.blockedMaterial = red;
        var lamp = Cube("Smelter Status Light", machinePosition + new Vector3(0, 0.7f, 1.02f),
            new Vector3(0.7f, 0.2f, 0.08f), yellow, root);
        Object.DestroyImmediate(lamp.GetComponent<Collider>());
        machine.statusLight = lamp.GetComponent<Renderer>();
        Sign("SMELTER\n1 ore > 1 ingot\nPress E to load", machinePosition + Vector3.up * 1.9f, root, Color.white);

        var ingot = Cube("Iron Ingot", Vector3.zero, new Vector3(0.45f, 0.3f, 0.65f), metal, null);
        var body = ingot.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ingot.AddComponent<FactoryItem>();
        machine.ingotPrefab = PrefabUtility.SaveAsPrefabAsset(ingot, ArtPath + "/Iron Ingot.prefab")
            .GetComponent<FactoryItem>();
        Object.DestroyImmediate(ingot);

        var binRoot = new GameObject("Submission Bin").transform;
        binRoot.SetParent(root);
        binRoot.position = new Vector3(end.x, 0f, end.z);
        var bin = binRoot.gameObject.AddComponent<SubmissionBin>();
        belt.outputReceiver = bin;
        ReuseCube("Cube (2)", "Bin Base", binRoot.position + Vector3.up * 0.15f,
            new Vector3(2.8f, 0.3f, 2.8f), dark, binRoot);
        Cube("Bin Left", binRoot.position + new Vector3(-1.3f, 0.85f, 0), new Vector3(0.2f, 1.4f, 2.8f), yellow, binRoot);
        Cube("Bin Right", binRoot.position + new Vector3(1.3f, 0.85f, 0), new Vector3(0.2f, 1.4f, 2.8f), yellow, binRoot);
        Cube("Bin Front", binRoot.position + new Vector3(0, 0.85f, 1.3f), new Vector3(2.4f, 1.4f, 0.2f), dark, binRoot);
        Cube("Bin Back", binRoot.position + new Vector3(0, 0.85f, -1.3f), new Vector3(2.4f, 1.4f, 0.2f), dark, binRoot);
        var trigger = binRoot.gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0, 0.85f, 0);
        trigger.size = new Vector3(2.4f, 1.4f, 2.4f);
        bin.counterLabel = Sign("SUBMISSION BIN\n0 / 10 INGOTS", binRoot.position + Vector3.up * 2.7f, root, yellow.color);

        foreach (var cube in Object.FindObjectsByType<Rigidbody>())
            if (!cube.transform.IsChildOf(root) && cube.name.StartsWith("Cube", StringComparison.Ordinal))
                cube.transform.SetParent(experiments, true);
        experiments.gameObject.SetActive(false);

        var controller = Object.FindAnyObjectByType<FirstPersonController>();
        var player = controller.gameObject.AddComponent<FactoryPlayer>();
        player.viewCamera = controller.GetComponentInChildren<Camera>();
        player.machine = machine;
        player.submissionBin = bin;
        controller.GetComponent<CharacterController>().enabled = false;
        controller.transform.position = new Vector3(start.x - 4f, 1.05f, start.z + 8f);
        controller.transform.rotation = Quaternion.Euler(0, 180, 0);
        controller.GetComponent<CharacterController>().enabled = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller.transform);
        var manager = Object.FindAnyObjectByType<InputManager>();
        var inputSettings = new SerializedObject(manager);
        inputSettings.FindProperty("lockCursorOnStart").boolValue = true;
        inputSettings.ApplyModifiedPropertiesWithoutUndo();
        CreateHud(root, player);

        // Keep the original sample scene available, with MainScene as the playable entry point.
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
            .Concat(EditorBuildSettings.scenes.Where(x => x.path != ScenePath)).ToArray();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Factory prototype saved: ore spot -> smelter -> existing conveyor -> submission bin.");
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("URP Lit shader is unavailable.");
        var material = new Material(shader) { name = name, color = color };
        AssetDatabase.CreateAsset(material, $"{ArtPath}/{name}.mat");
        return material;
    }

    private static GameObject ReuseCube(string existingName, string name, Vector3 position,
        Vector3 scale, Material material, Transform parent)
    {
        var cube = GameObject.Find(existingName);
        if (cube == null)
            throw new InvalidOperationException($"Expected the existing scene object '{existingName}'.");
        if (cube.TryGetComponent<Rigidbody>(out var body))
            Object.DestroyImmediate(body);
        ConfigureCube(cube, name, position, scale, material, parent);
        return cube;
    }

    private static GameObject Cube(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ConfigureCube(cube, name, position, scale, material, parent);
        return cube;
    }

    private static void ConfigureCube(GameObject cube, string name, Vector3 position,
        Vector3 scale, Material material, Transform parent)
    {
        cube.name = name;
        cube.transform.position = position;
        cube.transform.rotation = Quaternion.identity;
        cube.transform.localScale = scale;
        cube.transform.SetParent(parent, true);
        cube.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static TextMesh Sign(string text, Vector3 position, Transform parent, Color color)
    {
        var sign = new GameObject(text.Split('\n')[0] + " Sign").AddComponent<TextMesh>();
        sign.transform.SetParent(parent);
        sign.transform.position = position;
        sign.transform.rotation = Quaternion.Euler(0, 180, 0);
        sign.text = text;
        sign.characterSize = 0.035f;
        sign.fontSize = 64;
        sign.anchor = TextAnchor.MiddleCenter;
        sign.alignment = TextAlignment.Center;
        sign.color = color;
        return sign;
    }

    private static void CreateHud(Transform parent, FactoryPlayer player)
    {
        var canvas = new GameObject("Factory HUD", typeof(Canvas), typeof(CanvasScaler));
        canvas.transform.SetParent(parent);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        player.objectiveText = HudText(canvas.transform, "Objective", new Vector2(24, -22), new Vector2(620, 86), 23, TextAnchor.UpperLeft);
        player.inventoryText = HudText(canvas.transform, "Ore Pouch", new Vector2(24, -116), new Vector2(500, 138), 18, TextAnchor.UpperLeft);
        player.machineText = HudText(canvas.transform, "Machine Status", new Vector2(24, -258), new Vector2(700, 48), 18, TextAnchor.UpperLeft);
        player.promptText = HudText(canvas.transform, "Interaction Prompt", new Vector2(0, 52), new Vector2(1100, 60), 22, TextAnchor.MiddleCenter);
        var promptRect = player.promptText.rectTransform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0);
        promptRect.pivot = new Vector2(0.5f, 0);
        player.objectiveText.text = "FIRST FACTORY  |  Submit 10 iron ingots\nDelivered: 0 / 10";
        player.inventoryText.text = "Ore pouch: 0 / 10\n1. Hold E at the ore spot\n2. Press E at the smelter\n3. Watch ingots reach the bin";
        player.machineText.text = "SMELTER  |  Waiting for ore";
        player.promptText.text = "WASD move  |  Hold E to gather  |  Press E to load";
    }

    private static Text HudText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0, 1);
        text.rectTransform.pivot = new Vector2(0, 1);
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = size;
        text.GetComponent<Shadow>().effectDistance = new Vector2(1, -1);
        return text;
    }
}
