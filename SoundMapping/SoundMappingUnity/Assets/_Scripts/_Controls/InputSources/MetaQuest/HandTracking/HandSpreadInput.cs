using UnityEngine;

/// <summary>
/// Rate-based spread control via Meta Quest hand tracking.
/// Distance between left/right hands vs a calibrated neutral distance produces
/// a rate (-1..+1) for swarm spread.
/// Hands closer than neutral = contract (negative). Hands farther = expand (positive).
///
/// Sibling of ControllerSpreadInput. Reads OVRHand components and gates on both
/// hands tracked with (optional) high confidence — naturally exclusive with
/// controller input at the hardware level.
/// </summary>
public class HandSpreadInput : MonoBehaviour
{
    [Header("OVR References")]
    [Tooltip("Left hand from OVRCameraRig (OVRHand component)")]
    public OVRHand leftHand;

    [Tooltip("Right hand from OVRCameraRig (OVRHand component)")]
    public OVRHand rightHand;

    [Header("Distance Range (meters)")]
    [Tooltip("Neutral hand distance — overwritten by CalibrateNeutral()")]
    public float neutralDistance = 0.35f;

    [Tooltip("Distance for full -1 (contracting) rate")]
    [Range(0.05f, 0.4f)]
    public float minDistance = 0.1f;

    [Tooltip("Distance for full +1 (expanding) rate")]
    [Range(0.4f, 1.5f)]
    public float maxDistance = 0.8f;

    [Header("Deadzone")]
    [Tooltip("Half-width of the no-change band around neutral (meters)")]
    [Range(0f, 0.2f)]
    public float deadzone = 0.1f;

    [Header("Filtering")]
    [Tooltip("Only use tracking when both hands are at high confidence")]
    public bool requireHighConfidence = true;

    [Header("Smoothing")]
    [Tooltip("SmoothDamp time on rate output. 0 = no smoothing.")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.1f;

    [Header("Local Test Calibration")]
    [Tooltip("Optional standalone calibrate key — official flow is via InputFusionManager.PerformCalibration()")]
    public KeyCode calibrateKey = KeyCode.G;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (read by InputFusionManager)
    // ============================================

    /// <summary>Spread control as rate (-1..+1). + = expand, - = contract.</summary>
    public float SpreadControl { get; private set; }

    /// <summary>Always false — rate-based.</summary>
    public bool IsAbsoluteMode => false;

    /// <summary>Current measured distance between the two hand transforms (meters).</summary>
    public float CurrentDistance { get; private set; }

    /// <summary>True when both hands are tracked with required confidence.</summary>
    public bool IsAvailable
    {
        get
        {
            if (leftHand == null || rightHand == null) return false;
            return IsHandTracked(leftHand) && IsHandTracked(rightHand);
        }
    }

    // ============================================
    // PRIVATE STATE
    // ============================================

    private float _smoothedRate = 0f;
    private float _smoothVelocity = 0f;

    void Update()
    {
        if (Input.GetKeyDown(calibrateKey)) CalibrateNeutral();

        if (!IsAvailable)
        {
            SpreadControl = 0f;
            return;
        }

        CurrentDistance = Vector3.Distance(leftHand.transform.position, rightHand.transform.position);

        float deadMin = neutralDistance - deadzone;
        float deadMax = neutralDistance + deadzone;
        float rate;

        if (CurrentDistance >= deadMin && CurrentDistance <= deadMax)
        {
            rate = 0f;
        }
        else if (CurrentDistance < deadMin)
        {
            float span = Mathf.Max(deadMin - minDistance, 0.001f);
            rate = Mathf.Clamp((CurrentDistance - deadMin) / span, -1f, 0f);
        }
        else
        {
            float span = Mathf.Max(maxDistance - deadMax, 0.001f);
            rate = Mathf.Clamp((CurrentDistance - deadMax) / span, 0f, 1f);
        }

        if (smoothTime > 0f)
        {
            _smoothedRate = Mathf.SmoothDamp(_smoothedRate, rate, ref _smoothVelocity, smoothTime);
            SpreadControl = _smoothedRate;
        }
        else
        {
            SpreadControl = rate;
        }
    }

    bool IsHandTracked(OVRHand hand)
    {
        if (hand == null || !hand.IsTracked) return false;
        if (!requireHighConfidence) return true;
        return hand.HandConfidence == OVRHand.TrackingConfidence.High;
    }

    /// <summary>Set current inter-hand distance as the new neutral.</summary>
    public void CalibrateNeutral()
    {
        if (!IsAvailable) return;
        neutralDistance = Vector3.Distance(leftHand.transform.position, rightHand.transform.position);
        _smoothedRate = 0f;
        _smoothVelocity = 0f;
        Debug.Log($"HandSpreadInput: calibrated. Neutral = {neutralDistance:F3}m");
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        GUILayout.BeginArea(new Rect(10, 500, 360, 110));
        GUILayout.Label("<b>=== HAND SPREAD (rate) ===</b>");
        GUILayout.Label($"Available: {IsAvailable}");
        if (IsAvailable)
        {
            GUILayout.Label($"Distance: {CurrentDistance:F3}m  (neutral {neutralDistance:F2}, ±{deadzone:F2})");
            GUILayout.Label($"Rate output: {SpreadControl:F2}");
        }
        GUILayout.EndArea();
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo || !Application.isPlaying || !IsAvailable) return;
        Gizmos.color = Mathf.Abs(SpreadControl) > 0.01f ? Color.green : Color.yellow;
        Gizmos.DrawLine(leftHand.transform.position, rightHand.transform.position);
    }
}
