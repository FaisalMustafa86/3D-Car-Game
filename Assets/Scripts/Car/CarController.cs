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
    [SerializeField] float steerSmoothing = 6f;
    [SerializeField] float throttleSmoothing = 3f;

    [Header("Wheel Mesh Alignment")]
    [Tooltip("Extra rotation applied to wheel meshes to match how the model was authored. Try (0,0,0) first.")]
    [SerializeField] Vector3 wheelMeshRotationOffset = Vector3.zero;

    [Header("Physics Stability")]
    [Tooltip("Lower = more stable, higher = more tippy. Should sit roughly at the car's floor level, slightly forward of center.")]
    [SerializeField] Vector3 centerOfMassOffset = new Vector3(0f, -0.4f, 0.1f);

    public float SpeedKmh { get; private set; }

    Rigidbody rb;
    InputSystem_Actions input;

    float currentSteer;
    float currentThrottle;
    float rawThrottle;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;

        input = new InputSystem_Actions();

        ConfigureWheelFriction();
    }

    void ConfigureWheelFriction()
    {
        // High stiffness = grippy, no drifting
        var forward = new WheelFrictionCurve
        {
            extremumSlip = 0.4f, extremumValue = 1f,
            asymptoteSlip = 0.8f, asymptoteValue = 0.75f,
            stiffness = 1.5f
        };
        var sideways = new WheelFrictionCurve
        {
            extremumSlip = 0.2f, extremumValue = 1f,
            asymptoteSlip = 0.5f, asymptoteValue = 0.75f,
            stiffness = 2.5f
        };

        foreach (var w in new[] { frontLeft, frontRight, rearLeft, rearRight })
        {
            w.forwardFriction = forward;
            w.sidewaysFriction = sideways;
        }
    }

    void OnEnable()  => input.Player.Enable();
    void OnDisable() => input.Player.Disable();

    void Update()
    {
        var move = input.Player.Move.ReadValue<Vector2>();
        SpeedKmh    = rb.linearVelocity.magnitude * 3.6f;
        rawThrottle = move.y;

        currentSteer    = Mathf.Lerp(currentSteer,    move.x * maxSteerAngle, Time.deltaTime * steerSmoothing);
        currentThrottle = Mathf.Lerp(currentThrottle, move.y,                 Time.deltaTime * throttleSmoothing);
    }

    void FixedUpdate()
    {
        ApplySteering();
        ApplyMotor();
        SyncWheelMeshes();
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
