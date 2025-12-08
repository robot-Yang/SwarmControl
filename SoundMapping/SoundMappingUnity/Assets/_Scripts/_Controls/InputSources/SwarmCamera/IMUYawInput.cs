using UnityEngine;

/// <summary>
/// Reads yaw (Y-axis rotation) from OpenZen IMU sensor for camera rotation control.
/// Uses same sensor as IMUMovementInputBase but focuses on yaw instead of pitch/roll.
/// </summary>
public class IMUYawInput : MonoBehaviour
{
    [Header("IMU Source")]
    [Tooltip("Reference to the OpenZen IMU sensor (same sensor used for movement)")]
    public OpenZenMoveObject openZenIMU;

    [Header("Yaw Settings")]
    [Tooltip("Yaw angle (degrees) that produces maximum rotation speed")]
    public float yawMaxAngle = 75f;

    [Tooltip("Ignore yaw angles smaller than this (degrees)")]
    public float yawDeadzone = 5f;

    [Tooltip("Maximum rotation speed at full yaw tilt")]
    public float maxRotationSpeed = 2.0f;

    [Tooltip("Invert yaw direction (left becomes right)")]
    public bool invertYaw = false;

    [Header("Calibration")]
    [Tooltip("Press this key to calibrate neutral yaw position")]
    public KeyCode calibrateKey = KeyCode.V;

    [Header("Auto-Calibration")]
    [Tooltip("Automatically calibrate neutral position on Start")]
    public bool autoCalibrateOnStart = true;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private Vector3 _calibrationOffset = Vector3.zero;
    private bool _initialized = false;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>
    /// Normalized yaw rotation rate (-1 to +1)
    /// Positive = turning right, Negative = turning left
    /// </summary>
    public float YawRotationRate { get; private set; }

    /// <summary>
    /// Returns true if IMU is available and providing data
    /// </summary>
    public bool IsAvailable => openZenIMU != null;

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        // Auto-calibrate on first frame if enabled
        if (!_initialized && IsAvailable && autoCalibrateOnStart)
        {
            CalibrateNeutral();
            _initialized = true;
        }

        // Handle manual calibration input
        if (Input.GetKeyDown(calibrateKey))
        {
            CalibrateNeutral();
        }

        if (IsAvailable)
        {
            // Use direct Euler angles from sensor (not quaternion-derived)
            Vector3 rawAngles = openZenIMU.SensorEulerAnglesDirect;
            Vector3 calibratedAngles = rawAngles - _calibrationOffset;
            YawRotationRate = ConvertYawToRotation(calibratedAngles);
        }
        else
        {
            YawRotationRate = 0f;
        }
    }

    // ============================================
    // CONVERSION LOGIC
    // ============================================

    /// <summary>
    /// Converts IMU yaw angle to normalized rotation rate
    /// </summary>
    float ConvertYawToRotation(Vector3 eulerAngles)
    {
        // Extract yaw (Y-axis rotation)
        float yaw = NormalizeAngle(eulerAngles.y);

        // Apply deadzone
        if (Mathf.Abs(yaw) < yawDeadzone)
        {
            return 0f;
        }

        // Normalize to 0-1 range based on max angle
        float yawNormalized = Mathf.Clamp01(Mathf.Abs(yaw) / yawMaxAngle);

        // Apply linear mapping (rate-based)
        float rotationMapped = yawNormalized * maxRotationSpeed;

        // Restore sign and apply inversion
        float rotation = Mathf.Sign(yaw) * rotationMapped;
        if (invertYaw) rotation = -rotation;

        if (showDebugInfo && Mathf.Abs(rotation) > 0.01f)
        {
            Debug.Log($"[IMU Yaw] Raw: {eulerAngles.y:F1}° | Calibrated: {yaw:F1}° | Output: {rotation:F2}");
        }

        return rotation;
    }

    /// <summary>
    /// Normalizes angle from 0-360 range to -180 to +180 range
    /// </summary>
    float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            return angle - 360f;
        return angle;
    }

    /// <summary>
    /// Calibrates the current IMU yaw as the neutral position
    /// </summary>
    public void CalibrateNeutral()
    {
        if (IsAvailable)
        {
            _calibrationOffset = openZenIMU.SensorEulerAnglesDirect;
            Debug.Log($"IMU Yaw Calibrated. Neutral position offset: {_calibrationOffset}");
        }
        else
        {
            Debug.LogWarning("Cannot calibrate: IMU not available");
        }
    }

    /// <summary>
    /// Returns current yaw angle after normalization and deadzone
    /// </summary>
    public float GetYawAngle()
    {
        if (!IsAvailable) return 0f;

        Vector3 calibratedAngles = openZenIMU.SensorEulerAnglesDirect - _calibrationOffset;
        float yaw = NormalizeAngle(calibratedAngles.y);
        if (Mathf.Abs(yaw) < yawDeadzone) yaw = 0f;
        return yaw;
    }

    // ============================================
    // DEBUG VISUALIZATION
    // ============================================

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, 420, 300, 120));
        GUILayout.Label("<b>IMU Yaw Input</b>");
        GUILayout.Label($"Available: {IsAvailable}");
        if (IsAvailable)
        {
            GUILayout.Label($"Yaw Angle: {GetYawAngle():F1}°");
            GUILayout.Label($"Rotation Rate: {YawRotationRate:F2}");
        }
        GUILayout.EndArea();
    }
}
