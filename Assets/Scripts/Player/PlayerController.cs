using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float maxSpeed = 20f;
    [SerializeField] float acceleration = 15f;
    [SerializeField] float braking = 25f;
    [SerializeField] float drag = 5f;
    [SerializeField] float turnSpeed = 100f; // degrees per second at full steering

    [Header("Jump")]
    [SerializeField] float jumpSpeed = 7f;

    [Header("Front wheels (visual)")]
    [SerializeField] Transform frontLeftWheel;
    [SerializeField] Transform frontRightWheel;
    [SerializeField] float maxWheelSteerAngle = 30f;
    [SerializeField] float wheelSteerSmoothing = 12f;

    // Per-wheel data so each wheel group turns about the vertical axis through its own center.
    class SteerWheel
    {
        public Transform t;
        public Quaternion restRot;      // rotation relative to the kart root
        public Vector3 centerLocal;     // wheel center in kart-root space
        public Vector3 pivotOffset;     // pivot position minus center, in kart-root space
    }

    SteerWheel leftWheel, rightWheel;
    Rigidbody rb;
    float throttle, steer;
    bool jumpQueued;
    float currentWheelAngle;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate; // smooth motion for the follow camera
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        leftWheel = SetupWheel(frontLeftWheel);
        rightWheel = SetupWheel(frontRightWheel);
    }

    SteerWheel SetupWheel(Transform wheel)
    {
        if (!wheel) return null;

        // Wheel center = middle of all meshes under the wheel group
        Vector3 center = wheel.position;
        var renderers = wheel.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            center = b.center;
        }

        return new SteerWheel
        {
            t = wheel,
            restRot = Quaternion.Inverse(transform.rotation) * wheel.rotation,
            centerLocal = transform.InverseTransformPoint(center),
            pivotOffset = transform.InverseTransformPoint(wheel.position) - transform.InverseTransformPoint(center)
        };
    }

    void SteerWheelTo(SteerWheel w, float angle)
    {
        if (w == null) return;
        Quaternion steerRot = Quaternion.AngleAxis(angle, Vector3.up); // kart-root vertical axis
        w.t.rotation = transform.rotation * steerRot * w.restRot;
        w.t.position = transform.TransformPoint(w.centerLocal) + transform.rotation * (steerRot * w.pivotOffset);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        throttle = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        steer = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        if (kb.spaceKey.wasPressedThisFrame) jumpQueued = true;

        UpdateWheels();
    }

    void FixedUpdate()
    {
        bool grounded = IsGrounded();
        OnGround = grounded;

        Vector3 vel = rb.linearVelocity;
        Vector3 flat = new Vector3(vel.x, 0f, vel.z);
        float forwardSpeed = Vector3.Dot(flat, transform.forward);

        if (grounded)
        {
            // Accelerate / brake along facing direction
            if (Mathf.Abs(throttle) > 0.01f)
            {
                bool braking_ = throttle * forwardSpeed < 0f;
                forwardSpeed += throttle * (braking_ ? braking : acceleration) * Time.fixedDeltaTime;
            }
            else
            {
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, drag * Time.fixedDeltaTime);
            }
            forwardSpeed = Mathf.Clamp(forwardSpeed, -maxSpeed * 0.5f, maxSpeed);

            // Kill sideways slip so the kart follows its heading
            Vector3 newFlat = transform.forward * forwardSpeed;
            vel.x = newFlat.x;
            vel.z = newFlat.z;
        }

        // Steering only turns the kart while it is moving; reverses when going backward
        float speedFactor = Mathf.Clamp(forwardSpeed / maxSpeed, -1f, 1f);
        if (grounded && Mathf.Abs(speedFactor) > 0.01f)
        {
            float yaw = steer * turnSpeed * speedFactor * Time.fixedDeltaTime;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yaw, 0f));
        }

        if (jumpQueued && grounded)
            vel.y = jumpSpeed;
        jumpQueued = false;

        rb.linearVelocity = vel;
    }

    bool touchingGround;

    /// <summary>True when the kart was touching the ground during the last physics step.</summary>
    public bool OnGround { get; private set; }

    // Grounded = touching something whose surface faces mostly up.
    void OnCollisionStay(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                touchingGround = true;
                return;
            }
        }
    }

    bool IsGrounded()
    {
        bool result = touchingGround;
        touchingGround = false; // refreshed by OnCollisionStay each physics step
        return result;
    }

    void UpdateWheels()
    {
        float target = steer * maxWheelSteerAngle;
        currentWheelAngle = Mathf.Lerp(currentWheelAngle, target, 1f - Mathf.Exp(-wheelSteerSmoothing * Time.deltaTime));
        SteerWheelTo(leftWheel, currentWheelAngle);
        SteerWheelTo(rightWheel, currentWheelAngle);
    }
}
