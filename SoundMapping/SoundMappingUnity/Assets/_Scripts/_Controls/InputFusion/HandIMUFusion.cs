using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Sensor fusion: combines ArmIMUSpreadHeightInput (drift-prone, but works out of FOV)
/// with HandHeightInput / HandSpreadInput (drift-free, but FOV-limited) into a single
/// corrected stream. While hand tracking is available, gently pulls the IMU output
/// toward the hand-tracking rate; when hand tracking drops out, the IMU passes through
/// unchanged. Bounded drift: only accumulates during out-of-FOV periods.
///
/// Operates at the rate level — both ArmIMU.spreadMode and the hand inputs must be
/// rate-based for the blend to be meaningful (mixing rate with absolute meters
/// produces garbage). A warning fires at Start if ArmIMU is in absolute mode.
///
/// Wired into InputFusionManager as a drop-in replacement for armIMUInput when
/// assigned: the manager reads handIMUFusion.HeightControl / .SpreadControl instead
/// of the raw IMU values. Leave the field empty on InputFusionManager to bypass
/// fusion entirely (raw IMU passthrough).
/// </summary>
public class HandIMUFusion : MonoBehaviour
{
    [Header("Sensor Sources")]
    [Tooltip("Forearm IMU input — the drift-prone source we're correcting")]
    [FormerlySerializedAs("armIMUInput")]
    public ArmIMUSpreadHeightInput armIMU;

    [Tooltip("Meta Quest hand tracking — drift-free reference for height (when in FOV with high confidence)")]
    public HandHeightInput handHeightInput;

    [Tooltip("Meta Quest hand tracking — drift-free reference for spread (when in FOV with high confidence)")]
    public HandSpreadInput handSpreadInput;

    [Header("Correction Strength")]
    [Tooltip("How strongly hand tracking pulls the IMU rate toward its own (per second). 0 = pure passthrough (IMU only). Higher = snappier correction but can fight intentional gestures. Recommended: 0.05–0.2.")]
    [Range(0f, 1f)]
    [FormerlySerializedAs("questCorrectionStrength")]
    public float correctionStrength = 0.1f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ============================================
    // PUBLIC OUTPUTS (read by InputFusionManager)
    // ============================================

    /// <summary>Fused height rate (-1..+1).</summary>
    public float HeightControl { get; private set; }

    /// <summary>Fused spread rate (-1..+1).</summary>
    public float SpreadControl { get; private set; }

    /// <summary>Always false — fusion produces a rate.</summary>
    public bool IsAbsoluteMode => false;

    /// <summary>True when the underlying ArmIMU is available.</summary>
    public bool IsAvailable => armIMU != null && armIMU.IsAvailable;

    // ============================================
    // RUNTIME
    // ============================================

    void Start()
    {
        if (armIMU != null && armIMU.spreadMode != SpreadControlMode.RateBased)
        {
            Debug.LogWarning(
                "HandIMUFusion: ArmIMU.spreadMode is not RateBased. " +
                "HandSpreadInput is rate-only — fusing rate (hand) with meters (IMU absolute) " +
                "produces garbage. Set armIMU.spreadMode = RateBased.");
        }
    }

    void Update()
    {
        if (!IsAvailable)
        {
            HeightControl = 0f;
            SpreadControl = 0f;
            return;
        }

        float height = armIMU.HeightControl;
        float spread = armIMU.SpreadControl;

        float blend = correctionStrength * Time.deltaTime;

        // HandHeightInput / HandSpreadInput already gate IsAvailable on IsTracked + confidence
        // per their own settings — we just trust their availability flag here.
        if (handHeightInput != null && handHeightInput.IsAvailable)
            height = Mathf.Lerp(height, handHeightInput.HeightControl, blend);

        if (handSpreadInput != null && handSpreadInput.IsAvailable)
            spread = Mathf.Lerp(spread, handSpreadInput.SpreadControl, blend);

        HeightControl = height;
        SpreadControl = spread;
    }

    /// <summary>Forwards calibration to the underlying ArmIMU. Hand tracking sources calibrate via their own paths in InputFusionManager.PerformCalibration().</summary>
    public void CalibrateNeutral()
    {
        if (armIMU != null) armIMU.CalibrateNeutral();
    }

    void OnGUI()
    {
        if (!showDebugInfo || !Application.isPlaying || !IsAvailable) return;
        GUILayout.BeginArea(new Rect(10, 750, 380, 130));
        GUILayout.Label("<b>=== HAND-IMU FUSION ===</b>");

        bool handHeightOn = handHeightInput != null && handHeightInput.IsAvailable;
        bool handSpreadOn = handSpreadInput != null && handSpreadInput.IsAvailable;

        GUILayout.Label($"Hand height active: {(handHeightOn ? "<color=lime>YES</color>" : "<color=grey>no</color>")}");
        GUILayout.Label($"Hand spread active: {(handSpreadOn ? "<color=lime>YES</color>" : "<color=grey>no</color>")}");
        GUILayout.Label($"Height: armIMU={armIMU.HeightControl:F2} → fused={HeightControl:F2}");
        GUILayout.Label($"Spread: armIMU={armIMU.SpreadControl:F2} → fused={SpreadControl:F2}");
        GUILayout.EndArea();
    }
}
