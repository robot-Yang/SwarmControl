using UnityEngine;

/// <summary>
/// Reads Meta Quest headset tracking data and exposes it as clean input values.
/// Uses delta/rate-based rotation for natural head-look camera control.
/// </summary>
public class MetaQuestInput : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("Reference to the OVRCameraRig in your scene")]
    public OVRCameraRig cameraRig;

    public enum RotationMode { RateBased, Absolute }

    [Header("Rotation Settings")]
    [Tooltip("Control mode: RateBased (velocity) or Absolute (position mapping)")]
    public RotationMode rotationMode = RotationMode.Absolute;

    [Tooltip("Multiplier for head rotation sensitivity (1.0 = normal, 2.0 = twice as fast)")]
    [Range(0.1f, 5.0f)]
    public float rotationSensitivity = 1.0f;

    [Header("Rate-Based Settings (only used if mode = RateBased)")]
    [Tooltip("Ignore head rotations smaller than this (degrees per second)")]
    [Range(0f, 10f)]
    public float yawDeadzone = 2f;

    [Tooltip("Maximum rotation rate value (degrees per second) - higher values = faster max turn")]
    public float maxRotationRate = 90f;

    [Header("Absolute Mode Settings (only used if mode = Absolute)")]
    [Tooltip("Maximum head yaw angle left (-90 = max left rotation speed)")]
    [Range(-180f, 0f)]
    public float maxYawLeft = -90f;

    [Tooltip("Maximum head yaw angle right (+90 = max right rotation speed)")]
    [Range(0f, 180f)]
    public float maxYawRight = 90f;

    [Tooltip("Deadzone angle from neutral (degrees) - no rotation within this range")]
    [Range(0f, 30f)]
    public float neutralDeadzone = 5f;

    [Tooltip("Duration (seconds) head must stay rotated before return movement is ignored. 0 = always ignore returns.")]
    [Range(0f, 5f)]
    public float holdDuration = 0.5f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (Read by InputFusionManager)
    // ============================================
    
    /// <summary>
    /// Normalized yaw rotation rate from headset (-1 to +1, like joystick input)
    /// Positive = turning right, Negative = turning left
    /// </summary>
    public float HeadsetYawRate { get; private set; }

    /// <summary>
    /// Raw headset orientation (full quaternion)
    /// </summary>
    public Quaternion HeadsetOrientation { get; private set; }

    /// <summary>
    /// Current yaw angle (Y-axis rotation in degrees, 0-360)
    /// </summary>
    public float CurrentYaw { get; private set; }

    // ============================================
    // PRIVATE STATE
    // ============================================
    
    private float _previousYaw = 0f;
    private bool _initialized = false;
    private float _neutralYaw = 0f; // The "forward" reference point (calibrated neutral)
    private float _holdTimer = 0f; // Time spent outside deadzone
    private bool _isHoldActive = false; // True when hold duration has been met

    // ============================================
    // INITIALIZATION
    // ============================================

    void Start()
    {
        ValidateReferences();
    }

    void ValidateReferences()
    {
        if (cameraRig == null)
        {
            // Try to find OVRCameraRig in scene
            cameraRig = FindObjectOfType<OVRCameraRig>();
            
            if (cameraRig == null)
            {
                Debug.LogWarning("MetaQuestInput: OVRCameraRig not found! Please assign it in the Inspector or ensure it exists in the scene.");
            }
            else
            {
                Debug.Log("MetaQuestInput: Automatically found OVRCameraRig in scene.");
            }
        }

        if (cameraRig != null && cameraRig.centerEyeAnchor != null)
        {
            Debug.Log("MetaQuestInput: Successfully connected to Meta Quest headset tracking.");
        }
    }

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        if (!IsHeadsetAvailable())
        {
            HeadsetYawRate = 0f;
            return;
        }

        UpdateHeadsetData();

        // Use different calculation based on mode
        if (rotationMode == RotationMode.RateBased)
        {
            CalculateYawRate();
        }
        else
        {
            CalculateAbsoluteYawRate();
        }
    }

    /// <summary>
    /// Check if headset tracking is available
    /// </summary>
    bool IsHeadsetAvailable()
    {
        return cameraRig != null && cameraRig.centerEyeAnchor != null;
    }

    /// <summary>
    /// Read current headset orientation and yaw
    /// </summary>
    void UpdateHeadsetData()
    {
        Transform centerEye = cameraRig.centerEyeAnchor;
        
        // Store full orientation
        HeadsetOrientation = centerEye.rotation;
        
        // Extract yaw (Y-axis rotation)
        CurrentYaw = centerEye.eulerAngles.y;

        // Initialize previous yaw on first frame
        if (!_initialized)
        {
            _previousYaw = CurrentYaw;
            _initialized = true;
        }
    }

    /// <summary>
    /// Calculate rate of yaw change (how fast user is turning head)
    /// RATE-BASED MODE: velocity control
    /// </summary>
    void CalculateYawRate()
    {
        // Calculate angular difference (handles 360° wraparound correctly)
        float deltaYaw = Mathf.DeltaAngle(_previousYaw, CurrentYaw);
        
        // Convert to degrees per second
        float yawRateDegreesPerSec = deltaYaw / Time.deltaTime;

        // Apply deadzone (ignore small movements)
        if (Mathf.Abs(yawRateDegreesPerSec) < yawDeadzone)
        {
            yawRateDegreesPerSec = 0f;
        }

        // Apply sensitivity multiplier
        yawRateDegreesPerSec *= rotationSensitivity;

        // Normalize to -1 to +1 range (like joystick)
        HeadsetYawRate = Mathf.Clamp(yawRateDegreesPerSec / maxRotationRate, -1f, 1f);

        // Store for next frame
        _previousYaw = CurrentYaw;

        // Debug output
        if (showDebugInfo && Mathf.Abs(HeadsetYawRate) > 0.01f)
        {
            Debug.Log($"[MetaQuest RateBased] Yaw: {CurrentYaw:F1}° | Rate: {yawRateDegreesPerSec:F1}°/s | Normalized: {HeadsetYawRate:F2}");
        }
    }

    /// <summary>
    /// Calculate rotation based on absolute head position (angle from neutral)
    /// ABSOLUTE MODE: Only responds to outward movements (0→-90 or 0→+90)
    /// Returns to neutral are ignored after holdDuration expires
    /// </summary>
    void CalculateAbsoluteYawRate()
    {
        // Calculate angle relative to neutral reference point
        float relativeYaw = Mathf.DeltaAngle(_neutralYaw, CurrentYaw);

        // Check if inside deadzone (back at neutral)
        if (Mathf.Abs(relativeYaw) < neutralDeadzone)
        {
            HeadsetYawRate = 0f;
            _holdTimer = 0f; // Reset timer when back at neutral
            _isHoldActive = false;
            _previousYaw = CurrentYaw;
            return;
        }

        // Head is outside deadzone - update hold timer
        _holdTimer += Time.deltaTime;
        if (_holdTimer >= holdDuration)
        {
            _isHoldActive = true;
        }

        float output = 0f;
        float deltaYaw = Mathf.DeltaAngle(_previousYaw, CurrentYaw);

        if (relativeYaw > 0) // Turned right from neutral
        {
            // Apply rotation if:
            // 1. Moving AWAY from neutral (outward), OR
            // 2. Moving toward neutral BUT hold is NOT active yet (quick return)
            if (deltaYaw > 0) // Still moving right (away from neutral)
            {
                // Map angle to rotation rate: 0° to maxYawRight
                float normalized = Mathf.Clamp01((relativeYaw - neutralDeadzone) / (maxYawRight - neutralDeadzone));
                output = normalized * rotationSensitivity;
            }
            else if (deltaYaw <= 0 && !_isHoldActive) // Returning toward neutral before hold expires
            {
                // Still apply counter-rotation (quick return)
                float normalized = Mathf.Clamp01((relativeYaw - neutralDeadzone) / (maxYawRight - neutralDeadzone));
                output = normalized * rotationSensitivity;
            }
            // If deltaYaw <= 0 AND _isHoldActive, we're returning after hold - output stays 0 (ignored)
        }
        else if (relativeYaw < 0) // Turned left from neutral
        {
            // Apply rotation if:
            // 1. Moving AWAY from neutral (outward), OR
            // 2. Moving toward neutral BUT hold is NOT active yet (quick return)
            if (deltaYaw < 0) // Still moving left (away from neutral)
            {
                // Map angle to rotation rate: 0° to maxYawLeft
                float normalized = Mathf.Clamp01((Mathf.Abs(relativeYaw) - neutralDeadzone) / (Mathf.Abs(maxYawLeft) - neutralDeadzone));
                output = -normalized * rotationSensitivity;
            }
            else if (deltaYaw >= 0 && !_isHoldActive) // Returning toward neutral before hold expires
            {
                // Still apply counter-rotation (quick return)
                float normalized = Mathf.Clamp01((Mathf.Abs(relativeYaw) - neutralDeadzone) / (Mathf.Abs(maxYawLeft) - neutralDeadzone));
                output = -normalized * rotationSensitivity;
            }
            // If deltaYaw >= 0 AND _isHoldActive, we're returning after hold - output stays 0 (ignored)
        }

        HeadsetYawRate = output;
        _previousYaw = CurrentYaw;

        // Debug output
        if (showDebugInfo && Mathf.Abs(HeadsetYawRate) > 0.01f)
        {
            Debug.Log($"[MetaQuest Absolute] Yaw: {CurrentYaw:F1}° | Relative: {relativeYaw:F1}° | Hold: {_holdTimer:F2}s/{holdDuration}s | Active: {_isHoldActive} | Output: {HeadsetYawRate:F2}");
        }
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    /// <summary>
    /// Returns true if headset is actively being rotated
    /// </summary>
    public bool IsRotating()
    {
        return Mathf.Abs(HeadsetYawRate) > 0.01f;
    }

    /// <summary>
    /// Get raw yaw delta in degrees (before normalization)
    /// </summary>
    public float GetRawYawDelta()
    {
        return Mathf.DeltaAngle(_previousYaw, CurrentYaw);
    }

    /// <summary>
    /// Reset tracking (useful when teleporting or changing scenes)
    /// </summary>
    public void ResetTracking()
    {
        _initialized = false;
        HeadsetYawRate = 0f;
        _previousYaw = CurrentYaw;
        _neutralYaw = CurrentYaw;
        _holdTimer = 0f;
        _isHoldActive = false;
    }

    /// <summary>
    /// Calibrate current head orientation as the new neutral/forward direction
    /// Call this when user presses a calibration button
    /// </summary>
    public void CalibrateNeutral()
    {
        if (IsHeadsetAvailable())
        {
            _neutralYaw = CurrentYaw;
            _holdTimer = 0f;
            _isHoldActive = false;
            Debug.Log($"MetaQuest: Neutral calibrated to {_neutralYaw:F1}°");
        }
    }

    // ============================================
    // DEBUG VISUALIZATION
    // ============================================

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, 220, 300, 180));
        GUILayout.Label("<b>Meta Quest Input</b>");
        GUILayout.Label($"Mode: {rotationMode}");
        GUILayout.Label($"Headset Available: {IsHeadsetAvailable()}");
        GUILayout.Label($"Current Yaw: {CurrentYaw:F1}°");
        if (rotationMode == RotationMode.Absolute)
        {
            float relativeYaw = Mathf.DeltaAngle(_neutralYaw, CurrentYaw);
            GUILayout.Label($"Relative to Neutral: {relativeYaw:F1}°");
            GUILayout.Label($"Hold Timer: {_holdTimer:F2}s / {holdDuration:F1}s");
            GUILayout.Label($"Hold Active: {_isHoldActive}");
        }
        GUILayout.Label($"Yaw Rate: {HeadsetYawRate:F2}");
        GUILayout.Label($"Is Rotating: {IsRotating()}");
        GUILayout.EndArea();
    }
}