using System.Collections;
using UnityEngine;

/// <summary>One inexhaustible ore spot for the weekly prototype.</summary>
///  Resource spot to farm manually, or drill factory
public class ResourceSpot : FactoryInteractable
{
    [Tooltip("The item definition gathered from this spot.")]
    public Resource resourceData; // resource to give or generate
    [Min(0.05f)] public float gatherInterval = 0.4f;
    private float nextGatherTime;

    [Header("Harvest Tween / Juice")]
    [Tooltip("Transform to shake/punch. Defaults to this GameObject if unassigned.")]
    public Transform visualTransform;
    [Tooltip("Duration of the punch/shake in seconds.")]
    [Range(0.05f, 0.4f)] public float tweenDuration = 0.16f;
    [Tooltip("Maximum random positional jitter during the tween.")]
    public float shakeIntensity = 0.05f;
    [Tooltip("Relative punch scale multiplier (X, Y, Z). Y is negative to squash down, X/Z positive to bulge outward.")]
    public Vector3 punchScale = new Vector3(0.08f, -0.10f, 0.08f);

    private Vector3 initialLocalPosition;
    private Vector3 initialLocalScale;
    private bool hasRecordedRestTransform;
    private Coroutine tweenCoroutine;

    public override bool HoldToInteract => true;

    private void Awake()
    {
        EnsureResourceData();
        InitVisualTransform();
    }

    private void InitVisualTransform()
    {
        if (visualTransform == null)
            visualTransform = transform;
    }

    private void EnsureResourceData()
    {
        if (resourceData == null)
        {
            var smelter = Resources.Load<FactoryData>("FactoryData/Smelter");
            if (smelter != null && smelter.inputItem != null)
                resourceData = smelter.inputItem;
            else
            {
                var spotData = Resources.Load<FactoryData>("FactoryData/IronOreSpot");
                if (spotData != null && spotData.outputItem != null)
                    resourceData = spotData.outputItem;
            }
        }
    }

    public string ResourceName
    {
        get
        {
            EnsureResourceData();
            return resourceData != null ? resourceData.DisplayName : "Iron Ore";
        }
    }

    public override string GetPrompt(FactoryPlayer player) // write prompt to show on ui 
    {
        if (player == null)
            return string.Empty;

        return Inventory.Instance == null || !Inventory.Instance.CanAdd(resourceData)
            ? "Inventory full - load a machine, craft, or build [B]"
            : $"Hold [{{key}}] to gather {ResourceName}";
    }

    public override void Interact(FactoryPlayer player)
    {
        if (player == null || !isActiveAndEnabled || Time.time < nextGatherTime)
            return;

        EnsureResourceData();
        if (player.TryCollect(resourceData))
        {
            nextGatherTime = Time.time + gatherInterval;
            TriggerHarvestTween();
        }
    }

    public void TriggerHarvestTween()
    {
        if (!isActiveAndEnabled)
            return;

        InitVisualTransform();
        if (visualTransform == null)
            return;

        // If not currently running a tween, capture the current rest transform
        if (tweenCoroutine == null || !hasRecordedRestTransform)
        {
            initialLocalPosition = visualTransform.localPosition;
            initialLocalScale = visualTransform.localScale;
            hasRecordedRestTransform = true;
        }
        else
        {
            // Reset to rest before restarting
            StopCoroutine(tweenCoroutine);
            visualTransform.localPosition = initialLocalPosition;
            visualTransform.localScale = initialLocalScale;
        }

        tweenCoroutine = StartCoroutine(HarvestTweenRoutine());
    }

    private IEnumerator HarvestTweenRoutine()
    {
        float elapsed = 0f;
        while (elapsed < tweenDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / tweenDuration);

            // Smooth punch curve: rises to 1 then drops to 0
            float punch = Mathf.Sin(t * Mathf.PI);
            // Linear decay for position shake: strongest at hit, fading to 0
            float decay = 1f - t;

            // Punch scale: compress Y (downward impact) and expand X/Z
            Vector3 currentPunch = new Vector3(
                punchScale.x * punch,
                punchScale.y * punch,
                punchScale.z * punch
            );
            visualTransform.localScale = initialLocalScale + Vector3.Scale(initialLocalScale, currentPunch);

            // Positional jitter
            Vector3 jitter = Random.insideUnitSphere * (shakeIntensity * decay);
            visualTransform.localPosition = initialLocalPosition + jitter;

            yield return null;
        }

        visualTransform.localPosition = initialLocalPosition;
        visualTransform.localScale = initialLocalScale;
        tweenCoroutine = null;
    }

    private void OnDisable()
    {
        if (tweenCoroutine != null)
        {
            StopCoroutine(tweenCoroutine);
            tweenCoroutine = null;
            if (visualTransform != null && hasRecordedRestTransform)
            {
                visualTransform.localPosition = initialLocalPosition;
                visualTransform.localScale = initialLocalScale;
            }
        }
    }
}
