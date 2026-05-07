using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Pose-tracking input for swarm spread control, in either rate or absolute mode.
/// Reads pre-normalized hand distance (0..1) from WebSocketClient — the Python tracker
/// already mapped min/max to 0..1 using its per-user calibration profile.
///
/// • Absolute mode → target swarm separation (meters), lerp(minSwarmSep, maxSwarmSep, t).
/// • Rate mode     → -1..+1 around an implicit neutral at 0.5.
/// </summary>
public class PoseSpreadInput : MonoBehaviour
{
    [Header("WebSocket Connection")]
    [Tooltip("Reference to WebSocketClient that receives pose-tracking data")]
    public WebSocketClient webSocketClient;

    [Header("Output Mode")]
    [Tooltip("Rate (-1..+1 around 0.5 neutral) or Absolute (target meters).")]
    public SpreadMode mode = SpreadMode.Absolute;

    [Header("Absolute Mode — Target Range (meters between drones)")]
    [Tooltip("Swarm separation when raw distance == 0 (hands fully closed).")]
    public float minSwarmSeparation = 1.0f;

    [Tooltip("Swarm separation when raw distance == 1 (hands fully spread).")]
    public float maxSwarmSeparation = 10.0f;

    [Header("Response")]
    [Tooltip("Linear (pass-through) or Exponential (sign(x) * |x|^exponent on rate; pow(x, exp) on absolute).")]
    public ResponseCurve curveType = ResponseCurve.Exponential;

    [Tooltip("Exponent used when curveType == Exponential.")]
    [FormerlySerializedAs("responseCurve")]
    [Range(1f, 3f)]
    public float curveExponent = 2.0f;

    [Header("Deadzone")]
    [Tooltip("Absolute mode: ignore raw distance below this. Rate mode: half-width around 0.5 neutral.")]
    [FormerlySerializedAs("spreadDeadzone")]
    [Range(0f, 0.3f)]
    public float deadzone = 0.05f;

    [Header("Smoothing")]
    [Tooltip("Smoothing factor (0 = no smoothing, 1 = max smoothing)")]
    [Range(0f, 0.95f)]
    public float smoothing = 0.3f;

    private float _smoothed = 0f;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>
    /// Spread output. Meaning depends on mode:
    ///   • Rate     → -1..+1 rate
    ///   • Absolute → target swarm separation (meters)
    /// </summary>
    public float SpreadControl { get; private set; }

    /// <summary>True if the pose tracker is connected and providing data.</summary>
    public bool IsAvailable => webSocketClient != null && webSocketClient.IsConnected;

    /// <summary>True when the source is currently in Absolute mode.</summary>
    public bool IsAbsoluteMode => mode == SpreadMode.Absolute;

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        if (!IsAvailable)
        {
            SpreadControl = 0f;
            return;
        }

        float raw01 = webSocketClient.HandDistance;
        float target;

        if (mode == SpreadMode.Absolute)
        {
            // Below activation threshold: stay at min separation.
            float t = raw01 < deadzone ? 0f : raw01;
            // ApplyCurve expects signed input — for an unsigned 0..1 ramp, just power it directly.
            float curved = curveType == ResponseCurve.Linear ? t : Mathf.Pow(t, curveExponent);
            _smoothed = Mathf.Lerp(_smoothed, curved, 1f - smoothing);
            target = Mathf.Lerp(minSwarmSeparation, maxSwarmSeparation, _smoothed);
        }
        else
        {
            // Rate mode: 0.5 is neutral, deadzone half-width on each side. Map to -1..+1.
            float rawRate = InputCurves.ToRate(raw01, 0f, 0.5f, 1f, deadzone);
            float curved = InputCurves.ApplyCurve(rawRate, curveType, curveExponent);
            _smoothed = Mathf.Lerp(_smoothed, curved, 1f - smoothing);
            target = _smoothed;
        }

        SpreadControl = target;
    }

    // ============================================
    // DEBUG HELPERS
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(900, 10, 320, 100));
        GUILayout.Label($"<b>Pose Spread Input</b>  Connected: {IsAvailable}  mode: {mode}");
        if (IsAvailable)
        {
            GUILayout.Label($"Raw 0..1: {webSocketClient.HandDistance:F2}  curve: {curveType}");
            string label = mode == SpreadMode.Absolute
                ? $"Target: {SpreadControl:F2}m"
                : $"Rate: {SpreadControl:F2}";
            GUILayout.Label(label);
        }
        GUILayout.EndArea();
    }
}
