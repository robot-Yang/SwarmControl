using UnityEngine;

/// <summary>
/// Base class for all hand tracking input modes.
/// Provides shared functionality for reading Meta Quest hand positions.
/// </summary>
public abstract class HandTrackingInputBase : MonoBehaviour
{
    [Header("Hand References")]
    [Tooltip("Left hand tracker from OVRCameraRig/TrackingSpace/LeftHandAnchor")]
    public OVRHand leftHand;

    [Tooltip("Right hand tracker from OVRCameraRig/TrackingSpace/RightHandAnchor")]
    public OVRHand rightHand;

    [Header("Hand Distance Range")]
    [Tooltip("Minimum hand distance (meters)")]
    [Range(0.05f, 0.3f)]
    public float minDistance = 0.1f;

    [Tooltip("Maximum hand distance (meters)")]
    [Range(0.5f, 1.5f)]
    public float maxDistance = 0.8f;

    [Header("Filtering")]
    [Tooltip("Only use tracking when confidence is high (more stable, may lose control briefly)")]
    public bool requireHighConfidence = true;

    [Tooltip("Smoothing speed for spread changes (higher = faster response)")]
    [Range(1f, 20f)]
    public float smoothingSpeed = 6f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (Read by InputFusionManager)
    // ============================================

    /// <summary>
    /// Output spread control value. Meaning depends on implementation:
    /// - RateBased: Rate control (-1 to +1)
    /// - Absolute/Hybrid: Target separation distance (meters)
    /// </summary>
    public float HandSpreadControl { get; protected set; }

    /// <summary>
    /// Current distance between hands (meters)
    /// </summary>
    public float CurrentHandDistance { get; protected set; }

    /// <summary>
    /// Are both hands being tracked with sufficient confidence?
    /// </summary>
    public bool BothHandsTracked { get; protected set; }

    /// <summary>
    /// Returns true if this mode outputs absolute values (not rates)
    /// </summary>
    public abstract bool IsAbsoluteMode { get; }

    // ============================================
    // PROTECTED STATE
    // ============================================

    protected float _targetSpread = 0f;
    protected float _lastValidDistance = 0f;
    protected bool _initialized = false;

    // ============================================
    // INITIALIZATION
    // ============================================

    protected virtual void Start()
    {
        ValidateReferences();
        _lastValidDistance = (minDistance + maxDistance) / 2f;
    }

    protected void ValidateReferences()
    {
        if (leftHand == null)
        {
            Debug.LogWarning($"{GetType().Name}: Left hand reference is missing! Assign OVRHand from LeftHandAnchor.");
        }

        if (rightHand == null)
        {
            Debug.LogWarning($"{GetType().Name}: Right hand reference is missing! Assign OVRHand from RightHandAnchor.");
        }

        if (leftHand != null && rightHand != null)
        {
            Debug.Log($"{GetType().Name}: Hand tracking initialized successfully.");
        }
    }

    // ============================================
    // UPDATE LOOP
    // ============================================

    protected virtual void Update()
    {
        if (!AreHandsAvailable())
        {
            // Maintain last value when hands not tracked (don't snap to 0)
            // This prevents sudden spread changes when tracking is lost briefly
        }
        else
        {
            UpdateHandDistance();
            CalculateSpreadControl();
        }

        // Apply smoothing
        HandSpreadControl = Mathf.Lerp(HandSpreadControl, _targetSpread, Time.deltaTime * smoothingSpeed);
    }

    /// <summary>
    /// Check if hand tracking is available and meets confidence requirements
    /// </summary>
    protected bool AreHandsAvailable()
    {
        if (leftHand == null || rightHand == null)
        {
            BothHandsTracked = false;
            return false;
        }

        // Check if hands are tracked
        bool leftTracked = leftHand.IsTracked;
        bool rightTracked = rightHand.IsTracked;

        if (!leftTracked || !rightTracked)
        {
            BothHandsTracked = false;
            return false;
        }

        // Check confidence if required
        if (requireHighConfidence)
        {
            bool leftHighConfidence = leftHand.IsTracked && leftHand.HandConfidence == OVRHand.TrackingConfidence.High;
            bool rightHighConfidence = rightHand.IsTracked && rightHand.HandConfidence == OVRHand.TrackingConfidence.High;

            BothHandsTracked = leftHighConfidence && rightHighConfidence;
            return BothHandsTracked;
        }

        BothHandsTracked = true;
        return true;
    }

    /// <summary>
    /// Calculate distance between hands
    /// </summary>
    protected void UpdateHandDistance()
    {
        Vector3 leftPos = leftHand.transform.position;
        Vector3 rightPos = rightHand.transform.position;

        CurrentHandDistance = Vector3.Distance(leftPos, rightPos);
        _lastValidDistance = CurrentHandDistance;

        if (!_initialized)
        {
            _initialized = true;
        }
    }

    /// <summary>
    /// Calculate spread control value - implemented by each mode
    /// </summary>
    protected abstract void CalculateSpreadControl();

    // ============================================
    // HELPER METHODS
    // ============================================

    /// <summary>
    /// Returns true if hands are actively controlling spread
    /// </summary>
    public virtual bool IsControllingSpread()
    {
        return Mathf.Abs(HandSpreadControl) > 0.01f;
    }

    /// <summary>
    /// Get raw hand distance without any processing
    /// </summary>
    public float GetRawHandDistance()
    {
        if (leftHand == null || rightHand == null) return 0f;
        return Vector3.Distance(leftHand.transform.position, rightHand.transform.position);
    }

    /// <summary>
    /// Reset spread control to neutral
    /// </summary>
    public virtual void ResetSpread()
    {
        HandSpreadControl = 0f;
        _targetSpread = 0f;
    }

    // ============================================
    // DEBUG VISUALIZATION
    // ============================================

    protected virtual void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        GUILayout.BeginArea(new Rect(10, 380, 300, 200));
        GUILayout.Label($"<b>{GetType().Name}</b>");
        GUILayout.Label($"Both Hands Tracked: {BothHandsTracked}");
        GUILayout.Label($"Hand Distance: {CurrentHandDistance:F3}m");
        
        DrawModeSpecificGUI();
        
        GUILayout.Label($"Is Controlling: {IsControllingSpread()}");
        GUILayout.EndArea();
    }

    /// <summary>
    /// Override this to add mode-specific debug UI
    /// </summary>
    protected abstract void DrawModeSpecificGUI();

    protected virtual void OnDrawGizmos()
    {
        if (!showDebugInfo || !Application.isPlaying) return;
        if (leftHand == null || rightHand == null) return;
        if (!BothHandsTracked) return;

        // Draw line between hands
        Gizmos.color = IsControllingSpread() ? Color.green : Color.yellow;
        Gizmos.DrawLine(leftHand.transform.position, rightHand.transform.position);

        // Draw spheres at hand positions
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(leftHand.transform.position, 0.05f);
        Gizmos.DrawWireSphere(rightHand.transform.position, 0.05f);
    }
}
