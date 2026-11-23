using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Transform target;
    [SerializeField] Rigidbody targetBody; // optional, found on target if empty

    [Header("Follow")]
    [SerializeField] Vector3 offset = new Vector3(0f, 3f, -7f); // relative to the kart's heading
    [SerializeField] Vector3 lookAtOffset = new Vector3(0f, 1f, 2f);
    [SerializeField] float positionSmoothing = 8f;
    [SerializeField] float rotationSmoothing = 6f;

    [Header("Speed warp")]
    [SerializeField] float maxSpeed = 20f; // match the PlayerController max speed
    [SerializeField] float baseFov = 60f;
    [SerializeField] float maxFovBoost = 25f;
    [SerializeField] float pullBackDistance = 2f;
    [SerializeField] float fovSmoothing = 4f; // lower = more lag, so acceleration feels like a stretch

    Camera cam;
    float currentFov;
    float currentPullBack;
    float smoothedYaw;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.fieldOfView = baseFov;
        currentFov = baseFov;
    }

    void Start()
    {
        if (target == null) return;
        if (targetBody == null) targetBody = target.GetComponentInParent<Rigidbody>();
        smoothedYaw = target.eulerAngles.y;
        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Follow the kart's heading (yaw only) so bumps and jumps don't tilt the camera
        smoothedYaw = Mathf.LerpAngle(smoothedYaw, target.eulerAngles.y, Smooth(rotationSmoothing));
        Quaternion yawRot = Quaternion.Euler(0f, smoothedYaw, 0f);

        // Speed warp: FOV widens and the camera drops back as the kart speeds up
        float speed = targetBody ? targetBody.linearVelocity.magnitude : 0f;
        float t = Mathf.Clamp01(speed / maxSpeed);
        currentFov = Mathf.Lerp(currentFov, baseFov + maxFovBoost * t, Smooth(fovSmoothing));
        currentPullBack = Mathf.Lerp(currentPullBack, pullBackDistance * t, Smooth(fovSmoothing));
        cam.fieldOfView = currentFov;

        Vector3 desiredPos = target.position + yawRot * (offset + Vector3.back * currentPullBack);
        transform.position = Vector3.Lerp(transform.position, desiredPos, Smooth(positionSmoothing));

        Vector3 lookPoint = target.position + yawRot * lookAtOffset;
        Quaternion lookRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        transform.rotation = lookRot;
    }

    void SnapToTarget()
    {
        Quaternion yawRot = Quaternion.Euler(0f, smoothedYaw, 0f);
        transform.position = target.position + yawRot * offset;
        transform.rotation = Quaternion.LookRotation(target.position + yawRot * lookAtOffset - transform.position, Vector3.up);
    }

    // Frame-rate independent lerp factor
    static float Smooth(float rate) => 1f - Mathf.Exp(-rate * Time.deltaTime);
}
