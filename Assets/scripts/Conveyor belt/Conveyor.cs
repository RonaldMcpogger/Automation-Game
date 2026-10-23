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

    private readonly Dictionary<Rigidbody, Attachment> attachedBodies = new();
    private readonly Dictionary<Rigidbody, float> recentlyReleased = new();
    private float splineLength;

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
        foreach (Rigidbody body in new List<Rigidbody>(recentlyReleased.Keys))
            if (recentlyReleased[body] <= now)
                recentlyReleased.Remove(body);

        foreach (Rigidbody body in new List<Rigidbody>(attachedBodies.Keys))
        {
            if (body == null)
            {
                attachedBodies.Remove(body);
                continue;
            }

            Attachment attachment = attachedBodies[body];
            float nextT = attachment.t + speed * Time.fixedDeltaTime / splineLength;

            if (ConveyerSpline.Spline.Closed)
                nextT = Mathf.Repeat(nextT, 1f);
            else if (nextT < 0f || nextT > 1f)
            {
                Release(body, attachment, Mathf.Clamp01(nextT), keepMomentumAtEnd);
                continue;
            }

            attachment.t = nextT;
            attachedBodies[body] = attachment;
            MoveAttachedBody(body, nextT, attachment.surfaceHeight);
        }
    }

    private void OnCollisionEnter(Collision collision) => TryAttach(collision);
    private void OnCollisionStay(Collision collision) => TryAttach(collision);

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

        attachedBodies.Add(body, new Attachment
        {
            t = t,
            surfaceHeight = landingSurfaceHeight,
            originalKinematic = body.isKinematic,
            originalUseGravity = body.useGravity
        });

        // Kinematic movement keeps the item attached on steep parts of the spline.
        body.isKinematic = true;
        body.useGravity = false;
        MoveAttachedBody(body, t, attachedBodies[body].surfaceHeight);
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
