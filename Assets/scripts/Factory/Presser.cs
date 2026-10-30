using System;
using UnityEngine;

/// <summary>
/// Presses iron ingots into iron plates upon contact, held delivery, or conveyor feed.
/// </summary>
public class Presser : FactoryMachine, IHeldTargetReceiver
{
    [Header("Components")]
    public Transform smashHead;
    public Transform inputPort;
    public Transform outputPort;
    public Transform bed;
    public Collider trigger;
    public PresserReceiver receiver;

    [Tooltip("Downward stroke distance for the smash head.")]
    [Min(0.1f)] public float smashStroke = 0.85f;

    private Vector3 smashRestPos;
    private bool initialized;

    protected override void Awake()
    {
        base.Awake();
        InitReferences();
    }

    public void InitReferences()
    {
        if (initialized && smashHead != null && inputPort != null && outputPort != null)
            return;

        if (factoryData == null)
            factoryData = Resources.Load<FactoryData>("FactoryData/Presser");
        ApplyFactoryData();

        if (inputPort == null)
            inputPort = transform.Find("Input");
        if (outputPort == null)
            outputPort = transform.Find("Output");
        if (smashHead == null)
            smashHead = transform.Find("smash");
        if (bed == null)
            bed = transform.Find("Cube");

        if (smashHead != null)
            smashRestPos = smashHead.localPosition;

        var receiverTarget = inputPort != null ? inputPort.gameObject : gameObject;
        receiver = receiverTarget.GetComponent<PresserReceiver>();
        if (receiver == null)
        {
            receiver = receiverTarget.AddComponent<PresserReceiver>();
            receiver.presser = this;
        }

        if (trigger == null)
        {
            Transform existing = transform.Find("Trigger") ?? transform.Find("Presser Contact Trigger");
            if (existing != null)
            {
                trigger = existing.GetComponent<Collider>();
            }
            else
            {
                var triggerObj = new GameObject("Trigger");
                triggerObj.transform.SetParent(transform, false);
                triggerObj.transform.localPosition = new Vector3(-0.25f, 0.65f, -0.25f);
                var box = triggerObj.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(2.0f, 1.2f, 2.0f);
                var bridge = triggerObj.AddComponent<PresserTrigger>();
                bridge.presser = this;
                trigger = box;
            }
        }

        var interactable = GetComponent<Interactable>();
        if (interactable == null)
        {
            interactable = gameObject.AddComponent<Interactable>();
            interactable.SetInteractable(true);
        }

        initialized = true;
    }

    public override string Status =>
        OutputReady && outputBelt != null && !outputBelt.CanLoadAtStart ? $"Belt blocked | Queued: {BufferedOre}"
        : IsProcessing ? $"Pressing {Mathf.Clamp01(elapsed / processingSeconds):P0} | Queued: {BufferedOre}"
        : OutputReady ? $"Plate ready | Queued: {BufferedOre}"
        : $"Waiting for ingot | Queued: {BufferedOre}";

    public override string GetPrompt(FactoryPlayer player)
    {
        if (IsProcessing)
            return $"PRESSER - Pressing {Mathf.Clamp01(elapsed / processingSeconds):P0}";
        if (BufferedOre >= inputCapacity)
            return "PRESSER - Full";
        if (player != null && player.CarriedItem == InputItem && player.OreCount > 0)
            return "Press [{key}] to load ingot";
        return "PRESSER - Drop or insert ingot";
    }

    public override void Interact(FactoryPlayer player)
    {
        if (player != null && player.OreCount > 0 && (player.CarriedItem == InputItem || InputItem == null))
            LoadFrom(player);
    }

    /// <summary>FPSCore IHeldTargetReceiver: handles held ingot delivered to presser.</summary>
    public bool OnReceiveHeld(Carrier carrier, Carriable held)
    {
        if (!isActiveAndEnabled || held == null)
            return false;
        return TryProcess(held.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other != null)
            TryProcess(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null && collision.gameObject != null)
            TryProcess(collision.gameObject);
    }

    /// <summary>Processes contact with an ingot and begins pressing.</summary>
    public bool TryProcess(GameObject obj)
    {
        if (!isActiveAndEnabled || obj == null)
            return false;

        if (!IsValidInput(obj, out FactoryItem item, out Carriable carriable, out Rigidbody rb))
            return false;

        if (BufferedOre + (IsProcessing ? 1 : 0) >= inputCapacity)
            return false;

        if (carriable != null)
            carriable.Drop();

        GameObject target = item != null ? item.gameObject : (rb != null ? rb.gameObject : obj);
        target.SetActive(false);
        Destroy(target);

        if (!IsProcessing && !OutputReady)
        {
            elapsed = 0f;
            IsProcessing = true;
        }
        else
        {
            BufferedOre++;
        }

        return true;
    }

    public bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null)
            return false;
        return TryProcess(body.gameObject);
    }

    // Aliases for compatibility
    public bool TryProcessContact(GameObject obj) => TryProcess(obj);
    public bool TryReceiveFromConveyor(Rigidbody body) => TryReceive(body);
    public void AutoFindReferences() => InitReferences();

    public bool IsValidInput(GameObject obj, out FactoryItem factoryItem, out Carriable carriable, out Rigidbody rb)
    {
        factoryItem = obj.GetComponentInParent<FactoryItem>();
        carriable = obj.GetComponentInParent<Carriable>();
        rb = obj.GetComponentInParent<Rigidbody>();

        if (factoryItem != null)
        {
            if (InputItem != null)
                return factoryItem.Matches(InputItem);
            return factoryItem.material == FactoryItem.MaterialKind.IronIngot;
        }

        if (InputItem != null && obj.name.IndexOf(InputItem.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return obj.name.IndexOf("Ingot", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    protected override void Update()
    {
        FactoryItem outputPrefab = OutputPrefab;

        if (OutputReady)
        {
            if (outputBelt != null)
            {
                if (outputBelt.CanLoadAtStart && outputPrefab != null)
                {
                    var item = Instantiate(outputPrefab, outputBelt.StartPosition, Quaternion.identity);
                    item.itemData = OutputItem;
                    var body = item.GetComponent<Rigidbody>();
                    if (outputBelt.TryLoadAtStart(body, beltSurfaceHeight))
                    {
                        OutputReady = false;
                        ProducedCount++;
                    }
                    else
                    {
                        item.gameObject.SetActive(false);
                        Destroy(item.gameObject);
                    }
                }
            }
            else
            {
                if (outputPrefab != null)
                    SpawnPlate(outputPrefab);
                OutputReady = false;
                ProducedCount++;
            }
        }
        else if (IsProcessing)
        {
            elapsed += Time.deltaTime;

            if (smashHead != null && processingSeconds > 0.001f)
            {
                float t = Mathf.Clamp01(elapsed / processingSeconds);
                float stroke = Mathf.Sin(t * Mathf.PI);
                smashHead.localPosition = smashRestPos - Vector3.up * (stroke * smashStroke);
            }

            if (elapsed >= processingSeconds)
            {
                if (smashHead != null)
                    smashHead.localPosition = smashRestPos;

                IsProcessing = false;
                OutputReady = true;
            }
        }
        else if (BufferedOre > 0)
        {
            BufferedOre--;
            elapsed = 0f;
            IsProcessing = true;
        }
        else
        {
            if (smashHead != null)
                smashHead.localPosition = smashRestPos;
        }

        if (statusLight != null)
            statusLight.sharedMaterial = OutputReady && outputBelt != null && !outputBelt.CanLoadAtStart ? blockedMaterial
                : IsProcessing ? workingMaterial : idleMaterial;
    }

    private void SpawnPlate(FactoryItem outputPrefab)
    {
        Vector3 spawnPos = outputPort != null
            ? outputPort.position
            : (bed != null ? bed.position + Vector3.up * 0.25f : transform.position + transform.forward * 1.0f + Vector3.up * 0.5f);
        Quaternion spawnRot = outputPort != null ? outputPort.rotation : transform.rotation;

        var plate = Instantiate(outputPrefab, spawnPos, spawnRot);
        plate.itemData = OutputItem;
        if (plate.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
        }
    }
}

/// <summary>
/// Connects incoming conveyor belts to the Presser input port.
/// </summary>
public class PresserReceiver : ConveyorReceiver
{
    public Presser presser;

    public override bool TryReceive(Rigidbody body)
    {
        if (!isActiveAndEnabled || body == null)
            return false;

        if (presser == null)
            presser = GetComponentInParent<Presser>();

        if (presser == null)
            return false;

        return presser.TryReceive(body);
    }
}

/// <summary>
/// Forwards contact events to the Presser.
/// </summary>
public class PresserTrigger : MonoBehaviour
{
    public Presser presser;

    private void Awake()
    {
        if (presser == null)
            presser = GetComponentInParent<Presser>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (presser != null && other != null)
            presser.TryProcess(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (presser != null && collision != null && collision.gameObject != null)
            presser.TryProcess(collision.gameObject);
    }
}

// Aliases for backwards compatibility
public class PresserMachine : Presser { }
public class PresserConveyorReceiver : PresserReceiver { }
public class PresserContactTriggerBridge : PresserTrigger { }
