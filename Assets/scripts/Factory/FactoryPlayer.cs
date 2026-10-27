using UnityEngine;
using UnityEngine.UI;

/// <summary>A small ore pouch, station interaction, and HUD on the existing FPS player.</summary>
public class FactoryPlayer : MonoBehaviour
{
    public Camera viewCamera;
    public FactoryMachine machine;
    public SubmissionBin submissionBin;
    public Text objectiveText;
    public Text inventoryText;
    public Text machineText;
    public Text promptText;
    [Min(1)] public int oreCapacity = 10;
    [Min(0.5f)] public float interactionRange = 4f;

    public int OreCount { get; private set; }

    private void Awake()
    {
        if (!TryGetComponent<BuildController>(out _))
            gameObject.AddComponent<BuildController>();
    }

    public bool TryCollectOre()
    {
        if (OreCount >= oreCapacity)
            return false;
        OreCount++;
        return true;
    }

    public int TakeOre(int requested)
    {
        int taken = Mathf.Clamp(requested, 0, OreCount);
        OreCount -= taken;
        return taken;
    }

    private void Update()
    {
        var input = InputManager.Instance;
        FactoryInteractable station = null;
        if (viewCamera != null && input != null && input.GameplayEnabled && input.CursorLocked)
        {
            var ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            if (Physics.Raycast(ray, out var hit, interactionRange, ~0, QueryTriggerInteraction.Ignore))
                station = hit.collider.GetComponentInParent<FactoryInteractable>();

            if (station != null && station.isActiveAndEnabled
                && (station.HoldToInteract ? input.InteractHeld : input.InteractPressed))
                station.Interact(this);
        }

        if (objectiveText != null && submissionBin != null)
            objectiveText.text = submissionBin.GoalReached
                ? "FIRST FACTORY COMPLETE!\nKeep producing, or restart Play Mode to try again."
                : $"FIRST FACTORY  |  Submit {submissionBin.targetCount} iron ingots\n"
                    + $"Delivered: {submissionBin.SubmittedCount} / {submissionBin.targetCount}";
        if (inventoryText != null)
            inventoryText.text = $"Ore pouch: {OreCount} / {oreCapacity}\n"
                + "1. Hold E at the ore spot\n2. Press E at the smelter\n3. Watch ingots reach the bin";
        if (machineText != null && machine != null)
            machineText.text = "SMELTER  |  " + machine.Status;
        if (promptText != null)
        {
            var build = GetComponent<BuildController>();
            if (build != null && build.IsBuildModeActive)
            {
                promptText.text = "[B] Exit Build Mode  |  [1-4] Select Building  |  [LMB] Place";
            }
            else
            {
                string key = input != null ? input.GetLabel(PromptAction.Interact) : "E";
                promptText.text = input != null && !input.CursorLocked
                    ? "Click to resume  |  WASD move  |  Shift sprint  |  Space jump"
                    : station != null && station.isActiveAndEnabled
                        ? station.GetPrompt(this).Replace("{key}", key ?? "E")
                        : "WASD move  |  [B] Build Mode  |  Shift sprint  |  Space jump";
            }
        }
    }
}
