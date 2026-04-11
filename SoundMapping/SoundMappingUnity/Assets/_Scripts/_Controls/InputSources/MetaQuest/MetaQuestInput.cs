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

    [Header("Rotation Settings")]
    [Tooltip("Multiplier for head rotation sensitivity (1.0 = normal, 2.0 = twice as fast)")]
    [Range(0.1f, 5.0f)]
    public float rotationSensitivity = 1.0f;

    [Tooltip("Response curve exponent (1 = linear, 2 = squared). Higher = more precise at small head turns")]
    [Range(1f, 3f)]
    public float responseCurve = 2.0f;

    [Tooltip("Ignore yaw angles smaller than this (degrees) - same as IMU deadzone")]
    [Range(0f, 30f)]
    public float yawDeadzone = 5f;

    [Tooltip("Maximum yaw angle (degrees) that produces maximum rotation rate - same as IMU max angle")]
    [Range(10f, 90f)]
    public float yawMaxAngle = 30f;

    [Header("Calibration")]
    [Tooltip("Press this key to calibrate neutral yaw position (sets current direction as forward)")]
    public KeyCode calibrateKey = KeyCode.C;

    [Header("Smoothing")]
    [Tooltip("Time to reach target rotation (lower = faster response, higher = smoother). 0 = no smoothing.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

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
    private float _smoothedYawRate = 0f; // Smoothed output value
    private float _yawVelocity = 0f; // Velocity for SmoothDamp

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

    void LateUpdate()
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
        CalculateYawRate();
    }

    /// <summary>
    /// Check if headset tracking is available
    /// </summary>
    public bool IsHeadsetAvailable()
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

        // Apply response curve for better control feel
        float curved = Mathf.Pow(yawNormalized, responseCurve);

        // Apply sensitivity multiplier
        float yawMapped = curved * rotationSensitivity;

        // Restore sign (positive = right, negative = left)
        HeadsetYawRate = Mathf.Sign(yaw) * yawMapped;

        // Clamp final output to -1 to +1
        HeadsetYawRate = Mathf.Clamp(HeadsetYawRate, -1f, 1f);

        // Apply smoothing
        if (smoothTime > 0f)
        {
            _smoothedYawRate = Mathf.SmoothDamp(_smoothedYawRate, HeadsetYawRate, ref _yawVelocity, smoothTime);
            HeadsetYawRate = _smoothedYawRate;
        }

        // Store for next frame
        _previousYaw = CurrentYaw;

        // Debug output (show all the time when debug is on)
        if (showDebugInfo)
        {
            Debug.Log($"[MetaQuest RateBased] CurrentYaw: {CurrentYaw:F1}° | Neutral: {_neutralYaw:F1}° | Relative: {relativeYaw:F1}° | Normalized: {yawNormalized:F2} | Output: {HeadsetYawRate:F2}");
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
        _smoothedYawRate = 0f;
        _yawVelocity = 0f;
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
            _smoothedYawRate = 0f;
            _yawVelocity = 0f;
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
        GUILayout.Label($"OVRCameraRig Assigned: {cameraRig != null}");
        GUILayout.Label($"Headset Available: {IsHeadsetAvailable()}");
        
        if (IsHeadsetAvailable())
        {
            GUILayout.Label($"Current Yaw: {CurrentYaw:F1}°");
            float relativeYaw = Mathf.DeltaAngle(_neutralYaw, CurrentYaw);
            GUILayout.Label($"Neutral Yaw: {_neutralYaw:F1}°");
            GUILayout.Label($"Relative to Neutral: {relativeYaw:F1}°");
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