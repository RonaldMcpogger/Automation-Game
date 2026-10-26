using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A conveyor segment for a Cylinder instantiated along a spline.
/// With Spline Instantiate Rotation Offset X = 90, the cylinder's length is local Y.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ConveyorPart : MonoBehaviour
{
    [Header("Conveyor Settings")]
    [Min(0f)] public float speed = 1f;
    [Min(0f)] public float surfaceOffset = 0.01f;
    public bool keepMomentum = true;
    public bool snapRotation = true;
    [Min(0f)] public float rotationSpeed = 720f;

    // These defaults specifically match a Unity Cylinder rotated X = 90.
    // Its length is Y, and the side facing upward is negative Z.
    [Header("Cylinder Axes")]
    [SerializeField] private Vector3 localTravelAxis = Vector3.up;
    [SerializeField] private Vector3 localSurfaceNormal = Vector3.back;

    private BoxCollider conveyorCollider;
    private readonly Dictionary<Rigidbody, Attachment> attachedBodies = new();
    private readonly Dictionary<Rigidbody, float> recentlyReleased = new();

    private struct Attachment
    {
        public float distance;
        public Vector3 lateralOffset;
        public float heightAboveBelt;
    }

    private void Awake()
    {
        conveyorCollider = GetComponent<BoxCollider>();
    }

    private void FixedUpdate()
    {
        RemoveExpiredReleaseLocks();

        Vector3 travelAxis = localTravelAxis.normalized;
        float worldUnitsPerLocalUnit = transform.TransformVector(travelAxis).magnitude;
        if (worldUnitsPerLocalUnit <= Mathf.Epsilon)
            return;

        float length = GetColliderLength(travelAxis);
        foreach (Rigidbody body in new List<Rigidbody>(attachedBodies.Keys))
        {
            if (body == null)
            {
                attachedBodies.Remove(body);
                continue;
            }

            Attachment attachment = attachedBodies[body];
            attachment.distance += speed * Time.fixedDeltaTime / worldUnitsPerLocalUnit;

            if (attachment.distance > length)
            {
                Release(body);
                continue;
            }

            attachedBodies[body] = attachment;
            MoveBody(body, attachment);
        }
    }

    private void OnCollisionEnter(Collision collision) => TryAttach(collision);
    private void OnCollisionStay(Collision collision) => TryAttach(collision);

    private void TryAttach(Collision collision)
    {
        Rigidbody body = collision.rigidbody;
        if (body == null || body.isKinematic || attachedBodies.ContainsKey(body) || recentlyReleased.ContainsKey(body))
            return;

        Vector3 travelAxis = localTravelAxis.normalized;
        Vector3 surfaceNormal = localSurfaceNormal.normalized;
        ContactPoint contact = collision.GetContact(0);
        Vector3 localContact = transform.InverseTransformPoint(contact.point);
        float surfaceCoordinate = GetSurfaceCoordinate(surfaceNormal);

        // Attach only to the outward-facing side of the cylinder, not its underside.
        if (Vector3.Dot(localContact, surfaceNormal) < surfaceCoordinate - 0.02f)
            return;

        float length = GetColliderLength(travelAxis);
        float start = Vector3.Dot(conveyorCollider.center, travelAxis) - length * 0.5f;
        float distance = Mathf.Clamp(Vector3.Dot(localContact, travelAxis) - start, 0f, length);
        Vector3 localCenter = transform.InverseTransformPoint(body.worldCenterOfMass);

        // Preserve the object's position across the cylinder, while moving it along its length.
        Vector3 lateralOffset = localCenter
            - travelAxis * Vector3.Dot(localCenter, travelAxis)
            - surfaceNormal * Vector3.Dot(localCenter, surfaceNormal);
        Vector3 localSurface = lateralOffset
            + travelAxis * Vector3.Dot(localCenter, travelAxis)
            + surfaceNormal * surfaceCoordinate;
        Vector3 worldSurfaceNormal = transform.TransformDirection(surfaceNormal).normalized;

        attachedBodies.Add(body, new Attachment
        {
            distance = distance,
            lateralOffset = lateralOffset,
            heightAboveBelt = Vector3.Dot(body.worldCenterOfMass - transform.TransformPoint(localSurface), worldSurfaceNormal)
        });

        body.isKinematic = true;
        body.useGravity = false;
    }

    private void MoveBody(Rigidbody body, Attachment attachment)
    {
        Vector3 travelAxis = localTravelAxis.normalized;
        Vector3 surfaceNormal = localSurfaceNormal.normalized;
        float length = GetColliderLength(travelAxis);
        float start = Vector3.Dot(conveyorCollider.center, travelAxis) - length * 0.5f;
        float surfaceCoordinate = GetSurfaceCoordinate(surfaceNormal);
        Vector3 localSurface = attachment.lateralOffset
            + travelAxis * (start + attachment.distance)
            + surfaceNormal * surfaceCoordinate;
        Vector3 worldSurfaceNormal = transform.TransformDirection(surfaceNormal).normalized;
        Vector3 targetCenterOfMass = transform.TransformPoint(localSurface)
            + worldSurfaceNormal * (attachment.heightAboveBelt + surfaceOffset);

        body.MovePosition(body.position + targetCenterOfMass - body.worldCenterOfMass);

        if (snapRotation)
        {
            Quaternion targetRotation = Quaternion.LookRotation(
                transform.TransformDirection(travelAxis), worldSurfaceNormal);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }
    }

    private void Release(Rigidbody body)
    {
        attachedBodies.Remove(body);
        body.isKinematic = false;
        body.useGravity = true;

        if (keepMomentum)
            body.AddForce(transform.TransformDirection(localTravelAxis.normalized) * speed, ForceMode.VelocityChange);
       

        recentlyReleased[body] = Time.time + Time.fixedDeltaTime * 2f;
    }

    private float GetColliderLength(Vector3 axis)
    {
        return Mathf.Abs(axis.x) * conveyorCollider.size.x
            + Mathf.Abs(axis.y) * conveyorCollider.size.y
            + Mathf.Abs(axis.z) * conveyorCollider.size.z;
    }

    private float GetSurfaceCoordinate(Vector3 normal)
    {
        return Vector3.Dot(conveyorCollider.center, normal) + GetColliderLength(normal) * 0.5f;
    }

    private void RemoveExpiredReleaseLocks()
    {
        foreach (Rigidbody body in new List<Rigidbody>(recentlyReleased.Keys))
            if (body == null || recentlyReleased[body] <= Time.time)
                recentlyReleased.Remove(body);
    }
}
