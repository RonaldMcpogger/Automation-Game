using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>Moves rigidbodies that land on this object's collider along its spline.</summary>
[RequireComponent(typeof(Collider))]
public class Conveyor : MonoBehaviour
{
    [Header("Spline Container")]
    [Tooltip("Usually the SplineContainer on this same GameObject.")]
    public SplineContainer ConveyerSpline;

    [Header("Conveyor Settings")]
    [Min(0f)] public float speed = 1.0f;
    [Tooltip("Small clearance above the belt surface, in metres. This prevents an attached object from being pushed out by the belt collider.")]
    [Min(0f)] public float surfOffset = 0.01f;
    [Tooltip("Higher values find the closest part of a curved spline more accurately, at a small performance cost.")]
    [Min(2)] public int SearchRes = 16;
    [Tooltip("Give an item the belt's velocity when it reaches an open spline's end.")]
    public bool keepMomentumAtEnd = true;
    [Tooltip("Rotate attached items so their forward (Z) axis faces the belt's direction of travel.")]
    public bool snapRotation = true;
    [Min(0f)] public float rotationSpeed = 720f;

    [Header("Connections")]
    [Tooltip("The machine feeding this belt's start. A start point may have only one input connection.")]
    public FactoryMachine inputMachine;
    [Tooltip("The belt feeding this belt's start. A start point may have only one input connection.")]
    public Conveyor inputConveyor;
    [Tooltip("The belt receiving items from this belt's end. An end point may have only one output connection.")]
    public Conveyor outputConveyor;
    [Tooltip("The factory receiver connected to this belt's end. An end point may have only one output connection.")]
    public ConveyorReceiver outputReceiver;
    [Min(0.1f)] public float minimumSpacing = 0.8f;

    public bool HasInputConnection => inputMachine != null || inputConveyor != null;
    public bool HasOutputConnection => outputConveyor != null || outputReceiver != null;

    // Stored when spawned so an existing belt can be rebuilt without changing its chosen width.
    [HideInInspector] public float meshRadius;
    [HideInInspector] public float meshSegmentsPerUnit;

    private readonly Dictionary<Rigidbody, Attachment> attachedBodies = new();
    private readonly Dictionary<Rigidbody, float> recentlyReleased = new();
    private readonly List<Rigidbody> bodyBuffer = new();
    private readonly List<Rigidbody> releasedBuffer = new();
    private float splineLength;

    public int ItemCount => attachedBodies.Count;
    public Vector3 StartPosition
    {
        get
        {
            Evaluate(0f, out var position, out _, out _);
            return position;
        }
    }

    public bool CanLoadAtStart
    {
        get
        {
            if (!isActiveAndEnabled || ConveyerSpline == null || ConveyerSpline.Spline == null
                || ConveyerSpline.Spline.Closed || splineLength <= Mathf.Epsilon)
                return false;
            foreach (var attachment in attachedBodies.Values)
                if (attachment.t * splineLength < minimumSpacing)
                    return false;
            return true;
        }
    }

    /// <summary>Explicit machine output avoids relying on a falling item's collision timing.</summary>
    public bool TryLoadAtStart(Rigidbody body, float surfaceHeight)
    {
        if (body == null || attachedBodies.ContainsKey(body) || !CanLoadAtStart)
            return false;
        Attach(body, 0f, surfaceHeight);
        return true;
    }

    private struct Attachment
    {
        public float t;
        public float surfaceHeight;
        public bool originalKinematic;
        public bool originalUseGravity;
    }

    private void Awake()
    {
        FindSplineContainer();
        RefreshSplineLength();
    }

    private void OnValidate() => FindSplineContainer();

    private void FixedUpdate()
    {
        if (ConveyerSpline == null || ConveyerSpline.Spline == null)
            return;

        RefreshSplineLength();
        if (splineLength <= Mathf.Epsilon)
            return;

        float now = Time.time;
        releasedBuffer.Clear();
        releasedBuffer.AddRange(recentlyReleased.Keys);
        foreach (Rigidbody body in releasedBuffer)
            if (recentlyReleased[body] <= now)
                recentlyReleased.Remove(body);

        bodyBuffer.Clear();
        bodyBuffer.AddRange(attachedBodies.Keys);
        // Move the leading item first so followers can queue behind its new position.
        bool queueAtOutput = HasOutputConnection && !ConveyerSpline.Spline.Closed && speed >= 0f;
        if (queueAtOutput)
            bodyBuffer.Sort((a, b) => attachedBodies[b].t.CompareTo(attachedBodies[a].t));
        float frontT = float.PositiveInfinity;

        foreach (Rigidbody body in bodyBuffer)
        {
            if (body == null)
            {
                attachedBodies.Remove(body);
                continue;
            }

            Attachment attachment = attachedBodies[body];
            float nextT = attachment.t + speed * Time.fixedDeltaTime / splineLength;
            if (queueAtOutput)
                nextT = Mathf.Max(attachment.t, Mathf.Min(nextT, frontT - minimumSpacing / splineLength));

            if (ConveyerSpline.Spline.Closed)
                nextT = Mathf.Repeat(nextT, 1f);
            else if (nextT < 0f || nextT > 1f)
            {
                if (queueAtOutput)
                {
                    // A connected belt receives the item at its own start; a receiver consumes it.
                    if (outputConveyor != null && outputConveyor.TryLoadAtStart(body, attachment.surfaceHeight))
                    {
                        attachedBodies.Remove(body);
                        continue;
                    }
                    if (outputReceiver != null && outputReceiver.TryReceive(body))
                    {
                        attachedBodies.Remove(body);
                        continue;
                    }
                    nextT = 1f;
                }
                else
                {
                    Release(body, attachment, Mathf.Clamp01(nextT), keepMomentumAtEnd);
                    continue;
                }
            }

            attachment.t = nextT;
            attachedBodies[body] = attachment;
            MoveAttachedBody(body, nextT, attachment.surfaceHeight);
            frontT = nextT;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryAttach(collision);
    }
    private void OnCollisionStay(Collision collision)
    {
        TryAttach(collision);
    }
    private void TryAttach(Collision collision)
    {
        Rigidbody body = collision.rigidbody;
        if (body == null || body.isKinematic || attachedBodies.ContainsKey(body) || recentlyReleased.ContainsKey(body))
            return;
        if (ConveyerSpline == null || ConveyerSpline.Spline == null)
            return;

        ContactPoint contact = collision.GetContact(0);
        float t = FindNearestT(contact.point);
        Evaluate(t, out Vector3 splinePosition, out _, out Vector3 splineUp);

        float landingSurfaceHeight = Vector3.Dot(contact.point - splinePosition, splineUp);
        // Only take items that land on the top half of the conveyor, not its underside.
        if (landingSurfaceHeight < -0.001f)
            return;

        Attach(body, t, landingSurfaceHeight);
    }

    private void Attach(Rigidbody body, float t, float surfaceHeight)
    {
        attachedBodies.Add(body, new Attachment
        {
            t = t,
            surfaceHeight = surfaceHeight,
            originalKinematic = body.isKinematic,
            originalUseGravity = body.useGravity
        });

        // Kinematic movement keeps the item attached on steep parts of the spline.
        body.isKinematic = true;
        body.useGravity = false;
        MoveAttachedBody(body, t, attachedBodies[body].surfaceHeight);
    }

    private void OnDisable()
    {
        // Removing a belt must not leave its cargo permanently kinematic.
        foreach (var entry in attachedBodies)
        {
            if (entry.Key == null)
                continue;
            entry.Key.isKinematic = entry.Value.originalKinematic;
            entry.Key.useGravity = entry.Value.originalUseGravity;
        }
        attachedBodies.Clear();
        recentlyReleased.Clear();
    }

    private void MoveAttachedBody(Rigidbody body, float t, float surfaceHeight)
    {
        Evaluate(t, out Vector3 position, out Vector3 tangent, out Vector3 up);
        float supportDistance = GetSupportDistance(body, up);
        Vector3 targetCenterOfMass = position + up * (surfaceHeight + supportDistance + surfOffset);
        body.MovePosition(body.position + targetCenterOfMass - body.worldCenterOfMass);

        if (snapRotation && tangent.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(speed < 0f ? -tangent : tangent, up);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }
    }

    private void Release(Rigidbody body, Attachment attachment, float t, bool applyMomentum)
    {
        if (body == null)
            return;

        attachedBodies.Remove(body);
        body.isKinematic = attachment.originalKinematic;
        body.useGravity = attachment.originalUseGravity;

        if (applyMomentum && !body.isKinematic)
        {
            Evaluate(t, out _, out Vector3 tangent, out _);
            body.linearVelocity = tangent.normalized * speed;
        }

        recentlyReleased[body] = Time.time + Time.fixedDeltaTime * 2f;
    }

    private float FindNearestT(Vector3 worldPoint)
    {
        float3 localPoint = ConveyerSpline.transform.InverseTransformPoint(worldPoint);
        SplineUtility.GetNearestPoint(ConveyerSpline.Spline, localPoint, out _, out float t, SearchRes);
        return t;
    }

    private static float GetSupportDistance(Rigidbody body, Vector3 direction)
    {
        float distance = 0f;
        Vector3 centerOfMass = body.worldCenterOfMass;

        foreach (Collider itemCollider in body.GetComponentsInChildren<Collider>())
        {
            if (itemCollider.attachedRigidbody != body || !itemCollider.enabled)
                continue;

            Bounds bounds = itemCollider.bounds;
            Vector3 extents = bounds.extents;
            float extentAlongDirection = Mathf.Abs(direction.x) * extents.x
                + Mathf.Abs(direction.y) * extents.y
                + Mathf.Abs(direction.z) * extents.z;
            distance = Mathf.Max(distance, Vector3.Dot(bounds.center - centerOfMass, direction) + extentAlongDirection);
        }

        return distance;
    }

    private void Evaluate(float t, out Vector3 position, out Vector3 tangent, out Vector3 up)
    {
        SplineUtility.Evaluate(ConveyerSpline.Spline, t, out float3 localPosition, out float3 localTangent, out float3 localUp);
        position = ConveyerSpline.transform.TransformPoint((Vector3)localPosition);
        tangent = ConveyerSpline.transform.TransformDirection((Vector3)localTangent).normalized;
        up = ConveyerSpline.transform.TransformDirection((Vector3)localUp).normalized;
    }

    private void RefreshSplineLength()
    {
        if (ConveyerSpline != null && ConveyerSpline.Spline != null)
            splineLength = ConveyerSpline.CalculateLength();
    }

    private void FindSplineContainer()
    {
        if (ConveyerSpline == null)
            ConveyerSpline = GetComponent<SplineContainer>();
    }
}
