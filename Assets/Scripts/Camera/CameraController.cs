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

    float smoothYaw;
    bool initialized;

    void LateUpdate()
    {
        if (target == null) return;

        if (!initialized)
        {
            smoothYaw   = target.eulerAngles.y;
            initialized = true;
        }

        // Yaw lags behind the car's heading — gives a cinematic feel on turns
        smoothYaw = Mathf.LerpAngle(smoothYaw, target.eulerAngles.y, Time.deltaTime * rotationSmoothing);
        Quaternion yawRotation = Quaternion.Euler(0f, smoothYaw, 0f);

        Vector3 desiredPosition = target.position + yawRotation * offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * positionSmoothing);

        transform.LookAt(target.position + Vector3.up * lookHeightOffset);
    }
}
