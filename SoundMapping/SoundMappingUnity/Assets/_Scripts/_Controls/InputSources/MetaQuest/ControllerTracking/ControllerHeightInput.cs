using UnityEngine;

/// <summary>
/// Rate-based height control via Meta Quest Touch controllers.
/// Average Y position of left/right controller anchors relative to a calibrated
/// neutral height produces a velocity rate (-1 to +1) for swarm height.
///
/// Sibling of HandHeightInput. Reads OVRCameraRig anchors (which follow the
/// controller pose when controllers are connected) and gates availability on
/// OVRInput.IsControllerConnected so it's mutually exclusive with hand tracking
/// at the hardware level.
/// </summary>
public class ControllerHeightInput : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("OVRCameraRig in the scene — auto-found if left empty")]
    public OVRCameraRig cameraRig;

    [Header("Rate Range (meters from neutral)")]
    [Tooltip("Hand displacement above neutral that produces full +1 rate")]
    public float maxHeightAbove = 0.4f;

    [Tooltip("Hand displacement below neutral that produces full -1 rate")]
    public float maxHeightBelow = 0.4f;

    [Header("Default Neutral")]
    [Tooltip("Vertical offset from eye level for default neutral. Negative = below eyes.")]
    public float neutralOffset = -0.3f;

    [Header("Deadzone")]
    [Tooltip("Ignore controller movements within this distance from neutral (meters)")]
    public float deadzone = 0.05f;

    [Header("Smoothing")]
    [Tooltip("SmoothDamp time on rate output. 0 = no smoothing.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    [Header("Local Test Calibration")]
    [Tooltip("Optional standalone calibrate key — official flow is via InputFusionManager.PerformCalibration()")]
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

    private float _calibrationOffset = 0f;
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
        if (Input.GetKeyDown(calibrateKey)) CalibrateNeutral();

        if (!IsAvailable)
        {
            HeightControl = 0f;
            return;
        }

        float controllerY = GetAverageControllerHeight();
        float neutralY = cameraRig.centerEyeAnchor.position.y + neutralOffset + _calibrationOffset;
        float delta = controllerY - neutralY;

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

    float GetAverageControllerHeight()
    {
        float sum = 0f;
        int n = 0;
        if (OVRInput.IsControllerConnected(OVRInput.Controller.LTouch))
        {
            sum += cameraRig.leftHandAnchor.position.y;
            n++;
        }
        if (OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
        {
            sum += cameraRig.rightHandAnchor.position.y;
            n++;
        }
        return n > 0 ? sum / n : 0f;
    }

    /// <summary>Set current controller height as the new neutral.</summary>
    public void CalibrateNeutral()
    {
        if (!IsAvailable) return;
        float currentY = GetAverageControllerHeight();
        float defaultNeutral = cameraRig.centerEyeAnchor.position.y + neutralOffset;
        _calibrationOffset = currentY - defaultNeutral;
        _smoothedRate = 0f;
        _smoothVelocity = 0f;
        Debug.Log($"ControllerHeightInput: calibrated. Offset = {_calibrationOffset:F3}m");
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        GUILayout.BeginArea(new Rect(10, 600, 360, 110));
        GUILayout.Label("<b>=== CONTROLLER HEIGHT (rate) ===</b>");
        GUILayout.Label($"Available: {IsAvailable}");
        if (IsAvailable)
        {
            float curY = GetAverageControllerHeight();
            float neuY = cameraRig.centerEyeAnchor.position.y + neutralOffset + _calibrationOffset;
            GUILayout.Label($"Δ from neutral: {(curY - neuY):F3}m");
            GUILayout.Label($"Rate output: {HeightControl:F2}");
        }
        GUILayout.EndArea();
    }
}
