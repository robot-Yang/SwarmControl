using UnityEngine;

/// <summary>
/// Rate-based height control via Meta Quest hand tracking.
/// Average Y position of left/right hand maps to a rate (-1..+1) using captured
/// min/neutral/max bounds. Bounds come from the V-key calibration flow.
///
/// Sibling of ControllerHeightInput. Reads OVRHand components and gates on
/// OVRHand.IsTracked + (optional) high confidence.
/// </summary>
public class HandHeightInput : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("Left hand from OVRCameraRig (OVRHand component)")]
    public OVRHand leftHand;

    [Tooltip("Right hand from OVRCameraRig (OVRHand component)")]
    public OVRHand rightHand;

    [Tooltip("Center Eye Anchor from OVRCameraRig — only used by debug overlay")]
    public Transform centerEyeAnchor;

    [Header("Calibrated Bounds (meters, world Y)")]
    [Tooltip("World Y at the lowest comfortable hand position. Set by calibration.")]
    public float minHeight = 0.8f;

    [Tooltip("World Y at the comfortable neutral. Set by calibration.")]
    public float neutralHeight = 1.2f;

    [Tooltip("World Y at the highest comfortable hand position. Set by calibration.")]
    public float maxHeight = 1.8f;

    [Header("Response")]
    [Tooltip("Linear or exponential mapping from delta-from-neutral to rate.")]
    public ResponseCurve responseCurve = ResponseCurve.Linear;

    [Tooltip("Exponent used when responseCurve == Exponential. 2 = squared, 3 = cubed.")]
    [Range(1f, 3f)]
    public float curveExponent = 2.0f;

    [Header("Deadzone")]
    [Tooltip("Half-width of the no-change band around neutral (meters)")]
    [Range(0f, 0.2f)]
    public float deadzone = 0.05f;

    [Header("Filtering")]
    [Tooltip("Only use tracking when confidence is high")]
    public bool requireHighConfidence = true;

    [Header("Smoothing")]
    [Tooltip("SmoothDamp time on rate output. 0 = no smoothing.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    [Header("Local Test Calibration")]
    [Tooltip("Optional standalone re-center key — official flow is V (multi-step).")]
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

    /// <summary>True when at least one hand is tracked (with required confidence).</summary>
    public bool IsAvailable
    {
        get
        {
            if (leftHand == null && rightHand == null) return false;
            return IsHandTracked(leftHand) || IsHandTracked(rightHand);
        }
    }

    // ============================================
    // PRIVATE STATE
    // ============================================

    private float _smoothedRate = 0f;
    private float _smoothVelocity = 0f;

    void Update()
    {
        if (Input.GetKeyDown(calibrateKey)) CaptureNeutral();

        if (!IsAvailable)
        {
            HeightControl = 0f;
            return;
        }

        float currentY = GetAverageHandHeight();
        float rawRate = InputCurves.ToRate(currentY, minHeight, neutralHeight, maxHeight, deadzone);
        float curvedRate = InputCurves.ApplyCurve(rawRate, responseCurve, curveExponent);

        if (smoothTime > 0f)
        {
            _smoothedRate = Mathf.SmoothDamp(_smoothedRate, curvedRate, ref _smoothVelocity, smoothTime);
            HeightControl = _smoothedRate;
        }
        else
        {
            HeightControl = curvedRate;
        }
    }

    bool IsHandTracked(OVRHand hand)
    {
        if (hand == null || !hand.IsTracked) return false;
        if (!requireHighConfidence) return true;
        return hand.HandConfidence == OVRHand.TrackingConfidence.High;
    }

    /// <summary>
    /// Average world Y of whichever hands are currently tracked.
    /// Public so the V-key calibration flow can read it.
    /// </summary>
    public float GetAverageHandHeight()
    {
        float sum = 0f;
        int n = 0;
        if (IsHandTracked(leftHand))  { sum += leftHand.transform.position.y;  n++; }
        if (IsHandTracked(rightHand)) { sum += rightHand.transform.position.y; n++; }
        return n > 0 ? sum / n : 0f;
    }

    // ============================================
    // CALIBRATION HOOKS — used by MetaQuestCalibrationFlow
    // ============================================

    public void CaptureMin()     { if (IsAvailable) minHeight     = GetAverageHandHeight(); }
    public void CaptureNeutral() { if (IsAvailable) { neutralHeight = GetAverageHandHeight(); _smoothedRate = 0f; _smoothVelocity = 0f; } }
    public void CaptureMax()     { if (IsAvailable) maxHeight     = GetAverageHandHeight(); }

    /// <summary>Legacy single-pose neutral capture preserved so existing button flows still work.</summary>
    public void CalibrateNeutral() => CaptureNeutral();

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        GUILayout.BeginArea(new Rect(10, 380, 360, 130));
        GUILayout.Label("<b>=== HAND HEIGHT (rate) ===</b>");
        GUILayout.Label($"Available: {IsAvailable}");
        if (IsAvailable)
        {
            float curY = GetAverageHandHeight();
            GUILayout.Label($"Y: {curY:F2}m  bounds [{minHeight:F2}, {neutralHeight:F2}, {maxHeight:F2}]");
            GUILayout.Label($"Rate output: {HeightControl:F2}  curve: {responseCurve}");
        }
        GUILayout.EndArea();
    }
}
