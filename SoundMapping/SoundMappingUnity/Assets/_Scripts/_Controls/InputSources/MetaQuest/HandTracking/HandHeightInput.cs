using UnityEngine;

/// <summary>
/// Rate-based height control via Meta Quest hand tracking.
/// Average Y position of left/right hand relative to a calibrated neutral height
/// produces a velocity rate (-1..+1) for swarm height.
///
/// Sibling of ControllerHeightInput. Reads OVRHand components and gates on
/// OVRHand.IsTracked + (optional) high confidence — naturally exclusive with
/// controller input at the hardware level.
/// </summary>
public class HandHeightInput : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("Left hand from OVRCameraRig (OVRHand component)")]
    public OVRHand leftHand;

    [Tooltip("Right hand from OVRCameraRig (OVRHand component)")]
    public OVRHand rightHand;

    [Tooltip("Center Eye Anchor from OVRCameraRig — eye-level reference for neutral")]
    public Transform centerEyeAnchor;

    [Header("Rate Range (meters from neutral)")]
    [Tooltip("Hand displacement above neutral that produces full +1 rate")]
    public float maxHeightAbove = 0.4f;

    [Tooltip("Hand displacement below neutral that produces full -1 rate")]
    public float maxHeightBelow = 0.4f;

    [Header("Default Neutral")]
    [Tooltip("Vertical offset from eye level for default neutral. Negative = below eyes.")]
    public float neutralOffset = -0.3f;

    [Header("Deadzone")]
    [Tooltip("Ignore movements within this distance from neutral (meters)")]
    public float deadzone = 0.05f;

    [Header("Filtering")]
    [Tooltip("Only use tracking when confidence is high")]
    public bool requireHighConfidence = true;

    [Header("Smoothing")]
    [Tooltip("SmoothDamp time on rate output. 0 = no smoothing.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    [Header("Local Test Calibration")]
    [Tooltip("Optional standalone calibrate key — official flow is via InputFusionManager.PerformCalibration()")]
    public KeyCode calibrateKey = KeyCode.H;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (read by InputFusionManager)
    // ============================================

    /// <summary>Height control as rate (-1..+1). + = up, - = down.</summary>
    public float HeightControl { get; private set; }

    /// <summary>Always false — rate-based.</summary>
    public bool IsAbsoluteMode => false;

    /// <summary>True when at least one hand is tracked (with required confidence) and centerEyeAnchor is assigned.</summary>
    public bool IsAvailable
    {
        get
        {
            if (centerEyeAnchor == null) return false;
            if (leftHand == null && rightHand == null) return false;
            return IsHandTracked(leftHand) || IsHandTracked(rightHand);
        }
    }

    // ============================================
    // PRIVATE STATE
    // ============================================

    private float _calibrationOffset = 0f;
    private float _smoothedRate = 0f;
    private float _smoothVelocity = 0f;

    void Update()
    {
        if (Input.GetKeyDown(calibrateKey)) CalibrateNeutral();

        if (!IsAvailable)
        {
            HeightControl = 0f;
            return;
        }

        float handY = GetAverageHandHeight();
        float neutralY = centerEyeAnchor.position.y + neutralOffset + _calibrationOffset;
        float delta = handY - neutralY;

        float rate;
        if (Mathf.Abs(delta) < deadzone)
        {
            rate = 0f;
        }
        else if (delta > 0f)
        {
            float span = Mathf.Max(maxHeightAbove - deadzone, 0.001f);
            rate = Mathf.Clamp01((delta - deadzone) / span);
        }
        else
        {
            float span = Mathf.Max(maxHeightBelow - deadzone, 0.001f);
            rate = -Mathf.Clamp01((-delta - deadzone) / span);
        }

        if (smoothTime > 0f)
        {
            _smoothedRate = Mathf.SmoothDamp(_smoothedRate, rate, ref _smoothVelocity, smoothTime);
            HeightControl = _smoothedRate;
        }
        else
        {
            HeightControl = rate;
        }
    }

    bool IsHandTracked(OVRHand hand)
    {
        if (hand == null || !hand.IsTracked) return false;
        if (!requireHighConfidence) return true;
        return hand.HandConfidence == OVRHand.TrackingConfidence.High;
    }

    float GetAverageHandHeight()
    {
        float sum = 0f;
        int n = 0;
        if (IsHandTracked(leftHand))  { sum += leftHand.transform.position.y;  n++; }
        if (IsHandTracked(rightHand)) { sum += rightHand.transform.position.y; n++; }
        return n > 0 ? sum / n : 0f;
    }

    /// <summary>Set current hand height as the new neutral.</summary>
    public void CalibrateNeutral()
    {
        if (!IsAvailable) return;
        float currentY = GetAverageHandHeight();
        float defaultNeutral = centerEyeAnchor.position.y + neutralOffset;
        _calibrationOffset = currentY - defaultNeutral;
        _smoothedRate = 0f;
        _smoothVelocity = 0f;
        Debug.Log($"HandHeightInput: calibrated. Offset = {_calibrationOffset:F3}m");
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        GUILayout.BeginArea(new Rect(10, 380, 360, 110));
        GUILayout.Label("<b>=== HAND HEIGHT (rate) ===</b>");
        GUILayout.Label($"Available: {IsAvailable}");
        if (IsAvailable)
        {
            float curY = GetAverageHandHeight();
            float neuY = centerEyeAnchor.position.y + neutralOffset + _calibrationOffset;
            GUILayout.Label($"Δ from neutral: {(curY - neuY):F3}m");
            GUILayout.Label($"Rate output: {HeightControl:F2}");
        }
        GUILayout.EndArea();
    }
}
