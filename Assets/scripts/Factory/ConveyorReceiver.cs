using UnityEngine;

/// <summary>An optional endpoint; a rejected item waits on its conveyor.</summary>
public abstract class ConveyorReceiver : MonoBehaviour
{
    [Tooltip("The one conveyor connected to this receiver's input.")]
    public Conveyor inputConveyor;

    public bool HasInputConnection => inputConveyor != null;

    public abstract bool TryReceive(Rigidbody body);
}
