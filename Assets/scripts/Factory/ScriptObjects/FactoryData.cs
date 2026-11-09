using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// this could be like a category instead of adding every single factory implemented into factorykind
// might have to remove resourcespot since those are for randomly generated ores maybe make a drill so that it can transfer ores to smelter


public enum FactoryKind { Conveyor, Machine, SubmissionBin, ResourceSpot, Presser, CraftTable }

[CreateAssetMenu(fileName = "newFactoryData", menuName = "Automation Game /Factory Data")]
public class FactoryData : ScriptableObject
{
    [FormerlySerializedAs("FactoryModel")]
    [Tooltip("Optional complete prefab. If empty, BuildController creates the prototype fallback model.")]
    public GameObject factoryPrefab;
    [FormerlySerializedAs("FactoryName")]
    public string factoryName;
    public FactoryKind kind = FactoryKind.Machine;
    [TextArea] public string description;

    /// <summary>
    /// the added code here is test for  making build controller use a single preview and spawn prefab instead of a making a function for each new entry
    /// </summary>

    [Header("Placement")]
    [Tooltip("Offset from the placement point to the center of the model.")]
    public Vector3 placementOffset;
    [Tooltip("Offset for the rotation of the placed model.")]
    public Vector3 placementRotationOffset;
    [Tooltip("Toggle if the factory should use two-point placement (e.g., for conveyors).")]
    public bool useTwoPointPlacement; // true only for conveyors





    [Header("Recipe / Item Settings")]
    public Resource inputItem; // what item it needs
    public Resource outputItem; // what it will generate out of the input item

    public List<CraftingRecipe> ValidCraftables; // test
    
    
    [Min(1)] public int inputPerCycle = 1;
    [Min(1)] public int outputPerCycle = 1;
    [Min(1)] public int inputCapacity = 10;
    [Min(0.05f)] public float processingSeconds = 2f;

    [Header("Build UI")]
    public Sprite icon;
    public string DisplayName => string.IsNullOrWhiteSpace(factoryName) ? name : factoryName;

  
    
    
    
    [Header("Crafting / Build Cost")]
    [Tooltip("Toggle if builds should be free or require no resources")]
    public bool freeToBuild = false;


    [Tooltip("Resource required to build this structure (leave null for default).")]
    public Resource buildCostItem;
    [Tooltip("Number of items required to build (0 = default).")]
    [Min(0)] public int buildCostAmount = 0;

    public Resource BuildCostResource
    {
        get
        {
            if (buildCostItem != null)
                return buildCostItem;

            switch (kind) // fallback to default cost items if not specified
            {
                case FactoryKind.Machine:
                    return inputItem ?? Resources.Load<FactoryData>("FactoryData/Smelter")?.inputItem;
                case FactoryKind.SubmissionBin:
                    return Resources.Load<FactoryData>("FactoryData/Smelter")?.inputItem;
                case FactoryKind.Conveyor:
                    return Resources.Load<FactoryData>("FactoryData/Smelter")?.inputItem;
                case FactoryKind.Presser:
                    return inputItem ?? Resources.Load<FactoryData>("FactoryData/Presser")?.inputItem;
                default:
                    return null;
            }
        }
    }

    public int BuildCostQty
    {
        get
        {
            if (buildCostAmount > 0)
                return buildCostAmount;
            if (buildCostItem != null)
                return 1;

            switch (kind)
            {
                case FactoryKind.Machine: return 5;
                case FactoryKind.SubmissionBin: return 3;
                case FactoryKind.Conveyor: return 1;
                case FactoryKind.Presser: return 4;
                default: return 0;
            }
        }
    }

    public bool HasBuildCost =>!freeToBuild && BuildCostQty > 0 && BuildCostResource != null;

    public Resource FallbackCostItem
    {
        get
        {
            switch (kind) // optional fallback if it is missing
            {
                case FactoryKind.Machine:
                    return inputItem ?? Resources.Load<FactoryData>("FactoryData/Smelter")?.inputItem;
                case FactoryKind.SubmissionBin:
                    return Resources.Load<FactoryData>("FactoryData/Smelter")?.inputItem;
                case FactoryKind.Conveyor:
                    return Resources.Load<FactoryData>("FactoryData/Smelter")?.inputItem;
                case FactoryKind.Presser:
                    return inputItem ?? Resources.Load<FactoryData>("FactoryData/Presser")?.inputItem;
                default:
                    return null;
            }
        }
    }

    public int FallbackCostQty
    {
        get
        {
            switch (kind)
            {
                case FactoryKind.Machine: return 5;
                case FactoryKind.SubmissionBin: return 3;
                case FactoryKind.Conveyor: return 1;
                case FactoryKind.Presser: return 4;
                default: return 0;
            }
        }
    }

    public string GetCostText() // get cpst text
    {
        if (!HasBuildCost)
            return "Free";
        return $"{BuildCostQty}x {BuildCostResource.DisplayName}";
    }
}
