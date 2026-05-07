using UnityEngine;

/// <summary>
/// Rate-based height control via Meta Quest Touch controllers.
/// Average Y position of left/right controller anchors maps to a rate (-1..+1) using
/// captured min/neutral/max bounds. Bounds come from the V-key calibration flow.
///
/// Sibling of HandHeightInput. Reads OVRCameraRig anchors and gates on
/// OVRInput.IsControllerConnected so it's mutually exclusive with hand tracking.
/// </summary>
public class ControllerHeightInput : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("OVRCameraRig in the scene — auto-found if left empty")]
    public OVRCameraRig cameraRig;

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

    [Header("Smoothing")]
    [Tooltip("SmoothDamp time on rate output. 0 = no smoothing.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    [Header("Local Test Calibration")]
    [Tooltip("Optional standalone re-center key — official flow is V (multi-step).")]
    public KeyCode calibrateKey = KeyCode.J;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (read by InputFusionManager)
    // ============================================

    /// <summary>Height control as rate (-1..+1). Sign: + = up, - = down.</summary>
    public float HeightControl { get; private set; }

    /// <summary>Always false — controller height input is rate-based.</summary>
    public bool IsAbsoluteMode => false;

    /// <summary>True when at least one Touch controller is connected and anchors are valid.</summary>
    public bool IsAvailable
    {
        get
        {
            if (cameraRig == null
                || cameraRig.centerEyeAnchor == null
                || cameraRig.leftHandAnchor == null
                || cameraRig.rightHandAnchor == null) return false;
            return OVRInput.IsControllerConnected(OVRInput.Controller.LTouch)
                || OVRInput.IsControllerConnected(OVRInput.Controller.RTouch);
        }
    }

    // ============================================
    // PRIVATE STATE
    // ============================================

    private float _smoothedRate = 0f;
    private float _smoothVelocity = 0f;

    void Start()
    {
        if (cameraRig == null) cameraRig = FindObjectOfType<OVRCameraRig>();
        if (cameraRig == null)
            Debug.LogWarning("ControllerHeightInput: OVRCameraRig not found — assign it in the Inspector.");
    }

    void Update()
    {
        if (Input.GetKeyDown(calibrateKey)) CaptureNeutral();

        if (!IsAvailable)
        {
            HeightControl = 0f;
            return;
        }

        float currentY = GetAverageControllerHeight();
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

    /// <summary>
    /// Average world Y of whichever controllers are currently connected.
    /// Public so the V-key calibration flow can read it.
    /// </summary>
    public float GetAverageControllerHeight()
    {
        float sum = 0f;
        int n = 0;
        if (cameraRig != null && OVRInput.IsControllerConnected(OVRInput.Controller.LTouch))
        {
            sum += cameraRig.leftHandAnchor.position.y;
            n++;
        }
        if (cameraRig != null && OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
        {
            sum += cameraRig.rightHandAnchor.position.y;
            n++;
        }
        return n > 0 ? sum / n : 0f;
    }

    // ============================================
    // CALIBRATION HOOKS — used by MetaQuestCalibrationFlow
    // ============================================

    public void CaptureMin()     { if (IsAvailable) minHeight     = GetAverageControllerHeight(); }
    public void CaptureNeutral() { if (IsAvailable) { neutralHeight = GetAverageControllerHeight(); _smoothedRate = 0f; _smoothVelocity = 0f; } }
    public void CaptureMax()     { if (IsAvailable) maxHeight     = GetAverageControllerHeight(); }

    // ============================================
    // BACKWARDS-COMPAT
    // ============================================

    /// <summary>Legacy single-pose neutral capture preserved so existing button flows still work.</summary>
    public void CalibrateNeutral() => CaptureNeutral();

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        GUILayout.BeginArea(new Rect(10, 600, 360, 130));
        GUILayout.Label("<b>=== CONTROLLER HEIGHT (rate) ===</b>");
        GUILayout.Label($"Available: {IsAvailable}");
        if (IsAvailable)
        {
            float curY = GetAverageControllerHeight();
            GUILayout.Label($"Y: {curY:F2}m  bounds [{minHeight:F2}, {neutralHeight:F2}, {maxHeight:F2}]");
            GUILayout.Label($"Rate output: {HeightControl:F2}  curve: {responseCurve}");
        }
        GUILayout.EndArea();
    }
}
