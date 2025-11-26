using UnityEngine;

/// <summary>
/// Exponential hand tracking control.
/// More precision at smaller spreads, less at larger spreads.
/// Good for coarse-to-fine control - careful positioning when tight, quick spreading when wide.
/// </summary>
public class HandTrackingExponential : HandTrackingInputBase
{
    [Header("Exponential Mapping Settings")]
    [Tooltip("Minimum swarm separation (meters)")]
    [Range(0.5f, 2.0f)]
    public float minSwarmSeparation = 1.0f;

    [Tooltip("Maximum swarm separation (meters)")]
    [Range(3.0f, 20.0f)]
    public float maxSwarmSeparation = 10.0f;

    [Tooltip("Exponential power (higher = more aggressive curve, more precision at small spreads)")]
    [Range(1.5f, 4.0f)]
    public float exponent = 2.0f;

    public override bool IsAbsoluteMode => true;

    /// <summary>
    /// Exponential mapping: apply power function for non-linear curve
    /// Small hand movements at close range = small swarm changes (high precision)
    /// Large hand movements at far range = large swarm changes (quick spreading)
    /// </summary>
    protected override void CalculateSpreadControl()
    {
        float distance = CurrentHandDistance;

        // Normalize hand distance to 0-1
        float t = (distance - minDistance) / (maxDistance - minDistance);
        t = Mathf.Clamp01(t);

        // Apply exponential curve (x^2, x^3, etc.)
        float curved = Mathf.Pow(t, exponent);
        
        _targetSpread = Mathf.Lerp(minSwarmSeparation, maxSwarmSeparation, curved);

        if (showDebugInfo)
        {
            Debug.Log($"[HandTrackingExp] Distance: {distance:F3}m | t: {t:F2} | curved: {curved:F2} | Target: {_targetSpread:F2}m");
        }
    }

    protected override void DrawModeSpecificGUI()
    {
        GUILayout.Label($"Range: {minSwarmSeparation:F1}m - {maxSwarmSeparation:F1}m (Exp^{exponent:F1})");
        GUILayout.Label($"Target Separation: {HandSpreadControl:F2}m");
        GUILayout.Label($"<i>High precision at small spreads</i>");
    }
}
