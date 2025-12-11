using UnityEngine;

/// <summary>
/// Abstract base class for all IMU movement control modes.
/// Handles sensor reading, deadzone filtering, and angle normalization.
/// Derived classes implement different mapping curves (Linear, Exponential, etc.)
/// </summary>
public abstract class IMUMovementInputBase : MonoBehaviour
{
    [Header("IMU Source")]
    [Tooltip("Reference to the OpenZen IMU sensor")]
    public OpenZenMoveObject openZenIMU;

    [Header("Angle Mapping Settings")]
    [Tooltip("Pitch angle (degrees) that produces maximum forward/backward speed")]
    public float pitchMaxAngle = 30f;
    
    [Tooltip("Roll angle (degrees) that produces maximum left/right speed")]
    public float rollMaxAngle = 30f;

    [Header("Deadzone Settings")]
    [Tooltip("Ignore pitch angles smaller than this (degrees)")]
    public float pitchDeadzone = 5f;
    
    [Tooltip("Ignore roll angles smaller than this (degrees)")]
    public float rollDeadzone = 5f;

    [Header("Inversion Settings")]
    [Tooltip("Invert pitch direction (forward becomes backward)")]
    public bool invertPitch = false;
    
    [Tooltip("Invert roll direction (left becomes right)")]
    public bool invertRoll = true;

    [Header("Calibration")]
    [Tooltip("Press this key to calibrate neutral position")]
    public KeyCode calibrateKey = KeyCode.C;

    [Header("Auto-Calibration")]
    [Tooltip("Automatically calibrate neutral position on Start (not needed if OpenZenMoveObject already calibrates)")]
    public bool autoCalibrateOnStart = false;
    
    private Vector3 _calibrationOffset = Vector3.zero;
    private bool _initialized = false;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================
    
    /// <summary>
    /// Movement vector computed from IMU orientation.
    /// X = left/right (-1 to +1), Y = 0 (no height), Z = forward/back (-1 to +1)
    /// </summary>
    public Vector3 MovementVector { get; private set; }

    /// <summary>
    /// Returns true if IMU is available and providing data
    /// </summary>
    public bool IsAvailable => openZenIMU != null;

    /// <summary>
    /// Returns true if pitch is actively producing left/right movement
    /// (based on actual movement output, not just angle exceeding deadzone)
    /// Used to disable conflicting inputs like headset yaw rotation
    /// </summary>
    public bool IsPitchActive { get; private set; }

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
            // Use direct sensor angles (already calibrated by OpenZenMoveObject)
            Vector3 angles = openZenIMU.SensorEulerAnglesDirect;
            // Apply optional second-layer calibration if enabled
            if (autoCalibrateOnStart || _calibrationOffset != Vector3.zero)
                angles -= _calibrationOffset;
            MovementVector = ConvertIMUToMovement(angles);
        }
        else
        {
            MovementVector = Vector3.zero;
        }
    }

    // ============================================
    // CONVERSION LOGIC
    // ============================================

    /// <summary>
    /// Converts IMU sensor Euler angles to normalized movement vector.
    /// Uses the mapping curve defined by the derived class.
    /// </summary>
    Vector3 ConvertIMUToMovement(Vector3 eulerAngles)
    {
        // Normalize angles to -180 to +180 range
        // SWAPPED: Roll controls forward/back, Pitch controls left/right
        float pitch = NormalizeAngle(eulerAngles.x);
        float roll = NormalizeAngle(eulerAngles.z);
        float yaw = NormalizeAngle(eulerAngles.y); 

        // Apply deadzones
        if (Mathf.Abs(pitch) < pitchDeadzone) pitch = 0f;
        if (Mathf.Abs(roll) < rollDeadzone) roll = 0f;

        // Normalize to 0-1 range for mapping curve
        float pitchNormalized = Mathf.Clamp01(Mathf.Abs(pitch) / pitchMaxAngle);
        float rollNormalized = Mathf.Clamp01(Mathf.Abs(roll) / rollMaxAngle);

        // Apply the specific mapping curve (implemented by derived class)
        // SWAPPED: Pitch uses roll curve, Roll uses pitch curve
        float forwardMapped = ApplyRollMappingCurve(rollNormalized);   // Roll → Forward/Back
        float rightMapped = ApplyPitchMappingCurve(pitchNormalized);   // Pitch → Left/Right

        // Restore sign and apply inversions
        // SWAPPED: Roll controls forward, Pitch controls right
        float forward = Mathf.Sign(roll) * forwardMapped;
        float right = Mathf.Sign(pitch) * rightMapped;

        if (invertPitch) forward = -forward;  // Now affects forward/back
        if (invertRoll) right = -right;        // Now affects left/right

        // Check if there's actual left/right movement (pitch controls left/right)
        // Use a small threshold to ignore floating-point noise
        IsPitchActive = Mathf.Abs(right) > 0.01f;

        // Return as movement vector (X=right, Y=height, Z=forward)
        return new Vector3(right, 0f, forward);
    }

    /// <summary>
    /// Maps normalized pitch input (0-1) to forward/backward output using the specific curve.
    /// Must be implemented by derived classes.
    /// </summary>
    protected abstract float ApplyPitchMappingCurve(float normalizedInput);

    /// <summary>
    /// Maps normalized roll input (0-1) to left/right output using the specific curve.
    /// Must be implemented by derived classes.
    /// </summary>
    protected abstract float ApplyRollMappingCurve(float normalizedInput);

    /// <summary>
    /// Normalizes angle from 0-360 range to -180 to +180 range
    /// </summary>
    protected float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            return angle - 360f;
        return angle;
    }

    /// <summary>
    /// Calibrates the current IMU orientation as the neutral position
    /// </summary>
    public void CalibrateNeutral()
    {
        if (IsAvailable)
        {
            // Use direct sensor angles for calibration
            _calibrationOffset = openZenIMU.SensorEulerAnglesDirect;
            Debug.Log($"IMU Calibrated. Neutral position offset: {_calibrationOffset}");
        }
        else
        {
            Debug.LogWarning("Cannot calibrate: IMU not available");
        }
    }

    // ============================================
    // DEBUG HELPERS
    // ============================================

    /// <summary>
    /// Returns current pitch angle after normalization and deadzone
    /// </summary>
    public float GetPitchAngle()
    {
        if (!IsAvailable) return 0f;
        
        // Use direct sensor angles
        Vector3 calibratedAngles = openZenIMU.SensorEulerAnglesDirect - _calibrationOffset;
        float pitch = NormalizeAngle(calibratedAngles.x);
        if (Mathf.Abs(pitch) < pitchDeadzone) pitch = 0f;
        return pitch;
    }

    /// <summary>
    /// Returns current roll angle after normalization and deadzone
    /// </summary>
    public float GetRollAngle()
    {
        if (!IsAvailable) return 0f;
        
        // Use direct sensor angles
        Vector3 calibratedAngles = openZenIMU.SensorEulerAnglesDirect - _calibrationOffset;
        float roll = NormalizeAngle(calibratedAngles.z);
        if (Mathf.Abs(roll) < rollDeadzone) roll = 0f;
        return roll;
    }

    /// <summary>
    /// Returns current yaw angle (for display purposes only - NOT used for control)
    /// </summary>
    public float GetYawAngle()
    {
        if (!IsAvailable) return 0f;
        
        // Use direct sensor angles
        Vector3 calibratedAngles = openZenIMU.SensorEulerAnglesDirect - _calibrationOffset;
        float yaw = NormalizeAngle(calibratedAngles.y);
        return yaw;
    }
}
