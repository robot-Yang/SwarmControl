using UnityEngine;

/// <summary>
/// Abstract base class for all hand height control modes.
/// Tracks hand Y position relative to eye level (OVRCameraRig's CenterEyeAnchor).
/// Derived classes implement different mapping curves (Linear, RateBased, Exponential, Logarithmic).
/// </summary>
public abstract class HandHeightInputBase : MonoBehaviour
{
    [Header("VR Hand References")]
    [Tooltip("Left hand from OVRCameraRig")]
    public OVRHand leftHand;
    
    [Tooltip("Right hand from OVRCameraRig")]
    public OVRHand rightHand;

    [Header("Reference Point")]
    [Tooltip("Center Eye Anchor from OVRCameraRig - reference for eye level")]
    public Transform centerEyeAnchor;

    [Header("Height Range Settings")]
    [Tooltip("Maximum height offset above neutral (meters)")]
    public float maxHeightAbove = 1.5f;
    
    [Tooltip("Maximum height offset below neutral (meters)")]
    public float maxHeightBelow = 1.5f;

    [Header("Neutral Position")]
    [Tooltip("Vertical offset from eye level for neutral position (meters). Positive = above eyes, Negative = below eyes")]
    public float neutralOffset = -0.3f; // Default: 30cm below eye level

    [Header("Deadzone Settings")]
    [Tooltip("Ignore hand movements within this distance from neutral (meters)")]
    public float deadzone = 0.05f;

    [Header("Calibration")]
    [Tooltip("Press this key to set current hand position as neutral")]
    public KeyCode calibrateKey = KeyCode.H;

    private float _calibrationOffset = 0f;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>
    /// Height control value. Meaning depends on mode:
    /// - RateBased: velocity (-1 to +1)
    /// - Absolute modes: target height offset from neutral
    /// </summary>
    public float HeightControl { get; protected set; }

    /// <summary>
    /// Returns true if this mode outputs absolute target values (not rates)
    /// </summary>
    public abstract bool IsAbsoluteMode { get; }

    /// <summary>
    /// Returns true if hands are tracked and valid
    /// </summary>
    public bool AreHandsAvailable => (leftHand != null && leftHand.IsTracked) || 
                                      (rightHand != null && rightHand.IsTracked);

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        // Handle calibration
        if (Input.GetKeyDown(calibrateKey))
        {
            CalibrateNeutral();
        }

        if (AreHandsAvailable && centerEyeAnchor != null)
        {
            float handHeight = GetAverageHandHeight();
            HeightControl = CalculateHeightControl(handHeight);
        }
        else
        {
            HeightControl = 0f;
        }
    }

    // ============================================
    // HAND TRACKING LOGIC
    // ============================================

    /// <summary>
    /// Gets average Y position of tracked hands in world space
    /// </summary>
    float GetAverageHandHeight()
    {
        float totalHeight = 0f;
        int handCount = 0;

        if (leftHand != null && leftHand.IsTracked)
        {
            totalHeight += leftHand.transform.position.y;
            handCount++;
        }

        if (rightHand != null && rightHand.IsTracked)
        {
            totalHeight += rightHand.transform.position.y;
            handCount++;
        }

        return handCount > 0 ? totalHeight / handCount : 0f;
    }

    /// <summary>
    /// Converts hand height to control value using the specific mapping curve.
    /// </summary>
    float CalculateHeightControl(float handWorldHeight)
    {
        // Calculate neutral position (eye level + offset + calibration)
        float neutralHeight = centerEyeAnchor.position.y + neutralOffset + _calibrationOffset;

        // Distance from neutral
        float delta = handWorldHeight - neutralHeight;

        // Apply deadzone
        if (Mathf.Abs(delta) < deadzone)
        {
            return 0f;
        }

        // Normalize to 0-1 range based on direction
        float normalized;
        if (delta > 0)
        {
            // Hand above neutral
            normalized = Mathf.Clamp01(delta / maxHeightAbove);
        }
        else
        {
            // Hand below neutral
            normalized = Mathf.Clamp01(Mathf.Abs(delta) / maxHeightBelow);
        }

        // Apply the specific mapping curve (implemented by derived class)
        float mapped = ApplyMappingCurve(normalized);

        // Restore sign (positive = up, negative = down)
        return delta > 0 ? mapped : -mapped;
    }

    /// <summary>
    /// Maps normalized input (0-1) to output using the specific curve.
    /// Must be implemented by derived classes.
    /// </summary>
    protected abstract float ApplyMappingCurve(float normalizedInput);

    // ============================================
    // CALIBRATION
    // ============================================

    /// <summary>
    /// Sets current hand position as the new neutral height
    /// </summary>
    public void CalibrateNeutral()
    {
        if (AreHandsAvailable && centerEyeAnchor != null)
        {
            float currentHandHeight = GetAverageHandHeight();
            float currentNeutral = centerEyeAnchor.position.y + neutralOffset;
            _calibrationOffset = currentHandHeight - currentNeutral;
            
            Debug.Log($"Hand Height Calibrated. New neutral offset: {_calibrationOffset:F3}m from default");
        }
        else
        {
            Debug.LogWarning("Cannot calibrate: Hands not tracked or CenterEyeAnchor missing");
        }
    }

    // ============================================
    // DEBUG HELPERS
    // ============================================

    /// <summary>
    /// Returns current hand height relative to neutral position
    /// </summary>
    public float GetHandHeightRelativeToNeutral()
    {
        if (!AreHandsAvailable || centerEyeAnchor == null) return 0f;

        float handHeight = GetAverageHandHeight();
        float neutralHeight = centerEyeAnchor.position.y + neutralOffset + _calibrationOffset;
        return handHeight - neutralHeight;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || centerEyeAnchor == null) return;

        // Draw neutral height plane
        float neutralY = centerEyeAnchor.position.y + neutralOffset + _calibrationOffset;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(new Vector3(centerEyeAnchor.position.x, neutralY, centerEyeAnchor.position.z), 
                            new Vector3(0.3f, 0.01f, 0.3f));

        // Draw max height above
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(new Vector3(centerEyeAnchor.position.x, neutralY + maxHeightAbove, centerEyeAnchor.position.z), 
                            new Vector3(0.25f, 0.01f, 0.25f));

        // Draw max height below
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(new Vector3(centerEyeAnchor.position.x, neutralY - maxHeightBelow, centerEyeAnchor.position.z), 
                            new Vector3(0.25f, 0.01f, 0.25f));
    }
}
