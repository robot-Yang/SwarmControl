using UnityEngine;

/// <summary>
/// Top-view camera that follows the embodied drone from above.
/// Attach this to a separate camera GameObject in your scene.
/// </summary>
public class TopViewCamera : MonoBehaviour
{
    [Header("Camera Settings")]
    [Tooltip("Height of the camera above the embodied drone")]
    public float cameraHeight = 15f;
    
    [Tooltip("How smoothly the camera follows the drone (0 = instant, higher = smoother)")]
    public float followSmoothness = 5f;
    
    [Tooltip("Orthographic size of the camera (field of view)")]
    public float orthographicSize = 10f;
    
    [Tooltip("Enable this to rotate the camera with the drone's forward direction")]
    public bool rotateWithDrone = true;
    
    [Tooltip("Rotation smoothness (only if rotateWithDrone is true)")]
    public float rotationSmoothness = 3f;

    [Header("Optional: Follow Offset")]
    [Tooltip("Offset from the drone position (in world space)")]
    public Vector3 positionOffset = Vector3.zero;

    private Camera topCamera;

    void Start()
    {
        // Get or add camera component
        topCamera = GetComponent<Camera>();
        if (topCamera == null)
        {
            topCamera = gameObject.AddComponent<Camera>();
        }

        // Set camera to orthographic for true top-down view
        topCamera.orthographic = true;
        topCamera.orthographicSize = orthographicSize;

        // Position camera to look straight down
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void LateUpdate()
    {
        // Check if there's an embodied drone
        if (CameraMovement.embodiedDrone != null)
        {
            // Target position: above the embodied drone
            Vector3 targetPosition = CameraMovement.embodiedDrone.transform.position 
                                    + Vector3.up * cameraHeight 
                                    + positionOffset;

            // Smoothly move camera to target position
            if (followSmoothness > 0)
            {
                transform.position = Vector3.Lerp(
                    transform.position, 
                    targetPosition, 
                    Time.deltaTime * followSmoothness
                );
            }
            else
            {
                transform.position = targetPosition;
            }

            // Optional: Rotate camera to match drone's forward direction
            if (rotateWithDrone)
            {
                // Get the drone's forward direction projected on XZ plane
                Vector3 droneForward = CameraMovement.embodiedDrone.transform.forward;
                droneForward.y = 0;

                if (droneForward.magnitude > 0.01f)
                {
                    // Calculate rotation to align camera's "up" with drone's forward
                    Quaternion targetRotation = Quaternion.LookRotation(Vector3.down, droneForward);

                    if (rotationSmoothness > 0)
                    {
                        transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            targetRotation,
                            Time.deltaTime * rotationSmoothness
                        );
                    }
                    else
                    {
                        transform.rotation = targetRotation;
                    }
                }
            }
        }
        else
        {
            // No embodied drone - optionally disable camera or keep it at last position
            // topCamera.enabled = false;
        }
    }

    void OnValidate()
    {
        // Update camera settings in editor when values change
        if (topCamera != null)
        {
            topCamera.orthographicSize = orthographicSize;
        }
    }
}
