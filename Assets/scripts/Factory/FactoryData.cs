using UnityEngine;
using UnityEngine.Serialization;

public enum FactoryKind { Conveyor, Machine, SubmissionBin, ResourceSpot, Presser }

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
    public Resource inputItem;
    public Resource outputItem;
    [Min(1)] public int inputPerCycle = 1;
    [Min(1)] public int outputPerCycle = 1;
    [Min(1)] public int inputCapacity = 10;
    [Min(0.05f)] public float processingSeconds = 2f;

    [Header("Build UI")]
    public Sprite icon;
    public string DisplayName => string.IsNullOrWhiteSpace(factoryName) ? name : factoryName;

    [Header("Crafting / Build Cost")]
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

            switch (kind)
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

    public bool HasBuildCost => BuildCostQty > 0 && BuildCostResource != null;

    public string GetCostText()
    {
        if (!HasBuildCost)
            return "Free";
        return $"{BuildCostQty}x {BuildCostResource.DisplayName}";
    }
}
