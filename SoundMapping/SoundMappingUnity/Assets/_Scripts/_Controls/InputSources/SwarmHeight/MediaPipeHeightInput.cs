using UnityEngine;

/// <summary>
/// MediaPipe hand tracking input for swarm height control.
/// Reads hand height from WebSocketClient (Python MediaPipe script).
/// </summary>
public class MediaPipeHeightInput : MonoBehaviour
{
    [Header("WebSocket Connection")]
    [Tooltip("Reference to WebSocketClient that receives MediaPipe data")]
    public WebSocketClient webSocketClient;

    [Header("Height Mapping Settings")]
    [Tooltip("Maximum vertical speed (units/second) when hands at extreme positions")]
    public float maxVerticalSpeed = 2.0f;

    [Header("Smoothing")]
    [Tooltip("Smoothing factor (0 = no smoothing, 1 = max smoothing)")]
    [Range(0f, 0.95f)]
    public float smoothing = 0.3f;

    private float _smoothedHeight = 0f;

    // ============================================
    // OUTPUT PROPERTIES
    // ============================================

    /// <summary>
    /// Current height control value (rate-based: -1 to +1)
    /// Positive = up, Negative = down, 0 = neutral
    /// </summary>
    public float HeightControl { get; private set; }

    /// <summary>
    /// Returns true if MediaPipe is connected and providing data
    /// </summary>
    public bool IsAvailable => webSocketClient != null && webSocketClient.IsConnected;

    /// <summary>
    /// Returns false - MediaPipe height is rate-based (like joystick)
    /// </summary>
    public bool IsAbsoluteMode => false;

    // ============================================
    // UPDATE LOOP
    // ============================================

    void Update()
    {
        if (IsAvailable)
        {
            // Get raw height from WebSocket (-1..+1, 0 = neutral)
            float rawHeight = webSocketClient.HandHeight;

            // Apply smoothing
            _smoothedHeight = Mathf.Lerp(_smoothedHeight, rawHeight, 1f - smoothing);

            // Output is already in -1..+1 rate format
            HeightControl = _smoothedHeight;
        }
        else
        {
            HeightControl = 0f;
        }
    }

    // ============================================
    // DEBUG HELPERS
    // ============================================

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(320, 120, 300, 100));
        GUILayout.Label("<b>MediaPipe Height Input</b>");
        GUILayout.Label($"Connected: {IsAvailable}");
        if (IsAvailable)
        {
            GUILayout.Label($"Raw Height: {webSocketClient.HandHeight:F2}");
            GUILayout.Label($"Height Rate: {HeightControl:F2}");
        }
        GUILayout.EndArea();
    }
}
