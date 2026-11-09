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

    [Header("Recipe / Item Settings")]
    //input and output need to change in the future, there will be items in the future that turn into different material once processed,
    //example: ingot to plate is a different process than turning and ore to ingot or sand to glass if it were impleemnted.
    // maybe have it use a list of recipes and check which is compatbile or not with a specific machine in the future, using crafting recipe scriptable object
    public Resource inputItem; // what item it needs
    public Resource outputItem; // what it will generate out of the input item

    public List<CraftingRecipe> ValidCraftables;
    
    
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
