using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates and maintains the CentralStorageUI prefab at Assets/Resources/UI/CentralStorageUI.prefab.
/// </summary>
[InitializeOnLoad]
public static class CentralStorageUISetup
{
    public const string PrefabPath = "Assets/Resources/UI/CentralStorageUI.prefab";

    static CentralStorageUISetup()
    {
        EditorApplication.delayCall += EnsurePrefabExists;
    }

    public static void EnsurePrefabExists()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!File.Exists(PrefabPath))
            CreateOrRebuildPrefab();
    }

    [MenuItem("Tools/Factory UI/Rebuild Central Storage UI Prefab")]
    public static void CreateOrRebuildPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[CentralStorageUISetup] Cannot rebuild prefab while in Play Mode.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/UI"))
            AssetDatabase.CreateFolder("Assets/Resources", "UI");

        var tempGO = new GameObject("CentralStorageUI");
        try
        {
            var ui = tempGO.AddComponent<CentralStorageUI>();
            ui.BuildDefaultHierarchy();

            var cg = tempGO.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }

            PrefabUtility.SaveAsPrefabAsset(tempGO, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CentralStorageUISetup] Saved Central Storage UI prefab to {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(tempGO);
        }
    }

    [MenuItem("Tools/Factory UI/Add Central Storage UI to Current Scene")]
    public static void AddToCurrentScene()
    {
        EnsurePrefabExists();

        var existing = Object.FindAnyObjectByType<CentralStorageUI>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[CentralStorageUISetup] CentralStorageUI is already in the current scene.", existing);
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[CentralStorageUISetup] Could not load prefab at {PrefabPath}");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(instance, "Add CentralStorageUI to Scene");
        Selection.activeGameObject = instance;
    }
}
