using UnityEngine;

/// <summary>
/// MediaPipe hand tracking input for swarm spread control.
/// Reads hand distance from WebSocketClient (Python MediaPipe script).
/// </summary>
public class MediaPipeSpreadInput : MonoBehaviour
{
    [Header("WebSocket Connection")]
    [Tooltip("Reference to WebSocketClient that receives MediaPipe data")]
    public WebSocketClient webSocketClient;

    [Header("Spread Mapping Settings")]
    [Tooltip("Minimum swarm separation (meters) at distance=0")]
    public float minSwarmSeparation = 1.0f;
    
    [Tooltip("Maximum swarm separation (meters) at distance=1")]
    public float maxSwarmSeparation = 10.0f;

    [Header("Response Curve")]
    [Tooltip("Response curve exponent (1 = linear, 2 = squared). Higher = more precise at small spreads")]
    [Range(1f, 3f)]
    public float responseCurve = 2.0f;

    [Header("Deadzone")]
    [Tooltip("Ignore spread values below this threshold (0 = no deadzone)")]
    [Range(0f, 0.3f)]
    public float spreadDeadzone = 0.05f;

    [Header("Smoothing")]
    [Tooltip("Smoothing factor (0 = no smoothing, 1 = max smoothing)")]
    [Range(0f, 0.95f)]
    public float smoothing = 0.3f;

    private float _smoothedDistance = 0f;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>
    /// Current swarm spread target (absolute value in meters)
    /// </summary>
    public float SpreadControl { get; private set; }

    /// <summary>
    /// Returns true if MediaPipe is connected and providing data
    /// </summary>
    public bool IsAvailable => webSocketClient != null && webSocketClient.IsConnected;

    /// <summary>
    /// Always returns true - MediaPipe provides absolute target values
    /// </summary>
    public bool IsAbsoluteMode => true;

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        if (IsAvailable)
        {
            // Get raw distance from WebSocket (0..1)
            float rawDistance = webSocketClient.HandDistance;

            // Apply deadzone (ignore very small spread values)
            if (rawDistance < spreadDeadzone)
            {
                rawDistance = 0f;
            }

            // Apply response curve for better control (more precision at small spreads)
            float curved = Mathf.Pow(rawDistance, responseCurve);

            // Apply smoothing
            _smoothedDistance = Mathf.Lerp(_smoothedDistance, curved, 1f - smoothing);

            // Map to swarm separation range
            SpreadControl = Mathf.Lerp(minSwarmSeparation, maxSwarmSeparation, _smoothedDistance);
        }
        else
        {
            SpreadControl = 0f;
        }
    }

    // ============================================
    // DEBUG HELPERS
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(900, 10, 300, 100));
        GUILayout.Label($"<b>MediaPipe Spread Input</b> Connected: {IsAvailable}");
        if (IsAvailable)
        {
            GUILayout.Label($"Raw Distance: {webSocketClient.HandDistance:F2}");
            GUILayout.Label($"Spread Target: {SpreadControl:F2}m");
        }
        GUILayout.EndArea();
    }
}