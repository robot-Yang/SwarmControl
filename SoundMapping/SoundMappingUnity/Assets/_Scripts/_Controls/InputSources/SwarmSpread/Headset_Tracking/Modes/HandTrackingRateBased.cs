using UnityEngine;

/// <summary>
/// Rate-based hand tracking control.
/// Hand position controls the RATE of swarm spread change (like a joystick).
/// Hands at neutral = no change, hands closer/farther = continuous contraction/expansion.
/// </summary>
public class HandTrackingRateBased : HandTrackingInputBase
{
    [Header("Rate-Based Settings")]
    [Tooltip("Hand distance (meters) where spread is neutral (no change)")]
    [Range(0.2f, 0.6f)]
    public float neutralDistance = 0.35f;

    [Tooltip("Deadzone around neutral distance (meters) - no spread change in this range")]
    [Range(0.0f, 0.2f)]
    public float deadzone = 0.1f;

    public override bool IsAbsoluteMode => false;

    protected override void Start()
    {
        base.Start();
        _lastValidDistance = neutralDistance;
    }

    /// <summary>
    /// Rate-based mapping: distance from neutral determines rate of change
    /// Works like a joystick
    /// </summary>
    protected override void CalculateSpreadControl()
    {
        float distance = CurrentHandDistance;

        float deadzoneMin = neutralDistance - deadzone;
        float deadzoneMax = neutralDistance + deadzone;

        if (distance >= deadzoneMin && distance <= deadzoneMax)
        {
            // In deadzone - no change
            _targetSpread = 0f;
        }
        else if (distance < deadzoneMin)
        {
            // Hands closer than neutral - contract swarm (negative rate)
            float range = deadzoneMin - minDistance;
            float normalizedDist = (distance - minDistance) / range;
            float rate = (normalizedDist - 1f); // Results in -1 to 0
            _targetSpread = Mathf.Clamp(rate, -1f, 0f);
        }
        else // distance > deadzoneMax
        {
            // Hands farther than neutral - expand swarm (positive rate)
            float range = maxDistance - deadzoneMax;
            float normalizedDist = (distance - deadzoneMax) / range;
            _targetSpread = Mathf.Clamp(normalizedDist, 0f, 1f);
        }

        if (showDebugInfo && Mathf.Abs(_targetSpread) > 0.01f)
        {
            Debug.Log($"[HandTrackingRateBased] Distance: {distance:F3}m | Rate: {_targetSpread:F2}");
        }
    }

    protected override void DrawModeSpecificGUI()
    {
        GUILayout.Label($"Neutral: {neutralDistance:F2}m ± {deadzone:F2}m");
        GUILayout.Label($"Spread Rate: {HandSpreadControl:F2}");
        
        // Visual indicator
        if (BothHandsTracked)
        {
            string indicator = HandSpreadControl < -0.1f ? "<<< CONTRACTING" :
                              HandSpreadControl > 0.1f ? "EXPANDING >>>" :
                              "= NEUTRAL =";
            GUILayout.Label($"<b>{indicator}</b>");
        }
    }
}
