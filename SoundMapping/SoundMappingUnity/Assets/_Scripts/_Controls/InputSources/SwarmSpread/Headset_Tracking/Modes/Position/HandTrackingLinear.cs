using UnityEngine;

/// <summary>
/// Absolute hand tracking control.
/// Hand distance DIRECTLY sets swarm separation (like a slider).
/// Instant response - hands 20cm apart = swarm 2m separation (immediately).
/// </summary>
public class HandTrackingLinear : HandTrackingInputBase
{
    [Header("Absolute Mapping Settings")]
    [Tooltip("Minimum swarm separation (meters)")]
    [Range(0.5f, 2.0f)]
    public float minSwarmSeparation = 1.0f;

    [Tooltip("Maximum swarm separation (meters)")]
    [Range(3.0f, 10.0f)]
    public float maxSwarmSeparation = 5.0f;

    public override bool IsAbsoluteMode => true;

    /// <summary>
    /// Absolute mapping: hand distance directly maps to swarm separation
    /// Works like a slider - instant correspondence
    /// </summary>
    protected override void CalculateSpreadControl()
    {
        float distance = CurrentHandDistance;

        // Map hand distance to swarm separation range
        float t = (distance - minDistance) / (maxDistance - minDistance);
        t = Mathf.Clamp01(t);
        _targetSpread = Mathf.Lerp(minSwarmSeparation, maxSwarmSeparation, t);

        if (showDebugInfo)
        {
            Debug.Log($"[HandTrackingAbsolute] Distance: {distance:F3}m | Target: {_targetSpread:F2}m");
        }
    }

    protected override void DrawModeSpecificGUI()
    {
        GUILayout.Label($"Range: {minSwarmSeparation:F1}m - {maxSwarmSeparation:F1}m");
        GUILayout.Label($"Target Separation: {HandSpreadControl:F2}m");
    }
}
