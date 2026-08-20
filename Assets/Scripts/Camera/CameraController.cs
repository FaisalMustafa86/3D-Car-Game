using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] Transform target;

    [Header("Position")]
    [SerializeField] Vector3 offset = new Vector3(0f, 3f, -7f);
    [SerializeField] float positionSmoothing = 5f;

    [Header("Rotation")]
    [SerializeField] float rotationSmoothing = 3f;

    [Header("Look")]
    [SerializeField] float lookHeightOffset = 1f;

    [Header("Speed Sensation")]
    [Tooltip("The car to read speed from. Auto-found on the target if left empty.")]
    [SerializeField] CarController car;
    [Tooltip("Field of view when stopped / cruising slowly.")]
    [SerializeField] float baseFov = 60f;
    [Tooltip("Extra FOV added at top speed — widens the view so speed *feels* faster.")]
    [SerializeField] float fovKick = 12f;
    [Tooltip("Speed (km/h) at which the FOV kick is fully applied.")]
    [SerializeField] float fovKickSpeed = 80f;
    [Tooltip("Seconds for the FOV to catch up to its target. Higher = slower, smoother. Uses SmoothDamp so it stays glitch-free at any frame rate.")]
    [SerializeField] float fovSmoothTime = 0.25f;
    [Tooltip("Seconds to smooth the raw speed reading before it drives FOV. Filters out single-step velocity spikes from hard braking/drifting.")]
    [SerializeField] float speedSmoothTime = 0.15f;
    [Tooltip("Extra distance the camera pulls back at top speed, for a touch more drama.")]
    [SerializeField] float speedPullback = 1.2f;

    float smoothYaw;
    float currentFov;
    float fovVelocity;
    float smoothSpeed;
    float speedVelocity;
    bool initialized;
    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        currentFov = cam != null ? cam.fieldOfView : baseFov;
        if (car == null && target != null) car = target.GetComponentInParent<CarController>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (!initialized)
        {
            smoothYaw   = target.eulerAngles.y;
            initialized = true;
        }

        // Low-pass the raw speed first so a single-step velocity spike (hard
        // brake, wheels biting mid-drift) can't pop the lens for one frame.
        float rawSpeed = car != null ? car.SpeedKmh : 0f;
        smoothSpeed = Mathf.SmoothDamp(smoothSpeed, rawSpeed, ref speedVelocity, speedSmoothTime);

        // 0..1 how fast we're going — drives both FOV and camera pullback.
        float speedT = Mathf.Clamp01(smoothSpeed / fovKickSpeed);

        // Yaw lags behind the car's heading — gives a cinematic feel on turns
        smoothYaw = Mathf.LerpAngle(smoothYaw, target.eulerAngles.y, Time.deltaTime * rotationSmoothing);
        Quaternion yawRotation = Quaternion.Euler(0f, smoothYaw, 0f);

        // Pull back a touch at speed so the car sits a little deeper in frame.
        Vector3 speedOffset     = offset + new Vector3(0f, 0f, -speedPullback * speedT);
        Vector3 desiredPosition = target.position + yawRotation * speedOffset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * positionSmoothing);

        transform.LookAt(target.position + Vector3.up * lookHeightOffset);

        // Widen the lens with speed — the single biggest cue for "this feels fast".
        // SmoothDamp is frame-rate independent, so a frame hitch can't make it pop.
        if (cam != null)
        {
            float targetFov = baseFov + fovKick * speedT;
            currentFov = Mathf.SmoothDamp(currentFov, targetFov, ref fovVelocity, fovSmoothTime);
            cam.fieldOfView = currentFov;
        }
    }
}
