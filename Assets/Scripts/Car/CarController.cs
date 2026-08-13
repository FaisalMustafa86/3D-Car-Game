using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] WheelCollider frontLeft;
    [SerializeField] WheelCollider frontRight;
    [SerializeField] WheelCollider rearLeft;
    [SerializeField] WheelCollider rearRight;

    [Header("Wheel Meshes")]
    [SerializeField] Transform meshFrontLeft;
    [SerializeField] Transform meshFrontRight;
    [SerializeField] Transform meshRearLeft;
    [SerializeField] Transform meshRearRight;

    [Header("Driving Feel")]
    [SerializeField] float motorTorque = 1200f;
    [SerializeField] float brakeTorque = 3000f;
    [SerializeField] float maxSteerAngle = 28f;
    [SerializeField] float topSpeedKmh = 80f;
    [Tooltip("Steering response. Higher = snappier / more immediate.")]
    [SerializeField] float steerSmoothing = 10f;
    [SerializeField] float throttleSmoothing = 3f;

    [Header("Drift Feel — main dials")]
    [Tooltip("THE key dial. How quickly the car's momentum swings to follow where the nose points. HIGHER = grippier, more responsive, drifts less. LOWER = looser, bigger slides. Tune this first.")]
    [SerializeField] float gripAssist = 2.8f;
    [Tooltip("Turn-in sharpness: how hard the car rotates toward your steering. Higher = snappier, more eager to rotate into a drift.")]
    [SerializeField] float driftStability = 6f;
    [Tooltip("Fastest the car rotates from steering (deg/sec). Higher = quicker direction changes / easier to swing the tail round.")]
    [SerializeField] float maxDriftYawRate = 105f;

    [Header("Tyre Grip (secondary)")]
    [Tooltip("Front tyre sideways grip. Steering authority at low speed.")]
    [SerializeField] float frontGrip = 2.5f;
    [Tooltip("Rear tyre sideways grip. Low-speed grounding; at speed the Grip Assist dial does the real work.")]
    [SerializeField] float rearGrip = 1.8f;

    [Header("Handbrake (hold Space / gamepad South)")]
    [Tooltip("Fraction of Grip Assist kept while the handbrake is held. LOWER = the momentum lets go and the car slides big. 0.2 ≈ effortless deliberate drift.")]
    [Range(0f, 1f)]
    [SerializeField] float handbrakeGrip = 0.2f;
    [Tooltip("Rear tyre grip while handbraking, for extra looseness.")]
    [SerializeField] float driftSidewaysStiffness = 0.6f;
    [Tooltip("How quickly the rear tyres lose / regain grip on press / release. Higher = snappier, lower = smoother.")]
    [SerializeField] float driftGripSmoothing = 8f;
    [Tooltip("Light rear brake applied while handbraking to help break traction.")]
    [SerializeField] float driftRearBrake = 200f;

    [Header("Wheel Mesh Alignment")]
    [Tooltip("Extra rotation applied to wheel meshes to match how the model was authored. Try (0,0,0) first.")]
    [SerializeField] Vector3 wheelMeshRotationOffset = Vector3.zero;

    [Header("Physics Stability")]
    [Tooltip("Lower = more stable, higher = more tippy. Should sit roughly at the car's floor level, slightly forward of center.")]
    [SerializeField] Vector3 centerOfMassOffset = new Vector3(0f, -0.5f, 0.1f);
    [Tooltip("Stiffness of the anti-roll (stabilizer) bars. Higher = resists body roll / flipping harder. This is the main anti-flip control.")]
    [SerializeField] float antiRollForce = 6000f;
    [Tooltip("Extra downward force scaled by speed to keep the car planted through turns.")]
    [SerializeField] float downforce = 80f;
    [Tooltip("Hard cap on how fast the body can rotate (rad/s). Stops violent flips/spins from ever building up.")]
    [SerializeField] float maxAngularVelocity = 3.5f;
    [Tooltip("How much steering is reduced at top speed (0 = none, 1 = steering fully cut). Prevents fast-turn flips.")]
    [Range(0f, 1f)]
    [SerializeField] float highSpeedSteerReduction = 0.12f;

    public float SpeedKmh { get; private set; }

    Rigidbody rb;
    InputSystem_Actions input;

    float currentSteer;
    float currentThrottle;
    float rawThrottle;

    // Drift state
    WheelFrictionCurve rearSidewaysCurve;
    float baseSidewaysStiffness;
    float currentRearStiffness;
    float currentGrip;
    bool handbrake;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;
        rb.maxAngularVelocity = maxAngularVelocity;
        // Smooths the body between the fixed physics steps for rendering — without
        // this the camera pumps in/out and the wheels look like they judder.
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        input = new InputSystem_Actions();
        currentGrip = gripAssist;

        ConfigureWheelFriction();
    }

    void ConfigureWheelFriction()
    {
        var forward = new WheelFrictionCurve
        {
            extremumSlip = 0.4f, extremumValue = 1f,
            asymptoteSlip = 0.8f, asymptoteValue = 0.75f,
            stiffness = 1.5f
        };

        // Grippy front so steering always places the car; looser rear so the
        // back slides into corners (Inertial-Drift style). The yaw stabilizer
        // then keeps that slide controllable.
        var frontSideways = new WheelFrictionCurve
        {
            extremumSlip = 0.2f, extremumValue = 1f,
            asymptoteSlip = 0.5f, asymptoteValue = 0.9f,
            stiffness = frontGrip
        };
        var rearSideways = frontSideways;
        rearSideways.stiffness = rearGrip;

        frontLeft.forwardFriction  = forward;
        frontRight.forwardFriction = forward;
        rearLeft.forwardFriction   = forward;
        rearRight.forwardFriction  = forward;

        frontLeft.sidewaysFriction  = frontSideways;
        frontRight.sidewaysFriction = frontSideways;
        rearLeft.sidewaysFriction   = rearSideways;
        rearRight.sidewaysFriction  = rearSideways;

        // The handbrake code dials the rear between this baseline and a looser slide.
        rearSidewaysCurve     = rearSideways;
        baseSidewaysStiffness = rearGrip;
        currentRearStiffness  = rearGrip;
    }

    void OnEnable()  => input.Player.Enable();
    void OnDisable() => input.Player.Disable();

    void Update()
    {
        var move = input.Player.Move.ReadValue<Vector2>();
        SpeedKmh    = rb.linearVelocity.magnitude * 3.6f;
        rawThrottle = move.y;
        handbrake   = input.Player.Jump.IsPressed();

        // Ease off the steering as speed rises so hard turns can't tip the car.
        float speedT           = Mathf.Clamp01(SpeedKmh / topSpeedKmh);
        float steerFactor      = Mathf.Lerp(1f, 1f - highSpeedSteerReduction, speedT);
        float targetSteer      = move.x * maxSteerAngle * steerFactor;

        currentSteer    = Mathf.Lerp(currentSteer,    targetSteer, Time.deltaTime * steerSmoothing);
        currentThrottle = Mathf.Lerp(currentThrottle, move.y,      Time.deltaTime * throttleSmoothing);

        // Visuals run in the render loop so the interpolated body and the
        // wheels stay in lockstep (no juddering "un-round" look).
        SyncWheelMeshes();
    }

    void FixedUpdate()
    {
        ApplySteering();
        ApplyMotor();
        ApplyDrift();
        ApplyDriftHandling();
        ApplyGripAssist();
        ApplyAntiRoll();
        ApplyDownforce();
    }

    void ApplySteering()
    {
        frontLeft.steerAngle  = currentSteer;
        frontRight.steerAngle = currentSteer;
    }

    void ApplyMotor()
    {
        float t = currentThrottle;
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float speedFactor  = Mathf.Clamp01(1f - SpeedKmh / topSpeedKmh);

        if (rawThrottle > 0.01f)
        {
            // Drive forward — smoothed value for gradual acceleration feel
            SetMotorTorque(currentThrottle * motorTorque * speedFactor);
            SetBrakeTorque(0f);
        }
        else if (rawThrottle < -0.01f && forwardSpeed > 0.5f)
        {
            // Brake while rolling forward
            SetMotorTorque(0f);
            float b = Mathf.Abs(currentThrottle) * brakeTorque;
            frontLeft.brakeTorque  = b;
            frontRight.brakeTorque = b;
            rearLeft.brakeTorque   = b * 0.7f;
            rearRight.brakeTorque  = b * 0.7f;
        }
        else if (rawThrottle < -0.01f)
        {
            // Reverse
            float reverseLimit  = topSpeedKmh * 0.4f;
            float reverseFactor = Mathf.Clamp01(1f - SpeedKmh / reverseLimit);
            SetMotorTorque(currentThrottle * motorTorque * 0.8f * reverseFactor);
            SetBrakeTorque(0f);
        }
        else
        {
            SetMotorTorque(0f);
            SetBrakeTorque(400f);
            if (SpeedKmh < 0.5f)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    // Handbrake: while Space is held, smoothly bleed the rear tyres' grip further
    // (on top of the already-loose rear) for deliberate bigger slides, plus a light
    // rear brake to help break traction. Releasing eases the grip back smoothly.
    // Direction/angle of the slide is governed by ApplyDriftHandling below.
    void ApplyDrift()
    {
        float target = handbrake ? driftSidewaysStiffness : baseSidewaysStiffness;
        currentRearStiffness = Mathf.Lerp(currentRearStiffness, target, Time.fixedDeltaTime * driftGripSmoothing);

        rearSidewaysCurve.stiffness = currentRearStiffness;
        rearLeft.sidewaysFriction   = rearSidewaysCurve;
        rearRight.sidewaysFriction  = rearSidewaysCurve;

        if (!handbrake) return;

        rearLeft.brakeTorque  = Mathf.Max(rearLeft.brakeTorque,  driftRearBrake);
        rearRight.brakeTorque = Mathf.Max(rearRight.brakeTorque, driftRearBrake);
    }

    // Turn-in servo: rotates the car toward the yaw rate the player's steering is
    // asking for (and resists any rotation they didn't ask for). This is what makes
    // steering feel crisp and eager, and stops the tail snapping into a spin.
    void ApplyDriftHandling()
    {
        if (SpeedKmh < 3f) return;

        float yawRate   = Vector3.Dot(rb.angularVelocity, transform.up);
        float steerNorm = currentSteer / maxSteerAngle;             // -1..1
        // Positive steer = turn right = positive yaw about the car's up axis.
        float targetYaw = steerNorm * (maxDriftYawRate * Mathf.Deg2Rad);

        rb.AddTorque(transform.up * ((targetYaw - yawRate) * driftStability), ForceMode.Acceleration);
    }

    // Grip assist (velocity redirection) — the heart of the "effortless" feel.
    // Each step the car's horizontal momentum is swung toward wherever the nose is
    // pointing, at 'gripAssist' rad/s. Because the nose is what the servo/steering
    // controls, the car reliably GOES WHERE YOU POINT while any gap between heading
    // and travel shows up as a controllable drift angle. Higher grip = tighter and
    // more responsive; the handbrake drops it so the car slides big on demand.
    void ApplyGripAssist()
    {
        Vector3 v    = rb.linearVelocity;
        Vector3 flat = new Vector3(v.x, 0f, v.z);
        float speed  = flat.magnitude;
        if (speed < 1.5f) return;

        Vector3 nose = transform.forward;
        nose.y = 0f;
        if (nose.sqrMagnitude < 0.0001f) return;
        nose.Normalize();

        // Follow the nose forwards or backwards depending on travel direction.
        float travelDir = Vector3.Dot(flat, nose) < 0f ? -1f : 1f;
        Vector3 target  = nose * travelDir;

        float targetGrip = handbrake ? gripAssist * handbrakeGrip : gripAssist;
        currentGrip = Mathf.Lerp(currentGrip, targetGrip, Time.fixedDeltaTime * driftGripSmoothing);

        Vector3 newDir = Vector3.RotateTowards(flat / speed, target, currentGrip * Time.fixedDeltaTime, 0f);
        rb.linearVelocity = new Vector3(newDir.x * speed, v.y, newDir.z * speed);
    }

    // Anti-roll (stabilizer) bars: for each axle, if one wheel is compressed
    // more than its partner, push the compressed side up and the loose side
    // down to resist body roll. This is what keeps the car from tipping over
    // in hard turns while still allowing grippy, non-drifty cornering.
    void ApplyAntiRoll()
    {
        ApplyAntiRollAxle(frontLeft, frontRight);
        ApplyAntiRollAxle(rearLeft, rearRight);
    }

    void ApplyAntiRollAxle(WheelCollider left, WheelCollider right)
    {
        float travelL = 1f;
        float travelR = 1f;

        bool groundedL = left.GetGroundHit(out WheelHit hitL);
        if (groundedL)
            travelL = Mathf.Clamp01((-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance);

        bool groundedR = right.GetGroundHit(out WheelHit hitR);
        if (groundedR)
            travelR = Mathf.Clamp01((-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance);

        float antiRoll = (travelL - travelR) * antiRollForce;

        if (groundedL)
            rb.AddForceAtPosition(left.transform.up * -antiRoll, left.transform.position);
        if (groundedR)
            rb.AddForceAtPosition(right.transform.up * antiRoll, right.transform.position);
    }

    // Presses the car onto its wheels harder the faster it goes, keeping grip
    // through corners without needing a top-heavy amount of tire stiffness.
    void ApplyDownforce()
    {
        float speed = rb.linearVelocity.magnitude;
        rb.AddForce(-transform.up * (downforce * speed));
    }

    void SetMotorTorque(float torque)
    {
        rearLeft.motorTorque  = torque;
        rearRight.motorTorque = torque;
    }

    void SetBrakeTorque(float torque)
    {
        frontLeft.brakeTorque  = torque;
        frontRight.brakeTorque = torque;
        rearLeft.brakeTorque   = torque;
        rearRight.brakeTorque  = torque;
    }

    void SyncWheelMeshes()
    {
        SyncMesh(frontLeft,  meshFrontLeft);
        SyncMesh(frontRight, meshFrontRight);
        SyncMesh(rearLeft,   meshRearLeft);
        SyncMesh(rearRight,  meshRearRight);
    }

    void SyncMesh(WheelCollider col, Transform mesh)
    {
        if (mesh == null) return;
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.SetPositionAndRotation(pos, rot * Quaternion.Euler(wheelMeshRotationOffset));
    }
}
