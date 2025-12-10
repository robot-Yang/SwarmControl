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
    [Tooltip("Ignore yaw angles smaller than this (degrees) - same as IMU deadzone")]
    [Range(0f, 30f)]
    public float yawDeadzone = 5f;

    [Tooltip("Maximum yaw angle (degrees) that produces maximum rotation rate - same as IMU max angle")]
    [Range(10f, 90f)]
    public float yawMaxAngle = 30f;

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

    [Header("Calibration")]
    [Tooltip("Press this key to calibrate neutral yaw position (sets current direction as forward)")]
    public KeyCode calibrateKey = KeyCode.C;

    [Header("Debug")]
    public bool showDebugInfo = true;

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
            if (showDebugInfo && Time.frameCount % 120 == 0) // Log every 2 seconds
            {
                Debug.LogWarning("MetaQuestInput: Headset not available! Check OVRCameraRig assignment.");
            }
            HeadsetYawRate = 0f;
            return;
        }

        // Handle calibration input (matching IMU behavior)
        if (Input.GetKeyDown(calibrateKey))
        {
            CalibrateNeutral();
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

        // Initialize previous yaw and neutral on first frame
        if (!_initialized)
        {
            _previousYaw = CurrentYaw;
            _neutralYaw = CurrentYaw; // Auto-calibrate neutral on start (matching IMU behavior)
            _initialized = true;
            Debug.Log($"MetaQuest: Auto-calibrated neutral yaw to {_neutralYaw:F1}°");
        }
    }

    /// <summary>
    /// Calculate rotation based on yaw angle from neutral position.
    /// RATE-BASED MODE: Uses EXACT same logic as IMU pitch/roll control.
    /// Pipeline: Normalize angle → Deadzone → Normalize to 0-1 → Apply sensitivity → Restore direction
    /// </summary>
    void CalculateYawRate()
    {
        // Calculate angle relative to neutral reference point (matching IMU calibration)
        float relativeYaw = Mathf.DeltaAngle(_neutralYaw, CurrentYaw);
        
        // Normalize to -180 to +180 range (already done by DeltaAngle)
        float yaw = relativeYaw;

        // Apply deadzone (ignore angles smaller than threshold)
        if (Mathf.Abs(yaw) < yawDeadzone)
        {
            yaw = 0f;
        }

        // Normalize to 0-1 range for mapping (matching IMU logic)
        float yawNormalized = Mathf.Clamp01(Mathf.Abs(yaw) / yawMaxAngle);

        // Apply sensitivity as curve multiplier (matching IMU)
        float yawMapped = yawNormalized * rotationSensitivity;

        // Restore sign (positive = right, negative = left)
        HeadsetYawRate = Mathf.Sign(yaw) * yawMapped;

        // Clamp final output to -1 to +1
        HeadsetYawRate = Mathf.Clamp(HeadsetYawRate, -1f, 1f);

        // Store for next frame
        _previousYaw = CurrentYaw;

        // Debug output (show all the time when debug is on)
        if (showDebugInfo)
        {
            Debug.Log($"[MetaQuest RateBased] CurrentYaw: {CurrentYaw:F1}° | Neutral: {_neutralYaw:F1}° | Relative: {relativeYaw:F1}° | Normalized: {yawNormalized:F2} | Output: {HeadsetYawRate:F2}");
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

        GUILayout.BeginArea(new Rect(10, 220, 400, 220));
        GUILayout.Label("<b>=== META QUEST INPUT ===</b>");
        GUILayout.Label($"Mode: {rotationMode}");
        GUILayout.Label($"OVRCameraRig Assigned: {cameraRig != null}");
        GUILayout.Label($"Headset Available: {IsHeadsetAvailable()}");
        
        if (IsHeadsetAvailable())
        {
            GUILayout.Label($"Current Yaw: {CurrentYaw:F1}°");
            if (rotationMode == RotationMode.Absolute)
            {
                float relativeYaw = Mathf.DeltaAngle(_neutralYaw, CurrentYaw);
                GUILayout.Label($"Neutral Yaw: {_neutralYaw:F1}°");
                GUILayout.Label($"Relative to Neutral: {relativeYaw:F1}°");
                GUILayout.Label($"Hold Timer: {_holdTimer:F2}s / {holdDuration:F1}s");
                GUILayout.Label($"Hold Active: {_isHoldActive}");
            }
            GUILayout.Label($"<color=cyan>Yaw Rate OUTPUT: {HeadsetYawRate:F2}</color>");
            GUILayout.Label($"Is Rotating: {IsRotating()}");
        }
        else
        {
            GUILayout.Label("<color=red>HEADSET NOT DETECTED!</color>");
            GUILayout.Label("Check: OVRCameraRig in scene?");
        }
        GUILayout.EndArea();
    }
}