using UnityEngine;

/// <summary>
/// Logarithmic hand tracking control.
/// Good for covering HUGE dynamic ranges (e.g., 1m to 50m swarm spread).
/// More precision at larger spreads, less at smaller spreads.
/// </summary>
public class HandTrackingLogarithmic : HandTrackingInputBase
{
    [Header("Logarithmic Mapping Settings")]
    [Tooltip("Minimum swarm separation (meters)")]
    [Range(0.5f, 2.0f)]
    public float minSwarmSeparation = 1.0f;

    [Tooltip("Maximum swarm separation (meters) - can be much larger than linear modes")]
    [Range(5.0f, 100.0f)]
    public float maxSwarmSeparation = 50.0f;

    public override bool IsAbsoluteMode => true;

    /// <summary>
    /// Logarithmic mapping: interpolate in log space for exponential output
    /// Small hand movements at close range = large swarm changes (quick setup)
    /// Large hand movements at far range = small swarm changes (high precision at extremes)
    /// </summary>
    protected override void CalculateSpreadControl()
    {
        float distance = CurrentHandDistance;

        // Normalize hand distance to 0-1
        float t = (distance - minDistance) / (maxDistance - minDistance);
        t = Mathf.Clamp01(t);

        // Interpolate in logarithmic space
        // This gives exponential output: small changes in t = large changes in output
        float logMin = Mathf.Log(minSwarmSeparation);
        float logMax = Mathf.Log(maxSwarmSeparation);
        float logValue = Mathf.Lerp(logMin, logMax, t);
        
        _targetSpread = Mathf.Exp(logValue);

        if (showDebugInfo)
        {
            Debug.Log($"[HandTrackingLog] Distance: {distance:F3}m | t: {t:F2} | Target: {_targetSpread:F2}m");
        }
    }

    protected override void DrawModeSpecificGUI()
    {
        GUILayout.Label($"Range: {minSwarmSeparation:F1}m - {maxSwarmSeparation:F0}m (Log)");
        GUILayout.Label($"Target Separation: {HandSpreadControl:F2}m");
        GUILayout.Label($"<i>Covers huge range</i>");
    }
}
