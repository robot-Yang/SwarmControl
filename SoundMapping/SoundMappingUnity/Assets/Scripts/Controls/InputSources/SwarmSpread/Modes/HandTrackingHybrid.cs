using UnityEngine;

/// <summary>
/// Hybrid hand tracking control.
/// Hand distance sets TARGET swarm separation, but changes are rate-limited (like a thermostat).
/// Best of both: direct correspondence + smooth transitions, no sudden jumps from tracking glitches.
/// </summary>
public class HandTrackingHybrid : HandTrackingInputBase
{
    [Header("Hybrid Settings")]
    [Tooltip("Minimum swarm separation (meters)")]
    [Range(0.5f, 2.0f)]
    public float minSwarmSeparation = 1.0f;

    [Tooltip("Maximum swarm separation (meters)")]
    [Range(3.0f, 10.0f)]
    public float maxSwarmSeparation = 5.0f;

    [Tooltip("Maximum rate of swarm spread change (m/s)")]
    [Range(0.5f, 5.0f)]
    public float maxChangeRate = 2.0f;

    public override bool IsAbsoluteMode => true;

    /// <summary>
    /// Hybrid mapping: calculates target separation like absolute mode
    /// Rate limiting happens in InputFusionManager or MigrationPointController
    /// </summary>
    protected override void CalculateSpreadControl()
    {
        float distance = CurrentHandDistance;

        // Map hand distance to target swarm separation (same as absolute)
        float t = (distance - minDistance) / (maxDistance - minDistance);
        t = Mathf.Clamp01(t);
        _targetSpread = Mathf.Lerp(minSwarmSeparation, maxSwarmSeparation, t);

        if (showDebugInfo)
        {
            Debug.Log($"[HandTrackingHybrid] Distance: {distance:F3}m | Target: {_targetSpread:F2}m (rate-limited)");
        }
    }

    protected override void DrawModeSpecificGUI()
    {
        GUILayout.Label($"Range: {minSwarmSeparation:F1}m - {maxSwarmSeparation:F1}m");
        GUILayout.Label($"Target Separation: {HandSpreadControl:F2}m");
        GUILayout.Label($"Max Change Rate: {maxChangeRate:F1}m/s");
    }
}
