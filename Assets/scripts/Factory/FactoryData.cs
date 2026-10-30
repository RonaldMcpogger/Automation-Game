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
}
