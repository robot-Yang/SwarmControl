using UnityEngine;

/// <summary>
/// Rate-based camera yaw control via Meta Quest headset rotation.
/// Headset yaw deviation from a calibrated neutral produces a velocity rate
/// (-1..+1). Joystick metaphor: head turned right of neutral = camera turning
/// right at proportional speed; head returned to neutral = stop turning.
///
/// Sibling of HandHeightInput / HandSpreadInput / ControllerHeightInput /
/// ControllerSpreadInput — same rate-based joystick mapping pattern, sourced
/// from the headset rather than hands.
/// </summary>
public class HeadsetYawInput : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("OVRCameraRig in the scene — auto-found if left empty")]
    public OVRCameraRig cameraRig;

    [Header("Rate Range (degrees from neutral)")]
    [Tooltip("Yaw deviation that produces full ±1 rate")]
    [Range(10f, 90f)]
    public float yawMaxAngle = 30f;

    [Header("Deadzone")]
    [Tooltip("Ignore yaw deviations smaller than this (degrees)")]
    [Range(0f, 30f)]
    public float deadzone = 5f;

    [Header("Response")]
    [Tooltip("Curve exponent (1 = linear, 2 = squared). Higher = more precise at small head turns")]
    [Range(1f, 3f)]
    public float responseCurve = 2f;

    [Tooltip("Output multiplier (1 = normal). Tune to match camera yaw speed.")]
    [Range(0.1f, 5f)]
    public float sensitivity = 1f;

    [Header("Smoothing")]
    [Tooltip("SmoothDamp time on rate output. 0 = no smoothing.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    [Header("Auto-Anchor When Settled")]
    [Tooltip("Slowly bias the neutral toward current yaw when the head is still — corrects unintentional body drift without manual recalibration.")]
    public bool autoAnchorWhenSettled = true;

    [Tooltip("How long the head must be continuously still before auto-anchor kicks in (seconds)")]
    [Range(0.5f, 5f)]
    public float anchorSettleTime = 1.5f;

    [Tooltip("Head considered still below this angular velocity (deg/s)")]
    [Range(1f, 30f)]
    public float anchorAngVelThreshold = 5f;

    [Tooltip("Don't auto-correct deviations smaller than this — nothing meaningful to fix (deg)")]
    [Range(0f, 5f)]
    public float anchorMinDeviation = 1f;

    [Tooltip("Don't auto-correct deviations larger than this — assumed an intentional sustained turn (deg)")]
    [Range(2f, 30f)]
    public float anchorMaxDeviation = 8f;

    [Tooltip("How fast the neutral migrates toward current yaw when conditions are met (1/s)")]
    [Range(0.05f, 2f)]
    public float anchorLerpSpeed = 0.5f;

    [Header("Local Test Calibration")]
    [Tooltip("Optional standalone calibrate key — official flow is via InputFusionManager.PerformCalibration()")]
    public KeyCode calibrateKey = KeyCode.C;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (read by InputFusionManager)
    // ============================================

    /// <summary>Camera rotation rate (-1..+1). + = right, - = left.</summary>
    public float RotationControl { get; private set; }

    /// <summary>Always false — rate-based.</summary>
    public bool IsAbsoluteMode => false;

    /// <summary>True when OVRCameraRig and centerEyeAnchor are valid.</summary>
    public bool IsAvailable => cameraRig != null && cameraRig.centerEyeAnchor != null;

    /// <summary>Current absolute yaw of the headset (degrees, world-space).</summary>
    public float CurrentYaw { get; private set; }

    /// <summary>Calibrated neutral yaw (degrees, world-space).</summary>
    public float NeutralYaw => _neutralYaw;

    // ============================================
    // PRIVATE STATE
    // ============================================

    private float _neutralYaw = 0f;
    private bool _initialized = false;
    private float _smoothedRate = 0f;
    private float _smoothVelocity = 0f;
    private float _prevYaw = 0f;
    private float _settledTime = 0f;

    void Start()
    {
        if (cameraRig == null) cameraRig = FindObjectOfType<OVRCameraRig>();
        if (cameraRig == null)
            Debug.LogWarning("HeadsetYawInput: OVRCameraRig not found — assign it in the Inspector.");
    }

    void LateUpdate()
    {
        if (Input.GetKeyDown(calibrateKey)) CalibrateNeutral();

        if (!IsAvailable)
        {
            RotationControl = 0f;
            return;
        }

        CurrentYaw = cameraRig.centerEyeAnchor.eulerAngles.y;

        if (!_initialized)
        {
            _neutralYaw = CurrentYaw;
            _prevYaw = CurrentYaw;
            _initialized = true;
        }

        float relativeYaw = Mathf.DeltaAngle(_neutralYaw, CurrentYaw);

        float rate;
        if (Mathf.Abs(relativeYaw) < deadzone)
        {
            rate = 0f;
        }
        else
        {
            float span = Mathf.Max(yawMaxAngle - deadzone, 0.001f);
            float normalized = Mathf.Clamp01((Mathf.Abs(relativeYaw) - deadzone) / span);
            float curved = Mathf.Pow(normalized, responseCurve);
            rate = Mathf.Sign(relativeYaw) * curved * sensitivity;
            rate = Mathf.Clamp(rate, -1f, 1f);
        }

        if (smoothTime > 0f)
        {
            _smoothedRate = Mathf.SmoothDamp(_smoothedRate, rate, ref _smoothVelocity, smoothTime);
            RotationControl = _smoothedRate;
        }
        else
        {
            RotationControl = rate;
        }

        if (autoAnchorWhenSettled) UpdateAutoAnchor(relativeYaw);
        _prevYaw = CurrentYaw;
    }

    /// <summary>
    /// When the head has been still for a while with a small persistent deviation,
    /// gently bias the neutral toward current yaw. Corrects unintentional body drift
    /// (chair micro-adjustments, weight shifts) without cancelling intentional turns —
    /// large sustained deviations are assumed intentional and left alone.
    /// </summary>
    void UpdateAutoAnchor(float relativeYaw)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-6f);
        float angularVelocity = Mathf.Abs(Mathf.DeltaAngle(_prevYaw, CurrentYaw)) / dt;

        if (angularVelocity < anchorAngVelThreshold)
            _settledTime += Time.deltaTime;
        else
            _settledTime = 0f;

        float absDev = Mathf.Abs(relativeYaw);
        bool eligibleDeviation = absDev >= anchorMinDeviation && absDev <= anchorMaxDeviation;

        if (_settledTime >= anchorSettleTime && eligibleDeviation)
        {
            _neutralYaw = Mathf.LerpAngle(_neutralYaw, CurrentYaw, anchorLerpSpeed * Time.deltaTime);
        }
    }

    /// <summary>Set current head yaw as the new neutral.</summary>
    public void CalibrateNeutral()
    {
        if (!IsAvailable) return;
        _neutralYaw = cameraRig.centerEyeAnchor.eulerAngles.y;
        _prevYaw = _neutralYaw;
        _settledTime = 0f;
        _smoothedRate = 0f;
        _smoothVelocity = 0f;
        Debug.Log($"HeadsetYawInput: calibrated. Neutral yaw = {_neutralYaw:F1}°");
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        GUILayout.BeginArea(new Rect(10, 220, 360, 110));
        GUILayout.Label("<b>=== HEADSET YAW (rate) ===</b>");
        GUILayout.Label($"Available: {IsAvailable}");
        if (IsAvailable)
        {
            float rel = Mathf.DeltaAngle(_neutralYaw, CurrentYaw);
            GUILayout.Label($"Yaw: {CurrentYaw:F1}° (Δ {rel:F1}° from neutral {_neutralYaw:F1}°)");
            GUILayout.Label($"Rate output: {RotationControl:F2}");
            if (autoAnchorWhenSettled)
                GUILayout.Label($"Settled: {_settledTime:F1}s / {anchorSettleTime:F1}s");
        }
        GUILayout.EndArea();
    }
}
